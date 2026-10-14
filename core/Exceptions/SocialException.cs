namespace Cadenza.Core.Exceptions;

/// <summary>A request the Cadenza server rejected, with a message safe to show the user.</summary>
public sealed class SocialException : Exception
{
    public SocialException()
    {
    }

    public SocialException(string message)
        : base(message)
    {
    }

    public SocialException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SocialException(int statusCode, string message)
        : base(message) => StatusCode = statusCode;

    /// <summary>The HTTP status, or 0 when the server couldn't be reached.</summary>
    public int StatusCode { get; }

    /// <summary>The device token is no longer accepted (account deleted or token revoked).</summary>
    public bool IsSignedOut => StatusCode == 401;
}
