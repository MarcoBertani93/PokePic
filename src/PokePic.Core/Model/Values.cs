using System.Text;

namespace PokePic.Core.Model;

/// <summary>
/// Reference to a <see cref="Data.CardData"/> entry, written as "{key}", "{key:format}" or "{!key}";
/// "{set[key]}" looks up the value of the key in a template set ("{typeColor[move1.type.key]}").
/// </summary>
/// <param name="Key">Data key or variable, e.g. "species.name"; with <paramref name="Set"/>, the key whose value picks the item.</param>
/// <param name="Format">Optional format: "upper", "lower" or a .NET numeric format such as "D3". With a set, it applies to the item.</param>
/// <param name="Negate">Boolean negation ("{!key}"); valid only for boolean values.</param>
/// <param name="Set">Name of the template set to look the key's value up in, or null.</param>
public sealed record Binding(string Key, string? Format = null, bool Negate = false, string? Set = null)
{
    public override string ToString()
    {
        var sb = new StringBuilder("{");
        if (Negate)
            sb.Append('!');
        sb.Append(Set is null ? Key : $"{Set}[{Key}]");
        if (Format is not null)
            sb.Append(':').Append(Format);
        return sb.Append('}').ToString();
    }
}

/// <summary>
/// A template property: either a literal value or a binding resolved against the card data.
/// </summary>
public abstract record Value<T>;

public sealed record Literal<T>(T Value) : Value<T>;

public sealed record Bound<T>(Binding Binding) : Value<T>;

/// <summary>{ "set": "typeColor", "key": ["{move1.type.key}", "{type1.key}"] }: the item of a set.</summary>
public sealed record LookedUp<T>(SetLookup Lookup) : Value<T>;

public abstract record TextSegment;

public sealed record LiteralSegment(string Text) : TextSegment;

public sealed record BindingSegment(Binding Binding) : TextSegment;

/// <summary>
/// Text with interpolated bindings, e.g. "Lv. {level}". Braces are escaped as "{{" and "}}".
/// </summary>
public sealed record TextTemplate(IReadOnlyList<TextSegment> Segments)
{
    public IEnumerable<Binding> Bindings => Segments.OfType<BindingSegment>().Select(s => s.Binding);

    /// <summary>
    /// The binding, when the whole text is exactly one binding ("{key}").
    /// </summary>
    public Binding? SingleBinding => Segments is [BindingSegment only] ? only.Binding : null;

    /// <summary>
    /// The literal text, when the template contains no bindings.
    /// </summary>
    public string? LiteralText => Segments.All(s => s is LiteralSegment)
        ? string.Concat(Segments.Cast<LiteralSegment>().Select(s => s.Text))
        : null;

    public static TextTemplate FromLiteral(string text) => new([new LiteralSegment(text)]);

    public bool Equals(TextTemplate? other) => other is not null && Segments.SequenceEqual(other.Segments);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var s in Segments)
            hash.Add(s);
        return hash.ToHashCode();
    }
}
