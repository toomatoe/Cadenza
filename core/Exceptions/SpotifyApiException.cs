namespace Cadenza.Core.Exceptions;

public sealed class SpotifyApiException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
