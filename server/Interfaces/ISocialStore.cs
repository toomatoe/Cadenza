using Cadenza.Core.Social;

namespace Cadenza.Server.Interfaces;

/// <summary>
/// Accounts, invites, friendships, blocks, messages, reports and library entries. Rules that protect
/// people (only friends can message, blocks hide both ways) are enforced here, not in the client.
/// </summary>
public interface ISocialStore
{
    /// <summary>When nobody has joined yet, makes sure an owner invite exists and returns it; null once accounts exist.</summary>
    string? EnsureBootstrapInvite(string? configuredCode, DateTimeOffset now);

    /// <summary>Creates an account from a valid, unused invite and returns it with its device token.</summary>
    /// <exception cref="Exceptions.SocialRuleException">The invite, username or display name is invalid.</exception>
    AccountCreated CreateAccount(CreateAccountRequest request, DateTimeOffset now);

    /// <summary>The account a device token belongs to, or null.</summary>
    SocialUser? Authenticate(string token);

    /// <summary>The account with this ID, or null.</summary>
    SocialUser? FindUser(string userId);

    /// <summary>Creates a single-use invite for <paramref name="userId"/> to share.</summary>
    InviteCreated CreateInvite(string userId, DateTimeOffset now);

    /// <summary>Friends plus incoming and outgoing requests.</summary>
    FriendList GetFriends(string userId);

    /// <summary>Sends a friend request, or accepts one already waiting from the other person.</summary>
    /// <returns>The other person, and whether you are now friends.</returns>
    (SocialUser Other, bool Accepted) RequestFriend(string userId, string username, DateTimeOffset now);

    /// <summary>Accepts a pending request from <paramref name="otherId"/>.</summary>
    SocialUser Accept(string userId, string otherId);

    /// <summary>Unfriends, declines or cancels, whichever applies.</summary>
    void Remove(string userId, string otherId);

    /// <summary>Blocks <paramref name="otherId"/> and removes any friendship.</summary>
    void Block(string userId, string otherId);

    /// <summary>Lifts a block.</summary>
    void Unblock(string userId, string otherId);

    /// <summary>Stores a message to a friend.</summary>
    /// <exception cref="Exceptions.SocialRuleException">The message is invalid or the people aren't friends.</exception>
    ChatMessage SendMessage(string userId, string toId, SendMessageRequest request, DateTimeOffset now);

    /// <summary>The latest messages with a friend, oldest first; pass <paramref name="beforeId"/> to page back.</summary>
    IReadOnlyList<ChatMessage> Messages(string userId, string friendId, long? beforeId, int limit);

    /// <summary>Stores a report with a copy of the reported message.</summary>
    void Report(string userId, ReportRequest request, DateTimeOffset now);

    /// <summary>Changes the display name.</summary>
    SocialUser UpdateProfile(string userId, UpdateProfileRequest request);

    /// <summary>The library song with this file hash, if someone already uploaded it.</summary>
    LibraryTrack? FindMusicByHash(string sha256);

    /// <summary>The library song with this ID, or null.</summary>
    LibraryTrack? FindMusic(string id);

    /// <summary>Records an uploaded song.</summary>
    LibraryTrack AddMusic(SocialUser uploader, string id, UploadMetadata metadata, string format, string sha256, DateTimeOffset now);

    /// <summary>Songs whose title, artist or album contain every word of <paramref name="query"/>; newest first.</summary>
    IReadOnlyList<LibraryTrack> SearchMusic(string? query, int limit);

    /// <summary>Removes a song if <paramref name="userId"/> uploaded it; returns whether it did.</summary>
    bool RemoveMusic(string userId, string id);

    /// <summary>Deletes the account and everything tied to it (reports keep their copy; songs stay in the library).</summary>
    void DeleteAccount(string userId);

    /// <summary>Whether two people are friends.</summary>
    bool AreFriends(string userId, string otherId);
}
