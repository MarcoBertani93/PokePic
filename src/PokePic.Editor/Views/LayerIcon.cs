using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PokePic.Editor.Views;

/// <summary>
/// Vector icon for a layer type: folder (group), serif "T" (text), rounded square (shape),
/// picture with mountains and sun (image), diagonal stroke (line); in the properties header also a card
/// (the template) and stacked squares (several elements).
/// </summary>
public sealed class LayerIcon : Control
{
    public static readonly StyledProperty<string?> KindProperty = AvaloniaProperty.Register<LayerIcon, string?>(nameof(Kind));
    public static readonly StyledProperty<IBrush?> ForegroundProperty = AvaloniaProperty.Register<LayerIcon, IBrush?>(nameof(Foreground));

    private static readonly IBrush FolderFill = new SolidColorBrush(Color.FromRgb(0xE8, 0xB8, 0x4A));
    private static readonly IBrush FolderStroke = new SolidColorBrush(Color.FromRgb(0xB0, 0x82, 0x20));
    private static readonly IBrush ShapeFill = new SolidColorBrush(Color.FromRgb(0x6F, 0xA8, 0xDC));
    private static readonly IBrush ShapeStroke = new SolidColorBrush(Color.FromRgb(0x3D, 0x6F, 0xA3));
    private static readonly IBrush Sky = new SolidColorBrush(Color.FromRgb(0xCF, 0xE8, 0xFF));
    private static readonly IBrush Mountain = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0x52));
    private static readonly IBrush Sun = new SolidColorBrush(Color.FromRgb(0xF2, 0xB7, 0x05));
    private static readonly IBrush Frame = new SolidColorBrush(Color.FromRgb(0x6B, 0x6B, 0x6B));
    private static readonly IBrush LineColor = new SolidColorBrush(Color.FromRgb(0xC0, 0x4B, 0x4B));
    private static readonly Typeface Serif = new("Times New Roman, Liberation Serif, DejaVu Serif, serif", FontStyle.Normal, FontWeight.Bold);

    static LayerIcon()
    {
        AffectsRender<LayerIcon>(KindProperty, ForegroundProperty);
    }

    public LayerIcon()
    {
        Width = 16;
        Height = 16;
    }

    public string? Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    /// <summary>Color of the "T" of text layers; follows the theme when bound.</summary>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        double s = Math.Min(Bounds.Width, Bounds.Height) / 16;
        using var _ = context.PushTransform(Matrix.CreateScale(s, s));

        switch (Kind)
        {
            case "group":
                var folder = Geometry.Parse("M1.5,3.5 L6,3.5 L7.5,5 L14.5,5 L14.5,13.5 L1.5,13.5 Z");
                context.DrawGeometry(FolderFill, new Pen(FolderStroke, 1), folder);
                context.DrawLine(new Pen(FolderStroke, 1), new Point(1.5, 6.5), new Point(14.5, 6.5));
                break;

            case "shape":
                context.DrawRectangle(ShapeFill, new Pen(ShapeStroke, 1), new Rect(2.5, 2.5, 11, 11), 3, 3);
                break;

            case "image":
                var clip = new Rect(1.5, 2.5, 13, 11);
                context.DrawRectangle(Sky, null, clip);
                using (context.PushClip(clip))
                {
                    context.DrawGeometry(Mountain, null, Geometry.Parse("M1.5,13.5 L6,7.5 L9,11 L11,9 L14.5,13.5 Z"));
                    context.DrawEllipse(Sun, null, new Point(11, 5.5), 1.6, 1.6);
                }
                context.DrawRectangle(null, new Pen(Frame, 1), clip);
                break;

            case "line":
                context.DrawLine(new Pen(LineColor, 2, lineCap: PenLineCap.Round), new Point(3, 13), new Point(13, 3));
                break;

            case "text":
                var text = new FormattedText("T", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Serif, 15, Foreground ?? Brushes.Gray);
                context.DrawText(text, new Point((16 - text.Width) / 2, (16 - text.Height) / 2));
                break;

            case "template":
                // A card: portrait frame with a picture area and two text rows.
                context.DrawRectangle(Brushes.White, new Pen(Frame, 1), new Rect(3.5, 1.5, 9, 13), 1.5, 1.5);
                context.DrawRectangle(Sky, null, new Rect(5, 3, 6, 5));
                context.DrawEllipse(Sun, null, new Point(9.5, 4.5), 1, 1);
                context.DrawRectangle(Mountain, null, new Rect(5, 6.5, 6, 1.5));
                context.DrawLine(new Pen(Frame, 1), new Point(5, 10), new Point(11, 10));
                context.DrawLine(new Pen(Frame, 1), new Point(5, 12.5), new Point(9, 12.5));
                break;

            case "style":
                // A palette.
                context.DrawGeometry(Brushes.White, new Pen(Frame, 1), Geometry.Parse("M8,1.5 C12.5,1.5 14.5,4.5 14.5,7.5 C14.5,10 12.5,10.5 11,10 C9.5,9.5 9,11 10,12.5 C10.8,13.8 9.5,14.5 8,14.5 C4,14.5 1.5,11.5 1.5,8 C1.5,4.5 4,1.5 8,1.5 Z"));
                context.DrawEllipse(LineColor, null, new Point(5, 6), 1.3, 1.3);
                context.DrawEllipse(Sun, null, new Point(8.5, 4.5), 1.3, 1.3);
                context.DrawEllipse(ShapeFill, null, new Point(11.5, 6.5), 1.3, 1.3);
                context.DrawEllipse(Mountain, null, new Point(5, 10), 1.3, 1.3);
                break;

            case "multiple":
                // Stacked layers.
                context.DrawRectangle(Sky, new Pen(ShapeStroke, 1), new Rect(5.5, 1.5, 9, 9), 1.5, 1.5);
                context.DrawRectangle(ShapeFill, new Pen(ShapeStroke, 1), new Rect(1.5, 5.5, 9, 9), 1.5, 1.5);
                break;
        }
    }
}
