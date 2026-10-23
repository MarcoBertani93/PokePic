using PokePic.Core.Model;
using SkiaSharp;

namespace PokePic.Rendering;

internal static class Layout
{
    /// <summary>
    /// Destination rect of an image of the given size inside <paramref name="area"/>.
    /// </summary>
    /// <param name="fixedScale">The scale of <see cref="ImageFit.Scale"/>.</param>
    public static SKRect FitImage(SKRect area, int imageWidth, int imageHeight, ImageFit fit, Anchor anchor, float fixedScale = 1)
    {
        if (fit == ImageFit.Stretch || imageWidth == 0 || imageHeight == 0)
            return area;

        float scale = fit switch
        {
            ImageFit.Contain => Math.Min(area.Width / imageWidth, area.Height / imageHeight),
            ImageFit.Cover => Math.Max(area.Width / imageWidth, area.Height / imageHeight),
            ImageFit.Scale => fixedScale,
            _ => 1,
        };
        return Place(area, imageWidth * scale, imageHeight * scale, anchor);
    }

    public static SKRect Place(SKRect area, float width, float height, Anchor anchor)
    {
        float x = anchor switch
        {
            Anchor.TopLeft or Anchor.Left or Anchor.BottomLeft => area.Left,
            Anchor.TopRight or Anchor.Right or Anchor.BottomRight => area.Right - width,
            _ => area.Left + (area.Width - width) / 2,
        };
        float y = anchor switch
        {
            Anchor.TopLeft or Anchor.Top or Anchor.TopRight => area.Top,
            Anchor.BottomLeft or Anchor.Bottom or Anchor.BottomRight => area.Bottom - height,
            _ => area.Top + (area.Height - height) / 2,
        };
        return SKRect.Create(x, y, width, height);
    }

    /// <summary>
    /// Snaps a rect to whole pixels, keeping its size.
    /// </summary>
    public static SKRect Snap(SKRect rect) => SKRect.Create(
        MathF.Floor(rect.Left),
        MathF.Floor(rect.Top),
        MathF.Round(rect.Width),
        MathF.Round(rect.Height));
}
