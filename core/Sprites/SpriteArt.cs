namespace Cadenza.Core.Sprites;

/// <summary>
/// Original 24×24 pixel art for the profile companions, released under CC0 1.0 (public domain) as
/// part of Cadenza. Each row is a string of palette keys:
/// <list type="table">
/// <item><term>.</term><description>transparent</description></item>
/// <item><term>O</term><description>outline</description></item>
/// <item><term>B</term><description>body (recolourable)</description></item>
/// <item><term>S</term><description>body shade (derived from the body colour)</description></item>
/// <item><term>A</term><description>accent (recolourable)</description></item>
/// <item><term>W</term><description>belly / light patch</description></item>
/// <item><term>P</term><description>cheeks and nose</description></item>
/// <item><term>H</term><description>eye highlight</description></item>
/// <item><term>E</term><description>upper eye (becomes body colour when blinking)</description></item>
/// <item><term>e</term><description>lower eye (becomes outline when blinking)</description></item>
/// </list>
/// </summary>
public static class SpriteArt
{
    public const int Size = 24;

    public static IReadOnlyList<SpeciesArt> Species { get; } =
    [
        new(
            "cat",
            "Cat",
            new SpritePalette(Body: 0xFF9DB2CF, Accent: 0xFFF2AFC0, Belly: 0xFFEEF3FA, Cheek: 0xFFF2AFC0),
            new HeadAnchor(Top: 5, Left: 3, Right: 20, Neck: 13),
            [
                "........................",
                "........................",
                ".....O............O.....",
                ".....OO..........OO.....",
                ".....OAO........OAO.....",
                ".....OAAOOOOOOOOAAO.....",
                "....OBBBBBBBBBBBBBBO....",
                "...OBBBBBBBBBBBBBBBBO...",
                "...OBBHEBBBBBBBBHEBBO...",
                "...OBBeeBBBBBBBBeeBBO...",
                "...OBPBBBBBPPBBBBBPBO...",
                "...OBBBBBBOBBOBBBBBBO...",
                "....OBBBBBBBBBBBBBBO....",
                ".....OOBBBBBBBBBBOO.....",
                "......OBWWWWWWWWBO...OO.",
                ".....OBBWWWWWWWWBBO.OAAO",
                ".....OBBWWWWWWWWBBO.OBO.",
                ".....OBBWWWWWWWWBBOOBBO.",
                ".....OBBBWWWWWWBBBOBBO..",
                ".....OBBBBBBBBBBBBOOO...",
                ".....OSSOSSSSSSOSSO.....",
                ".....OOOOOOOOOOOOOO.....",
                "........................",
                "........................",
            ]),
        new(
            "panda",
            "Panda",
            new SpritePalette(Body: 0xFFFFFFFF, Accent: 0xFF000000, Belly: 0xFFBDBDBD, Cheek: 0xFFF2AFC0),
            new HeadAnchor(Top: 1, Left: 1, Right: 22, Neck: 14),
            [
                "...AAA............AAA...",
                "..AAAAA.AAAAAAAA.AAAAA..",
                ".AAAAAAABBBBBBBBAAAAAAA.",
                ".AAAABBBBBBBBBBBBBBAAAA.",
                ".AAABBBBBBBBBBBBBBBBAAA.",
                "..ABBBBBBBBBBBBBBBBBBA..",
                "..ABBBBBBBBBBBBBBBBBBA..",
                ".ABBBBAAABBBBBBAAABBBBA.",
                ".ABBBAAAAABBBBAAAAABBBA.",
                ".ABBAAAABABBBBABAAAABBA.",
                ".ABBAAAABABBBBABAAAABBA.",
                ".ABBAAAAABBBBBBAAAAABBA.",
                ".ABBBAAABBBAABBBAAABBBA.",
                "..ABBBBBBBBAABBBBBBBBA..",
                "..AAAABWWWBBBBWWWBAAAA..",
                ".AAAAAAWWWWBBWWWWAAAAAA.",
                ".AAAAAAAWWWWWWWWAAAAAAA.",
                ".AAAAAAAWWWWWWWWAAAAAAA.",
                ".AAAAAAAWWWWWWWWAAAAAAA.",
                "..AAAAAAWWWWWWWWAAAAAA..",
                "...AAAABWWWWWWWWBAAAA...",
                ".........WWWWWW.........",
                "..........WWWW..........",
                "...........WW...........",
            ]),
        new(
            "fox",
            "Fox",
            new SpritePalette(Body: 0xFFE8894A, Accent: 0xFFFFE9D6, Belly: 0xFFFFF6EC, Cheek: 0xFFF5A9A0),
            new HeadAnchor(Top: 5, Left: 2, Right: 21, Neck: 13),
            [
                "........................",
                "....O..............O....",
                "....OO............OO....",
                "....OAO..........OAO....",
                "....OAAO........OAAO....",
                "....OAABOOOOOOOOBAAO....",
                "...OBBBBBBBBBBBBBBBBO...",
                "..OBBBBBBBBBBBBBBBBBBO..",
                "..OBBBHEBBBBBBBBHEBBBO..",
                "..OBBBeeBBBBBBBBeeBBBO..",
                "..OWWWWBBBBBBBBBBWWWWO..",
                "...OWWWWWWWOOWWWWWWWO...",
                "....OWWWWWWWWWWWWWWO....",
                ".....OOWWWWWWWWWWOO.....",
                "......OBBWWWWWWBBO......",
                ".....OBBBWWWWWWBBBO.....",
                ".....OBBBWWWWWWBBBO.OOO.",
                ".....OBBBWWWWWWBBBOOBBAO",
                ".....OBBBBWWWWBBBBOBBAAO",
                ".....OBBBBBBBBBBBBBBAAO.",
                ".....OSSOSSSSSSOSSOOOOO.",
                ".....OOOOOOOOOOOOOO.....",
                "........................",
                "........................",
            ]),
        new(
            "frog",
            "Frog",
            new SpritePalette(Body: 0xFF8FCB7B, Accent: 0xFF5E9C4E, Belly: 0xFFF4FBEF, Cheek: 0xFFF2A7A7),
            new HeadAnchor(Top: 6, Left: 2, Right: 21, Neck: 11),
            [
                "........................",
                "........................",
                "......OOOO....OOOO......",
                ".....OWWWWO..OWWWWO.....",
                ".....OWHEWO..OWHEWO.....",
                ".....OWeeWOOOOWeeWO.....",
                "....OBBBBBBBBBBBBBBO....",
                "...OBBBBBBBBBBBBBBBBO...",
                "..OBPBBBBBBBBBBBBBBPBO..",
                "..OBBOBBBBBBBBBBBBOBBO..",
                "..OBBBOOOOOOOOOOOOBBBO..",
                "...OBBBBBBBBBBBBBBBBO...",
                "....OBBWWWWWWWWWWBBO....",
                "....OBWWWWWWWWWWWWBO....",
                "...OBBWWWWWWWWWWWWBBO...",
                "..OSBBWWWWWWWWWWWWBBSO..",
                "..OSSBBWWWWWWWWWWBBSSO..",
                ".OSSOOBBBBBBBBBBBBOOSSO.",
                ".OOO..OOOOOOOOOOOO..OOO.",
                "........................",
                "........................",
                "........................",
                "........................",
                "........................",
            ]),
        new(
            "bunny",
            "Bunny",
            new SpritePalette(Body: 0xFFF3EEF6, Accent: 0xFFF6B9C9, Belly: 0xFFFFFFFF, Cheek: 0xFFF6B9C9),
            new HeadAnchor(Top: 6, Left: 3, Right: 20, Neck: 14),
            [
                ".......OO......OO.......",
                "......OBBO....OBBO......",
                "......OBAO....OABO......",
                "......OBAO....OABO......",
                "......OBAO....OABO......",
                "......OBBO....OBBO......",
                ".....OOBBOOOOOOBBOO.....",
                "....OBBBBBBBBBBBBBBO....",
                "...OBBBBBBBBBBBBBBBBO...",
                "...OBBHEBBBBBBBBHEBBO...",
                "...OBBeeBBBBBBBBeeBBO...",
                "...OBPBBBBBPPBBBBBPBO...",
                "...OBBBBBBBOOBBBBBBBO...",
                "....OBBBBBBBBBBBBBBO....",
                ".....OOBWWWWWWWWBOO.....",
                "......OBWWWWWWWWBO......",
                ".....OBBWWWWWWWWBBO.....",
                ".....OBBWWWWWWWWBBO.....",
                ".....OBBBWWWWWWBBBO.....",
                ".....OBBBBBBBBBBBBO.....",
                ".....OSSOSSSSSSOSSO.....",
                ".....OOOOOOOOOOOOOO.....",
                "........................",
                "........................",
            ]),
        new(
            "chick",
            "Chick",
            new SpritePalette(Body: 0xFFFFD966, Accent: 0xFFF28C38, Belly: 0xFFFFF2BF, Cheek: 0xFFF7A8A0),
            new HeadAnchor(Top: 6, Left: 3, Right: 20, Neck: 12),
            [
                "........................",
                "........................",
                "........................",
                "...........OO...........",
                "..........OBBO..........",
                ".......OOOOBBOOOO.......",
                ".....OOBBBBBBBBBBOO.....",
                "....OBBBBBBBBBBBBBBO....",
                "...OBBBBBBBBBBBBBBBBO...",
                "...OBBHEBBBBBBBBHEBBO...",
                "...OBBeeBBBBBBBBeeBBO...",
                "..OBBPBBBBAAAABBBBPBBO..",
                "..OBBBBBBBBAABBBBBBBBO..",
                ".OSBBBBBBBBBBBBBBBBBBSO.",
                ".OSSBBBWWWWWWWWWWBBBSSO.",
                ".OSSBBWWWWWWWWWWWWBBSSO.",
                "..OSSBWWWWWWWWWWWWBSSO..",
                "...OOBBWWWWWWWWWWBBOO...",
                ".....OBBBBBBBBBBBBO.....",
                "......OOOOOOOOOOOO......",
                ".......OAAO..OAAO.......",
                ".......OOOO..OOOO.......",
                "........................",
                "........................",
            ]),
        new(
            "axolotl",
            "Axolotl",
            new SpritePalette(Body: 0xFFF7B7CF, Accent: 0xFFE05F8E, Belly: 0xFFFFE3EE, Cheek: 0xFFE66C97),
            new HeadAnchor(Top: 5, Left: 4, Right: 19, Neck: 13),
            [
                "........................",
                "........................",
                "........................",
                "........................",
                "........................",
                ".O....OOOOOOOOOOOO....O.",
                "OAO..OBBBBBBBBBBBBO..OAO",
                ".OAOOBBBBBBBBBBBBBBOOAO.",
                "OAAAOBBBBBBBBBBBBBBOAAAO",
                ".OOOBBHEBBBBBBBBHEBBOOO.",
                "OAAOBBeeBBBBBBBBeeBBOAAO",
                ".OO.OBPBBBBOOBBBBPBO.OO.",
                ".....OBBBBBBBBBBBBO.....",
                "......OOBBBBBBBBOO......",
                ".....OBBWWWWWWWWBBO.....",
                "....OBBBWWWWWWWWBBBO....",
                "....OBBBWWWWWWWWBBBOOO..",
                "....OBBBBWWWWWWBBBBOBBO.",
                "....OBBBBBBBBBBBBBBBBBO.",
                "....OSSOSSSSSSSSOSSOOO..",
                "....OOOOOOOOOOOOOOOO....",
                "........................",
                "........................",
                "........................",
            ]),
    ];

    /// <summary>Accessories drawn over the head or neck, positioned from each species' <see cref="HeadAnchor"/>.</summary>
    public static IReadOnlyList<string> Accessories { get; } = ["none", "headphones", "beanie", "scarf", "flower"];

    /// <summary>Small 5-pixel-wide glyphs that float above the companion to show its mood.</summary>
    public static IReadOnlyDictionary<SpriteGlyph, string[]> Glyphs { get; } = new Dictionary<SpriteGlyph, string[]>
    {
        [SpriteGlyph.Note] = ["..OO.", "..O.O", "..O..", "OOO..", "OOO.."],
        [SpriteGlyph.Heart] = [".O.O.", "OOOOO", "OOOOO", ".OOO.", "..O.."],
        [SpriteGlyph.Sleep] = ["OOOO.", "..O..", ".O...", "OOOO."],
        [SpriteGlyph.Question] = [".OOO.", "O...O", "...O.", "..O..", ".....", "..O.."],
    };

    public static SpeciesArt Find(string id) =>
        Species.FirstOrDefault(species => string.Equals(species.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Species[0];
}

/// <summary>One companion species: its art, default colours and where accessories sit.</summary>
public sealed record SpeciesArt(string Id, string DisplayName, SpritePalette DefaultPalette, HeadAnchor Head, string[] Rows);

/// <summary>Recolourable parts of a species, as 0xAARRGGBB.</summary>
public sealed record SpritePalette(uint Body, uint Accent, uint Belly, uint Cheek);

/// <summary>Head bounds in art coordinates: top outline row, left and right outline columns, and the neck row.</summary>
public sealed record HeadAnchor(int Top, int Left, int Right, int Neck);

public enum SpriteGlyph
{
    None,
    Note,
    Heart,
    Sleep,
    Question
}
