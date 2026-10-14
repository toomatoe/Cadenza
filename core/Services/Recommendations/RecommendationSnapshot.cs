using System.Text.Json;
using Cadenza.Core.Models.Recommendations;

namespace Cadenza.Core.Services.Recommendations;

/// <summary>
/// The last set of suggestions, saved so the "For you" page opens instantly instead of recomputing.
/// Snapshots older than <see cref="FreshFor"/> are ignored; Refresh always recomputes.
/// </summary>
public static class RecommendationSnapshot
{
    public static readonly TimeSpan FreshFor = TimeSpan.FromHours(12);

    public static void Save(string path, RecommendationResult result, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(result);
        var snapshot = new Snapshot(now, result.Items.Select(item => new Item(TrackRecord.From(item.Track), item.Shelf, item.Reason, item.Score)).ToArray());
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
            File.Move(temporary, path, overwrite: true);
        }
        catch (IOException)
        {
            // Only saves a wait next time.
        }
    }

    /// <returns>The saved suggestions and when they were made, or null if missing, unreadable or stale.</returns>
    public static (RecommendationResult Result, DateTimeOffset MadeAt)? Load(string path, DateTimeOffset now)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var snapshot = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path));
            if (snapshot is null || now - snapshot.MadeAt > FreshFor || snapshot.Items.Length == 0)
                return null;

            var items = snapshot.Items.Select(item => new Recommendation(item.Track.ToTrack(), item.Shelf, item.Reason, item.Score)).ToArray();
            return (new RecommendationResult(items, []), snapshot.MadeAt);
        }
        catch (Exception error) when (error is IOException or JsonException)
        {
            return null;
        }
    }

    public static void Delete(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }

    private sealed class Snapshot
    {
        public Snapshot(DateTimeOffset madeAt, Item[] items)
        {
            MadeAt = madeAt;
            Items = items;
        }

        public DateTimeOffset MadeAt { get; }

        public Item[] Items { get; }
    }
    private sealed class Item
    {
        public Item(TrackRecord track, RecommendationShelf shelf, string reason, ScoreBreakdown score)
        {
            Track = track;
            Shelf = shelf;
            Reason = reason;
            Score = score;
        }

        public TrackRecord Track { get; }

        public RecommendationShelf Shelf { get; }

        public string Reason { get; }

        public ScoreBreakdown Score { get; }
    }}
