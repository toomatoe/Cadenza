# ApurvaSpotify

A native Windows music application focused on a clean listening experience, predictable controls, and low resource use.

## Status

Repository initialized. Application code, playback integration, and Windows build verification are not implemented yet. This repository is not currently a runnable Spotify client.

## Technical direction

| Layer | Technology | Responsibility |
| --- | --- | --- |
| Desktop interface | C# and WinUI 3 | Navigation, library views, search, queue, settings |
| Public API access | C# HttpClient with async/await | Spotify search, playlists, and permitted library operations |
| Playback engine | Rust | Session, buffering, decoding, audio output |
| Native boundary | C ABI DLL and .NET P/Invoke | Commands, request IDs, completion events, engine lifetime |
| Local storage | SQLite | Settings and user-created organization data |

The application will not use Electron, a browser interface, or a local web server. Opening the system browser for Spotify authorization does not make the desktop interface a web application.

## Concurrency design

Network calls for the public API stay asynchronous in C#. Playback commands cross a small native boundary and enter bounded Rust worker queues. Rust reports completion and playback events; C# dispatches interface updates onto the UI thread. Rust futures and C# Tasks do not cross the ABI directly. Blocking library calls run on workers, never on the UI thread or real-time audio callback.

The bridge must define cancellation, queue backpressure, buffer ownership, error handling, callback lifetime, and graceful shutdown. Native audio samples stay inside Rust.

## Spotify integration

Spotify Premium is required for the proposed playback integration. Premium alone does not grant a Developer Client ID or permission to distribute a replacement client.

The public Web API provides metadata and playback controls, not native audio streaming. Standalone playback would require an unofficial protocol implementation such as the approach used by Psst/librespot, or a supported agreement with Spotify. Compatibility and permissions must be evaluated before distribution.

Public API authorization will use Authorization Code with PKCE. Do not commit client secrets, access tokens, refresh tokens, credentials, or account data. Tokens should be protected using Windows credential storage and deleted on disconnect.

References:
- [Spotify developer documentation](https://developer.spotify.com/documentation/web-api)
- [Spotify developer policy](https://developer.spotify.com/policy)
- [Psst](https://github.com/jpochyla/psst)
- [librespot](https://github.com/librespot-org/librespot)

## First implementation milestones

1. Native window, navigation, accessible controls, and empty/error states.
2. PKCE sign-in, secure token storage, async search, and library pagination.
3. Rust DLL with a tested command/event contract and deterministic shutdown.
4. Validate standalone playback on Windows with a Premium account.
5. Queue editing, keyboard navigation, compact player, and user-created tags.
6. Measure memory, startup time, idle CPU, and sustained playback stability.

Recommendation experiments must use permitted data and comply with Spotify policy. Do not ingest Spotify content into ML/AI models or assume access to restricted audio-feature or recommendation endpoints.

## Performance principles

- Virtualize visible rows and page library data.
- Bound image caches and decode artwork at display size.
- Debounce searches and cancel stale requests.
- Limit background refreshes and stop unnecessary work when minimized.
- Keep playback isolated from UI stalls.
- Compare total process-tree resource use against Spotify under the same workload; no performance improvement is claimed before measurement.
