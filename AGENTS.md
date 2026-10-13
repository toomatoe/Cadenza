# Agent notes for Cadenza

Cadenza is a native Windows Spotify client: a WinUI 3 interface in C# (`desktop/`, `core/`) and a Rust audio engine (`native/`).

- **Before any UI change, read [docs/UI-GUIDE.md](docs/UI-GUIDE.md).** It describes the design vision (frosted ice-blue glass over a live blurred desktop), the theme tokens, the layout and the recurring pitfalls. It overrides the older notes in `docs/UI-VALIDATION.md` and `design-qa.md`.
- Code structure and C# conventions: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). The build treats warnings as errors and enforces `.editorconfig`.
- Build the interface with `powershell -File .\build.ps1 -UiOnly`; run the managed tests with `dotnet run --project tests/Cadenza.Tests.csproj -c Release`. The full app with audio needs Rust and the MSVC C++ tools, or the `Cadenza-windows-x64` artifact from GitHub Actions.
- Commit as the repository owner only; don't add co-author trailers.
