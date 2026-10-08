//! Capture only fixed failure categories; never retain or expose library log text.
use std::sync::atomic::{AtomicI32, Ordering};
use log::{LevelFilter, Log, Metadata, Record};

pub(super) const TRACK_UNAVAILABLE: i32 = 9;
pub(super) const AUDIO_KEY_REJECTED: i32 = 10;
pub(super) const METADATA_DENIED: i32 = 11;
pub(super) const NETWORK_FAILED: i32 = 12;
pub(super) const DECODER_FAILED: i32 = 13;
pub(super) const CLIENT_TOKEN_FAILED: i32 = 14;
static FAILURE: AtomicI32 = AtomicI32::new(0);
static LOGGER: PlaybackLogger = PlaybackLogger;
struct PlaybackLogger;
impl Log for PlaybackLogger {
    fn enabled(&self, metadata: &Metadata) -> bool {
        metadata.level() <= log::Level::Warn && metadata.target().starts_with("librespot")
    }
    fn log(&self, record: &Record) {
        if !self.enabled(record.metadata()) { return; }
        let reason = classify(record.target(), &record.args().to_string());
        if reason != 0 {
            // Preserve the first root failure over downstream decoding/skip errors.
            let _ = FAILURE.compare_exchange(0, reason, Ordering::AcqRel, Ordering::Acquire);
        }
    }
    fn flush(&self) {}
}
pub(super) fn init() {
    if log::set_logger(&LOGGER).is_ok() { log::set_max_level(LevelFilter::Warn); }
}
pub(super) fn clear() { FAILURE.store(0, Ordering::Release); }
pub(super) fn reason(fallback: i32) -> i32 {
    match FAILURE.load(Ordering::Acquire) { 0 => fallback, value => value }
}
fn classify(target: &str, message: &str) -> i32 {
    let text = message.to_ascii_lowercase();
    if !target.starts_with("librespot") { return 0; }
    if text.contains("timeout") || text.contains("timed out") || text.contains("dns error")
        || text.contains("connection refused") || text.contains("connection reset")
        || text.contains("certificate") || text.contains("tls error") { return NETWORK_FAILED; }
    if target.contains("audio_key") && text.contains("audio key") { return AUDIO_KEY_REJECTED; }
    if text.contains("client token") || text.contains("client-token") { return CLIENT_TOKEN_FAILED; }
    if text.contains("403") || text.contains("forbidden") || text.contains("permission denied") { return METADATA_DENIED; }
    if text.contains("decoder error") || text.contains("decoding") || text.contains("unable to read audio file") { return DECODER_FAILED; }
    0
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn classify_root_causes_without_retaining_sensitive_text() {
        assert_eq!(classify("librespot_core::audio_key", "error audio key 0 1"), AUDIO_KEY_REJECTED);
        assert_eq!(classify("librespot_core::audio_key", "Audio key response timeout"), NETWORK_FAILED);
        assert_eq!(classify("librespot_playback::player", "Unable to load audio item: HTTP 403"), METADATA_DENIED);
        assert_eq!(classify("librespot_core::spclient", "Unable to get client token"), CLIENT_TOKEN_FAILED);
        assert_eq!(classify("librespot_playback::player", "Unable to read audio file: Symphonia Decoder Error: end of stream"), DECODER_FAILED);
        assert_eq!(classify("other_library", "HTTP 403"), 0);
        assert_eq!(classify("librespot_playback::player", "Unable to get normalisation data, continuing with defaults."), 0);
    }
}
