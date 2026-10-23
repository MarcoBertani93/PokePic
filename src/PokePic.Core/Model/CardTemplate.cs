using System.Collections.Immutable;

namespace PokePic.Core.Model;

/// <summary>
/// A parsed card template. Elements are layers: each one is drawn above the previous one.
/// </summary>
/// <param name="Values">Named values ("values": { "text": …, "color": …, "image": …, "font": … }), usable like data keys
/// ("{title}"): see <see cref="TemplateValue"/>.</param>
/// <param name="Language">Language of all localized values, overriding the default passed by the caller (PKHeX). Null: use the default.</param>
public sealed record CardTemplate(
    string Id,
    int Version,
    string? Name,
    CanvasSpec Canvas,
    ImmutableSortedDictionary<string, TemplateValue> Values,
    ImmutableArray<Element> Elements,
    string? Language = null);

/// <param name="Width">Logical width in pixels.</param>
/// <param name="Height">Logical height in pixels.</param>
/// <param name="Background">Optional fill drawn below every element.</param>
/// <param name="DefaultMode">Suggested render mode; the renderer caller decides.</param>
/// <param name="DefaultScale">Suggested scale factor.</param>
public sealed record CanvasSpec(int Width, int Height, Fill? Background, RenderMode DefaultMode, float DefaultScale);

public enum RenderMode
{
    HighResolution,
    PixelPerfect,
}

public sealed record RectValue(Value<float> X, Value<float> Y, Value<float> Width, Value<float> Height);

public sealed record PointValue(Value<float> X, Value<float> Y);

/// <summary>
/// Base for all layers. <see cref="Path"/> is the JSON path, used in error messages.
/// </summary>
public abstract record Element(string Path, string? Name, Value<bool> Visible, float Opacity);

/// <summary>
/// A folder of layers. Children are drawn in order, translated by <see cref="Position"/>:
/// their coordinates are relative to the group.
/// </summary>
public sealed record GroupElement(
    string Path,
    string? Name,
    Value<bool> Visible,
    float Opacity,
    PointValue Position,
    ImmutableArray<Element> Children) : Element(Path, Name, Visible, Opacity);

public sealed record ImageElement(
    string Path,
    string? Name,
    Value<bool> Visible,
    float Opacity,
    RectValue Rect,
    ValueExpr Source,
    ImageFit Fit,
    Anchor Anchor,
    Flip Flip,
    bool? Smooth,
    string? Language = null,
    float Scale = 1,
    ImageOverflow Overflow = ImageOverflow.None) : Element(Path, Name, Visible, Opacity);

/// <summary>What an image does when it is larger than its rectangle.</summary>
public enum ImageOverflow
{
    /// <summary>Drawn whole, also outside the rectangle (except with fit "cover", which always crops).</summary>
    None,

    /// <summary>Drawn only inside the rectangle.</summary>
    Crop,
}

public sealed record ShapeElement(
    string Path,
    string? Name,
    Value<bool> Visible,
    float Opacity,
    RectValue Rect,
    ShapeKind Shape,
    float Radius,
    Fill? Fill,
    Stroke? Stroke) : Element(Path, Name, Visible, Opacity);

public sealed record LineElement(
    string Path,
    string? Name,
    Value<bool> Visible,
    float Opacity,
    PointValue From,
    PointValue To,
    Value<Rgba> Color,
    float Width) : Element(Path, Name, Visible, Opacity);

public sealed record TextElement(
    string Path,
    string? Name,
    Value<bool> Visible,
    float Opacity,
    RectValue Rect,
    TextTemplate Text,
    ValueExpr Font,
    float Size,
    Value<Rgba> Color,
    HorizontalAlign Align,
    VerticalAlign VAlign,
    float? LineHeight,
    string? Language = null,
    TextOverflow Overflow = TextOverflow.None,
    string Ellipsis = TextElement.DefaultEllipsis) : Element(Path, Name, Visible, Opacity)
{
    public const string DefaultEllipsis = ".";
}

/// <summary>What a text does when it does not fit its rectangle.</summary>
public enum TextOverflow
{
    /// <summary>Drawn whole, also outside the rectangle.</summary>
    None,

    /// <summary>Drawn only inside the rectangle.</summary>
    Crop,

    /// <summary>Each line too wide is shortened and ends with the "ellipsis" text.</summary>
    Ellipsis,
}


/// <summary>
/// Fonts installed on the machine, written "system:Family Name" in a text's "font" or as a value of "values.font".
/// The card then depends on the machine: the renderer fails if the family is not installed.
/// </summary>
public static class SystemFonts
{
    public const string Prefix = "system:";

    public static bool IsSystem(string font) => font.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Family(string font) => font[Prefix.Length..].Trim();
}

public abstract record Fill;

public sealed record SolidFill(Value<Rgba> Color) : Fill;

public sealed record LinearGradientFill(PointValue From, PointValue To, ImmutableArray<GradientStop> Stops) : Fill;

public sealed record GradientStop(float Offset, Value<Rgba> Color);

public sealed record Stroke(Value<Rgba> Color, float Width);

public enum ShapeKind
{
    Rect,
    Ellipse,
}

public enum ImageFit
{
    /// <summary>Native size, positioned by the anchor.</summary>
    None,
    /// <summary>Fills the rect, ignoring the aspect ratio.</summary>
    Stretch,
    /// <summary>Largest size that fits inside the rect, keeping the aspect ratio.</summary>
    Contain,
    /// <summary>Smallest size that covers the rect, keeping the aspect ratio; overflow is clipped.</summary>
    Cover,
    /// <summary>Native size times the element's "scale", positioned by the anchor.</summary>
    Scale,
}

public enum Anchor
{
    TopLeft,
    Top,
    TopRight,
    Left,
    Center,
    Right,
    BottomLeft,
    Bottom,
    BottomRight,
}

public enum Flip
{
    None,
    Horizontal,
    Vertical,
    Both,
}

public enum HorizontalAlign
{
    Left,
    Center,
    Right,
}

public enum VerticalAlign
{
    Top,
    Middle,
    Bottom,
}
