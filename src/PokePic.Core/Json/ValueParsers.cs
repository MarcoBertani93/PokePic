using System.Collections.Immutable;
using System.Text.Json;
using PokePic.Core.Model;

namespace PokePic.Core.Json;

/// <summary>
/// Parsers for property values that can be either literals or bindings.
/// </summary>
internal static class ValueParsers
{
    public static Value<bool>? Bool(IssueCollector issues, string path, JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.True:
            case JsonValueKind.False:
                return new Literal<bool>(e.GetBoolean());
            case JsonValueKind.Object:
                return Lookup(issues, path, e) is { } lookup ? new LookedUp<bool>(lookup) : null;
            case JsonValueKind.String when SingleBinding(issues, path, e.GetString()!) is { } binding:
                return new Bound<bool>(binding);
            case JsonValueKind.String:
                return null;
            default:
                issues.Error(path, $"Expected true, false or a binding such as \"{{key}}\", found {ObjectReader.Describe(e)}.");
                return null;
        }
    }

    public static Value<float>? Float(IssueCollector issues, string path, JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Number:
                return new Literal<float>(e.GetSingle());
            case JsonValueKind.Object:
                return Lookup(issues, path, e) is { } lookup ? new LookedUp<float>(lookup) : null;
            case JsonValueKind.String when SingleBinding(issues, path, e.GetString()!) is { } binding:
                if (binding.Negate)
                {
                    issues.Error(path, "Negation '!' is only valid for boolean values.");
                    return null;
                }
                return new Bound<float>(binding);
            case JsonValueKind.String:
                return null;
            default:
                issues.Error(path, $"Expected a number or a binding, found {ObjectReader.Describe(e)}.");
                return null;
        }
    }

    public static Value<Rgba>? Color(IssueCollector issues, string path, JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
            return Lookup(issues, path, e) is { } lookup ? new LookedUp<Rgba>(lookup) : null;
        if (e.ValueKind != JsonValueKind.String)
        {
            issues.Error(path, $"Expected a color such as \"#RRGGBB\" or a binding, found {ObjectReader.Describe(e)}.");
            return null;
        }

        var text = e.GetString()!;
        if (BindingSyntax.TryParseSingleBinding(text, out var binding, out var error))
        {
            if (binding is null)
            {
                issues.Error(path, error!);
                return null;
            }
            if (binding.Negate || binding.Format is not null)
            {
                issues.Error(path, "Color bindings cannot have a format or a negation.");
                return null;
            }
            return new Bound<Rgba>(binding);
        }

        if (Rgba.TryParse(text, out var color))
            return new Literal<Rgba>(color.Value);

        issues.Error(path, $"Invalid color \"{text}\". Use \"#RGB\", \"#RGBA\", \"#RRGGBB\" or \"#RRGGBBAA\".");
        return null;
    }

    public static TextTemplate? Text(IssueCollector issues, string path, JsonElement e)
    {
        if (e.ValueKind != JsonValueKind.String)
        {
            issues.Error(path, $"Expected a string, found {ObjectReader.Describe(e)}.");
            return null;
        }
        var template = BindingSyntax.ParseText(e.GetString()!, out var error);
        if (template is null)
            issues.Error(path, error!);
        return template;
    }

    public static RectValue? Rect(IssueCollector issues, string path, JsonElement e)
    {
        var parts = NumberArray(issues, path, e, 4, "[x, y, width, height]");
        return parts is null ? null : new RectValue(parts[0], parts[1], parts[2], parts[3]);
    }

    public static PointValue? Point(IssueCollector issues, string path, JsonElement e)
    {
        var parts = NumberArray(issues, path, e, 2, "[x, y]");
        return parts is null ? null : new PointValue(parts[0], parts[1]);
    }

    public static Fill? Fill(IssueCollector issues, string path, JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String || IsLookup(e))
            return Color(issues, path, e) is { } c ? new SolidFill(c) : null;

        var obj = ObjectReader.From(issues, path, e);
        if (obj is null)
            return null;

        Fill? result = null;
        if (obj.Get("linear", required: true) is { } linear && ObjectReader.From(issues, obj.PathOf("linear"), linear) is { } lin)
        {
            var from = lin.Point("from", required: true);
            var to = lin.Point("to", required: true);
            var stops = Stops(issues, lin.PathOf("stops"), lin.Get("stops", required: true));
            lin.ReportUnknown();
            if (from is not null && to is not null && stops is not null)
                result = new LinearGradientFill(from, to, stops.Value);
        }
        obj.ReportUnknown();
        return result;
    }

    public static Stroke? Stroke(IssueCollector issues, string path, JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String || IsLookup(e))
            return Color(issues, path, e) is { } c ? new Stroke(c, 1) : null;

        var obj = ObjectReader.From(issues, path, e);
        if (obj is null)
            return null;
        var color = obj.Color("color", required: true);
        var width = obj.Float("width", min: 0) ?? 1;
        obj.ReportUnknown();
        return color is null ? null : new Stroke(color, width);
    }

    private static bool IsLookup(JsonElement e) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("set", out _);

    /// <summary>{ "set": "artwork", "key": "{species}" } or with candidate keys: ["{species}-{form}", "{species}"].</summary>
    public static SetLookup? Lookup(IssueCollector issues, string path, JsonElement e)
    {
        var obj = ObjectReader.From(issues, path, e);
        if (obj is null)
            return null;
        var set = obj.String("set", required: true);
        var keys = Keys(issues, obj.PathOf("key"), obj.Get("key", required: true));
        obj.ReportUnknown();
        return set is null || keys is null ? null : new SetLookup(set, keys.Value);
    }

    /// <summary>
    /// A value of a kind, as written in "values" and in the "image" and "font" of elements: a literal (number, true/false,
    /// "#RRGGBB"), an alias ("{key}", "{!key}", "{set[key]}"), a composed text or path ("{BASE}/{key}.png", "system:Arial"),
    /// or { "set": …, "key": … }.
    /// </summary>
    public static ValueExpr? Expr(IssueCollector issues, string path, JsonElement e, Data.ValueKind kind)
    {
        if (e.ValueKind == JsonValueKind.Object)
            return IsLookup(e) && Lookup(issues, path, e) is { } lookup ? new LookupExpr(lookup) : Invalid();

        if (TemplateValue.IsComposable(kind))
        {
            if (Text(issues, path, e) is not { } text)
                return null;
            if (kind != Data.ValueKind.Text && text.LiteralText is { Length: 0 })
            {
                issues.Error(path, "A file path cannot be empty.");
                return null;
            }
            if (text.SingleBinding is { Negate: true })
            {
                issues.Error(path, "Negation '!' is only valid for boolean values.");
                return null;
            }
            return new ComposedExpr(text);
        }

        if (e.ValueKind == JsonValueKind.String && BindingSyntax.TryParseSingleBinding(e.GetString()!, out var binding, out var error))
        {
            if (binding is null)
            {
                issues.Error(path, error!);
                return null;
            }
            if (binding.Format is not null || (binding.Negate && kind != Data.ValueKind.Bool))
            {
                issues.Error(path, kind == Data.ValueKind.Bool
                    ? "A true/false value cannot have a format."
                    : $"A {kind.ToString().ToLowerInvariant()} value is a literal or a reference to another one (\"{{key}}\"), without format or '!'.");
                return null;
            }
            return new AliasExpr(binding);
        }

        Data.CardValue? literal = kind switch
        {
            Data.ValueKind.Int when e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var i) => new Data.IntValue(i),
            Data.ValueKind.Float when e.ValueKind == JsonValueKind.Number => new Data.FloatValue(e.GetDouble()),
            Data.ValueKind.Bool when e.ValueKind is JsonValueKind.True or JsonValueKind.False => new Data.BoolValue(e.GetBoolean()),
            Data.ValueKind.Color when e.ValueKind == JsonValueKind.String && Rgba.TryParse(e.GetString(), out var c) => new Data.ColorValue(c.Value),
            _ => null,
        };
        return literal is null ? Invalid() : new LiteralExpr(literal);

        ValueExpr? Invalid()
        {
            var expected = kind switch
            {
                Data.ValueKind.Int => "an integer",
                Data.ValueKind.Float => "a number",
                Data.ValueKind.Bool => "true or false",
                Data.ValueKind.Color => "a color such as \"#RRGGBB\"",
                Data.ValueKind.Text => "a text",
                _ => "a file path",
            };
            issues.Error(path, $"Expected {expected}, a reference such as \"{{key}}\" or {{ \"set\": …, \"key\": … }}, found {ObjectReader.Describe(e)}.");
            return null;
        }
    }

    /// <summary>"{species}" or ["{species}-{form}", "{species}"]: candidates tried in order.</summary>
    private static ImmutableArray<TextTemplate>? Keys(IssueCollector issues, string path, JsonElement? e)
    {
        if (e is null)
            return null;
        if (e.Value.ValueKind == JsonValueKind.String)
            return Text(issues, path, e.Value) is { } single ? [single] : null;
        if (e.Value.ValueKind != JsonValueKind.Array || e.Value.GetArrayLength() == 0)
        {
            issues.Error(path, $"Expected a key or a non-empty array of candidate keys, found {ObjectReader.Describe(e.Value)}.");
            return null;
        }

        var keys = ImmutableArray.CreateBuilder<TextTemplate>();
        int i = 0;
        foreach (var item in e.Value.EnumerateArray())
        {
            if (Text(issues, JsonPath.Index(path, i++), item) is { } key)
                keys.Add(key);
            else
                return null;
        }
        return keys.ToImmutable();
    }

    private static ImmutableArray<GradientStop>? Stops(IssueCollector issues, string path, JsonElement? e)
    {
        if (e is null)
            return null;
        if (e.Value.ValueKind != JsonValueKind.Array || e.Value.GetArrayLength() < 2)
        {
            issues.Error(path, "Expected an array of at least two stops, e.g. [[0, \"#FF0000\"], [1, \"#0000FF\"]].");
            return null;
        }

        var stops = ImmutableArray.CreateBuilder<GradientStop>();
        int i = 0;
        bool ok = true;
        foreach (var stop in e.Value.EnumerateArray())
        {
            var stopPath = JsonPath.Index(path, i++);
            if (stop.ValueKind != JsonValueKind.Array || stop.GetArrayLength() != 2 || stop[0].ValueKind != JsonValueKind.Number)
            {
                issues.Error(stopPath, "Expected a stop as [offset, color], with offset between 0 and 1.");
                ok = false;
                continue;
            }
            float offset = stop[0].GetSingle();
            if (offset is < 0 or > 1)
            {
                issues.Error(stopPath, $"Stop offset {offset} must be between 0 and 1.");
                ok = false;
            }
            var color = Color(issues, JsonPath.Index(stopPath, 1), stop[1]);
            if (color is null)
                ok = false;
            else
                stops.Add(new GradientStop(offset, color));
        }

        for (int s = 1; s < stops.Count; s++)
        {
            if (stops[s].Offset < stops[s - 1].Offset)
            {
                issues.Error(path, "Stop offsets must be in increasing order.");
                ok = false;
                break;
            }
        }

        return ok ? stops.ToImmutable() : null;
    }

    private static Value<float>[]? NumberArray(IssueCollector issues, string path, JsonElement e, int count, string shape)
    {
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() != count)
        {
            issues.Error(path, $"Expected {shape}, found {ObjectReader.Describe(e)}.");
            return null;
        }

        var result = new Value<float>[count];
        bool ok = true;
        for (int i = 0; i < count; i++)
        {
            var v = Float(issues, JsonPath.Index(path, i), e[i]);
            if (v is null)
                ok = false;
            else
                result[i] = v;
        }
        return ok ? result : null;
    }

    /// <summary>
    /// Parses a string that must be a single binding. Reports an error and returns null otherwise.
    /// </summary>
    private static Binding? SingleBinding(IssueCollector issues, string path, string text)
    {
        if (BindingSyntax.TryParseSingleBinding(text, out var binding, out var error))
        {
            if (binding is null)
                issues.Error(path, error!);
            return binding;
        }
        issues.Error(path, $"Expected a binding such as \"{{key}}\", found \"{text}\".");
        return null;
    }
}
