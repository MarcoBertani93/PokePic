using System.Text.Json;
using System.Text.Json.Nodes;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;

namespace PokePic.Editor.ViewModels.Docking;

/// <summary>
/// Saves the dock layout as a small JSON tree of docks and panel ids, and rebuilds it on startup.
/// Floating windows are not restored: their panels go back to their default side.
/// </summary>
public static class LayoutStore
{
    /// <summary>2: one panel per kind of resource; 3: value sets; 4: one panel per kind of value, the data pack in the top bar.</summary>
    private const int Version = 4;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PokePic", "editor-layout.json");

    public static void Save(EditorDockFactory factory, string path)
    {
        if (factory.Root?.VisibleDockables?.FirstOrDefault() is not IDock main)
            return;

        var hidden = new JsonArray();
        foreach (var tool in factory.Root.HiddenDockables ?? [])
        {
            if (tool is EditorTool t)
                hidden.Add(new JsonObject { ["id"] = t.Id, ["owner"] = t.OriginalOwner?.Id });
        }

        var json = new JsonObject
        {
            ["version"] = Version,
            ["layout"] = Write(main),
            ["hidden"] = hidden,
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Rebuilds a saved layout, or returns null if the file is missing, unreadable or inconsistent.
    /// </summary>
    public static IRootDock? Load(EditorDockFactory factory, string path, out string? error)
    {
        error = null;
        if (!File.Exists(path))
            return null;

        try
        {
            var json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            // A layout of an older version has other panels: the default layout replaces it.
            if (json?["version"]?.GetValue<int>() is < Version)
                return null;
            if (json?["version"]?.GetValue<int>() != Version || json["layout"] is not JsonObject layout)
            {
                error = "Unsupported layout file.";
                return null;
            }

            var used = new HashSet<string>(StringComparer.Ordinal);
            if (Read(factory, layout, used) is not IDock main || !used.Contains(ToolIds.Canvas))
            {
                error = "The saved layout does not contain the card.";
                return null;
            }

            var root = factory.RootFor(main);
            foreach (var item in json["hidden"] as JsonArray ?? [])
            {
                var id = item?["id"]?.GetValue<string>();
                if (id is null || !factory.Tools.TryGetValue(id, out var tool) || !used.Add(id))
                    continue;
                root.HiddenDockables!.Add(tool);
                tool.OriginalOwner = factory.FindDockable(main, d => d.Id == item!["owner"]?.GetValue<string>()) as IDock;
            }

            // Panels missing from the file (e.g. added in a newer version) go to their default side.
            foreach (var tool in factory.Tools.Values.Where(t => !used.Contains(t.Id)))
                root.HiddenDockables!.Add(tool);
            return root;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }
    }

    private static JsonObject Write(IDockable dockable)
    {
        var node = new JsonObject
        {
            ["id"] = dockable.Id,
            ["proportion"] = double.IsNaN(dockable.Proportion) ? null : dockable.Proportion,
        };
        switch (dockable)
        {
            case IProportionalDockSplitter:
                node["kind"] = "splitter";
                break;
            case IToolDock tool:
                node["kind"] = "tools";
                node["alignment"] = tool.Alignment.ToString();
                node["active"] = tool.ActiveDockable?.Id;
                node["items"] = Ids(tool);
                break;
            case IDocumentDock documents:
                node["kind"] = "documents";
                node["items"] = Ids(documents);
                break;
            case IProportionalDock proportional:
                node["kind"] = "split";
                node["orientation"] = proportional.Orientation.ToString();
                node["children"] = new JsonArray([.. (proportional.VisibleDockables ?? []).Select(d => (JsonNode)Write(d))]);
                break;
        }
        return node;

        static JsonArray Ids(IDock dock) => new([.. (dock.VisibleDockables ?? []).Select(d => (JsonNode?)JsonValue.Create(d.Id))]);
    }

    private static IDockable? Read(EditorDockFactory factory, JsonObject node, HashSet<string> used)
    {
        string? id = node["id"]?.GetValue<string>();
        double proportion = node["proportion"]?.GetValue<double>() ?? double.NaN;

        switch (node["kind"]?.GetValue<string>())
        {
            case "splitter":
                return new ProportionalDockSplitter();

            case "tools":
                {
                    var tools = Items(node, used, i => factory.Tools.GetValueOrDefault(i));
                    if (tools.Count == 0)
                        return null;
                    var alignment = Enum.TryParse<Alignment>(node["alignment"]?.GetValue<string>(), out var a) ? a : Alignment.Left;
                    var dock = new ToolDock
                    {
                        Id = id ?? "",
                        Alignment = alignment,
                        Proportion = proportion,
                        VisibleDockables = factory.CreateList<IDockable>([.. tools]),
                    };
                    var active = node["active"]?.GetValue<string>();
                    dock.ActiveDockable = tools.FirstOrDefault(t => t.Id == active) ?? tools[0];
                    return dock;
                }

            case "documents":
                {
                    var items = Items(node, used, i => i == ToolIds.Canvas ? factory.Canvas : null);
                    if (items.Count == 0)
                        return null;
                    var dock = factory.DocumentDock();
                    dock.Proportion = proportion;
                    return dock;
                }

            case "split":
                {
                    var children = (node["children"] as JsonArray ?? [])
                        .OfType<JsonObject>()
                        .Select(c => Read(factory, c, used))
                        .OfType<IDockable>()
                        .ToList();
                    // Drop splitters left without a neighbour.
                    while (children.Count > 0 && children[0] is ProportionalDockSplitter)
                        children.RemoveAt(0);
                    while (children.Count > 0 && children[^1] is ProportionalDockSplitter)
                        children.RemoveAt(children.Count - 1);
                    if (children.Count == 0)
                        return null;
                    var orientation = Enum.TryParse<Orientation>(node["orientation"]?.GetValue<string>(), out var o) ? o : Orientation.Horizontal;
                    return new ProportionalDock
                    {
                        Id = id ?? "",
                        Orientation = orientation,
                        Proportion = proportion,
                        VisibleDockables = factory.CreateList([.. children]),
                    };
                }

            default:
                return null;
        }
    }

    private static List<IDockable> Items(JsonObject node, HashSet<string> used, Func<string, IDockable?> resolve) =>
        [.. (node["items"] as JsonArray ?? [])
            .Select(i => i?.GetValue<string>())
            .OfType<string>()
            .Where(used.Add)
            .Select(resolve)
            .OfType<IDockable>()];
}
