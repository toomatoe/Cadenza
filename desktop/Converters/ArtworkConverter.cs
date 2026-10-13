using System.Globalization;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Cadenza.Desktop.Converters;

/// <summary>Converts an HTTPS artwork URL to a decoded bitmap. The optional parameter is the decode width.</summary>
public sealed class ArtworkConverter : IValueConverter
{
    private const int CacheLimit = 128;
    private const int MinDecodeWidth = 64;
    private const int MaxDecodeWidth = 360;

    // UI-thread-only weak cache: reuses live decoded artwork without retaining offscreen bitmaps.
    // Both the key set and the eviction queue have a fixed bound.
    private static readonly Dictionary<(string Url, int Width), WeakReference<BitmapImage>> s_cache = [];
    private static readonly Queue<(string Url, int Width)> s_cacheOrder = [];

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var width = parameter is string text && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : MaxDecodeWidth;
        return value is string url ? GetArtwork(url, width) : null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    public static BitmapImage? GetArtwork(string url, int width = MaxDecodeWidth)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        width = Math.Clamp(width, MinDecodeWidth, MaxDecodeWidth);
        var key = (url, width);
        if (s_cache.TryGetValue(key, out var reference) && reference.TryGetTarget(out var live))
        {
            return live;
        }

        var bitmap = new BitmapImage { DecodePixelWidth = width, UriSource = uri };
        if (!s_cache.ContainsKey(key))
        {
            if (s_cache.Count == CacheLimit)
            {
                s_cache.Remove(s_cacheOrder.Dequeue());
            }

            s_cacheOrder.Enqueue(key);
        }

        s_cache[key] = new WeakReference<BitmapImage>(bitmap);
        return bitmap;
    }
}
