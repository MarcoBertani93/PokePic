using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PokePic.Core.Editing;

/// <summary>
/// Writes templates in a compact, diff-friendly layout: one element per line when it fits,
/// nested containers only where needed. Comments are not preserved.
/// </summary>
public static class TemplateJsonFormatter
{
    private const int MaxLineLength = 180;
    private const string Indent = "  ";

    private static readonly JsonSerializerOptions ScalarOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Format(JsonNode root)
    {
        var sb = new StringBuilder();
        Write(sb, root, 0, forceMultiline: true);
        sb.Append('\n');
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, JsonNode? node, int depth, bool forceMultiline = false)
    {
        if (!forceMultiline && node is JsonObject or JsonArray && !HasElementList(node))
        {
            var inline = Inline(node);
            if (depth * Indent.Length + inline.Length <= MaxLineLength)
            {
                sb.Append(inline);
                return;
            }
        }

        switch (node)
        {
            case JsonObject obj:
                if (obj.Count == 0)
                {
                    sb.Append("{}");
                    return;
                }
                sb.Append("{\n");
                int i = 0;
                foreach (var (key, value) in obj)
                {
                    sb.Append(Repeat(depth + 1)).Append(Scalar(JsonValue.Create(key))).Append(": ");
                    Write(sb, value, depth + 1);
                    sb.Append(++i < obj.Count ? ",\n" : "\n");
                }
                sb.Append(Repeat(depth)).Append('}');
                break;

            case JsonArray array:
                if (array.Count == 0)
                {
                    sb.Append("[]");
                    return;
                }
                sb.Append("[\n");
                for (int j = 0; j < array.Count; j++)
                {
                    sb.Append(Repeat(depth + 1));
                    Write(sb, array[j], depth + 1);
                    sb.Append(j + 1 < array.Count ? ",\n" : "\n");
                }
                sb.Append(Repeat(depth)).Append(']');
                break;

            default:
                sb.Append(Scalar(node));
                break;
        }
    }

    /// <summary>
    /// Element lists (and groups containing them) are always expanded, one element per line.
    /// </summary>
    private static bool HasElementList(JsonNode? node) =>
        node is JsonObject obj && (obj.ContainsKey(TemplateDocument.ElementsProperty) || obj.ContainsKey(TemplateDocument.ChildrenProperty));

    private static string Inline(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                return obj.Count == 0
                    ? "{}"
                    : "{ " + string.Join(", ", obj.Select(kv => $"{Scalar(JsonValue.Create(kv.Key))}: {Inline(kv.Value)}")) + " }";
            case JsonArray array:
                return "[" + string.Join(", ", array.Select(Inline)) + "]";
            default:
                return Scalar(node);
        }
    }

    private static string Scalar(JsonNode? node) => node is null ? "null" : node.ToJsonString(ScalarOptions);

    private static string Repeat(int depth) => string.Concat(Enumerable.Repeat(Indent, depth));
}
