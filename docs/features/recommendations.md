# Recommendations tab (design proposal)

**Status:** proposal; nothing built yet. Review the open questions at the end before implementation starts.

## Goal

A **Recommendations** page that suggests tracks the listener is likely to enjoy, explains *why* each one was suggested, and lets the listener steer how adventurous it is.

**No generative AI.** The engine is a transparent, deterministic scoring algorithm: every suggestion can be traced to the signals and weights that produced it.

## Constraints that shape the design

| Constraint | Effect |
| --- | --- |
| Spotify closed `/recommendations`, `/artists/{id}/related-artists`, `/audio-features` and `/audio-analysis` to apps created after 27 Nov 2024. | We can't ask Spotify for recommendations, similar artists or audio features (tempo, energy, valence). |
| Spotify Developer Policy (clause 14): don't use Spotify content to train an ML or AI model, or ingest it into one. | No learned models on Spotify data. Plain counting, scoring and ranking on the user's own data, done on their device, is the intended approach. **To confirm against the current policy text before shipping.** |
| Development Mode (Feb 2026): Premium required, 5 users per app, and some endpoints (artist popularity, follower counts, artist top tracks, new releases) are restricted. | Don't depend on popularity or top-tracks endpoints. Each Cadenza user brings their own Client ID, so the 5-user cap applies per user's app. |
| Listening data is personal. | All scoring runs locally. Nothing about the user's taste leaves the device unless they opt into a feature that needs it (e.g. Friends). |

## How recommendations work today (research summary)

- **Collaborative filtering** ("people who like X also like Y"): matrix factorization or neighbour methods over many users' listening. Strong, but needs large multi-user data we don't have and may not train on.
- **Content-based:** similarity from audio features, genres, tags or lyrics. No longer available from Spotify for new apps; open metadata (MusicBrainz tags and genres) is a partial substitute.
- **Sequence and context models:** next-track prediction from sessions (e.g. word2vec-style embeddings over playlists). Learned models, so out of scope.
- **Bandits / explore-exploit:** reserve a slice of slots for discovery and learn from the response.
- **Known problems** with mainstream systems: popularity bias, filter bubbles and repetitive suggestions, feedback loops, no explanation, and no user control.

**Where Cadenza can be better:** explain every pick, let the listener control discovery, enforce diversity, respect "not this", and keep data on the device.

## Signals (all from the user's own account or Cadenza use)

| Signal | Source | Notes |
| --- | --- | --- |
| Saved tracks, with `added_at` | `me/tracks` (existing scope) | Recency-weighted affinity. |
| Top artists and tracks (short, medium, long term) | `me/top/{type}` | Needs the `user-top-read` scope, so a one-time reconnect. |
| Recently played | `me/player/recently-played` | Needs `user-read-recently-played`. |
| Playlist membership | `me/playlists`, playlist items | Co-occurrence: tracks and artists that share the user's playlists. |
| In-app plays, skips, likes, "not interested" | Cadenza's local event log | The strongest signal for steering; bounded and kept local. |
| Track identifiers (ISRC) | Track `external_ids.isrc` | Maps Spotify tracks to MusicBrainz recordings for open similarity data. |

## Candidate sources (where suggestions come from)

1. **Open similarity graphs:** ListenBrainz similar-artist and similar-recording datasets (open data), joined via MusicBrainz IDs (ISRC → MusicBrainz recording → artist).
2. **Last.fm** `artist.getSimilar` / `track.getSimilar`, as an optional second graph. **Check the Last.fm API terms** before relying on it.
3. **The user's own graph:** artists and tracks that co-occur in the user's playlists but aren't saved yet.
4. **Spotify search:** turns candidate artist and track names into playable Spotify IDs (e.g. `artist:"Name" track:"Title"`).

## Scoring

For each candidate track *c*:

```
score(c) =  w_sim · Σ_seeds  affinity(seed) · similarity(seed, c)      // close to what you love
          + w_aff · artistAffinity(c.artist)                          // you already like this artist
          + w_new · novelty(c)          · discovery                   // unfamiliar, scaled by the dial
          - w_rep · recentlyHeardPenalty(c)                           // heard or suggested recently
          - w_neg · negativeFeedback(c)                               // skipped, "not interested"
```

- **Affinity** decays with time (e.g. 30-day half-life), with likes and saves weighted above plays and skips counting against.
- **Discovery dial** (0–1, default 0.3): blends familiar against new artists. This is the user-facing control.
- **Re-ranking for diversity:** maximal marginal relevance, so the list isn't ten tracks by one artist. It reuses the queue's artist-spacing idea.
- **Exploration:** a fixed share of slots (e.g. 2 in 20) goes to lower-scored but novel candidates, to avoid a filter bubble.
- **Weights** are named constants with sensible defaults, tunable in an "advanced" panel. Adjusting counters and weights from feedback is arithmetic, not model training.

Each suggestion keeps its top contributing term as a **reason**: "Because you saved *X*", "Similar to *Artist* (ListenBrainz)", "New to you", "In your playlist *Y*".

## Page design (follows docs/UI-GUIDE.md)

- A glass header card with the **discovery dial** and a **Refresh** button.
- Shelves such as **"More like your recent favourites"**, **"Deeper cuts from artists you love"** and **"Something new"**, each a row of the standard 208px cards with a one-line reason caption.
- Per card: Play, Add to queue, Like and "Not interested" (Fluent icons), and a "Why?" tooltip with the score breakdown.
- An empty state for new accounts: "Listen to a few tracks and we'll start suggesting."

## Architecture

- `core/Recommendations/`: `RecommendationEngine` (pure, testable scoring and re-ranking), `ListeningSignals` (aggregated affinities), `ICandidateSource` implementations (ListenBrainz, Last.fm, playlist co-occurrence), `SpotifyTrackResolver` (search → playable ID).
- `desktop/`: a new `AppView.Recommendations` page and a `MainWindow.Recommendations.cs` partial.
- A local, bounded event log (plays, skips, feedback) in `%LOCALAPPDATA%\Cadenza`.
- Caching of similarity lookups with a TTL, plus request throttling for ListenBrainz, Last.fm and Spotify.

## Evaluation

- **Offline, on the device:** hold out the user's most recent saves and measure how many the engine ranks in its top 50 (hit rate, recall@k), plus diversity (distinct artists) and novelty.
- **In use:** save rate, play-through rate and skip rate of suggested tracks, shown on a debug panel and never uploaded.

## Phases

1. Signals and the local event log; resolving Spotify tracks to MusicBrainz IDs.
2. Candidate sources (playlist co-occurrence first, then ListenBrainz).
3. Scoring, re-ranking, reasons, plus unit tests with fixed fixtures.
4. The Recommendations page and the discovery dial.
5. Feedback loop and the evaluation panel.

## Open questions

1. Is a one-time reconnect for the `user-top-read` and `user-read-recently-played` scopes acceptable?
2. Are third-party sources (ListenBrainz, optionally Last.fm) fine, given they need network calls carrying artist and track names, though no user identity?
3. Should recommendations also draw on friends' listening, once the Friends feature exists (opt-in)?
4. Confirm the reading of Spotify Developer Policy clause 14 for local, non-ML scoring before release.

## Sources

- Spotify endpoint changes for new apps: [Techzine](https://www.techzine.eu/news/applications/126612/spotify-is-restricting-developers-api-access/), [Spotify Community](https://community.spotify.com/t5/Spotify-for-Developers/Spotify-API-recommendations-Endpoint-Always-Returns-404-Even/m-p/6918309)
- ML/AI clause: [Spotify Community discussion](https://community.spotify.com/t5/Spotify-for-Developers/Clarification-on-using-Spotify-data-with-AI-ML-models/td-p/6033126), [Hypebot](https://www.hypebot.com/updated-spotify-terms-mean-you-cant-use-their-data-but-they-can-use-yours/); authoritative text: developer.spotify.com/policy
- Development Mode changes: [TechCrunch](https://techcrunch.com/?p=3090291), [Spotify Community, Feb 2026 thread](https://community.spotify.com/t5/Spotify-for-Developers/February-2026-Spotify-for-Developers-update-thread/m-p/7368237/highlight/true)
- Open similarity data: [ListenBrainz API reference](https://liblistenbrainz.readthedocs.io/en/stable/api_ref.html), [Troi (ListenBrainz recommendation engine)](https://pypi.org/project/troi/2024.1.30.0), [Last.fm track.getSimilar](https://www.last.fm/api/show/track.getSimilar)
