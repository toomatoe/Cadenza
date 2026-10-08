# Native bridge contract, ABI 2

The optional `playback` feature adds librespot 0.8 and a Windows Rodio output. The default build is bridge-only. Windows build.ps1 enables playback. Live streaming compatibility is unverified.

| Function | Contract |
| --- | --- |
| `cadenza_abi_version()` | Returns 2. |
| `cadenza_create()` | Nonzero opaque u64 handle, or 0 on failure. |
| `cadenza_submit(handle, request_id, opcode)` | Enqueues a command immediately. |
| `cadenza_submit_text(handle, request_id, opcode, bytes, length)` | Copies UTF-8 synchronously, 1–8192 bytes; pointer is never retained. |
| `cadenza_poll(handle, Event*)` | Writes one event; returns 1 if empty. |
| `cadenza_destroy(handle)` | Removes the handle, stops and joins its worker. Call off the UI thread. |

Request IDs must be nonzero and unique among outstanding requests. Accepted commands plus unconsumed completions are capped at 32. Telemetry occupies one separate coalesced slot; position updates cannot fill the completion queue. The C-layout event is 24 bytes: u64 request ID, u32 kind, i32 status, u32 value, u32 track stamp. Request ID 0 identifies telemetry; the stamp ties it to the load completion's value.

Kinds: 1 diagnostic, 2 command completion, 10 loading, 11 playing, 12 paused, 13 stopped, 14 ended, 15 failed. Values are ABI version for diagnostics, track stamp for load completion, position milliseconds for playback events, otherwise 0.

Statuses: 0 success, 1 empty, 2 invalid, 3 full, 4 unsupported, 5 internal failure, 6 disconnected, 7 authentication failure/timeout, 8 audio output failure. A command completion means accepted by the player; only a player event establishes its playback state.

Opcodes: 0 diagnostic; 1 legacy unsupported check; 2 authenticate (`clientID` + newline + access token); 3 load Spotify track URI; 4 resume; 5 pause; 6 seek (milliseconds); 7 volume (0–100); 8 stop. Opcodes 2/3/6/7 use text; the others use submit. Authentication has a 30-second timeout and checks shutdown every 50 ms. The session runtime uses two Tokio workers; librespot also owns a player runtime with persistent caching disabled. Librespot still uses temporary stream-buffering files. Queue-owned payload bytes are erased on drop; no token logging is installed.

C# owns Tasks; Rust owns sessions and player lifetimes. Cancellation stops a managed wait, not native work; its completion is still drained. Destroy interrupts session connection, stops the player, and closes the runtime. All native work is off the UI thread. Exported functions catch panics; sink creation failures yield errors rather than silent output. Poll output must be aligned and writable; text pointers must remain readable throughout the call.

Tests cover bounded completions, invalid payloads, commands without a session, concurrent managed completions, cancellation, and disposal. They do not validate an actual account, audible output, Windows launch, or memory performance.
