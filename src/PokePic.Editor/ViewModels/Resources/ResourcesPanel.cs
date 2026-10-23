using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokePic.Core.Editing;
using PokePic.Core.Model;
using PokePic.Editor.Localization;
using PokePic.Editor.ViewModels.Properties;

namespace PokePic.Editor.ViewModels.Resources;

/// <summary>
/// The template's styles (the Styles panel): name and element type here, the properties in the Properties panel (✎),
/// with what each style sets and how many elements use it. Typed names are applied on Enter or when the focus moves
/// (<see cref="CommitPending"/>), as one undoable edit of the JSON.
/// </summary>
public sealed partial class ResourcesPanel(MainViewModel main) : ObservableObject
{
    public ObservableCollection<ResourceItem> Styles { get; } = [];

    public void Load(TemplateDocument? doc)
    {
        var entries = (doc?.Root["styles"] as JsonObject)?.Select(kv => (kv.Key, kv.Value)).ToList() ?? [];
        if (!Styles.Select(i => i.Name).SequenceEqual(entries.Select(e => e.Key)))
        {
            Styles.Clear();
            foreach (var (name, _) in entries)
                Styles.Add(new ResourceItem(this, name));
        }
        for (int i = 0; i < entries.Count; i++)
            Styles[i].Load(entries[i].Value);
        DescribeStyles(doc);
    }

    /// <summary>What each style sets, and how many elements use it.</summary>
    private void DescribeStyles(TemplateDocument? doc)
    {
        var uses = new Dictionary<string, int>(StringComparer.Ordinal);
        void Count(JsonNode? elements)
        {
            foreach (var element in (elements as JsonArray ?? []).OfType<JsonObject>())
            {
                if (element["style"] is JsonValue s)
                    uses[ValueField.ToText(s)] = uses.GetValueOrDefault(ValueField.ToText(s)) + 1;
                Count(element["children"]);
            }
        }
        Count(doc?.Root["elements"]);

        foreach (var item in Styles)
        {
            var properties = ((doc?.Root["styles"] as JsonObject)?[item.Name] as JsonObject)?.Select(kv => kv.Key).Where(k => k != "type").ToList() ?? [];
            item.SetInfo(
                properties.Count == 0 ? Strings.Get("StyleEmpty") : string.Join(", ", properties),
                Strings.Format("StyleUses", uses.GetValueOrDefault(item.Name)));
        }
    }

    internal void EditStyle(ResourceItem item) => main.EditStyle(item.Name);

    public void CommitPending()
    {
        foreach (var item in Styles.ToList())
            item.CommitPending();
    }

    /// <summary>A new style opens in the properties panel, to choose its type and set its properties.</summary>
    [RelayCommand]
    private void AddStyle()
    {
        var name = "";
        Edit((_, entries) =>
        {
            name = "style1";
            for (int i = 2; entries.ContainsKey(name); i++)
                name = $"style{i}";
            entries[name] = new JsonObject();
        });
        main.EditStyle(name);
    }

    /// <summary>Renaming a style also renames it in the elements that use it.</summary>
    internal void Rename(string name, string newName)
    {
        bool renamed = false;
        Edit((doc, entries) =>
        {
            if (newName.Length == 0 || entries.ContainsKey(newName) || !entries.ContainsKey(name))
                return;
            var list = entries.ToList();
            entries.Clear();
            foreach (var (key, value) in list)
                entries[key == name ? newName : key] = value?.DeepClone();
            MainViewModel.RenameStyleReferences(doc.Root["elements"], name, newName);
            renamed = true;
        });
        if (renamed)
            main.OnStyleRenamed(name, newName);
    }

    /// <summary>The type of a style; the other properties stay.</summary>
    internal void SetType(ResourceItem item, string type) => Edit((_, entries) =>
    {
        if (entries[item.Name] is JsonObject style)
            TemplateDocument.SetOrRemove(style, "type", type.Length == 0 ? null : JsonValue.Create(type));
    });

    internal void Remove(ResourceItem item) => Edit((_, entries) => entries.Remove(item.Name));

    private void Edit(Action<TemplateDocument, JsonObject> edit) => main.ApplyEdit(doc =>
    {
        if (doc.Root["styles"] is not JsonObject entries)
        {
            entries = [];
            doc.Root["styles"] = entries;
        }
        edit(doc, entries);
        if (entries.Count == 0)
            doc.Root.Remove("styles");
        return null;
    }, rebuildProperties: false);
}

/// <summary>
/// One style: its name (applied by <see cref="CommitPending"/>) and its element type (applied when picked).
/// </summary>
public sealed partial class ResourceItem(ResourcesPanel owner, string name) : ObservableObject
{
    private string _type = "";
    private bool _loading;

    public string Name { get; } = name;

    [ObservableProperty]
    public partial string EditName { get; set; } = name;

    /// <summary>The element type; "" = any element.</summary>
    [ObservableProperty]
    public partial string EditValue { get; set; } = "";

    /// <summary>What the style sets.</summary>
    [ObservableProperty]
    public partial string Info { get; set; } = "";

    /// <summary>How many elements use the style.</summary>
    [ObservableProperty]
    public partial string? Detail { get; set; }

    internal void Load(JsonNode? node)
    {
        _type = ValueField.ToText((node as JsonObject)?["type"]);
        _loading = true;
        EditName = Name;
        EditValue = _type;
        _loading = false;
    }

    /// <summary>Element types a style can be for; "" = any element.</summary>
    public static IReadOnlyList<StyleTypeChoice> StyleTypeChoices { get; } =
        [new("", Strings.Get("StyleAnyType")), .. ElementProperties.Types.Select(t => new StyleTypeChoice(t, t))];

    public StyleTypeChoice? SelectedStyleType
    {
        get => StyleTypeChoices.FirstOrDefault(c => c.Code == EditValue);
        set
        {
            if (value is not null)
                EditValue = value.Code;
        }
    }

    /// <summary>The type is picked from a list: it applies at once.</summary>
    partial void OnEditValueChanged(string value)
    {
        OnPropertyChanged(nameof(SelectedStyleType));
        if (!_loading && value != _type)
        {
            _type = value;
            owner.SetType(this, value);
        }
    }

    [RelayCommand]
    private void Edit() => owner.EditStyle(this);

    internal void SetInfo(string info, string? detail)
    {
        Info = info;
        Detail = detail;
    }

    public void CommitPending()
    {
        var name = EditName.Trim();
        if (name != Name && name.Length > 0)
            owner.Rename(Name, name);
    }

    [RelayCommand]
    private void Remove() => owner.Remove(this);
}

public sealed record StyleTypeChoice(string Code, string Label)
{
    public override string ToString() => Label;
}
