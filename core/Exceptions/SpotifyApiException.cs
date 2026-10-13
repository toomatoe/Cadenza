namespace Cadenza.Core.Exceptions;

/// <summary>A Spotify request failed. <see cref="Exception.Message"/> is safe to show to the user.</summary>
public sealed class SpotifyApiException : Exception
{
    public SpotifyApiException()
    {
    }

    public SpotifyApiException(string message)
        : base(message)
    {
    }

    public SpotifyApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SpotifyApiException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>The HTTP status code Spotify returned, or 0 if none.</summary>
    public int StatusCode { get; }
}
