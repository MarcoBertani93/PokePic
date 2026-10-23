using System.Text;
using System.Text.Json;

namespace PokePic.Core.Json;

/// <summary>
/// Maps JSON paths ("elements[2].rect") to their position in the source text, so issues can point to a line.
/// </summary>
internal sealed class JsonLineMap
{
    private readonly Dictionary<string, (int Line, int Column)> _positions;
    private readonly Dictionary<string, (int Start, int End)> _spans;
    private readonly byte[] _bytes;

    private JsonLineMap(Dictionary<string, (int Line, int Column)> positions, Dictionary<string, (int Start, int End)> spans, byte[] bytes)
    {
        _positions = positions;
        _spans = spans;
        _bytes = bytes;
    }

    public static JsonLineMap Build(string json, JsonReaderOptions options)
    {
        var positions = new Dictionary<string, (int, int)>(StringComparer.Ordinal);
        var spans = new Dictionary<string, (int Start, int End)>(StringComparer.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(json);
        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\n')
                lineStarts.Add(i + 1);
        }

        var reader = new Utf8JsonReader(bytes, options);
        var stack = new List<Frame>();
        string? pendingProperty = null;

        try
        {
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        pendingProperty = reader.GetString();
                        break;
                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        {
                            var path = NextValuePath(stack, pendingProperty);
                            Record(path, reader.TokenStartIndex);
                            stack.Add(new Frame(path, reader.TokenType == JsonTokenType.StartArray, (int)reader.TokenStartIndex));
                            break;
                        }
                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        {
                            var frame = stack[^1];
                            spans.TryAdd(frame.Path, (frame.Start, (int)reader.TokenStartIndex + 1));
                            stack.RemoveAt(stack.Count - 1);
                            break;
                        }
                    default:
                        {
                            var path = NextValuePath(stack, pendingProperty);
                            Record(path, reader.TokenStartIndex);
                            spans.TryAdd(path, ((int)reader.TokenStartIndex, (int)reader.BytesConsumed));
                            break;
                        }
                }
            }
        }
        catch (JsonException)
        {
            // Syntax errors are reported by the parser; a partial map is still useful.
        }

        return new JsonLineMap(positions, spans, bytes);

        void Record(string path, long offset)
        {
            int index = lineStarts.BinarySearch((int)offset);
            if (index < 0)
                index = ~index - 1;
            positions.TryAdd(path, (index + 1, (int)offset - lineStarts[index] + 1));
        }
    }

    /// <summary>
    /// Character range of the value at the path (e.g. a whole element object), or null if unknown.
    /// </summary>
    public (int Start, int Length)? FindSpan(string path)
    {
        if (!_spans.TryGetValue(path, out var span))
            return null;
        int start = Encoding.UTF8.GetCharCount(_bytes, 0, span.Start);
        int length = Encoding.UTF8.GetCharCount(_bytes, span.Start, span.End - span.Start);
        return (start, length);
    }

    /// <summary>
    /// Position of the path, or of its closest known ancestor.
    /// </summary>
    public (int Line, int Column)? Find(string path)
    {
        var current = path;
        while (true)
        {
            if (_positions.TryGetValue(current, out var pos))
                return pos;
            if (current.Length == 0)
                return null;
            current = JsonPath.Parent(current);
        }
    }

    public IReadOnlyList<TemplateIssue> Locate(IEnumerable<TemplateIssue> issues) =>
        [.. issues.Select(i =>
        {
            if (i.Line is not null)
                return i;
            var pos = Find(i.Path);
            return pos is null ? i : i with { Line = pos.Value.Line, Column = pos.Value.Column };
        })];

    private static string NextValuePath(List<Frame> stack, string? property)
    {
        if (stack.Count == 0)
            return "";
        var top = stack[^1];
        if (top.IsArray)
            return JsonPath.Index(top.Path, top.NextIndex++);
        return JsonPath.Property(top.Path, property ?? "");
    }

    private sealed class Frame(string path, bool isArray, int start)
    {
        public string Path { get; } = path;
        public bool IsArray { get; } = isArray;
        public int Start { get; } = start;
        public int NextIndex { get; set; }
    }
}

internal static class JsonPath
{
    public static string Property(string parent, string name) => parent.Length == 0 ? name : $"{parent}.{name}";

    public static string Index(string parent, int index) => $"{parent}[{index}]";

    public static string Parent(string path)
    {
        int cut = Math.Max(path.LastIndexOf('.'), path.LastIndexOf('['));
        return cut <= 0 ? "" : path[..cut];
    }
}
