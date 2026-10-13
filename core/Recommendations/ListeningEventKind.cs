namespace Cadenza.Core.Recommendations;

/// <summary>Things the listener does in Cadenza that steer recommendations.</summary>
public enum ListeningEventKind
{
    /// <summary>A track played to the end.</summary>
    Completed,

    /// <summary>A track skipped within its first 30 seconds.</summary>
    Skipped,

    /// <summary>A track liked in Cadenza.</summary>
    Liked,

    /// <summary>"Not interested" on a recommendation.</summary>
    NotInterested
}
