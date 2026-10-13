using Cadenza.Core.Services.Spotify;

namespace Cadenza.Desktop.Models;

/// <summary>User preferences persisted as JSON by <see cref="Services.PreferencesStore"/>.</summary>
/// <param name="ClientId">The user's Spotify developer Client ID.</param>
/// <param name="ArtistSpacing">Minimum tracks between the same artist when shuffling.</param>
/// <param name="RedirectUri">Loopback redirect registered in the Spotify developer app.</param>
/// <param name="SolidAppearance">Legacy flag kept so older preference files still load.</param>
/// <param name="Appearance">Window material; null falls back to <paramref name="SolidAppearance"/>.</param>
public sealed record Preferences(
    string ClientId = "",
    int ArtistSpacing = 2,
    string RedirectUri = SpotifyAuth.RedirectUri,
    bool SolidAppearance = false,
    AppearanceMode? Appearance = null);
