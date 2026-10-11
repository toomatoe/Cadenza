# Reference comparison

final result: blocked

The target is the user's wallpaper-backed native Cadenza library reference. Actual WinUI captures live under `artifacts/ui-validation/wallpaper-option-*.png` (local, ignored artifacts). The latest capture shows four album columns, the first albums at the top, square artwork, reduced card padding, rounded sidebar/player surfaces, and the heading/search layout without the former hero and animated fan.

Typography uses native Segoe UI, with the reference's hierarchy retained. Spacing was corrected to put albums immediately below the header. Shared light theme colors retain blue controls and opaque text. Artwork uses real catalog URLs and bounded decoding; no replacement images or generated mockups were used. Content and actions remain the existing catalog, playlist, queue, and playback controls.

Full material fidelity cannot be judged: Windows Transparency effects is off on this machine, so the captured Wallpaper and Mica choices are Solid fallbacks. The source attachment also is not available as a local image for a normalized side-by-side composite. The actual enabled Wallpaper/Mica appearance and high-contrast runtime state still require capture and comparison. Smaller windows appropriately scroll, rather than fitting the entire reference's two rows into a short viewport.

Do not treat passing builds or successful captures as a passing visual-match gate. Follow `docs/UI-VALIDATION.md` to complete the remaining checks; preserve the user's direction not to test playback.
