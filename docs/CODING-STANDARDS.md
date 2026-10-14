# Cadenza coding standards

These rules follow the Campaign Deputy team standards. Cadenza is a desktop app, so it doesn't use most of Campaign Deputy's web stack (EF, Dapper, Hangfire, Kafka, Aspire), but the C# rules are the same. The sources are:

- `CampaignDeputy.Web/.github/copilot-instructions.md` and its `CLAUDE.md` code-style section, which set the written rules;
- the newer code in `CampaignDeputy.Shared` (the 2025 MessagePublisher library), which shows how they're applied.

Where Cadenza is stricter (warnings as errors, analyzers), it keeps the stricter setting. Every agent and contributor follows this file. When it disagrees with older code in the repo, this file wins; fix the code when you touch it.

---

## 1. Language

- Use the latest C# (`<LangVersion>latest</LangVersion>`) and its features.
- **File-scoped namespaces** and single-line `using` directives. `System` usings come first, and there are no unused usings.
- Use **pattern matching and switch expressions** wherever they read better than `if` chains.
- Use **`nameof`** instead of string literals for member and parameter names.
- **Every async method ends in `Async`** and takes `CancellationToken cancellationToken` as its last parameter (`= default` on public APIs). Pass it down to every call that accepts one.
- Use `var` everywhere and target-typed `new()` when the type is already on the left.
- **No primary constructors.** Write classic constructors that assign `private readonly` fields, with one parameter per line when there are more than two.

## 2. Formatting

- **If an `if` (or `else`) has a single-line body, leave out the braces.** Multi-line bodies always get braces, which go on their own lines.
- **A method's final `return` sits on its own line**, separated from the statements above it by a blank line.
- **If the same string is used more than once, make it a `const`** at the top of the class.
- Expression-bodied members are fine for one-liners (`=> ...`). Use block bodies otherwise.
- No `#region`. No commented-out code. Comments explain *why*, not what. Short section dividers (`// ── Playback ──`) are fine in long files.

## 3. Nullable reference types

- Nullable is on everywhere. Declare things non-nullable by default.
- **Check for `null` at entry points** (public methods that take data from outside: UI, network, files, JSON). **Trust the annotations** everywhere else. Don't add null checks the type system says are impossible.
- **Always `is null` / `is not null`**, never `== null` / `!= null`.

## 4. Naming

| Kind | Style | Example |
| --- | --- | --- |
| Types, methods, properties, events, enum values | PascalCase | `RecommendationEngine`, `LoadAsync` |
| Interfaces | `I` + the implementation's name | `ISpotifyApi` for `SpotifyApi` |
| Private fields, including static fields | `_camelCase` | `_requestGate`, `_json` |
| Constants (any accessibility) | PascalCase | `MaxPageSize` |
| Parameters and locals | camelCase | `artistName`, `cancellationToken` |
| Async methods | `…Async` | `SearchMusicAsync` |
| Exceptions | `<Reason>Exception` | `SocialException` |

## 5. Types and files

- **One type per file, named after the type.** The exception is small related data models, which are grouped by feature in `<Feature>Models.cs` (for example `core/Social/MessageModels.cs`).
- **Models are classes, not records:** sealed classes with an explicit constructor and get-only properties. Constructor-bound properties keep System.Text.Json deserialization working, and WinUI's XAML compiler mishandles `init` setters. Enums stay enums. The one exception is settings objects updated with non-destructive `with` expressions (`Preferences`, `SpriteProfile`), which stay records. When a model is deserialized from JSON, its constructor parameter names must match the property names, using `@explicit`-style escapes for keywords.
- Classes are `sealed` unless they're designed for inheritance. The analyzers ask for it, and it's stricter than Shared, not in conflict.
- `public` is for what other projects use; `internal` is for implementation details.

## 6. Project structure

Folders map to namespaces (`core/Services/Spotify/SpotifyApi.cs` is in `Cadenza.Core.Services.Spotify`). Each project uses the same role folders:

| Folder | Holds |
| --- | --- |
| `Interfaces/` | Every service contract, `I`-prefixed. Consumers depend on these, never on concrete services. |
| `Models/` | Data models and enums, grouped by feature (`Models/Social/`, `Models/Recommendations/`). |
| `Services/<Feature>/` | Implementations, one folder per feature (`Services/Spotify/`, `Services/Social/`, `Services/Recommendations/`). |
| `Exceptions/` | `<Reason>Exception` types, each with the standard `()`, `(string message)` and `(string message, Exception innerException)` constructors. |
| `Extensions/` | Extension-method classes, including DI registration. |

Project-specific layout:

- **`core/` (`Cadenza.Core`)** holds domain models, service interfaces and their implementations, with no WinUI references.
- **`desktop/` (`Cadenza.Desktop`)** is the WinUI app:
  - `Views/` holds XAML plus thin code-behind.
  - `ViewModels/` holds page state and commands.
  - `Services/` holds Windows-specific services (credential store, preferences).
  - `Controls/` and `Converters/` hold UI pieces.
  - `ServiceConfiguration.cs` is the composition root.
- **`server/` (`Cadenza.Server`)** is the Friends server. ASP.NET Core minimal APIs are grouped by feature in `Endpoints/`, services sit behind interfaces, and registration lives in `Extensions/ServiceCollectionExtensions.cs` as `AddCadenzaServer(this IServiceCollection, IConfiguration)`.
- **`tests/` (`Cadenza.UnitTests`)** holds MSTest tests (see §10).
- **`native/`** is the Rust audio engine. It follows `cargo fmt` and its own README.

## 7. Services and composition

- **Every service has an interface** in `Interfaces/`. Constructors take interfaces, never concrete services, so tests can swap in Moq mocks.
- **Constructor injection only.** Services don't create their own collaborators, apart from plain values and BCL types.
- **Composition roots:**
  - **Server:** `services.AddCadenzaServer(configuration)`, using the built-in ASP.NET Core container. Keep registrations in alphabetical order, singletons for stateless or thread-safe services.
  - **Desktop:** `ServiceConfiguration` builds the object graph by hand. There's no DI container package (see the "lightweight" rule in `AGENTS.md`). Feature services are created lazily, on first use.
- Settings that aren't secrets come from configuration or `Preferences`. Secrets go in Windows Credential Manager (desktop) or environment variables (server), never in files under source control.
- Use one shared `HttpClient`. Never write `new HttpClient()` inside a service.

## 8. Errors and logging

- **Errors are exceptions,** not result types. Throw a specific `<Reason>Exception` with a message that's safe to show the user. Use guard clauses with an early `return`/`throw`, and `?? throw new …` for required lookups.
- Use exception filters (`catch (HttpRequestException error) when (…)`) instead of catching and re-throwing. **Never** write an empty `catch` without a comment explaining why it's safe.
- **Logging (server):**
  - Inject `ILogger<T>`.
  - **Use message templates, never string interpolation:** `_logger.LogWarning("Upload from {User} refused: {Reason}", user, reason)`.
  - **When logging an exception, pass it as the first argument** (`_logger.LogError(error, "…")`). If it has an `InnerException`, log that too.
- The desktop app reports problems to the user through the notice bar. It has no logging framework.

## 9. Documentation

- **XML doc comments on every public API** (`<summary>`, plus `<param>`/`<returns>`/`<exception>` where they add information). Add `<example>`/`<code>` where usage isn't obvious. The build enforces this: CS1591 is not suppressed.
- Feature behaviour belongs in `docs/features/<feature>.md`; UI rules in `docs/UI-GUIDE.md`.

## 10. Tests

- **MSTest** (`MSTest.Sdk`) with **Moq** for mocks, in `tests/Cadenza.UnitTests.csproj`. Use MSTest's built-in `Assert` methods.
- One `[TestClass]` per area, named `<Area>Tests` (`RecommendationEngineTests`). Test methods are descriptive PascalCase sentences (`SavedTracksAreNeverRecommended`), matching nearby files.
- Arrange / act / assert separated by blank lines, without labels.
- Tests that need the compiled Rust engine are `[TestCategory("Native")]`. CI runs them, and local runs can filter them out.
- Integration tests (the Friends server) start the real server on a random local port with a temporary database, as `SocialServerTests` does.

## 11. Build and workflow

- Always `dotnet build -nodeReuse:false` (it stops orphaned MSBuild and compiler processes), or use `build.ps1`.
- Warnings are errors. Fix the cause; don't add a suppression without a comment explaining why.
- **One pull request per change into `main`.** CI ("Windows native build") must pass before merging.
- Commit messages are short and imperative ("Add the shared music library"). Commit as the repository owner only, with no co-author trailers.
- Don't add NuGet packages to `core/` or `desktop/` without saying what they cost (see `AGENTS.md`).
