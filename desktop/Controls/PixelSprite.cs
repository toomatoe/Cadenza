using System.Runtime.InteropServices.WindowsRuntime;
using Cadenza.Core.Sprites;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI.ViewManagement;

namespace Cadenza.Desktop.Controls;

/// <summary>What the companion is doing; set from playback state.</summary>
public enum SpriteMood
{
    Idle,
    Playing,
    Paused
}

/// <summary>
/// Draws a <see cref="SpriteProfile"/> companion as crisp pixel art and animates it at a low frame rate.
/// The bitmap is scaled by a whole number of physical pixels for the current display scale, so pixels
/// never blur. Animation stops while hidden and holds a still frame when Windows animations are off.
/// </summary>
public sealed partial class PixelSprite : Grid
{
    private const int TicksPerSecond = 10;
    private const int BeatTicks = 3;
    private const int BlinkPeriodTicks = 42;
    private const int NotePeriodTicks = 24;
    private const int ReactionTicks = 16;

    private readonly Image _image = new() { Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly UISettings _uiSettings = new();
    private DispatcherQueueTimer? _timer;
    private WriteableBitmap? _bitmap;
    private int _pixelScale;
    private int _tick;
    private int _reactionStart = -1;
    private SpriteGlyph _reaction;
    private SpriteFrame? _lastFrame;
    private SpriteProfile? _lastProfile;
    private SpriteProfile _profile = new();
    private SpriteMood _mood;
    private bool _suspended;

    public PixelSprite()
    {
        Children.Add(_image);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>Approximate on-screen size in device-independent pixels; snapped to whole pixel multiples.</summary>
    public double DisplaySize { get; set; } = 72;

    public SpriteProfile Profile
    {
        get => _profile;
        set
        {
            _profile = value ?? new SpriteProfile();
            Render();
        }
    }

    public SpriteMood Mood
    {
        get => _mood;
        set
        {
            _mood = value;
            Render();
        }
    }

    /// <summary>Pauses animation, for example while the window is minimised.</summary>
    public bool Suspended
    {
        get => _suspended;
        set
        {
            _suspended = value;
            UpdateTimer();
        }
    }

    private bool AnimationsEnabled => _uiSettings.AnimationsEnabled;

    /// <summary>Shows a short-lived glyph, such as a heart when a track is liked.</summary>
    public void React(SpriteGlyph glyph)
    {
        _reaction = glyph;
        _reactionStart = _tick;
        Render();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _timer ??= CreateTimer();
        if (XamlRoot is not null)
            XamlRoot.Changed += XamlRoot_Changed;

        UpdateTimer();
        Render();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _timer?.Stop();
        if (XamlRoot is not null)
            XamlRoot.Changed -= XamlRoot_Changed;
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => Render();

    private DispatcherQueueTimer CreateTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(1000 / TicksPerSecond);
        timer.Tick += (_, _) =>
        {
            _tick++;
            Render();
        };

        return timer;
    }

    private void UpdateTimer()
    {
        if (_timer is null)
            return;

        if (_suspended || !AnimationsEnabled || !IsLoaded)
            _timer.Stop();
        else if (!_timer.IsRunning)
            _timer.Start();
    }

    private SpriteFrame CurrentFrame()
    {
        var animate = AnimationsEnabled && !_suspended;
        if (_reactionStart >= 0 && _tick - _reactionStart < ReactionTicks)
        {
            var rise = animate ? Math.Min((_tick - _reactionStart) / 4, 2) : 0;
            return new SpriteFrame(false, 0, _reaction, rise);
        }

        return _mood switch
        {
            SpriteMood.Paused => new SpriteFrame(true, 0, SpriteGlyph.Sleep, animate ? (_tick / 6) % 3 : 0),
            SpriteMood.Playing when animate => new SpriteFrame(
                false,
                (_tick / BeatTicks) % 2 == 0 ? 0 : -1,
                _tick % NotePeriodTicks < NotePeriodTicks / 2 ? SpriteGlyph.Note : SpriteGlyph.None,
                (_tick % NotePeriodTicks) / 4),
            SpriteMood.Playing => new SpriteFrame(false, 0, SpriteGlyph.Note, 0),
            _ => new SpriteFrame(animate && _tick % BlinkPeriodTicks == 0, 0, SpriteGlyph.None, 0)
        };
    }

    private void Render()
    {
        if (!IsLoaded)
            return;

        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        var pixelScale = Math.Max(1, (int)Math.Round(DisplaySize * scale / SpriteComposer.Width));
        var frame = CurrentFrame();
        if (pixelScale == _pixelScale && _lastFrame == frame && ReferenceEquals(_lastProfile, _profile))
            return;

        if (pixelScale != _pixelScale || _bitmap is null)
        {
            _pixelScale = pixelScale;
            _bitmap = new WriteableBitmap(SpriteComposer.Width * pixelScale, SpriteComposer.Height * pixelScale);
            _image.Source = _bitmap;
            _image.Width = _bitmap.PixelWidth / scale;
            _image.Height = _bitmap.PixelHeight / scale;
        }

        _lastFrame = frame;
        _lastProfile = _profile;
        WritePixels(_bitmap, SpriteComposer.Compose(_profile, frame), pixelScale);
    }

    /// <summary>Copies 0xAARRGGBB art into the bitmap as premultiplied BGRA, scaled by nearest neighbour.</summary>
    private static void WritePixels(WriteableBitmap bitmap, uint[] art, int scale)
    {
        var width = bitmap.PixelWidth;
        var bytes = new byte[width * bitmap.PixelHeight * 4];
        for (var y = 0; y < SpriteComposer.Height; y++)
        {
            for (var x = 0; x < SpriteComposer.Width; x++)
            {
                var argb = art[(y * SpriteComposer.Width) + x];
                if (argb >> 24 == 0)
                    continue;

                // The art is fully opaque or fully transparent, so premultiplying is a no-op.
                var b = (byte)argb;
                var g = (byte)(argb >> 8);
                var r = (byte)(argb >> 16);
                for (var dy = 0; dy < scale; dy++)
                {
                    var rowStart = (((y * scale) + dy) * width + (x * scale)) * 4;
                    for (var dx = 0; dx < scale; dx++)
                    {
                        var i = rowStart + (dx * 4);
                        bytes[i] = b;
                        bytes[i + 1] = g;
                        bytes[i + 2] = r;
                        bytes[i + 3] = 0xFF;
                    }
                }
            }
        }

        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(bytes, 0, bytes.Length);
        }

        bitmap.Invalidate();
    }
}
