namespace PokePic.Core.Model;

/// <summary>
/// The JSON properties of each element type, to check styles: a style applies only properties that the
/// element's type has, and a style with a "type" can only be used by elements of that type.
/// </summary>
public static class ElementProperties
{
    public static IReadOnlyList<string> Types { get; } = ["group", "image", "shape", "line", "text"];

    /// <summary>Properties every element has.</summary>
    public static IReadOnlyList<string> Common { get; } = ["type", "name", "style", "visible", "opacity"];

    /// <summary>Properties of a type, besides <see cref="Common"/>; empty for an unknown type.</summary>
    public static IReadOnlyList<string> Of(string type) => type switch
    {
        "group" => ["position", "children"],
        "image" => ["rect", "image", "fit", "scale", "anchor", "flip", "smooth", "overflow", "language"],
        "shape" => ["rect", "shape", "radius", "fill", "stroke"],
        "line" => ["from", "to", "color", "width"],
        "text" => ["rect", "text", "font", "size", "color", "align", "valign", "lineHeight", "language", "overflow", "ellipsis"],
        _ => [],
    };

    /// <summary>Whether elements of <paramref name="type"/> have the property.</summary>
    public static bool Has(string type, string property) => Common.Contains(property) || Of(type).Contains(property);

    /// <summary>Properties a style cannot set: they identify or contain the element.</summary>
    public static IReadOnlyList<string> NotInStyles { get; } = ["name", "style", "children"];
}
