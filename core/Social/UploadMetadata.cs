namespace Cadenza.Core.Social;

/// <summary>Song details sent with an upload, read from the file's tags (or its name) on the uploader's PC.</summary>
public sealed record UploadMetadata(string Title, string Artist, string Album, int DurationMs);
