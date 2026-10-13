using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Cadenza.Core.Social;

/// <summary>
/// Client for the Cadenza Friends server. Sends only what the feature needs: your Cadenza username,
/// display name, messages and Spotify track IDs. Never Spotify tokens or listening history.
/// </summary>
public sealed class SocialClient(HttpClient http, Uri server)
{
    /// <summary>The device token; set after joining or when restored from the credential store.</summary>
    public string? Token { get; set; }

    public Uri Server { get; } = server;

    public Task<AccountCreated> CreateAccountAsync(CreateAccountRequest request, CancellationToken ct) =>
        SendAsync<AccountCreated>(HttpMethod.Post, "api/accounts", request, ct);

    public Task<SocialUser> MeAsync(CancellationToken ct) => SendAsync<SocialUser>(HttpMethod.Get, "api/me", null, ct);

    public Task<SocialUser> UpdateProfileAsync(string displayName, CancellationToken ct) =>
        SendAsync<SocialUser>(HttpMethod.Put, "api/me", new UpdateProfileRequest(displayName), ct);

    public Task DeleteAccountAsync(CancellationToken ct) => SendAsync<object>(HttpMethod.Delete, "api/me", null, ct);

    public Task<InviteCreated> CreateInviteAsync(CancellationToken ct) => SendAsync<InviteCreated>(HttpMethod.Post, "api/invites", null, ct);

    public Task<FriendList> FriendsAsync(CancellationToken ct) => SendAsync<FriendList>(HttpMethod.Get, "api/friends", null, ct);

    public Task<FriendList> AddFriendAsync(string username, CancellationToken ct) =>
        SendAsync<FriendList>(HttpMethod.Post, "api/friends/requests", new FriendRequest(username), ct);

    public Task<FriendList> AcceptAsync(string userId, CancellationToken ct) =>
        SendAsync<FriendList>(HttpMethod.Post, $"api/friends/{Escape(userId)}/accept", null, ct);

    /// <summary>Unfriends, declines or cancels a request.</summary>
    public Task<FriendList> RemoveAsync(string userId, CancellationToken ct) =>
        SendAsync<FriendList>(HttpMethod.Delete, $"api/friends/{Escape(userId)}", null, ct);

    public Task<FriendList> BlockAsync(string userId, CancellationToken ct) =>
        SendAsync<FriendList>(HttpMethod.Post, $"api/blocks/{Escape(userId)}", null, ct);

    public Task<IReadOnlyList<ChatMessage>> MessagesAsync(string friendId, long? beforeId, CancellationToken ct) =>
        SendAsync<IReadOnlyList<ChatMessage>>(HttpMethod.Get, $"api/messages/{Escape(friendId)}" + (beforeId is { } id ? $"?before={id}" : ""), null, ct);

    public Task<ChatMessage> SendAsync(string friendId, SendMessageRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SocialRules.ValidateMessage(request) is { } problem
            ? throw new SocialException(400, problem)
            : SendAsync<ChatMessage>(HttpMethod.Post, $"api/messages/{Escape(friendId)}", request, ct);
    }

    public Task ReportAsync(long messageId, string reason, CancellationToken ct) =>
        SendAsync<object>(HttpMethod.Post, "api/reports", new ReportRequest(messageId, reason), ct);

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private async Task<T> SendAsync<T>(HttpMethod method, string route, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri(Server, route));
        if (Token is { Length: > 0 } token)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: SocialRules.Json);
        }

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException error)
        {
            throw new SocialException("Cadenza Friends is offline right now. Your music still works.", error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new SocialException((int)response.StatusCode, await ReadErrorAsync(response, ct));
            }

            if (typeof(T) == typeof(object) || response.Content.Headers.ContentLength == 0)
            {
                return default!;
            }

            return await response.Content.ReadFromJsonAsync<T>(SocialRules.Json, ct)
                ?? throw new SocialException((int)response.StatusCode, "The server sent an empty reply.");
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<SocialError>(SocialRules.Json, ct);
            if (!string.IsNullOrWhiteSpace(error?.Error))
            {
                return error.Error;
            }
        }
        catch (JsonException)
        {
            // Not one of our error bodies (for example a proxy error page).
        }

        return $"The server refused that ({(int)response.StatusCode}).";
    }
}
