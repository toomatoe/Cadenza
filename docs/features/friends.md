# Friends tab

**Status:** first release built (phases 1–3). See "What was built"; the sections around it are the original proposal.

## Goal

A **Friends** page where listeners add friends, chat, and do music things together, so that sharing music feels like playing, not just sending links.

## What makes it Cadenza (feature ideas)

Ranked by how distinctive they are versus how hard:

| Idea | What it is | Effort |
| --- | --- | --- |
| **Share cards** | Tracks, albums and playlists appear in chat as glass cards with Play, Add to queue and Open. | Low |
| **Queue gifts** | Send a track straight into a friend's Up next (they choose whether to allow it: from anyone, friends only, or ask first). | Low |
| **Now listening** | An opt-in status showing what a friend is playing, with their profile sprite bobbing along. | Low–medium |
| **Listen together** | One friend hosts and others follow: each client plays the same track on its own Premium account, kept in sync by the server (track, position, play/pause). | Medium–high |
| **Mixtape exchange** | A weekly prompt to trade a five-track mixtape with a note; it unlocks on a set day. | Medium |
| **Blind listen** | A game: a friend sends a track with the title hidden, and you guess the artist or rate it before it's revealed. | Medium |
| **Taste match** | A compatibility score from overlapping artists, computed so neither side sees the other's full history. | Medium |

**Suggested first release:** friends list, one-to-one chat, share cards, queue gifts. Then "now listening", then "listen together".

## Constraints that shape the design

| Constraint | Effect |
| --- | --- |
| Spotify has **no social or messaging API**. | Cadenza needs its own server for accounts, friendships, messages and real-time sync. |
| **Development Mode** (Feb 2026): Premium required, 5 users per app, one Client ID per developer. | Each friend runs Cadenza with their own Spotify developer app, as today. That's real onboarding friction; the guide must be very clear. |
| Spotify content policies limit **storing or caching Spotify content**. | The server stores only **Spotify IDs** (track, album, playlist). Each client fetches names and artwork itself through its own Spotify session. |
| Spotify Developer Policy forbids using Spotify content to train or feed ML or AI models. | No AI features over chat or listening data. |
| Social features attract **abuse**. | Blocking, reporting, rate limits and privacy defaults are part of the first release, not a later add-on. |

## Architecture

```
Cadenza desktop (WinUI)  ── HTTPS + WebSocket (SignalR) ──►  Cadenza server (ASP.NET Core)
   │  own Spotify session                                        │ accounts, friends, messages,
   │  hydrates IDs → names/art                                   │ presence, listen-together rooms
   ▼                                                             ▼
 Spotify Web API / librespot                                  PostgreSQL
```

- **Server:** ASP.NET Core minimal APIs + **SignalR** for real-time chat, presence and sync. C# end to end, sharing models with `core/`. Hosted on the owner's Raspberry Pi (see below).
- **Database:** PostgreSQL holding users, friendships (pending, accepted, blocked), conversations, messages, share payloads (Spotify ID + type) and reports.
- **Accounts:** a Cadenza account separate from Spotify, via **passkeys** or an email magic link. **Don't send users' Spotify tokens to the server.** The display name and profile sprite are chosen in Cadenza.
- **Friend discovery:** add by username or by invite link or code; there's no contact-list upload.
- **Messages:** TLS in transit, encrypted at rest. End-to-end encryption is a later option (it complicates moderation and multi-device sync).
- **Client:** a `core/Social/` library (API client, SignalR connection, models) and a desktop `AppView.Friends` page with a `MainWindow.Friends.cs` partial.

### Hosting: the owner's Raspberry Pi (decided)

The server runs on the owner's **Raspberry Pi (16 GB RAM, 128 GB storage)**. That is plenty for an invite-only group: chat, presence and listen-together sync are small, infrequent messages, and audio never passes through the server (each client streams from Spotify itself).

| Concern | Plan |
| --- | --- |
| **OS and runtime** | 64-bit Raspberry Pi OS (or Ubuntu Server arm64) with the .NET 10 ARM64 runtime. The server is published as a framework-dependent `linux-arm64` app. |
| **Reaching it from the internet** | **Cloudflare Tunnel** (`cloudflared`): an outbound-only connection, so **no ports are opened on the home router** and the home IP isn't exposed. Cloudflare provides HTTPS; WebSockets (SignalR) pass through. Needs a domain on Cloudflare (a cheap one is fine). Alternative: Tailscale Funnel. |
| **Kestrel** | Listens on `localhost` only; the tunnel forwards to it. Use the forwarded-headers middleware for the real scheme and client IP. Keep SignalR keep-alives under Cloudflare's idle timeout. |
| **Database** | PostgreSQL. **Store its data on a USB SSD if possible**: database writes wear out SD cards. SQLite is an acceptable first step if setup simplicity matters more. |
| **Process management** | `systemd` units for the server and `cloudflared`, restarting on failure and starting on boot. Docker Compose is an option, not a requirement. |
| **Backups** | Nightly `pg_dump`, kept for 14 days, copied off the Pi (another disk or cloud storage). Test restoring once. |
| **Updates and security** | Unattended OS security updates; SSH by key only (or SSH only over Tailscale); the server runs as an unprivileged user; dependencies updated with each release. |
| **Availability** | Home power or internet outages take the service offline. The client must degrade gracefully: music keeps working, chat shows "offline", and messages retry. |
| **Limits** | Home upload bandwidth and a single machine. Fine for tens of users; revisit before any public launch. |

Deployment gets its own guide: `server/DEPLOY-RASPBERRY-PI.md`.

### Listen together (sync design sketch)

- The host's client publishes `{trackId, positionMs, playing, serverTimestamp}` on every change and as a heartbeat every 5 s.
- Followers load the same track through their own engine and seek to `positionMs + (now − serverTimestamp)`. They re-sync when drift exceeds about 1.5 s.
- Followers who aren't Premium, or can't play the track in their region, see a message and can still chat.

## What was built (first release)

Phases 1–3 are built: accounts, friend lists, one-to-one chat, share cards, queue gifts, block and report.

| Area | As built | Changed from the proposal because |
| --- | --- | --- |
| Real-time | Plain **WebSockets** (`/api/live`), one per device, with 30 s keep-alives and client reconnect backoff (2 s doubling to 60 s). | SignalR's client package would add weight to the desktop app; `ClientWebSocket` is built into .NET. |
| Database | **SQLite** in one file, through `Microsoft.Data.Sqlite.Core`, with no ORM. WAL mode, foreign keys and cascade deletes. | No database server to run on the Pi; plenty for an invite-only group. |
| Accounts | **Invite code + device token.** Joining needs a single-use invite (valid 7 days, at most 5 open per person). The server returns a random 256-bit token, stores only its SHA-256 hash, and the app keeps the token in Windows Credential Manager. Whoever invited you becomes your first friend. The first account uses a bootstrap invite printed in the server log. | Passkeys or magic links need an identity or email service. Invite-only matches "you and your friends". |
| Server | ASP.NET Core minimal API (`server/`), with contracts shared from `core/Social`. Built-in rate limiting: 30 messages per minute and 20 slow actions (joins, invites, friend requests, reports) per hour. | — |
| Client | `core/Social` (`SocialClient`, `SocialConnection`) and the desktop Friends page (`MainWindow.Friends.cs`). It connects at startup only if you've joined, and adds **no NuGet packages** to the app. | — |
| Shares | Messages carry only a Spotify track ID. Each client looks up the name and artwork with its own Spotify session (`SpotifyApi.TrackAsync`) and caches up to 200 tracks for the session. | — |

**Safety, as built:** only friends can message or read a conversation. Blocking removes the friendship, stops messages both ways, and makes you look like a username that doesn't exist. Reports store a copy of the message for the owner to review (see `server/DEPLOY-RASPBERRY-PI.md`). Queue gifts default to **ask first**. "Leave Cadenza Friends" deletes the account and everything tied to it. Message text is capped at 2,000 characters.

**Not built yet:** "now listening" presence and sprites in the friends list (phase 4), listen together (phase 5), and the games (phase 6).

**Tests:** `tests/Suites/SocialChecks.cs` starts the real server on a random local port with a temporary database, and drives it with the real client and WebSocket. It covers invites, friend requests, live delivery, history paging, validation, reports, blocks, sign-out and account deletion.

**SQLite dependency:** the earlier 2.1.11 bundled SQLite package was flagged for CVE-2025-6965. The Pi deployment branch now uses `SQLitePCLRaw.bundle_e_sqlite3` 2.1.14, which is outside the advisory's affected version range, and removes the audit suppression. Keep NuGet audit enabled for future updates.

## Page design (follows docs/UI-GUIDE.md)

- **Left column:** a glass list of friends with their profile sprites, names and opt-in "now listening" line; requests at the top.
- **Right column:** the conversation, with message bubbles on glass and share cards using the standard artwork tile and caption band. A composer has a "Share" button that searches your library.
- A **Requests and invites** panel, and a **privacy** section in Settings: who can message you, who can send queue gifts, whether to show "now listening".

## Safety and privacy defaults

- New accounts: messages from friends only; queue gifts set to "ask first"; "now listening" off.
- Block (hides the user both ways) and report (stored with the message for review) on every conversation.
- Rate limits on friend requests and messages; link previews only for Spotify IDs.
- Minimum age per the terms of service; a way to delete your account and data from the app.

## Phases

1. **Spike:** server skeleton (auth, users, friendships) with integration tests; a desktop client that can sign in and list friends.
2. One-to-one chat over SignalR, share cards (ID-only payloads, hydrated on the client), block and report.
3. Queue gifts with permission settings.
4. "Now listening" presence and sprites (depends on the Profile sprite feature).
5. Listen together.
6. Mixtapes, blind listen, taste match.

## Open questions

1. ~~**Hosting:**~~ **Decided: the owner's Raspberry Pi** behind Cloudflare Tunnel. Still open: who reviews reports (assumed: the owner)?
2. ~~**Accounts:**~~ **Decided for now:** invite code + device token (see "What was built"). Passkeys can come later.
3. ~~**Audience:**~~ **Decided:** invite-only.
4. **Encryption:** TLS in transit (Cloudflare), optional LUKS at rest. End-to-end encryption remains a later option.
5. **Onboarding friction:** each friend needs Spotify Premium and their own developer Client ID. Is that acceptable for the intended audience?

## Sources

- Development Mode changes: [TechCrunch](https://techcrunch.com/?p=3090291), [Spotify Community, Feb 2026 thread](https://community.spotify.com/t5/Spotify-for-Developers/February-2026-Spotify-for-Developers-update-thread/m-p/7368237/highlight/true)
- ML/AI clause: [Spotify Community discussion](https://community.spotify.com/t5/Spotify-for-Developers/Clarification-on-using-Spotify-data-with-AI-ML-models/td-p/6033126); authoritative text: developer.spotify.com/policy
