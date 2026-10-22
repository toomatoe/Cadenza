# Cadenza UI tests

End-to-end tests for the desktop app. They start the real `Cadenza.Desktop.exe` and click through it with **Windows UI Automation** (`System.Windows.Automation`), the accessibility API screen readers use.

Playwright can't do this: it only drives web pages, and Cadenza's interface is native WinUI 3. UI Automation is built into the Windows desktop runtime, so these tests add **no packages** and nothing to the app.

## Run them

```powershell
powershell -File .\build.ps1 -UiOnly          # builds the app the tests launch
dotnet test ui-tests/Cadenza.UI.Tests.csproj -c Release -nodeReuse:false --settings ui-tests/headless.runsettings
```

Filter by class like any MSTest project, for example `--filter "FullyQualifiedName~SearchAndQueueTests"`.

## Headless

WinUI apps can't run truly headless; they need a Windows desktop session. With `Headless=true`, the default in `headless.runsettings`, the window is shown **off-screen and without activation**. It never covers your work or takes focus, but it renders normally, so UI Automation and screenshots still work.

To watch a test while debugging, set it to `false`, in the file or on the command line:

```powershell
dotnet test ui-tests/Cadenza.UI.Tests.csproj -c Release --settings ui-tests/headless.runsettings -- TestRunParameters.Parameter(name=\"Headless\", value=\"false\")
```

Other parameters:
- `AppPath` points at a different build.
- `TimeoutSeconds` sets how long lookups wait. The default is 30.

Tests never run in parallel. Each test class shares one app window and closes it when the class finishes. A failing test saves a screenshot of the window next to the test results.

## Spotify data

Tests that need Spotify data use a real Spotify sign-in. Without one they're **skipped** (Inconclusive), and everything else still runs.

- **Locally:** open Cadenza once and connect Spotify in Settings. The tests reuse that saved sign-in and never see a password. They only read, except that the search test adds one track to the queue and then clears the queue (the queue isn't saved). They don't change Settings.
- **CI:** add two repository secrets: `CADENZA_SPOTIFY_CLIENT_ID` (your Spotify app's client ID) and `CADENZA_SPOTIFY_REFRESH_TOKEN` (a refresh token for that app). The workflow sets `CADENZA_UI_TEST_SEED=1`, and `Infrastructure/CiSignIn.cs` writes them into the runner's preferences and Credential Manager before the tests start. That seeding only happens with `CADENZA_UI_TEST_SEED=1`, so a local run never touches your settings. Spotify may rotate refresh tokens, so if the Spotify tests start skipping or failing in CI, update the secret.

## Writing tests

- Put one `[TestClass]` per page under `Pages/`, deriving from `UiTestBase`. Name methods as descriptive PascalCase sentences, as in the unit tests.
- Find elements by **automation ID** (the XAML `x:Name`) or by **accessible name** (`AutomationProperties.Name`). If a control has neither, add an `AutomationProperties.Name` to the XAML. That also helps screen-reader users.
- Use `App.WaitUntil(...)` instead of sleeping. Pages load lazily and Spotify calls take time.
- Leave the user's state as you found it: undo anything a test changes, and don't touch Settings.
