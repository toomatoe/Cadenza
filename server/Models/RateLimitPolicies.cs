namespace Cadenza.Server.Models;

/// <summary>Rate-limit policy names and limits. Partitioned per device token, or per client address when signed out.</summary>
public static class RateLimitPolicies
{
    /// <summary>Sending messages.</summary>
    public const string Send = "send";

    /// <summary>Joining, invites, friend requests and reports.</summary>
    public const string Slow = "slow";

    /// <summary>Uploading songs to the shared library.</summary>
    public const string Upload = "upload";

    /// <summary>Messages a device may send per minute.</summary>
    public const int MessagesPerMinute = 30;

    /// <summary>Slow actions a device may take per hour.</summary>
    public const int SlowActionsPerHour = 20;

    /// <summary>Songs a device may upload per hour.</summary>
    public const int UploadsPerHour = 200;
}
