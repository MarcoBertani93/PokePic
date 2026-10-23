using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PokePic.Core.Data;
using PokePic.Core.Editing;
using PokePic.Editor.ViewModels.Properties;

namespace PokePic.Editor.ViewModels.Values;

/// <summary>
/// Reads and writes the template's "values" in the JSON document: sections by kind, the four forms of a value, the
/// text of a cell.
/// </summary>
internal static class ValueEdits
{
    public static string SectionOf(ValueKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>The section's object, created if missing.</summary>
    public static JsonObject Section(TemplateDocument doc, string section)
    {
        if (doc.Root["values"] is not JsonObject values)
        {
            values = [];
            // "values" goes after "canvas", like in the samples.
            var list = doc.Root.ToList();
            int at = list.FindIndex(kv => kv.Key == "canvas") + 1;
            doc.Root.Clear();
            for (int i = 0; i < list.Count; i++)
            {
                if (i == at)
                    doc.Root["values"] = values;
                doc.Root[list[i].Key] = list[i].Value?.DeepClone();
            }
            if (at >= list.Count)
                doc.Root["values"] = values;
            values = (JsonObject)doc.Root["values"]!;
        }
        if (values[section] is not JsonObject entries)
        {
            entries = [];
            values[section] = entries;
        }
        return entries;
    }

    /// <summary>Removes empty sections, and "values" if nothing is left.</summary>
    public static void Tidy(TemplateDocument doc)
    {
        if (doc.Root["values"] is not JsonObject values)
            return;
        foreach (var (section, entries) in values.ToList())
        {
            if (entries is JsonObject { Count: 0 })
                values.Remove(section);
        }
        if (values.Count == 0)
            doc.Root.Remove("values");
    }

    /// <summary>A name not used by any value of the template.</summary>
    public static string UniqueName(TemplateDocument doc, string name)
    {
        var used = ((doc.Root["values"] as JsonObject) ?? []).SelectMany(s => (s.Value as JsonObject)?.Select(kv => kv.Key) ?? []).ToHashSet(StringComparer.Ordinal);
        var unique = name;
        for (int i = 2; used.Contains(unique); i++)
            unique = $"{name}{i}";
        return unique;
    }

    /// <summary>Renames an entry keeping the order.</summary>
    public static bool Rename(JsonObject entries, string name, string newName)
    {
        if (newName.Length == 0 || entries.ContainsKey(newName) || !entries.ContainsKey(name))
            return false;
        var list = entries.ToList();
        entries.Clear();
        foreach (var (key, value) in list)
            entries[key == name ? newName : key] = value?.DeepClone();
        return true;
    }

    /// <summary>
    /// What a cell shows: numbers and true/false as written, texts as they are, { "set": …, "key": … } as compact JSON.
    /// </summary>
    public static string CellText(JsonNode? node) => node switch
    {
        JsonObject o => o.ToJsonString(),
        null => "",
        _ => ValueField.ToText(node),
    };

    /// <summary>
    /// The JSON of a typed cell: a number in "int" and "float", true/false in "bool", an object for
    /// { "set": …, "key": … }; anything else is a string (a literal, a reference "{key}", a composition).
    /// </summary>
    public static JsonNode CellNode(string section, string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("{\"", StringComparison.Ordinal))
        {
            try
            {
                if (JsonNode.Parse(trimmed) is JsonObject lookup)
                    return lookup;
            }
            catch (JsonException)
            {
                // not JSON: a text
            }
        }
        return section switch
        {
            "int" when long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) => JsonValue.Create(i),
            "float" when double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) => TemplateDocument.NumberNode(f),
            "bool" when trimmed is "true" or "false" => JsonValue.Create(trimmed == "true"),
            "int" or "float" or "bool" or "color" => JsonValue.Create(trimmed),
            _ => JsonValue.Create(text),
        };
    }

    /// <summary>A body by language → its cells: a single value is the "en" cell.</summary>
    public static Dictionary<string, string> Cells(JsonNode? body) => body is JsonObject o && !o.ContainsKey("set")
        ? o.ToDictionary(kv => kv.Key, kv => CellText(kv.Value), StringComparer.Ordinal)
        : new Dictionary<string, string>(StringComparer.Ordinal) { [Languages.Fallback] = CellText(body) };

    /// <summary>
    /// The JSON of a row's cells, in the order of <paramref name="languages"/>: only "en" is a single value, more
    /// languages are { "en": …, "it": … }. Empty cells other than "en" are left out.
    /// </summary>
    public static JsonNode Body(string section, IReadOnlyList<string> languages, IReadOnlyDictionary<string, string> cells)
    {
        var filled = languages.Where(l => l == Languages.Fallback || cells.GetValueOrDefault(l, "").Length > 0).ToList();
        if (filled.Count == 1)
            return CellNode(section, cells.GetValueOrDefault(Languages.Fallback, ""));
        var obj = new JsonObject();
        foreach (var language in filled)
            obj[language] = CellNode(section, cells.GetValueOrDefault(language, ""));
        return obj;
    }

    /// <summary>The default value of a new entry of a section.</summary>
    public static JsonNode Default(string section) => section switch
    {
        "int" => JsonValue.Create(0),
        "float" => JsonValue.Create(1.5),
        "bool" => JsonValue.Create(false),
        "color" => JsonValue.Create("#000000"),
        _ => JsonValue.Create(""),
    };
}
