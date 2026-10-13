# Native application structure

Cadenza remains C#/WinUI 3 with the existing Rust audio engine. There is no web frontend or new framework dependency.

The catalog follows MVC boundaries:

| Responsibility | Files | Rules |
| --- | --- | --- |
| Domain models | `core/Models/Track.cs`, `Playlist.cs`, `QueueEntry.cs`, `Page.cs`, `Tokens.cs`, `ListeningQueue.cs`, `PlaybackState.cs`, `PlaybackUpdate.cs`, `TrackSource.cs` | Each model has its own file. Track/playlist values are immutable; the queue has bounded undo history and one reusable read-only view. |
| Presentation model | `core/Models/CatalogModel.cs` | Stable observable collections and committed paging state, with a 500-item limit. |
| Catalog controller | `core/Controllers/CatalogController.cs` | Selects the metadata request, checks cancellation before committing, replaces/appends pages, and enforces collection bounds. No WinUI references. |
| Service contracts | `core/Services/Contracts/IMusicCatalog.cs`, `ITokenStore.cs` | Metadata and credential-storage contracts, separated from models and implementations. |
| Spotify services | `core/Services/Spotify/SpotifyApi.cs`, `SpotifyAuth.cs` | HTTP, authentication retries, response parsing, and shared page size. The controller accepts a fake metadata service in tests. |
| Native view | `desktop/Views/MainWindow.xaml` and its partial C# files | Controls, navigation, notifications, keyboard handling, rendering, and UI-thread dispatch. Catalog operations delegate to the controller. |
| Preference models | `desktop/Models/Preferences.cs`, `AppearanceMode.cs` | Preference values only; Wallpaper is the appearance default, while old Solid preferences are retained. |
| Windows services | `desktop/Services/PreferencesStore.cs`, `WindowsTokenStore.cs` | JSON persistence with atomic replacement and Windows Credential Manager access. Persistence does not live in models. |
| Native playback services | `core/Services/Playback/NativeEngine.cs`, `PlaybackErrors.cs`, `native/` | Existing command/event ABI, bounded queues, request correlation, and native audio lifecycle. Playback state and update records live in Models. |
| Supporting types | `core/Security/Pkce.cs`, `core/Exceptions/SpotifyApiException.cs`, `desktop/Converters/ArtworkConverter.cs` | Security helpers, exceptions, and XAML converters are separated from services, models, and views. |

`desktop/App.xaml` and `App.xaml.cs` remain at the project root as the WinUI application entry point. Files in `desktop/Views/` use the `Cadenza.Desktop.Views` namespace. The same folder-to-namespace convention applies to Models, Controllers, Services, Security, Exceptions, and Converters.

`desktop/Views/MainWindow.xaml.cs` contains composition and window lifecycle. Its `.Catalog.cs`, `.Account.cs`, `.Queue.cs`, `.Appearance.cs`, and `.Playback.cs` partials group the corresponding native-view responsibilities. Partial files share the same window instance; moving methods introduces no new runtime objects or event subscriptions.

The working player orchestration remains in `MainWindow.Playback.cs`; this change does not claim a complete MVC rewrite of the playback lifecycle. The catalog controller is the first extracted application controller. Playback commands, session restoration, event handling, and queue progression were preserved when organizing the view code.

Catalog operations resume on the caller's UI synchronization context. Keep collection mutation serialized on that context. The view cancels superseded requests; the controller rejects a canceled result even when a metadata provider ignores cancellation. Do not update observable collections from a worker thread or add polling to synchronize the view.

The generated WinUI entry point is owned by the SDK. `tests/Program.cs` only selects named check suites and prints the result. Checks live in `tests/Suites/`, fake providers and test data in `tests/Fixtures/`, and the assertion runner in `tests/Infrastructure/`. Each fixture has its own file. The optional `--native` suite remains available without being required by managed controller checks.

## Coding conventions

`.editorconfig` and `Directory.Build.props` apply to every C# project, and the build treats warnings as errors, so these rules are enforced rather than advisory:

- One type per file, named after the type (`Track.cs` holds `Track`). Folders map to namespaces.
- PascalCase for types and members, `_camelCase` for private fields, `s_camelCase` for private static fields, and PascalCase constants.
- File-scoped namespaces, `System` usings first, no unused usings, braces on their own lines, and one statement per line.
- Classes and records are `sealed` unless they are designed for inheritance.
- No magic numbers: native opcodes and statuses, Win32 constants, layout sizes and limits are named constants or enums. Pages are the `AppView` enum, not strings.
- Types that own disposable resources implement `IDisposable`/`IAsyncDisposable`, or document why WinUI owns their lifetime.
- The `latest-recommended` .NET analyzers run on every build. A suppression needs a `Justification`.

Run `dotnet format Cadenza.slnx` before committing to apply the formatting rules.
