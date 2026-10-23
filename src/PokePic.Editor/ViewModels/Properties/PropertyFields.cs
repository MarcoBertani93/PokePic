using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokePic.Core.Data;
using PokePic.Core.Editing;
using PokePic.Core.Model;

namespace PokePic.Editor.ViewModels.Properties;

public enum FieldKind
{
    Text,
    MultilineText,
    Number,
    Bool,
    Color,
    Choice,
}

/// <summary>
/// What property fields need from the editor.
/// </summary>
public interface IPropertyHost
{
    /// <summary>Applies an edit to the template; <paramref name="rebuild"/> recreates the panel (structure changed).</summary>
    void Apply(Action<TemplateDocument> edit, bool rebuild = false);

    CardData Data { get; }

    IReadOnlyList<string> Suggestions(SuggestionSource source);

    Task<string?> AddAssetAsync(AssetKind kind);
}

public enum SuggestionSource
{
    None,
    NumberBindings,
    BoolBindings,
    ColorBindings,
    TextBindings,
    Fonts,
    Images,
    ImageSets,
    Styles,
    ShapeKinds,
    Fits,
    Anchors,
    Flips,
    Aligns,
    VAligns,
    Modes,
    Languages,
}

public enum AssetKind
{
    Font,
    Image,
}

/// <summary>
/// Reads and writes one JSON node of the document.
/// </summary>
public sealed record NodeSlot(Func<TemplateDocument, JsonNode?> Get, Action<TemplateDocument, JsonNode?> Set);

/// <summary>
/// Reads and writes the same JSON node in one or more places: with several elements selected, a field shows
/// their common value and writes to all of them.
/// </summary>
public sealed class NodeAccessor
{
    private readonly IReadOnlyList<NodeSlot> _slots;

    private NodeAccessor(IReadOnlyList<NodeSlot> slots) => _slots = slots;

    public NodeAccessor(Func<TemplateDocument, JsonNode?> get, Action<TemplateDocument, JsonNode?> set) : this([new NodeSlot(get, set)])
    {
    }

    public static NodeAccessor Property(Func<TemplateDocument, JsonObject?> owner, string name) => Property([owner], name);

    public static NodeAccessor Property(IEnumerable<Func<TemplateDocument, JsonObject?>> owners, string name) =>
        new([.. owners.Select(owner => PropertySlot(owner, name))]);

    /// <summary>The same property inside the object each slot points to.</summary>
    public NodeAccessor Property(string name) => Map(slot => PropertySlot(doc => slot.Get(doc) as JsonObject, name));

    /// <summary>Builds a derived accessor, one slot for each slot of this one.</summary>
    public NodeAccessor Map(Func<NodeSlot, NodeSlot> derive) => new([.. _slots.Select(derive)]);

    public int Count => _slots.Count;

    public IReadOnlyList<JsonNode?> GetAll(TemplateDocument doc) => [.. _slots.Select(s => s.Get(doc))];

    /// <summary>The value of the first slot.</summary>
    public JsonNode? Get(TemplateDocument doc) => _slots[0].Get(doc);

    /// <summary>True when the slots hold different values.</summary>
    public bool IsMixed(TemplateDocument doc)
    {
        var values = GetAll(doc);
        return values.Skip(1).Any(v => !JsonNode.DeepEquals(v, values[0]));
    }

    public void Set(TemplateDocument doc, JsonNode? value)
    {
        foreach (var slot in _slots)
            slot.Set(doc, value?.DeepClone());
    }

    /// <summary>Writes a value computed from each slot's current value.</summary>
    public void Update(TemplateDocument doc, Func<JsonNode?, JsonNode?> update)
    {
        foreach (var slot in _slots)
            slot.Set(doc, update(slot.Get(doc)));
    }

    private static NodeSlot PropertySlot(Func<TemplateDocument, JsonObject?> owner, string name) => new(
        doc => owner(doc)?[name],
        (doc, value) =>
        {
            if (owner(doc) is { } obj)
                TemplateDocument.SetOrRemove(obj, name, value?.DeepClone());
        });
}

public abstract partial class FieldBase : ObservableObject
{
    protected FieldBase(string label) => Label = label;

    public string Label { get; }

    public abstract void Load(TemplateDocument doc);

    /// <summary>
    /// Applies text typed but not yet confirmed. Fields never apply on each keystroke: the editor calls this
    /// on Enter, on focus changes and before any other action, so an edit is never lost.
    /// </summary>
    public virtual void CommitPending()
    {
    }
}

/// <summary>
/// Section title in the properties panel.
/// </summary>
public sealed class SectionHeader(string label) : FieldBase(label)
{
    public override void Load(TemplateDocument doc)
    {
    }
}

/// <summary>
/// Read-only text, e.g. the names of the selected elements.
/// </summary>
public sealed class InfoField(string label, string text) : FieldBase(label)
{
    public string Text { get; } = text;

    public override void Load(TemplateDocument doc)
    {
    }
}

/// <summary>
/// A scalar: literal (number, bool, color, text) or "{binding}". Empty removes the property,
/// so the style or the default applies (shown as placeholder).
/// </summary>
public sealed partial class ValueField : FieldBase
{
    private readonly IPropertyHost _host;
    private readonly NodeAccessor _accessor;
    private bool _loading;

    /// <param name="choices">Suggestions given directly, instead of <paramref name="suggestions"/>.</param>
    /// <param name="unsetChoice">A choice that stands for "not set" (e.g. "auto"): picking it removes the property.</param>
    public ValueField(IPropertyHost host, string label, NodeAccessor accessor, FieldKind kind, string placeholder = "", SuggestionSource suggestions = SuggestionSource.None, bool keepEmpty = false, IReadOnlyList<string>? choices = null, string? unsetChoice = null)
        : base(label)
    {
        _host = host;
        _accessor = accessor;
        Kind = kind;
        _placeholder = placeholder;
        Placeholder = placeholder;
        KeepEmpty = keepEmpty;
        _unsetChoice = unsetChoice;
        Suggestions = choices ?? host.Suggestions(suggestions);
    }

    private readonly string? _unsetChoice;

    public FieldKind Kind { get; }

    private readonly string _placeholder;

    /// <summary>The inherited or default value, or "{multiple values}" when the selected elements differ.</summary>
    [ObservableProperty]
    public partial string Placeholder { get; set; }

    public bool IsMixed { get; private set; }

    /// <summary>Write "" instead of removing the property when the field is cleared.</summary>
    public bool KeepEmpty { get; }

    public IReadOnlyList<string> Suggestions { get; }

    public bool HasSuggestions => Suggestions.Count > 0;

    public bool IsMultiline => Kind == FieldKind.MultilineText;

    public bool IsSingleLine => !IsMultiline && !HasSuggestions;

    public bool IsCombo => !IsMultiline && HasSuggestions;

    public bool IsColor => Kind == FieldKind.Color;

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    public partial IBrush? Swatch { get; set; }

    [ObservableProperty]
    public partial Color PickerColor { get; set; }

    public bool IsPending { get; private set; }

    public override void Load(TemplateDocument doc)
    {
        _loading = true;
        IsMixed = _accessor.IsMixed(doc);
        Text = IsMixed ? "" : ToText(_accessor.Get(doc));
        Placeholder = IsMixed ? Localization.Strings.Get("MixedValues") : _placeholder;
        IsPending = false;
        _loading = false;
        UpdateSwatch();
    }

    /// <summary>Called when the color picker flyout closes.</summary>
    public void CommitPickerColor()
    {
        var c = PickerColor;
        var hex = new Rgba(c.R, c.G, c.B, c.A).ToString();
        if (!string.Equals(hex, Text, StringComparison.OrdinalIgnoreCase))
            Text = hex;
        CommitPending();
    }

    public override void CommitPending()
    {
        if (!IsPending)
            return;
        IsPending = false;
        var node = Parse(Text);
        _host.Apply(doc => _accessor.Set(doc, node));
    }

    partial void OnTextChanged(string value)
    {
        UpdateSwatch();
        if (!_loading)
            IsPending = true;
    }

    private void UpdateSwatch()
    {
        if (!IsColor)
            return;
        Rgba? color = null;
        if (IsMixed && Text.Length == 0)
        {
            Swatch = null;
            return;
        }
        if (Rgba.TryParse(Text.Trim(), out var literal))
            color = literal;
        else if (BindingSyntax.TryParseSingleBinding(Text, out var binding, out _) && binding is not null
                 && _host.Data.Values.TryGetValue(binding.Key, out var value) && value is ColorValue cv)
            color = cv.Value;

        Swatch = color is { } c ? new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B)) : null;
        if (color is { } p)
            PickerColor = Color.FromArgb(p.A, p.R, p.G, p.B);
    }

    private JsonNode? Parse(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return KeepEmpty ? JsonValue.Create(text) : null;
        if (trimmed == _unsetChoice)
            return null;

        switch (Kind)
        {
            case FieldKind.Number when double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var number):
                return TemplateDocument.NumberNode(number);
            case FieldKind.Bool when trimmed is "true" or "false":
                return JsonValue.Create(trimmed == "true");
            case FieldKind.Text:
            case FieldKind.MultilineText:
                return JsonValue.Create(text);
            default:
                return JsonValue.Create(trimmed);
        }
    }

    public static string ToText(JsonNode? node) => node switch
    {
        null => "",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        JsonValue v when v.GetValueKind() == JsonValueKind.Number => TemplateDocument.ToDouble(v)!.Value.ToString(CultureInfo.InvariantCulture),
        JsonValue v when v.GetValueKind() == JsonValueKind.True => "true",
        JsonValue v when v.GetValueKind() == JsonValueKind.False => "false",
        _ => node.ToJsonString(),
    };
}

/// <summary>
/// [x, y] or [x, y, width, height]: each part is a number or a binding.
/// </summary>
public sealed partial class TupleField : FieldBase
{
    private readonly IPropertyHost _host;
    private readonly NodeAccessor _accessor;
    private bool _loading;

    public TupleField(IPropertyHost host, string label, NodeAccessor accessor, params string[] partLabels) : base(label)
    {
        _host = host;
        _accessor = accessor;
        Parts = [.. partLabels.Select(l => new TuplePart(this, l))];
    }

    public IReadOnlyList<TuplePart> Parts { get; }

    internal bool IsLoading => _loading;

    public override void Load(TemplateDocument doc)
    {
        _loading = true;
        var arrays = _accessor.GetAll(doc).Select(n => n as JsonArray).ToList();
        for (int i = 0; i < Parts.Count; i++)
        {
            var texts = arrays.Select(a => a is not null && i < a.Count ? ValueField.ToText(a[i]) : "").Distinct().ToList();
            Parts[i].Load(texts.Count == 1 ? texts[0] : null);
        }
        _loading = false;
    }

    /// <summary>
    /// Writes the edited parts; the others keep each element's own value. All parts empty removes the property.
    /// </summary>
    public override void CommitPending()
    {
        if (!Parts.Any(p => p.IsEdited))
            return;
        var edited = Parts.Select(p => p.IsEdited ? p.Text.Trim() : null).ToList();
        bool remove = Parts.All(p => p.Text.Trim().Length == 0 && !p.IsMixed);
        foreach (var p in Parts)
            p.IsEdited = false;

        _host.Apply(doc => _accessor.Update(doc, current =>
        {
            if (remove)
                return null;
            var old = current as JsonArray;
            return new JsonArray([.. edited.Select((t, i) => t switch
            {
                null => old is not null && i < old.Count ? old[i]?.DeepClone() : TemplateDocument.NumberNode(0),
                _ when double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) => TemplateDocument.NumberNode(n),
                "" => TemplateDocument.NumberNode(0),
                _ => JsonValue.Create(t),
            })]);
        }));
    }
}

public sealed partial class TuplePart(TupleField owner, string label) : ObservableObject
{
    public string Label { get; } = label;

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    /// <summary>The part label, or a "multiple values" mark when the selected elements differ.</summary>
    [ObservableProperty]
    public partial string Placeholder { get; set; } = label;

    public bool IsMixed { get; private set; }

    public bool IsEdited { get; set; }

    internal void Load(string? text)
    {
        IsMixed = text is null;
        Text = text ?? "";
        Placeholder = IsMixed ? Localization.Strings.Get("MixedValuesShort") : Label;
        IsEdited = false;
    }

    partial void OnTextChanged(string value)
    {
        if (!owner.IsLoading)
            IsEdited = true;
    }
}

/// <summary>
/// Fill of shapes and of the canvas background: none, solid color or linear gradient.
/// </summary>
public sealed partial class FillField : FieldBase
{
    private readonly IPropertyHost _host;
    private readonly NodeAccessor _accessor;
    private readonly float _defaultWidth;
    private bool _loading;

    public FillField(IPropertyHost host, string label, NodeAccessor accessor, float defaultWidth) : base(label)
    {
        _host = host;
        _accessor = accessor;
        _defaultWidth = defaultWidth;

        var linear = _accessor.Property("linear");
        Solid = new ValueField(host, Localization.Strings.Get("P_color"), _accessor, FieldKind.Color, "", SuggestionSource.ColorBindings);
        From = new TupleField(host, Localization.Strings.Get("P_from"), linear.Property("from"), "x", "y");
        To = new TupleField(host, Localization.Strings.Get("P_to"), linear.Property("to"), "x", "y");
        StopsAccessor = linear.Property("stops");
    }

    private static int ModeOf(JsonNode? node) => node switch
    {
        null => 0,
        JsonObject => 2,
        _ => 1,
    };

    internal NodeAccessor StopsAccessor { get; }

    public IReadOnlyList<string> Modes { get; } = [Localization.Strings.Get("FillNone"), Localization.Strings.Get("FillSolid"), Localization.Strings.Get("FillLinear")];

    [ObservableProperty]
    public partial int Mode { get; set; }

    public bool IsSolid => Mode == 1;

    public bool IsLinear => Mode == 2;

    public ValueField Solid { get; }

    public TupleField From { get; }

    public TupleField To { get; }

    public ObservableCollection<GradientStopField> Stops { get; } = [];

    public override void Load(TemplateDocument doc)
    {
        _loading = true;
        // Elements with different kinds of fill: no mode is shown until one is chosen for all.
        var modes = _accessor.GetAll(doc).Select(ModeOf).Distinct().ToList();
        Mode = modes.Count == 1 ? modes[0] : -1;
        OnPropertyChanged(nameof(IsSolid));
        OnPropertyChanged(nameof(IsLinear));
        Solid.Load(doc);
        From.Load(doc);
        To.Load(doc);

        // With several gradients, the stops they all have.
        int count = StopsAccessor.GetAll(doc).Min(n => (n as JsonArray)?.Count ?? 0);
        if (Stops.Count != count)
        {
            Stops.Clear();
            for (int i = 0; i < count; i++)
                Stops.Add(new GradientStopField(_host, this, i));
        }
        foreach (var s in Stops)
            s.Load(doc);
        _loading = false;
    }

    public override void CommitPending()
    {
        Solid.CommitPending();
        From.CommitPending();
        To.CommitPending();
        foreach (var s in Stops.ToList())
        {
            s.Offset.CommitPending();
            s.Color.CommitPending();
        }
    }

    partial void OnModeChanged(int value)
    {
        OnPropertyChanged(nameof(IsSolid));
        OnPropertyChanged(nameof(IsLinear));
        if (_loading || value < 0)
            return;

        _host.Apply(doc => _accessor.Update(doc, current => value switch
        {
            1 => current is JsonValue ? current.DeepClone() : JsonValue.Create("#FFFFFF"),
            2 => current is JsonObject ? current.DeepClone() : JsonNode.Parse(string.Create(CultureInfo.InvariantCulture,
                $$"""{ "linear": { "from": [0, 0], "to": [{{_defaultWidth}}, 0], "stops": [[0, "#FFFFFF"], [1, "#000000"]] } }""")),
            _ => null,
        }), rebuild: true);
    }

    [RelayCommand]
    private void AddStop() => _host.Apply(doc =>
    {
        foreach (var stops in StopsAccessor.GetAll(doc).OfType<JsonArray>())
            stops.Add(new JsonArray(1, "#000000"));
    }, rebuild: true);

    internal void RemoveStop(int index) => _host.Apply(doc =>
    {
        foreach (var stops in StopsAccessor.GetAll(doc).OfType<JsonArray>().Where(s => index < s.Count))
            stops.RemoveAt(index);
    }, rebuild: true);
}

public sealed partial class GradientStopField : ObservableObject
{
    private readonly FillField _owner;
    private readonly int _index;

    public GradientStopField(IPropertyHost host, FillField owner, int index)
    {
        _owner = owner;
        _index = index;
        var stop = owner.StopsAccessor.Map(stops => new NodeSlot(
            doc => stops.Get(doc) is JsonArray a && index < a.Count ? a[index] : null,
            (_, _) => throw new InvalidOperationException("A gradient stop is edited through its parts.")));
        Offset = new ValueField(host, "", stop.Map(s => Element(s, 0)), FieldKind.Number);
        Color = new ValueField(host, "", stop.Map(s => Element(s, 1)), FieldKind.Color, "", SuggestionSource.ColorBindings);
    }

    public ValueField Offset { get; }

    public ValueField Color { get; }

    public void Load(TemplateDocument doc)
    {
        Offset.Load(doc);
        Color.Load(doc);
    }

    [RelayCommand]
    private void Remove() => _owner.RemoveStop(_index);

    private static NodeSlot Element(NodeSlot stop, int i) => new(
        doc => stop.Get(doc) is JsonArray a && i < a.Count ? a[i] : null,
        (doc, v) =>
        {
            if (stop.Get(doc) is JsonArray a && i < a.Count)
                a[i] = v?.DeepClone() ?? TemplateDocument.NumberNode(0);
        });
}

/// <summary>
/// Border of a shape: { "color", "width" }, or absent.
/// </summary>
public sealed partial class StrokeField : FieldBase
{
    private readonly IPropertyHost _host;
    private readonly NodeAccessor _accessor;
    private bool _loading;

    public StrokeField(IPropertyHost host, string label, NodeAccessor accessor) : base(label)
    {
        _host = host;
        _accessor = accessor;

        // A stroke written as a plain color string is normalized to an object on edit.
        static JsonObject? Stroke(NodeSlot slot, TemplateDocument doc)
        {
            var node = slot.Get(doc);
            if (node is JsonValue color)
            {
                slot.Set(doc, new JsonObject { ["color"] = color.DeepClone(), ["width"] = 1 });
                return slot.Get(doc) as JsonObject;
            }
            return node as JsonObject;
        }

        Color = new ValueField(host, Localization.Strings.Get("P_color"), _accessor.Map(slot => new NodeSlot(
            doc => slot.Get(doc) is JsonValue v ? v : (slot.Get(doc) as JsonObject)?["color"],
            (doc, v) =>
            {
                if (Stroke(slot, doc) is { } s)
                    TemplateDocument.SetOrRemove(s, "color", v?.DeepClone());
            })), FieldKind.Color, "", SuggestionSource.ColorBindings);
        Width = new ValueField(host, Localization.Strings.Get("P_width"), _accessor.Map(slot => new NodeSlot(
            doc => (slot.Get(doc) as JsonObject)?["width"],
            (doc, v) =>
            {
                if (Stroke(slot, doc) is { } s)
                    TemplateDocument.SetOrRemove(s, "width", v?.DeepClone());
            })), FieldKind.Number, "1");
    }

    /// <summary>Null when only some of the selected elements have a border.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDetails))]
    public partial bool? Enabled { get; set; }

    public bool ShowDetails => Enabled == true;

    public ValueField Color { get; }

    public ValueField Width { get; }

    public override void Load(TemplateDocument doc)
    {
        _loading = true;
        var enabled = _accessor.GetAll(doc).Select(n => n is not null).Distinct().ToList();
        Enabled = enabled.Count == 1 ? enabled[0] : null;
        Color.Load(doc);
        Width.Load(doc);
        _loading = false;
    }

    public override void CommitPending()
    {
        Color.CommitPending();
        Width.CommitPending();
    }

    partial void OnEnabledChanged(bool? value)
    {
        if (_loading || value is not { } enabled)
            return;
        // Turning the border on keeps the borders that already exist.
        _host.Apply(doc => _accessor.Update(doc, current => !enabled ? null
            : current?.DeepClone() ?? new JsonObject { ["color"] = "#000000", ["width"] = 1 }), rebuild: true);
    }
}

/// <summary>
/// Source of an image element: a value ("{frame}", "{species.sprite}") or a path ("images/a.png", "{BASE}/{species}.png"),
/// or the item of a set of images, written { "set": …, "key": … } or "{set[key]}".
/// </summary>
public sealed partial class ImageSourceField : FieldBase
{
    private readonly IPropertyHost _host;
    private readonly NodeAccessor _accessor;
    private bool _loading;

    public ImageSourceField(IPropertyHost host, string label, NodeAccessor accessor) : base(label)
    {
        _host = host;
        _accessor = accessor;
        Image = new ValueField(host, Localization.Strings.Get("P_imageValue"), _accessor, FieldKind.Choice, "", SuggestionSource.Images);
        // An image of a set is { "set": …, "key": … } or, with one plain key, "{set[key]}": both are shown as set and keys.
        SetName = new ValueField(host, Localization.Strings.Get("P_set"), _accessor.Map(slot => new NodeSlot(
            doc => SetParts(slot.Get(doc)) is { Set.Length: > 0 } parts ? JsonValue.Create(parts.Set) : null,
            (doc, v) => slot.Set(doc, WriteSet(slot.Get(doc), ValueField.ToText(v), SetParts(slot.Get(doc)).Key)))),
            FieldKind.Choice, "", SuggestionSource.ImageSets);
        // Candidate keys, one per line: a single key is written as a string, several as an array.
        SetKey = new ValueField(host, Localization.Strings.Get("P_keys"), _accessor.Map(slot => new NodeSlot(
            doc => SetParts(slot.Get(doc)).Key switch
            {
                JsonArray keys => JsonValue.Create(string.Join("\n", keys.Select(ValueField.ToText))),
                var key => key?.DeepClone(),
            },
            (doc, v) =>
            {
                var old = slot.Get(doc);
                var keys = ValueField.ToText(v).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                JsonNode key = keys.Length == 1 ? JsonValue.Create(keys[0]) : new JsonArray([.. keys.Select(k => (JsonNode?)JsonValue.Create(k))]);
                slot.Set(doc, WriteSet(old, SetParts(old).Set, key));
            })), FieldKind.MultilineText, Localization.Strings.Get("KeysPlaceholder"));
    }

    private static int ModeOf(JsonNode? node) => node is JsonObject || ShortSet(node) is not null ? 1 : 0;

    /// <summary>"{icons[move4.type.key]}": an image of a set written in the short form.</summary>
    private static Binding? ShortSet(JsonNode? node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.String
        && BindingSyntax.TryParseSingleBinding(v.GetValue<string>(), out var binding, out _) && binding?.Set is not null
            ? binding
            : null;

    private static (string Set, JsonNode? Key) SetParts(JsonNode? node) => ShortSet(node) is { } binding
        ? (binding.Set!, JsonValue.Create($"{{{binding.Key}}}"))
        : ((node as JsonObject)?["set"] is { } set ? ValueField.ToText(set) : "", (node as JsonObject)?["key"]);

    /// <summary>
    /// Writes an image of a set, keeping the short form "{set[key]}" when the node had it and the key is still
    /// one plain binding; otherwise { "set": …, "key": … }.
    /// </summary>
    private static JsonNode WriteSet(JsonNode? old, string set, JsonNode? key)
    {
        if (ShortSet(old) is not null && key is JsonValue k
            && BindingSyntax.TryParseSingleBinding(ValueField.ToText(k), out var binding, out _)
            && binding is { Format: null, Negate: false, Set: null })
            return JsonValue.Create($"{{{set}[{binding.Key}]}}");
        return new JsonObject { ["set"] = set, ["key"] = key?.DeepClone() ?? "" };
    }

    public IReadOnlyList<string> Modes { get; } = [Localization.Strings.Get("SourceValue"), Localization.Strings.Get("SourceSet")];

    [ObservableProperty]
    public partial int Mode { get; set; }

    public bool IsValue => Mode == 0;

    public bool IsSet => Mode == 1;

    public ValueField Image { get; }

    public ValueField SetName { get; }

    public ValueField SetKey { get; }

    public override void Load(TemplateDocument doc)
    {
        _loading = true;
        var modes = _accessor.GetAll(doc).Select(ModeOf).Distinct().ToList();
        Mode = modes.Count == 1 ? modes[0] : -1;
        NotifyMode();
        Image.Load(doc);
        SetName.Load(doc);
        SetKey.Load(doc);
        _loading = false;
    }

    public override void CommitPending()
    {
        Image.CommitPending();
        SetName.CommitPending();
        SetKey.CommitPending();
    }

    partial void OnModeChanged(int value)
    {
        NotifyMode();
        if (_loading || value < 0)
            return;
        _host.Apply(doc => _accessor.Set(doc, value == 1
            ? new JsonObject { ["set"] = _host.Suggestions(SuggestionSource.ImageSets).FirstOrDefault() ?? "", ["key"] = "" }
            : JsonValue.Create(_host.Suggestions(SuggestionSource.Images).FirstOrDefault() ?? "")), rebuild: true);
    }

    private void NotifyMode()
    {
        OnPropertyChanged(nameof(IsValue));
        OnPropertyChanged(nameof(IsSet));
    }
}

