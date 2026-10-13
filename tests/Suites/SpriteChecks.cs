using Cadenza.Core.Sprites;
using Cadenza.Tests.Infrastructure;

namespace Cadenza.Tests.Suites;

internal static class SpriteChecks
{
    private static readonly SpriteFrame s_still = new(false, 0, SpriteGlyph.None, 0);

    public static void Run(CheckRunner checks)
    {
        foreach (var species in SpriteArt.Species)
        {
            checks.Check(
                species.Rows.Length == SpriteArt.Size && species.Rows.All(row => row.Length == SpriteArt.Size),
                $"{species.Id} art is {SpriteArt.Size}x{SpriteArt.Size}");
            checks.Check(
                species.Rows.SelectMany(row => row).All(key => ".OBSAWPHEe".Contains(key, StringComparison.Ordinal)),
                $"{species.Id} art uses only known palette keys");
        }

        var cat = new SpriteProfile("cat");
        var open = SpriteComposer.Compose(cat, s_still);
        checks.Check(open.Length == SpriteComposer.Width * SpriteComposer.Height, "Frame has the composer's dimensions");
        checks.Check(open.Any(pixel => pixel >> 24 != 0), "Frame draws visible pixels");
        checks.Check(!open.SequenceEqual(SpriteComposer.Compose(cat, s_still with { Blink = true })), "Blinking changes the eyes");
        checks.Check(
            !open.SequenceEqual(SpriteComposer.Compose(cat with { BodyColor = 0xFF5B6478 }, s_still)),
            "Body colour recolours the art");
        checks.Check(
            SpriteComposer.Compose(new SpriteProfile("unknown"), s_still).SequenceEqual(SpriteComposer.Compose(new SpriteProfile("cat"), s_still)),
            "Unknown species falls back to the first species");

        var bare = SpriteComposer.Compose(cat with { Accessory = "none" }, s_still);
        foreach (var accessory in SpriteArt.Accessories.Where(name => name != "none"))
        {
            checks.Check(!bare.SequenceEqual(SpriteComposer.Compose(cat with { Accessory = accessory }, s_still)), $"{accessory} is drawn");
        }

        var bobbed = SpriteComposer.Compose(cat with { Accessory = "none" }, s_still with { Bob = -1 });
        checks.Check(!bare.SequenceEqual(bobbed), "Bobbing moves the companion");
        checks.Check(SpriteComposer.Darken(0xFF808080, 0.5) == 0xFF404040, "Shade darkens each channel and keeps alpha");
    }
}
