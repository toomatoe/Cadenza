# Cadenza

Cadenza is a personal music app for Windows, built with C#/WinUI 3 and Rust. Browse your Spotify library, make a queue, and shuffle with a little space between tracks by the same artist. The interface runs as a native Windows app.

## What works so far

Here?s what?s in the app:

- Native desktop navigation with Acrylic, Mica, and Solid backgrounds, restrained surface fills, artwork cards for the library and playlists, playlist cover headers, and a content-sized playback dock.
- Spotify Authorization Code with PKCE using the system browser and a temporary loopback callback listener.
- Windows Credential Manager token storage, serialized refresh, and disconnect cleanup.
- Asynchronous Spotify requests with paging, canceled stale searches, bounded retries, and rate-limit handling.
- In-memory queue editing, undo, and an explicit artist-spacing shuffle rule.
- Rust DLL boundary with bounded queues, request IDs, completion events, shutdown, and cross-language tests.
- Experimental librespot 0.8 session and Windows Rodio audio output, with native player events, play/pause, seek, volume, stop, and queue progression.
- Windows build/package workflow and PowerShell build script.

**Native playback is experimental and not yet verified with a live account.** The Windows build includes librespot; the default Rust build remains a bridge-only build. Player events drive status rather than command acceptance. Compilation and mocked API tests do not verify live sign-in, audible streaming, or lower memory usage. A Web API app's token may be rejected by Spotify's internal playback services; a successful metadata login does not guarantee streaming access.

## Build on Windows

Prerequisites: Windows 10 build 19041 or newer (Windows 11 recommended), .NET 10 SDK, Rust stable with the MSVC toolchain, and Visual Studio Build Tools with Desktop development with C++ plus a Windows SDK. Windows PowerShell 5.1 or PowerShell 7 works. Reopen PowerShell after installing prerequisites and confirm `dotnet --version` and `cargo --version` work.

```powershell
git clone https://github.com/toomatoe/Cadenza.git
cd Cadenza
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Publish
& .\artifacts\windows-x64\Cadenza.Desktop.exe
```

To build and launch the latest app after installing the prerequisites:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\run.ps1
```

`run.ps1` builds and opens the Debug version by default. It finds the output through MSBuild and closes any running copy of that same build before updating it. Add `-Configuration Release` for Release, or `-NoRestore` when your packages are already restored.

### A reminder for VS Code

> **Use the updated launch configurations:** open Run and Debug and choose **Cadenza Desktop (Debug x64)** for everyday development. Choose **Cadenza Desktop (Release x64)** when you want to check Release. Building Release does not update the Debug executable, and vice versa.

- **F5** saves your open files, rebuilds the selected configuration (including native audio), then starts that exact executable under the debugger.
- **Ctrl+F5** uses the same configuration and build step, without attaching the debugger.
- **Ctrl+Shift+B** rebuilds Debug by default. To build Release, use **Tasks: Run Build Task** and choose **build Cadenza Desktop (Release x64)**.

The build step closes any running copy of the matching executable so its files can be replaced. If the build fails, VS Code stops there instead of opening an old copy. These settings live in `.vscode/launch.json`, `.vscode/tasks.json`, and `.vscode/settings.json`; the tasks call `run.ps1 -BuildOnly -Rebuild`.

If you prefer the terminal, this builds the same Debug output used by F5:

```powershell
dotnet build desktop/Cadenza.Desktop.csproj -c Debug -p:Platform=x64
```

Close the running app first when building directly with `dotnet`. To handle that and launch the new build in one command, use `run.ps1` above.

### Just building the interface

If you?re working on the UI and don?t need to compile the audio engine:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -UiOnly
& .\desktop\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64\Cadenza.Desktop.exe
```

If dependencies have already been restored, add `-NoRestore` to avoid contacting NuGet again. A first build needs network access for package restoration.

Normal desktop builds compile the Rust playback engine and copy its DLL beside the executable. The project finds Rust in its standard user install location even when the IDE has an older PATH. Use `run.ps1 -UiOnly` or `build.ps1 -UiOnly` to explicitly build the interface without compiling native audio. Publishing requires the DLL; `build.ps1 -Publish` builds and tests it with playback enabled before packaging. If Windows reports a missing audio DLL, rebuild with the command above and launch the executable in `artifacts/windows-x64`. If the DLL is present but a dependency cannot load, install the Microsoft Visual C++ x64 runtime.

Keep the whole publish folder together?the executable needs the files beside it. Windows CI also produces a zip when the build and checks pass, though it cannot confirm live Spotify playback.

## Appearance

Open **Settings > Appearance** to choose Acrylic, Mica, or Solid. Acrylic is the default: it shows a live, blurred view of what?s behind the window, with a lighter tint so more of your desktop comes through. If you previously chose Wallpaper, that selection now uses Acrylic. Mica picks up your Windows wallpaper?s colors, while Solid gives you a plain background. Cadenza respects Windows transparency and high-contrast settings and falls back to Solid when effects are unavailable. Text and artwork stay fully opaque.

See [native application structure](docs/ARCHITECTURE.md) for the model, controller, service, and view boundaries.

## Connect your account

1. Create a Web API app at [Spotify's developer dashboard](https://developer.spotify.com/dashboard).
2. Register your chosen redirect URI (`http://127.0.0.1:8888/callback` is only the default) and allow your Spotify account as a development user where required.
3. Open Settings in Cadenza and enter the app's Client ID and the same redirect URL, then select Connect Spotify.
4. Complete Spotify's consent screen in your system browser. No client secret is used. Reconnect after updating from the foundation build to grant the added `streaming` scope.
5. Select Play on a track. The first time, your browser asks you to approve playback once: Spotify's streaming servers reject tokens issued to developer apps, so playback signs in separately with the desktop client ID librespot uses (Spotify Premium required). Playback connects on demand; Pause, Next, Stop, volume, and seeking are in the bottom bar. Next consumes the first queued track. Queue progression follows native end-of-track events.

Premium alone does not provide a developer Client ID. Endpoint availability depends on your application's current Spotify access. Forbidden endpoints show an actionable error. Your chosen port must be available during sign-in; the listener is stopped afterward. Sign-in times out after three minutes.

Tokens are stored in Windows Credential Manager under `Cadenza/<Client ID>`. Disconnect removes the saved sign-in and clears session data and queue history. Preferences save your Client ID, redirect URL, artist spacing, and appearance choice. Updating from the old project name requires reconnecting because credentials and preferences now use Cadenza. Queue contents are not saved to disk. Remove app access in your Spotify account settings if you also want to revoke authorization at Spotify.

The redirect URL can be chosen freely within [Spotify's redirect URI rules](https://developer.spotify.com/documentation/web-api/concepts/redirect_uri) and must match the registered URL. Spotify requires HTTPS except for explicit loopback IP addresses, and does not allow `localhost`. Cadenza currently receives callbacks through HTTP on `127.0.0.1`; choose any available port and path, for example `http://127.0.0.1:9000/signin`. A remote URL requires a different callback mechanism.

## Project layout

| Directory | Purpose |
| --- | --- |
| `desktop/Views/` | WinUI window, XAML, and native view code |
| `desktop/Models/` | Desktop preference values |
| `desktop/Services/` | Windows credential storage and preference persistence |
| `desktop/Converters/` | XAML artwork conversion |
| `core/Models/` | Track, playlist, paging, token, queue, and playback models |
| `core/Controllers/` | Catalog request coordination and model updates |
| `core/Services/` | Spotify services, playback bridge, and service contracts |
| `core/Security/`, `core/Exceptions/` | PKCE helper and service exceptions |
| `native/` | Rust DLL and command/event contract |
| `tests/` | Small entry point, with Suites, Fixtures, and Infrastructure folders |

Preferences live in a small JSON file for now. We?ll consider SQLite when the app has more data to keep between sessions. Spotify Web API data stays in C#; audio/session work belongs in Rust. Async functions do not cross the ABI: commands return promptly and C# Tasks complete when events arrive.

## Validation

```powershell
cargo test --locked --features playback --manifest-path native/Cargo.toml
dotnet run --project tests/Cadenza.Tests.csproj -c Release
# Build the DLL and expose it to the native checks:
cargo build --locked --release --features playback --manifest-path native/Cargo.toml
$env:PATH = (Join-Path $PWD "native\target\release") + ";" + $env:PATH
dotnet run --project tests/Cadenza.Tests.csproj -c Release -- --native
```

The Windows script handles DLL placement. On Linux, set `LD_LIBRARY_PATH` to `native/target/release` for the native checks. The Windows interface cannot be run on Linux.

## Playback validation still required

[librespot](https://github.com/librespot-org/librespot) is pinned to 0.8.0, with its compatible `vergen` 9.0.6 build dependency pinned and Cargo.lock committed. Windows selects Rodio explicitly; another platform never silently substitutes a pipe or null output. Persistent credential and audio caches are disabled in the Rust session. Librespot uses temporary files for stream buffering. The app disconnects and destroys that session before removing saved credentials.

On a Windows machine, verify launch, browser sign-in, audible playback of several tracks, pause/resume, seek, volume, next, natural queue progression, and disconnect during loading. Also test a missing output device and an expired/rejected token. These checks need an authorized Premium account and are not performed by CI. Internal protocols can change without a supported compatibility contract. Spotify's public Web API supplies metadata and controls rather than native audio streams.

Spotify's [developer policy](https://developer.spotify.com/policy) restricts replacement experiences and ML/AI ingestion of Spotify content. This project does not currently train models or analyze audio. The shuffle rule is an explicit queue-ordering preference, not a learned recommendation system.

We want Cadenza to use less memory, but we haven?t measured that yet. Compare full process-tree private memory, idle CPU, startup time, and prolonged listening under equivalent workloads after playback exists. Artwork uses bounded 360-pixel decoding and a record placeholder when covers are unavailable; lists and artwork shelves virtualize their items, API data is paged, and the queue is capped at 500 tracks.
