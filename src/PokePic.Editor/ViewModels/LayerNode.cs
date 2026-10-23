using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using PokePic.Core.Editing;
using PokePic.Editor.ViewModels.Properties;

namespace PokePic.Editor.ViewModels;

public enum DropPosition
{
    None,
    Above,
    Below,
    Into,
}

/// <summary>
/// A layer in the tree. Rows are in drawing order: the first row is drawn first (bottom layer),
/// so a new layer appears at the end of the list and on top of the image.
/// </summary>
public sealed partial class LayerNode : ObservableObject
{
    private readonly Action<LayerNode, bool> _setVisible;
    private readonly Action<LayerNode, string> _rename;
    private bool _loading;

    public LayerNode(string path, JsonObject element, Action<LayerNode, bool> setVisible, Action<LayerNode, string> rename)
    {
        _setVisible = setVisible;
        _rename = rename;
        Path = path;
        Type = ValueField.ToText(element["type"]);
        _loading = true;
        Name = element["name"] is { } n ? ValueField.ToText(n) : Type;
        var visible = element["visible"];
        IsVisibleBound = visible is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.String;
        IsVisible = visible is null || (visible is JsonValue b && b.GetValueKind() == System.Text.Json.JsonValueKind.True) || IsVisibleBound;
        VisibleBinding = IsVisibleBound ? ValueField.ToText(visible) : null;
        _loading = false;
    }

    public string Path { get; }

    public string Type { get; }

    public bool IsGroup => Type == "group";

    /// <summary>One-letter badge for the type.</summary>
    public string Badge => Type switch
    {
        "group" => "G",
        "shape" => "S",
        "line" => "L",
        "image" => "I",
        "text" => "T",
        _ => "?",
    };

    public ObservableCollection<LayerNode> Children { get; } = [];

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    /// <summary>Literal visibility; for bound visibility the checkbox is disabled.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    public bool IsVisibleBound { get; }

    public bool CanToggleVisible => !IsVisibleBound;

    public string? VisibleBinding { get; }

    [ObservableProperty]
    public partial DropPosition DropHint { get; set; }

    public bool DropAbove => DropHint == DropPosition.Above;

    public bool DropBelow => DropHint == DropPosition.Below;

    public bool DropInto => DropHint == DropPosition.Into;

    partial void OnDropHintChanged(DropPosition value)
    {
        OnPropertyChanged(nameof(DropAbove));
        OnPropertyChanged(nameof(DropBelow));
        OnPropertyChanged(nameof(DropInto));
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (!_loading && !IsVisibleBound)
            _setVisible(this, value);
    }

    /// <summary>Called by the inline editor when renaming ends.</summary>
    public void CommitRename(string name)
    {
        IsEditing = false;
        name = name.Trim();
        if (name.Length > 0 && name != Name)
            _rename(this, name);
    }

    public IEnumerable<LayerNode> SelfAndDescendants()
    {
        yield return this;
        foreach (var child in Children)
        {
            foreach (var d in child.SelfAndDescendants())
                yield return d;
        }
    }

    /// <summary>
    /// Builds the tree for an element array, in drawing order.
    /// </summary>
    public static List<LayerNode> Build(TemplateDocument doc, string? parentPath, ISet<string> expanded, Action<LayerNode, bool> setVisible, Action<LayerNode, string> rename)
    {
        var result = new List<LayerNode>();
        if ((parentPath is null ? doc.Root[TemplateDocument.ElementsProperty] : doc.GetElement(parentPath)?[TemplateDocument.ChildrenProperty]) is not JsonArray array)
            return result;

        for (int i = 0; i < array.Count; i++)
        {
            if (array[i] is not JsonObject element)
                continue;
            var path = TemplateDocument.ElementPath(parentPath, i);
            var node = new LayerNode(path, element, setVisible, rename) { IsExpanded = expanded.Contains(path) };
            if (node.IsGroup)
            {
                foreach (var child in Build(doc, path, expanded, setVisible, rename))
                    node.Children.Add(child);
            }
            result.Add(node);
        }
        return result;
    }
}
