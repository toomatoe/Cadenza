# Native bridge contract, ABI 1

This crate is a tested command/event foundation, not an audio player. There is no Spotify session, audio decoder, or audio device attached yet. Opcode 0 checks the bridge; every playback command completes with status 4 (unsupported). No simulated playback success is emitted.

| Function | Contract |
| --- | --- |
| `apurva_abi_version()` | Returns 1. |
| `apurva_create()` | Returns a nonzero opaque u64 handle, or 0 on failure. |
| `apurva_submit(handle, request_id, opcode)` | Copies a command; returns immediately. Request ID must be nonzero and unique among this client's outstanding requests. |
| `apurva_poll(handle, Event*)` | Writes one event to caller-owned memory; returns 1 if empty. |
| `apurva_destroy(handle)` | Removes the handle, stops and joins its worker. Call off the UI thread. |

Status codes: 0 success; 1 empty; 2 invalid; 3 full; 4 unsupported playback; 5 internal failure. Accepted commands plus unconsumed events are limited to 32, so backpressure cannot cause dropped completions or unlimited memory growth. The event is 24 bytes, C layout: u64 request ID, u32 kind, i32 status, u32 value, u32 reserved. Kind 1 is a diagnostic response; kind 2 is an error. Reserved is zero.

There are no borrowed strings or cross-runtime callbacks. C# owns its Tasks; Rust owns its worker. Cancellation of a C# wait does not cancel native work; its completion is still drained. Disposal rejects new submissions and cancels pending waits. Rust panics are caught at exported boundaries. Poll's output pointer must be valid, aligned, and writable.

Before introducing streaming: evaluate current librespot APIs, session authorization requirements, library lifetime and shutdown behavior, native output device handling, and Spotify's permissions. Add a separate streaming integration test on Windows with authorized Premium credentials. A successful ABI test does not establish streaming compatibility.
