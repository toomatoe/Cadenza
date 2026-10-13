# Cadenza UI guide

**Read this before changing anything in `desktop/`.** It is the source of truth for how Cadenza should look and feel, and why. Older notes in `docs/UI-VALIDATION.md` and `design-qa.md` are a historical log; where they disagree with this file, this file wins.

Cadenza is a lightweight native Windows Spotify client: WinUI 3 in C#, with a Rust audio engine. There is no web UI and there should not be one.

---

## 1. The vision

**Frosted ice-blue glass over your desktop.** The window is see-through: you can see a soft blur of whatever is behind it, tinted ice blue, with gentle glows of light. On top of that float rounded, frosted-glass panels that hold the content. The overall feel is calm, airy, glossy and premium, closer to a piece of frosted glass than to a flat app.

The visual reference is the owner's **"Cadenza Glass Design"** mockup (an HTML design canvas). Its key traits:

- One colour family: ice blue. Navy for text, a single blue accent, white glass. No rainbow accents.
- Light, translucent glass panels with bright white top edges and soft shadows.
- Glossy "record" artwork: rounded square tiles with a sheen, holding a dark vinyl record.
- A library page built around a big **"Jump back in" / "Now playing"** card, a **fanned stack of playlist cards**, and a **"Recently added"** grid.
- A floating glass **player bar** along the bottom with a round navy play button.

### Owner preferences (decided, don't revisit without asking)

| Decision | Detail |
| --- | --- |
| **Keep the blur.** | The live Acrylic, blurred-desktop background is wanted. Never switch the default to Solid or remove the transparency to match the mockup more literally. |
| **Ice blue, one hue.** | Lighter and bluer is better than grey or white. Earlier violet and multi-colour palettes were rejected. |
| **Glossy and translucent.** | Artwork tiles and cards should feel glassy: gradients, sheen, translucency, not flat fills. |
| **Match the mockup's structure.** | Hero card, playlist fan, Recently added with All/Liked filters, sidebar playlist list, search at top right. Don't remove these to "simplify". |
| **No visual seams.** | Edges and corners must be clean. Visible lines or bands at the window edges have been reported more than once; see §4.3. |

---

## 2. Design tokens

All colours live in `desktop/App.xaml` as **theme brushes** with Light, Dark and HighContrast variants. The window forces the Light theme (`RequestedTheme="Light"`), so Light is the look that matters; Dark must still work, and HighContrast must stay plain and readable.

**Never hard-code a colour in a view.** Use the brush by key: `{ThemeResource Ink}` in XAML, `AppearanceBrush("Ink")` in code-behind. Code-behind must use `AppearanceBrush`, because it resolves the window's actual theme, including high contrast.

### Colour roles (Light)

| Key | Value | Use |
| --- | --- | --- |
| `Ink` | `#10243D` navy | Primary text and icons. |
| `MutedText` | `#465D77` | Secondary text: artists, subtitles, hints, eyebrows. Keep at least 4.5:1 contrast. |
| `Accent` | `#2A74CF` blue | The one accent: primary buttons, selected navigation, links, progress fill. |
| `WindowWash` | ice-blue gradient, ~30–35% alpha | Laid over the Acrylic to tint it blue. Must stay translucent. |
| `GlowPrimary` / `GlowHighlight` / `GlowLower` | radial gradients | Soft blue glow top left, white highlight right, blue glow bottom. |
| `SolidWindow` | opaque ice-blue gradient | Background when transparency is off (Solid mode, effects disabled, high contrast). |
| `GlassSurface` | `#66FFFFFF` | Fill for glass panels (sidebar, hero, player, settings cards). |
| `CardSurface` | `#66FFFFFF` | Fill for album and playlist cards. |
| `GlassEdge` | `#D6FFFFFF` | 1px bright edge on glass panels. |
| `CaptionBand` / `CaptionEdge` | translucent white | The strip under artwork that holds title and subtitle. |
| `PlaylistFanSurface` / `PlaylistCaption` | in-app Acrylic / near-opaque | Fan cards must be opaque enough that overlapping titles don't bleed through. |
| `NavSelectedBrush` | `#99FFFFFF` | Selected sidebar item fill. |
| `ArtTile`, `ArtEdge`, `VinylFill` | neutral blue-grey, white edge, dark navy | Artwork placeholder and record graphics. |

The window's Acrylic itself is configured in `desktop/Services/TransparentAcrylicBackdrop.cs`: Thin material, light tint `RGB(214,232,250)` at 22%, luminosity 35%, fallback `RGB(221,235,248)` for inactive windows.

**Alpha belongs to brushes, not elements.** Make a surface translucent through its brush. Don't set `Opacity` on containers, which would also fade the text and icons inside them.

### Typography

| Role | Font | Size and weight |
| --- | --- | --- |
| Page title (`PageTitle`) | Sora (`HeadingFont`) | 40, SemiBold |
| Hero title | Sora | 34 |
| Section title (`SectionTitleStyle`) | Sora | 22, SemiBold |
| Body, labels | DM Sans (`BodyFont`) | 14–16 |
| Secondary text | DM Sans | 12–13, `MutedText` |
| Eyebrow (`EyebrowTextStyle`) | DM Sans | 11, letter-spacing 180, uppercase, `MutedText` (e.g. "YOUR MUSIC, YOUR MOMENT", "JUMP BACK IN") |

Both fonts ship in `desktop/Assets/Fonts`, with their OFL licences. Don't add other typefaces. Inter, Roboto and Segoe UI for content text are off-brand.

### Shape and spacing

- Corner radius: large panels 26 (sidebar, player, hero), cards 16, buttons 14, pills 22–24, icon buttons fully round.
- The window content is inset 16px left, right and bottom; the title bar row is 36px.
- Cards: 208px wide, at least 266px tall, 16px gaps.
- Touch and click targets are at least 40×40 (icon buttons) or 44px tall (nav, pills).
- Icons: Segoe Fluent Icons via `FontIcon`, never emoji.

---

## 3. Layout map

```
┌─────────────────────────────── window (live Acrylic + ice-blue wash + glows, edge to edge) ───┐
│ title bar (36px, draggable, "Cadenza")                                         – □ ×          │
│ ┌ sidebar (220, glass) ┐  ┌ page ─────────────────────────────────────────────────────────┐ │
│ │ cadenza / tagline    │  │ EYEBROW                                        [ search pill ] │ │
│ │ Library  For you     │  │ Page title                                                     │ │
│ │ Playlists  Queue (n) │  │ ┌ content: Library / Search / Playlists / Queue / Settings ──┐ │ │
│ │ PLAYLISTS (scroll)   │  │ │                                                            │ │ │
│ │ ● Spotify connected  │  │ └────────────────────────────────────────────────────────────┘ │ │
│ │ Settings             │  │ [ toolbar row: Load more / queue tools ]                       │ │
│ └──────────────────────┘  └────────────────────────────────────────────────────────────────┘ │
│ ┌ player bar (glass) — art · title/artist · ♡ ║ shuffle ■ (▶) ▶| ║ volume ───────────────────┐ │
│ │                          progress slider · status                                         │ │
│ └───────────────────────────────────────────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────────────────────────────────────────┘
```

Pages are the `AppView` enum (`desktop/Models/AppView.cs`). Navigation buttons carry the enum name in `Tag`.

- **Library:** hero card ("JUMP BACK IN", or "NOW PLAYING" while playing) with glossy record artwork, Play / Add to queue / Open, and an **Up next** column showing three queued tracks. Below it, **Your playlists** (the fan) and **Recently added** with All / Liked chips, an **Add songs** button (uploads music files to the shared Cadenza library) and the album grid.
- **For you:** a reconnect InfoBar when the newer Spotify scopes are missing, a glass header card with the Familiar–New discovery slider and Refresh, then titled shelves (More like your favourites, Deeper cuts, Something new) of 208px album cards. Each card adds a one-line accent reason (its tooltip shows the score breakdown) and a "Not interested" button on the artwork. Nothing loads until the page is opened.
- **Friends:** a join card (server address, invite code, username, display name) until you join. Then two glass columns: on the left, you, an Invite button, add-by-username, incoming requests and your friends; on the right, the conversation, where chat bubbles align by sender and shared tracks show as a small art tile with Play and Add to queue. The composer has Share what's playing and Send to their queue. The Friends nav item shows an unread count.
- **Search:** results list; an empty-state card before you type ("Find something to play").
- **Playlists:** playlist card grid, and a playlist detail header with its track list.
- **Queue:** track list with a toolbar (Play queue, up/down, Remove, Undo, Shuffle with spacing, Clear); empty state "A fresh listening session".
- **Settings:** glass cards for Appearance (Acrylic / Mica / Solid), Connect Spotify and queue preferences.

Below 1100px wide (`CompactLayoutWidth`), the search pill drops under the title and Up next moves under the hero. Every layout change must still work at that width.

Code for each area lives in the matching `MainWindow.*.cs` partial (Catalog, Library, Queue, Playback, Account, Appearance, Profile, Recommendations, Friends). See `docs/ARCHITECTURE.md` for coding conventions.

---

## 4. Materials and layering

### 4.1 The layer stack (back to front)

1. **System backdrop.** `TransparentAcrylicBackdrop` is a live blur of the desktop. Mica or nothing replaces it when the user picks those, or when Windows transparency effects, power saving or high contrast turn effects off.
2. **`SolidBackground`.** Opaque ice-blue gradient. Visible only when no backdrop is active (`ApplyAppearance` toggles it).
3. **`WindowWash`.** Translucent ice-blue gradient that tints the blur.
4. **Glows.** Three radial gradients that add light, as in the mockup.
5. **Glass panels and content.**

Layers 2–4 live in the outer `WindowSurface` grid, **which fills the whole window**. Only `Root` (the content grid) has the 16px margin.

### 4.2 Glass panel recipe

`GlassCardStyle`: translucent white fill (`GlassSurface`), 1px bright `GlassEdge`, rounded corners. Cards use `AlbumCardStyle`. Captions under artwork use a `CaptionBand` strip with a 1px `CaptionEdge` top border. Keep panels light and see-through; heavier fills make the app look grey.

### 4.3 Seams and edges (recurring bug; check every time)

Visible lines at the window edges have come back more than once. The causes so far:

- **Background layers inside an inset container.** If a wash or glow sits inside a grid with a margin, a strip of untinted Acrylic shows between it and the window frame, with a hard seam. Background layers must fill the window; inset only the content.
- **Opaque fills meeting translucent ones** at a hard edge.
- **Clipping and borders on rounded panels.** Check panel corners for doubled borders or square edges.

After any change to backgrounds, margins, borders or panels, look closely at **all four window edges and the corners** in a live capture (§7).

### 4.4 Artwork

- Real album art fills the square tile (`UniformToFill`) with rounded corners.
- With no art, show the neutral `ArtTile` placeholder or a record graphic (`VinylFill` disc with a light label). Never leave an empty or broken image.
- Artwork goes through `ArtworkConverter` / `ArtworkConverter.GetArtwork(url, width)`, which accepts HTTPS only and caches with a bounded decode width.

---

## 5. Components and behaviour

- **Navigation:** the selected item gets `NavSelectedBrush`, `Accent` text and SemiBold weight. `UpdateNav` sets this; always update it after changing `_view`.
- **Primary actions** use `PillAccentButtonStyle` (blue pill, white text). The player's play/pause is a 52px navy circle. Secondary actions are glass buttons.
- **Text inside buttons must use `ButtonLabelStyle`.** The app-wide `TextBlock` style hard-codes `Ink`, which stops button text from greying out when the button is disabled or turning white on accent buttons. This bug has happened before.
- **Playlist fan:** up to three pinned playlists plus "Liked songs", tilted ±8° and overlapping at a 90px step; they straighten and spread to a 190px step on hover or keyboard focus. Captions are left-aligned so the start of each name shows where cards overlap. Fan cards need near-opaque surfaces.
- **Cards:** one-line titles with ellipsis and a full-name tooltip. A wrap grid sizes every item like the first one, so wrapped titles push subtitles out of the card.
- **Empty states:** a centred glass card with an icon, a heading and one helpful action, and the heading must fit the page. The Queue offers "Browse library"; Connect Spotify appears only when disconnected.
- **Messages:** use the `Notice` InfoBar through `Message(text, severity)`. Never use modal dialogs for routine feedback.
- **Disabled controls** must look disabled. Never hide important controls just because they're unavailable.

---

## 5a. Profile companion

- The pixel-art companion (`PixelSprite`) must stay **crisp**: integer scaling in physical pixels, never `Stretch` or fractional sizes. New art must be 24×24 in `core/Sprites/SpriteArt.cs` using the documented palette keys, and must be original or CC0.
- It is decorative and personal: it may bring its own colours, but keep it small (sidebar about 48px, preview about 120px) and never let it carry information unavailable elsewhere.
- Animation is low frame rate, pauses when hidden, and respects Windows animation settings.

## 6. Accessibility (non-negotiable)

- Every icon-only or icon-and-text button needs `AutomationProperties.Name`. WinUI can't derive a name from a `StackPanel`.
- Keep text contrast at least 4.5:1 against its actual background, including over a dark desktop seen through the blur.
- Keep focus visuals (`UseSystemFocusVisuals`) and full keyboard access; anything that reacts to hover must also react to focus, like the fan.
- High contrast must use system colours and no glows or washes. Check that new brushes get a HighContrast entry.
- Respect Windows transparency settings: the app falls back to Solid automatically, so don't fight it.

---

## 7. Making and verifying UI changes

1. **Build:** `powershell -File .\build.ps1 -UiOnly` (or `run.ps1 -UiOnly`). Warnings are errors, so the build must be clean.
2. **Run it and look.** A clean build doesn't prove the UI is right. Check the page you changed, its empty state, the compact width (below 1100px), and all four window edges.
3. **Screenshots:**
   - Off-screen capture (`PrintWindow`) works without stealing focus, but shows the **inactive Acrylic fallback**, not the live blur.
   - Live appearance needs an on-screen capture of the window in front.
   - **The owner uses this machine.** Don't repeatedly bring the window to the front or drive the mouse; prefer UI Automation (`InvokePattern`) and off-screen captures, and stop if another app is in front.
4. **Compare with the vision** (§1) and the mockup's structure, not pixel by pixel. Real artwork and content will differ from the mockup.
5. **Report honestly:** say which states and pages were actually seen and what wasn't verified, e.g. the live blur, dark theme, high contrast, or audio playback.

### Checklist for any UI change

- [ ] Colours come from theme brushes; any new brush has Light, Dark and HighContrast entries.
- [ ] Still ice blue and translucent; no new hues, no grey or white wash, blur intact.
- [ ] No seams at the window edges or panel corners.
- [ ] Buttons have accessible names; text inside buttons uses `ButtonLabelStyle`.
- [ ] Long text truncates cleanly with a tooltip; nothing clips.
- [ ] Works at compact width and with an empty library or queue.
- [ ] Clean build; app launched and the changed screens looked at.

---

## 8. Known gaps versus the mockup

These are open, not intentional differences. Ask before treating them as done:

- No animation: the mockup's record spins and its equalizer moves while playing.
- The player bar has no **Previous** button (needs a listening history) and no elapsed/total time on either side of the progress bar.
- "Liked" filtering is session-only and doesn't save to Spotify.
