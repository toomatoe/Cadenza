# Music from other sources (design proposal)

**Status:** proposal; nothing built yet. Review the open questions at the end before implementation starts.

## Goal

Let Cadenza play music that isn't on Spotify, such as **your own files** and **SoundCloud**, in the same library, queue and player, so a session can mix sources freely.

## What's possible, source by source

| Source | Verdict | Why |
| --- | --- | --- |
| **Local files** (MP3, FLAC, AAC/M4A, OGG/Opus, WAV) | **Yes, first.** | Your own files; no terms to break, works offline, no API keys. |
| **SoundCloud** | **Yes, with conditions.** | API sign-up reopened in May 2026, but **only for Artist Pro subscribers**, who self-serve a Client ID and secret. Use is bound by the SoundCloud API Terms of Use, which can change at any time. Some tracks set `streamable = false` and can't be played through the API. Each Cadenza user would bring their own SoundCloud credentials, as with Spotify today. |
| **YouTube / YouTube Music** | **No playback.** | YouTube's API rules centre on its own embedded player ("Required Minimum Functionality"). Extracting or downloading audio streams, which is how unofficial players work (e.g. yt-dlp), breaks YouTube's terms. An embedded web player would contradict Cadenza's "no embedded browser" principle and the audio-first design. YouTube Music has no public API. **Allowed:** "Open on YouTube" links in share cards. |
| **Bandcamp** | **Links only.** | No public API for playback. Purchased downloads can be played as local files. |
| **Free and open catalogues** (Jamendo, Internet Archive, Free Music Archive) | **Maybe later.** | Creative-Commons music with public APIs; check each API's terms. |
| **Internet radio** (Radio Browser directory) | **Maybe later.** | An open directory of public streams; easy to add once HTTP streaming exists. |

## Architecture

Today every track is a Spotify track played by librespot. The change is to make sources pluggable.

- **Track identity:** `Track` gains a `Source` (Spotify, Local, SoundCloud) and a source-specific `SourceId` (Spotify ID, file path, SoundCloud track ID). Spotify-only fields become optional. The queue, library and player work with any source.
- **Providers (C#):** `IMusicProvider` with search, library listing and "resolve to playable", implemented by `SpotifyProvider` (the existing `SpotifyApi`), `LocalLibraryProvider` and `SoundCloudProvider`. Recommendations and Friends address tracks by `(Source, SourceId)`.
- **Playback (Rust engine):** add a **non-Spotify playback path** next to librespot:
  - **Decoding:** [Symphonia](https://github.com/pdeljanov/Symphonia) (pure Rust; MP3, FLAC, AAC, Vorbis, Opus, WAV), output through the existing **rodio** sink, sharing volume, seek and the play/pause/end events.
  - **Local files:** decode from disk.
  - **SoundCloud:** stream from the HTTP or HLS URL the API returns. HLS needs a small playlist-and-segment fetcher feeding the decoder.
  - **ABI:** a new opcode such as `LoadUri` (`file:///…` or `https://…`), keeping ABI-version checks in place. Spotify keeps using librespot.
- **Local library:** the user picks music folders in Settings. Cadenza scans and indexes tags (title, artist, album, artwork, duration) into a local index, and watches for changes. Tag reading via TagLib# (C#) or [lofty](https://github.com/Serial-ATA/lofty-rs) (Rust).
- **Credentials:** SoundCloud tokens go in Windows Credential Manager, like Spotify's.

## UI (follows docs/UI-GUIDE.md)

- A small **source badge** (Spotify, file, SoundCloud) on cards and track rows; the artwork style stays the same. Local files without embedded art use the record placeholder.
- **Library:** a source filter next to All / Liked (All sources, Spotify, Local, SoundCloud).
- **Search:** results grouped or filtered by source.
- **Settings → Sources:** music folders (add, remove, rescan) and SoundCloud connect/disconnect, explaining the Artist Pro requirement.
- The player bar and queue look the same whatever the source.

## Phases

1. **Track model and provider abstraction**, keeping Spotify behaviour unchanged (covered by tests).
2. **Local files:** folder picker, scanner and index; a Symphonia decoding path in the Rust engine; playback, seek and queue across sources.
3. **SoundCloud:** connect, search, likes, streaming (HTTP then HLS), handling of `streamable = false`.
4. Later: open catalogues and internet radio.

## Open questions

1. Start with **local files**, then SoundCloud?
2. **SoundCloud** needs each user to pay for **Artist Pro** to get API credentials. Is that acceptable, or should SoundCloud wait?
3. Agree that **YouTube is links-only** (no playback)?
4. Can local files be shared with **Friends**? Only as metadata, never the files themselves. Friends with the same track available on Spotify could play it there.

## Sources

- SoundCloud API access: [SoundCloud developer blog (May 2026)](https://developers.soundcloud.com/blog/vibe-coding-ai-agent-docs-self-serve-api-keys), [Register an app](https://developers.soundcloud.com/docs/api/register-app), [API sign-up changes](https://developers.soundcloud.com/blog/api-sign-up-changes), [API Terms of Use](https://developers.soundcloud.com/docs/api/terms-of-use), [Developer policies](https://developers.soundcloud.com/policies)
- YouTube: [YouTube API Services – Required Minimum Functionality](https://developers.google.com/youtube/terms/required-minimum-functionality). The full API Terms of Service and Developer Policies should be read before any YouTube integration.
