# Music from other sources (design proposal)

**Status:** proposal; nothing built yet. Revised after the owner's feedback that personal music should be **stored on a server** (the owner's Raspberry Pi) rather than only on one PC.

## Goal

Let Cadenza play music that isn't on Spotify, chiefly **your own music library, stored on your Raspberry Pi** and streamed to any PC running Cadenza, plus SoundCloud later, in the same library, queue and player, so a session can mix sources freely.

## What's possible, source by source

| Source | Verdict | Why |
| --- | --- | --- |
| **Your own files on the Pi** (MP3, FLAC, AAC/M4A, OGG/Opus, WAV) | **Yes, first.** | Your own music; stored once on the Pi and streamed to every device. No third-party terms. |
| **Local files on this PC** | **Yes, small add-on.** | Same playback path, read from disk instead of the network; useful offline. |
| **SoundCloud** | **Yes, with conditions.** | API sign-up reopened in May 2026, but **only for Artist Pro subscribers**. Use is bound by SoundCloud's API Terms, and some tracks set `streamable = false`. |
| **YouTube / YouTube Music** | **No playback.** | YouTube's API rules centre on its own embedded player; extracting audio streams (e.g. yt-dlp) breaks its terms; an embedded browser contradicts Cadenza's design. **Allowed:** "Open on YouTube" links. |
| **Bandcamp** | **Links only.** | No public playback API. Purchased downloads go into your Pi library. |
| **Open catalogues, internet radio** | **Maybe later.** | Creative-Commons catalogues (Jamendo, Internet Archive) and the open Radio Browser directory. |

## Server: Navidrome on the Raspberry Pi (recommended)

Rather than writing our own music server, run **[Navidrome](https://www.navidrome.org/)**:

- Open source (GPL-3.0), lightweight, with official Raspberry Pi (arm64) builds and Docker images.
- Indexes a music folder (tags, artwork, albums, artists), with an embedded SQLite database.
- Multi-user: per-user favourites, play counts and playlists; per-library access control.
- Speaks the **Subsonic API**, a long-standing open protocol with dozens of compatible clients, including phone apps, so the same library works away from the PC too.
- Streams original files, or transcodes with ffmpeg on request. Transcoding is the main CPU cost on a Pi; prefer original-quality streaming on a home network.

It runs next to the Friends server on the same Pi.

| Concern | Plan |
| --- | --- |
| **Storage** | The 128 GB card holds the OS and apps. Keep the music on a **USB SSD or HDD** for capacity, speed, and to spare the SD card. |
| **Getting music onto it** | A network share (Samba) or `rsync`/`scp` into the music folder. Navidrome rescans automatically. |
| **Reaching it from Cadenza** | **Tailscale** (a private WireGuard network) for the music library: only your own devices, and people you share with, can reach it, and it isn't public. Using Cloudflare Tunnel for media streaming risks Cloudflare's content-delivery terms, so keep Cloudflare Tunnel for the Friends API. |
| **Backups** | The music library is the irreplaceable part: back it up to a second drive or cloud storage. |
| **Sharing with friends** | Navidrome accounts can be given to friends, but **streaming your files to other people can amount to distributing copyrighted music.** Default to the library being private to you; any sharing is your decision and responsibility. |

## Cadenza changes

- **Track identity:** `Track` gains a `Source` (Spotify, Library, LocalFile, SoundCloud) and a source-specific `SourceId`. Spotify-only fields become optional, and the queue, library and player work with any source.
- **Providers (C#):** `IMusicProvider` (search, browse, resolve to a playable stream), implemented by:
  - `SpotifyProvider`: the existing `SpotifyApi`.
  - `SubsonicProvider`: talks to Navidrome via the Subsonic API (`search3`, `getAlbumList2`, `getAlbum`, `getCoverArt`, `stream`, `star`, `scrobble`), using salted-token authentication.
  - `LocalFileProvider`: optional, for files on this PC.
- **Playback (Rust engine):** a **non-Spotify playback path** next to librespot:
  - **Decoding:** [Symphonia](https://github.com/pdeljanov/Symphonia) (pure Rust: MP3, FLAC, AAC, Vorbis, Opus, WAV) into the existing **rodio** output, sharing volume, seek and the play/pause/end events.
  - **Streams:** HTTP range requests against Navidrome's `stream` endpoint, so seeking doesn't download the whole file. Local files are decoded from disk.
  - **ABI:** a new `LoadUri` opcode (`https://…` or `file:///…`), behind the existing ABI-version check. Spotify keeps using librespot.
- **Credentials:** the server URL, username and token go in Windows Credential Manager, like Spotify's.
- **Recommendations:** library tracks can be scored with the same engine and can resolve recommendations to library copies.

## UI (follows docs/UI-GUIDE.md)

- A small **source badge** (Spotify or Library) on cards and track rows; library tracks show their embedded cover art, or the record placeholder.
- **Library page:** a source filter next to All / Liked.
- **Search:** results from both Spotify and your library, labelled.
- **Settings → Sources:** connect to your music server (URL, username, password → token), test the connection, and pick local folders.

## Phases

1. **Pi setup guide:** Navidrome + music drive + Tailscale (`server/DEPLOY-RASPBERRY-PI.md`, shared with Friends).
2. **Track model and provider abstraction**, with Spotify behaviour unchanged (covered by tests).
3. **Subsonic client:** browse and search the library in Cadenza.
4. **Rust streaming playback:** Symphonia + HTTP range reads; mixed-source queue.
5. Optional local files; then SoundCloud for users with Artist Pro credentials.

## Open questions

1. Is **Navidrome** OK, rather than a custom music server? (Recommended: it's mature, and gives you phone apps for free.)
2. **Tailscale** for reaching the library: OK to install it on the Pi and your PCs?
3. Is the library **private to you**, or should friends get accounts (see the copyright note)?
4. Agree that **YouTube is links-only** and **SoundCloud waits** for Artist Pro credentials?

## Sources

- Navidrome: [overview](https://www.navidrome.org/docs/overview/.), [documentation](https://www.navidrome.org/_print/docs/), [Armbian package](https://docs.armbian.com/software/navidrome/), [Pi multi-client streaming guide](https://pidiylab.com/navidrome-raspberry-pi-multi-client-streaming/)
- SoundCloud API access: [developer blog (May 2026)](https://developers.soundcloud.com/blog/vibe-coding-ai-agent-docs-self-serve-api-keys), [register an app](https://developers.soundcloud.com/docs/api/register-app), [API Terms of Use](https://developers.soundcloud.com/docs/api/terms-of-use)
- YouTube: [Required Minimum Functionality](https://developers.google.com/youtube/terms/required-minimum-functionality)
