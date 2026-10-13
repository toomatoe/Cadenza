using Cadenza.Core.Services.Spotify;
using Cadenza.Core.Sprites;

namespace Cadenza.Desktop.Models;

/// <summary>User preferences persisted as JSON by <see cref="Services.PreferencesStore"/>.</summary>
/// <param name="ClientId">The user's Spotify developer Client ID.</param>
/// <param name="ArtistSpacing">Minimum tracks between the same artist when shuffling.</param>
/// <param name="RedirectUri">Loopback redirect registered in the Spotify developer app.</param>
/// <param name="SolidAppearance">Legacy flag kept so older preference files still load.</param>
/// <param name="Appearance">Window material; null falls back to <paramref name="SolidAppearance"/>.</param>
/// <param name="Sprite">The profile companion; null (older files) uses the default companion.</param>
/// <param name="Discovery">The "For you" discovery dial: 0 sticks to known artists, 1 favours new ones.</param>
/// <param name="FriendsServer">Cadenza Friends server address; empty until you join.</param>
/// <param name="AutoQueueGifts">Add friends' queue gifts straight to Up next instead of asking.</param>
public sealed record Preferences(
    string ClientId = "",
    int ArtistSpacing = 2,
    string RedirectUri = SpotifyAuth.RedirectUri,
    bool SolidAppearance = false,
    AppearanceMode? Appearance = null,
    SpriteProfile? Sprite = null,
    double Discovery = 0.3,
    string FriendsServer = "",
    bool AutoQueueGifts = false);
