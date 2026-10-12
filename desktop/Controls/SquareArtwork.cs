using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Cadenza.Desktop.Controls;

// Report the square's height during measure, before the scrolling layout places its rows.
public class SquareArtwork : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        var side = double.IsFinite(availableSize.Width) ? availableSize.Width : 152;
        var square = new Size(side, side);
        foreach (var child in Children) child.Measure(square);
        return square;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children) child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }
}
