# Claude design restoration

Status: library structure and tested interactions restored; active-window transparency still needs visual confirmation.

Reference: the user-provided Cadenza Glass Design HTML, rendered locally at 1440 x 900. Captures are under artifacts/ and are not published. The reference uses fictional music and active playback; the native app uses the connected library with idle playback. These content/state differences are not treated as visual defects. This is not a claim of pixel-identical rendering.

Restored the Sora headings and DM Sans text from the bundled reference fonts, the hero with Up next preview, playlist fan with hover/focus expansion, Recently added All/Liked filters, rounded square artwork, caption bands, search pill, and compact dark player control. Native Acrylic remains the background material.

Earlier defects corrected: missing hero/fan/filters; mismatched typography; oversized navigation and cards; duplicate bottom library toolbar; clipped section heading; zero-height artwork and missing item data contexts in the virtualized shelf. Post-fix artwork capture: artifacts/restored-library-cards.png. The source and native captures were also placed together in artifacts/design-comparison.png for structural review.

Checks: Debug and Release builds succeeded with zero warnings/errors; all 69 core checks passed. Native UI automation verified All/Liked switching, scrolling, adding a track, queue badge update, queue navigation, undo, and the Up next section moving below the hero at a compact width. The temporary queue addition was undone. Audio playback was not started.

Remaining native differences: Windows caption controls/icons and system-material rendering; real album artwork instead of the reference's fictional record tiles. Likes currently last for the session and do not alter Spotify's saved library. Continuous record/equalizer animation is not implemented.

Follow-up rendering correction: the user's screenshot exposed overlapping captions, insufficient light-theme contrast over a dark desktop, and a clipped player timeline. The earlier structural checks did not catch those defects. Playlist cards now stretch their content across the full card and use in-app Acrylic with a stronger caption surface to obscure underlying labels. The light theme has a neutral translucent wash and stronger panel fills. Idle player text is shorter, its text column is wider, and the timeline uses the native control's required height instead of being forced into 22 pixels. Programmatic playlist brushes refresh when the appearance changes.

Validation after this correction: Debug and Release builds passed with zero warnings/errors; 69 core checks passed; native automation checked idle player text, timeline height, and library scrolling. Window-only captures are artifacts/render-fix-home.png and artifacts/render-fix-shelf.png. The home capture confirms card occlusion and the complete player thumb, but shows the inactive Acrylic fallback; active desktop transparency and a pixel-perfect reference match are not claimed. Audio playback was not started.
