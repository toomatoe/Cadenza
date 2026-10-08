//! Session and player live entirely behind the worker. No password or audio cache.
use super::{Command, Event, INVALID, OK, UNSUPPORTED};
use librespot_core::{
    authentication::Credentials, config::SessionConfig, session::Session, spotify_uri::SpotifyUri,
};
use librespot_playback::{
    config::PlayerConfig,
    mixer::VolumeGetter,
    player::{Player, PlayerEvent},
};
use std::{
    sync::{
        Arc,
        atomic::{AtomicBool, AtomicU32, Ordering},
    },
    time::Duration,
};
use tokio::{runtime::Runtime, sync::mpsc::UnboundedReceiver};
use zeroize::Zeroizing;

const NOT_CONNECTED: i32 = 6;
const AUTH_FAILED: i32 = 7;
const AUDIO_FAILED: i32 = 8;
struct Volume(Arc<AtomicU32>);
impl VolumeGetter for Volume {
    fn attenuation_factor(&self) -> f64 {
        self.0.load(Ordering::Relaxed) as f64 / 100.0
    }
}
pub(super) struct Playback {
    // Drop player and session before the runtime shuts down.
    player: Option<Arc<Player>>,
    session: Option<Session>,
    events: Option<UnboundedReceiver<PlayerEvent>>,
    runtime: Option<Runtime>,
    running: Arc<AtomicBool>,
    volume: Arc<AtomicU32>,
    audio_failed: Arc<AtomicBool>,
    stamp: u32,
    native_request: Option<u64>,
    current: Option<SpotifyUri>,
    last: Event,
    reported_failure: bool,
}
impl Playback {
    pub(super) fn new(running: Arc<AtomicBool>) -> Self {
        Self {
            player: None,
            session: None,
            events: None,
            runtime: None,
            running,
            volume: Arc::new(AtomicU32::new(25)),
            audio_failed: Arc::new(AtomicBool::new(false)),
            stamp: 0,
            native_request: None,
            current: None,
            last: Event::default(),
            reported_failure: false,
        }
    }
    pub(super) fn command(&mut self, command: &Command) -> (i32, u32) {
        let text = std::str::from_utf8(&command.payload).unwrap_or("");
        if command.opcode == 1 {
            return (UNSUPPORTED, 0);
        }
        if command.opcode == 2 {
            return (self.authenticate(text), 0);
        }
        let Some(player) = self.player.as_ref() else {
            return (NOT_CONNECTED, 0);
        };
        if self.session.as_ref().is_none_or(Session::is_invalid) || player.is_invalid() {
            return (NOT_CONNECTED, 0);
        }
        match command.opcode {
            3 => {
                let Ok(uri @ SpotifyUri::Track { .. }) = SpotifyUri::from_uri(text) else {
                    return (INVALID, 0);
                };
                // Discard old events before changing the active track. Event IDs isolate later arrivals.
                if let Some(events) = &mut self.events {
                    while events.try_recv().is_ok() {}
                }
                super::diagnostics::clear();
                self.stamp = self.stamp.wrapping_add(1).max(1);
                self.native_request = None;
                self.current = Some(uri.clone());
                self.last = Event {
                    kind: 10,
                    reserved: self.stamp,
                    ..Event::default()
                };
                player.load(uri, true, 0);
                (OK, self.stamp)
            }
            4 if self.current.is_some() => {
                player.play();
                (OK, 0)
            }
            5 if self.current.is_some() => {
                player.pause();
                (OK, 0)
            }
            6 if self.current.is_some() => match text.parse::<u32>() {
                Ok(position) => {
                    player.seek(position);
                    (OK, 0)
                }
                Err(_) => (INVALID, 0),
            },
            7 => match text.parse::<u32>() {
                Ok(value) if value <= 100 => {
                    self.volume.store(value, Ordering::Relaxed);
                    (OK, 0)
                }
                _ => (INVALID, 0),
            },
            8 => {
                player.stop();
                (OK, 0)
            }
            4..=6 => (NOT_CONNECTED, 0),
            _ => (UNSUPPORTED, 0),
        }
    }
    fn authenticate(&mut self, text: &str) -> i32 {
        let Some((client_id, token)) = text.split_once('\n') else {
            return INVALID;
        };
        if client_id.len() != 32
            || !client_id.bytes().all(|b| b.is_ascii_hexdigit())
            || token.is_empty()
            || token.contains(['\n', '\r', '\0'])
        {
            return INVALID;
        }
        // Only Windows has an audio output in this build. Never fall back to a pipe or null sink.
        if !cfg!(windows) {
            return UNSUPPORTED;
        }
        self.disconnect();
        super::diagnostics::clear();
        let Ok(runtime) = tokio::runtime::Builder::new_multi_thread()
            .worker_threads(2)
            .enable_all()
            .build()
        else {
            return super::INTERNAL;
        };
        let session = {
            let _entered = runtime.enter();
            Session::new(
                SessionConfig {
                    client_id: client_id.into(),
                    ..SessionConfig::default()
                },
                None,
            )
        };
        let token = Zeroizing::new(token.to_owned());
        let credentials = Credentials::with_access_token(token.as_str());
        let running = self.running.clone();
        let connected = runtime.block_on(async {
            let connect = tokio::time::timeout(Duration::from_secs(30), session.connect(credentials, false));
            tokio::pin!(connect);
            loop {
                tokio::select! {
                    result = &mut connect => break matches!(result, Ok(Ok(()))),
                    _ = tokio::time::sleep(Duration::from_millis(50)) => if !running.load(Ordering::Acquire) { break false; }
                }
            }
        });
        if !connected {
            session.shutdown();
            return AUTH_FAILED;
        }
        let Some(builder) = librespot_playback::audio_backend::find(Some("rodio".into())) else {
            session.shutdown();
            return AUDIO_FAILED;
        };
        self.audio_failed.store(false, Ordering::Release);
        let failed = self.audio_failed.clone();
        let _entered = runtime.enter();
        let player = Player::new(
            PlayerConfig {
                normalisation: true,
                position_update_interval: Some(Duration::from_secs(1)),
                ..PlayerConfig::default()
            },
            session.clone(),
            Box::new(Volume(self.volume.clone())),
            move || {
                use std::panic::{AssertUnwindSafe, catch_unwind};
                match catch_unwind(AssertUnwindSafe(|| {
                    builder(None, librespot_playback::config::AudioFormat::F32)
                })) {
                    Ok(sink) => Box::new(CheckedSink {
                        inner: Some(sink),
                        failed,
                    }),
                    Err(_) => {
                        failed.store(true, Ordering::Release);
                        Box::new(CheckedSink {
                            inner: None,
                            failed,
                        })
                    }
                }
            },
        );
        self.events = Some(player.get_player_event_channel());
        self.player = Some(player);
        self.session = Some(session);
        drop(_entered);
        self.runtime = Some(runtime);
        OK
    }
    pub(super) fn poll(&mut self) -> Option<Event> {
        self.events.as_ref()?;
        let mut update = None;
        while let Ok(event) = self.events.as_mut()?.try_recv() {
            if let Some(value) = self.map_event(event) {
                update = Some(value);
            }
        }
        if self.audio_failed.load(Ordering::Acquire)
            || self.player.as_ref().is_some_and(|p| p.is_invalid())
            || self.session.as_ref().is_some_and(Session::is_invalid)
        {
            if !self.reported_failure {
                self.reported_failure = true;
                return Some(Event {
                    kind: 15,
                    status: if self.audio_failed.load(Ordering::Acquire) {
                        AUDIO_FAILED
                    } else {
                        super::diagnostics::reason(NOT_CONNECTED)
                    },
                    reserved: self.stamp,
                    ..Event::default()
                });
            }
            return None;
        }
        update
    }
    fn map_event(&mut self, event: PlayerEvent) -> Option<Event> {
        // Replaying a fully loaded track can skip Loading, but always changes request ID.
        if let PlayerEvent::PlayRequestIdChanged { play_request_id } = event {
            self.native_request = Some(play_request_id);
            return None;
        }
        let (request, uri, kind, position) = match event {
            PlayerEvent::Loading {
                play_request_id,
                track_id,
                position_ms,
            } => (play_request_id, track_id, 10, position_ms),
            PlayerEvent::Playing {
                play_request_id,
                track_id,
                position_ms,
            } => (play_request_id, track_id, 11, position_ms),
            PlayerEvent::Paused {
                play_request_id,
                track_id,
                position_ms,
            } => (play_request_id, track_id, 12, position_ms),
            PlayerEvent::Stopped {
                play_request_id,
                track_id,
            } => (play_request_id, track_id, 13, 0),
            PlayerEvent::EndOfTrack {
                play_request_id,
                track_id,
            } => (play_request_id, track_id, 14, self.last.value),
            PlayerEvent::Unavailable {
                play_request_id,
                track_id,
            } => (play_request_id, track_id, 15, 0),
            PlayerEvent::PositionChanged {
                play_request_id,
                track_id,
                position_ms,
            }
            | PlayerEvent::PositionCorrection {
                play_request_id,
                track_id,
                position_ms,
            }
            | PlayerEvent::Seeked {
                play_request_id,
                track_id,
                position_ms,
            } => (play_request_id, track_id, self.last.kind, position_ms),
            _ => return None,
        };
        if self.current.as_ref() != Some(&uri) {
            return None;
        }
        if self.native_request != Some(request) {
            return None;
        }
        self.last = Event {
            kind,
            status: if kind == 15 { super::diagnostics::reason(super::diagnostics::TRACK_UNAVAILABLE) } else { OK },
            value: position,
            reserved: self.stamp,
            ..Event::default()
        };
        Some(self.last)
    }
    fn disconnect(&mut self) {
        self.events = None;
        if let Some(session) = self.session.take() {
            session.shutdown();
        }
        self.player = None;
        if let Some(runtime) = self.runtime.take() {
            runtime.shutdown_timeout(Duration::from_secs(1));
        }
        self.current = None;
        self.native_request = None;
        self.reported_failure = false;
    }
}
impl Drop for Playback {
    fn drop(&mut self) {
        self.disconnect();
    }
}

// Report output failure; this fallback always returns an error, never silent audio success.
struct CheckedSink {
    inner: Option<Box<dyn librespot_playback::audio_backend::Sink>>,
    failed: Arc<AtomicBool>,
}
impl CheckedSink {
    fn check(
        &self,
        result: librespot_playback::audio_backend::SinkResult<()>,
    ) -> librespot_playback::audio_backend::SinkResult<()> {
        if result.is_err() {
            self.failed.store(true, Ordering::Release);
        }
        result
    }
}
impl librespot_playback::audio_backend::Sink for CheckedSink {
    fn start(&mut self) -> librespot_playback::audio_backend::SinkResult<()> {
        let result = self
            .inner
            .as_mut()
            .ok_or_else(|| {
                librespot_playback::audio_backend::SinkError::NotConnected(
                    "No Windows audio output".into(),
                )
            })
            .and_then(|s| s.start());
        self.check(result)
    }
    fn stop(&mut self) -> librespot_playback::audio_backend::SinkResult<()> {
        let result = self.inner.as_mut().map_or(Ok(()), |s| s.stop());
        self.check(result)
    }
    fn write(
        &mut self,
        packet: librespot_playback::decoder::AudioPacket,
        converter: &mut librespot_playback::convert::Converter,
    ) -> librespot_playback::audio_backend::SinkResult<()> {
        let result = self
            .inner
            .as_mut()
            .ok_or_else(|| {
                librespot_playback::audio_backend::SinkError::NotConnected(
                    "No Windows audio output".into(),
                )
            })
            .and_then(|s| s.write(packet, converter));
        self.check(result)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn repeat_track_without_loading_and_stale_events() {
        let mut player = Playback::new(Arc::new(AtomicBool::new(true)));
        let uri = SpotifyUri::from_uri("spotify:track:4uLU6hMCjMI75M1A2tKUQC").unwrap();
        player.current = Some(uri.clone());
        player.stamp = 2;
        player.map_event(PlayerEvent::PlayRequestIdChanged { play_request_id: 1 });
        assert!(
            player
                .map_event(PlayerEvent::Playing {
                    play_request_id: 0,
                    track_id: uri.clone(),
                    position_ms: 0
                })
                .is_none()
        );
        let event = player
            .map_event(PlayerEvent::Playing {
                play_request_id: 1,
                track_id: uri.clone(),
                position_ms: 0,
            })
            .unwrap();
        assert_eq!((event.kind, event.reserved), (11, 2));
        let end = player
            .map_event(PlayerEvent::EndOfTrack {
                play_request_id: 1,
                track_id: uri,
            })
            .unwrap();
        assert_eq!(end.kind, 14);
    }
    #[test]
    fn unavailable_track_reports_a_failure_code() {
        let mut player = Playback::new(Arc::new(AtomicBool::new(true)));
        let uri = SpotifyUri::from_uri("spotify:track:4uLU6hMCjMI75M1A2tKUQC").unwrap();
        player.current = Some(uri.clone());
        player.stamp = 3;
        player.native_request = Some(1);
        let event = player.map_event(PlayerEvent::Unavailable { play_request_id: 1, track_id: uri }).unwrap();
        assert_eq!(event.kind, 15);
        assert_ne!(event.status, OK);
        assert_eq!(event.reserved, 3);
    }
    #[test]
    fn rejects_commands_without_a_session() {
        let mut player = Playback::new(Arc::new(AtomicBool::new(true)));
        for opcode in 3..=8 {
            assert_eq!(
                player
                    .command(&Command {
                        request_id: 1,
                        opcode,
                        payload: b"1".to_vec()
                    })
                    .0,
                NOT_CONNECTED
            );
        }
        assert_eq!(player.authenticate("malformed"), INVALID);
        assert!(player.poll().is_none());
    }
}
