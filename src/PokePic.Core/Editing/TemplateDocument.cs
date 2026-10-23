using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PokePic.Core.Json;

namespace PokePic.Core.Editing;

/// <summary>
/// Mutable JSON view of a template, used by the visual editor. Elements are addressed by the same paths
/// the parser reports ("elements[2]", "elements[2].children[0]"). Edits never interpret bindings:
/// coordinates that are bindings are left untouched.
/// </summary>
public sealed class TemplateDocument
{
    public const string ElementsProperty = "elements";
    public const string ChildrenProperty = "children";

    private TemplateDocument(JsonObject root)
    {
        Root = root;
    }

    public JsonObject Root { get; }

    public static TemplateDocument? TryParse(string json, out string? error)
    {
        error = null;
        try
        {
            var node = JsonNode.Parse(json, documentOptions: TemplateParser.DocumentOptions);
            if (node is JsonObject obj)
                return new TemplateDocument(obj);
            error = "The template must be a JSON object.";
        }
        catch (JsonException ex)
        {
            error = ex.Message;
        }
        return null;
    }

    public TemplateDocument Clone() => new((JsonObject)Root.DeepClone());

    public string ToJson() => TemplateJsonFormatter.Format(Root);

    /// <summary>
    /// The element array of a group, or of the template when <paramref name="parentPath"/> is null.
    /// It is created if missing.
    /// </summary>
    public JsonArray ChildrenOf(string? parentPath)
    {
        JsonObject owner = parentPath is null ? Root : GetElement(parentPath) ?? throw new ArgumentException($"No element at \"{parentPath}\".", nameof(parentPath));
        string property = parentPath is null ? ElementsProperty : ChildrenProperty;
        if (owner[property] is JsonArray array)
            return array;
        array = [];
        owner[property] = array;
        return array;
    }

    public JsonObject? GetElement(string path)
    {
        JsonNode? current = Root;
        foreach (var (property, index) in ParsePath(path))
        {
            if (current is not JsonObject obj || obj[property] is not JsonArray array || index < 0 || index >= array.Count)
                return null;
            current = array[index];
        }
        return current as JsonObject;
    }

    public static string ElementPath(string? parentPath, int index) =>
        parentPath is null ? $"{ElementsProperty}[{index}]" : $"{parentPath}.{ChildrenProperty}[{index}]";

    /// <summary>
    /// Splits "elements[2].children[1]" into ("elements[2]", 1).
    /// </summary>
    public static (string? ParentPath, int Index) Split(string path)
    {
        var parts = ParsePath(path);
        if (parts.Count == 0)
            throw new ArgumentException($"Invalid element path \"{path}\".", nameof(path));
        int index = parts[^1].Index;
        int cut = path.LastIndexOf('.');
        string? parent = parts.Count == 1 ? null : path[..cut];
        return (parent, index);
    }

    public static bool IsInside(string path, string ancestorPath) =>
        path.StartsWith(ancestorPath + ".", StringComparison.Ordinal);

    public string Insert(string? parentPath, int index, JsonObject element)
    {
        var array = ChildrenOf(parentPath);
        index = Math.Clamp(index, 0, array.Count);
        array.Insert(index, element);
        return ElementPath(parentPath, index);
    }

    public void Remove(string path)
    {
        var (parent, index) = Split(path);
        ChildrenOf(parent).RemoveAt(index);
    }

    /// <summary>
    /// Moves an element to <paramref name="index"/> of another (or the same) parent, as if it had been removed first.
    /// Returns the new path, or null if the move is not possible (e.g. into itself).
    /// </summary>
    /// <param name="keepCanvasPosition">Compensates the coordinates for the different group offsets,
    /// so the element stays where it was on the canvas.</param>
    public string? Move(string path, string? newParentPath, int index, bool keepCanvasPosition = true)
    {
        if (newParentPath is not null && (newParentPath == path || IsInside(newParentPath, path)))
            return null;
        if (newParentPath is not null && GetElement(newParentPath)?["type"]?.GetValue<string>() != "group")
            return null;

        var element = GetElement(path) ?? throw new ArgumentException($"No element at \"{path}\".", nameof(path));
        var (oldParent, oldIndex) = Split(path);
        var (oldX, oldY) = OriginOf(oldParent);
        var (newX, newY) = OriginOf(newParentPath);

        // Paths of the destination may shift once the element is removed from an ancestor array.
        string? adjustedParent = newParentPath is null ? null : AdjustAfterRemoval(newParentPath, oldParent, oldIndex);

        ChildrenOf(oldParent).RemoveAt(oldIndex);
        var target = ChildrenOf(adjustedParent);
        index = Math.Clamp(index, 0, target.Count);
        target.Insert(index, element);
        if (keepCanvasPosition && (oldX, oldY) != (newX, newY))
            TranslateNode(element, oldX - newX, oldY - newY);
        return ElementPath(adjustedParent, index);
    }

    /// <summary>
    /// Canvas position of a group's coordinate space: the sum of its own and its ancestors' literal positions.
    /// </summary>
    public (float X, float Y) OriginOf(string? groupPath)
    {
        float x = 0, y = 0;
        for (var p = groupPath; p is not null; p = Split(p).ParentPath)
        {
            var position = GetElement(p)?["position"];
            x += Number(position, 0) ?? 0;
            y += Number(position, 1) ?? 0;
        }
        return (x, y);
    }

    public string Duplicate(string path)
    {
        var element = GetElement(path) ?? throw new ArgumentException($"No element at \"{path}\".", nameof(path));
        var copy = (JsonObject)element.DeepClone();
        if (copy["name"] is JsonValue name && name.TryGetValue(out string? text))
            copy["name"] = text + " copy";
        var (parent, index) = Split(path);
        return Insert(parent, index + 1, copy);
    }

    /// <summary>
    /// Wraps an element in a new group placed where the element was. Returns the group path.
    /// </summary>
    public string Group(string path, string groupName)
    {
        var element = GetElement(path) ?? throw new ArgumentException($"No element at \"{path}\".", nameof(path));
        var (parent, index) = Split(path);
        var array = ChildrenOf(parent);
        array.RemoveAt(index);
        var group = new JsonObject
        {
            ["name"] = groupName,
            ["type"] = "group",
            ["position"] = new JsonArray(0, 0),
            [ChildrenProperty] = new JsonArray(element),
        };
        array.Insert(index, group);
        return ElementPath(parent, index);
    }

    /// <summary>
    /// Wraps several elements in a new group placed where the frontmost of them was. Elements keep their
    /// canvas position and drawing order. Elements inside another selected element are moved with it.
    /// Returns the group path.
    /// </summary>
    public string GroupMany(IEnumerable<string> paths, string groupName)
    {
        var nodes = TopLevel(paths).OrderBy(p => p, PathComparer.Instance).Select(p => GetElement(p)!).ToList();
        if (nodes.Count == 0)
            throw new ArgumentException("No elements to group.", nameof(paths));

        var (parent, index) = Split(PathOf(nodes[^1])!);
        var group = new JsonObject
        {
            ["name"] = groupName,
            ["type"] = "group",
            ["position"] = new JsonArray(0, 0),
            [ChildrenProperty] = new JsonArray(),
        };
        Insert(parent, index + 1, group);
        foreach (var node in nodes)
        {
            var groupPath = PathOf(group)!;
            Move(PathOf(node)!, groupPath, ChildrenOf(groupPath).Count);
        }
        return PathOf(group)!;
    }

    /// <summary>
    /// Current path of an element node, found by identity (paths change as elements move).
    /// </summary>
    public string? PathOf(JsonObject element)
    {
        return Search(null);

        string? Search(string? parent)
        {
            if ((parent is null ? Root[ElementsProperty] : GetElement(parent)?[ChildrenProperty]) is not JsonArray array)
                return null;
            for (int i = 0; i < array.Count; i++)
            {
                var path = ElementPath(parent, i);
                if (ReferenceEquals(array[i], element))
                    return path;
                if (array[i] is JsonObject child && child[ChildrenProperty] is JsonArray && Search(path) is { } found)
                    return found;
            }
            return null;
        }
    }

    /// <summary>
    /// The paths that are not inside another of the given paths (moving or deleting a group covers its children).
    /// </summary>
    public static IReadOnlyList<string> TopLevel(IEnumerable<string> paths)
    {
        var list = paths.Distinct(StringComparer.Ordinal).ToList();
        return [.. list.Where(p => !list.Any(other => IsInside(p, other)))];
    }

    /// <summary>
    /// Orders element paths by drawing order ("elements[2]" before "elements[10]", parents before children).
    /// </summary>
    public sealed class PathComparer : IComparer<string>
    {
        public static PathComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            var a = Indices(x ?? "");
            var b = Indices(y ?? "");
            for (int i = 0; i < Math.Min(a.Count, b.Count); i++)
            {
                int c = a[i].CompareTo(b[i]);
                if (c != 0)
                    return c;
            }
            return a.Count.CompareTo(b.Count);
        }

        private static List<int> Indices(string path) => [.. ParsePath(path).Select(p => p.Index)];
    }

    /// <summary>
    /// Replaces a group with its children, keeping them where they were on the canvas.
    /// Returns the path of the first child, or null if the group was empty.
    /// </summary>
    public string? Ungroup(string path)
    {
        var group = GetElement(path) ?? throw new ArgumentException($"No element at \"{path}\".", nameof(path));
        if (group["type"]?.GetValue<string>() != "group")
            throw new ArgumentException($"\"{path}\" is not a group.", nameof(path));

        var (dx, dy) = (Number(group["position"], 0) ?? 0, Number(group["position"], 1) ?? 0);
        var children = group[ChildrenProperty] as JsonArray ?? [];
        var (parent, index) = Split(path);
        var array = ChildrenOf(parent);
        array.RemoveAt(index);

        var moved = children.ToList();
        children.Clear();
        for (int i = 0; i < moved.Count; i++)
        {
            var child = (JsonObject)moved[i]!;
            TranslateNode(child, dx, dy);
            array.Insert(index + i, child);
        }
        return moved.Count == 0 ? null : ElementPath(parent, index);
    }

    public JsonNode? GetProperty(string path, string name) => GetElement(path)?[name];

    /// <summary>
    /// Sets a property; null removes it (the style or the default applies).
    /// </summary>
    public void SetProperty(string path, string name, JsonNode? value)
    {
        var element = GetElement(path) ?? throw new ArgumentException($"No element at \"{path}\".", nameof(path));
        SetOrRemove(element, name, value);
    }

    public static void SetOrRemove(JsonObject obj, string name, JsonNode? value)
    {
        if (value is null)
            obj.Remove(name);
        else
            obj[name] = value;
    }

    /// <summary>
    /// Moves an element on the canvas by whole pixels. Bound coordinates are not changed.
    /// Returns false if no coordinate could be moved.
    /// </summary>
    public bool Translate(string path, float dx, float dy)
    {
        var element = GetElement(path) ?? throw new ArgumentException($"No element at \"{path}\".", nameof(path));
        return TranslateNode(element, dx, dy);
    }

    /// <summary>
    /// Sets the rect of an element (literal numbers only), keeping bound components.
    /// </summary>
    public void SetRect(string path, float x, float y, float width, float height)
    {
        var element = GetElement(path) ?? throw new ArgumentException($"No element at \"{path}\".", nameof(path));
        var rect = element["rect"] as JsonArray;
        float[] values = [x, y, Math.Max(0, width), Math.Max(0, height)];
        if (rect is null || rect.Count != 4)
        {
            element["rect"] = new JsonArray([.. values.Select(v => (JsonNode?)NumberNode(v))]);
            return;
        }
        for (int i = 0; i < 4; i++)
        {
            if (IsNumber(rect[i]))
                rect[i] = NumberNode(values[i]);
        }
    }

    public void SetPoint(string path, string property, float x, float y)
    {
        var element = GetElement(path) ?? throw new ArgumentException($"No element at \"{path}\".", nameof(path));
        if (element[property] is JsonArray { Count: 2 } point)
        {
            if (IsNumber(point[0]))
                point[0] = NumberNode(x);
            if (IsNumber(point[1]))
                point[1] = NumberNode(y);
        }
        else
        {
            element[property] = new JsonArray(NumberNode(x), NumberNode(y));
        }
    }

    /// <summary>
    /// A JSON number, written as an integer when it has no fractional part.
    /// </summary>
    public static JsonNode NumberNode(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < int.MaxValue
            ? JsonValue.Create((int)value)
            : JsonValue.Create(Math.Round(value, 4));

    public static bool IsNumber(JsonNode? node) => node is JsonValue v && v.GetValueKind() == JsonValueKind.Number;

    /// <summary>
    /// Numeric value of a node, whether it was parsed or created from an int or a double.
    /// </summary>
    public static double? ToDouble(JsonNode? node)
    {
        if (node is not JsonValue v || v.GetValueKind() != JsonValueKind.Number)
            return null;
        if (v.TryGetValue(out double d))
            return d;
        if (v.TryGetValue(out int i))
            return i;
        if (v.TryGetValue(out long l))
            return l;
        return double.Parse(v.ToJsonString(), CultureInfo.InvariantCulture);
    }

    public static float? Number(JsonNode? array, int index) =>
        array is JsonArray a && index < a.Count && ToDouble(a[index]) is { } d ? (float)d : null;

    private static bool TranslateNode(JsonObject element, float dx, float dy)
    {
        bool changed = false;
        switch (element["type"]?.GetValue<string>())
        {
            case "group":
                changed |= Shift(element["position"], dx, dy);
                break;
            case "line":
                changed |= Shift(element["from"], dx, dy);
                changed |= Shift(element["to"], dx, dy);
                break;
            default:
                changed |= Shift(element["rect"], dx, dy);
                break;
        }

        // Gradients are in the element's coordinate space: they move with it.
        if (element["fill"] is JsonObject fill && fill["linear"] is JsonObject linear)
        {
            Shift(linear["from"], dx, dy);
            Shift(linear["to"], dx, dy);
        }
        return changed;
    }

    private static bool Shift(JsonNode? node, float dx, float dy)
    {
        if (node is not JsonArray { Count: >= 2 } a)
            return false;
        bool changed = false;
        if (dx != 0 && IsNumber(a[0]))
        {
            a[0] = NumberNode(ToDouble(a[0])!.Value + dx);
            changed = true;
        }
        if (dy != 0 && IsNumber(a[1]))
        {
            a[1] = NumberNode(ToDouble(a[1])!.Value + dy);
            changed = true;
        }
        return changed || (dx == 0 && dy == 0);
    }

    private static string? AdjustAfterRemoval(string path, string? removedParent, int removedIndex)
    {
        // Only an ancestor array that contains the removed element shifts.
        string prefix = removedParent is null ? ElementsProperty : $"{removedParent}.{ChildrenProperty}";
        if (!path.StartsWith(prefix + "[", StringComparison.Ordinal))
            return path;
        int close = path.IndexOf(']', prefix.Length);
        int index = int.Parse(path.AsSpan(prefix.Length + 1, close - prefix.Length - 1), CultureInfo.InvariantCulture);
        if (index <= removedIndex)
            return path;
        return $"{prefix}[{index - 1}]{path[(close + 1)..]}";
    }

    private static List<(string Property, int Index)> ParsePath(string path)
    {
        var result = new List<(string, int)>();
        foreach (var segment in path.Split('.'))
        {
            int open = segment.IndexOf('[');
            if (open <= 0 || !segment.EndsWith(']') ||
                !int.TryParse(segment.AsSpan(open + 1, segment.Length - open - 2), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                return [];
            result.Add((segment[..open], index));
        }
        return result;
    }
}
