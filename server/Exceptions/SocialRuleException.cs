namespace Cadenza.Server.Exceptions;

/// <summary>A request that breaks a rule; becomes an HTTP error with a message safe to show the user.</summary>
public sealed class SocialRuleException : Exception
{
    public SocialRuleException()
    {
    }

    public SocialRuleException(string message)
        : base(message)
    {
    }

    public SocialRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public SocialRuleException(int statusCode, string message)
        : base(message) => StatusCode = statusCode;

    public int StatusCode { get; } = StatusCodes.Status400BadRequest;
}
