using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using PokePic.Core.Assets;
using PokePic.Core.Json;
using PokePic.Core.Model;

namespace PokePic.Core.Data;

/// <summary>
/// A <see cref="CardData"/> saved to JSON. This is how values and images coming from PKHeX
/// reach the standalone editor, and how sample data is shipped.
/// </summary>
/// <param name="Description">Free text shown in the editor, e.g. "Hypno Lv. 42".</param>
/// <param name="Source">Who produced the pack, e.g. "PKHeX 26.10.18" or "sample".</param>
/// <param name="Language">Default language for localized values, e.g. the language selected in PKHeX. Null: English.</param>
/// <param name="Name">Short name of the Pokémon, used in file names (e.g. "Spotlight_Drago.png"): its nickname,
/// else its species in <paramref name="Language"/>. The producer of the pack sets it; see <see cref="EffectiveName"/>.</param>
public sealed record DataPack(string? Description, string? Source, CardData Data, string? Language = null, string? Name = null)
{
    /// <summary>
    /// <see cref="Name"/>, or for packs without it the "nickname" text (unless "isNicknamed" is false),
    /// else "species.name" in the pack's language.
    /// </summary>
    public string? EffectiveName =>
        Name is { Length: > 0 } ? Name
        : Data.Values.GetValueOrDefault("nickname") is TextValue { Value.Length: > 0 } nickname
          && Data.Values.GetValueOrDefault("isNicknamed") is not BoolValue { Value: false } ? nickname.Value
        : Data.Values.GetValueOrDefault("species.name") is { Kind: ValueKind.Text } species
            ? LocalizedValue.In(species, Language ?? Languages.Fallback).ToText()
            : null;
}

public sealed record DataPackReadResult(DataPack? Pack, IReadOnlyList<TemplateIssue> Issues)
{
    public bool Success => Pack is not null && Issues.Count == 0;
}

/// <summary>
/// JSON format:
/// <code>
/// {
///   "formatVersion": 1,
///   "description": "Hypno Lv. 42",
///   "name": "Hypno",
///   "language": "it",
///   "values": {
///     "text":  { "nickname": "Hypno", "species.name": { "en": "Hypno", "ja": "スリーパー" } },
///     "int":   { "level": 42 },
///     "float": { },
///     "bool":  { "hasType2": false },
///     "color": { "type1.color": "#EF4179" },
///     "image": { "species.sprite": "sprites/b_97.png", "ball.sprite": { "png": "iVBORw0..." } }
///   }
/// }
/// </code>
/// The sections are those of a template's "values" (without "font"). Every value is single, or by language
/// (language code → value, "en" required as the fallback). An image is a file path relative to the pack, or { "png": Base64 }.
/// </summary>
public static class DataPackSerializer
{
    public const int FormatVersion = 1;

    /// <summary>The sections of "values" in a data pack, by kind.</summary>
    public static IReadOnlyList<(string Name, ValueKind Kind)> Sections { get; } =
    [
        ("text", ValueKind.Text), ("int", ValueKind.Int), ("float", ValueKind.Float),
        ("bool", ValueKind.Bool), ("color", ValueKind.Color), ("image", ValueKind.Image),
    ];

    public static DataPackReadResult Read(string json, IAssetProvider assets)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, TemplateParser.DocumentOptions);
        }
        catch (JsonException ex)
        {
            int? line = ex.LineNumber is { } l ? (int)l + 1 : null;
            return new DataPackReadResult(null, [new TemplateIssue("", $"Invalid JSON: {ex.Message}", line)]);
        }

        using (document)
        {
            var issues = new IssueCollector();
            var pack = ReadPack(issues, document.RootElement, assets);
            var located = JsonLineMap.Build(json, TemplateParser.ReaderOptions).Locate(issues.Issues);
            return new DataPackReadResult(issues.HasErrors ? null : pack, located);
        }
    }

    public static DataPackReadResult ReadFile(string path, WebAssetCache? web = null)
    {
        var assets = new FileSystemAssetProvider(Path.GetDirectoryName(Path.GetFullPath(path))!, web);
        return Read(File.ReadAllText(path), assets);
    }

    /// <summary>
    /// Writes a self-contained pack: images are embedded as Base64.
    /// </summary>
    public static string Write(DataPack pack)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteNumber("formatVersion", FormatVersion);
            if (pack.Description is not null)
                w.WriteString("description", pack.Description);
            if (pack.Name is not null)
                w.WriteString("name", pack.Name);
            if (pack.Source is not null)
                w.WriteString("source", pack.Source);
            if (pack.Language is not null)
                w.WriteString("language", pack.Language);

            w.WriteStartObject("values");
            foreach (var (section, kind) in Sections)
            {
                w.WriteStartObject(section);
                foreach (var (key, value) in pack.Data.Values)
                {
                    if (value.Kind != kind)
                        continue;
                    w.WritePropertyName(key);
                    if (value is LocalizedValue localized)
                    {
                        w.WriteStartObject();
                        foreach (var (language, item) in localized.Values)
                        {
                            w.WritePropertyName(language);
                            WriteSingle(w, item);
                        }
                        w.WriteEndObject();
                    }
                    else
                    {
                        WriteSingle(w, value);
                    }
                }
                w.WriteEndObject();
            }
            w.WriteEndObject();

            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteSingle(Utf8JsonWriter w, CardValue value)
    {
        switch (value)
        {
            case TextValue t:
                w.WriteStringValue(t.Value);
                break;
            case IntValue i:
                w.WriteNumberValue(i.Value);
                break;
            case FloatValue f:
                w.WriteNumberValue(f.Value);
                break;
            case BoolValue b:
                w.WriteBooleanValue(b.Value);
                break;
            case ColorValue c:
                w.WriteStringValue(c.Value.ToString());
                break;
            case ImageValue image:
                w.WriteStartObject();
                w.WriteBase64String("png", image.Image.Encoded.AsSpan());
                w.WriteEndObject();
                break;
            default:
                throw new InvalidOperationException($"A data pack cannot hold {value.GetType().Name}.");
        }
    }

    private static DataPack? ReadPack(IssueCollector issues, JsonElement root, IAssetProvider assets)
    {
        var obj = ObjectReader.From(issues, "", root);
        if (obj is null)
            return null;

        obj.Ignore("$schema");
        var version = obj.Int("formatVersion", required: true);
        if (version is not null && version != FormatVersion)
            issues.Error(obj.PathOf("formatVersion"), $"Unsupported data pack format version {version}; expected {FormatVersion}.");

        var description = obj.String("description");
        var name = obj.String("name");
        var source = obj.String("source");
        var language = obj.String("language");
        if (language is not null && !Languages.IsValidCode(language))
        {
            issues.Error(obj.PathOf("language"), $"Invalid language code \"{language}\".");
            language = null;
        }

        var values = ImmutableSortedDictionary.CreateBuilder<string, CardValue>(StringComparer.Ordinal);
        if (obj.Get("values") is { } valuesElement && ObjectReader.From(issues, obj.PathOf("values"), valuesElement) is { } sections)
        {
            foreach (var (section, kind) in Sections)
            {
                if (sections.Get(section) is not { } e || ObjectReader.From(issues, sections.PathOf(section), e) is not { } entries)
                    continue;
                foreach (var (key, value) in entries.All.ToList())
                {
                    entries.Ignore(key);
                    var path = entries.PathOf(key);
                    if (values.ContainsKey(key))
                        issues.Error(path, $"Key \"{key}\" is defined in more than one section.");
                    else if (ReadValue(issues, path, value, kind, assets) is { } v)
                        values[key] = v;
                }
            }
            sections.ReportUnknown();
        }

        obj.ReportUnknown();
        return new DataPack(description, source, new CardData(values.ToImmutable()), language, name);
    }

    /// <summary>A single value of the kind, or by language ({ "en": …, "it": … }).</summary>
    private static CardValue? ReadValue(IssueCollector issues, string path, JsonElement e, ValueKind kind, IAssetProvider assets)
    {
        // An embedded image is an object too: { "png": … }.
        if (e.ValueKind != JsonValueKind.Object || (kind == ValueKind.Image && e.TryGetProperty("png", out _)))
            return ReadSingle(issues, path, e, kind, assets);

        var obj = ObjectReader.From(issues, path, e)!;
        var items = ImmutableSortedDictionary.CreateBuilder<string, CardValue>(StringComparer.Ordinal);
        bool ok = true;
        foreach (var (language, value) in obj.All.ToList())
        {
            obj.Ignore(language);
            if (!Languages.IsValidCode(language))
            {
                issues.Error(obj.PathOf(language), $"Invalid language code \"{language}\".");
                ok = false;
            }
            else if (ReadSingle(issues, obj.PathOf(language), value, kind, assets) is { } item)
                items[language] = item;
            else
                ok = false;
        }
        if (!obj.Has(Languages.Fallback))
        {
            issues.Error(path, $"A value by language must contain \"{Languages.Fallback}\": it is used when a language is not available.");
            return null;
        }
        return ok ? new LocalizedValue(items.ToImmutable()) : null;
    }

    private static CardValue? ReadSingle(IssueCollector issues, string path, JsonElement e, ValueKind kind, IAssetProvider assets)
    {
        if (kind == ValueKind.Image)
            return ReadImage(issues, path, e, assets) is { } image ? new ImageValue(image) : null;

        CardValue? value = kind switch
        {
            ValueKind.Text when e.ValueKind == JsonValueKind.String => new TextValue(e.GetString()!),
            ValueKind.Int when e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var i) => new IntValue(i),
            ValueKind.Float when e.ValueKind == JsonValueKind.Number => new FloatValue(e.GetDouble()),
            ValueKind.Bool when e.ValueKind is JsonValueKind.True or JsonValueKind.False => new BoolValue(e.GetBoolean()),
            ValueKind.Color when e.ValueKind == JsonValueKind.String && Rgba.TryParse(e.GetString(), out var c) => new ColorValue(c.Value),
            _ => null,
        };
        if (value is null)
        {
            var expected = kind switch
            {
                ValueKind.Text => "a text or an object of language → text",
                ValueKind.Int => "an integer",
                ValueKind.Float => "a number",
                ValueKind.Bool => "true or false",
                _ => "a color such as \"#RRGGBB\"",
            };
            issues.Error(path, $"Expected {expected}, found {ObjectReader.Describe(e)}.");
        }
        return value;
    }

    private static CardImage? ReadImage(IssueCollector issues, string path, JsonElement e, IAssetProvider assets)
    {
        if (e.ValueKind == JsonValueKind.String)
        {
            var file = e.GetString()!;
            try
            {
                if (!assets.Exists(file))
                {
                    issues.Error(path, $"File not found: {assets.Describe(file)}");
                    return null;
                }
                return CardImage.FromBytes(assets.Read(file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Error(path, ex.Message);
                return null;
            }
        }

        var obj = ObjectReader.From(issues, path, e);
        if (obj is null)
            return null;
        var base64 = obj.String("png", required: true);
        obj.ReportUnknown();
        if (base64 is null)
            return null;

        try
        {
            return CardImage.FromBytes(Convert.FromBase64String(base64));
        }
        catch (FormatException)
        {
            issues.Error(obj.PathOf("png"), "Invalid Base64 data.");
            return null;
        }
    }
}
