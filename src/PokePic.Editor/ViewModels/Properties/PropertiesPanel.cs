using System.Text.Json.Nodes;
using PokePic.Core.Editing;
using PokePic.Core.Model;
using PokePic.Editor.Localization;

namespace PokePic.Editor.ViewModels.Properties;

/// <summary>
/// Fields for the selected elements, for a style, or for the template itself when nothing is selected.
/// The panel is recreated only when the selection or the element structure changes, so focus survives edits.
/// </summary>
public sealed class PropertiesPanel
{
    public const string TemplateType = "template";
    public const string MultipleType = "multiple";
    public const string StyleType = "style";

    private PropertiesPanel(string key, string title, string type, IReadOnlyList<FieldBase> fields)
    {
        Key = key;
        Title = title;
        Type = type;
        Fields = fields;
    }

    /// <summary>Identifies what the panel shows; see <see cref="KeyFor"/> and <see cref="StyleKey"/>.</summary>
    public string Key { get; }

    public string Title { get; }

    /// <summary>Element type, <see cref="TemplateType"/>, <see cref="MultipleType"/> or <see cref="StyleType"/>: picks the header icon.</summary>
    public string Type { get; }

    public IReadOnlyList<FieldBase> Fields { get; }

    public static string KeyFor(IReadOnlyList<string> selection) => selection.Count == 0 ? TemplateType : string.Join("|", selection);

    /// <summary>A style's fields depend on its type: changing the type rebuilds the panel.</summary>
    public static string StyleKey(TemplateDocument doc, string name) => $"style:{name}:{ValueField.ToText(Style(doc, name)?["type"])}";

    public void Load(TemplateDocument doc)
    {
        foreach (var f in Fields)
            f.Load(doc);
    }

    public void CommitPending()
    {
        foreach (var f in Fields)
            f.CommitPending();
    }

    public static PropertiesPanel ForTemplate(IPropertyHost host, TemplateDocument doc)
    {
        JsonObject Root(TemplateDocument d) => d.Root;
        JsonObject? Canvas(TemplateDocument d) => d.Root["canvas"] as JsonObject;
        float width = (float?)TemplateDocument.ToDouble(Canvas(doc)?["width"]) ?? 100;

        var fields = new List<FieldBase>
        {
            new ValueField(host, "id", NodeAccessor.Property(Root, "id"), FieldKind.Text),
            new ValueField(host, Strings.Get("P_name"), NodeAccessor.Property(Root, "name"), FieldKind.Text),
            new ValueField(host, Strings.Get("P_language"), NodeAccessor.Property(Root, "language"), FieldKind.Choice, Strings.Get("LanguageDefault"), SuggestionSource.Languages),
            new ValueField(host, Strings.Get("P_version"), NodeAccessor.Property(Root, "version"), FieldKind.Number, "1"),
            new SectionHeader(Strings.Get("S_canvas")),
            new ValueField(host, Strings.Get("P_width"), NodeAccessor.Property(Canvas, "width"), FieldKind.Number),
            new ValueField(host, Strings.Get("P_height"), NodeAccessor.Property(Canvas, "height"), FieldKind.Number),
            new ValueField(host, Strings.Get("P_mode"), NodeAccessor.Property(Canvas, "mode"), FieldKind.Choice, "highResolution", SuggestionSource.Modes),
            new ValueField(host, Strings.Get("P_scale"), NodeAccessor.Property(Canvas, "scale"), FieldKind.Number, "1"),
            new FillField(host, Strings.Get("P_background"), NodeAccessor.Property(Canvas, "background"), width),
        };
        var panel = new PropertiesPanel(TemplateType, Strings.Get("S_template"), TemplateType, fields);
        panel.Load(doc);
        return panel;
    }

    /// <summary>
    /// One element: all its fields. Several: the fields they have in common, writing to all of them;
    /// a field shows the shared value, or "{multiple values}" when they differ.
    /// </summary>
    public static PropertiesPanel? ForElements(IPropertyHost host, TemplateDocument doc, IReadOnlyList<string> paths)
    {
        var elements = paths.Select(doc.GetElement).ToList();
        if (paths.Count == 0 || elements.Any(e => e is null))
            return null;

        var owners = paths.Select(p => (Func<TemplateDocument, JsonObject?>)(d => d.GetElement(p))).ToList();
        NodeAccessor P(string name) => NodeAccessor.Property(owners, name);
        var types = elements.Select(e => ValueField.ToText(e!["type"])).Distinct().ToList();
        string? type = types.Count == 1 ? types[0] : null;
        bool single = paths.Count == 1;

        // Placeholders show the value inherited from the style (when all elements share it), or the default.
        var styles = elements.Select(e => e!["style"] is JsonValue s ? Style(doc, ValueField.ToText(s)) : null).ToList();
        string Inherited(string name, string fallback)
        {
            var values = styles.Select(s => s?[name] is { } v ? ValueField.ToText(v) : null).Distinct().ToList();
            return values is [{ } value] ? $"{value} (style)" : fallback;
        }

        var fields = new List<FieldBase>();
        if (single)
            fields.Add(new ValueField(host, Strings.Get("P_name"), P("name"), FieldKind.Text));
        else
            fields.Add(new InfoField(Strings.Get("P_names"), string.Join("\n", elements.Select(e => NameOf(e!)))));
        fields.Add(new ValueField(host, Strings.Get("P_visible"), P("visible"), FieldKind.Bool, "true", SuggestionSource.BoolBindings));
        fields.Add(new ValueField(host, Strings.Get("P_opacity"), P("opacity"), FieldKind.Number, "1"));
        if (!types.Contains("group"))
        {
            // Only the styles these elements can use: those for their type, and those without a type.
            var usable = (doc.Root["styles"] as JsonObject)?
                .Where(kv => kv.Value?["type"] is not JsonValue t || ValueField.ToText(t) == type)
                .Select(kv => kv.Key).ToList() ?? [];
            fields.Add(new ValueField(host, Strings.Get("P_style"), P("style"), FieldKind.Choice, "", choices: usable));
        }
        // Elements of different types still share their rectangle, when they all have one.
        if (type is null && !types.Contains("line") && !types.Contains("group"))
            fields.Add(new TupleField(host, Strings.Get("P_rect"), P("rect"), "x", "y", "w", "h"));
        if (type is not null)
            AddTypeFields(fields, host, doc, type, P, Inherited, forStyle: false);

        var panel = single
            ? new PropertiesPanel(KeyFor(paths), NameOf(elements[0]!), type!, fields)
            : new PropertiesPanel(KeyFor(paths), Strings.Format("SelectedCount", paths.Count), MultipleType, fields);
        panel.Load(doc);
        return panel;
    }

    /// <summary>
    /// A style: its element type and the properties it sets for that type (all of them are defaults that the
    /// element can override). Without a type only the properties every element has.
    /// </summary>
    public static PropertiesPanel? ForStyle(IPropertyHost host, TemplateDocument doc, string name)
    {
        if (Style(doc, name) is not { } style)
            return null;

        NodeAccessor P(string property) => NodeAccessor.Property(d => Style(d, name), property);
        string Default(string _, string fallback) => fallback;
        var type = style["type"] is JsonValue t ? ValueField.ToText(t) : null;

        var fields = new List<FieldBase>
        {
            new ValueField(host, Strings.Get("P_styleType"), P("type"), FieldKind.Choice, Strings.Get("StyleAnyType"), choices: ElementProperties.Types),
            new ValueField(host, Strings.Get("P_visible"), P("visible"), FieldKind.Bool, "true", SuggestionSource.BoolBindings),
            new ValueField(host, Strings.Get("P_opacity"), P("opacity"), FieldKind.Number, "1"),
        };
        if (type is not null)
            AddTypeFields(fields, host, doc, type, P, Default, forStyle: true);

        var panel = new PropertiesPanel(StyleKey(doc, name), name, StyleType, fields);
        panel.Load(doc);
        return panel;
    }

    /// <summary>
    /// The fields of one element type. A style leaves out where the element is (rect, position, from, to)
    /// and what it shows (text, image): those belong to each element.
    /// </summary>
    private static void AddTypeFields(List<FieldBase> fields, IPropertyHost host, TemplateDocument doc, string type,
        Func<string, NodeAccessor> P, Func<string, string, string> Inherited, bool forStyle)
    {
        float canvasWidth = (float?)TemplateDocument.ToDouble((doc.Root["canvas"] as JsonObject)?["width"]) ?? 100;
        bool element = !forStyle;
        switch (type)
        {
            case "group":
                if (element)
                    fields.Add(new TupleField(host, Strings.Get("P_position"), P("position"), "x", "y"));
                break;

            case "shape":
                if (element)
                    fields.Add(new TupleField(host, Strings.Get("P_rect"), P("rect"), "x", "y", "w", "h"));
                fields.Add(new ValueField(host, Strings.Get("P_shape"), P("shape"), FieldKind.Choice, Inherited("shape", "rect"), SuggestionSource.ShapeKinds));
                fields.Add(new ValueField(host, Strings.Get("P_radius"), P("radius"), FieldKind.Number, Inherited("radius", "0"), SuggestionSource.NumberBindings));
                fields.Add(new FillField(host, Strings.Get("P_fill"), P("fill"), canvasWidth));
                fields.Add(new StrokeField(host, Strings.Get("P_stroke"), P("stroke")));
                break;

            case "line":
                if (element)
                {
                    fields.Add(new TupleField(host, Strings.Get("P_from"), P("from"), "x", "y"));
                    fields.Add(new TupleField(host, Strings.Get("P_to"), P("to"), "x", "y"));
                }
                fields.Add(new ValueField(host, Strings.Get("P_color"), P("color"), FieldKind.Color, Inherited("color", ""), SuggestionSource.ColorBindings));
                fields.Add(new ValueField(host, Strings.Get("P_width"), P("width"), FieldKind.Number, Inherited("width", "1"), SuggestionSource.NumberBindings));
                break;

            case "image":
                if (element)
                {
                    fields.Add(new TupleField(host, Strings.Get("P_rect"), P("rect"), "x", "y", "w", "h"));
                    fields.Add(new ImageSourceField(host, Strings.Get("P_image"), P("image")));
                }
                fields.Add(new ValueField(host, Strings.Get("P_fit"), P("fit"), FieldKind.Choice, Inherited("fit", "none"), SuggestionSource.Fits));
                fields.Add(new ValueField(host, Strings.Get("P_scale"), P("scale"), FieldKind.Number, Inherited("scale", Strings.Get("ScaleOnlyWithFit"))));
                fields.Add(new ValueField(host, Strings.Get("P_overflow"), P("overflow"), FieldKind.Choice, Inherited("overflow", "none"), choices: ["none", "crop"]));
                fields.Add(new ValueField(host, Strings.Get("P_anchor"), P("anchor"), FieldKind.Choice, Inherited("anchor", "center"), SuggestionSource.Anchors));
                fields.Add(new ValueField(host, Strings.Get("P_flip"), P("flip"), FieldKind.Choice, Inherited("flip", "none"), SuggestionSource.Flips));
                // auto (no property): smooth in high resolution; pixel-perfect always samples by pixel.
                fields.Add(new ValueField(host, Strings.Get("P_smooth"), P("smooth"), FieldKind.Bool, Inherited("smooth", Strings.Get("Auto")),
                    choices: [Strings.Get("Auto"), "true", "false"], unsetChoice: Strings.Get("Auto")));
                // Used by image sets with "{language}" in the pattern.
                fields.Add(new ValueField(host, Strings.Get("P_language"), P("language"), FieldKind.Choice, Inherited("language", Strings.Get("LanguageTemplate")), SuggestionSource.Languages));
                break;

            case "text":
                if (element)
                {
                    fields.Add(new TupleField(host, Strings.Get("P_rect"), P("rect"), "x", "y", "w", "h"));
                    fields.Add(new ValueField(host, Strings.Get("P_text"), P("text"), FieldKind.MultilineText, "", keepEmpty: true));
                }
                fields.Add(new ValueField(host, Strings.Get("P_font"), P("font"), FieldKind.Choice, Inherited("font", ""), SuggestionSource.Fonts));
                fields.Add(new ValueField(host, Strings.Get("P_size"), P("size"), FieldKind.Number, Inherited("size", "")));
                fields.Add(new ValueField(host, Strings.Get("P_color"), P("color"), FieldKind.Color, Inherited("color", "#000000"), SuggestionSource.ColorBindings));
                fields.Add(new ValueField(host, Strings.Get("P_align"), P("align"), FieldKind.Choice, Inherited("align", "left"), SuggestionSource.Aligns));
                fields.Add(new ValueField(host, Strings.Get("P_valign"), P("valign"), FieldKind.Choice, Inherited("valign", "top"), SuggestionSource.VAligns));
                fields.Add(new ValueField(host, Strings.Get("P_lineHeight"), P("lineHeight"), FieldKind.Number, Inherited("lineHeight", Strings.Get("Auto"))));
                fields.Add(new ValueField(host, Strings.Get("P_overflow"), P("overflow"), FieldKind.Choice, Inherited("overflow", "none"), choices: ["none", "crop", "ellipsis"]));
                fields.Add(new ValueField(host, Strings.Get("P_ellipsis"), P("ellipsis"), FieldKind.Text, Inherited("ellipsis", Core.Model.TextElement.DefaultEllipsis), keepEmpty: true));
                fields.Add(new ValueField(host, Strings.Get("P_language"), P("language"), FieldKind.Choice, Inherited("language", Strings.Get("LanguageTemplate")), SuggestionSource.Languages));
                break;
        }
    }

    private static JsonObject? Style(TemplateDocument doc, string name) => (doc.Root["styles"] as JsonObject)?[name] as JsonObject;

    private static string NameOf(JsonObject element) =>
        element["name"] is { } n ? ValueField.ToText(n) : ValueField.ToText(element["type"]);
}
