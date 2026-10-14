using Microsoft.UI.Xaml;

namespace Cadenza.Desktop.Converters;

/// <summary>Small functions for <c>x:Bind</c> function bindings, so simple cases need no converter classes.</summary>
public static class BindingHelpers
{
    private const double DimmedOpacity = 0.5;

    /// <summary>Visible when <paramref name="value"/> is false.</summary>
    public static Visibility VisibleWhenFalse(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Full opacity when enabled, dimmed otherwise.</summary>
    public static double OpacityFor(bool enabled) => enabled ? 1 : DimmedOpacity;
}
