using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PokePic.Core.Data;
using PokePic.Core.Editing;
using PokePic.Core.Model;
using PokePic.Editor.Localization;
using PokePic.Editor.ViewModels.Properties;
using PokePic.Editor.ViewModels.Values;

namespace PokePic.Editor.ViewModels;

public sealed partial class MainViewModel
{
    private bool _quietSelection;

    /// <summary>Selected element paths, in selection order: the last one is <see cref="SelectedLayer"/>.</summary>
    private IReadOnlyList<string> _selection = [];

    /// <summary>Layer tree in drawing order: the first row is drawn first (bottom layer).</summary>
    public ObservableCollection<LayerNode> Layers { get; } = [];

    /// <summary>The primary selected layer (the last one clicked). Setting it selects only that layer.</summary>
    [ObservableProperty]
    public partial LayerNode? SelectedLayer { get; set; }

    [ObservableProperty]
    public partial PropertiesPanel? Properties { get; set; }

    public IReadOnlyList<string> SelectedPaths => _selection;

    public IReadOnlyList<LayerNode> SelectedLayers => [.. _selection.Select(FindNode).OfType<LayerNode>()];

    public bool HasSelection => _selection.Count > 0;

    public bool IsMultiSelection => _selection.Count > 1;

    public bool SelectedIsGroup => HasSelection && SelectedLayers.All(n => n.IsGroup);

    /// <summary>Raised after the selection changed, so the layer tree can follow it.</summary>
    public event Action? SelectionChanged;

    partial void OnSelectedLayerChanged(LayerNode? value)
    {
        if (!_quietSelection)
            SetSelection(value is null ? [] : [value.Path]);
    }

    /// <summary>Selects a layer by path (null: the template), expanding its parents.</summary>
    public void SelectPath(string? path) => SetSelection(path is null ? [] : [path]);

    /// <summary>Ctrl+click: adds the layer to the selection, or removes it.</summary>
    public void ToggleSelection(string path) =>
        SetSelection(_selection.Contains(path) ? _selection.Where(p => p != path) : [.. _selection, path]);

    public void SetSelection(IEnumerable<string> paths)
    {
        if (_quietSelection)
            return;
        // Typed values belong to the fields of the current selection: apply them first.
        FlushPendingEdits();
        var list = paths.Distinct(StringComparer.Ordinal).Where(p => FindNode(p) is not null).ToList();
        bool wasEditingStyle = EditedStyle is not null;
        EditedStyle = null; // selecting elements (or the template) ends the editing of a style
        if (list.SequenceEqual(_selection) && !wasEditingStyle)
            return;

        _selection = list;
        foreach (var node in AllNodes().Where(n => list.Any(p => TemplateDocument.IsInside(p, n.Path))))
            node.IsExpanded = true;
        ApplySelectionState();
        RebuildProperties();
        UpdateOverlay();
        if (list.Count == 1)
            HighlightInJson(list[0]);
    }

    private void ApplySelectionState()
    {
        _quietSelection = true;
        SelectedLayer = _selection.Count > 0 ? FindNode(_selection[^1]) : null;
        _quietSelection = false;
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsMultiSelection));
        OnPropertyChanged(nameof(SelectedIsGroup));
        SelectionChanged?.Invoke();
    }

    private LayerNode? FindNode(string path) => AllNodes().FirstOrDefault(n => n.Path == path);

    private IEnumerable<LayerNode> AllNodes() => Layers.SelectMany(n => n.SelfAndDescendants());

    private void RebuildLayers(IReadOnlyList<string> select)
    {
        if (_document is null)
            return;

        var expanded = new HashSet<string>(AllNodes().Where(n => n.IsExpanded).Select(n => n.Path), StringComparer.Ordinal);
        foreach (var path in select)
        {
            // Keep the new selection visible.
            for (var p = TemplateDocument.Split(path).ParentPath; p is not null; p = TemplateDocument.Split(p).ParentPath)
                expanded.Add(p);
        }

        var nodes = LayerNode.Build(_document, null, expanded, SetLayerVisible, RenameLayer);
        // The tree reports the removed rows as a selection change: ignore it while rebuilding.
        _quietSelection = true;
        Layers.Clear();
        foreach (var node in nodes)
            Layers.Add(node);
        _quietSelection = false;
        _selection = [.. select.Where(p => FindNode(p) is not null)];
        ApplySelectionState();
        UpdateOverlay();
    }

    private void RebuildProperties()
    {
        if (_document is null)
        {
            Properties = null;
            return;
        }
        Properties = EditedStyle is { } style && PropertiesPanel.ForStyle(this, _document, style) is { } stylePanel ? stylePanel
            : _selection.Count > 0 ? PropertiesPanel.ForElements(this, _document, _selection)
            : PropertiesPanel.ForTemplate(this, _document);
    }

    /// <summary>The style shown in the properties panel (from the Resources panel), or null.</summary>
    public string? EditedStyle { get; private set; }

    /// <summary>Shows a style in the properties panel; selecting anything else ends it.</summary>
    public void EditStyle(string name)
    {
        FlushPendingEdits();
        _selection = [];
        ApplySelectionState();
        UpdateOverlay();
        EditedStyle = name;
        RebuildProperties();
        ShowProperties = true;
    }

    /// <summary>The key of the panel that should be shown now.</summary>
    private string CurrentPropertiesKey(TemplateDocument doc) =>
        EditedStyle is { } style ? PropertiesPanel.StyleKey(doc, style) : PropertiesPanel.KeyFor(_selection);

    /// <summary>A style was renamed: elements that use it follow, and so does the properties panel.</summary>
    internal static void RenameStyleReferences(JsonNode? elements, string oldName, string newName)
    {
        foreach (var element in (elements as JsonArray ?? []).OfType<JsonObject>())
        {
            if (element["style"] is JsonValue s && s.GetValueKind() == System.Text.Json.JsonValueKind.String && s.GetValue<string>() == oldName)
                element["style"] = newName;
            RenameStyleReferences(element["children"], oldName, newName);
        }
    }

    internal void OnStyleRenamed(string oldName, string newName)
    {
        if (EditedStyle == oldName)
            EditedStyle = newName;
    }

    [RelayCommand]
    private void ClearSelection() => SetSelection([]);

    /// <summary>Selected elements as nodes (identity survives the path changes of multi-element edits).</summary>
    private static List<JsonObject> SelectedNodes(TemplateDocument doc, IEnumerable<string> paths) =>
        [.. TemplateDocument.TopLevel(paths).OrderBy(p => p, TemplateDocument.PathComparer.Instance).Select(p => doc.GetElement(p)).OfType<JsonObject>()];

    [RelayCommand]
    private void AddLayer(string kind) => ApplyEdit(doc =>
    {
        var (parent, index) = InsertionPoint(doc);
        return doc.Insert(parent, index, NewElement(doc, kind));
    }, rebuildProperties: true);

    /// <summary>New and pasted layers go right above the selection, or into the selected group when it is open.</summary>
    private (string? Parent, int Index) InsertionPoint(TemplateDocument doc)
    {
        if (SelectedLayer is not { } selected)
            return (null, doc.ChildrenOf(null).Count);
        if (selected.IsGroup && selected.IsExpanded)
            return (selected.Path, doc.ChildrenOf(selected.Path).Count);
        var (parent, i) = TemplateDocument.Split(selected.Path);
        return (parent, i + 1);
    }

    // ----- Clipboard -----

    /// <summary>
    /// The selected layers as JSON for the clipboard: an array of elements with canvas coordinates (as if they were
    /// not in a group), so they paste at the same place in any group, or as text into the JSON.
    /// </summary>
    public string? CopySelection()
    {
        FlushPendingEdits();
        if (_document is null || !HasSelection)
            return null;
        var doc = _document.Clone();
        var copies = new JsonArray();
        foreach (var node in SelectedNodes(doc, _selection))
        {
            var moved = doc.Move(doc.PathOf(node)!, null, doc.ChildrenOf(null).Count);
            copies.Add(doc.GetElement(moved ?? throw new InvalidOperationException("Move failed."))!.DeepClone());
        }
        return copies.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    public string? CutSelection()
    {
        var text = CopySelection();
        if (text is not null)
            DeleteLayer();
        return text;
    }

    /// <summary>Pastes elements (an element or an array of elements, with canvas coordinates) and selects them.</summary>
    public bool Paste(string text)
    {
        FlushPendingEdits();
        if (_document is null)
            return false;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(text, documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (System.Text.Json.JsonException)
        {
            node = null;
        }
        List<JsonNode?> elements = node switch
        {
            JsonArray array => [.. array],
            JsonObject element => [element],
            _ => [],
        };
        if (elements.Count == 0 || elements.Any(e => e is not JsonObject { } o || o["type"] is null))
        {
            Status = Strings.Get("PasteNotElements");
            return false;
        }

        ApplyEditMany(doc =>
        {
            var (parent, index) = InsertionPoint(doc);
            var (ox, oy) = doc.OriginOf(parent);
            var pasted = new List<JsonObject>();
            foreach (var element in elements)
            {
                var clone = (JsonObject)element!.DeepClone();
                var path = doc.Insert(parent, index++, clone);
                if ((ox, oy) != (0, 0) && !doc.Translate(path, -ox, -oy))
                    Status = Strings.Get("BoundCoordinates");
                pasted.Add(doc.GetElement(path)!);
            }
            return [.. pasted.Select(p => doc.PathOf(p)!)];
        }, rebuildProperties: true);
        return true;
    }

    [RelayCommand]
    private void DeleteLayer()
    {
        if (!HasSelection)
            return;
        ApplyEditMany(doc =>
        {
            foreach (var node in SelectedNodes(doc, _selection))
                doc.Remove(doc.PathOf(node)!);
            return [];
        }, rebuildProperties: true);
    }

    [RelayCommand]
    private void DuplicateLayer()
    {
        if (!HasSelection)
            return;
        ApplyEditMany(doc =>
        {
            var copies = SelectedNodes(doc, _selection)
                .Select(node => doc.GetElement(doc.Duplicate(doc.PathOf(node)!))!)
                .ToList();
            return [.. copies.Select(c => doc.PathOf(c)!)];
        }, rebuildProperties: true);
    }

    /// <summary>Up in the tree: earlier in drawing order, i.e. further back.</summary>
    [RelayCommand]
    private void MoveLayerUp() => Reorder(-1);

    [RelayCommand]
    private void MoveLayerDown() => Reorder(+1);

    /// <summary>
    /// Moves every selected layer one step among its siblings; a block of adjacent selected layers moves together.
    /// </summary>
    private void Reorder(int delta)
    {
        if (!HasSelection)
            return;
        ApplyEditMany(doc =>
        {
            var nodes = SelectedNodes(doc, _selection);
            var ordered = delta < 0 ? nodes : Enumerable.Reverse(nodes).ToList();
            foreach (var node in ordered)
            {
                var (parent, index) = TemplateDocument.Split(doc.PathOf(node)!);
                var siblings = doc.ChildrenOf(parent);
                int target = index + delta;
                if (target < 0 || target >= siblings.Count || nodes.Contains(siblings[target]))
                    continue;
                doc.Move(doc.PathOf(node)!, parent, target);
            }
            return [.. nodes.Select(n => doc.PathOf(n)!)];
        }, rebuildProperties: false);
    }

    [RelayCommand]
    private void GroupLayer()
    {
        if (HasSelection)
            ApplyEdit(doc => doc.GroupMany(_selection, Strings.Get("NewGroupName")), rebuildProperties: true);
    }

    [RelayCommand]
    private void UngroupLayer()
    {
        if (!SelectedIsGroup)
            return;
        ApplyEditMany(doc =>
        {
            var children = new List<JsonObject>();
            foreach (var group in SelectedNodes(doc, _selection))
            {
                children.AddRange((group[TemplateDocument.ChildrenProperty] as JsonArray ?? []).OfType<JsonObject>());
                doc.Ungroup(doc.PathOf(group)!);
            }
            return [.. children.Select(c => doc.PathOf(c)).OfType<string>()];
        }, rebuildProperties: true);
    }

    [RelayCommand]
    private void BeginRename()
    {
        if (SelectedLayer is { } layer && !IsMultiSelection)
            layer.IsEditing = true;
    }

    /// <summary>
    /// Drop of a layer onto another in the tree. Above/below refer to the tree, which lists layers in drawing order.
    /// </summary>
    public void DropLayer(LayerNode source, LayerNode target, DropPosition position)
    {
        if (source == target || position == DropPosition.None || TemplateDocument.IsInside(target.Path, source.Path))
            return;

        ApplyEdit(doc =>
        {
            if (position == DropPosition.Into && target.IsGroup)
                return doc.Move(source.Path, target.Path, doc.ChildrenOf(target.Path).Count);

            var (targetParent, targetIndex) = TemplateDocument.Split(target.Path);
            var (sourceParent, sourceIndex) = TemplateDocument.Split(source.Path);

            // The tree lists layers in drawing order: above the target = drawn before it.
            int index = position == DropPosition.Above ? targetIndex : targetIndex + 1;
            if (sourceParent == targetParent && sourceIndex < index)
                index--;
            return doc.Move(source.Path, targetParent, index);
        }, rebuildProperties: false);
    }

    // Visibility and names keep the current selection (a row's checkbox is not a selection).
    private void SetLayerVisible(LayerNode node, bool visible) =>
        ApplyEdit(doc =>
        {
            doc.SetProperty(node.Path, "visible", visible ? null : JsonValue.Create(false));
            return null;
        }, rebuildProperties: false);

    private void RenameLayer(LayerNode node, string name) =>
        ApplyEdit(doc =>
        {
            doc.SetProperty(node.Path, "name", JsonValue.Create(name));
            return null;
        }, rebuildProperties: false);

    private JsonObject NewElement(TemplateDocument doc, string kind)
    {
        string? FirstValue(string section) => TemplateValueNames(doc, section).FirstOrDefault() is { } name ? $"{{{name}}}" : null;

        return kind switch
        {
            "group" => new JsonObject
            {
                ["name"] = Strings.Get("NewGroupName"),
                ["type"] = "group",
                ["position"] = new JsonArray(0, 0),
                ["children"] = new JsonArray(),
            },
            "text" => new JsonObject
            {
                ["name"] = Strings.Get("InsertText"),
                ["type"] = "text",
                ["rect"] = new JsonArray(8, 8, 64, 12),
                ["text"] = Strings.Get("InsertText"),
                ["font"] = FirstValue("font") ?? "",
                ["size"] = 8,
                ["color"] = "#000000",
            },
            "image" => new JsonObject
            {
                ["name"] = Strings.Get("InsertImage"),
                ["type"] = "image",
                ["rect"] = new JsonArray(8, 8, 32, 32),
                ["image"] = FirstValue("image") ?? (DataKeys(ValueKind.Image).FirstOrDefault() is { } key ? $"{{{key}}}" : ""),
            },
            "line" => new JsonObject
            {
                ["name"] = Strings.Get("InsertLine"),
                ["type"] = "line",
                ["from"] = new JsonArray(8, 8),
                ["to"] = new JsonArray(40, 8),
                ["color"] = "#000000",
            },
            _ => new JsonObject
            {
                ["name"] = Strings.Get("InsertShape"),
                ["type"] = "shape",
                ["rect"] = new JsonArray(8, 8, 32, 16),
                ["fill"] = "#FFFFFF",
                ["stroke"] = new JsonObject { ["color"] = "#000000", ["width"] = 1 },
            },
        };
    }

    // ----- IPropertyHost -----

    /// <summary>Installed font families, offered as "system:Family" after the template fonts.</summary>
    private readonly Lazy<IReadOnlyList<string>> _systemFonts = new(() =>
        [.. SkiaSharp.SKFontManager.Default.GetFontFamilies().Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)]);

    /// <summary>Names of the template's values of a section ("text", "color"…); sets only with <paramref name="sets"/>.</summary>
    internal static IEnumerable<string> TemplateValueNames(TemplateDocument? doc, string section, bool sets = false) =>
        ((doc?.Root["values"] as JsonObject)?[section] as JsonObject)?
            .Where(kv => (kv.Value is JsonObject o && (o.ContainsKey("items") || o.ContainsKey("pattern"))) == sets)
            .Select(kv => kv.Key) ?? [];

    private IEnumerable<string> DataKeys(params ValueKind[] kinds) => _data.Values.Where(kv => kinds.Contains(kv.Value.Kind)).Select(kv => kv.Key);

    IReadOnlyList<string> IPropertyHost.Suggestions(SuggestionSource source)
    {
        // Values of the template first, then those of the data pack.
        IEnumerable<string> Bindings(params ValueKind[] kinds) =>
            kinds.SelectMany(k => TemplateValueNames(_document, ValueEdits.SectionOf(k))).Concat(DataKeys(kinds)).Select(k => $"{{{k}}}");
        IEnumerable<string> Names(string property) => (_document?.Root[property] as JsonObject)?.Select(kv => kv.Key) ?? [];

        return source switch
        {
            SuggestionSource.NumberBindings => [.. Bindings(ValueKind.Int, ValueKind.Float)],
            SuggestionSource.BoolBindings => ["true", "false", .. Bindings(ValueKind.Bool), .. Bindings(ValueKind.Bool).Select(b => "{!" + b[1..])],
            SuggestionSource.ColorBindings => [.. Bindings(ValueKind.Color)],
            SuggestionSource.TextBindings => [.. Bindings(ValueKind.Text, ValueKind.Int, ValueKind.Float, ValueKind.Bool, ValueKind.Color)],
            SuggestionSource.Fonts => [.. Bindings(ValueKind.Font), .. _systemFonts.Value.Select(f => SystemFonts.Prefix + f)],
            SuggestionSource.Images => [.. Bindings(ValueKind.Image)],
            SuggestionSource.ImageSets => [.. TemplateValueNames(_document, "image", sets: true)],
            SuggestionSource.Styles => [.. Names("styles")],
            SuggestionSource.ShapeKinds => ["rect", "ellipse"],
            SuggestionSource.Fits => ["none", "stretch", "contain", "cover", "scale"],
            SuggestionSource.Anchors => ["topLeft", "top", "topRight", "left", "center", "right", "bottomLeft", "bottom", "bottomRight"],
            SuggestionSource.Flips => ["none", "horizontal", "vertical", "both"],
            SuggestionSource.Aligns => ["left", "center", "right"],
            SuggestionSource.VAligns => ["top", "middle", "bottom"],
            SuggestionSource.Modes => ["pixelPerfect", "highResolution"],
            SuggestionSource.Languages => [.. TextLanguages],
            _ => [],
        };
    }

    async Task<string?> IPropertyHost.AddAssetAsync(AssetKind kind)
    {
        if (TemplatePath is null)
        {
            await _dialogs.ShowErrorAsync(Strings.Get("SaveBeforeAssets"));
            return null;
        }

        var filter = kind == AssetKind.Font
            ? new Services.FileFilter(Strings.Get("FilterFont"), "*.ttf;*.otf")
            : new Services.FileFilter(Strings.Get("FilterImage"), "*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp");
        var file = await _dialogs.OpenFileAsync(Strings.Get(kind == AssetKind.Font ? "AddFont" : "AddImage"), filter);
        if (file is null)
            return null;

        string folder = Path.GetDirectoryName(TemplatePath)!;
        string relative;
        try
        {
            relative = AssetFiles.ImportIntoFolder(file, folder, kind == AssetKind.Font ? "fonts" : "images");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await _dialogs.ShowErrorAsync(ex.Message);
            return null;
        }

        string section = kind == AssetKind.Font ? "font" : "image";
        string name = Path.GetFileNameWithoutExtension(file);
        ApplyEdit(doc =>
        {
            name = ValueEdits.UniqueName(doc, name);
            ValueEdits.Section(doc, section)[name] = relative;
            return null;
        }, rebuildProperties: true);
        return name;
    }
}

internal static class AssetFiles
{
    /// <summary>
    /// Returns the path of <paramref name="file"/> relative to the template folder, copying it
    /// into <paramref name="subfolder"/> first when it lives outside the template folder.
    /// </summary>
    public static string ImportIntoFolder(string file, string templateFolder, string subfolder)
    {
        var full = Path.GetFullPath(file);
        var root = Path.GetFullPath(templateFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            var targetDir = Path.Combine(root, subfolder);
            Directory.CreateDirectory(targetDir);
            var target = Path.Combine(targetDir, Path.GetFileName(full));
            for (int i = 2; File.Exists(target) && !File.ReadAllBytes(target).AsSpan().SequenceEqual(File.ReadAllBytes(full)); i++)
                target = Path.Combine(targetDir, $"{Path.GetFileNameWithoutExtension(full)}{i}{Path.GetExtension(full)}");
            if (!File.Exists(target))
                File.Copy(full, target);
            full = target;
        }
        return Path.GetRelativePath(root, full).Replace(Path.DirectorySeparatorChar, '/');
    }
}
