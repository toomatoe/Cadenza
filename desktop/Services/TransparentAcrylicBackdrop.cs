using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Cadenza.Desktop.Services;

/// <summary>
/// A thin, lightly tinted desktop Acrylic backdrop. Each window owns its backdrop; the default
/// configuration keeps Windows policy handling (battery saver, inactive windows, transparency settings).
/// </summary>
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "WinUI owns the backdrop lifetime; the controller is disposed in OnTargetDisconnected.")]
public sealed class TransparentAcrylicBackdrop : SystemBackdrop
{
    private static readonly Color _darkTint = Color.FromArgb(255, 32, 36, 43);
    private static readonly Color _lightTint = Color.FromArgb(255, 214, 232, 250);
    private static readonly Color _lightFallback = Color.FromArgb(255, 221, 235, 248);

    private DesktopAcrylicController? _controller;
    private bool _dark;

    public void SetTheme(ElementTheme theme)
    {
        _dark = theme == ElementTheme.Dark;
        ApplyTint();
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
        ApplyTint();
        _controller.SetSystemBackdropConfiguration(GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot));
        _controller.AddSystemBackdropTarget(connectedTarget);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        if (_controller is not null)
        {
            _controller.RemoveSystemBackdropTarget(disconnectedTarget);
            _controller.Dispose();
            _controller = null;
        }

        base.OnTargetDisconnected(disconnectedTarget);
    }

    private void ApplyTint()
    {
        if (_controller is null)
            return;

        _controller.TintColor = _dark ? _darkTint : _lightTint;
        _controller.TintOpacity = _dark ? 0.20f : 0.22f;
        _controller.LuminosityOpacity = 0.35f;
        _controller.FallbackColor = _dark ? _darkTint : _lightFallback;
    }
}
