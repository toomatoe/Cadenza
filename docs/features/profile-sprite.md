# Profile sprite (design proposal)

**Status:** proposal; nothing built yet. Review the open questions at the end before implementation starts.

## Goal

Each listener gets a small **2D pixel-art companion**, an animal or sprite they configure, that represents them in Cadenza. It reacts to what's playing, gives the app personality, and becomes the listener's face in the Friends feature.

## Concept

- **Species:** a starter set of about six, e.g. cat, fox, frog, bird, axolotl, bunny.
- **Customisation:** body colour and accent colour (palette swap), one accessory slot (headphones, beanie, scarf, flower, none) and a name.
- **Moods, driven by playback state:**

| State | Animation |
| --- | --- |
| Nothing playing | Idle: slow breathing, occasional blink |
| Playing | Bobbing in time with a steady beat |
| Paused | Sitting or dozing ("z") |
| Track liked | A short heart pop |
| Playback failed | A brief confused look, then idle |

Spotify no longer gives new apps tempo or beat data, so "in time" means a fixed, gentle tempo (around 100 BPM), not the song's real beat.

## Where it appears

- **Sidebar**, under the "cadenza" wordmark: the companion at 3× scale (about 96px), with the name on hover.
- **Settings → Profile**: an editor with a live preview, species and colour pickers, accessory choice and name.
- **Friends** (later): avatars in the friends list and chat; shared through the profile, never as uploaded images.

It should feel at home in the frosted ice-blue look (docs/UI-GUIDE.md): the sprite sits on glass, with no heavy frame. The sprite may bring its own colours, but the default palette should lean cool and soft.

## Art pipeline

- **Grid:** 32×32 pixels per frame, scaled by whole numbers only (2×, 3×, 4×) with **nearest-neighbour** filtering, so pixels stay crisp and never blur.
- **Layered sprite sheets** (PNG), one row per animation: `body_<species>.png`, `accessory_<name>.png`. Layers are composited at runtime.
- **Palette swap:** the body art uses a fixed set of "key" colours that are replaced with the chosen body and accent colours when loading, so one sheet serves every colour scheme.
- **Animation:** 4–8 frames per state at 8–10 fps.
- **Source of the art:** commissioned, self-drawn (Aseprite or LibreSprite), or a permissively licensed pack (CC0 or CC-BY with attribution). The licence must allow redistribution in an open-source app; record it in `desktop/Assets/Sprites/LICENSE.txt`.

## Technical design

- `desktop/Controls/PixelSprite`: a custom control that composites layers into a `WriteableBitmap` at native size, then displays it in an `Image` with integer scaling.
  - Crisp scaling needs nearest-neighbour. WinUI's `Image` smooths by default, so either pre-scale the bitmap by an integer factor before display, or draw with Win2D (`CanvasControl`, `CanvasImageInterpolation.NearestNeighbor`). **Decision for the spike:** pre-scaling avoids a new dependency; use Win2D only if pre-scaling is too costly.
- **Animation timing:** a `DispatcherQueueTimer` at the sprite's frame rate, **paused when the window is minimised or hidden**, and frozen on a still frame when Windows "Animation effects" is off (`UISettings.AnimationsEnabled`).
- **State:** `ProfileSprite` record (species, body colour, accent colour, accessory, name) saved in `preferences.json` alongside the other preferences. Old preference files without it load with a default sprite.
- **Mood input:** the window already tracks `PlaybackState` and likes; the sprite subscribes to a small `SpriteMood` value derived from them.
- **Accessibility:** the sprite is decorative. `AutomationProperties.Name` = "{name} the {species}", and it never carries information that isn't available elsewhere.

## Phases

1. Spike: a `PixelSprite` control with one placeholder species, crisp scaling, idle animation and timer lifecycle.
2. Palette swap, accessory layer, profile storage.
3. Settings → Profile editor with live preview.
4. Playback moods.
5. The full species and accessory set once the art is ready.
6. Friends integration (depends on the Friends feature).

## Open questions

1. **Art:** who makes it (you, a commission, or an open-licence pack), and is 32×32 the right size?
2. **Species list:** which animals, and should rarer ones unlock through listening milestones?
3. **Placement:** sidebar only, or also a tiny version in the player bar?
4. Should friends be able to see your sprite's current mood, i.e. "listening now"? That shares playback state, so it would be opt-in.
