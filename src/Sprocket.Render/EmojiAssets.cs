using System.Globalization;
using System.Text;
using SkiaSharp;

namespace Sprocket.Render;

/// <summary>
/// The bundled emoji glyphs (Etapa C): 512×512 transparent PNGs rasterised once from the Twemoji SVG set
/// (CC-BY 4.0 — see THIRD-PARTY-NOTICES.md) and embedded under <c>Assets/Emoji/</c>, so the emoji
/// transitions and stickers render byte-identically across OSes — the same embed-for-determinism
/// rationale as <see cref="TitleFonts"/>: a system emoji font is never consulted. Each glyph is decoded
/// once into a cached premultiplied <see cref="SKImage"/> and sampled as a plain texture afterwards —
/// no per-frame work (§1). An emoji without a bundled glyph yields <see langword="null"/> and callers
/// degrade to the cross dissolve.
/// </summary>
public static class EmojiAssets
{
    /// <summary>The bundled emoji characters, in «Autocolantes» palette display order.</summary>
    public static readonly IReadOnlyList<string> Bundled =
    [
        "🔥", "❤️", "😂", "😮", "🎉", "👍", "😎", "😱",
        "✨", "😭", "🥱", "💀", "👀", "🤩", "😴", "🎬",
    ];

    private static readonly Dictionary<string, SKImage?> Cache = new();
    private static readonly Lock Gate = new();

    /// <summary>
    /// The cached premultiplied glyph for <paramref name="emoji"/>, or <see langword="null"/> when that
    /// emoji has no bundled PNG. Callers must not dispose the returned image — it is shared for the
    /// process lifetime.
    /// </summary>
    public static SKImage? Get(string? emoji)
    {
        if (string.IsNullOrEmpty(emoji))
            return null;
        string name = AssetName(emoji);
        lock (Gate)
        {
            if (Cache.TryGetValue(name, out SKImage? cached))
                return cached;
            SKImage? image = Load(name);
            Cache[name] = image;
            return image;
        }
    }

    /// <summary>The embedded resource stem for an emoji — its code points in lowercase hex joined by
    /// <c>-</c>, variation selectors (U+FE0F) dropped: "🔥" → <c>emoji_1f525</c>, "❤️" → <c>emoji_2764</c>.</summary>
    public static string AssetName(string emoji)
    {
        var parts = new List<string>(emoji.Length);
        foreach (Rune rune in emoji.EnumerateRunes())
        {
            if (rune.Value == 0xFE0F)
                continue;
            parts.Add(rune.Value.ToString("x", CultureInfo.InvariantCulture));
        }
        return "emoji_" + string.Join("-", parts);
    }

    private static SKImage? Load(string name)
    {
        using Stream? stream = typeof(EmojiAssets).Assembly
            .GetManifestResourceStream($"Sprocket.Render.Assets.Emoji.{name}.png");
        if (stream is null)
            return null;
        // PNGs decode unpremultiplied while the transition shaders operate on premultiplied colour —
        // drawing the bitmap onto a premul surface converts exactly once, at cache time.
        using SKBitmap bitmap = SKBitmap.Decode(stream);
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return null;
        var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info);
        surface.Canvas.DrawBitmap(bitmap, 0, 0);
        return surface.Snapshot();
    }
}
