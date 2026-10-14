namespace Cadenza.Core.Sprites;

/// <summary>A configured companion: species, colours, accessory and name.</summary>
/// <param name="Species">Species id from <see cref="SpriteArt.Species"/>.</param>
/// <param name="BodyColor">Body colour as 0xAARRGGBB; 0 uses the species default.</param>
/// <param name="AccentColor">Accent colour as 0xAARRGGBB; 0 uses the species default.</param>
/// <param name="Accessory">One of <see cref="SpriteArt.Accessories"/>.</param>
/// <param name="Name">Shown in tooltips and, later, to friends.</param>
public sealed record SpriteProfile(
    string Species = "cat",
    uint BodyColor = 0,
    uint AccentColor = 0,
    string Accessory = "headphones",
    string Name = "Mochi");

/// <summary>One animation frame's state.</summary>
/// <param name="Blink">Eyes closed.</param>
/// <param name="Bob">Vertical offset in pixels (negative is up).</param>
/// <param name="Glyph">Mood glyph floating above the companion.</param>
/// <param name="GlyphRise">How far the glyph has floated up, in pixels.</param>
public readonly record struct SpriteFrame(bool Blink, int Bob, SpriteGlyph Glyph, int GlyphRise);

/// <summary>
/// Composes a companion frame into a 0xAARRGGBB pixel buffer of <see cref="Width"/> × <see cref="Height"/>.
/// Pure and allocation-light so the UI can redraw at a low frame rate and tests can check the output.
/// </summary>
public static class SpriteComposer
{
    public const int Width = SpriteArt.Size;

    /// <summary>Art plus headroom for accessories and mood glyphs.</summary>
    public const int Height = SpriteArt.Size + TopMargin;

    private const int TopMargin = 4;
    private const int GroundRow = 21;

    private const uint Transparent = 0x00000000;
    private const uint Outline = 0xFF1F2A3D;
    private const uint Highlight = 0xFFFFFFFF;
    private const uint HeadphoneBand = 0xFF2A74CF;
    private const uint BeanieBody = 0xFF3D6FB6;
    private const uint BeanieRim = 0xFF6E97D6;
    private const uint BeaniePom = 0xFFEEF3FA;
    private const uint ScarfBody = 0xFFE05F5F;
    private const uint ScarfShade = 0xFFB94646;
    private const uint FlowerPetal = 0xFFF59BB8;
    private const uint FlowerCenter = 0xFFFFD34D;

    private static readonly Dictionary<SpriteGlyph, uint> _glyphColors = new()
    {
        [SpriteGlyph.Note] = Outline,
        [SpriteGlyph.Heart] = 0xFFE05F8E,
        [SpriteGlyph.Sleep] = 0xFF5A7BA6,
        [SpriteGlyph.Question] = 0xFF2A74CF,
    };

    public static uint[] Compose(SpriteProfile profile, SpriteFrame frame)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var species = SpriteArt.Find(profile.Species);
        var body = profile.BodyColor != 0 ? profile.BodyColor : species.DefaultPalette.Body;
        var accent = profile.AccentColor != 0 ? profile.AccentColor : species.DefaultPalette.Accent;
        var shade = Darken(body, 0.18);

        var pixels = new uint[Width * Height];
        var originY = TopMargin + GroundOffset(species) + frame.Bob;

        for (var row = 0; row < SpriteArt.Size; row++)
        {
            var line = species.Rows[row];
            for (var column = 0; column < SpriteArt.Size; column++)
            {
                var color = line[column] switch
                {
                    'O' => Outline,
                    'B' => body,
                    'S' => shade,
                    'A' => accent,
                    'W' => species.DefaultPalette.Belly,
                    'P' => species.DefaultPalette.Cheek,
                    'H' => frame.Blink ? body : Highlight,
                    'E' => frame.Blink ? body : Outline,
                    'e' => Outline,
                    _ => Transparent
                };
                Set(pixels, column, originY + row, color);
            }
        }

        DrawAccessory(pixels, species, profile.Accessory, originY);
        DrawGlyph(pixels, frame.Glyph, frame.GlyphRise);

        return pixels;
    }

    /// <summary>Darkens a colour by <paramref name="amount"/> (0–1), keeping its alpha.</summary>
    public static uint Darken(uint color, double amount)
    {
        static uint Channel(uint value, double factor) => (uint)Math.Clamp(Math.Round(value * factor), 0, 255);
        var factor = 1 - amount;
        var r = Channel((color >> 16) & 0xFF, factor);
        var g = Channel((color >> 8) & 0xFF, factor);
        var b = Channel(color & 0xFF, factor);

        return (color & 0xFF000000) | (r << 16) | (g << 8) | b;
    }

    /// <summary>Shifts art so every species stands on the same ground line.</summary>
    private static int GroundOffset(SpeciesArt species)
    {
        for (var row = SpriteArt.Size - 1; row >= 0; row--)
        {
            if (species.Rows[row].Any(key => key != '.'))
                return GroundRow - row;
        }

        return 0;
    }

    private static void DrawAccessory(uint[] pixels, SpeciesArt species, string accessory, int originY)
    {
        var head = species.Head;
        var top = originY + head.Top;
        var left = head.Left;
        var right = head.Right;
        switch (accessory)
        {
            case "headphones":
                for (var x = left; x <= right; x++)
                {
                    Set(pixels, x, top - 2, Outline);
                    Set(pixels, x, top - 1, HeadphoneBand);
                }

                for (var y = top - 1; y <= top + 2; y++)
                {
                    Set(pixels, left - 1, y, Outline);
                    Set(pixels, left, y, HeadphoneBand);
                    Set(pixels, right, y, HeadphoneBand);
                    Set(pixels, right + 1, y, Outline);
                }

                FillOutlined(pixels, left - 2, top + 3, 3, 5, HeadphoneBand);
                FillOutlined(pixels, right, top + 3, 3, 5, HeadphoneBand);
                break;

            case "beanie":
                for (var x = left + 2; x <= right - 2; x++)
                {
                    Set(pixels, x, top - 2, Outline);
                }

                for (var y = top - 1; y <= top; y++)
                {
                    Set(pixels, left + 1, y, Outline);
                    Set(pixels, right - 1, y, Outline);
                    for (var x = left + 2; x <= right - 2; x++)
                    {
                        Set(pixels, x, y, BeanieBody);
                    }
                }

                Set(pixels, left, top + 1, Outline);
                Set(pixels, right, top + 1, Outline);
                for (var x = left + 1; x <= right - 1; x++)
                {
                    Set(pixels, x, top + 1, BeanieRim);
                }

                var middle = (left + right) / 2;
                Set(pixels, middle, top - 4, Outline);
                Set(pixels, middle + 1, top - 4, Outline);
                Set(pixels, middle - 1, top - 3, Outline);
                Set(pixels, middle, top - 3, BeaniePom);
                Set(pixels, middle + 1, top - 3, BeaniePom);
                Set(pixels, middle + 2, top - 3, Outline);
                break;

            case "scarf":
                var neck = originY + head.Neck;
                var (bodyLeft, bodyRight) = RowBounds(species.Rows[head.Neck]);
                for (var x = bodyLeft; x <= bodyRight; x++)
                {
                    Set(pixels, x, neck, ScarfBody);
                    Set(pixels, x, neck + 1, x == bodyLeft || x == bodyRight ? ScarfShade : ScarfBody);
                }

                // The loose end hangs over the chest on the right.
                for (var y = neck + 2; y <= neck + 3; y++)
                {
                    Set(pixels, bodyRight - 3, y, Outline);
                    Set(pixels, bodyRight - 2, y, ScarfShade);
                    Set(pixels, bodyRight - 1, y, ScarfShade);
                    Set(pixels, bodyRight, y, Outline);
                }

                for (var x = bodyRight - 3; x <= bodyRight; x++)
                {
                    Set(pixels, x, neck + 4, Outline);
                }
                break;

            case "flower":
                var cx = right - 3;
                var cy = top;
                Set(pixels, cx, cy - 1, FlowerPetal);
                Set(pixels, cx - 1, cy, FlowerPetal);
                Set(pixels, cx + 1, cy, FlowerPetal);
                Set(pixels, cx, cy + 1, FlowerPetal);
                Set(pixels, cx, cy, FlowerCenter);
                break;
        }
    }

    private static void DrawGlyph(uint[] pixels, SpriteGlyph glyph, int rise)
    {
        if (glyph == SpriteGlyph.None || !SpriteArt.Glyphs.TryGetValue(glyph, out var rows))
            return;

        var color = _glyphColors[glyph];
        const int Left = Width - 6;
        var top = 1 - rise;
        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < rows[row].Length; column++)
            {
                if (rows[row][column] != '.')
                    Set(pixels, Left + column, top + row, color);
            }
        }
    }

    /// <summary>First and last non-outline, non-transparent columns of an art row.</summary>
    private static (int Left, int Right) RowBounds(string row)
    {
        var first = row.IndexOfAny(['B', 'S', 'W', 'A']);
        var last = row.LastIndexOfAny(['B', 'S', 'W', 'A']);

        return first < 0 ? (0, -1) : (first, last);
    }

    private static void FillOutlined(uint[] pixels, int x, int y, int width, int height, uint fill)
    {
        for (var dy = 0; dy < height; dy++)
        {
            for (var dx = 0; dx < width; dx++)
            {
                var edge = dx == 0 || dy == 0 || dx == width - 1 || dy == height - 1;
                Set(pixels, x + dx, y + dy, edge ? Outline : fill);
            }
        }
    }

    private static void Set(uint[] pixels, int x, int y, uint color)
    {
        if (color == Transparent || x < 0 || y < 0 || x >= Width || y >= Height)
            return;

        pixels[(y * Width) + x] = color;
    }
}
