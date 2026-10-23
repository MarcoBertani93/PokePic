using PokePic.Core;
using PokePic.Core.Assets;
using PokePic.Core.Data;
using PokePic.Core.Model;
using PokePic.Core.Rendering;
using SkiaSharp;

namespace PokePic.Rendering;

/// <summary>
/// SkiaSharp implementation of <see cref="ICardRenderer"/>.
/// </summary>
public sealed class SkiaCardRenderer : ICardRenderer
{
    /// <summary>
    /// Files written directly (not depending on the data) are loaded now, so a broken file is reported when the template
    /// opens; the others (paths composed from the data, items of patterns) when first used.
    /// </summary>
    public IPreparedTemplate Prepare(CardTemplate template, IAssetProvider assets)
    {
        var issues = new List<TemplateIssue>();
        var files = new FileCache(assets);
        foreach (var (path, kind, file) in StaticFiles(template))
        {
            try
            {
                if (kind == ValueKind.Font)
                    files.Typeface(file, path);
                else
                    files.Image(file, path);
            }
            catch (TemplateException ex)
            {
                issues.AddRange(ex.Issues);
            }
        }

        if (issues.Count > 0)
        {
            files.Dispose();
            throw new TemplateException(issues);
        }
        return new PreparedSkiaTemplate(template, files);
    }

    /// <summary>Images and fonts written as a path without bindings, in "values" and in elements.</summary>
    private static IEnumerable<(string Path, ValueKind Kind, string File)> StaticFiles(CardTemplate template)
    {
        foreach (var (_, value) in template.Values.Where(v => TemplateValue.IsFile(v.Value.Kind)))
        {
            IEnumerable<(string, ValueBody)> bodies = value.Definition switch
            {
                PlainDefinition p => [(value.Path, p.Body)],
                ItemsDefinition i => [.. i.Items.Select(kv => ($"{value.Path}.items.{kv.Key}", kv.Value)), .. Fallback(value.Path, i.Fallback)],
                PatternDefinition p => Fallback(value.Path, p.Fallback),
                _ => [],
            };
            foreach (var (path, body) in bodies)
            {
                foreach (var (exprPath, expr) in Exprs(path, body))
                {
                    if (expr is ComposedExpr { Text.LiteralText: { } file })
                        yield return (exprPath, value.Kind, file);
                }
            }
        }

        foreach (var element in All(template.Elements))
        {
            if (element is ImageElement { Source: ComposedExpr { Text.LiteralText: { } image } })
                yield return (element.Path + ".image", ValueKind.Image, image);
            if (element is TextElement { Font: ComposedExpr { Text.LiteralText: { } font } })
                yield return (element.Path + ".font", ValueKind.Font, font);
        }

        static IEnumerable<(string, ValueBody)> Fallback(string path, ValueBody? body) => body is null ? [] : [($"{path}.fallback", body)];

        static IEnumerable<(string, ValueExpr)> Exprs(string path, ValueBody body) => body switch
        {
            SingleBody s => [(path, s.Expr)],
            ByLanguageBody b => b.Exprs.Select(kv => ($"{path}.{kv.Key}", kv.Value)),
            _ => [],
        };
    }

    private static IEnumerable<Element> All(IEnumerable<Element> elements) =>
        elements.SelectMany(e => e is GroupElement g ? [e, .. All(g.Children)] : new[] { e });
}

/// <summary>
/// Images and fonts of the template, read (or downloaded) and decoded on first use, and kept for the life of the prepared
/// template (e.g. a box export decodes each artwork once). Not thread-safe: the prepared template serializes renders.
/// </summary>
internal sealed class FileCache(IAssetProvider assets) : IDisposable
{
    private readonly Dictionary<string, SKImage> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SKTypeface> _typefaces = new(StringComparer.Ordinal);
    private readonly Dictionary<string, bool> _exists = new(StringComparer.Ordinal);

    /// <exception cref="TemplateException">Whether the file exists cannot be known (e.g. a download failed).</exception>
    public bool Exists(string file, string path)
    {
        if (_images.ContainsKey(file) || _typefaces.ContainsKey(file) || SystemFonts.IsSystem(file))
            return true;
        if (_exists.TryGetValue(file, out var known))
            return known;
        try
        {
            return _exists[file] = assets.Exists(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TemplateException(path, ex.Message, ex);
        }
    }

    /// <exception cref="TemplateException">The file cannot be read or decoded.</exception>
    public SKImage Image(string file, string path)
    {
        if (!_images.TryGetValue(file, out var image))
        {
            image = SkiaImages.Decode(Read(file, path)) ?? throw new TemplateException(path, $"{assets.Describe(file)} is not a valid image file.");
            _images[file] = image;
        }
        return image;
    }

    /// <summary>A font file, or an installed font written "system:Family".</summary>
    /// <exception cref="TemplateException">The file cannot be read or decoded, or the font is not installed.</exception>
    public SKTypeface Typeface(string font, string path)
    {
        if (_typefaces.TryGetValue(font, out var typeface))
            return typeface;

        if (SystemFonts.IsSystem(font))
        {
            var family = SystemFonts.Family(font);
            typeface = SKFontManager.Default.MatchFamily(family);
            if (typeface is null || !string.Equals(typeface.FamilyName, family, StringComparison.OrdinalIgnoreCase))
            {
                typeface?.Dispose();
                throw new TemplateException(path, $"The font \"{family}\" is not installed on this computer.");
            }
        }
        else
        {
            using var data = SKData.CreateCopy(Read(font, path));
            typeface = SKTypeface.FromData(data) ?? throw new TemplateException(path, $"{assets.Describe(font)} is not a valid font file.");
        }
        _typefaces[font] = typeface;
        return typeface;
    }

    private byte[] Read(string file, string path)
    {
        try
        {
            return assets.Read(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TemplateException(path, $"Cannot read {assets.Describe(file)}: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        foreach (var image in _images.Values)
            image.Dispose();
        foreach (var typeface in _typefaces.Values)
            typeface.Dispose();
        _images.Clear();
        _typefaces.Clear();
    }
}
