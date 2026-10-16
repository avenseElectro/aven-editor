using SkiaSharp;
using Sprocket.Render;
using Xunit;

namespace Sprocket.Render.Tests;

/// <summary>
/// Exercises the bundled emoji glyph cache (Etapa C): the code-point → resource-name mapping and the
/// load of every glyph listed in <see cref="EmojiAssets.Bundled"/> — the same embed-for-determinism
/// contract the title fonts carry (§5).
/// </summary>
public sealed class EmojiAssetsTests
{
    [Theory]
    [InlineData("🔥", "emoji_1f525")]
    [InlineData("❤️", "emoji_2764")]   // variation selector U+FE0F is dropped
    [InlineData("🎬", "emoji_1f3ac")]
    [InlineData("🫠", "emoji_1fae0")]  // name maps even though no PNG is bundled for it
    public void AssetName_MapsCodePointsToHex_DroppingVariationSelectors(string emoji, string expected)
    {
        Assert.Equal(expected, EmojiAssets.AssetName(emoji));
    }

    [Fact]
    public void EveryBundledEmoji_LoadsA512Glyph()
    {
        foreach (string emoji in EmojiAssets.Bundled)
        {
            SKImage? glyph = EmojiAssets.Get(emoji);
            Assert.NotNull(glyph);
            Assert.Equal(512, glyph!.Width);
            Assert.Equal(512, glyph.Height);
        }
    }

    [Fact]
    public void UnbundledEmoji_YieldsNull_WithoutThrowing()
    {
        Assert.Null(EmojiAssets.Get("🫠"));
        Assert.Null(EmojiAssets.Get(null));
        Assert.Null(EmojiAssets.Get(""));
    }

    [Fact]
    public void Get_ReturnsTheCachedInstance()
    {
        // The glyphs are shared for the process lifetime; a second lookup must be the same SKImage.
        Assert.Same(EmojiAssets.Get("🔥"), EmojiAssets.Get("🔥"));
    }
}
