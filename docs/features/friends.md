# Friends tab (design proposal)

**Status:** proposal; nothing built yet. This is the largest of the planned features because it needs a server. Review the open questions at the end before implementation starts.

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

- **Server:** ASP.NET Core minimal APIs + **SignalR** for real-time chat, presence and sync. C# end to end, sharing models with `core/`. Hosting options: Azure App Service, Fly.io, or a small VPS. Costs to be estimated.
- **Database:** PostgreSQL holding users, friendships (pending, accepted, blocked), conversations, messages, share payloads (Spotify ID + type) and reports.
- **Accounts:** a Cadenza account separate from Spotify, via **passkeys** or an email magic link. **Don't send users' Spotify tokens to the server.** The display name and profile sprite are chosen in Cadenza.
- **Friend discovery:** add by username or by invite link or code; there's no contact-list upload.
- **Messages:** TLS in transit, encrypted at rest. End-to-end encryption is a later option (it complicates moderation and multi-device sync).
- **Client:** a `core/Social/` library (API client, SignalR connection, models) and a desktop `AppView.Friends` page with a `MainWindow.Friends.cs` partial.

### Listen together (sync design sketch)

- The host's client publishes `{trackId, positionMs, playing, serverTimestamp}` on every change and as a heartbeat every 5 s.
- Followers load the same track through their own engine and seek to `positionMs + (now − serverTimestamp)`. They re-sync when drift exceeds about 1.5 s.
- Followers who aren't Premium, or can't play the track in their region, see a message and can still chat.

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

1. **Hosting and cost:** are you OK running and paying for a small server? Who operates it and handles reports?
2. **Accounts:** passkeys, email magic link, or both?
3. **Audience:** just you and friends for now (simpler: invite-only), or open sign-up?
4. **Encryption:** is TLS plus encryption at rest acceptable for the first release, with end-to-end encryption later?
5. **Onboarding friction:** each friend needs Spotify Premium and their own developer Client ID. Is that acceptable for the intended audience?

## Sources

- Development Mode changes: [TechCrunch](https://techcrunch.com/?p=3090291), [Spotify Community, Feb 2026 thread](https://community.spotify.com/t5/Spotify-for-Developers/February-2026-Spotify-for-Developers-update-thread/m-p/7368237/highlight/true)
- ML/AI clause: [Spotify Community discussion](https://community.spotify.com/t5/Spotify-for-Developers/Clarification-on-using-Spotify-data-with-AI-ML-models/td-p/6033126); authoritative text: developer.spotify.com/policy
