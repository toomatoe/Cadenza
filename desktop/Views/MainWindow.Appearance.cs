using Cadenza.Desktop.Models;
using Cadenza.Desktop.Services;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Cadenza.Desktop.Views;

public sealed partial class MainWindow
{
    private const double CompactLayoutWidth = 1100;
    private static readonly Color s_darkCaptionInk = Color.FromArgb(255, 243, 246, 252);
    private static readonly Color s_lightCaptionInk = Color.FromArgb(255, 23, 40, 62);
    private static readonly Color s_darkCaptionHover = Color.FromArgb(40, 255, 255, 255);
    private static readonly Color s_lightCaptionHover = Color.FromArgb(24, 0, 0, 0);

    private readonly UISettings _uiSettings = new();
    private readonly AccessibilitySettings _accessibility = new();
    private bool _appearanceInitialized;
    private bool? _compactLayout;

    private AppearanceMode CurrentAppearance => _preferences.Appearance
        ?? (_preferences.SolidAppearance ? AppearanceMode.Solid : AppearanceMode.Acrylic);

    private void Root_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        var compact = args.NewSize.Width < CompactLayoutWidth;
        Grid.SetRow(HeroQueue, compact ? 1 : 0);
        Grid.SetColumn(HeroQueue, compact ? 0 : 1);
        HeroQueueColumn.Width = compact ? new GridLength(0) : new GridLength(2, GridUnitType.Star);
        Grid.SetColumnSpan(HeroMain, compact ? 2 : 1);
        Grid.SetColumnSpan(HeroQueue, compact ? 2 : 1);
        if (_compactLayout == compact)
        {
            return;
        }

        _compactLayout = compact;
        Grid.SetRow(SearchPanel, compact ? 1 : 0);
        Grid.SetColumn(SearchPanel, compact ? 0 : 1);
        Grid.SetColumnSpan(SearchPanel, compact ? 2 : 1);
        SearchPanel.Margin = new Thickness(0, compact ? 12 : 0, 0, 0);
        SearchPanel.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
    }

    /// <summary>Resolves a theme brush for the window's current theme, including high contrast.</summary>
    private Brush AppearanceBrush(string key)
    {
        var theme = _accessibility.HighContrast ? "HighContrast" : Root.ActualTheme.ToString();
        var resources = (ResourceDictionary)Application.Current.Resources.ThemeDictionaries[theme];
        return (Brush)resources[key];
    }

    private void InitializeAppearance()
    {
        AppearanceBox.SelectedIndex = (int)CurrentAppearance;
        _appearanceInitialized = true;
        _uiSettings.AdvancedEffectsEnabledChanged += AdvancedEffectsChanged;
        _uiSettings.ColorValuesChanged += SystemColorsChanged;
        Root.ActualThemeChanged += ThemeChanged;
        ApplyAppearance();
    }

    private void AdvancedEffectsChanged(UISettings sender, object args) => QueueAppearanceUpdate();

    private void SystemColorsChanged(UISettings sender, object args) => QueueAppearanceUpdate();

    private void ThemeChanged(FrameworkElement sender, object args) => ApplyAppearance();

    // UISettings events arrive off the UI thread.
    private void QueueAppearanceUpdate() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_closing)
        {
            ApplyAppearance();
        }
    });

    private void ApplyAppearance()
    {
        var effectsEnabled = !_accessibility.HighContrast && _uiSettings.AdvancedEffectsEnabled;
        var useAcrylic = CurrentAppearance == AppearanceMode.Acrylic && effectsEnabled && DesktopAcrylicController.IsSupported();
        var useMica = CurrentAppearance == AppearanceMode.Mica && effectsEnabled && MicaController.IsSupported();
        if (useAcrylic)
        {
            if (SystemBackdrop is not TransparentAcrylicBackdrop acrylic)
            {
                acrylic = new TransparentAcrylicBackdrop();
                SystemBackdrop = acrylic;
            }

            acrylic.SetTheme(Root.ActualTheme);
        }
        else if (useMica)
        {
            if (SystemBackdrop is not MicaBackdrop)
            {
                SystemBackdrop = new MicaBackdrop();
            }
        }
        else
        {
            SystemBackdrop = null;
        }

        SolidBackground.Visibility = useAcrylic || useMica ? Visibility.Collapsed : Visibility.Visible;

        // Native caption buttons must also follow theme and contrast changes.
        var dark = Root.ActualTheme == ElementTheme.Dark;
        var ink = _accessibility.HighContrast
            ? _uiSettings.GetColorValue(UIColorType.Foreground)
            : dark ? s_darkCaptionInk : s_lightCaptionInk;
        AppWindow.TitleBar.ButtonForegroundColor = ink;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = ink;
        AppWindow.TitleBar.ButtonHoverForegroundColor = ink;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = _accessibility.HighContrast
            ? _uiSettings.GetColorValue(UIColorType.Background)
            : dark ? s_darkCaptionHover : s_lightCaptionHover;
        UpdateNav();
        RefreshPlaylistDeck();
    }

    private void Appearance_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_appearanceInitialized || AppearanceBox.SelectedIndex < 0)
        {
            return;
        }

        var selected = (AppearanceMode)AppearanceBox.SelectedIndex;
        var updated = _preferences with { Appearance = selected, SolidAppearance = selected == AppearanceMode.Solid };
        try
        {
            PreferencesStore.Save(updated);
            _preferences = updated;
            ApplyAppearance();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Restore the saved selection without re-entering this handler.
            _appearanceInitialized = false;
            AppearanceBox.SelectedIndex = (int)CurrentAppearance;
            _appearanceInitialized = true;
            Message("Couldn't save appearance. Check your local application data folder.", InfoBarSeverity.Error);
        }
    }

    private void DisposeAppearance()
    {
        _uiSettings.AdvancedEffectsEnabledChanged -= AdvancedEffectsChanged;
        _uiSettings.ColorValuesChanged -= SystemColorsChanged;
        Root.ActualThemeChanged -= ThemeChanged;
        SystemBackdrop = null;
    }
}
