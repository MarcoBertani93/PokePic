using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokePic.Core;
using PokePic.Core.Assets;
using PokePic.Core.Data;
using PokePic.Core.Editing;
using PokePic.Core.Model;
using PokePic.Core.Resolution;
using PokePic.Editor.Localization;
using PokePic.Editor.ViewModels.Properties;

namespace PokePic.Editor.ViewModels.Values;

/// <summary>
/// The value panels, one per kind (texts, numbers, true/false, colors, images, fonts): on one side the values of the data
/// pack, read-only; on the other the template's values of that kind, editable. Typed text is applied on Enter or when
/// the focus moves (<see cref="CommitPending"/>), as one undoable edit of the JSON.
/// </summary>
public sealed class ValueTabs
{
    public ValueTabs(MainViewModel main)
    {
        Main = main;
        Tabs =
        [
            new KindTab(this, "TextValues", "S_texts", [ValueKind.Text]),
            new KindTab(this, "NumberValues", "S_numbers", [ValueKind.Int, ValueKind.Float]),
            new KindTab(this, "BoolValues", "S_bools", [ValueKind.Bool]),
            new KindTab(this, "ColorValues", "S_colors", [ValueKind.Color]),
            new KindTab(this, "ImageValues", "S_images", [ValueKind.Image]),
            new KindTab(this, "FontValues", "S_fonts", [ValueKind.Font]),
        ];
    }

    internal MainViewModel Main { get; }

    internal FilePreviews Previews { get; } = new();

    internal int Generation { get; private set; }

    public IReadOnlyList<KindTab> Tabs { get; }

    public KindTab Tab(string id) => Tabs.Single(t => t.Id == id);

    /// <summary>
    /// Shows the data pack's values and the document's values. <paramref name="template"/> is the parsed template (null if it
    /// has errors): it gives what each value is with the current data and language.
    /// </summary>
    public void Load(TemplateDocument? doc, CardTemplate? template, CardData data, string language, IAssetProvider assets)
    {
        Generation++;
        var resolver = template is null ? null : BindingResolver.For(template, data, language);
        foreach (var tab in Tabs)
            tab.Load(doc, template, resolver, data, language, assets);
    }

    /// <summary>F5: files are read again.</summary>
    public void Forget() => Previews.Forget();

    public void CommitPending()
    {
        foreach (var tab in Tabs)
            tab.CommitPending();
    }

    internal void Edit(Action<TemplateDocument> edit) => Main.ApplyEdit(doc =>
    {
        edit(doc);
        ValueEdits.Tidy(doc);
        return null;
    }, rebuildProperties: false);
}

/// <summary>The values of one kind (numbers: whole and decimal).</summary>
public sealed partial class KindTab : ObservableObject
{
    private readonly ValueTabs _tabs;

    internal KindTab(ValueTabs tabs, string id, string titleKey, ValueKind[] kinds)
    {
        _tabs = tabs;
        Id = id;
        TitleKey = titleKey;
        Kinds = kinds;
    }

    public string Id { get; }

    public string TitleKey { get; }

    public IReadOnlyList<ValueKind> Kinds { get; }

    internal ValueTabs Tabs => _tabs;

    public ObservableCollection<DataItem> DataItems { get; } = [];

    public ObservableCollection<ValueItem> Items { get; } = [];

    public bool IsNumbers => Kinds.Count > 1;

    /// <summary>Images and fonts can be added from a file.</summary>
    public bool CanImport => Kinds[0] is ValueKind.Image or ValueKind.Font;

    /// <summary>Texts, images and fonts can have a pattern.</summary>
    public bool CanHavePattern => TemplateValue.IsComposable(Kinds[0]);

    /// <summary>Width of a cell: texts and paths are longer than numbers and colors.</summary>
    public double CellWidth => CanHavePattern ? 220 : 120;

    internal void Load(TemplateDocument? doc, CardTemplate? template, BindingResolver? resolver, CardData data, string language, IAssetProvider assets)
    {
        foreach (var old in DataItems)
            old.Thumbnail?.Dispose();
        DataItems.Clear();
        foreach (var (key, value) in data.Values.Where(kv => Kinds.Contains(kv.Value.Kind)))
            DataItems.Add(DataItem.Of(key, value, language));

        var entries = Kinds
            .SelectMany(k => (((doc?.Root["values"] as JsonObject)?[ValueEdits.SectionOf(k)] as JsonObject) ?? []).Select(kv => (Section: ValueEdits.SectionOf(k), kv.Key, kv.Value)))
            .ToList();
        if (!Items.Select(i => (i.Section, i.Name)).SequenceEqual(entries.Select(e => (e.Section, e.Key))))
        {
            Items.Clear();
            foreach (var (section, name, _) in entries)
                Items.Add(new ValueItem(this, section, name));
        }
        for (int i = 0; i < entries.Count; i++)
        {
            Items[i].Load(entries[i].Value);
            _ = Items[i].DescribeAsync(template?.Values.GetValueOrDefault(Items[i].Name), resolver, language, assets, _tabs.Generation);
        }
    }

    internal void CommitPending()
    {
        foreach (var item in Items.ToList())
            item.CommitPending();
    }

    internal void Edit(Action<TemplateDocument> edit) => _tabs.Edit(edit);

    /// <summary>A new value of the section ("int" or "float" for numbers), named after the kind.</summary>
    [RelayCommand]
    private void Add(string? section)
    {
        section ??= ValueEdits.SectionOf(Kinds[0]);
        Edit(doc => ValueEdits.Section(doc, section)[ValueEdits.UniqueName(doc, section)] = ValueEdits.Default(section));
    }

    [RelayCommand]
    private async Task ImportAsync() => await ((IPropertyHost)_tabs.Main).AddAssetAsync(Kinds[0] == ValueKind.Font ? AssetKind.Font : AssetKind.Image);

    [RelayCommand]
    private void InsertData(DataItem? item)
    {
        if (item is not null)
            _tabs.Main.InsertBindingText($"{{{item.Key}}}");
    }

    internal void Insert(string text) => _tabs.Main.InsertBindingText(text);
}

/// <summary>A value of the data pack, in the preview language.</summary>
public sealed record DataItem(string Key, string Display, string? Languages, IBrush? Swatch, Bitmap? Thumbnail)
{
    internal static DataItem Of(string key, CardValue value, string language)
    {
        var shown = LocalizedValue.In(value, language);
        var languages = value is LocalizedValue l ? string.Join(", ", l.Values.Keys) : null;
        return shown switch
        {
            ColorValue c => new DataItem(key, c.Value.ToString(), languages, Brush(c.Value), null),
            ImageValue image => new DataItem(key, "", languages, null, FilePreviews.Thumbnail([.. image.Image.Encoded])),
            TextValue { Value.Length: 0 } => new DataItem(key, "\"\"", languages, null, null),
            _ => new DataItem(key, shown.ToText(), languages, null, null),
        };
    }

    internal static IBrush Brush(Rgba c) => new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
}

public enum ValueForm
{
    Single,
    Items,
    Pattern,
}

public enum RowKind
{
    Single,
    Item,
    Fallback,
    Pattern,
}

/// <summary>
/// A template value as a table: a row for a single value, or a row per item of a set (or the pattern) plus the fallback;
/// a column per language. A value written once is its "en" cell; more languages make it { "en": …, "it": … }.
/// </summary>
public sealed partial class ValueItem : ObservableObject
{
    private readonly KindTab _tab;

    /// <summary>Languages added in this session that no cell uses yet: kept as empty columns until filled.</summary>
    private readonly List<string> _added = [];

    private bool _loading;

    internal ValueItem(KindTab tab, string section, string name)
    {
        _tab = tab;
        Section = section;
        Name = name;
        EditName = name;
    }

    public KindTab Tab => _tab;

    /// <summary>"text", "int"…: the section of "values".</summary>
    public string Section { get; }

    public string Name { get; }

    [ObservableProperty]
    public partial string EditName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKeys), nameof(IsItems))]
    public partial ValueForm Form { get; set; }

    public IReadOnlyList<ValueForm> Forms => _tab.CanHavePattern ? [ValueForm.Single, ValueForm.Items, ValueForm.Pattern] : [ValueForm.Single, ValueForm.Items];

    public IReadOnlyList<string> FormLabels => [.. Forms.Select(f => Strings.Get($"Form{f}"))];

    public int FormIndex
    {
        get => Forms.ToList().IndexOf(Form);
        set
        {
            if (value >= 0 && value < Forms.Count && !_loading && Forms[value] != Form)
                SetForm(Forms[value]);
        }
    }

    /// <summary>A set: the rows have a key (or are the pattern and the fallback).</summary>
    public bool HasKeys => Form != ValueForm.Single;

    public bool IsItems => Form == ValueForm.Items;

    public ObservableCollection<LanguageColumn> Columns { get; } = [];

    public ObservableCollection<ValueRow> Rows { get; } = [];

    /// <summary>Code typed for a new column.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLanguageCommand))]
    public partial string NewLanguage { get; set; } = "";

    internal IReadOnlyList<string> Languages => [.. Columns.Select(c => c.Code)];

    internal void Load(JsonNode? node)
    {
        _loading = true;
        var obj = node as JsonObject;
        Form = obj?.ContainsKey("items") == true ? ValueForm.Items : obj?.ContainsKey("pattern") == true ? ValueForm.Pattern : ValueForm.Single;
        OnPropertyChanged(nameof(FormIndex));

        var rows = new List<(RowKind Kind, string? Key, Dictionary<string, string> Cells)>();
        switch (Form)
        {
            case ValueForm.Single:
                rows.Add((RowKind.Single, null, ValueEdits.Cells(node)));
                break;
            case ValueForm.Items:
                foreach (var (key, body) in obj!["items"] as JsonObject ?? [])
                    rows.Add((RowKind.Item, key, ValueEdits.Cells(body)));
                rows.Add((RowKind.Fallback, null, obj!["fallback"] is { } itemsFallback ? ValueEdits.Cells(itemsFallback) : []));
                break;
            case ValueForm.Pattern:
                rows.Add((RowKind.Pattern, null, ValueEdits.Cells(obj!["pattern"])));
                rows.Add((RowKind.Fallback, null, obj!["fallback"] is { } patternFallback ? ValueEdits.Cells(patternFallback) : []));
                break;
        }

        var languages = rows.SelectMany(r => r.Cells.Keys).Concat(_added).Append(Core.Data.Languages.Fallback)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(l => l == Core.Data.Languages.Fallback ? 0 : 1)
            .ThenBy(l => l, StringComparer.Ordinal)
            .ToList();
        bool sameColumns = Languages.SequenceEqual(languages);
        if (!sameColumns)
        {
            Columns.Clear();
            foreach (var language in languages)
                Columns.Add(new LanguageColumn(this, language));
        }
        if (!sameColumns || !Rows.Select(r => (r.Kind, r.Key)).SequenceEqual(rows.Select(r => (r.Kind, r.Key))))
        {
            Rows.Clear();
            foreach (var (kind, key, _) in rows)
                Rows.Add(new ValueRow(this, kind, key, languages));
        }
        for (int i = 0; i < rows.Count; i++)
            Rows[i].Load(rows[i].Cells);
        EditName = Name;
        _loading = false;
    }

    /// <summary>Each row's value in the preview language, with the current data: text, color, thumbnail or font sample.</summary>
    internal async Task DescribeAsync(TemplateValue? parsed, BindingResolver? resolver, string language, IAssetProvider assets, int generation)
    {
        foreach (var row in Rows)
        {
            if (parsed is null || resolver is null)
            {
                row.SetPreview(null, null, null, Strings.Get("ResourceUnavailable"));
                continue;
            }
            var body = (row.Kind, parsed.Definition) switch
            {
                (RowKind.Single, PlainDefinition p) => p.Body,
                (RowKind.Item, ItemsDefinition i) => i.Items.GetValueOrDefault(row.Key!),
                (RowKind.Fallback, ItemsDefinition i) => i.Fallback,
                (RowKind.Fallback, PatternDefinition p) => p.Fallback,
                _ => null, // a pattern depends on the key
            };
            if (body is null)
            {
                row.SetPreview(null, null, null, null);
                continue;
            }

            CardValue value;
            try
            {
                value = resolver.Evaluate(body.For(language), parsed.Kind, parsed.Path, language);
            }
            catch (TemplateException ex)
            {
                row.SetPreview(null, null, null, ex.Issues[0].Message);
                continue;
            }

            switch (value)
            {
                case ColorValue c:
                    row.SetPreview(c.Value.ToString(), DataItem.Brush(c.Value), null, null);
                    break;
                case ImageValue image:
                    row.SetPreview(Strings.Get("DataImage"), null, FilePreviews.Thumbnail([.. image.Image.Encoded]), null);
                    break;
                case FileValue file:
                    var preview = await _tab.Tabs.Previews.GetAsync(file.Path, file.Kind == ValueKind.Font, assets);
                    if (generation != _tab.Tabs.Generation)
                        return; // a newer load is running
                    row.SetPreview(preview.Info.Length > 0 ? $"{preview.Info} · {preview.Location}" : preview.Location, null,
                        preview.Png is { } png ? FilePreviews.Thumbnail(png, preview.Scale) : null, preview.Error);
                    break;
                default:
                    row.SetPreview(value is TextValue { Value.Length: 0 } ? "\"\"" : value.ToText(), null, null, null);
                    break;
            }
        }
    }

    internal void CommitPending()
    {
        var name = EditName.Trim();
        if (Rows.FirstOrDefault(r => r.IsPending) is { } row)
            row.CommitPending();
        else if (name != Name && name.Length > 0)
            Edit((doc, entries) =>
            {
                if (ValueEdits.UniqueName(doc, name) == name)
                    ValueEdits.Rename(entries, Name, name);
            });
    }

    /// <summary>Writes a row's cells.</summary>
    internal void SetCells(ValueRow row, IReadOnlyDictionary<string, string> cells) => Edit((_, entries) => Write(entries, row, cells, Languages));

    private void Write(JsonObject entries, ValueRow row, IReadOnlyDictionary<string, string> cells, IReadOnlyList<string> languages)
    {
        var body = ValueEdits.Body(Section, languages, cells);
        var set = entries[Name] as JsonObject;
        switch (row.Kind)
        {
            case RowKind.Single:
                entries[Name] = body;
                break;
            case RowKind.Item when set is not null && set["items"] is JsonObject items:
                items[row.Key!] = body;
                break;
            case RowKind.Pattern when set is not null:
                set["pattern"] = body;
                break;
            case RowKind.Fallback when set is not null:
                TemplateDocument.SetOrRemove(set, "fallback", cells.Values.All(v => v.Length == 0) ? null : body);
                break;
        }
    }

    private void Edit(Action<TemplateDocument, JsonObject> edit) => _tab.Edit(doc => edit(doc, ValueEdits.Section(doc, Section)));

    private bool CanAddLanguage() =>
        Core.Data.Languages.IsValidCode(NewLanguage.Trim()) && !Columns.Any(c => string.Equals(c.Code, NewLanguage.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>An empty column: nothing changes in the JSON until a cell is filled.</summary>
    [RelayCommand(CanExecute = nameof(CanAddLanguage))]
    private void AddLanguage()
    {
        _added.Add(NewLanguage.Trim());
        NewLanguage = "";
        Load(Current());
    }

    /// <summary>Removes the language from every row.</summary>
    internal void RemoveLanguage(string language)
    {
        _added.Remove(language);
        var rows = Rows.Where(r => r.Has(language)).ToList();
        if (rows.Count == 0)
        {
            Load(Current());
            return;
        }
        var languages = Languages.Where(l => l != language).ToList();
        Edit((_, entries) =>
        {
            foreach (var row in rows)
                Write(entries, row, row.Loaded.Where(kv => kv.Key != language).ToDictionary(StringComparer.Ordinal), languages);
        });
    }

    private JsonNode? Current() => ((_tab.Tabs.Main.Document?.Root["values"] as JsonObject)?[Section] as JsonObject)?[Name];

    /// <summary>
    /// Changes the form keeping what fits: a single value becomes the first item, or the fallback of a pattern; a set
    /// becomes its first item, or its fallback.
    /// </summary>
    private void SetForm(ValueForm form) => Edit((_, entries) =>
    {
        var node = entries[Name];
        var obj = node as JsonObject;
        JsonNode? single = obj is null || !(obj.ContainsKey("items") || obj.ContainsKey("pattern"))
            ? node?.DeepClone()
            : ((obj["items"] as JsonObject)?.FirstOrDefault().Value ?? obj["fallback"])?.DeepClone();
        var fallback = obj?["fallback"]?.DeepClone();
        single ??= ValueEdits.Default(Section);

        entries[Name] = form switch
        {
            ValueForm.Items => new JsonObject { ["items"] = new JsonObject { ["key1"] = single } },
            ValueForm.Pattern => fallback is null
                ? new JsonObject { ["pattern"] = "{key}", ["fallback"] = single }
                : new JsonObject { ["pattern"] = "{key}", ["fallback"] = fallback },
            _ => single,
        };
    });

    [RelayCommand]
    private void AddRow() => Edit((_, entries) =>
    {
        if ((entries[Name] as JsonObject)?["items"] is not JsonObject items)
            return;
        var key = "key1";
        for (int i = 2; items.ContainsKey(key); i++)
            key = $"key{i}";
        // The new item copies the last one, as a starting point.
        items[key] = items.LastOrDefault().Value?.DeepClone() ?? ValueEdits.Default(Section);
    });

    internal void RenameKey(string key, string newKey) => Edit((_, entries) =>
    {
        if ((entries[Name] as JsonObject)?["items"] is JsonObject items && TemplateValue.IsValidKey(newKey))
            ValueEdits.Rename(items, key, newKey);
    });

    internal void RemoveKey(string key) => Edit((_, entries) => ((entries[Name] as JsonObject)?["items"] as JsonObject)?.Remove(key));

    [RelayCommand]
    private void Remove() => Edit((_, entries) => entries.Remove(Name));

    /// <summary>"{name}", or for a set "{name[key]}", where the JSON caret is.</summary>
    [RelayCommand]
    private void Insert() => _tab.Insert(HasKeys ? $"{{{Name}[key]}}" : $"{{{Name}}}");
}

/// <summary>A language of a value's table. English cannot be removed.</summary>
public sealed partial class LanguageColumn(ValueItem item, string code)
{
    public string Code { get; } = code;

    public bool CanRemove => Code != Languages.Fallback;

    public double Width => item.Tab.CellWidth;

    [RelayCommand]
    private void Remove() => item.RemoveLanguage(Code);
}

/// <summary>A row of a value's table: the single value, an item, the pattern or the fallback, with a cell per language.</summary>
public sealed partial class ValueRow : ObservableObject
{
    private readonly ValueItem _item;

    internal ValueRow(ValueItem item, RowKind kind, string? key, IEnumerable<string> languages)
    {
        _item = item;
        Kind = kind;
        Key = key;
        EditKey = key ?? "";
        Cells = [.. languages.Select(l => new ValueCell(this, l))];
    }

    public RowKind Kind { get; }

    public string? Key { get; }

    public bool IsItem => Kind == RowKind.Item;

    /// <summary>"fallback" or "pattern": rows of a set without a key of their own.</summary>
    public string? Label => Kind switch
    {
        RowKind.Fallback => Strings.Get("RowFallback"),
        RowKind.Pattern => Strings.Get("RowPattern"),
        _ => null,
    };

    public bool HasLabel => Label is not null;

    [ObservableProperty]
    public partial string EditKey { get; set; }

    public IReadOnlyList<ValueCell> Cells { get; }

    public ValueItem Item => _item;

    internal IReadOnlyDictionary<string, string> Loaded { get; private set; } = new Dictionary<string, string>();

    [ObservableProperty]
    public partial string? Preview { get; set; }

    [ObservableProperty]
    public partial IBrush? Swatch { get; set; }

    [ObservableProperty]
    public partial Bitmap? Thumbnail { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; set; }

    public bool HasError => Error is not null;

    internal bool Has(string language) => Loaded.GetValueOrDefault(language, "").Length > 0;

    internal bool IsPending => Cells.Any(c => c.IsPending) || (IsItem && EditKey.Trim() != Key);

    internal void Load(Dictionary<string, string> cells)
    {
        Loaded = cells;
        var available = cells.Where(kv => kv.Value.Length > 0).Select(kv => kv.Key).ToList();
        foreach (var cell in Cells)
        {
            // An empty cell shows, greyed, the value used instead.
            var used = Languages.Pick(available, cell.Language);
            cell.Load(cells.GetValueOrDefault(cell.Language),
                used == cell.Language || !cells.TryGetValue(used, out var fallback) ? null : $"{used}: {fallback}");
        }
        EditKey = Key ?? "";
    }

    internal void SetPreview(string? preview, IBrush? swatch, Bitmap? thumbnail, string? error)
    {
        Preview = preview;
        Swatch = swatch;
        var old = Thumbnail;
        Thumbnail = thumbnail;
        if (!ReferenceEquals(old, thumbnail))
            old?.Dispose();
        Error = error;
    }

    internal void CommitPending()
    {
        if (Cells.FirstOrDefault(c => c.IsPending) is { } cell)
        {
            var cells = new Dictionary<string, string>(Loaded, StringComparer.Ordinal) { [cell.Language] = cell.EditText };
            _item.SetCells(this, cells);
        }
        else if (IsItem && EditKey.Trim() is { Length: > 0 } key && key != Key)
        {
            _item.RenameKey(Key!, key);
        }
    }

    [RelayCommand]
    private void Remove() => _item.RemoveKey(Key!);
}

/// <summary>The value of a row in one language, applied by <see cref="ValueRow.CommitPending"/>.</summary>
public sealed partial class ValueCell(ValueRow row, string language) : ObservableObject
{
    private string _value = "";

    public string Language { get; } = language;

    public double Width => row.Item.Tab.CellWidth;

    [ObservableProperty]
    public partial string EditText { get; set; } = "";

    /// <summary>For an empty cell: the language and value used instead.</summary>
    [ObservableProperty]
    public partial string? Placeholder { get; set; }

    internal bool IsPending => EditText != _value;

    internal void Load(string? value, string? placeholder)
    {
        _value = value ?? "";
        EditText = _value;
        Placeholder = placeholder;
    }
}
