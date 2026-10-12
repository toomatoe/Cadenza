> Design restoration: the original saved HTML now grounds the library structure. See design-qa.md for captured evidence, executed checks, and differences. Hero/Up next, playlist fan, Recently added filters, bundled Sora/DM Sans, square artwork and caption bands are restored. Debug/Release builds and 69 checks passed; native filter, queue-edit, scroll and compact-layout checks passed without audio playback.

> Acrylic tuning: uses a custom DesktopAcrylicController with Thin material, 15% light tint (20% dark), and 35% luminosity. Light panel fills are reduced from 28% to 16%. Debug build including native audio passed with zero warnings and errors, and was launched. Visual readability over varied backgrounds remains to be verified.

> Update: The cached Wallpaper implementation described below has been replaced by native Desktop Acrylic. Appearance value 0 now selects Acrylic, preserving saved selections. The image provider and wallpaper overlays were removed. The Release desktop build passed with zero warnings and errors. Earlier screenshot and runtime findings below describe the previous implementation; live Acrylic rendering, movement over other windows, and Windows fallback behavior require fresh visual verification.
# Native appearance validation

The reference is the user's light, wallpaper-backed library screenshot. The production interface remains WinUI 3; album art still comes from the catalog. No generated mockup, web frontend, new dependency, shader, or live blur was introduced.

## Changes

- `desktop/Views/MainWindow.xaml`: albums directly below the heading/search, restrained card fills, square artwork, rounded sidebar and player, native focus visuals. Removed the library hero and animated playlist fan; playlists remain accessible from the sidebar and Playlists page.
- `MainWindow.Catalog.cs`, `.Queue.cs`, `.Account.cs`, `.Playback.cs`, and `.xaml.cs`: removed rendering updates for those deleted decorative surfaces. Playback commands and event handling were preserved.
- `desktop/Models/AppearanceModeModel.cs`, `PreferencesModel.cs`: Wallpaper, Mica, and Solid appearance choices, with legacy Solid preferences preserved.
- `desktop/Services/WallpaperProvider.cs`: one cached native-decoded wallpaper image per window, read through the Windows wallpaper API. Decode target is 1024 × 768. No polling or movement tracking. Changes to the desktop wallpaper appear on the next launch. It uses the wallpaper returned by Windows, not separate monitor-specific images.
- `MainWindow.Appearance.cs`, `desktop/App.xaml`: shared brushes, Solid fallback when effects are disabled, high-contrast suppression, and failure fallback when wallpaper cannot load.

Wallpaper appearance is an opaque image with tinted surface fills, not live transparency. Mica is the native opaque wallpaper tint. Desktop Acrylic would provide actual translucency but was not added because it continuously composites a blurred background and conflicts with the priority on low GPU use. No measured performance superiority is claimed.

## Executed checks

- 48 managed checks passed; no live playback or native playback checks were run.
- Debug and Release Windows x64 builds succeeded without warnings or errors.
- Actual native captures were inspected at the default size and a smaller window, with navigation, material selection, and library scrolling. A scroll reset after page replacement was corrected so the first albums are visible.
- **Windows Transparency effects is disabled on this machine (`EnableTransparency = 0`).** All three material choices therefore correctly use the Solid fallback. Enabled Wallpaper/Mica rendering remains unverified. High-contrast runtime rendering remains unverified. The captures do not establish a visual match to the supplied wallpaper reference.
- The final mixed-mode capture process used roughly 135 MiB private bytes and 242 MiB working set. This is a single observation after navigation/resizing, not a before/after comparison. CPU and GPU differences were not measured.

## Reproducible follow-up

1. Enable Windows Settings → Accessibility → Visual effects → Transparency effects. Launch the freshly built x64 executable from the same path used by `.vscode/launch.json`.
2. Choose Wallpaper, Mica, then Solid in Cadenza Settings. Verify album grid, search, sidebar, controls, keyboard focus, resizing, and scrolling. Wallpaper should show the actual system wallpaper, which may differ from the reference image. Test missing/invalid wallpaper fallback as well.
3. Enable a Windows contrast theme, verify readable system colors and Solid fallback, then restore the prior Windows preferences.
4. For a comparison, build baseline and modified revisions with identical configuration and SDK. Use the same wallpaper, material, library, page count, window size, account, machine, and power settings. Close other instances and repeat each scenario at least three times.
5. Record cold startup separately from warmed startup. Restore/show the window before collecting idle and scroll samples. In Task Manager or Windows Performance Recorder, record process private bytes, working set, CPU, and GPU engine usage; include Desktop Window Manager GPU costs for system materials. Report unsupported GPU counters as unavailable. `scripts/Measure-Ui.ps1` provides process sampling, but its hidden startup must be restored before interpreting visible-rendering costs.
6. Do not run playback scenarios unless the user reauthorizes playback testing. No startup, idle, scrolling, or playback improvement has been established by a controlled before/after measurement for this change.
Rendering follow-up: corrected playlist caption bleed with in-app Acrylic and full-width card content, improved light-theme contrast, and removed the 22px player timeline constraint. Debug/Release builds and 69 core checks passed. Native automation verified idle text, timeline height, and library scrolling. Window-only captures show the inactive Acrylic fallback; active-window transparency still needs visual confirmation. See design-qa.md for the limits of this check.
