using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Cadenza.Desktop.Controls;

/// <summary>
/// Lays out its children in a square as wide as the available space. Reporting the height during
/// measure, before the scrolling layout places its rows, keeps virtualized card rows a stable size.
/// </summary>
public sealed class SquareArtwork : Panel
{
    private const double FallbackSide = 152;

    protected override Size MeasureOverride(Size availableSize)
    {
        var side = double.IsFinite(availableSize.Width) ? availableSize.Width : FallbackSide;
        var square = new Size(side, side);
        foreach (var child in Children)
        {
            child.Measure(square);
        }

        return square;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
        {
            child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        }

        return finalSize;
    }
}
