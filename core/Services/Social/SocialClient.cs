using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Cadenza.Core.Exceptions;
using Cadenza.Core.Models.Social;

namespace Cadenza.Core.Services.Social;

/// <summary>
/// Client for the Cadenza Friends server. Sends only what the feature needs: your Cadenza username,
/// display name, messages and Spotify track IDs. Never Spotify tokens or listening history.
/// </summary>
public sealed class SocialClient
{
    private readonly HttpClient _http;

    public SocialClient(HttpClient http, Uri server)
    {
        _http = http;
        Server = server;
    }

    /// <summary>The device token; set after joining or when restored from the credential store.</summary>
    public string? Token { get; set; }

    public Uri Server { get; }

    public Task<AccountCreated> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken) =>
        SendAsync<AccountCreated>(HttpMethod.Post, "api/accounts", request, cancellationToken);

    public Task<SocialUser> MeAsync(CancellationToken cancellationToken) => SendAsync<SocialUser>(HttpMethod.Get, "api/me", null, cancellationToken);

    public Task<SocialUser> UpdateProfileAsync(string displayName, CancellationToken cancellationToken) =>
        SendAsync<SocialUser>(HttpMethod.Put, "api/me", new UpdateProfileRequest(displayName), cancellationToken);

    public Task DeleteAccountAsync(CancellationToken cancellationToken) => SendAsync<object>(HttpMethod.Delete, "api/me", null, cancellationToken);

    public Task<InviteCreated> CreateInviteAsync(CancellationToken cancellationToken) => SendAsync<InviteCreated>(HttpMethod.Post, "api/invites", null, cancellationToken);

    public Task<FriendList> FriendsAsync(CancellationToken cancellationToken) => SendAsync<FriendList>(HttpMethod.Get, "api/friends", null, cancellationToken);

    public Task<FriendList> AddFriendAsync(string username, CancellationToken cancellationToken) =>
        SendAsync<FriendList>(HttpMethod.Post, "api/friends/requests", new FriendRequest(username), cancellationToken);

    public Task<FriendList> AcceptAsync(string userId, CancellationToken cancellationToken) =>
        SendAsync<FriendList>(HttpMethod.Post, $"api/friends/{Escape(userId)}/accept", null, cancellationToken);

    /// <summary>Unfriends, declines or cancels a request.</summary>
    public Task<FriendList> RemoveAsync(string userId, CancellationToken cancellationToken) =>
        SendAsync<FriendList>(HttpMethod.Delete, $"api/friends/{Escape(userId)}", null, cancellationToken);

    public Task<FriendList> BlockAsync(string userId, CancellationToken cancellationToken) =>
        SendAsync<FriendList>(HttpMethod.Post, $"api/blocks/{Escape(userId)}", null, cancellationToken);

    public Task<IReadOnlyList<ChatMessage>> MessagesAsync(string friendId, long? beforeId, CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<ChatMessage>>(HttpMethod.Get, $"api/messages/{Escape(friendId)}" + (beforeId is { } id ? $"?before={id}" : ""), null, cancellationToken);

    public Task<ChatMessage> SendAsync(string friendId, SendMessageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SocialRules.ValidateMessage(request) is { } problem
            ? throw new SocialException(400, problem)
            : SendAsync<ChatMessage>(HttpMethod.Post, $"api/messages/{Escape(friendId)}", request, cancellationToken);
    }

    public Task ReportAsync(long messageId, string reason, CancellationToken cancellationToken) =>
        SendAsync<object>(HttpMethod.Post, "api/reports", new ReportRequest(messageId, reason), cancellationToken);

    /// <summary>Uploads a music file to the shared library; returns the existing entry if the same file is already there.</summary>
    public async Task<LibraryTrack> UploadMusicAsync(string path, UploadMetadata metadata, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (MusicFormats.Validate(metadata) is { } problem)
            throw new SocialException(400, problem);

        await using var file = File.OpenRead(path);
        if (file.Length > MusicFormats.MaxUploadBytes)
            throw new SocialException(413, $"{Path.GetFileName(path)} is larger than {MusicFormats.MaxUploadBytes / (1024 * 1024)} MB.");

        using var content = new MultipartFormDataContent
        {
            { JsonContent.Create(metadata, options: SocialRules.Json), "metadata" },
            { new StreamContent(file), "file", Path.GetFileName(path) },
        };

        return await SendAsync<LibraryTrack>(HttpMethod.Post, "api/music", content, cancellationToken);
    }

    /// <summary>Searches the shared library by title, artist or album; an empty query lists the newest songs.</summary>
    public Task<IReadOnlyList<LibraryTrack>> SearchMusicAsync(string query, CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyList<LibraryTrack>>(HttpMethod.Get, "api/music?q=" + Escape(query ?? ""), null, cancellationToken);

    public Task DeleteMusicAsync(string id, CancellationToken cancellationToken) =>
        SendAsync<object>(HttpMethod.Delete, $"api/music/{Escape(id)}", null, cancellationToken);

    /// <summary>Downloads a library song to <paramref name="destination"/> (written to a temporary file first).</summary>
    public async Task DownloadMusicAsync(string id, string destination, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(Server, $"api/music/{Escape(id)}/file"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            throw new SocialException("Cadenza Friends is offline right now, so library songs can't play.", error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new SocialException((int)response.StatusCode, await ReadErrorAsync(response, cancellationToken));

            var partial = destination + ".part";
            await using (var output = File.Create(partial))
            {
                await response.Content.CopyToAsync(output, cancellationToken);
            }

            File.Move(partial, destination, overwrite: true);
        }
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private async Task<T> SendAsync<T>(HttpMethod method, string route, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(Server, route));
        if (Token is { Length: > 0 } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        if (body is HttpContent content)
            request.Content = content;
        else if (body is not null)
            request.Content = JsonContent.Create(body, body.GetType(), options: SocialRules.Json);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException error)
        {
            throw new SocialException("Cadenza Friends is offline right now. Your music still works.", error);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new SocialException((int)response.StatusCode, await ReadErrorAsync(response, cancellationToken));

            if (typeof(T) == typeof(object) || response.Content.Headers.ContentLength == 0)
                return default!;

            return await response.Content.ReadFromJsonAsync<T>(SocialRules.Json, cancellationToken)
                ?? throw new SocialException((int)response.StatusCode, "The server sent an empty reply.");
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<SocialError>(SocialRules.Json, cancellationToken);
            if (!string.IsNullOrWhiteSpace(error?.Error))
                return error.Error;
        }
        catch (JsonException)
        {
            // Not one of our error bodies (for example a proxy error page).
        }

        return $"The server refused that ({(int)response.StatusCode}).";
    }
}
