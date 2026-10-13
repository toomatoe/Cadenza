using Cadenza.Core.Models;
using Cadenza.Core.Sprites;
using Cadenza.Desktop.Controls;
using Cadenza.Desktop.Models;
using Cadenza.Desktop.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Cadenza.Desktop.Views;

public sealed partial class MainWindow
{
    private const double SwatchSize = 32;

    // 0 means "the species' own colour".
    private static readonly (string Name, uint Color)[] s_bodyColors =
    [
        ("Default", 0),
        ("Slate", 0xFF9DB2CF),
        ("Ice", 0xFFC9DDF2),
        ("Ginger", 0xFFE8894A),
        ("Leaf", 0xFF8FCB7B),
        ("Snow", 0xFFF3EEF6),
        ("Sun", 0xFFFFD966),
        ("Blossom", 0xFFF7B7CF),
        ("Ink", 0xFF5B6478),
    ];

    private static readonly (string Name, uint Color)[] s_accentColors =
    [
        ("Default", 0),
        ("Pink", 0xFFF2AFC0),
        ("Blue", 0xFF2A74CF),
        ("Orange", 0xFFF28C38),
        ("Green", 0xFF5E9C4E),
        ("Cream", 0xFFFFE9D6),
    ];

    private SpriteProfile _spriteProfile = new();
    private bool _profileInitialized;

    private void InitializeProfile()
    {
        _spriteProfile = _preferences.Sprite ?? new SpriteProfile();
        SpriteNameBox.Text = _spriteProfile.Name;
        SpeciesBox.ItemsSource = SpriteArt.Species.Select(species => species.DisplayName).ToArray();
        SpeciesBox.SelectedIndex = Math.Max(0, SpriteArt.Species.ToList().FindIndex(species => species.Id == _spriteProfile.Species));
        AccessoryBox.ItemsSource = SpriteArt.Accessories.Select(ToDisplayName).ToArray();
        AccessoryBox.SelectedIndex = Math.Max(0, SpriteArt.Accessories.ToList().IndexOf(_spriteProfile.Accessory));
        BuildSwatches(BodySwatches, s_bodyColors, "Colour", isAccent: false);
        BuildSwatches(AccentSwatches, s_accentColors, "Accent", isAccent: true);
        _profileInitialized = true;
        ApplySpriteProfile();

        // Stop animating while the window is minimised or hidden.
        VisibilityChanged += (_, args) =>
        {
            SidebarSprite.Suspended = !args.Visible;
            ProfilePreview.Suspended = !args.Visible;
        };
    }

    private void ApplySpriteProfile()
    {
        SidebarSprite.Profile = _spriteProfile;
        ProfilePreview.Profile = _spriteProfile;
        var species = SpriteArt.Find(_spriteProfile.Species).DisplayName.ToLowerInvariant();
        var description = $"{_spriteProfile.Name} the {species}";
        AutomationProperties.SetName(SidebarSpriteButton, $"{description}. Edit profile");
        ToolTipService.SetToolTip(SidebarSpriteButton, description);
        AutomationProperties.SetName(ProfilePreview, description);
        UpdateSwatchSelection(BodySwatches, _spriteProfile.BodyColor);
        UpdateSwatchSelection(AccentSwatches, _spriteProfile.AccentColor);
    }

    private void UpdateSpriteProfile(SpriteProfile updated)
    {
        if (!_profileInitialized || updated == _spriteProfile)
        {
            return;
        }

        _spriteProfile = updated;
        ApplySpriteProfile();
        try
        {
            _preferences = _preferences with { Sprite = updated };
            PreferencesStore.Save(_preferences);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Message("Couldn't save your profile. Check your local application data folder.", InfoBarSeverity.Error);
        }
    }

    /// <summary>Keeps the companion's mood in step with playback.</summary>
    private void UpdateSpriteMood()
    {
        var mood = _playingTrack is null
            ? SpriteMood.Idle
            : _playbackState is PlaybackState.Playing or PlaybackState.Loading ? SpriteMood.Playing : SpriteMood.Paused;
        SidebarSprite.Mood = mood;
        ProfilePreview.Mood = mood;
    }

    private void ReactSprite(SpriteGlyph glyph)
    {
        SidebarSprite.React(glyph);
        ProfilePreview.React(glyph);
    }

    private async void SidebarSprite_Click(object sender, RoutedEventArgs e)
    {
        if (!_signingIn)
        {
            await NavigateAsync(AppView.Settings);
        }
    }

    private void SpriteName_TextChanged(object sender, TextChangedEventArgs e)
    {
        var name = SpriteNameBox.Text.Trim();
        UpdateSpriteProfile(_spriteProfile with { Name = name.Length > 0 ? name : new SpriteProfile().Name });
    }

    private void Species_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SpeciesBox.SelectedIndex >= 0)
        {
            UpdateSpriteProfile(_spriteProfile with { Species = SpriteArt.Species[SpeciesBox.SelectedIndex].Id });
        }
    }

    private void Accessory_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AccessoryBox.SelectedIndex >= 0)
        {
            UpdateSpriteProfile(_spriteProfile with { Accessory = SpriteArt.Accessories[AccessoryBox.SelectedIndex] });
        }
    }

    private void BuildSwatches(StackPanel host, (string Name, uint Color)[] colors, string label, bool isAccent)
    {
        host.Children.Clear();
        foreach (var (name, color) in colors)
        {
            var swatch = new Button
            {
                Width = SwatchSize,
                Height = SwatchSize,
                MinHeight = 0,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(SwatchSize / 2),
                Tag = color,
                Background = color == 0 ? AppearanceBrush("GlassSurface") : new SolidColorBrush(ToColor(color)),
                Content = color == 0 ? new TextBlock { Text = "A", FontSize = 12, Style = (Style)Application.Current.Resources["ButtonLabelStyle"] } : null
            };
            AutomationProperties.SetName(swatch, $"{label}: {name}");
            ToolTipService.SetToolTip(swatch, name);
            swatch.Click += (_, _) => UpdateSpriteProfile(isAccent
                ? _spriteProfile with { AccentColor = color }
                : _spriteProfile with { BodyColor = color });
            host.Children.Add(swatch);
        }
    }

    private void UpdateSwatchSelection(StackPanel host, uint selected)
    {
        foreach (var swatch in host.Children.OfType<Button>())
        {
            var isSelected = swatch.Tag is uint color && color == selected;
            swatch.BorderBrush = AppearanceBrush(isSelected ? "Accent" : "GlassEdge");
            swatch.BorderThickness = new Thickness(isSelected ? 3 : 1);
        }
    }

    private static Color ToColor(uint argb) =>
        Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    private static string ToDisplayName(string id) => id.Length == 0 ? id : char.ToUpperInvariant(id[0]) + id[1..];
}
