using System.Globalization;
using PokePic.Core.Assets;
using PokePic.Core.Data;
using PokePic.Core.Model;
using PokePic.Core.Resolution;

namespace PokePic.Core.Validation;

/// <summary>
/// Checks a parsed template against the data it will be rendered with and the assets it needs,
/// so that rendering never fails because of the template itself.
/// </summary>
public static class TemplateValidator
{
    public static IReadOnlyList<TemplateIssue> Validate(CardTemplate template, DataSchema schema, IAssetProvider assets)
    {
        var v = new Visitor(template, schema, assets);
        v.Run();
        return v.Issues;
    }

    private sealed class Visitor(CardTemplate template, DataSchema schema, IAssetProvider assets)
    {
        public List<TemplateIssue> Issues { get; } = [];

        public void Run()
        {
            CheckValues();

            if (template.Canvas.Background is { } background)
                CheckFill("canvas.background", background);

            foreach (var element in template.Elements)
                CheckElement(element);
        }

        // ----- Values -----

        private void CheckValues()
        {
            foreach (var (name, value) in template.Values)
            {
                if (schema.Values.ContainsKey(name))
                    Error(value.Path, $"\"{name}\" is already a data value: choose another name.");
            }
            CheckCycles();

            foreach (var (_, value) in template.Values)
            {
                switch (value.Definition)
                {
                    case PlainDefinition plain:
                        CheckBody(value.Path, plain.Body, value.Kind, inPattern: false);
                        break;
                    case ItemsDefinition items:
                        foreach (var (key, item) in items.Items)
                            CheckBody($"{value.Path}.items.{key}", item, value.Kind, inPattern: false);
                        if (items.Fallback is { } itemsFallback)
                            CheckBody($"{value.Path}.fallback", itemsFallback, value.Kind, inPattern: false);
                        break;
                    case PatternDefinition pattern:
                        CheckPattern($"{value.Path}.pattern", pattern.Pattern, value.Kind);
                        if (pattern.Fallback is { } patternFallback)
                            CheckBody($"{value.Path}.fallback", patternFallback, value.Kind, inPattern: false);
                        break;
                }
            }
        }

        private void CheckBody(string path, ValueBody body, ValueKind kind, bool inPattern)
        {
            switch (body)
            {
                case SingleBody single:
                    CheckExpr(path, single.Expr, kind, inPattern);
                    break;
                case ByLanguageBody byLanguage:
                    foreach (var (language, expr) in byLanguage.Exprs)
                        CheckExpr($"{path}.{language}", expr, kind, inPattern);
                    break;
            }
        }

        /// <summary>
        /// A pattern is a composed text or path with "{key}". For files, when the rest does not depend on the data, the folder
        /// of the files is checked (the files themselves depend on the key).
        /// </summary>
        private void CheckPattern(string path, ValueBody body, ValueKind kind)
        {
            foreach (var (exprPath, expr) in Exprs(path, body))
            {
                if (expr is not ComposedExpr { Text: var text } || !text.Bindings.Any(b => b.Key == TemplateValue.KeyName && b.Set is null))
                {
                    Error(exprPath, $"A pattern must contain \"{{{TemplateValue.KeyName}}}\", e.g. \"artwork/{{{TemplateValue.KeyName}}}.png\".");
                    continue;
                }
                CheckExpr(exprPath, expr, kind, inPattern: true);

                if (TemplateValue.IsFile(kind) && text.Bindings.All(b => b.Set is null && b.Key is TemplateValue.KeyName or TemplateValue.LanguageName))
                {
                    var sample = new BindingResolver(CardData.Empty, Languages.Fallback).Resolve(Replace(text, TemplateValue.KeyName, "x"), exprPath).Replace('\\', '/');
                    var folder = sample.LastIndexOf('/') is var slash and >= 0 ? sample[..slash] : "";
                    if (!Try(exprPath, () => assets.DirectoryExists(folder), assumed: true))
                        Error(exprPath, $"Folder not found: {assets.Describe(folder)}");
                }
            }
        }

        private static TextTemplate Replace(TextTemplate text, string key, string value) =>
            new([.. text.Segments.Select(s => s is BindingSegment { Binding: { Set: null } b } && b.Key == key ? new LiteralSegment(value) : s)]);

        private static IEnumerable<(string Path, ValueExpr Expr)> Exprs(string path, ValueBody body) => body switch
        {
            SingleBody s => [(path, s.Expr)],
            ByLanguageBody b => b.Exprs.Select(kv => ($"{path}.{kv.Key}", kv.Value)),
            _ => [],
        };

        /// <summary>
        /// Colors, numbers and true/false take a literal or a reference of their kind; texts, images and fonts are composed
        /// of texts and numbers, or are a lone reference of their kind.
        /// </summary>
        private void CheckExpr(string path, ValueExpr expr, ValueKind kind, bool inPattern)
        {
            switch (expr)
            {
                case AliasExpr alias:
                    if (BindingKind(path, alias.Binding, inPattern) is { } target)
                        ExpectKind(path, Name(alias.Binding), target, kind);
                    if (alias.Binding.Negate && kind != ValueKind.Bool)
                        Error(path, "Negation '!' is only valid for boolean values.");
                    break;
                case LookupExpr lookup:
                    if (LookupKind(path, lookup.Lookup, inPattern) is { } setKind)
                        ExpectKind(path, lookup.Lookup.Set, setKind, kind);
                    break;
                case ComposedExpr { Text: var text }:
                    if (kind != ValueKind.Text && text.SingleBinding is { Format: not null } formatted && BindingKind(path, formatted, inPattern, report: false) == kind)
                    {
                        Error(path, $"\"{formatted}\": a reference to {(kind == ValueKind.Image ? "an image" : "a font")} cannot have a format.");
                        break;
                    }
                    if (kind != ValueKind.Text && text.SingleBinding is { Format: null } single && BindingKind(path, single, inPattern, report: false) is { } singleKind)
                    {
                        if (singleKind == kind)
                            break; // "{frame}", "{species.sprite}": the value itself
                        if (singleKind is not (ValueKind.Text or ValueKind.Int or ValueKind.Float))
                        {
                            // A lone color or true/false is not a path: most likely the wrong value.
                            Error(path, $"Value \"{Name(single)}\" is {singleKind.ToString().ToLowerInvariant()}, expected {kind.ToString().ToLowerInvariant()} or a text.");
                            break;
                        }
                    }
                    CheckText(path, text, inPattern);
                    if (text.LiteralText is { } literal)
                    {
                        if (kind == ValueKind.Font && SystemFonts.IsSystem(literal))
                            CheckSystemFont(path, literal);
                        else if (TemplateValue.IsFile(kind))
                            CheckFile(path, literal);
                    }
                    break;
            }
        }

        private void ExpectKind(string path, string name, ValueKind actual, params ValueKind[] expected)
        {
            // A whole number fits where a decimal is expected.
            if (expected.Contains(actual) || (actual == ValueKind.Int && expected.Contains(ValueKind.Float)))
                return;
            Error(path, $"Value \"{name}\" is {actual.ToString().ToLowerInvariant()}, expected {string.Join(" or ", expected.Select(k => k.ToString().ToLowerInvariant()))}.");
        }

        private static string Name(Model.Binding binding) => binding.Set is null ? binding.Key : $"{binding.Set}[{binding.Key}]";

        /// <summary>
        /// The kind of a binding: "{language}", "{key}" in a pattern, a data key, a template value, or "{set[key]}" the
        /// set's kind. Null after reporting why not.
        /// </summary>
        private ValueKind? BindingKind(string path, Model.Binding binding, bool inPattern, bool report = true)
        {
            if (binding.Set is not null)
                return report ? LookupKind(path, SetLookup.Of(binding), inPattern) : SetKindOrNull(binding.Set);
            if (binding.Key == TemplateValue.LanguageName || (inPattern && binding.Key == TemplateValue.KeyName))
                return ValueKind.Text;
            if (schema.Values.TryGetValue(binding.Key, out var dataKind))
                return dataKind;
            if (template.Values.TryGetValue(binding.Key, out var value))
            {
                if (!value.IsSet)
                    return value.Kind;
                if (report)
                    Error(path, $"\"{binding.Key}\" is a set: write \"{{{binding.Key}[key]}}\".");
                return null;
            }
            if (report)
                Error(path, $"The card data has no value \"{binding.Key}\".");
            return null;
        }

        private ValueKind? SetKindOrNull(string set) => template.Values.TryGetValue(set, out var v) && v.IsSet ? v.Kind : null;

        /// <summary>The kind of the set's items; its keys must be valid texts.</summary>
        private ValueKind? LookupKind(string path, SetLookup lookup, bool inPattern)
        {
            for (int i = 0; i < lookup.Keys.Length; i++)
            {
                var keyPath = lookup.Keys.Length == 1 ? path : $"{path}.key[{i}]";
                CheckText(keyPath, lookup.Keys[i], inPattern);
                if (lookup.Keys[i].LiteralText is { } literal && !TemplateValue.IsValidKey(literal))
                    Error(keyPath, $"Invalid key \"{literal}\": use {TemplateValue.KeyRule}.");
            }
            if (!template.Values.TryGetValue(lookup.Set, out var set))
            {
                Error(path, $"Unknown set \"{lookup.Set}\".");
                return null;
            }
            if (!set.IsSet)
            {
                Error(path, $"\"{lookup.Set}\" is not a set: write \"{{{lookup.Set}}}\", or give it \"items\" or a \"pattern\".");
                return null;
            }
            CheckLiteralKeys(path, lookup, set);
            return set.Kind;
        }

        /// <summary>
        /// With only literal keys the result is known now: the set must have one of them (for a pattern of files, one of the
        /// files must exist), or a fallback.
        /// </summary>
        private void CheckLiteralKeys(string path, SetLookup lookup, TemplateValue set)
        {
            var keys = lookup.Keys.Select(k => k.LiteralText).ToList();
            if (keys.Any(k => k is null || !TemplateValue.IsValidKey(k)))
                return;
            switch (set.Definition)
            {
                case ItemsDefinition { Fallback: null } items when !keys.Any(k => items.Items.ContainsKey(k!)):
                    Error(path, $"Set \"{lookup.Set}\" has no item {string.Join(", ", keys.Select(k => $"\"{k}\""))} and no fallback.");
                    break;
                case PatternDefinition { Fallback: null, Pattern: SingleBody { Expr: ComposedExpr { Text: var text } } }
                    when TemplateValue.IsFile(set.Kind) && text.Bindings.All(b => b.Set is null && b.Key is TemplateValue.KeyName or TemplateValue.LanguageName):
                    var resolver = new BindingResolver(CardData.Empty, Languages.Fallback);
                    var files = keys.Select(k => resolver.Resolve(Replace(text, TemplateValue.KeyName, k!), path)).ToList();
                    if (!files.Any(f => Try(path, () => assets.Exists(f), assumed: true)))
                        Error(path, $"Set \"{lookup.Set}\" has no file for {string.Join(", ", keys.Select(k => $"\"{k}\""))}: {assets.Describe(files[0])}");
                    break;
            }
        }

        /// <summary>A text, or a path: bindings to texts, numbers, true/false or colors, with valid formats.</summary>
        private void CheckText(string path, TextTemplate text, bool inPattern = false)
        {
            foreach (var binding in text.Bindings)
            {
                if (BindingKind(path, binding, inPattern) is not { } kind)
                    continue;
                if (TemplateValue.IsFile(kind))
                {
                    Error(path, $"\"{binding}\" is {kind.ToString().ToLowerInvariant()}: it cannot be written inside a text.");
                    continue;
                }
                if (binding.Negate && kind != ValueKind.Bool)
                    Error(path, $"\"{binding}\": negation '!' is only valid for boolean values.");
                CheckFormat(path, binding, kind);
            }
        }

        private void CheckFormat(string path, Model.Binding binding, ValueKind kind)
        {
            if (binding.Format is null or BindingResolver.UpperFormat or BindingResolver.LowerFormat)
                return;

            if (kind is not (ValueKind.Int or ValueKind.Float))
            {
                Error(path, $"\"{binding}\": format \"{binding.Format}\" is only valid for numbers. Text values accept \"upper\" and \"lower\".");
                return;
            }

            try
            {
                _ = kind == ValueKind.Int
                    ? 0L.ToString(binding.Format, CultureInfo.InvariantCulture)
                    : 0d.ToString(binding.Format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                Error(path, $"\"{binding}\": invalid numeric format \"{binding.Format}\".");
            }
        }

        /// <summary>Template values that reach themselves through the values and sets they use.</summary>
        private void CheckCycles()
        {
            var state = new Dictionary<string, int>(StringComparer.Ordinal); // 1 visiting, 2 done
            var reported = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in template.Values.Keys)
                Visit(name, []);

            void Visit(string name, List<string> stack)
            {
                if (state.GetValueOrDefault(name) == 2 || !template.Values.TryGetValue(name, out var value))
                    return;
                if (state.GetValueOrDefault(name) == 1)
                {
                    foreach (var member in stack.SkipWhile(n => n != name))
                    {
                        if (reported.Add(member))
                            Error(template.Values[member].Path, $"\"{member}\" refers to itself through other values.");
                    }
                    return;
                }
                state[name] = 1;
                stack.Add(name);
                foreach (var used in References(value))
                    Visit(used, stack);
                stack.RemoveAt(stack.Count - 1);
                state[name] = 2;
            }
        }

        private static IEnumerable<string> References(TemplateValue value)
        {
            IEnumerable<ValueBody> bodies = value.Definition switch
            {
                PlainDefinition p => [p.Body],
                ItemsDefinition i => [.. i.Items.Values, .. i.Fallback is null ? [] : new[] { i.Fallback }],
                PatternDefinition p => [p.Pattern, .. p.Fallback is null ? [] : new[] { p.Fallback }],
                _ => [],
            };
            foreach (var expr in bodies.SelectMany(b => b.All))
            {
                switch (expr)
                {
                    case AliasExpr a:
                        foreach (var name in Names(a.Binding))
                            yield return name;
                        break;
                    case ComposedExpr c:
                        foreach (var name in c.Text.Bindings.SelectMany(Names))
                            yield return name;
                        break;
                    case LookupExpr l:
                        yield return l.Lookup.Set;
                        foreach (var name in l.Lookup.Keys.SelectMany(k => k.Bindings).SelectMany(Names))
                            yield return name;
                        break;
                }
            }

            static IEnumerable<string> Names(Model.Binding b) => b.Set is null ? [b.Key] : [b.Set, b.Key];
        }

        // ----- Elements -----

        private void CheckElement(Element element)
        {
            var p = element.Path;
            CheckBool($"{p}.visible", element.Visible);

            switch (element)
            {
                case GroupElement group:
                    CheckPoint($"{p}.position", group.Position);
                    foreach (var child in group.Children)
                        CheckElement(child);
                    break;
                case ImageElement img:
                    CheckRect($"{p}.rect", img.Rect);
                    CheckExpr($"{p}.image", img.Source, ValueKind.Image, inPattern: false);
                    break;
                case ShapeElement shape:
                    CheckRect($"{p}.rect", shape.Rect);
                    if (shape.Fill is not null)
                        CheckFill($"{p}.fill", shape.Fill);
                    if (shape.Stroke is not null)
                        CheckColor($"{p}.stroke.color", shape.Stroke.Color);
                    break;
                case LineElement line:
                    CheckPoint($"{p}.from", line.From);
                    CheckPoint($"{p}.to", line.To);
                    CheckColor($"{p}.color", line.Color);
                    break;
                case TextElement text:
                    CheckRect($"{p}.rect", text.Rect);
                    CheckText($"{p}.text", text.Text);
                    CheckColor($"{p}.color", text.Color);
                    CheckExpr($"{p}.font", text.Font, ValueKind.Font, inPattern: false);
                    break;
            }
        }

        private void CheckFill(string path, Fill fill)
        {
            switch (fill)
            {
                case SolidFill s:
                    CheckColor(path, s.Color);
                    break;
                case LinearGradientFill g:
                    CheckPoint($"{path}.linear.from", g.From);
                    CheckPoint($"{path}.linear.to", g.To);
                    for (int i = 0; i < g.Stops.Length; i++)
                        CheckColor($"{path}.linear.stops[{i}][1]", g.Stops[i].Color);
                    break;
            }
        }

        private void CheckRect(string path, RectValue rect)
        {
            CheckFloat($"{path}[0]", rect.X);
            CheckFloat($"{path}[1]", rect.Y);
            CheckFloat($"{path}[2]", rect.Width);
            CheckFloat($"{path}[3]", rect.Height);
            if (rect.Width is Literal<float> { Value: < 0 } || rect.Height is Literal<float> { Value: < 0 })
                Error(path, "Width and height cannot be negative.");
        }

        private void CheckPoint(string path, PointValue point)
        {
            CheckFloat($"{path}[0]", point.X);
            CheckFloat($"{path}[1]", point.Y);
        }

        private void CheckBool(string path, Value<bool> value)
        {
            if (value is Bound<bool> b && Expect(path, b.Binding, ValueKind.Bool) && b.Binding.Format is not null)
                Error(path, "Boolean bindings cannot have a format.");
            else if (value is LookedUp<bool> l && LookupKind(path, l.Lookup, false) is { } kind)
                ExpectKind(path, l.Lookup.Set, kind, ValueKind.Bool);
        }

        private void CheckFloat(string path, Value<float> value)
        {
            if (value is Bound<float> b && Expect(path, b.Binding, ValueKind.Int, ValueKind.Float) && b.Binding.Format is not null)
                Error(path, "Numeric property bindings cannot have a format.");
            else if (value is LookedUp<float> l && LookupKind(path, l.Lookup, false) is { } kind)
                ExpectKind(path, l.Lookup.Set, kind, ValueKind.Int, ValueKind.Float);
        }

        private void CheckColor(string path, Value<Rgba> value)
        {
            if (value is Bound<Rgba> b)
                Expect(path, b.Binding, ValueKind.Color);
            else if (value is LookedUp<Rgba> l && LookupKind(path, l.Lookup, false) is { } kind)
                ExpectKind(path, l.Lookup.Set, kind, ValueKind.Color);
        }

        private bool Expect(string path, Model.Binding binding, params ValueKind[] kinds)
        {
            if (BindingKind(path, binding, inPattern: false) is not { } kind)
                return false;
            int before = Issues.Count;
            ExpectKind(path, Name(binding), kind, kinds);
            return Issues.Count == before;
        }

        // ----- Files -----

        /// <summary>Whether the family is installed is checked by the renderer, which has access to the system fonts.</summary>
        private void CheckSystemFont(string path, string font)
        {
            if (SystemFonts.Family(font).Length == 0)
                Error(path, $"Missing font family after \"{SystemFonts.Prefix}\".");
        }

        private void CheckFile(string path, string file)
        {
            if (!Try(path, () => assets.Exists(file), assumed: true))
                Error(path, $"File not found: {assets.Describe(file)}");
        }

        /// <summary>
        /// Asks the asset provider; when it cannot answer (e.g. a download failed) the reason becomes an issue and
        /// <paramref name="assumed"/> is returned, so the same file is not reported twice.
        /// </summary>
        private bool Try(string path, Func<bool> check, bool assumed)
        {
            try
            {
                return check();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Error(path, ex.Message);
                return assumed;
            }
        }

        private void Error(string path, string message) => Issues.Add(new TemplateIssue(path, message));
    }
}
