using Cadenza.Core.Models.Social;

namespace Cadenza.Core.Interfaces;

/// <summary>
/// The Cadenza Friends server API: accounts, friends, messages, reports and the shared music library.
/// Failures are thrown as <see cref="Exceptions.SocialException"/> with a message safe to show the user.
/// </summary>
public interface ISocialClient
{
    /// <summary>The device token; set after joining or when restored from the credential store.</summary>
    string? Token { get; set; }

    /// <summary>The server's base address.</summary>
    Uri Server { get; }

    Task<AccountCreated> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken);

    Task<SocialUser> MeAsync(CancellationToken cancellationToken);

    Task<SocialUser> UpdateProfileAsync(string displayName, CancellationToken cancellationToken);

    Task DeleteAccountAsync(CancellationToken cancellationToken);

    Task<InviteCreated> CreateInviteAsync(CancellationToken cancellationToken);

    Task<FriendList> FriendsAsync(CancellationToken cancellationToken);

    Task<FriendList> AddFriendAsync(string username, CancellationToken cancellationToken);

    Task<FriendList> AcceptAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Unfriends, declines or cancels a request.</summary>
    Task<FriendList> RemoveAsync(string userId, CancellationToken cancellationToken);

    Task<FriendList> BlockAsync(string userId, CancellationToken cancellationToken);

    /// <summary>The latest messages with a friend, oldest first; pass <paramref name="beforeId"/> to page back.</summary>
    Task<IReadOnlyList<ChatMessage>> MessagesAsync(string friendId, long? beforeId, CancellationToken cancellationToken);

    Task<ChatMessage> SendAsync(string friendId, SendMessageRequest request, CancellationToken cancellationToken);

    Task ReportAsync(long messageId, string reason, CancellationToken cancellationToken);

    /// <summary>Uploads a music file to the shared library; returns the existing entry if the same file is already there.</summary>
    Task<LibraryTrack> UploadMusicAsync(string path, UploadMetadata metadata, CancellationToken cancellationToken);

    /// <summary>Searches the shared library; an empty query lists the newest songs.</summary>
    Task<IReadOnlyList<LibraryTrack>> SearchMusicAsync(string query, CancellationToken cancellationToken);

    Task DeleteMusicAsync(string id, CancellationToken cancellationToken);

    /// <summary>Downloads a library song to <paramref name="destination"/>.</summary>
    Task DownloadMusicAsync(string id, string destination, CancellationToken cancellationToken);
}
