using System.Collections.Immutable;
using System.Globalization;
using PokePic.Core.Model;

namespace PokePic.Core.Data;

/// <summary>
/// The kinds of values, the same in the data pack and in the template ("values": { "text": …, "int": … }).
/// Fonts exist only in templates.
/// </summary>
public enum ValueKind
{
    Text,
    Int,
    Float,
    Bool,
    Color,
    Image,
    Font,
}

/// <summary>
/// A typed value of the card data.
/// </summary>
public abstract record CardValue
{
    public abstract ValueKind Kind { get; }

    /// <summary>
    /// Invariant, culture-independent text representation.
    /// </summary>
    public abstract string ToText(string? numericFormat = null);
}

public sealed record TextValue(string Value) : CardValue
{
    public override ValueKind Kind => ValueKind.Text;
    public override string ToText(string? numericFormat = null) => Value;
}

/// <summary>
/// A value in several languages (language code → value of one kind). English is required: it is the fallback.
/// </summary>
public sealed record LocalizedValue : CardValue
{
    public LocalizedValue(ImmutableSortedDictionary<string, CardValue> values)
    {
        if (!values.ContainsKey(Languages.Fallback))
            throw new ArgumentException($"A localized value must contain the \"{Languages.Fallback}\" value.", nameof(values));
        if (values.Values.Any(v => v is LocalizedValue) || values.Values.Select(v => v.Kind).Distinct().Count() > 1)
            throw new ArgumentException("The values of a localized value must have one kind and no languages of their own.", nameof(values));
        Values = values;
    }

    /// <summary>A text in several languages.</summary>
    public static LocalizedValue Texts(IEnumerable<KeyValuePair<string, string>> texts) =>
        new(texts.ToImmutableSortedDictionary(kv => kv.Key, kv => (CardValue)new TextValue(kv.Value), StringComparer.Ordinal));

    public ImmutableSortedDictionary<string, CardValue> Values { get; }

    public override ValueKind Kind => Values[Languages.Fallback].Kind;

    public override string ToText(string? numericFormat = null) => Values[Languages.Fallback].ToText(numericFormat);

    /// <summary>The value for a language: the same language, else its base language, else English.</summary>
    public CardValue Get(string? language) => Values[Languages.Pick(Values.Keys, language)];

    public string GetText(string? language) => Get(language).ToText();

    /// <summary>The value in <paramref name="language"/> when <paramref name="value"/> is localized, else the value itself.</summary>
    public static CardValue In(CardValue value, string? language) => value is LocalizedValue l ? l.Get(language) : value;

    public bool Equals(LocalizedValue? other) => other is not null && Values.SequenceEqual(other.Values);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var (language, value) in Values)
        {
            hash.Add(language);
            hash.Add(value);
        }
        return hash.ToHashCode();
    }
}

/// <summary>
/// Language codes as used by PKHeX ("en", "it", "ja", "zh-Hans"...).
/// </summary>
public static class Languages
{
    public const string Fallback = "en";

    /// <summary>
    /// The available language to use for a request: the same code (ignoring case), else its base language
    /// ("it-IT" → "it"), else English.
    /// </summary>
    public static string Pick(IEnumerable<string> available, string? requested)
    {
        var list = available as IReadOnlyCollection<string> ?? [.. available];
        if (!string.IsNullOrEmpty(requested))
        {
            if (list.FirstOrDefault(l => string.Equals(l, requested, StringComparison.OrdinalIgnoreCase)) is { } exact)
                return exact;
            int dash = requested.IndexOf('-');
            if (dash > 0 && list.FirstOrDefault(l => string.Equals(l, requested[..dash], StringComparison.OrdinalIgnoreCase)) is { } primary)
                return primary;
        }
        return Fallback;
    }

    /// <summary>
    /// The languages to try for a request, in order: the language, its base language, English
    /// (the same rule as <see cref="Pick"/>, for files that may or may not exist).
    /// </summary>
    public static IReadOnlyList<string> Candidates(string? requested)
    {
        var list = new List<string>();
        if (!string.IsNullOrEmpty(requested))
        {
            list.Add(requested);
            int dash = requested.IndexOf('-');
            if (dash > 0)
                list.Add(requested[..dash]);
        }
        list.Add(Fallback);
        return [.. list.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>"en", "it", "zh-Hans", "pt-BR"...</summary>
    public static bool IsValidCode(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return false;
        var parts = code.Split('-');
        return parts[0].Length is 2 or 3 && parts[0].All(char.IsAsciiLetter)
            && parts.Skip(1).All(p => p.Length is >= 2 and <= 8 && p.All(char.IsAsciiLetterOrDigit));
    }
}

public sealed record IntValue(long Value) : CardValue
{
    public override ValueKind Kind => ValueKind.Int;
    public override string ToText(string? numericFormat = null) => Value.ToString(numericFormat, CultureInfo.InvariantCulture);
}

public sealed record FloatValue(double Value) : CardValue
{
    public override ValueKind Kind => ValueKind.Float;
    public override string ToText(string? numericFormat = null) => Value.ToString(numericFormat, CultureInfo.InvariantCulture);
}

public sealed record BoolValue(bool Value) : CardValue
{
    public override ValueKind Kind => ValueKind.Bool;
    public override string ToText(string? numericFormat = null) => Value ? "true" : "false";
}

public sealed record ColorValue(Rgba Value) : CardValue
{
    public override ValueKind Kind => ValueKind.Color;
    public override string ToText(string? numericFormat = null) => Value.ToString();
}

/// <summary>
/// Encoded image (PNG) provided by the data source.
/// </summary>
public sealed record CardImage(ImmutableArray<byte> Encoded)
{
    public static CardImage FromBytes(byte[] bytes) => new(ImmutableArray.Create(bytes));

    public bool Equals(CardImage? other) => other is not null && Encoded.AsSpan().SequenceEqual(other.Encoded.AsSpan());

    public override int GetHashCode() => HashCode.Combine(Encoded.Length);
}

/// <summary>An image of the data (e.g. the sprite from PKHeX).</summary>
public sealed record ImageValue(CardImage Image) : CardValue
{
    public override ValueKind Kind => ValueKind.Image;
    public override string ToText(string? numericFormat = null) => $"<image {Image.Encoded.Length} bytes>";
}

/// <summary>
/// A file of the template, resolved: an image or a font, as a path relative to the template folder, file:// or
/// http(s):// (fonts also "system:Family").
/// </summary>
public sealed record FileValue(ValueKind FileKind, string Path) : CardValue
{
    public override ValueKind Kind => FileKind;
    public override string ToText(string? numericFormat = null) => Path;
}

/// <summary>
/// Neutral data a card is rendered from. Knows nothing about PKHeX.
/// Keys are case-sensitive and sorted ordinally, so iteration is deterministic.
/// </summary>
public sealed record CardData(ImmutableSortedDictionary<string, CardValue> Values)
{
    public static CardData Empty { get; } = new(ImmutableSortedDictionary.Create<string, CardValue>(StringComparer.Ordinal));

    public DataSchema Schema => DataSchema.FromData(this);
}

/// <summary>
/// The keys and kinds a template may bind to. Used to validate templates without the actual data.
/// </summary>
public sealed record DataSchema(ImmutableSortedDictionary<string, ValueKind> Values)
{
    public static DataSchema FromData(CardData data) =>
        new(data.Values.ToImmutableSortedDictionary(kv => kv.Key, kv => kv.Value.Kind, StringComparer.Ordinal));
}
