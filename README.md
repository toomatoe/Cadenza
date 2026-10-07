# ApurvaSpotify

A native Windows music application with a C#/WinUI 3 interface and a Rust command bridge. No Electron, embedded browser interface, or persistent local web server.

## Current build

Implemented in source:
- Native desktop navigation for library, search, playlists, queue, and settings.
- Spotify Authorization Code with PKCE using the system browser and a temporary loopback callback listener.
- Windows Credential Manager token storage, serialized refresh, and disconnect cleanup.
- Asynchronous Spotify requests with paging, canceled stale searches, bounded retries, and rate-limit handling.
- In-memory queue editing, undo, and an explicit artist-spacing shuffle rule.
- Rust DLL boundary with bounded queues, request IDs, completion events, shutdown, and cross-language tests.
- Windows build/package workflow and PowerShell build script.

**Standalone Spotify audio is not implemented.** The Rust worker explicitly rejects playback commands. There is no fake playing state, demo library, or dependency on an official Spotify client for this build's functionality. Track links open Spotify only when clicked. Compilation and mocked API tests do not verify live sign-in, streaming, or lower memory usage.

## Build on Windows

Prerequisites: Windows 10 build 19041 or newer (Windows 11 recommended), .NET 10 SDK, Rust stable with the MSVC toolchain, and Visual Studio Build Tools with Desktop development with C++ plus a Windows SDK. Use PowerShell 7.

```powershell
git clone https://github.com/toomatoe/ApurvaSpotify.git
cd ApurvaSpotify
./build.ps1 -Publish
./artifacts/windows-x64/ApurvaSpotify.Desktop.exe
```

The publish folder is self-contained. Keep its files together; the exe alone is not the application. Windows CI uploads a zip if compilation and checks succeed. CI is a build check, not a playback certification.

## Connect your account

1. Create a Web API app at [Spotify's developer dashboard](https://developer.spotify.com/dashboard).
2. Register `http://127.0.0.1:8888/callback` as the redirect URI and allow your Spotify account as a development user where required.
3. Open Settings in ApurvaSpotify and enter the app's Client ID, then select Connect Spotify.
4. Complete Spotify's consent screen in your system browser. No client secret is used.

Premium alone does not provide a developer Client ID. Endpoint availability depends on your application's current Spotify access. Forbidden endpoints show an actionable error. Port 8888 must be available during sign-in; the listener is stopped afterward. Sign-in times out after three minutes.

Tokens are stored in Windows Credential Manager under `ApurvaSpotify/<Client ID>`. Disconnect removes the saved sign-in and clears session data and queue history. Preferences contain only the Client ID and the user-chosen spacing setting. Queue contents are not saved to disk. Remove app access in your Spotify account settings if you also want to revoke authorization at Spotify.

## Project layout

| Directory | Purpose |
| --- | --- |
| `desktop/` | WinUI interface, Windows credential storage, local preferences |
| `core/` | API, OAuth, queue rules, and managed native bridge |
| `native/` | Rust DLL and command/event contract |
| `tests/` | Executable checks for PKCE, API responses, queue behavior, and DLL interop |

Simple preferences currently use a small JSON file. SQLite is deferred until there is enough persistent application data to justify it. Spotify Web API data stays in C#; audio/session work belongs in Rust. Async functions do not cross the ABI: commands return promptly and C# Tasks complete when events arrive.

## Validation

```sh
cargo test --locked --manifest-path native/Cargo.toml
dotnet run --project tests/ApurvaSpotify.Tests.csproj -c Release
# After building the native library, put its directory on the native library search path:
dotnet run --project tests/ApurvaSpotify.Tests.csproj -c Release -- --native
```

The Windows script handles DLL placement. On Linux, set `LD_LIBRARY_PATH` to `native/target/release` for the native checks. The Windows interface cannot be run on Linux.

## Next playback milestone

Integrate a maintained playback engine such as [librespot](https://github.com/librespot-org/librespot), validate its authorization path, add native audio output and real playback events, then test on Windows with a Premium account. Internal protocols can change without a supported compatibility contract. Spotify's public Web API supplies metadata and controls rather than native audio streams.

Spotify's [developer policy](https://developer.spotify.com/policy) restricts replacement experiences and ML/AI ingestion of Spotify content. This project does not currently train models or analyze audio. The shuffle rule is an explicit queue-ordering preference, not a learned recommendation system.

Memory improvements remain a goal, not a measured claim. Compare full process-tree private memory, idle CPU, startup time, and prolonged listening under equivalent workloads after playback exists. Artwork is not loaded in this milestone; list controls virtualize rows, API data is paged, and the queue is capped at 500 tracks.
