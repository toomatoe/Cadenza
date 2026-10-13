using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadenza.Core.Social;

/// <summary>Validation rules and wire format shared by the Cadenza server and client.</summary>
public static class SocialRules
{
    public const int MinUsernameLength = 3;
    public const int MaxUsernameLength = 20;
    public const int MaxDisplayNameLength = 40;
    public const int MaxMessageLength = 2000;
    public const int MaxReportLength = 500;
    public const int PageSize = 50;
    private const int SpotifyIdLength = 22;

    /// <summary>JSON options for every request, response and WebSocket event.</summary>
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Lowercase letters, digits and underscores, 3–20 characters.</summary>
    public static bool IsValidUsername(string? username) =>
        username is { Length: >= MinUsernameLength and <= MaxUsernameLength } &&
        username.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_');

    public static bool IsValidDisplayName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= MaxDisplayNameLength && !name.Any(char.IsControl);

    public static bool IsSpotifyId(string? id) =>
        id is { Length: SpotifyIdLength } && id.All(char.IsAsciiLetterOrDigit);

    /// <summary>Checks a message before sending; returns the problem, or null when it is valid.</summary>
    public static string? ValidateMessage(SendMessageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.Text ?? "").Length > MaxMessageLength)
        {
            return $"Messages are limited to {MaxMessageLength} characters.";
        }

        return request.Kind switch
        {
            ChatMessageKind.Text when string.IsNullOrWhiteSpace(request.Text) => "Write a message first.",
            ChatMessageKind.Text when request.TrackId is not null => "Text messages can't carry a track.",
            ChatMessageKind.Share or ChatMessageKind.Gift when !IsSpotifyId(request.TrackId) => "That isn't a Spotify track.",
            _ when !Enum.IsDefined(request.Kind) => "Unknown message type.",
            _ => null,
        };
    }
}
