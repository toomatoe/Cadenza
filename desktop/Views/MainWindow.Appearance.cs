using Cadenza.Desktop.Services;
using Cadenza.Desktop.Models;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace Cadenza.Desktop.Views;

public sealed partial class MainWindow
{
    private readonly UISettings uiSettings = new();
    private readonly AccessibilitySettings accessibility = new();
    private bool appearanceInitialized;
    private bool? compactLayout;
    private AppearanceMode CurrentAppearance => preferences.Appearance
        ?? (preferences.SolidAppearance ? AppearanceMode.Solid : AppearanceMode.Acrylic);

    private void Root_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        var compact = args.NewSize.Width < 1100;
        Grid.SetRow(HeroQueue, compact ? 1 : 0);
        Grid.SetColumn(HeroQueue, compact ? 0 : 1);
        HeroQueueColumn.Width = compact ? new GridLength(0) : new GridLength(2, GridUnitType.Star);
        Grid.SetColumnSpan(HeroMain, compact ? 2 : 1);
        Grid.SetColumnSpan(HeroQueue, compact ? 2 : 1);
        if (compactLayout == compact) return;
        compactLayout = compact;
        Grid.SetRow(SearchPanel, compact ? 1 : 0);
        Grid.SetColumn(SearchPanel, compact ? 0 : 1);
        Grid.SetColumnSpan(SearchPanel, compact ? 2 : 1);
        SearchPanel.Margin = new Thickness(0, compact ? 12 : 0, 0, 0);
        SearchPanel.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
    }

    private Brush AppearanceBrush(string key)
    {
        var theme = accessibility.HighContrast ? "HighContrast" : Root.ActualTheme.ToString();
        var resources = (ResourceDictionary)Application.Current.Resources.ThemeDictionaries[theme];
        return (Brush)resources[key];
    }

    private void InitializeAppearance()
    {
        AppearanceBox.SelectedIndex = (int)CurrentAppearance;
        appearanceInitialized = true;
        uiSettings.AdvancedEffectsEnabledChanged += AdvancedEffectsChanged;
        uiSettings.ColorValuesChanged += SystemColorsChanged;
        Root.ActualThemeChanged += ThemeChanged;
        ApplyAppearance();
    }

    private void AdvancedEffectsChanged(UISettings sender, object args) => QueueAppearanceUpdate();
    private void SystemColorsChanged(UISettings sender, object args) => QueueAppearanceUpdate();
    private void ThemeChanged(FrameworkElement sender, object args) => ApplyAppearance();
    private void QueueAppearanceUpdate() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!closing) ApplyAppearance();
    });

    private void ApplyAppearance()
    {
        var effectsEnabled = !accessibility.HighContrast && uiSettings.AdvancedEffectsEnabled;
        var useAcrylic = CurrentAppearance == AppearanceMode.Acrylic && effectsEnabled && DesktopAcrylicController.IsSupported();
        var useMica = CurrentAppearance == AppearanceMode.Mica && effectsEnabled && MicaController.IsSupported();
        if (useAcrylic)
        {
            if (SystemBackdrop is not TransparentAcrylicBackdrop) SystemBackdrop = new TransparentAcrylicBackdrop();
            ((TransparentAcrylicBackdrop)SystemBackdrop).SetTheme(Root.ActualTheme);
        }
        else if (useMica)
        {
            if (SystemBackdrop is not MicaBackdrop) SystemBackdrop = new MicaBackdrop();
        }
        else SystemBackdrop = null;
        SolidBackground.Visibility = useAcrylic || useMica ? Visibility.Collapsed : Visibility.Visible;

        // Native caption buttons must also follow theme and contrast changes.
        var ink = accessibility.HighContrast ? uiSettings.GetColorValue(UIColorType.Foreground)
            : Root.ActualTheme == ElementTheme.Dark
                ? Windows.UI.Color.FromArgb(255, 243, 246, 252)
                : Windows.UI.Color.FromArgb(255, 23, 40, 62);
        AppWindow.TitleBar.ButtonForegroundColor = ink;
        AppWindow.TitleBar.ButtonInactiveForegroundColor = ink;
        AppWindow.TitleBar.ButtonHoverForegroundColor = ink;
        AppWindow.TitleBar.ButtonHoverBackgroundColor = accessibility.HighContrast
            ? uiSettings.GetColorValue(UIColorType.Background)
            : Root.ActualTheme == ElementTheme.Dark
                ? Windows.UI.Color.FromArgb(40, 255, 255, 255)
                : Windows.UI.Color.FromArgb(24, 0, 0, 0);
        UpdateNav();
        RefreshPlaylistDeck();
    }

    private void Appearance_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!appearanceInitialized) return;
        if (AppearanceBox.SelectedIndex < 0) return;
        var selected = (AppearanceMode)AppearanceBox.SelectedIndex;
        var updated = preferences with { Appearance = selected, SolidAppearance = selected == AppearanceMode.Solid };
        try
        {
            PreferencesStore.Save(updated);
            preferences = updated;
            ApplyAppearance();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            appearanceInitialized = false;
            AppearanceBox.SelectedIndex = (int)CurrentAppearance;
            appearanceInitialized = true;
            Message("Couldn't save appearance. Check your local application data folder.", InfoBarSeverity.Error);
        }
    }

    private void DisposeAppearance()
    {
        uiSettings.AdvancedEffectsEnabledChanged -= AdvancedEffectsChanged;
        uiSettings.ColorValuesChanged -= SystemColorsChanged;
        Root.ActualThemeChanged -= ThemeChanged;
        SystemBackdrop = null;
    }
}
