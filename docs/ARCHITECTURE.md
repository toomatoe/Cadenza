# Architecture

Cadenza is a C#/WinUI 3 app with a Rust audio engine and an optional ASP.NET Core Friends server. It has no web frontend and no UI framework beyond WinUI. Keeping it lightweight is a goal (see `AGENTS.md`), so the app has no DI container, MVVM toolkit or logging framework.

Code rules are in [CODING-STANDARDS.md](CODING-STANDARDS.md). This file describes where things live.

## Projects

| Project | Folder | Role |
| --- | --- | --- |
| `Cadenza.Core` | `core/` | Domain models, service interfaces and their implementations. No WinUI references, so it's fully unit-testable. |
| `Cadenza.Desktop` | `desktop/` | The WinUI 3 app: window, XAML, controls, Windows-specific services. |
| `Cadenza.Server` | `server/` | The optional Friends server: accounts, chat, the shared music library. |
| `Cadenza.UnitTests` | `tests/` | MSTest + Moq tests for core and the server, including end-to-end server tests. |
| `cadenza-audio` | `native/` | The Rust audio engine (librespot for Spotify, rodio/Symphonia for library files), called through a small C ABI. |

## `core/` (`Cadenza.Core`)

| Folder | Holds |
| --- | --- |
| `Interfaces/` | Service contracts: `IMusicCatalog`, `ITokenStore`, `ISimilarArtistProvider`, `ITrackResolver`, … |
| `Models/` | `Track`, `Playlist`, `Page`, `QueueEntry`, `ListeningQueue`, `CatalogModel`, playback state and updates, `Tokens`. |
| `Models/Social/` | Friends contracts, grouped by feature: `AccountModels`, `FriendModels`, `MessageModels`, `LibraryModels`. |
| `Models/Recommendations/` | Taste inputs, listening events, recommendations and score breakdowns. |
| `Services/Catalog/` | `CatalogController` (paging, cancellation and bounds for the track and playlist lists) and `QueuePlaybackController`. |
| `Services/Spotify/` | `SpotifyApi`, `SpotifyAuth` (PKCE sign-in, token refresh) and `Pkce`. |
| `Services/Playback/` | `NativeEngine` (the managed side of the Rust ABI) and `PlaybackErrors`. |
| `Services/Recommendations/` | The "For you" engine: taste profile, ListenBrainz lookups, scoring, caches. See `docs/features/recommendations.md`. |
| `Services/Social/` | The Friends client (`SocialClient`, `SocialConnection`), the library song cache, validation rules and music formats. |
| `Services/Music/` | `MusicFileReader`: tags and duration from MP3/FLAC/Ogg files, with no extra libraries. |
| `Services/Sprites/` | The profile companion's pixel art and composer. |
| `Exceptions/` | `SpotifyApiException`, `SocialException`. |

## `desktop/` (`Cadenza.Desktop`)

- `App.xaml` / `App.xaml.cs` are the WinUI entry point and theme resources.
- `Views/MainWindow.xaml` is the single window. Its code-behind is split by feature into partials: `.Catalog`, `.Library`, `.Queue`, `.Playback`, `.Account`, `.Appearance`, `.Profile`, `.Recommendations`, `.Friends`, `.SharedMusic`. *Planned:* move page state and logic into `ViewModels/`, behind core interfaces, with a hand-written `ServiceConfiguration` composition root.
- `Models/` holds desktop values (`Preferences`, `AppView`, `AppearanceMode`) and display items.
- `Services/` holds Windows-only services: `PreferencesStore` (JSON with atomic replace) and `WindowsTokenStore` (Credential Manager).
- `Controls/` (`PixelSprite`, `SquareArtwork`) and `Converters/` (`ArtworkConverter`) are UI building blocks.
- UI rules are in `docs/UI-GUIDE.md`.

## `server/` (`Cadenza.Server`)

- `ServerApp.Build` creates the app with `AddCadenzaServer(configuration)`, `UseCadenzaServer()` and `MapCadenzaEndpoints()` (in `Extensions/`).
- `Endpoints/` has one minimal-API group per feature (accounts, friends, messages, music) plus `AuthenticationFilter`.
- `Interfaces/` (`ISocialStore`, `IMusicLibrary`, `IConnectionHub`) and `Services/` hold the SQLite store, the song files, the WebSocket hub and `TokenHasher`.
- `Models/CadenzaServerOptions` is bound from the `Cadenza` configuration section. `Models/RateLimitPolicies` holds the rate limits.
- Deployment is covered in `server/DEPLOY-RASPBERRY-PI.md`.

## Threading and state

- Catalog and Friends calls resume on the UI synchronization context. Observable collections change only on the UI thread, never from a worker thread.
- The view cancels superseded requests. `CatalogController` rejects a cancelled result even when a provider ignores cancellation.
- Native playback events arrive on a background thread and are dispatched to the UI thread. Async work doesn't cross the Rust ABI: commands return promptly, and C# tasks complete when the matching event arrives.
- Feature services (recommendations, Friends, the library cache) are created on first use, so unused features cost nothing.
