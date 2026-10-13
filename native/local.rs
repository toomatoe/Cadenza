//! Plays music files from disk (MP3, FLAC, Ogg Vorbis) with rodio's Symphonia decoder, which
//! librespot already compiles in. Used for tracks from the shared Cadenza library.
use super::{Event, INVALID, OK, UNSUPPORTED};
use rodio::{Decoder, OutputStream, OutputStreamBuilder, Sink};
use std::{fs::File, time::Duration};

const AUDIO_FAILED: i32 = 8;
const LOADING: u32 = 10;
const PLAYING: u32 = 11;
const PAUSED: u32 = 12;
const STOPPED: u32 = 13;
const ENDED: u32 = 14;

pub(super) struct LocalPlayer {
    // The sink must drop before the stream it plays into.
    sink: Sink,
    _stream: OutputStream,
    stamp: u32,
    reported: Option<(u32, u32)>,
    finished: bool,
}

impl LocalPlayer {
    /// Opens `path` and starts playing it. Errors are bridge statuses.
    pub(super) fn open(path: &str, volume: u32, stamp: u32) -> Result<Self, i32> {
        if path.is_empty() || path.contains(['\0', '\n', '\r']) {
            return Err(INVALID);
        }
        let file = File::open(path).map_err(|_| INVALID)?;
        let decoder = Decoder::try_from(file).map_err(|_| UNSUPPORTED)?;
        let mut stream = OutputStreamBuilder::open_default_stream().map_err(|_| AUDIO_FAILED)?;
        stream.log_on_drop(false);
        let sink = Sink::connect_new(stream.mixer());
        sink.set_volume(volume as f32 / 100.0);
        sink.append(decoder);
        Ok(Self {
            sink,
            _stream: stream,
            stamp,
            reported: None,
            finished: false,
        })
    }

    /// Handles resume, pause, seek, volume and stop for the file that is playing.
    pub(super) fn command(&mut self, opcode: u32, text: &str) -> i32 {
        match opcode {
            4 if !self.finished => {
                self.sink.play();
                OK
            }
            5 if !self.finished => {
                self.sink.pause();
                OK
            }
            6 => match text.parse::<u64>() {
                Ok(position) if !self.finished => {
                    match self.sink.try_seek(Duration::from_millis(position)) {
                        Ok(()) => OK,
                        Err(_) => UNSUPPORTED,
                    }
                }
                Ok(_) => OK,
                Err(_) => INVALID,
            },
            7 => match text.parse::<u32>() {
                Ok(value) if value <= 100 => {
                    self.sink.set_volume(value as f32 / 100.0);
                    OK
                }
                _ => INVALID,
            },
            8 => {
                self.sink.stop();
                self.finished = true;
                self.reported = None;
                OK
            }
            4 | 5 => OK,
            _ => UNSUPPORTED,
        }
    }

    /// Telemetry in the same shape as Spotify playback: state changes, plus position about once a second.
    pub(super) fn poll(&mut self) -> Option<Event> {
        let position = u32::try_from(self.sink.get_pos().as_millis()).unwrap_or(u32::MAX);
        let kind = if self.finished {
            if self.reported.is_some_and(|(kind, _)| kind == ENDED) {
                ENDED
            } else {
                STOPPED
            }
        } else if self.sink.empty() {
            self.finished = true;
            ENDED
        } else if self.sink.is_paused() {
            PAUSED
        } else if self.reported.is_none() {
            LOADING
        } else {
            PLAYING
        };
        let second = position / 1000;
        if self.reported == Some((kind, second)) {
            return None;
        }
        self.reported = Some((kind, second));
        Some(Event {
            kind,
            status: OK,
            value: if kind == STOPPED { 0 } else { position },
            reserved: self.stamp,
            ..Event::default()
        })
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn rejects_bad_paths_and_unreadable_files() {
        assert_eq!(LocalPlayer::open("", 50, 1).err(), Some(INVALID));
        assert_eq!(LocalPlayer::open("a\nb", 50, 1).err(), Some(INVALID));
        assert_eq!(
            LocalPlayer::open("Z:\\definitely\\missing.mp3", 50, 1).err(),
            Some(INVALID)
        );
        let text = std::env::temp_dir().join("cadenza-not-audio.mp3");
        std::fs::write(&text, b"this is not audio").unwrap();
        assert_eq!(
            LocalPlayer::open(text.to_str().unwrap(), 50, 1).err(),
            Some(UNSUPPORTED)
        );
        let _ = std::fs::remove_file(text);
    }
}
