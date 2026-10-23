using System.Text.Json;
using PokePic.Core.Model;

namespace PokePic.Core.Json;

/// <summary>
/// Collects issues while reading JSON. Parsing continues after an error to report as many issues as possible.
/// </summary>
internal sealed class IssueCollector
{
    private readonly List<TemplateIssue> _issues = [];

    public IReadOnlyList<TemplateIssue> Issues => _issues;

    public bool HasErrors => _issues.Count > 0;

    public void Error(string path, string message) => _issues.Add(new TemplateIssue(path, message));
}

/// <summary>
/// Strict reader for a JSON object: every property must be consumed, unknown ones are reported.
/// </summary>
internal sealed class ObjectReader
{
    private readonly IssueCollector _issues;
    private readonly Dictionary<string, JsonElement> _properties = new(StringComparer.Ordinal);
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    public ObjectReader(IssueCollector issues, string path, IEnumerable<KeyValuePair<string, JsonElement>> properties)
    {
        _issues = issues;
        Path = path;
        foreach (var (name, value) in properties)
        {
            _properties[name] = value;
        }
    }

    public string Path { get; }

    public IssueCollector Issues => _issues;

    public static ObjectReader? From(IssueCollector issues, string path, JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            issues.Error(path, $"Expected an object, found {Describe(element)}.");
            return null;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var properties = new List<KeyValuePair<string, JsonElement>>();
        foreach (var p in element.EnumerateObject())
        {
            if (!seen.Add(p.Name))
                issues.Error(JsonPath.Property(path, p.Name), $"Duplicate property \"{p.Name}\".");
            properties.Add(new(p.Name, p.Value));
        }
        return new ObjectReader(issues, path, properties);
    }

    public IEnumerable<KeyValuePair<string, JsonElement>> All => _properties;

    public string PathOf(string name) => JsonPath.Property(Path, name);

    public bool Has(string name) => _properties.ContainsKey(name);

    public void Ignore(string name) => _used.Add(name);

    public JsonElement? Get(string name, bool required = false)
    {
        _used.Add(name);
        if (_properties.TryGetValue(name, out var value) && value.ValueKind != JsonValueKind.Null)
            return value;
        if (required)
            _issues.Error(Path, $"Missing required property \"{name}\".");
        return null;
    }

    public void ReportUnknown()
    {
        foreach (var name in _properties.Keys)
        {
            if (!_used.Contains(name))
                _issues.Error(PathOf(name), $"Unknown property \"{name}\".");
        }
    }

    public string? String(string name, bool required = false)
    {
        if (Get(name, required) is not { } e)
            return null;
        if (e.ValueKind != JsonValueKind.String)
        {
            _issues.Error(PathOf(name), $"Expected a string, found {Describe(e)}.");
            return null;
        }
        return e.GetString();
    }

    public int? Int(string name, bool required = false, int min = int.MinValue, int max = int.MaxValue)
    {
        if (Get(name, required) is not { } e)
            return null;
        if (e.ValueKind != JsonValueKind.Number || !e.TryGetInt32(out int v))
        {
            _issues.Error(PathOf(name), $"Expected an integer, found {Describe(e)}.");
            return null;
        }
        if (v < min || v > max)
        {
            _issues.Error(PathOf(name), $"Value {v} is out of range [{min}, {max}].");
            return null;
        }
        return v;
    }

    public float? Float(string name, bool required = false, float min = float.MinValue, float max = float.MaxValue)
    {
        if (Get(name, required) is not { } e)
            return null;
        if (e.ValueKind != JsonValueKind.Number)
        {
            _issues.Error(PathOf(name), $"Expected a number, found {Describe(e)}.");
            return null;
        }
        float v = e.GetSingle();
        if (v < min || v > max || !float.IsFinite(v))
        {
            _issues.Error(PathOf(name), $"Value {v} is out of range [{min}, {max}].");
            return null;
        }
        return v;
    }

    public bool? Bool(string name)
    {
        if (Get(name) is not { } e)
            return null;
        if (e.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            _issues.Error(PathOf(name), $"Expected true or false, found {Describe(e)}.");
            return null;
        }
        return e.GetBoolean();
    }

    public T Enum<T>(string name, T defaultValue) where T : struct, Enum
    {
        if (Get(name) is not { } e)
            return defaultValue;
        if (e.ValueKind == JsonValueKind.String)
        {
            var text = e.GetString()!;
            foreach (var candidate in System.Enum.GetValues<T>())
            {
                if (string.Equals(candidate.ToString(), text, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
        }
        var allowed = string.Join(", ", System.Enum.GetNames<T>().Select(CamelCase));
        _issues.Error(PathOf(name), $"Invalid value {Describe(e)}. Allowed: {allowed}.");
        return defaultValue;
    }

    public Value<bool> BoolValue(string name, bool defaultValue)
    {
        if (Get(name) is not { } e)
            return new Literal<bool>(defaultValue);
        return ValueParsers.Bool(_issues, PathOf(name), e) ?? new Literal<bool>(defaultValue);
    }

    public Value<Rgba>? Color(string name, bool required = false) =>
        Get(name, required) is { } e ? ValueParsers.Color(_issues, PathOf(name), e) : null;

    public TextTemplate? Text(string name, bool required = false) =>
        Get(name, required) is { } e ? ValueParsers.Text(_issues, PathOf(name), e) : null;

    public RectValue? Rect(string name, bool required = false) =>
        Get(name, required) is { } e ? ValueParsers.Rect(_issues, PathOf(name), e) : null;

    public PointValue? Point(string name, bool required = false) =>
        Get(name, required) is { } e ? ValueParsers.Point(_issues, PathOf(name), e) : null;

    public static string Describe(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => $"\"{e.GetString()}\"",
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        JsonValueKind.Array => "an array",
        JsonValueKind.Object => "an object",
        _ => e.ValueKind.ToString(),
    };

    public static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
