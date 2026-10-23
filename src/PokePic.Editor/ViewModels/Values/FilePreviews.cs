using Avalonia.Media.Imaging;
using PokePic.Core.Assets;
using PokePic.Core.Model;
using PokePic.Editor.Localization;
using SkiaSharp;

namespace PokePic.Editor.ViewModels.Values;

/// <summary>What a file gives: a thumbnail of an image or a sample of a font, its size or family, or why it cannot be read.</summary>
internal sealed record FilePreview(string Info, string Location, string? Error, byte[]? Png, bool Scale = true);

/// <summary>
/// Previews of the template's files, read (or downloaded) once and kept until <see cref="Forget"/> (F5). Files can be on
/// the web: they are read off the UI thread.
/// </summary>
internal sealed class FilePreviews
{
    private const int ThumbnailWidth = 64;

    private readonly Dictionary<string, FilePreview> _previews = new(StringComparer.Ordinal);

    public void Forget() => _previews.Clear();

    public async Task<FilePreview> GetAsync(string file, bool font, IAssetProvider assets)
    {
        var key = (font ? "font|" : "image|") + file;
        if (!_previews.TryGetValue(key, out var preview))
        {
            preview = await Task.Run(() => font ? Font(file, assets) : Image(file, assets));
            _previews[key] = preview;
        }
        return preview;
    }

    private static FilePreview Font(string font, IAssetProvider assets)
    {
        SKTypeface? typeface;
        string location;
        if (SystemFonts.IsSystem(font))
        {
            location = font;
            typeface = SKFontManager.Default.MatchFamily(SystemFonts.Family(font));
            if (typeface is null || !string.Equals(typeface.FamilyName, SystemFonts.Family(font), StringComparison.OrdinalIgnoreCase))
                return new FilePreview("", location, Strings.Get("FontNotInstalled"), null);
        }
        else
        {
            location = assets.Describe(font);
            try
            {
                using var data = SKData.CreateCopy(assets.Read(font));
                typeface = SKTypeface.FromData(data);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new FilePreview("", location, ex.Message, null);
            }
            if (typeface is null)
                return new FilePreview("", location, Strings.Get("NotAFont"), null);
        }

        using (typeface)
        {
            // A sample at the font's pixel size 16, drawn without smoothing like pixel-perfect cards.
            using var skFont = new SKFont(typeface, 16) { Edging = SKFontEdging.Alias, Hinting = SKFontHinting.None };
            const string Sample = "AaBbCc 0123 àè";
            float width = Math.Max(1, skFont.MeasureText(Sample));
            var metrics = skFont.Metrics;
            int height = (int)Math.Ceiling(metrics.Descent - metrics.Ascent) + 2;
            using var bitmap = new SKBitmap((int)Math.Ceiling(width) + 4, height);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.White);
                using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = false };
                canvas.DrawText(Sample, 2, 1 - metrics.Ascent, skFont, paint);
            }
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            return new FilePreview(typeface.FamilyName, location, null, png.ToArray(), Scale: false);
        }
    }

    private static FilePreview Image(string file, IAssetProvider assets)
    {
        var location = assets.Describe(file);
        try
        {
            var bytes = assets.Read(file);
            using var codec = SKCodec.Create(new MemoryStream(bytes));
            if (codec is null)
                return new FilePreview("", location, Strings.Get("NotAnImage"), null);
            return new FilePreview($"{codec.Info.Width}×{codec.Info.Height}", location, null, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new FilePreview("", location, ex.Message, null);
        }
    }

    /// <param name="scale">False for font samples, which are shown at their size.</param>
    public static Bitmap? Thumbnail(byte[] bytes, bool scale = true)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            if (!scale || bitmap.PixelSize.Width <= ThumbnailWidth * 2)
                return bitmap;
            using (bitmap)
                return bitmap.CreateScaledBitmap(new Avalonia.PixelSize(ThumbnailWidth * 2, Math.Max(1, bitmap.PixelSize.Height * ThumbnailWidth * 2 / bitmap.PixelSize.Width)));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null; // decodable by Skia but not by the UI: the info line still says what it is
        }
    }
}
