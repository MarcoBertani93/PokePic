using System.Collections.Immutable;
using System.Text.Json;
using PokePic.Core.Model;
using PokePic.Core.Resolution;

namespace PokePic.Core.Json;

public sealed record TemplateParseResult(CardTemplate? Template, IReadOnlyList<TemplateIssue> Issues)
{
    public bool Success => Template is not null && Issues.Count == 0;
}

/// <summary>
/// Reads a template from JSON (comments and trailing commas allowed). Never throws on bad input:
/// every problem is returned as a <see cref="TemplateIssue"/> with its JSON path and line.
/// </summary>
public static class TemplateParser
{
    internal static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    internal static readonly JsonReaderOptions ReaderOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static TemplateParseResult Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException ex)
        {
            int? line = ex.LineNumber is { } l ? (int)l + 1 : null;
            int? column = ex.BytePositionInLine is { } c ? (int)c + 1 : null;
            return new TemplateParseResult(null, [new TemplateIssue("", $"Invalid JSON: {FirstSentence(ex.Message)}", line, column)]);
        }

        using (document)
        {
            var issues = new IssueCollector();
            var template = ReadTemplate(issues, document.RootElement);
            var located = JsonLineMap.Build(json, ReaderOptions).Locate(issues.Issues);
            return new TemplateParseResult(issues.HasErrors ? null : template, located);
        }
    }

    private static CardTemplate? ReadTemplate(IssueCollector issues, JsonElement root)
    {
        var obj = ObjectReader.From(issues, "", root);
        if (obj is null)
            return null;

        obj.Ignore("$schema");
        var id = obj.String("id", required: true);
        var version = obj.Int("version", min: 1) ?? 1;
        var name = obj.String("name");
        var language = LanguageCode(obj, "language");
        var canvas = obj.Get("canvas", required: true) is { } c ? ReadCanvas(issues, obj.PathOf("canvas"), c) : null;
        var values = ReadValues(issues, obj.PathOf("values"), obj.Get("values"));
        var styles = ReadStyles(issues, obj.PathOf("styles"), obj.Get("styles"));
        var elements = ReadElements(issues, obj.PathOf("elements"), obj.Get("elements", required: true), styles);
        obj.ReportUnknown();

        if (id is null || canvas is null || elements is null)
            return null;

        return new CardTemplate(id, version, name, canvas, values, elements.Value, language);
    }

    /// <summary>The sections of a template's "values", by kind: those of a data pack, and "font".</summary>
    public static IReadOnlyList<(string Name, Data.ValueKind Kind)> ValueSections { get; } =
        [.. Data.DataPackSerializer.Sections, ("font", Data.ValueKind.Font)];

    /// <summary>
    /// "values": { "text": { … }, "int": { … }, … }. Names are unique across the sections; each value is single, by
    /// language, a set of "items" or a "pattern".
    /// </summary>
    private static ImmutableSortedDictionary<string, TemplateValue> ReadValues(IssueCollector issues, string path, JsonElement? e)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<string, TemplateValue>(StringComparer.Ordinal);
        if (e is null || ObjectReader.From(issues, path, e.Value) is not { } sections)
            return builder.ToImmutable();

        foreach (var (section, kind) in ValueSections)
        {
            if (sections.Get(section) is not { } s || ObjectReader.From(issues, sections.PathOf(section), s) is not { } entries)
                continue;
            foreach (var (name, value) in entries.All.ToList())
            {
                entries.Ignore(name);
                var itemPath = entries.PathOf(name);
                if (!IsValidValueName(name))
                    issues.Error(itemPath, $"Invalid name \"{name}\": use letters, digits, \".\", \"_\" and \"-\".");
                else if (name is TemplateValue.LanguageName or TemplateValue.KeyName)
                    issues.Error(itemPath, $"\"{name}\" is reserved: \"{{{TemplateValue.LanguageName}}}\" is the language of the card, \"{{{TemplateValue.KeyName}}}\" the key in a pattern.");
                else if (builder.ContainsKey(name))
                    issues.Error(itemPath, $"\"{name}\" is defined in more than one section.");
                else if (ReadDefinition(issues, itemPath, value, kind) is { } definition)
                    builder[name] = new TemplateValue(itemPath, kind, definition);
            }
        }
        sections.ReportUnknown();
        return builder.ToImmutable();
    }

    private static bool IsValidValueName(string name) =>
        name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    private static ValueDefinition? ReadDefinition(IssueCollector issues, string path, JsonElement e, Data.ValueKind kind)
    {
        if (e.ValueKind != JsonValueKind.Object || !(e.TryGetProperty("items", out _) || e.TryGetProperty("pattern", out _)))
            return ReadBody(issues, path, e, kind) is { } body ? new PlainDefinition(body) : null;

        var obj = ObjectReader.From(issues, path, e)!;
        ValueBody? fallback = null;
        bool ok = true;
        if (obj.Get("fallback") is { } f)
            ok = (fallback = ReadBody(issues, obj.PathOf("fallback"), f, kind)) is not null;

        ValueDefinition? result = null;
        if (obj.Has("items") && obj.Has("pattern"))
        {
            issues.Error(path, "A set has \"items\" or a \"pattern\", not both.");
            obj.Ignore("items");
            obj.Ignore("pattern");
        }
        else if (obj.Get("items") is { } itemsElement && ObjectReader.From(issues, obj.PathOf("items"), itemsElement) is { } items)
        {
            var builder = ImmutableSortedDictionary.CreateBuilder<string, ValueBody>(StringComparer.Ordinal);
            foreach (var (key, item) in items.All.ToList())
            {
                items.Ignore(key);
                if (!TemplateValue.IsValidKey(key))
                {
                    issues.Error(items.PathOf(key), $"Invalid key \"{key}\": use {TemplateValue.KeyRule}.");
                    ok = false;
                }
                else if (ReadBody(issues, items.PathOf(key), item, kind) is { } body)
                    builder[key] = body;
                else
                    ok = false;
            }
            if (builder.Count == 0 && ok)
            {
                issues.Error(obj.PathOf("items"), "A set needs at least one item.");
                ok = false;
            }
            if (ok)
                result = new ItemsDefinition(builder.ToImmutable(), fallback);
        }
        else if (obj.Get("pattern") is { } patternElement)
        {
            if (!TemplateValue.IsComposable(kind))
                issues.Error(obj.PathOf("pattern"), "Only texts, images and fonts can have a \"pattern\": list the items in \"items\".");
            else if (ReadBody(issues, obj.PathOf("pattern"), patternElement, kind) is { } pattern && ok)
                result = new PatternDefinition(pattern, fallback);
        }
        obj.ReportUnknown();
        return result;
    }

    /// <summary>A value written once, or { "en": …, "it": … } ("en" required). { "set": …, "key": … } is one value.</summary>
    private static ValueBody? ReadBody(IssueCollector issues, string path, JsonElement e, Data.ValueKind kind)
    {
        if (e.ValueKind != JsonValueKind.Object || e.TryGetProperty("set", out _))
            return ValueParsers.Expr(issues, path, e, kind) is { } expr ? new SingleBody(expr) : null;

        var obj = ObjectReader.From(issues, path, e)!;
        var builder = ImmutableSortedDictionary.CreateBuilder<string, ValueExpr>(StringComparer.Ordinal);
        bool ok = true;
        foreach (var (language, value) in obj.All.ToList())
        {
            obj.Ignore(language);
            if (!Data.Languages.IsValidCode(language))
            {
                issues.Error(obj.PathOf(language), $"Invalid language code \"{language}\". Use codes such as \"en\", \"it\", \"ja\", \"zh-Hans\" (a set is written {{ \"items\": {{ … }} }}).");
                ok = false;
            }
            else if (ValueParsers.Expr(issues, obj.PathOf(language), value, kind) is { } expr)
                builder[language] = expr;
            else
                ok = false;
        }
        if (!obj.Has(Data.Languages.Fallback))
        {
            issues.Error(path, $"Values by language must contain \"{Data.Languages.Fallback}\": it is used when a language is not available.");
            return null;
        }
        return ok ? new ByLanguageBody(builder.ToImmutable()) : null;
    }

    private static string? LanguageCode(ObjectReader obj, string property)
    {
        var code = obj.String(property);
        if (code is not null && !Data.Languages.IsValidCode(code))
        {
            obj.Issues.Error(obj.PathOf(property), $"Invalid language code \"{code}\". Use codes such as \"en\", \"it\", \"ja\", \"zh-Hans\".");
            return null;
        }
        return code;
    }

    private static CanvasSpec? ReadCanvas(IssueCollector issues, string path, JsonElement e)
    {
        var obj = ObjectReader.From(issues, path, e);
        if (obj is null)
            return null;

        var width = obj.Int("width", required: true, min: 1, max: 4096);
        var height = obj.Int("height", required: true, min: 1, max: 4096);
        var background = obj.Get("background") is { } b ? ValueParsers.Fill(issues, obj.PathOf("background"), b) : null;
        var mode = obj.Enum("mode", RenderMode.HighResolution);
        var scale = obj.Float("scale", min: 0.1f, max: 16) ?? 1;
        obj.ReportUnknown();

        if (mode == RenderMode.PixelPerfect && scale != MathF.Floor(scale))
            issues.Error(obj.PathOf("scale"), "Pixel-perfect mode requires an integer scale.");

        return width is null || height is null ? null : new CanvasSpec(width.Value, height.Value, background, mode, scale);
    }

    /// <summary>A style: default properties, for one element type or (without "type") for any element that has them.</summary>
    private sealed record StyleDefinition(string? Type, IReadOnlyList<KeyValuePair<string, JsonElement>> Properties);

    private static ImmutableSortedDictionary<string, StyleDefinition> ReadStyles(IssueCollector issues, string path, JsonElement? e)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<string, StyleDefinition>(StringComparer.Ordinal);
        if (e is null || ObjectReader.From(issues, path, e.Value) is not { } obj)
            return builder.ToImmutable();

        foreach (var (name, value) in obj.All.ToList())
        {
            obj.Ignore(name);
            var style = ObjectReader.From(issues, obj.PathOf(name), value);
            if (style is null)
                continue;

            var type = style.String("type");
            if (type is not null && !ElementProperties.Types.Contains(type))
            {
                issues.Error(style.PathOf("type"), $"Unknown element type \"{type}\". Allowed: {string.Join(", ", ElementProperties.Types)}.");
                type = null;
            }

            var properties = new List<KeyValuePair<string, JsonElement>>();
            foreach (var property in style.All.Where(p => p.Key != "type"))
            {
                var propertyPath = style.PathOf(property.Key);
                if (ElementProperties.NotInStyles.Contains(property.Key))
                    issues.Error(propertyPath, $"A style cannot define \"{property.Key}\".");
                else if (type is not null && !ElementProperties.Has(type, property.Key))
                    issues.Error(propertyPath, $"\"{property.Key}\" is not a property of {type} elements.");
                else if (type is null && !ElementProperties.Types.Any(t => ElementProperties.Has(t, property.Key)))
                    issues.Error(propertyPath, $"Unknown property \"{property.Key}\".");
                else
                    properties.Add(property);
            }
            builder[name] = new StyleDefinition(type, properties);
        }
        return builder.ToImmutable();
    }

    private static ImmutableArray<Element>? ReadElements(IssueCollector issues, string path, JsonElement? e, ImmutableSortedDictionary<string, StyleDefinition> styles)
    {
        if (e is null)
            return null;
        if (e.Value.ValueKind != JsonValueKind.Array)
        {
            issues.Error(path, $"Expected an array of elements, found {ObjectReader.Describe(e.Value)}.");
            return null;
        }

        var elements = ImmutableArray.CreateBuilder<Element>();
        int index = 0;
        foreach (var item in e.Value.EnumerateArray())
        {
            if (ReadElement(issues, JsonPath.Index(path, index++), item, styles) is { } element)
                elements.Add(element);
        }
        return elements.ToImmutable();
    }

    private static Element? ReadElement(IssueCollector issues, string path, JsonElement e, ImmutableSortedDictionary<string, StyleDefinition> styles)
    {
        var raw = ObjectReader.From(issues, path, e);
        if (raw is null)
            return null;

        // Style properties act as defaults: the element's own properties win.
        var obj = raw;
        if (raw.String("style") is { } styleName)
        {
            var elementType = raw.Has("type") && raw.All.First(p => p.Key == "type").Value is { ValueKind: JsonValueKind.String } t ? t.GetString()! : "";
            if (!styles.TryGetValue(styleName, out var style))
            {
                issues.Error(raw.PathOf("style"), $"Unknown style \"{styleName}\".");
            }
            else if (style.Type is not null && style.Type != elementType)
            {
                issues.Error(raw.PathOf("style"), $"Style \"{styleName}\" is for {style.Type} elements, this is a {elementType} element.");
            }
            else
            {
                var missing = style.Properties.Where(p => !ElementProperties.Has(elementType, p.Key)).Select(p => p.Key).ToList();
                if (missing.Count > 0 && ElementProperties.Types.Contains(elementType))
                    issues.Error(raw.PathOf("style"), $"Style \"{styleName}\" sets {string.Join(", ", missing)}, which {elementType} elements do not have: give the style a \"type\" or use another style.");
                var merged = style.Properties
                    .Where(p => !raw.Has(p.Key) && ElementProperties.Has(elementType, p.Key))
                    .Concat(raw.All.Where(p => p.Key != "style"));
                obj = new ObjectReader(issues, path, merged);
            }
        }

        var type = obj.String("type", required: true);
        var name = obj.String("name");
        var visible = obj.BoolValue("visible", true);
        var opacity = obj.Float("opacity", min: 0, max: 1) ?? 1;

        Element? element = type switch
        {
            null => null,
            "group" => ReadGroup(obj, path, name, visible, opacity, styles),
            "image" => ReadImage(obj, path, name, visible, opacity),
            "shape" => ReadShape(obj, path, name, visible, opacity),
            "line" => ReadLine(obj, path, name, visible, opacity),
            "text" => ReadText(obj, path, name, visible, opacity),
            _ => Unknown(obj, type),
        };

        if (type is "group" or "image" or "shape" or "line" or "text")
            obj.ReportUnknown();
        return element;

        static Element? Unknown(ObjectReader obj, string type)
        {
            obj.Issues.Error(obj.PathOf("type"), $"Unknown element type \"{type}\". Allowed: group, image, shape, line, text.");
            return null;
        }
    }

    private static Element? ReadGroup(ObjectReader obj, string path, string? name, Value<bool> visible, float opacity, ImmutableSortedDictionary<string, StyleDefinition> styles)
    {
        var position = obj.Point("position") ?? new PointValue(new Literal<float>(0), new Literal<float>(0));
        var children = ReadElements(obj.Issues, obj.PathOf("children"), obj.Get("children"), styles) ?? [];
        return new GroupElement(path, name, visible, opacity, position, children);
    }

    private static Element? ReadImage(ObjectReader obj, string path, string? name, Value<bool> visible, float opacity)
    {
        var rect = obj.Rect("rect", required: true);
        var source = obj.Get("image", required: true) is { } img ? ValueParsers.Expr(obj.Issues, obj.PathOf("image"), img, Data.ValueKind.Image) : null;
        var fit = obj.Enum("fit", ImageFit.None);
        var anchor = obj.Enum("anchor", Anchor.Center);
        var flip = obj.Enum("flip", Flip.None);
        var smooth = obj.Bool("smooth");
        var language = LanguageCode(obj, "language");
        var scale = obj.Float("scale", min: 0.01f, max: 100);
        if (scale is not null && fit != ImageFit.Scale)
            obj.Issues.Error(obj.PathOf("scale"), "\"scale\" is used only with \"fit\": \"scale\".");
        var overflow = obj.Enum("overflow", ImageOverflow.None);

        return rect is null || source is null
            ? null
            : new ImageElement(path, name, visible, opacity, rect, source, fit, anchor, flip, smooth, language, scale ?? 1, overflow);
    }

    private static Element? ReadShape(ObjectReader obj, string path, string? name, Value<bool> visible, float opacity)
    {
        var rect = obj.Rect("rect", required: true);
        var shape = obj.Enum("shape", ShapeKind.Rect);
        var radius = obj.Float("radius", min: 0) ?? 0;
        var fill = obj.Get("fill") is { } f ? ValueParsers.Fill(obj.Issues, obj.PathOf("fill"), f) : null;
        var stroke = obj.Get("stroke") is { } s ? ValueParsers.Stroke(obj.Issues, obj.PathOf("stroke"), s) : null;

        if (!obj.Has("fill") && !obj.Has("stroke"))
            obj.Issues.Error(path, "A shape needs a \"fill\", a \"stroke\" or both.");

        return rect is null ? null : new ShapeElement(path, name, visible, opacity, rect, shape, radius, fill, stroke);
    }

    private static Element? ReadLine(ObjectReader obj, string path, string? name, Value<bool> visible, float opacity)
    {
        var from = obj.Point("from", required: true);
        var to = obj.Point("to", required: true);
        var color = obj.Color("color", required: true);
        var width = obj.Float("width", min: 0.01f) ?? 1;

        return from is null || to is null || color is null
            ? null
            : new LineElement(path, name, visible, opacity, from, to, color, width);
    }

    private static Element? ReadText(ObjectReader obj, string path, string? name, Value<bool> visible, float opacity)
    {
        var rect = obj.Rect("rect", required: true);
        var text = obj.Text("text", required: true);
        var font = obj.Get("font", required: true) is { } f ? ValueParsers.Expr(obj.Issues, obj.PathOf("font"), f, Data.ValueKind.Font) : null;
        var size = obj.Float("size", required: true, min: 0.5f, max: 1000);
        var color = obj.Color("color") ?? new Literal<Rgba>(new Rgba(0, 0, 0));
        var align = obj.Enum("align", HorizontalAlign.Left);
        var valign = obj.Enum("valign", VerticalAlign.Top);
        var lineHeight = obj.Float("lineHeight", min: 0.5f, max: 1000);
        var language = LanguageCode(obj, "language");
        var overflow = obj.Enum("overflow", TextOverflow.None);
        var ellipsis = obj.String("ellipsis") ?? TextElement.DefaultEllipsis;

        return rect is null || text is null || font is null || size is null
            ? null
            : new TextElement(path, name, visible, opacity, rect, text, font, size.Value, color, align, valign, lineHeight, language, overflow, ellipsis);
    }

    private static string FirstSentence(string message)
    {
        int cut = message.IndexOf(" Path:", StringComparison.Ordinal);
        return cut > 0 ? message[..cut] : message;
    }
}
