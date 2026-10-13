namespace Cadenza.Core.Recommendations;

/// <summary>
/// How much the listener likes each artist, built from their own data by <see cref="TasteProfile.Build"/>.
/// Plain weighted counts with time decay; nothing is learned or trained.
/// </summary>
public sealed class TasteProfile
{
    // Weights for each signal (see docs/features/recommendations.md, "Algorithm as built").
    public const double SavedTrackWeight = 1.0;
    public const double RecentPlayWeight = 0.4;
    public const double TopTrackArtistShare = 0.75;
    public const double LikedWeight = 2.0;
    public const double CompletedWeight = 0.5;
    public const double SkippedWeight = -0.75;
    public const double NotInterestedArtistWeight = -1.5;

    public static readonly TimeSpan SavedHalfLife = TimeSpan.FromDays(120);
    public static readonly TimeSpan RecentHalfLife = TimeSpan.FromDays(14);
    public static readonly TimeSpan EventHalfLife = TimeSpan.FromDays(60);

    private static readonly Dictionary<TimeRange, double> s_rangeWeights = new()
    {
        [TimeRange.ShortTerm] = 3.0,
        [TimeRange.MediumTerm] = 2.0,
        [TimeRange.LongTerm] = 1.0,
    };

    private readonly Dictionary<string, double> _affinity;
    private readonly Dictionary<string, string> _displayNames;
    private readonly double _maxAffinity;

    private TasteProfile(
        Dictionary<string, double> affinity,
        Dictionary<string, string> displayNames,
        HashSet<string> knownTrackIds,
        HashSet<string> rejectedTrackIds)
    {
        _affinity = affinity;
        _displayNames = displayNames;
        KnownTrackIds = knownTrackIds;
        RejectedTrackIds = rejectedTrackIds;
        _maxAffinity = affinity.Values.Where(value => value > 0).DefaultIfEmpty(0).Max();
    }

    /// <summary>Tracks the listener already has (saved, top, recently played, liked); never recommended.</summary>
    public IReadOnlySet<string> KnownTrackIds { get; }

    /// <summary>Tracks marked "Not interested"; never recommended.</summary>
    public IReadOnlySet<string> RejectedTrackIds { get; }

    public bool IsEmpty => _maxAffinity <= 0;

    /// <summary>Normalises an artist name for matching across Spotify, MusicBrainz and ListenBrainz.</summary>
    public static string Key(string artistName) => artistName.Trim().ToUpperInvariant();

    /// <summary>Builds the profile. <paramref name="now"/> is passed in so results are reproducible.</summary>
    public static TasteProfile Build(TasteInputs inputs, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var affinity = new Dictionary<string, double>();
        var names = new Dictionary<string, string>();
        var known = new HashSet<string>(StringComparer.Ordinal);
        var rejected = new HashSet<string>(StringComparer.Ordinal);

        void Add(string artistName, double amount)
        {
            if (string.IsNullOrWhiteSpace(artistName))
            {
                return;
            }

            var key = Key(artistName);
            affinity[key] = affinity.GetValueOrDefault(key) + amount;
            names.TryAdd(key, artistName.Trim());
        }

        foreach (var saved in inputs.SavedTracks)
        {
            Add(saved.Track.LeadArtist, SavedTrackWeight * Decay(now - saved.At, SavedHalfLife));
            known.Add(saved.Track.Id);
        }

        foreach (var (range, artists) in inputs.TopArtists)
        {
            for (var rank = 0; rank < artists.Count; rank++)
            {
                Add(artists[rank].Name, s_rangeWeights[range] * RankFactor(rank, artists.Count));
            }
        }

        foreach (var (range, tracks) in inputs.TopTracks)
        {
            for (var rank = 0; rank < tracks.Count; rank++)
            {
                Add(tracks[rank].LeadArtist, TopTrackArtistShare * s_rangeWeights[range] * RankFactor(rank, tracks.Count));
                known.Add(tracks[rank].Id);
            }
        }

        foreach (var played in inputs.RecentlyPlayed)
        {
            Add(played.Track.LeadArtist, RecentPlayWeight * Decay(now - played.At, RecentHalfLife));
            known.Add(played.Track.Id);
        }

        foreach (var listeningEvent in inputs.Events)
        {
            var decay = Decay(now - listeningEvent.At, EventHalfLife);
            switch (listeningEvent.Kind)
            {
                case ListeningEventKind.Liked:
                    Add(listeningEvent.ArtistName, LikedWeight * decay);
                    known.Add(listeningEvent.TrackId);
                    break;
                case ListeningEventKind.Completed:
                    Add(listeningEvent.ArtistName, CompletedWeight * decay);
                    break;
                case ListeningEventKind.Skipped:
                    Add(listeningEvent.ArtistName, SkippedWeight * decay);
                    break;
                case ListeningEventKind.NotInterested:
                    Add(listeningEvent.ArtistName, NotInterestedArtistWeight * decay);
                    rejected.Add(listeningEvent.TrackId);
                    break;
            }
        }

        return new TasteProfile(affinity, names, known, rejected);
    }

    /// <summary>Raw affinity: positive means liked, negative means skipped or rejected, 0 means unknown.</summary>
    public double Affinity(string artistName) => _affinity.GetValueOrDefault(Key(artistName));

    /// <summary>Affinity scaled to 0–1 against the listener's favourite artist; negatives become 0.</summary>
    public double NormalizedAffinity(string artistName) =>
        _maxAffinity <= 0 ? 0 : Math.Clamp(Affinity(artistName) / _maxAffinity, 0, 1);

    /// <summary>The listener's favourite artists, highest affinity first.</summary>
    public IReadOnlyList<(string Name, double Weight)> TopArtists(int count) =>
        _affinity.Where(pair => pair.Value > 0)
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(count)
            .Select(pair => (_displayNames[pair.Key], pair.Value / _maxAffinity))
            .ToArray();

    /// <summary>Exponential decay: 1 now, 0.5 after one half-life.</summary>
    public static double Decay(TimeSpan age, TimeSpan halfLife) =>
        age <= TimeSpan.Zero ? 1 : Math.Pow(0.5, age / halfLife);

    /// <summary>Linear rank weight from 1 for the top item down to 0.2 for the last.</summary>
    public static double RankFactor(int rank, int count) => count <= 1 ? 1 : 1 - (0.8 * rank / (count - 1));
}
