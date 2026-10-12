using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Cadenza.Desktop.Converters;

public class ArtworkConverter : IValueConverter
{
    // UI-thread-only, weak cache: reuse live decoded artwork without retaining
    // offscreen bitmaps. Both metadata and URL references have a fixed bound.
    private const int CacheLimit = 128;
    private static readonly Dictionary<(string Url, int Width), WeakReference<BitmapImage>> cache = [];
    private static readonly Queue<(string Url, int Width)> cacheOrder = [];

    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        var width = parameter is string text && int.TryParse(text, out var parsed) ? parsed : 360;
        return value is string url ? GetArtwork(url, width) : null;
    }
    public static BitmapImage? GetArtwork(string url, int width = 360)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        width = Math.Clamp(width, 64, 360);
        var key = (url, width);
        if (cache.TryGetValue(key, out var reference) && reference.TryGetTarget(out var live)) return live;
        var bitmap = new BitmapImage { DecodePixelWidth = width, UriSource = uri };
        if (!cache.ContainsKey(key))
        {
            if (cache.Count == CacheLimit) cache.Remove(cacheOrder.Dequeue());
            cacheOrder.Enqueue(key);
        }
        cache[key] = new(bitmap);
        return bitmap;
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
