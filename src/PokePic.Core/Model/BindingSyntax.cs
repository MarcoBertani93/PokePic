using System.Text;

namespace PokePic.Core.Model;

/// <summary>
/// Parser for the "{key}" / "{key:format}" / "{!key}" / "{set[key]}" binding syntax.
/// </summary>
public static class BindingSyntax
{
    /// <summary>
    /// Parses a text with interpolated bindings. Returns null and an error message if the syntax is invalid.
    /// </summary>
    public static TextTemplate? ParseText(string text, out string? error)
    {
        error = null;
        var segments = new List<TextSegment>();
        var literal = new StringBuilder();
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];
            if (c == '{')
            {
                if (i + 1 < text.Length && text[i + 1] == '{')
                {
                    literal.Append('{');
                    i += 2;
                    continue;
                }

                int close = text.IndexOf('}', i + 1);
                if (close < 0)
                {
                    error = $"Unclosed '{{' at position {i}.";
                    return null;
                }

                var binding = ParseBindingBody(text[(i + 1)..close], out error);
                if (binding is null)
                    return null;

                if (literal.Length > 0)
                {
                    segments.Add(new LiteralSegment(literal.ToString()));
                    literal.Clear();
                }
                segments.Add(new BindingSegment(binding));
                i = close + 1;
            }
            else if (c == '}')
            {
                if (i + 1 < text.Length && text[i + 1] == '}')
                {
                    literal.Append('}');
                    i += 2;
                    continue;
                }
                error = $"Unexpected '}}' at position {i}. Use '}}}}' for a literal brace.";
                return null;
            }
            else
            {
                literal.Append(c);
                i++;
            }
        }

        if (literal.Length > 0 || segments.Count == 0)
            segments.Add(new LiteralSegment(literal.ToString()));

        return new TextTemplate(segments);
    }

    /// <summary>
    /// Returns true if the whole string is a single binding, e.g. "{type1.color}".
    /// </summary>
    public static bool TryParseSingleBinding(string text, out Binding? binding, out string? error)
    {
        binding = null;
        error = null;
        var trimmed = text.Trim();
        if (trimmed.Length < 2 || trimmed[0] != '{' || trimmed[^1] != '}' || trimmed.StartsWith("{{", StringComparison.Ordinal))
            return false;

        var template = ParseText(trimmed, out error);
        if (template is null)
            return true; // looks like a binding but is malformed: caller reports the error

        binding = template.SingleBinding;
        if (binding is null)
        {
            error = "Expected a single binding such as \"{key}\".";
        }
        return true;
    }

    private static Binding? ParseBindingBody(string body, out string? error)
    {
        error = null;
        bool negate = false;
        var span = body.Trim();

        if (span.StartsWith('!'))
        {
            negate = true;
            span = span[1..].TrimStart();
        }

        string key = span;
        string? format = null;
        int colon = span.IndexOf(':');
        if (colon >= 0)
        {
            key = span[..colon].Trim();
            format = span[(colon + 1)..].Trim();
            if (format.Length == 0)
            {
                error = $"Empty format in binding \"{{{body}}}\".";
                return null;
            }
        }

        if (key.Length == 0)
        {
            error = "Empty binding \"{}\".";
            return null;
        }

        // "set[key]": the key's value picks an item of a template set.
        string? set = null;
        int open = key.IndexOf('[');
        if (open >= 0)
        {
            if (key[^1] != ']' || open == 0 || open == key.Length - 2)
            {
                error = $"Invalid binding \"{{{body}}}\": a set lookup is written \"{{set[key]}}\".";
                return null;
            }
            set = key[..open].Trim();
            key = key[(open + 1)..^1].Trim();
            if (!IsValidName(set, out error))
                return null;
        }

        return IsValidName(key, out error) ? new Binding(key, format, negate, set) : null;
    }

    private static bool IsValidName(string name, out string? error)
    {
        error = null;
        if (name.Length == 0)
        {
            error = "Empty binding key.";
            return false;
        }
        foreach (var ch in name)
        {
            if (!(char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-'))
            {
                error = $"Invalid character '{ch}' in binding key \"{name}\".";
                return false;
            }
        }
        return true;
    }
}
