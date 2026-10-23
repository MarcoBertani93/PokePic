using System.Collections.Immutable;
using PokePic.Core.Data;

namespace PokePic.Core.Model;

/// <summary>
/// A named value of the template, declared in its section of "values" ("text", "int", "float", "bool", "color",
/// "image", "font"), which gives its kind. It is used like a data key: "{name}", or "{name[key]}" for a set.
/// </summary>
/// <param name="Path">JSON path, e.g. "values.color.accent", for error messages.</param>
public sealed record TemplateValue(string Path, ValueKind Kind, ValueDefinition Definition)
{
    public bool IsSet => Definition is ItemsDefinition or PatternDefinition;

    /// <summary>Kinds that can be composed of texts: "{BASE_URL}/{species:D4}.png". The others take a literal or an alias.</summary>
    public static bool IsComposable(ValueKind kind) => kind is ValueKind.Text or ValueKind.Image or ValueKind.Font;

    /// <summary>Kinds whose value is a file of the template (or of the data, for images).</summary>
    public static bool IsFile(ValueKind kind) => kind is ValueKind.Image or ValueKind.Font;

    /// <summary>Names that are always defined: "{language}", the language of the element being drawn.</summary>
    public const string LanguageName = "language";

    /// <summary>In a pattern, "{key}" is the key looked up.</summary>
    public const string KeyName = "key";

    /// <summary>What <see cref="IsValidKey"/> accepts, for error messages.</summary>
    public const string KeyRule = "letters, digits, \"-\" and \"_\", in parts separated by \"/\"";

    /// <summary>
    /// Keys of sets: letters, digits, "-" and "_", in parts separated by "/" (e.g. "shiny/female/3"): no empty part and
    /// no ".", so the key of a pattern cannot leave its folder.
    /// </summary>
    public static bool IsValidKey(string key) =>
        key.Split('/').All(part => part.Length > 0 && part.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
}

/// <summary>How a template value is written: once (maybe by language), or as a set.</summary>
public abstract record ValueDefinition;

/// <summary>"#FF0000", or { "en": "Lv.", "it": "Liv." }.</summary>
public sealed record PlainDefinition(ValueBody Body) : ValueDefinition;

/// <summary>{ "items": { "fire": …, "water": … }, "fallback": … }: a value per key.</summary>
/// <param name="Fallback">For keys not listed. Null: such a key is an error when the card is made.</param>
public sealed record ItemsDefinition(ImmutableSortedDictionary<string, ValueBody> Items, ValueBody? Fallback) : ValueDefinition
{
    public bool Equals(ItemsDefinition? other) => other is not null && Items.SequenceEqual(other.Items) && Equals(Fallback, other.Fallback);

    public override int GetHashCode() => HashCode.Combine(Items.Count, Fallback);
}

/// <summary>
/// { "pattern": "{BASE}/{key}.png", "fallback": … }: a value computed from the key, for texts, images and fonts. Items are
/// not listed (a set of artworks can hold thousands of files): for files, a key whose file does not exist uses the fallback.
/// </summary>
public sealed record PatternDefinition(ValueBody Pattern, ValueBody? Fallback) : ValueDefinition;

/// <summary>A value written once, or once per language.</summary>
public abstract record ValueBody
{
    /// <summary>The expression for a language: the same, else its base language, else English.</summary>
    public abstract ValueExpr For(string? language);

    public abstract IEnumerable<ValueExpr> All { get; }
}

public sealed record SingleBody(ValueExpr Expr) : ValueBody
{
    public override ValueExpr For(string? language) => Expr;

    public override IEnumerable<ValueExpr> All => [Expr];
}

/// <summary>{ "en": …, "it": … }: "en" is required.</summary>
public sealed record ByLanguageBody(ImmutableSortedDictionary<string, ValueExpr> Exprs) : ValueBody
{
    public override ValueExpr For(string? language) => Exprs[Languages.Pick(Exprs.Keys, language)];

    public override IEnumerable<ValueExpr> All => Exprs.Values;

    public bool Equals(ByLanguageBody? other) => other is not null && Exprs.SequenceEqual(other.Exprs);

    public override int GetHashCode() => HashCode.Combine(Exprs.Count);
}

/// <summary>One value: a literal, a reference to another value, a composition of texts, or the item of a set.</summary>
public abstract record ValueExpr;

/// <summary>6, 1.5, true, "#FF0000".</summary>
public sealed record LiteralExpr(CardValue Value) : ValueExpr;

/// <summary>"{type1.color}", "{!hasType2}", "{typeColor[move1.type.key]}": another value of the same kind.</summary>
public sealed record AliasExpr(Binding Binding) : ValueExpr;

/// <summary>
/// A text, or the path of an image or font: "Lv. {level}", "{BASE_URL}/{species:D4}.png", "fonts/a.ttf",
/// "system:Arial". A lone binding to a value of the same kind ("{frame}", "{species.sprite}") is that value.
/// </summary>
public sealed record ComposedExpr(TextTemplate Text) : ValueExpr;

/// <summary>{ "set": "artwork", "key": ["{species}-{form}", "{species}"] }: see <see cref="SetLookup"/>.</summary>
public sealed record LookupExpr(SetLookup Lookup) : ValueExpr;

/// <summary>
/// The item of a set picked by keys tried in order: the first key the set has (for a pattern of files, whose file
/// exists), then the set's fallback. "{set[key]}" is the same with one key.
/// </summary>
public sealed record SetLookup(string Set, ImmutableArray<TextTemplate> Keys)
{
    public static SetLookup Of(Binding binding) =>
        new(binding.Set!, [new TextTemplate([new BindingSegment(new Binding(binding.Key))])]);

    public bool Equals(SetLookup? other) => other is not null && Set == other.Set && Keys.SequenceEqual(other.Keys);

    public override int GetHashCode() => HashCode.Combine(Set, Keys.Length);
}
