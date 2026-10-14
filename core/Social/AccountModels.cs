namespace Cadenza.Core.Social;

/// <summary>A Cadenza Friends account as other people see it. Unrelated to the Spotify account.</summary>
public sealed class SocialUser
{
    // Get-only properties set by the constructor, like Track: WinUI's metadata generator treats
    // init-only setters as regular setters. System.Text.Json binds through the constructor.
    public SocialUser(string id, string username, string displayName)
    {
        Id = id;
        Username = username;
        DisplayName = displayName;
    }

    public string Id { get; }

    public string Username { get; }

    public string DisplayName { get; }
}

/// <summary>Request body for joining with an invite code.</summary>
public sealed class CreateAccountRequest
{
    public CreateAccountRequest(string inviteCode, string username, string displayName)
    {
        InviteCode = inviteCode;
        Username = username;
        DisplayName = displayName;
    }

    public string InviteCode { get; }

    public string Username { get; }

    public string DisplayName { get; }
}

/// <summary>The new account and its device token. The token is shown once; the server keeps only a hash.</summary>
public sealed class AccountCreated
{
    public AccountCreated(SocialUser user, string token)
    {
        User = user;
        Token = token;
    }

    public SocialUser User { get; }

    public string Token { get; }
}

public sealed class UpdateProfileRequest
{
    public UpdateProfileRequest(string displayName)
    {
        DisplayName = displayName;
    }

    public string DisplayName { get; }
}

/// <summary>A single-use invite code for a friend to join.</summary>
public sealed class InviteCreated
{
    public InviteCreated(string code, DateTimeOffset expiresAt)
    {
        Code = code;
        ExpiresAt = expiresAt;
    }

    public string Code { get; }

    public DateTimeOffset ExpiresAt { get; }
}
