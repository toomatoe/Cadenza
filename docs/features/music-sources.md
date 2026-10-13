# Music from other sources

**Status:** the shared library is built (see "What was built"). The sections after it are the earlier research and design.

## Goal

Let Cadenza play music that isn't on Spotify, in the same search, queue and player, so a session can mix sources freely.

## Decisions (owner feedback, 2026-10-13)

- **Songs that aren't on Spotify are stored on the server** (the Raspberry Pi) in **one shared library**. Anyone with an account on that server finds them through the normal search bar.
- People add songs by **uploading music files** they have (MP3, FLAC, Ogg Vorbis) from Cadenza.
- **YouTube is links only; SoundCloud waits** until Artist Pro credentials are available.
- **Navidrome is optional,** not required (see below). **Tailscale** isn't needed for this step.

## What was built

| Part | How it works |
| --- | --- |
| **Uploading** | Library → **Add songs** opens a file picker (multiple files). Cadenza reads title, artist, album and duration from the file's tags (`core/Music/MusicFileReader.cs`, plain C#: ID3v2 + Xing/CBR for MP3, STREAMINFO + Vorbis comments for FLAC, Ogg Vorbis headers), falls back to an "Artist - Title" file name, and uploads to the Friends server. Files are checked by their first bytes, capped at 95 MB (Cloudflare's free plan allows 100 MB per request), and stored once: an identical file uploaded again returns the existing song. |
| **Server** | The Friends server keeps files in its music folder (`Cadenza:MusicFolder`), named by ID, with details in SQLite. `GET /api/music?q=` searches title, artist and album (every word must match); `GET /api/music/{id}/file` streams with range requests; uploaders can remove their own songs. Uploads are rate-limited to 200 an hour. |
| **Search** | When you've joined Cadenza Friends, matching library songs appear above the Spotify results, labelled "Cadenza library · added by …" (or their album). |
| **Playback** | A library song is downloaded to a local cache (`%LOCALAPPDATA%\Cadenza\library-cache`, capped at 1 GB, least recently played removed first), then played by the Rust engine's new **opcode 9** (ABI 3). That uses rodio's Symphonia decoder for MP3, FLAC and Ogg Vorbis, which librespot already compiles in, so no new crates are added. Play, pause, seek, volume, the queue and end-of-track all work as they do for Spotify, and a library song plays without a Spotify session. |
| **Friends** | Library songs have no Spotify ID, so sharing one sends a message telling your friend to search for it. |
| **Weight** | No new NuGet packages in the app and no new Rust crates. Nothing runs until you search, upload or play a library song. |

**Not in this step:** cover art for library songs (they show the record placeholder), M4A/AAC and Opus (they'd need extra Symphonia crates), editing song details after upload, and phone apps.

**Your call:** in a shared library, everyone on the server can stream every song. Uploading copyrighted music you bought and letting others stream it can count as distributing it. The owner decided on a shared library knowing this.

**Optional Navidrome:** point Navidrome at the same music folder (read-only) if you want phone apps or a web player. Its files are the uploaded originals, so their tags show up there too.

**Network note:** music goes through the same Cloudflare Tunnel as chat. Cloudflare's terms discourage using its network mainly for large media files. For a small group, that's a judgement call; the alternative is Tailscale (see `server/DEPLOY-RASPBERRY-PI.md`).

**Tests:**
- Tag reading for all three formats, and the file-name fallback.
- The playback cache's eviction order.
- Format detection.
- End-to-end against the real server: upload, de-duplication, search, byte-exact download, range requests, refusing non-music files, and that only the uploader can remove a song.
- Rust: the file player rejects bad paths and non-audio files.

Audible playback can't be checked automatically.

## What's possible, source by source

| Source | Verdict | Why |
| --- | --- | --- |
| **Your own files on the Pi** (MP3, FLAC, AAC/M4A, OGG/Opus, WAV) | **Yes, first.** | Your own music; stored once on the Pi and streamed to every device. No third-party terms. |
| **Local files on this PC** | **Yes, small add-on.** | Same playback path, read from disk instead of the network; useful offline. |
| **SoundCloud** | **Yes, with conditions.** | API sign-up reopened in May 2026, but **only for Artist Pro subscribers**. Use is bound by SoundCloud's API Terms, and some tracks set `streamable = false`. |
| **YouTube / YouTube Music** | **No playback.** | YouTube's API rules centre on its own embedded player; extracting audio streams (e.g. yt-dlp) breaks its terms; an embedded browser contradicts Cadenza's design. **Allowed:** "Open on YouTube" links. |
| **Bandcamp** | **Links only.** | No public playback API. Purchased downloads go into your Pi library. |
| **Open catalogues, internet radio** | **Maybe later.** | Creative-Commons catalogues (Jamendo, Internet Archive) and the open Radio Browser directory. |

## Research: Navidrome on the Raspberry Pi (optional now)

Rather than writing our own music server, run **[Navidrome](https://www.navidrome.org/)**:

- Open source (GPL-3.0), lightweight, with official Raspberry Pi (arm64) builds and Docker images.
- Indexes a music folder (tags, artwork, albums, artists), with an embedded SQLite database.
- Multi-user: per-user favourites, play counts and playlists; per-library access control.
- Speaks the **Subsonic API**, a long-standing open protocol with dozens of compatible clients, including phone apps, so the same library works away from the PC too.
- Streams original files, or transcodes with ffmpeg on request. Transcoding is the main CPU cost on a Pi; prefer original-quality streaming on a home network.

It runs next to the Friends server on the same Pi.

### Navidrome vs the alternatives (checked 2026-10-13)

| | Navidrome | gonic | Jellyfin |
| --- | --- | --- | --- |
| Focus | Music only | Music only, minimal | Video, music and books |
| Latest release | v0.64.2 (2026-09-24) | v0.22.0 (2026-06-08) | v12.2 (2026-10-05) |
| Community (GitHub stars) | ~24k | ~2.6k | ~58k |
| Pi footprint | Single Go binary, official linux-arm64 builds, plus a `deluan/navidrome` Docker image with arm64 | Single Go binary, very light | Heavier .NET server; guides suggest 2–4 GB RAM |
| Subsonic API (phone apps, and our client) | Yes | Yes | No (its own API) |
| Web player, multi-user | Yes | No web player | Yes |

**Verdict: Navidrome.**
- It's light enough for the Pi, actively maintained, and runs as a native binary or with Docker.
- It speaks the Subsonic API, which Cadenza would talk to and which phone apps already support.
- gonic is lighter still, but it has no web UI and a much smaller community.
- Jellyfin is built around video and doesn't speak Subsonic.
- None of them uploads music from a browser well. You copy files into the music folder (a Samba share or `rsync`), and the server rescans it.


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

1. ~~Shared library: upload, search, play~~ **Built.**
2. Cover art from embedded tags; M4A/AAC if wanted.
3. Optional Navidrome next to the Friends server for phone apps.
4. SoundCloud for users with Artist Pro credentials.

## Open questions

1. ~~Navidrome?~~ Optional; not needed for the shared library.
2. ~~Tailscale?~~ Not needed now; an alternative if Cloudflare's media terms become a concern.
3. ~~Library private or shared?~~ **Decided: shared** with everyone on the server.
4. ~~YouTube / SoundCloud?~~ **Decided:** YouTube links only; SoundCloud later.

## Sources

- Server comparison: [navidrome/navidrome](https://github.com/navidrome/navidrome/releases), [sentriz/gonic](https://github.com/sentriz/gonic), [jellyfin/jellyfin](https://github.com/jellyfin/jellyfin), [deluan/navidrome on Docker Hub](https://hub.docker.com/r/deluan/navidrome), [selfhosting.sh music servers](https://selfhosting.sh/best/music-streaming/)
- Navidrome: [overview](https://www.navidrome.org/docs/overview/.), [documentation](https://www.navidrome.org/_print/docs/), [Armbian package](https://docs.armbian.com/software/navidrome/), [Pi multi-client streaming guide](https://pidiylab.com/navidrome-raspberry-pi-multi-client-streaming/)
- SoundCloud API access: [developer blog (May 2026)](https://developers.soundcloud.com/blog/vibe-coding-ai-agent-docs-self-serve-api-keys), [register an app](https://developers.soundcloud.com/docs/api/register-app), [API Terms of Use](https://developers.soundcloud.com/docs/api/terms-of-use)
- YouTube: [Required Minimum Functionality](https://developers.google.com/youtube/terms/required-minimum-functionality)
