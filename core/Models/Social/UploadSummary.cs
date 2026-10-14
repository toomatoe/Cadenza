namespace Cadenza.Core.Models.Social;

/// <summary>The outcome of adding songs to the shared library.</summary>
public sealed class UploadSummary
{
    public UploadSummary(int added, IReadOnlyList<string> skipped)
    {
        Added = added;
        Skipped = skipped;
    }

    /// <summary>Songs now in the library (including ones that were already there).</summary>
    public int Added { get; }

    /// <summary>"file name (reason)" for each file that wasn't added.</summary>
    public IReadOnlyList<string> Skipped { get; }
}
