using System.Text;
using PokePic.Core.Data;
using PokePic.Core.Model;

namespace PokePic.Core.Resolution;

/// <summary>
/// Resolves template values against card data and the template's values. All formatting is culture-invariant.
/// Localized values are resolved in <see cref="Language"/> unless a call asks for another language.
/// </summary>
/// <param name="fileExists">
/// Whether a file of the template exists (file, JSON path for errors), to pick the first existing file among candidates (keys of a pattern set,
/// languages of a path with "{language}"). Null: the first candidate is taken.
/// </param>
public sealed class BindingResolver(
    CardData data,
    string language = Languages.Fallback,
    IReadOnlyDictionary<string, TemplateValue>? values = null,
    Func<string, string, bool>? fileExists = null)
{
    public const string UpperFormat = "upper";
    public const string LowerFormat = "lower";

    /// <summary>Values referencing each other deeper than this are a cycle (the validator reports them earlier).</summary>
    private const int MaxDepth = 32;

    public CardData Data { get; } = data;

    public string Language { get; } = language;

    public IReadOnlyDictionary<string, TemplateValue> Values { get; } = values ?? new Dictionary<string, TemplateValue>();

    /// <summary>A resolver for a template: its values.</summary>
    public static BindingResolver For(CardTemplate template, CardData data, string language = Languages.Fallback, Func<string, string, bool>? fileExists = null) =>
        new(data, language, template.Values, fileExists);

    /// <summary>What the bindings of a text see: the language, and in a pattern the key looked up.</summary>
    private readonly record struct Scope(string Language, int Depth, string? Key = null, string? LanguageCode = null)
    {
        public Scope Deeper => this with { Depth = Depth + 1, Key = null, LanguageCode = null };
    }

    public bool Resolve(Value<bool> value, string path) => value switch
    {
        Literal<bool> l => l.Value,
        Bound<bool> b => Lookup<BoolValue>(b.Binding, path).Value,
        LookedUp<bool> s => As<BoolValue>(Find(s.Lookup, path, new Scope(Language, 0)), s.Lookup.Set, path).Value,
        _ => throw Unsupported(value),
    };

    public float Resolve(Value<float> value, string path) => value switch
    {
        Literal<float> l => l.Value,
        Bound<float> b => Number(Value(b.Binding, path, new Scope(Language, 0)), Name(b.Binding), path),
        LookedUp<float> s => Number(Find(s.Lookup, path, new Scope(Language, 0)), s.Lookup.Set, path),
        _ => throw Unsupported(value),
    };

    public Rgba Resolve(Value<Rgba> value, string path) => value switch
    {
        Literal<Rgba> l => l.Value,
        Bound<Rgba> b => Lookup<ColorValue>(b.Binding, path).Value,
        LookedUp<Rgba> s => As<ColorValue>(Find(s.Lookup, path, new Scope(Language, 0)), s.Lookup.Set, path).Value,
        _ => throw Unsupported(value),
    };

    /// <param name="language">Overrides <see cref="Language"/> for this text (e.g. a text element's own language).</param>
    public string Resolve(TextTemplate text, string path, string? language = null) => ResolveText(text, path, new Scope(language ?? Language, 0));

    /// <summary>
    /// An image or a font: an image of the data (<see cref="ImageValue"/>) or a file of the template (<see cref="FileValue"/>).
    /// </summary>
    public CardValue ResolveFile(ValueExpr expr, ValueKind kind, string path, string? language = null) => Evaluate(expr, kind, path, language);

    /// <summary>The value of an expression of a kind, e.g. one cell of a template value.</summary>
    public CardValue Evaluate(ValueExpr expr, ValueKind kind, string path, string? language = null) =>
        Evaluate(expr, kind, path, new Scope(language ?? Language, 0));

    /// <summary>The value of a data key or of a template value, with its kind (e.g. to show what a value gives).</summary>
    public CardValue ValueOf(string key, string path) => Value(key, path, new Scope(Language, 0));

    public static string Format(CardValue value, Binding binding, string language = Languages.Fallback)
    {
        value = LocalizedValue.In(value, language);
        return binding.Format switch
        {
            null => value.ToText(),
            UpperFormat => value.ToText().ToUpperInvariant(),
            LowerFormat => value.ToText().ToLowerInvariant(),
            var numeric => value.ToText(numeric),
        };
    }

    private string ResolveText(TextTemplate text, string path, Scope scope)
    {
        var sb = new StringBuilder();
        foreach (var segment in text.Segments)
        {
            switch (segment)
            {
                case LiteralSegment l:
                    sb.Append(l.Text);
                    break;
                case BindingSegment b:
                    sb.Append(Format(Value(b.Binding, path, scope), b.Binding, scope.Language));
                    break;
            }
        }
        return sb.ToString();
    }

    private T Lookup<T>(Binding binding, string path) where T : CardValue =>
        As<T>(Value(binding, path, new Scope(Language, 0)), Name(binding), path);

    private static T As<T>(CardValue value, string name, string path) where T : CardValue =>
        value as T ?? throw new TemplateException(path,
            $"Value \"{name}\" is {value.Kind.ToString().ToLowerInvariant()}, expected {typeof(T).Name.Replace("Value", "", StringComparison.Ordinal).ToLowerInvariant()}.");

    private static float Number(CardValue value, string name, string path) => value switch
    {
        IntValue i => i.Value,
        FloatValue f => (float)f.Value,
        _ => throw new TemplateException(path, $"Value \"{name}\" is {value.Kind.ToString().ToLowerInvariant()}, expected a number."),
    };

    private static string Name(Binding binding) => binding.Set is null ? binding.Key : $"{binding.Set}[{binding.Key}]";

    /// <summary>A binding's value: a data value or template value, or with a set the item picked by the key's value. "!" negates.</summary>
    private CardValue Value(Binding binding, string path, Scope scope)
    {
        var value = binding.Set is null ? Value(binding.Key, path, scope) : Find(SetLookup.Of(binding), path, scope);
        return binding.Negate && value is BoolValue b ? new BoolValue(!b.Value) : value;
    }

    /// <summary>A data value or a template value, in the scope's language.</summary>
    private CardValue Value(string key, string path, Scope scope)
    {
        if (key == TemplateValue.KeyName && scope.Key is not null)
            return new TextValue(scope.Key);
        if (key == TemplateValue.LanguageName)
            return new TextValue(scope.LanguageCode ?? scope.Language);
        if (Data.Values.TryGetValue(key, out var data))
            return LocalizedValue.In(data, scope.Language);
        if (!Values.TryGetValue(key, out var value))
            throw new TemplateException(path, $"The card data has no value \"{key}\".");
        if (scope.Depth >= MaxDepth)
            throw new TemplateException(path, $"Value \"{key}\" refers to itself through other values.");
        if (value.Definition is not PlainDefinition plain)
            throw new TemplateException(path, $"\"{key}\" is a set: write \"{{{key}[key]}}\".");
        return Evaluate(plain.Body.For(scope.Language), value.Kind, path, scope.Deeper);
    }

    private CardValue Evaluate(ValueExpr expr, ValueKind kind, string path, Scope scope) => expr switch
    {
        LiteralExpr l => l.Value,
        AliasExpr a => Value(a.Binding, path, scope),
        LookupExpr s => Find(s.Lookup, path, scope),
        ComposedExpr c => Compose(c.Text, kind, path, scope),
        _ => throw Unsupported(expr),
    };

    /// <summary>
    /// A text, or the path of a file. A lone binding to a value of the kind is that value ("{frame}", "{species.sprite}").
    /// A path with "{language}" tries the language, its base language and English, and takes the first file that exists.
    /// </summary>
    private CardValue Compose(TextTemplate text, ValueKind kind, string path, Scope scope)
    {
        if (kind == ValueKind.Text)
            return new TextValue(ResolveText(text, path, scope));

        if (text.SingleBinding is { Format: null, Negate: false } single)
        {
            var value = Value(single, path, scope);
            if (value.Kind == kind)
                return value;
        }

        if (fileExists is not null && text.Bindings.Any(b => b.Key == TemplateValue.LanguageName && b.Set is null))
        {
            var candidates = Languages.Candidates(scope.Language).Select(l => ResolveText(text, path, scope with { LanguageCode = l })).ToList();
            return new FileValue(kind, candidates.FirstOrDefault(c => fileExists(c, path)) ?? candidates[0]);
        }
        return new FileValue(kind, ResolveText(text, path, scope));
    }

    /// <summary>The item of a set for the first key that has one, else the set's fallback.</summary>
    private CardValue Find(SetLookup lookup, string path, Scope scope)
    {
        if (!Values.TryGetValue(lookup.Set, out var value) || !value.IsSet)
            throw new TemplateException(path, $"\"{lookup.Set}\" is not a set.");
        if (scope.Depth >= MaxDepth)
            throw new TemplateException(path, $"Set \"{lookup.Set}\" refers to itself through other values.");

        var inner = scope.Deeper;
        var tried = new List<string>();
        foreach (var keyText in lookup.Keys)
        {
            var key = ResolveText(keyText, path, scope);
            tried.Add(key);
            switch (value.Definition)
            {
                case ItemsDefinition items when items.Items.TryGetValue(key, out var item):
                    return Evaluate(item.For(scope.Language), value.Kind, path, inner);
                case PatternDefinition pattern:
                    if (!TemplateValue.IsValidKey(key))
                        throw new TemplateException(path, $"Set \"{lookup.Set}\": invalid key \"{key}\" ({TemplateValue.KeyRule} only).");
                    var found = Evaluate(pattern.Pattern.For(scope.Language), value.Kind, path, inner with { Key = key });
                    if (fileExists is null || !TemplateValue.IsFile(value.Kind) || found is not FileValue file || fileExists(file.Path, path))
                        return found;
                    break;
            }
        }

        var fallback = value.Definition switch
        {
            ItemsDefinition i => i.Fallback,
            PatternDefinition p => p.Fallback,
            _ => null,
        };
        return fallback is not null
            ? Evaluate(fallback.For(scope.Language), value.Kind, path, inner)
            : throw new TemplateException(path, $"Set \"{lookup.Set}\" has nothing for {string.Join(", ", tried.Select(k => $"\"{k}\""))} and no fallback.");
    }

    private static InvalidOperationException Unsupported(object value) => new($"Unsupported value node {value.GetType().Name}.");
}
