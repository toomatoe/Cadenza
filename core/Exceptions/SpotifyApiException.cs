namespace Cadenza.Core.Exceptions;

public class SpotifyApiException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
