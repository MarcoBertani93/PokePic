using System.Drawing.Drawing2D;

namespace PokePic.Plugin;

/// <summary>
/// Shows a card fitted to the control: enlarged by whole pixels (pixel art stays sharp), reduced smoothly,
/// over a checkerboard for transparent pixels.
/// </summary>
internal sealed class PixelPreview : Control
{
    private Image? _image;

    public PixelPreview()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Color.FromArgb(0x4A, 0x4A, 0x4A);
    }

    /// <summary>The image shown; the control disposes the previous one.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Image? Image
    {
        get => _image;
        set
        {
            var old = _image;
            _image = value;
            if (!ReferenceEquals(old, value))
                old?.Dispose();
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_image is not { } image)
            return;

        const int margin = 12;
        float fit = Math.Min((Width - 2f * margin) / image.Width, (Height - 2f * margin) / image.Height);
        if (fit <= 0)
            return;
        // Enlarge by a whole factor when possible: every image pixel becomes the same number of screen pixels.
        float scale = fit >= 1 ? MathF.Floor(fit) : fit;
        var size = new SizeF(image.Width * scale, image.Height * scale);
        var dest = new RectangleF((Width - size.Width) / 2, (Height - size.Height) / 2, size.Width, size.Height);

        var g = e.Graphics;
        using (var light = new SolidBrush(Color.FromArgb(0x9A, 0x9A, 0x9A)))
        using (var dark = new SolidBrush(Color.FromArgb(0x80, 0x80, 0x80)))
        {
            g.FillRectangle(light, dest);
            const float cell = 8;
            for (float y = 0; y < dest.Height; y += cell)
            {
                for (float x = (int)(y / cell) % 2 * cell; x < dest.Width; x += 2 * cell)
                    g.FillRectangle(dark, dest.X + x, dest.Y + y, Math.Min(cell, dest.Width - x), Math.Min(cell, dest.Height - y));
            }
        }

        g.InterpolationMode = scale >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(image, dest);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            Image = null;
        base.Dispose(disposing);
    }
}
