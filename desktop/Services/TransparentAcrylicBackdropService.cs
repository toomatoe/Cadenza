using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Cadenza.Desktop.Services;

// Each window owns its backdrop; the base configuration retains Windows policy handling.
public class TransparentAcrylicBackdrop : SystemBackdrop
{
    private DesktopAcrylicController? controller;
    private bool dark;

    public void SetTheme(ElementTheme theme)
    {
        dark = theme == ElementTheme.Dark;
        ApplyTint();
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        controller = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Thin };
        ApplyTint();
        controller.SetSystemBackdropConfiguration(GetDefaultSystemBackdropConfiguration(connectedTarget, xamlRoot));
        controller.AddSystemBackdropTarget(connectedTarget);
    }

    private void ApplyTint()
    {
        if (controller is null) return;
        controller.TintColor = dark ? Color.FromArgb(255, 32, 36, 43) : Color.FromArgb(255, 245, 248, 255);
        controller.TintOpacity = dark ? 0.32f : 0.48f;
        controller.LuminosityOpacity = dark ? 0.35f : 0.65f;
        controller.FallbackColor = dark ? Color.FromArgb(255, 32, 36, 43) : Color.FromArgb(255, 231, 237, 247);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        if (controller is not null)
        {
            controller.RemoveSystemBackdropTarget(disconnectedTarget);
            controller.Dispose();
            controller = null;
        }
        base.OnTargetDisconnected(disconnectedTarget);
    }
}
