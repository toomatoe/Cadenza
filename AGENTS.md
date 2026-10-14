# Agent notes for Cadenza

Cadenza is a native Windows Spotify client: a WinUI 3 interface in C# (`desktop/`, `core/`), a Rust audio engine (`native/`), and an optional Friends server (`server/`).

## Read first

1. **[docs/CODING-STANDARDS.md](docs/CODING-STANDARDS.md)**: the C# rules, project structure, services, errors, docs and tests. They follow the Campaign Deputy team standards and are mandatory for every change. When older code disagrees with them, the standards win; bring the code you touch into line.
2. **[docs/UI-GUIDE.md](docs/UI-GUIDE.md)** before any UI change: the design vision (frosted ice-blue glass over a live blurred desktop), theme tokens, layout and recurring pitfalls. It overrides the older notes in `docs/UI-VALIDATION.md` and `design-qa.md`.
3. [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): where things live and how they connect. Feature behaviour is in `docs/features/`.

## The short version of the standards

- File-scoped namespaces, pattern matching and switch expressions, `nameof`, `var`. Async methods end in `Async` and take `CancellationToken cancellationToken` last.
- No braces on single-line `if` bodies; a blank line before a method's final `return`; repeated strings become `const`s.
- `is null` / `is not null`. Check for null at entry points only, and trust the annotations elsewhere.
- `_camelCase` private fields (static ones too) and PascalCase constants. No primary constructors.
- Every service has an `I`-interface in `Interfaces/`, and constructors take interfaces. Errors are `<Reason>Exception` types.
- On the server, log with message templates, never interpolation, and pass the exception (and its inner exception) to `LogError`.
- XML docs on every public API. The build enforces them.
- Tests are MSTest + Moq. One `[TestClass]` per area, with descriptive PascalCase test names.

## Working rules

- **Keep the app lightweight.** Most features are C#/.NET (some work moved to Rust). Prefer the BCL over new NuGet packages in `core/` and `desktop/`, create feature services on first use, keep caches and logs bounded, and avoid timers or polling when idle. Mention the cost of any new dependency before adding it.
- **Build:**
  - The interface: `powershell -File .\build.ps1 -UiOnly`.
  - With raw `dotnet build`, always pass `-nodeReuse:false`.
  - Tests: `dotnet test tests/Cadenza.UnitTests.csproj -c Release -nodeReuse:false --filter "TestCategory!=Native"` (drop the filter when the Rust DLL is built).
  - The full app with audio needs Rust and the MSVC C++ tools, or the `Cadenza-windows-x64` artifact from GitHub Actions.
- Warnings are errors; fix them rather than suppressing them.
- One pull request per change into `main`; CI must pass before merging.
- Commit as the repository owner only; don't add co-author trailers.
