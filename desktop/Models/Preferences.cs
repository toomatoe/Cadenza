using Cadenza.Core.Services.Spotify;

namespace Cadenza.Desktop.Models;

internal sealed record Preferences(string ClientId = "", int ArtistSpacing = 2, string RedirectUri = SpotifyAuth.RedirectUri,
    bool SolidAppearance = false, AppearanceMode? Appearance = null);
