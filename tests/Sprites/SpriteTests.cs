using Cadenza.Core.Sprites;

namespace Cadenza.UnitTests.Sprites;

[TestClass]
public sealed class SpriteTests
{
    private const string NoAccessory = "none";

    private static readonly SpriteFrame s_still = new(false, 0, SpriteGlyph.None, 0);
    private static readonly SpriteProfile s_cat = new("cat");

    [TestMethod]
    public void EverySpeciesArtIsSquareAtTheSpriteSize()
    {
        foreach (var species in SpriteArt.Species)
        {
            Assert.IsTrue(
                species.Rows.Length == SpriteArt.Size && species.Rows.All(row => row.Length == SpriteArt.Size),
                $"{species.Id} art is {SpriteArt.Size}x{SpriteArt.Size}");
        }
    }

    [TestMethod]
    public void EverySpeciesArtUsesOnlyKnownPaletteKeys()
    {
        foreach (var species in SpriteArt.Species)
        {
            Assert.IsTrue(
                species.Rows.SelectMany(row => row).All(key => ".OBSAWPHEe".Contains(key, StringComparison.Ordinal)),
                $"{species.Id} art uses only known palette keys");
        }
    }

    [TestMethod]
    public void FrameHasTheComposerDimensions()
    {
        var frame = SpriteComposer.Compose(s_cat, s_still);

        Assert.HasCount(SpriteComposer.Width * SpriteComposer.Height, frame, "Frame has the composer's dimensions");
    }

    [TestMethod]
    public void FrameDrawsVisiblePixels()
    {
        var frame = SpriteComposer.Compose(s_cat, s_still);

        Assert.Contains(pixel => pixel >> 24 != 0, frame, "Frame draws visible pixels");
    }

    [TestMethod]
    public void BlinkingChangesTheEyes()
    {
        var open = SpriteComposer.Compose(s_cat, s_still);

        var blinking = SpriteComposer.Compose(s_cat, s_still with { Blink = true });

        Assert.IsFalse(open.SequenceEqual(blinking), "Blinking changes the eyes");
    }

    [TestMethod]
    public void BodyColourRecoloursTheArt()
    {
        var original = SpriteComposer.Compose(s_cat, s_still);

        var recoloured = SpriteComposer.Compose(s_cat with { BodyColor = 0xFF5B6478 }, s_still);

        Assert.IsFalse(original.SequenceEqual(recoloured), "Body colour recolours the art");
    }

    [TestMethod]
    public void UnknownSpeciesFallsBackToTheFirstSpecies()
    {
        var unknown = SpriteComposer.Compose(new SpriteProfile("unknown"), s_still);

        Assert.IsTrue(unknown.SequenceEqual(SpriteComposer.Compose(new SpriteProfile("cat"), s_still)), "Unknown species falls back to the first species");
    }

    [TestMethod]
    public void EveryAccessoryIsDrawn()
    {
        var bare = SpriteComposer.Compose(s_cat with { Accessory = NoAccessory }, s_still);

        foreach (var accessory in SpriteArt.Accessories.Where(name => name != NoAccessory))
        {
            Assert.IsFalse(bare.SequenceEqual(SpriteComposer.Compose(s_cat with { Accessory = accessory }, s_still)), $"{accessory} is drawn");
        }
    }

    [TestMethod]
    public void BobbingMovesTheCompanion()
    {
        var bare = SpriteComposer.Compose(s_cat with { Accessory = NoAccessory }, s_still);

        var bobbed = SpriteComposer.Compose(s_cat with { Accessory = NoAccessory }, s_still with { Bob = -1 });

        Assert.IsFalse(bare.SequenceEqual(bobbed), "Bobbing moves the companion");
    }

    [TestMethod]
    public void ShadeDarkensEachChannelAndKeepsAlpha() =>
        Assert.AreEqual(0xFF404040, SpriteComposer.Darken(0xFF808080, 0.5), "Shade darkens each channel and keeps alpha");
}
