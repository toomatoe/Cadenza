namespace Cadenza.Desktop.Models;

/// <summary>Window background material. Values are persisted, so keep the numbering stable.</summary>
public enum AppearanceMode
{
    // Keep value 0 so existing wallpaper preferences migrate to live Acrylic.
    Acrylic = 0,
    Mica = 1,
    Solid = 2
}
