# Agent notes for Cadenza

Cadenza is a native Windows Spotify client: a WinUI 3 interface in C# (`desktop/`, `core/`), a Rust audio engine (`native/`), and an optional Friends server (`server/`).

- **Before any UI change, read [docs/UI-GUIDE.md](docs/UI-GUIDE.md).** It describes the design vision (frosted ice-blue glass over a live blurred desktop), the theme tokens, the layout and the recurring pitfalls. It overrides the older notes in `docs/UI-VALIDATION.md` and `design-qa.md`.
- **Keep the app lightweight.** Most features are C#/.NET (some work moved to Rust). Prefer the BCL over new NuGet packages in `core/` and `desktop/`, create feature services on first use, keep caches and logs bounded, and avoid timers or polling when idle. Mention the cost of any new dependency before adding it.
- Code structure and C# conventions: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). The build treats warnings as errors and enforces `.editorconfig`.
- Build the interface with `powershell -File .\build.ps1 -UiOnly`; run the managed tests with `dotnet test tests/Cadenza.UnitTests.csproj -c Release -nodeReuse:false --filter "TestCategory!=Native"`. The full app with audio needs Rust and the MSVC C++ tools, or the `Cadenza-windows-x64` artifact from GitHub Actions.
- Commit as the repository owner only; don't add co-author trailers.
