using PokePic.Core.Assets;
using PokePic.Core.Data;
using PokePic.Core.Model;

namespace PokePic.Core.Rendering;

/// <param name="Mode">High resolution (anti-aliased) or pixel-perfect (no anti-aliasing, nearest-neighbor upscale).</param>
/// <param name="Scale">Output scale factor. Must be an integer in pixel-perfect mode.</param>
/// <param name="Language">The language of whoever uses the card (the data pack's, e.g. PKHeX's): the default.</param>
/// <param name="LanguageOverride">A language that wins over the template's (the editor's preview language), or null.</param>
/// <remarks>
/// Language of a localized text or image, from the strongest: the element's own, <paramref name="LanguageOverride"/>,
/// the template's, <paramref name="Language"/>; a language missing from the data falls back to its base language, then English.
/// </remarks>
public sealed record RenderOptions(RenderMode Mode, float Scale, string Language = Languages.Fallback, string? LanguageOverride = null)
{
    public static RenderOptions FromTemplate(CardTemplate template, string language = Languages.Fallback) =>
        new(template.Canvas.DefaultMode, template.Canvas.DefaultScale, language);

    /// <summary>Language for elements without their own: the override, else the template's, else the default.</summary>
    public string EffectiveLanguage(CardTemplate template) => LanguageOverride ?? template.Language ?? Language;

    /// <summary>
    /// Returns an error message if the options are not valid, null otherwise.
    /// </summary>
    public string? Validate()
    {
        if (!float.IsFinite(Scale) || Scale <= 0 || Scale > 16)
            return $"Scale {Scale} must be greater than 0 and at most 16.";
        if (Mode == RenderMode.PixelPerfect && Scale != MathF.Floor(Scale))
            return $"Pixel-perfect mode requires an integer scale, got {Scale}.";
        return null;
    }
}

/// <param name="Png">The card encoded as PNG, without any payload chunk.</param>
public sealed record RenderedCard(int Width, int Height, byte[] Png);

/// <summary>
/// Turns a validated template into something that can be rendered many times (e.g. a whole box).
/// </summary>
public interface ICardRenderer
{
    /// <summary>
    /// Loads and decodes the template's assets.
    /// </summary>
    /// <exception cref="TemplateException">An asset is missing or cannot be decoded.</exception>
    IPreparedTemplate Prepare(CardTemplate template, IAssetProvider assets);
}

public interface IPreparedTemplate : IDisposable
{
    CardTemplate Template { get; }

    /// <exception cref="TemplateException">The data does not match the template (missing key, wrong kind, unknown set key).</exception>
    RenderedCard Render(CardData data, RenderOptions options);
}
