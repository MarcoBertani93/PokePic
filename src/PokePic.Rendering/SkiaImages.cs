using SkiaSharp;

namespace PokePic.Rendering;

internal static class SkiaImages
{
    /// <summary>
    /// Decodes an encoded image (PNG, ...) into a raster image that owns its pixels.
    /// </summary>
    public static SKImage? Decode(SKData data)
    {
        using var bitmap = SKBitmap.Decode(data);
        return bitmap is null ? null : SKImage.FromBitmap(bitmap);
    }

    public static SKImage? Decode(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        return Decode(data);
    }

    public static byte[] EncodePng(SKImage image)
    {
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public static SKColor ToSkia(this PokePic.Core.Model.Rgba c) => new(c.R, c.G, c.B, c.A);
}
