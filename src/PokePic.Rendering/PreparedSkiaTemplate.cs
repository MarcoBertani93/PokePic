using System.Runtime.InteropServices;
using PokePic.Core;
using PokePic.Core.Data;
using PokePic.Core.Model;
using PokePic.Core.Rendering;
using PokePic.Core.Resolution;
using SkiaSharp;

namespace PokePic.Rendering;

/// <summary>
/// A template with decoded assets. Elements are drawn in order: each one is a layer above the previous.
/// </summary>
internal sealed class PreparedSkiaTemplate(CardTemplate template, FileCache files) : IPreparedTemplate
{
    private readonly Lock _lock = new();
    private bool _disposed;

    public CardTemplate Template { get; } = template;

    public RenderedCard Render(CardData data, RenderOptions options)
    {
        if (options.Validate() is { } error)
            throw new ArgumentException(error, nameof(options));

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            using var image = options.Mode == RenderMode.PixelPerfect
                ? RenderPixelPerfect(data, (int)options.Scale, options.EffectiveLanguage(Template))
                : RenderHighResolution(data, options.Scale, options.EffectiveLanguage(Template));
            return new RenderedCard(image.Width, image.Height, SkiaImages.EncodePng(image));
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
            files.Dispose();
        }
    }

    /// <summary>
    /// Draws at the template's base resolution without anti-aliasing, then upscales with nearest-neighbor.
    /// </summary>
    private SKImage RenderPixelPerfect(CardData data, int scale, string language)
    {
        int width = Template.Canvas.Width;
        int height = Template.Canvas.Height;

        using var baseSurface = CreateSurface(width, height);
        DrawScene(baseSurface.Canvas, data, language, antialias: false);
        var baseImage = baseSurface.Snapshot();
        if (scale == 1)
            return baseImage;

        using (baseImage)
        {
            using var surface = CreateSurface(width * scale, height * scale);
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.DrawImage(
                baseImage,
                SKRect.Create(0, 0, width * scale, height * scale),
                new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
            return surface.Snapshot();
        }
    }

    /// <summary>
    /// Draws directly at the output resolution with anti-aliasing.
    /// </summary>
    private SKImage RenderHighResolution(CardData data, float scale, string language)
    {
        int width = (int)MathF.Ceiling(Template.Canvas.Width * scale);
        int height = (int)MathF.Ceiling(Template.Canvas.Height * scale);

        using var surface = CreateSurface(width, height);
        surface.Canvas.Scale(scale);
        DrawScene(surface.Canvas, data, language, antialias: true);
        return surface.Snapshot();
    }

    private static SKSurface CreateSurface(int width, int height) =>
        SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul))
        ?? throw new InvalidOperationException($"Cannot create a {width}x{height} drawing surface.");

    private void DrawScene(SKCanvas canvas, CardData data, string language, bool antialias)
    {
        canvas.Clear(SKColors.Transparent);
        var resolver = BindingResolver.For(Template, data, language, files.Exists);
        var dataImages = new Dictionary<CardImage, SKImage>(ReferenceEqualityComparer.Instance);
        var ctx = new DrawContext(canvas, resolver, antialias, dataImages);

        try
        {
            if (Template.Canvas.Background is { } background)
            {
                using var paint = ctx.FillPaint(background, "canvas.background");
                canvas.DrawRect(SKRect.Create(0, 0, Template.Canvas.Width, Template.Canvas.Height), paint);
            }

            DrawElements(ctx, Template.Elements);
        }
        finally
        {
            foreach (var image in dataImages.Values)
                image.Dispose();
        }
    }

    private void DrawElements(DrawContext ctx, IEnumerable<Element> elements)
    {
        var canvas = ctx.Canvas;
        foreach (var element in elements)
        {
            if (!ctx.Resolver.Resolve(element.Visible, element.Path + ".visible"))
                continue;

            bool layer = element.Opacity < 1;
            if (layer)
            {
                using var layerPaint = new SKPaint { Color = SKColors.White.WithAlpha((byte)MathF.Round(element.Opacity * 255)) };
                canvas.SaveLayer(layerPaint);
            }

            switch (element)
            {
                case GroupElement group:
                    DrawGroup(ctx, group);
                    break;
                case ShapeElement shape:
                    DrawShape(ctx, shape);
                    break;
                case LineElement line:
                    DrawLine(ctx, line);
                    break;
                case ImageElement image:
                    DrawImage(ctx, image);
                    break;
                case TextElement text:
                    DrawText(ctx, text);
                    break;
            }

            if (layer)
                canvas.Restore();
        }
    }

    private void DrawGroup(DrawContext ctx, GroupElement group)
    {
        var offset = ctx.Point(group.Position, group.Path + ".position");
        if (!ctx.Antialias)
            offset = new SKPoint(MathF.Round(offset.X), MathF.Round(offset.Y));

        ctx.Canvas.Save();
        ctx.Canvas.Translate(offset.X, offset.Y);
        DrawElements(ctx, group.Children);
        ctx.Canvas.Restore();
    }

    private static void DrawShape(DrawContext ctx, ShapeElement shape)
    {
        var rect = ctx.Rect(shape.Rect, shape.Path + ".rect");

        if (shape.Fill is { } fill)
        {
            using var paint = ctx.FillPaint(fill, shape.Path + ".fill");
            DrawShapeGeometry(ctx.Canvas, shape, rect, shape.Radius, paint);
        }

        if (shape.Stroke is { Width: > 0 } stroke)
        {
            using var paint = new SKPaint
            {
                IsAntialias = ctx.Antialias,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = stroke.Width,
                Color = ctx.Resolver.Resolve(stroke.Color, shape.Path + ".stroke.color").ToSkia(),
            };
            // The stroke stays inside the rect, like a border.
            float inset = stroke.Width / 2;
            var inner = SKRect.Inflate(rect, -inset, -inset);
            DrawShapeGeometry(ctx.Canvas, shape, inner, Math.Max(0, shape.Radius - inset), paint);
        }
    }

    private static void DrawShapeGeometry(SKCanvas canvas, ShapeElement shape, SKRect rect, float radius, SKPaint paint)
    {
        if (shape.Shape == ShapeKind.Ellipse)
            canvas.DrawOval(rect, paint);
        else if (radius > 0)
            canvas.DrawRoundRect(rect, radius, radius, paint);
        else
            canvas.DrawRect(rect, paint);
    }

    /// <summary>
    /// With an odd integer width, coordinates address pixel cells: a line from (10,20) to (50,20)
    /// covers pixels 10..50 of row 20, both ends included.
    /// </summary>
    private static void DrawLine(DrawContext ctx, LineElement line)
    {
        var from = ctx.Point(line.From, line.Path + ".from");
        var to = ctx.Point(line.To, line.Path + ".to");

        bool oddInteger = line.Width == MathF.Floor(line.Width) && (int)line.Width % 2 == 1;
        if (oddInteger)
        {
            from.Offset(0.5f, 0.5f);
            to.Offset(0.5f, 0.5f);
        }

        using var paint = new SKPaint
        {
            IsAntialias = ctx.Antialias,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = line.Width,
            StrokeCap = SKStrokeCap.Square,
            Color = ctx.Resolver.Resolve(line.Color, line.Path + ".color").ToSkia(),
        };
        ctx.Canvas.DrawLine(from, to, paint);
    }

    private void DrawImage(DrawContext ctx, ImageElement element)
    {
        var image = ResolveImage(ctx, element);
        var rect = ctx.Rect(element.Rect, element.Path + ".rect");
        var dest = Layout.FitImage(rect, image.Width, image.Height, element.Fit, element.Anchor, element.Scale);
        if (!ctx.Antialias)
            dest = Layout.Snap(dest);

        // Pixel-perfect always samples nearest; high resolution is smooth unless the element says otherwise.
        var sampling = ctx.Antialias && element.Smooth != false
            ? new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)
            : new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

        var canvas = ctx.Canvas;
        canvas.Save();
        if (element.Fit == ImageFit.Cover || element.Overflow == ImageOverflow.Crop)
            canvas.ClipRect(rect, antialias: false);

        if (element.Flip != Flip.None)
        {
            float sx = element.Flip is Flip.Horizontal or Flip.Both ? -1 : 1;
            float sy = element.Flip is Flip.Vertical or Flip.Both ? -1 : 1;
            canvas.Translate(dest.MidX, dest.MidY);
            canvas.Scale(sx, sy);
            canvas.Translate(-dest.MidX, -dest.MidY);
        }

        canvas.DrawImage(image, dest, sampling);
        canvas.Restore();
    }

    /// <summary>An image of the data (decoded once per card), or a file of the template.</summary>
    private SKImage ResolveImage(DrawContext ctx, ImageElement element)
    {
        var path = element.Path + ".image";
        switch (ctx.Resolver.ResolveFile(element.Source, ValueKind.Image, path, element.Language))
        {
            case ImageValue { Image: var encoded }:
                if (!ctx.DataImages.TryGetValue(encoded, out var decoded))
                {
                    decoded = SkiaImages.Decode(ImmutableCollectionsMarshal.AsArray(encoded.Encoded)!)
                        ?? throw new TemplateException(path, "The image of the card data is not a valid image.");
                    ctx.DataImages[encoded] = decoded;
                }
                return decoded;
            case FileValue file:
                return files.Image(file.Path, path);
            case var other:
                throw new TemplateException(path, $"Expected an image, found {other.Kind.ToString().ToLowerInvariant()}.");
        }
    }

    private void DrawText(DrawContext ctx, TextElement element)
    {
        var text = ctx.Resolver.Resolve(element.Text, element.Path + ".text", element.Language);
        if (text.Length == 0)
            return;

        var rect = ctx.Rect(element.Rect, element.Path + ".rect");
        var fontPath = element.Path + ".font";
        var typeface = ctx.Resolver.ResolveFile(element.Font, ValueKind.Font, fontPath, element.Language) is FileValue fontFile
            ? files.Typeface(fontFile.Path, fontPath)
            : throw new TemplateException(fontPath, "Expected a font.");
        using var font = new SKFont(typeface, element.Size)
        {
            Edging = ctx.Antialias ? SKFontEdging.Antialias : SKFontEdging.Alias,
            Hinting = ctx.Antialias ? SKFontHinting.Slight : SKFontHinting.None,
            Subpixel = ctx.Antialias,
        };
        using var paint = new SKPaint
        {
            IsAntialias = ctx.Antialias,
            Color = ctx.Resolver.Resolve(element.Color, element.Path + ".color").ToSkia(),
        };

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var metrics = font.Metrics;
        float ascent = -metrics.Ascent;
        float descent = metrics.Descent;
        float lineHeight = element.LineHeight ?? (ascent + descent + metrics.Leading);
        float blockHeight = lineHeight * (lines.Length - 1) + ascent + descent;

        float top = element.VAlign switch
        {
            VerticalAlign.Middle => rect.Top + (rect.Height - blockHeight) / 2,
            VerticalAlign.Bottom => rect.Bottom - blockHeight,
            _ => rect.Top,
        };

        if (element.Overflow == TextOverflow.Ellipsis)
        {
            for (int i = 0; i < lines.Length; i++)
                lines[i] = Shorten(lines[i], element.Ellipsis, rect.Width, font);
        }

        int saved = ctx.Canvas.Save();
        if (element.Overflow == TextOverflow.Crop)
            ctx.Canvas.ClipRect(rect, antialias: ctx.Antialias);
        for (int i = 0; i < lines.Length; i++)
        {
            float width = font.MeasureText(lines[i]);
            float x = element.Align switch
            {
                HorizontalAlign.Center => rect.Left + (rect.Width - width) / 2,
                HorizontalAlign.Right => rect.Right - width,
                _ => rect.Left,
            };
            float baseline = top + ascent + i * lineHeight;
            if (!ctx.Antialias)
            {
                x = MathF.Round(x);
                baseline = MathF.Round(baseline);
            }
            ctx.Canvas.DrawText(lines[i], x, baseline, SKTextAlign.Left, font, paint);
        }
        ctx.Canvas.RestoreToCount(saved);
    }

    /// <summary>
    /// The longest start of the line that fits <paramref name="width"/> with the ellipsis after it (whole characters,
    /// emoji and accents included). A line that fits is unchanged; if not even the ellipsis fits, nothing is drawn.
    /// </summary>
    private static string Shorten(string line, string ellipsis, float width, SKFont font)
    {
        if (font.MeasureText(line) <= width)
            return line;
        var starts = System.Globalization.StringInfo.ParseCombiningCharacters(line);
        for (int count = starts.Length - 1; count >= 0; count--)
        {
            var candidate = line[..starts[count]].TrimEnd() + ellipsis;
            if (font.MeasureText(candidate) <= width)
                return candidate;
        }
        return "";
    }

    private sealed class DrawContext(SKCanvas canvas, BindingResolver resolver, bool antialias, Dictionary<CardImage, SKImage> dataImages)
    {
        public SKCanvas Canvas { get; } = canvas;
        public BindingResolver Resolver { get; } = resolver;
        public bool Antialias { get; } = antialias;
        public Dictionary<CardImage, SKImage> DataImages { get; } = dataImages;

        public SKRect Rect(RectValue r, string path) => SKRect.Create(
            Resolver.Resolve(r.X, path + "[0]"),
            Resolver.Resolve(r.Y, path + "[1]"),
            Resolver.Resolve(r.Width, path + "[2]"),
            Resolver.Resolve(r.Height, path + "[3]"));

        public SKPoint Point(PointValue p, string path) => new(
            Resolver.Resolve(p.X, path + "[0]"),
            Resolver.Resolve(p.Y, path + "[1]"));

        public SKPaint FillPaint(Fill fill, string path)
        {
            var paint = new SKPaint { IsAntialias = Antialias, Style = SKPaintStyle.Fill, IsDither = false };
            switch (fill)
            {
                case SolidFill s:
                    paint.Color = Resolver.Resolve(s.Color, path).ToSkia();
                    break;
                case LinearGradientFill g:
                    var colors = g.Stops.Select((stop, i) => Resolver.Resolve(stop.Color, $"{path}.linear.stops[{i}][1]").ToSkia()).ToArray();
                    var offsets = g.Stops.Select(stop => stop.Offset).ToArray();
                    paint.Shader = SKShader.CreateLinearGradient(
                        Point(g.From, path + ".linear.from"),
                        Point(g.To, path + ".linear.to"),
                        colors,
                        offsets,
                        SKShaderTileMode.Clamp);
                    break;
            }
            return paint;
        }
    }
}
