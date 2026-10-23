#:project ../src/PokePic.Adapter/PokePic.Adapter.csproj
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:property TreatWarningsAsErrors=false
#:property NoWarn=IL2026;IL3050;IL2075;IL2070
// Generates the template reference and the data keys from the PokePic code, and refuses to do it when they disagree:
//   - element property names must equal PokePic.Core.Model.ElementProperties;
//   - enum values are read from the real enum types;
//   - every rule (required, enum values, numeric ranges, literal-only numbers) is probed with TemplateParser;
//   - the data keys are those PokePic.Adapter produces for a real PKHeX Pokémon, and each must have a description here.
// Writes docs/template-reference.md, docs/data-keys.md and their .it.md translations; with a second folder, also
// template.schema.json and data-keys.json there (for the agent package).
// Usage: dotnet run build/generate-reference.cs -- docs [<schema folder>]
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PKHeX.Core;
using PokePic.Adapter;
using PokePic.Core.Data;
using PokePic.Core.Json;
using PokePic.Core.Model;
using PokePic.Core.Rendering;
using static Spec;

var docsDir = args.Length > 0 ? args[0] : "docs";
var schemaOut = args.Length > 1 ? args[1] : null;
var failures = new List<string>();

// ---------------------------------------------------------------- specification (one place)

Prop[] common =
[
    new("type", K.ElementType, Required: true, Doc: new("Element type.", "Tipo di elemento.")),
    new("name", K.String, Doc: new("Name shown in the editor's Layers panel.", "Nome mostrato nel pannello Livelli dell'editor.")),
    new("style", K.String, Doc: new("Name of a style of `styles`: the element's own properties win.", "Nome di uno stile di `styles`: le proprietà dell'elemento hanno la precedenza.")),
    new("visible", K.BoolOrBinding, Default: "true", Doc: new("`\"{!key}\"` is the opposite of the value.", "`\"{!chiave}\"` è il contrario del valore.")),
    new("opacity", K.Number, Min: 0, Max: 1, Default: "1", Doc: new("0 invisible, 1 opaque.", "0 invisibile, 1 opaco.")),
];

var elements = new Dictionary<string, Prop[]>
{
    ["group"] =
    [
        new("position", K.Point, Default: "[0, 0]", Doc: new("Offset of the children: their coordinates (gradients included) are relative to it.", "Spostamento dei figli: le loro coordinate (gradienti compresi) sono relative a questo punto.")),
        new("children", K.Elements, Default: "[]", Doc: new("Elements of the group, drawn in order.", "Elementi del gruppo, disegnati in ordine.")),
    ],
    ["image"] =
    [
        new("rect", K.Rect, Required: true, Doc: new("The rectangle the image is placed in.", "Il rettangolo in cui si colloca l'immagine.")),
        new("image", K.ImageSource, Required: true, Doc: new("Which image: see *Images and fonts*.", "Quale immagine: vedi *Immagini e font*.")),
        new("fit", K.Enum, typeof(ImageFit), Default: "none", Doc: new("`none` original size; `stretch` distorted to fill the rectangle; `contain` whole, aspect ratio kept; `cover` fills the rectangle, cropped; `scale` original size times `scale`.", "`none` dimensione originale; `stretch` deformata per riempire il rettangolo; `contain` intera, proporzioni mantenute; `cover` riempie il rettangolo, ritagliata; `scale` dimensione originale per `scale`.")),
        new("scale", K.Number, Min: 0.01f, Max: 100, Default: "1", Doc: new("Only with `fit: scale` (an error otherwise): `2` double, `0.5` half.", "Solo con `fit: scale` (altrimenti è un errore): `2` doppia, `0.5` metà.")),
        new("anchor", K.Enum, typeof(Anchor), Default: "center", Doc: new("Where the image sits inside the rectangle.", "Dove si colloca l'immagine nel rettangolo.")),
        new("flip", K.Enum, typeof(Flip), Default: "none", Doc: new("Mirrors the image.", "Rispecchia l'immagine.")),
        new("smooth", K.Bool, Doc: new("Absent: smoothed in highResolution. `false`: enlarges pixel art without smoothing. In pixelPerfect images are never smoothed.", "Assente: sfumata in highResolution. `false`: ingrandisce la pixel art senza sfumare. In pixelPerfect le immagini non sono mai sfumate.")),
        new("overflow", K.Enum, typeof(ImageOverflow), Default: "none", Doc: new("`none`: drawn whole, also outside the rectangle; `crop`: only the part inside it. With `fit: cover` the image is always cropped.", "`none`: disegnata intera, anche fuori dal rettangolo; `crop`: solo la parte dentro. Con `fit: cover` è sempre ritagliata.")),
        new("language", K.Language, Doc: new("Forces the language of a localized image set (`{language}` in its pattern).", "Forza la lingua di un set di immagini localizzato (`{language}` nel pattern).")),
    ],
    ["shape"] =
    [
        new("rect", K.Rect, Required: true, Doc: new("The rectangle of the shape.", "Il rettangolo della forma.")),
        new("shape", K.Enum, typeof(ShapeKind), Default: "rect", Doc: new("`ellipse` is the ellipse inscribed in the rectangle.", "`ellipse` è l'ellisse inscritta nel rettangolo.")),
        new("radius", K.Number, Min: 0, Default: "0", Doc: new("Rounded corners (rectangles only).", "Angoli arrotondati (solo rettangoli).")),
        new("fill", K.Fill, Doc: new("See *Fill*. A shape needs `fill`, `stroke` or both.", "Vedi *Riempimento*. Una forma ha bisogno di `fill`, `stroke` o entrambi.")),
        new("stroke", K.Stroke, Doc: new("The border, drawn inside the rectangle: see *Stroke*.", "Il bordo, disegnato dentro il rettangolo: vedi *Bordo*.")),
    ],
    ["line"] =
    [
        new("from", K.Point, Required: true, Doc: new("Start of the line.", "Inizio della linea.")),
        new("to", K.Point, Required: true, Doc: new("End of the line.", "Fine della linea.")),
        new("color", K.Color, Required: true),
        new("width", K.Number, Min: 0.01f, Default: "1", Doc: new("Thickness in pixels. With an odd width the coordinates are pixels, both ends included.", "Spessore in pixel. Con uno spessore dispari le coordinate sono pixel, estremi compresi.")),
    ],
    ["text"] =
    [
        new("rect", K.Rect, Required: true, Doc: new("The rectangle the text is laid out in. Texts do not wrap: lines break only at `\\n`.", "Il rettangolo in cui si dispone il testo. I testi non vanno a capo da soli: solo con `\\n`.")),
        new("text", K.TextTemplate, Required: true, Doc: new("The text, with bindings; `\\n` breaks the line.", "Il testo, con i binding; `\\n` va a capo.")),
        new("font", K.String, Required: true, Doc: new("A font: `\"{name}\"` of `values.font`, a file path, or `\"system:Family name\"` for an installed font; see *Images and fonts*.", "Un font: `\"{nome}\"` di `values.font`, il percorso di un file, oppure `\"system:Nome famiglia\"` per un font installato; vedi *Immagini e font*.")),
        new("size", K.Number, Required: true, Min: 0.5f, Max: 1000, Doc: new("Size in pixels.", "Dimensione in pixel.")),
        new("color", K.Color, Default: "#000000"),
        new("align", K.Enum, typeof(HorizontalAlign), Default: "left"),
        new("valign", K.Enum, typeof(VerticalAlign), Default: "top"),
        new("lineHeight", K.Number, Min: 0.5f, Max: 1000, Doc: new("Distance between lines, in pixels. Absent: from the font.", "Distanza fra le righe, in pixel. Assente: quella del font.")),
        new("language", K.Language, Doc: new("Forces the language of the translated values in this text.", "Forza la lingua dei valori tradotti di questo testo.")),
        new("overflow", K.Enum, typeof(TextOverflow), Default: "none", Doc: new("`none`: drawn whole, also outside the rectangle; `crop`: only the part inside it; `ellipsis`: each line that is too long is shortened and ends with `ellipsis`.", "`none`: disegnato intero, anche fuori dal rettangolo; `crop`: solo la parte dentro; `ellipsis`: ogni riga troppo lunga viene accorciata e finisce con `ellipsis`.")),
        new("ellipsis", K.String, Default: TextElement.DefaultEllipsis, Doc: new("Added to shortened lines, e.g. `...` or `…`.", "Aggiunto alle righe accorciate, ad esempio `...` o `…`.")),
    ],
};

Prop[] canvas =
[
    new("width", K.Integer, Required: true, Min: 1, Max: 4096, Doc: new("Template width in pixels: every coordinate is in these pixels.", "Larghezza del template in pixel: ogni coordinata è in questi pixel.")),
    new("height", K.Integer, Required: true, Min: 1, Max: 4096, Doc: new("Template height in pixels.", "Altezza del template in pixel.")),
    new("mode", K.Enum, typeof(RenderMode), Default: "highResolution", Doc: new("`pixelPerfect`: sharp pixels, no smoothed edges (pixel art). `highResolution`: smoothed edges, sharp texts and images at any scale.", "`pixelPerfect`: pixel netti, senza bordi sfumati (pixel art). `highResolution`: bordi sfumati, testi e immagini nitidi a ogni scala.")),
    new("scale", K.Number, Min: 0.1f, Max: 16, Default: "1", Doc: new("Multiplier of the PNG: a 192×128 canvas with scale 4 gives a 768×512 PNG. A whole number in pixelPerfect.", "Moltiplicatore del PNG: un canvas 192×128 con scala 4 dà un PNG 768×512. Intero in pixelPerfect.")),
    new("background", K.Fill, Doc: new("See *Fill*. Absent: transparent.", "Vedi *Riempimento*. Assente: trasparente.")),
];

Prop[] root =
[
    new("$schema", K.String, Doc: new("Ignored by PokePic (useful for editors).", "Ignorata da PokePic (utile agli editor).")),
    new("id", K.String, Required: true, Doc: new("Identifier, without spaces; stored in exported cards.", "Identificativo, senza spazi; salvato nelle card esportate.")),
    new("version", K.Integer, Min: 1, Default: "1", Doc: new("The template's own version.", "La versione del template.")),
    new("name", K.String, Doc: new("Display name; also the first part of exported file names.", "Nome visualizzato; è anche la prima parte del nome dei file esportati.")),
    new("language", K.Language, Doc: new("Forces the language of the whole card.", "Forza la lingua di tutta la card.")),
    new("canvas", K.Canvas, Required: true),
    new("values", K.Values, Doc: new("Named values by kind (`text`, `int`, `float`, `bool`, `color`, `image`, `font`): see *Values*.", "Valori con un nome, per tipo (`text`, `int`, `float`, `bool`, `color`, `image`, `font`): vedi *Valori*.")),
    new("styles", K.Styles, Doc: new("See *Styles*.", "Vedi *Stili*.")),
    new("elements", K.Elements, Required: true, Doc: new("Layers, drawn in order: each over the previous ones.", "Livelli, disegnati in ordine: ognuno sopra i precedenti.")),
];

// ---------------------------------------------------------------- checks against the code

foreach (var (type, props) in elements)
{
    var expected = ElementProperties.Common.Concat(ElementProperties.Of(type)).Order().ToArray();
    var actual = common.Concat(props).Select(p => p.Name).Order().ToArray();
    if (!expected.SequenceEqual(actual))
        failures.Add($"{type}: schema properties [{string.Join(", ", actual)}] differ from ElementProperties [{string.Join(", ", expected)}].");
}
if (!ElementProperties.Types.Order().SequenceEqual(elements.Keys.Order()))
    failures.Add($"Element types differ from ElementProperties.Types: {string.Join(", ", ElementProperties.Types)}.");

// Minimal valid elements and canvas, used as the base of every probe.
var minimal = new Dictionary<string, JsonObject>
{
    ["group"] = new() { ["type"] = "group" },
    ["image"] = new() { ["type"] = "image", ["rect"] = new JsonArray(0, 0, 10, 10), ["image"] = "frame" },
    ["shape"] = new() { ["type"] = "shape", ["rect"] = new JsonArray(0, 0, 10, 10), ["fill"] = "#FFFFFF" },
    ["line"] = new() { ["type"] = "line", ["from"] = new JsonArray(0, 0), ["to"] = new JsonArray(10, 0), ["color"] = "#000000" },
    ["text"] = new() { ["type"] = "text", ["rect"] = new JsonArray(0, 0, 10, 10), ["text"] = "x", ["font"] = "f", ["size"] = 8 },
};

IReadOnlyList<PokePic.Core.TemplateIssue> Parse(JsonObject? element = null, JsonObject? canvasObj = null)
{
    var template = new JsonObject
    {
        ["id"] = "probe",
        ["canvas"] = canvasObj?.DeepClone() ?? new JsonObject { ["width"] = 10, ["height"] = 10 },
        ["elements"] = element is null ? new JsonArray() : new JsonArray(element.DeepClone()),
    };
    return TemplateParser.Parse(template.ToJsonString()).Issues;
}

void Expect(bool ok, string what, IReadOnlyList<PokePic.Core.TemplateIssue> issues)
{
    if (!ok)
        failures.Add($"{what}: {(issues.Count == 0 ? "accepted" : string.Join("; ", issues))}");
}

void Probe(string where, JsonObject baseObj, Prop p, Func<JsonObject, IReadOnlyList<PokePic.Core.TemplateIssue>> parse, string path)
{
    JsonObject With(JsonNode? value)
    {
        var o = (JsonObject)baseObj.DeepClone();
        o[p.Name] = value?.DeepClone();
        return o;
    }
    JsonObject Without()
    {
        var o = (JsonObject)baseObj.DeepClone();
        o.Remove(p.Name);
        return o;
    }
    bool Fails(IReadOnlyList<PokePic.Core.TemplateIssue> issues, string at) => issues.Any(i => i.Path == at || i.Path.StartsWith(at + ".", StringComparison.Ordinal) || i.Path.StartsWith(at + "[", StringComparison.Ordinal));

    if (p.Required)
    {
        var issues = parse(Without());
        Expect(issues.Any(i => i.Message.Contains($"\"{p.Name}\"", StringComparison.Ordinal)), $"{where}.{p.Name} must be required", issues);
    }
    var at = $"{path}.{p.Name}";
    switch (p.Kind)
    {
        case K.Enum:
            foreach (var value in EnumValues(p.EnumType!))
            {
                var issues = parse(With(value));
                Expect(!Fails(issues, at), $"{where}.{p.Name} must accept \"{value}\"", issues);
            }
            Expect(Fails(parse(With("notAValue")), at), $"{where}.{p.Name} must reject \"notAValue\"", parse(With("notAValue")));
            break;
        case K.Number or K.Integer:
            if (p.Min is { } min)
            {
                Expect(!Fails(parse(With(min)), at), $"{where}.{p.Name} must accept {min}", parse(With(min)));
                var below = p.Kind == K.Integer ? min - 1 : min - 0.001f;
                Expect(Fails(parse(With(below)), at), $"{where}.{p.Name} must reject {below}", parse(With(below)));
            }
            if (p.Max is { } max)
            {
                Expect(!Fails(parse(With(max)), at), $"{where}.{p.Name} must accept {max}", parse(With(max)));
                var above = p.Kind == K.Integer ? max + 1 : max + 0.001f;
                Expect(Fails(parse(With(above)), at), $"{where}.{p.Name} must reject {above}", parse(With(above)));
            }
            // Plain numbers are literals: a binding is refused.
            Expect(Fails(parse(With("{level}")), at), $"{where}.{p.Name} must reject a binding (literal only)", parse(With("{level}")));
            break;
        case K.BoolOrBinding:
            foreach (JsonNode ok in new JsonNode[] { true, false, "{flag}", "{!flag}" })
                Expect(!Fails(parse(With(ok.DeepClone())), at), $"{where}.{p.Name} must accept {ok.ToJsonString()}", parse(With(ok.DeepClone())));
            Expect(Fails(parse(With(5)), at), $"{where}.{p.Name} must reject 5", parse(With(5)));
            break;
        case K.Rect or K.Point:
            int n = p.Kind == K.Rect ? 4 : 2;
            var withBinding = new JsonArray([.. Enumerable.Range(0, n).Select(i => (JsonNode?)(i == 0 ? JsonValue.Create("{level}") : JsonValue.Create(1)))]);
            Expect(!Fails(parse(With(withBinding)), at), $"{where}.{p.Name} must accept a numeric binding as a component", parse(With(withBinding)));
            var wrongLength = new JsonArray([.. Enumerable.Range(0, n + 1).Select(_ => (JsonNode?)JsonValue.Create(1))]);
            Expect(Fails(parse(With(wrongLength)), at), $"{where}.{p.Name} must reject {n + 1} components", parse(With(wrongLength)));
            break;
        case K.Color:
            foreach (var ok in new[] { "#FFF", "#FFFA", "#FFFFFF", "#FFFFFFAA", "{type1.color}" })
                Expect(!Fails(parse(With(ok)), at), $"{where}.{p.Name} must accept \"{ok}\"", parse(With(ok)));
            Expect(Fails(parse(With("red")), at), $"{where}.{p.Name} must reject \"red\"", parse(With("red")));
            break;
        case K.Bool:
            Expect(Fails(parse(With("{flag}")), at), $"{where}.{p.Name} must reject a binding (literal only)", parse(With("{flag}")));
            break;
        case K.Language:
            Expect(!Fails(parse(With("zh-Hans")), at), $"{where}.{p.Name} must accept \"zh-Hans\"", parse(With("zh-Hans")));
            Expect(Fails(parse(With("italian")), at), $"{where}.{p.Name} must reject \"italian\"", parse(With("italian")));
            break;
    }
}

foreach (var (type, props) in elements)
{
    var baseObj = minimal[type];
    Expect(Parse(baseObj).Count == 0, $"minimal {type} must be valid", Parse(baseObj));
    // "scale" needs fit "scale" to be valid at all.
    foreach (var p in common.Concat(props).Where(p => p.Name != "type"))
    {
        var b = type == "image" && p.Name == "scale" ? With(baseObj, "fit", "scale") : baseObj;
        Probe(type, b, p, o => Parse(o), "elements[0]");
    }
    Expect(Parse(With(baseObj, "unknownProperty", 1)).Any(i => i.Message.Contains("Unknown property", StringComparison.Ordinal)), $"{type} must reject unknown properties", Parse(With(baseObj, "unknownProperty", 1)));
}
Expect(Parse(new JsonObject { ["type"] = "shape", ["rect"] = new JsonArray(0, 0, 1, 1) }).Count > 0, "shape without fill and stroke must be rejected", []);
Expect(Parse(With(minimal["image"], "scale", 2)).Count > 0, "image scale without fit scale must be rejected", []);

var minimalCanvas = new JsonObject { ["width"] = 10, ["height"] = 10 };
foreach (var p in canvas)
    Probe("canvas", minimalCanvas, p, o => Parse(canvasObj: o), "canvas");
Expect(Parse(canvasObj: new JsonObject { ["width"] = 10, ["height"] = 10, ["mode"] = "pixelPerfect", ["scale"] = 1.5 }).Count > 0, "pixelPerfect with a fractional scale must be rejected", []);

if (failures.Count > 0)
{
    Console.Error.WriteLine("The reference does not match the PokePic code:");
    foreach (var f in failures)
        Console.Error.WriteLine("  " + f);
    return 1;
}

// ---------------------------------------------------------------- data keys (from the Adapter, i.e. from PKHeX)

// A realistic Pokémon so the examples in data-keys.md are meaningful: two moves only, to show empty move slots.
var pk = new PK9
{
    Species = (ushort)Species.Charizard, CurrentLevel = 50, Nickname = "Drago", IsNicknamed = true,
    PID = 0x12345678, ID32 = 0x0001E240, Ability = (int)Ability.Blaze, Ball = (byte)Ball.Poke, HeldItem = 234,
    Nature = Nature.Modest, OriginalTrainerFriendship = 120, TeraTypeOriginal = MoveType.Dragon,
    Move1 = (ushort)Move.Flamethrower, Move1_PP = 15, Move2 = (ushort)Move.AirSlash, Move2_PP = 15,
    IV_HP = 31, IV_ATK = 0, IV_DEF = 31, IV_SPA = 31, IV_SPD = 31, IV_SPE = 31,
    EV_HP = 4, EV_SPA = 252, EV_SPE = 252,
};
var pack = PkmAdapter.ToDataPack(pk, "en", new NoSprites());
var keys = pack.Data.Values.Keys.ToArray();
foreach (var key in keys.Where(k => Describe(k, "en") is null))
    failures.Add($"The data key \"{key}\" has no description in Spec.Describe.");
if (failures.Count > 0)
{
    Console.Error.WriteLine("The reference does not match the PokePic code:");
    foreach (var f in failures)
        Console.Error.WriteLine("  " + f);
    return 1;
}

// ---------------------------------------------------------------- output

Directory.CreateDirectory(docsDir);
foreach (var lang in new[] { "en", "it" })
{
    var suffix = lang == "en" ? "" : "." + lang;
    File.WriteAllText(Path.Combine(docsDir, $"template-reference{suffix}.md"), Reference(lang, root, canvas, common, elements));
    File.WriteAllText(Path.Combine(docsDir, $"data-keys{suffix}.md"), DataKeys(lang, pack));
}
if (schemaOut is not null)
{
    Directory.CreateDirectory(schemaOut);
    File.WriteAllText(Path.Combine(schemaOut, "template.schema.json"), JsonSchema(root, canvas, common, elements).ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    File.WriteAllText(Path.Combine(schemaOut, "data-keys.json"), DataKeysJson(pack));
}
Console.WriteLine($"Reference generated: {elements.Count} element types, {keys.Length} data keys; every rule checked against the PokePic parser.");
return 0;

static JsonObject With(JsonObject o, string name, JsonNode? value)
{
    var copy = (JsonObject)o.DeepClone();
    copy[name] = value?.DeepClone();
    return copy;
}

// ---------------------------------------------------------------- spec types and writers

enum K { String, Integer, Number, Bool, BoolOrBinding, Color, Rect, Point, Fill, Stroke, ImageSource, TextTemplate, Language, Enum, ElementType, Elements, Canvas, Values, Styles }

/// <summary>A text in English and Italian.</summary>
record Tx(string En, string It)
{
    public string In(string lang) => lang == "it" ? It : En;
}

record Prop(string Name, K Kind, Type? EnumType = null, bool Required = false, float? Min = null, float? Max = null, string? Default = null, Tx? Doc = null);

sealed class NoSprites : ISpriteSource
{
    public byte[]? Species(PKM pkm) => null;
    public byte[]? Ball(PKM pkm) => null;
    public byte[]? HeldItem(PKM pkm) => null;
}

static class Spec
{
    public static IEnumerable<string> EnumValues(Type t) => Enum.GetNames(t).Select(n => char.ToLowerInvariant(n[0]) + n[1..]); // same rule as the parser's ObjectReader.CamelCase (probed below)

    const string BindingPattern = @"^\{!?[A-Za-z0-9._-]+(\[[A-Za-z0-9._-]+\])?(:[^{}]+)?\}$";
    const string ColorPattern = @"^(#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{4}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})|\{[A-Za-z0-9._-]+(\[[A-Za-z0-9._-]+\])?\})$";

    static JsonObject Ref(string name) => new() { ["$ref"] = $"#/$defs/{name}" };

    static JsonObject TypeOf(Prop p) => p.Kind switch
    {
        K.String => new() { ["type"] = "string" },
        K.Integer => Range(new JsonObject { ["type"] = "integer" }, p),
        K.Number => Range(new JsonObject { ["type"] = "number" }, p),
        K.Bool => new() { ["type"] = "boolean" },
        K.BoolOrBinding => Ref("boolOrBinding"),
        K.Color => Ref("color"),
        K.Rect => Ref("rect"),
        K.Point => Ref("point"),
        K.Fill => Ref("fill"),
        K.Stroke => Ref("stroke"),
        K.ImageSource => Ref("imageSource"),
        K.TextTemplate => new() { ["type"] = "string" },
        K.Language => Ref("language"),
        K.Enum => new() { ["enum"] = new JsonArray([.. EnumValues(p.EnumType!).Select(v => (JsonNode?)v)]) },
        K.ElementType => new() { ["enum"] = new JsonArray([.. ElementProperties.Types.Select(v => (JsonNode?)v)]) },
        K.Elements => new() { ["type"] = "array", ["items"] = Ref("element") },
        K.Canvas => Ref("canvas"),
        K.Values => Ref("values"),
        K.Styles => new() { ["type"] = "object", ["additionalProperties"] = Ref("style") },
        _ => throw new InvalidOperationException(p.Kind.ToString()),
    };

    static JsonObject Range(JsonObject o, Prop p)
    {
        if (p.Min is { } min) o["minimum"] = min;
        if (p.Max is { } max) o["maximum"] = max;
        return o;
    }

    static JsonObject Described(Prop p)
    {
        var o = TypeOf(p);
        var text = string.Join(" ", new[] { p.Doc?.En, p.Default is null ? null : $"Default: {p.Default}." }.Where(s => s is not null));
        if (text.Length > 0)
            o["description"] = text;
        return o;
    }

    static JsonObject Object(IEnumerable<Prop> props, bool closed = true)
    {
        var properties = new JsonObject();
        foreach (var p in props)
            properties[p.Name] = Described(p);
        var o = new JsonObject { ["type"] = "object", ["properties"] = properties };
        var required = props.Where(p => p.Required).Select(p => (JsonNode?)p.Name).ToArray();
        if (required.Length > 0)
            o["required"] = new JsonArray(required);
        if (closed)
            o["additionalProperties"] = false;
        return o;
    }

    /// <summary>
    /// "values": a section per kind; every value is single, by language, a set of "items" or a "pattern" (texts, images,
    /// fonts). Colors, numbers and true/false are a literal or a reference; texts and paths are composed.
    /// </summary>
    static JsonObject Values()
    {
        static JsonObject Any(params JsonNode[] options) => new() { ["anyOf"] = new JsonArray(options) };
        JsonObject Expr(string section) => section switch
        {
            "int" => Any(new JsonObject { ["type"] = "integer" }, new JsonObject { ["type"] = "string", ["pattern"] = BindingPattern }, Ref("lookup")),
            "float" => Any(new JsonObject { ["type"] = "number" }, new JsonObject { ["type"] = "string", ["pattern"] = BindingPattern }, Ref("lookup")),
            "bool" => Any(new JsonObject { ["type"] = "boolean" }, new JsonObject { ["type"] = "string", ["pattern"] = BindingPattern }, Ref("lookup")),
            "color" => Any(Ref("color"), Ref("lookup")),
            _ => Any(new JsonObject { ["type"] = "string" }, Ref("lookup")),
        };
        JsonObject Body(string section) => Any(Expr(section), new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("en"),
            ["propertyNames"] = new JsonObject { ["pattern"] = "^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$" },
            ["additionalProperties"] = Expr(section),
            ["description"] = "By language: language code -> value; \"en\" required, used when a language is missing.",
        });
        JsonObject Definition(string section)
        {
            var options = new List<JsonNode>
            {
                Body(section),
                new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("items"),
                    ["additionalProperties"] = false,
                    ["description"] = "A set: a value per key, used as \"{name[key]}\".",
                    ["properties"] = new JsonObject
                    {
                        ["items"] = new JsonObject { ["type"] = "object", ["minProperties"] = 1, ["propertyNames"] = new JsonObject { ["pattern"] = "^[A-Za-z0-9_-]+(/[A-Za-z0-9_-]+)*$" }, ["additionalProperties"] = Body(section) },
                        ["fallback"] = Body(section),
                    },
                },
            };
            if (section is "text" or "image" or "font")
            {
                options.Add(new JsonObject
                {
                    ["type"] = "object",
                    ["required"] = new JsonArray("pattern"),
                    ["additionalProperties"] = false,
                    ["description"] = "A set computed from the key: \"{key}\" in the pattern, e.g. \"artwork/{key}.png\".",
                    ["properties"] = new JsonObject { ["pattern"] = Body(section), ["fallback"] = Body(section) },
                });
            }
            return new JsonObject { ["anyOf"] = new JsonArray([.. options]) };
        }

        var sections = new JsonObject();
        foreach (var section in new[] { "text", "int", "float", "bool", "color", "image", "font" })
        {
            sections[section] = new JsonObject
            {
                ["type"] = "object",
                ["propertyNames"] = new JsonObject { ["pattern"] = "^[A-Za-z0-9._-]+$" },
                ["additionalProperties"] = Definition(section),
            };
        }
        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["description"] = "Named values by kind, used like data keys: \"{name}\", \"{set[key]}\". Names are unique across the sections.",
            ["properties"] = sections,
        };
    }

    public static JsonObject JsonSchema(Prop[] root, Prop[] canvas, Prop[] common, Dictionary<string, Prop[]> elements)
    {
        var defs = new JsonObject
        {
            ["binding"] = new JsonObject { ["type"] = "string", ["pattern"] = BindingPattern, ["description"] = "A single binding: {key}, {key:format} or {!key}." },
            ["boolOrBinding"] = new JsonObject { ["oneOf"] = new JsonArray(new JsonObject { ["type"] = "boolean" }, Ref("binding")) },
            ["numberOrBinding"] = new JsonObject { ["oneOf"] = new JsonArray(new JsonObject { ["type"] = "number" }, Ref("binding")) },
            ["color"] = new JsonObject { ["type"] = "string", ["pattern"] = ColorPattern, ["description"] = "#RGB, #RGBA, #RRGGBB, #RRGGBBAA or a color binding (no format, no negation)." },
            ["rect"] = new JsonObject { ["type"] = "array", ["items"] = Ref("numberOrBinding"), ["minItems"] = 4, ["maxItems"] = 4, ["description"] = "[x, y, width, height]; each a number or a numeric binding." },
            ["point"] = new JsonObject { ["type"] = "array", ["items"] = Ref("numberOrBinding"), ["minItems"] = 2, ["maxItems"] = 2, ["description"] = "[x, y]; each a number or a numeric binding." },
            ["language"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})*$", ["description"] = "Language code: en, it, ja, zh-Hans, es-419…" },
            ["fill"] = new JsonObject
            {
                ["oneOf"] = new JsonArray(
                    Ref("color"),
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["required"] = new JsonArray("linear"),
                        ["additionalProperties"] = false,
                        ["properties"] = new JsonObject
                        {
                            ["linear"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["required"] = new JsonArray("from", "to", "stops"),
                                ["additionalProperties"] = false,
                                ["properties"] = new JsonObject
                                {
                                    ["from"] = Ref("point"),
                                    ["to"] = Ref("point"),
                                    ["stops"] = new JsonObject
                                    {
                                        ["type"] = "array",
                                        ["minItems"] = 2,
                                        ["description"] = "[[offset 0..1, color], ...], offsets increasing.",
                                        ["items"] = new JsonObject
                                        {
                                            ["type"] = "array",
                                            ["prefixItems"] = new JsonArray(new JsonObject { ["type"] = "number", ["minimum"] = 0, ["maximum"] = 1 }, Ref("color")),
                                            ["minItems"] = 2,
                                            ["maxItems"] = 2,
                                        },
                                    },
                                },
                            },
                        },
                    }),
            },
            ["stroke"] = new JsonObject
            {
                ["oneOf"] = new JsonArray(
                    Ref("color"),
                    new JsonObject
                    {
                        ["type"] = "object",
                        ["required"] = new JsonArray("color"),
                        ["additionalProperties"] = false,
                        ["properties"] = new JsonObject { ["color"] = Ref("color"), ["width"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["description"] = "Default: 1." } },
                    }),
            },
            ["lookup"] = new JsonObject
            {
                ["type"] = "object",
                ["required"] = new JsonArray("set", "key"),
                ["additionalProperties"] = false,
                ["description"] = "The item of a set: the first key the set has (for a pattern of files, whose file exists), then the set's fallback.",
                ["properties"] = new JsonObject
                {
                    ["set"] = new JsonObject { ["type"] = "string" },
                    ["key"] = new JsonObject { ["oneOf"] = new JsonArray(new JsonObject { ["type"] = "string" }, new JsonObject { ["type"] = "array", ["minItems"] = 1, ["items"] = new JsonObject { ["type"] = "string" } }) },
                },
            },
            ["imageSource"] = new JsonObject
            {
                ["description"] = "\"{name}\" (an image of values.image, or of the data pack), \"{set[key]}\", a file path (can be composed: \"{BASE}/{species}.png\"), or { set, key }.",
                ["oneOf"] = new JsonArray(new JsonObject { ["type"] = "string", ["minLength"] = 1 }, Ref("lookup")),
            },
            ["values"] = Values(),
            ["canvas"] = Object(canvas),
        };

        // Styles: optional "type", then properties of that element type (or of any element without "type").
        // The same name can have different values in different types (overflow): an untyped style accepts any of them.
        static bool Styleable(Prop p) => !ElementProperties.NotInStyles.Contains(p.Name) && p.Name != "type";
        var styleProps = new JsonObject { ["type"] = new JsonObject { ["enum"] = new JsonArray([.. ElementProperties.Types.Select(v => (JsonNode?)v)]) } };
        foreach (var group in common.Concat(elements.Values.SelectMany(v => v)).Where(Styleable).GroupBy(p => p.Name))
        {
            var variants = group.Select(Described).DistinctBy(s => s.ToJsonString()).ToArray();
            styleProps[group.Key] = variants.Length == 1 ? variants[0] : new JsonObject { ["anyOf"] = new JsonArray([.. variants]) };
        }
        var typedStyles = new JsonArray();
        foreach (var (type, props) in elements)
        {
            var own = new JsonObject();
            foreach (var p in common.Concat(props).Where(Styleable))
                own[p.Name] = Described(p);
            typedStyles.Add(new JsonObject
            {
                ["if"] = new JsonObject { ["properties"] = new JsonObject { ["type"] = new JsonObject { ["const"] = type } }, ["required"] = new JsonArray("type") },
                ["then"] = new JsonObject
                {
                    ["properties"] = own,
                    ["propertyNames"] = new JsonObject { ["enum"] = new JsonArray([.. own.Select(kv => (JsonNode?)kv.Key).Prepend("type")]) },
                },
            });
        }
        defs["style"] = new JsonObject { ["type"] = "object", ["properties"] = styleProps, ["additionalProperties"] = false, ["allOf"] = typedStyles };

        // Elements: one definition per type. Required properties are required only without "style",
        // because a style can provide them.
        var conditions = new JsonArray();
        foreach (var (type, props) in elements)
        {
            var all = common.Concat(props).ToArray();
            var o = Object(all.Select(p => p with { Required = false }));
            ((JsonObject)o["properties"]!)["type"] = new JsonObject { ["const"] = type };
            o["required"] = new JsonArray("type");
            var required = all.Where(p => p.Required && p.Name != "type").Select(p => (JsonNode?)p.Name).ToArray();
            if (required.Length > 0)
                o["if"] = new JsonObject { ["not"] = new JsonObject { ["required"] = new JsonArray("style") } };
            if (required.Length > 0)
                o["then"] = new JsonObject { ["required"] = new JsonArray(required) };
            defs[type] = o;
            conditions.Add(new JsonObject
            {
                ["if"] = new JsonObject { ["properties"] = new JsonObject { ["type"] = new JsonObject { ["const"] = type } }, ["required"] = new JsonArray("type") },
                ["then"] = Ref(type),
            });
        }
        defs["element"] = new JsonObject
        {
            ["type"] = "object",
            ["required"] = new JsonArray("type"),
            ["properties"] = new JsonObject { ["type"] = new JsonObject { ["enum"] = new JsonArray([.. ElementProperties.Types.Select(v => (JsonNode?)v)]) } },
            ["allOf"] = conditions,
        };

        var schema = Object(root);
        schema = new JsonObject
        {
            ["$schema"] = "https://json-schema.org/draft/2020-12/schema",
            ["$id"] = "https://pokepic/template.schema.json",
            ["title"] = "PokePic template (template.json)",
            ["description"] = "Generated from the PokePic code by build/generate-reference.cs in the PokePic repository. The PokePic CLI is the authority: a template is valid only when \"pokepic check\" and \"pokepic render\" succeed.",
            ["type"] = "object",
            ["properties"] = schema["properties"]!.DeepClone(),
            ["required"] = schema["required"]!.DeepClone(),
            ["additionalProperties"] = false,
            ["$defs"] = defs,
        };
        return schema;
    }

    static string Number(float? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "…";

    static string Cell(Prop p, string lang)
    {
        string T(string en, string it) => lang == "it" ? it : en;
        return p.Kind switch
        {
            K.Enum => string.Join(" \\| ", EnumValues(p.EnumType!).Select(v => $"`{v}`")),
            K.ElementType => string.Join(" \\| ", ElementProperties.Types.Select(v => $"`{v}`")),
            K.Number or K.Integer => (p.Kind == K.Integer ? T("integer", "intero") : T("number", "numero"))
                + (p.Min is not null || p.Max is not null ? $" {Number(p.Min)}–{Number(p.Max)}" : "")
                + T(", fixed value", ", valore fisso"),
            K.BoolOrBinding => "`true` \\| `false` \\| `\"{bool}\"` \\| `\"{!bool}\"`",
            K.Bool => T("`true` \\| `false`, fixed value", "`true` \\| `false`, valore fisso"),
            K.Color => T("color or binding to a color", "colore o binding a un colore"),
            K.Rect => T("`[x, y, width, height]`: numbers or numeric bindings", "`[x, y, larghezza, altezza]`: numeri o binding numerici"),
            K.Point => T("`[x, y]`: numbers or numeric bindings", "`[x, y]`: numeri o binding numerici"),
            K.Fill => T("color or gradient", "colore o gradiente"),
            K.Stroke => T("color, or `{ \"color\", \"width\" }`", "colore, oppure `{ \"color\", \"width\" }`"),
            K.ImageSource => T("`\"{name}\"`, a path, or `{ \"set\", \"key\" }`", "`\"{nome}\"`, un percorso, oppure `{ \"set\", \"key\" }`"),
            K.TextTemplate => T("text with bindings", "testo con binding"),
            K.Language => T("language code (`en`, `it`, `ja`, `zh-Hans`…)", "codice di lingua (`en`, `it`, `ja`, `zh-Hans`…)"),
            K.Elements => T("array of elements", "array di elementi"),
            K.Canvas => T("object, see `canvas`", "oggetto, vedi `canvas`"),
            K.Values or K.Styles => T("object", "oggetto"),
            _ => T("text", "testo"),
        };
    }

    static void Table(StringBuilder sb, IEnumerable<Prop> props, string lang)
    {
        sb.AppendLine(lang == "it" ? "| Proprietà | Obbligatoria | Valori | Predefinito | Note |" : "| Property | Required | Values | Default | Notes |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var p in props)
            sb.AppendLine($"| `{p.Name}` | {(p.Required ? (lang == "it" ? "sì" : "yes") : "")} | {Cell(p, lang)} | {(p.Default is null ? "" : $"`{p.Default}`")} | {p.Doc?.In(lang).Replace("|", "\\|")} |");
        sb.AppendLine();
    }

    static string Switcher(string name, string lang) => lang == "it"
        ? $"[English]({name}.md) | **Italiano**"
        : $"**English** | [Italiano]({name}.it.md)";

    public static string Reference(string lang, Prop[] root, Prop[] canvas, Prop[] common, Dictionary<string, Prop[]> elements)
    {
        string T(string en, string it) => lang == "it" ? it : en;
        var sb = new StringBuilder();
        sb.AppendLine(T("# Template reference", "# Riferimento del template"));
        sb.AppendLine();
        sb.AppendLine(Switcher("template-reference", lang));
        sb.AppendLine();
        sb.AppendLine(T(
            "Every property of `template.json`, with its values, limits and default. How a template works is explained in the [guide](guide.md); the data a template can use is in [data keys](data-keys.md).",
            "Tutte le proprietà di `template.json`, con valori, limiti e valore predefinito. Come funziona un template lo spiega la [guida](guide.it.md); i dati che un template può usare sono nelle [chiavi dei dati](data-keys.it.md)."));
        sb.AppendLine();
        sb.AppendLine(T(
            "*Generated from the PokePic code by `build/generate-reference.cs`, which checks every rule below against PokePic's parser: do not edit by hand.*",
            "*Generato dal codice di PokePic da `build/generate-reference.cs`, che verifica ogni regola con il parser di PokePic: non modificarlo a mano.*"));
        sb.AppendLine();
        sb.AppendLine(T(
            "An unknown property is an error. A property set to `null` counts as absent. Values in a list (`left`, `center`…) are case-insensitive. A *fixed value* cannot be a binding.",
            "Una proprietà sconosciuta è un errore. Una proprietà a `null` vale come assente. I valori di un elenco (`left`, `center`…) non distinguono maiuscole e minuscole. Un *valore fisso* non può essere un binding."));
        sb.AppendLine();
        sb.AppendLine(T("## Template (`template.json`)", "## Template (`template.json`)"));
        sb.AppendLine();
        Table(sb, root, lang);
        sb.AppendLine("## `canvas`");
        sb.AppendLine();
        Table(sb, canvas, lang);
        sb.AppendLine(T("## Properties of every element", "## Proprietà di ogni elemento"));
        sb.AppendLine();
        Table(sb, common, lang);
        foreach (var (type, props) in elements)
        {
            sb.AppendLine($"## `{type}`");
            sb.AppendLine();
            Table(sb, props, lang);
        }
        sb.AppendLine(T("## Values", "## Valori"));
        sb.AppendLine();
        sb.AppendLine(T(
            "- **Color**: `\"#RGB\"`, `\"#RGBA\"`, `\"#RRGGBB\"`, `\"#RRGGBBAA\"` (the last pair is the transparency: `00` invisible, `FF` opaque), or a binding to a color (`\"{type1.color}\"`, without format or `!`). Color names are not accepted.",
            "- **Colore**: `\"#RGB\"`, `\"#RGBA\"`, `\"#RRGGBB\"`, `\"#RRGGBBAA\"` (l'ultima coppia è la trasparenza: `00` invisibile, `FF` opaco), oppure un binding a un colore (`\"{type1.color}\"`, senza formato né `!`). I nomi dei colori non sono accettati."));
        sb.AppendLine(T(
            "- **Fill**: a color, or a linear gradient `{ \"linear\": { \"from\": [x, y], \"to\": [x, y], \"stops\": [[0, color], [1, color]] } }`: at least two stops, offsets from 0 to 1 in increasing order. Gradient coordinates are those of the canvas (of the group, inside a group).",
            "- **Riempimento** (`fill`): un colore, oppure un gradiente lineare `{ \"linear\": { \"from\": [x, y], \"to\": [x, y], \"stops\": [[0, colore], [1, colore]] } }`: almeno due tappe, posizioni da 0 a 1 in ordine crescente. Le coordinate del gradiente sono quelle del canvas (del gruppo, dentro un gruppo)."));
        sb.AppendLine(T(
            "- **Stroke**: a color (width 1), or `{ \"color\": color, \"width\": number ≥ 0 }`. It is drawn inside the rectangle.",
            "- **Bordo** (`stroke`): un colore (spessore 1), oppure `{ \"color\": colore, \"width\": numero ≥ 0 }`. È disegnato dentro il rettangolo."));
        sb.AppendLine(T(
            "- **Values** (`values.<kind>.<name>`, kinds `text`, `int`, `float`, `bool`, `color`, `image`, `font`): used like data keys, `\"{name}\"`; names may contain letters, digits, `.`, `_` and `-`, and are unique across the kinds. Every value is written in one of four forms: single (`\"#F08030\"`, `6`, `\"fonts/a.ttf\"`); by language (`{ \"en\": \"Lv.\", \"it\": \"Liv.\" }`, `en` required, chosen like the data's); a set (`{ \"items\": { \"fire\": …, \"water\": … }, \"fallback\": … }`, each item single or by language); a pattern, for texts, images and fonts only (`{ \"pattern\": \"artwork/{key}.png\", \"fallback\": … }`). Colors, numbers and true/false take a literal or a reference of their kind (`\"{type1.color}\"`, `\"{!hasType2}\"`, `\"{typeColor[type1.key]}\"`; a whole number fits a decimal); texts, images and fonts are composed of texts and numbers (`\"{species.name} Lv. {level}\"`, `\"{BASE}/{species:D4}.png\"`), or are a lone reference of their kind (`\"{frame}\"`, `\"{species.sprite}\"`). `{language}` is always the language of the element. A cycle, an unknown key, a wrong kind or a name equal to a data key is an error.",
            "- **Valori** (`values.<tipo>.<nome>`, tipi `text`, `int`, `float`, `bool`, `color`, `image`, `font`): si usano come le chiavi dei dati, `\"{nome}\"`; i nomi possono contenere lettere, cifre, `.`, `_` e `-`, e sono unici fra i tipi. Ogni valore si scrive in una di quattro forme: singolo (`\"#F08030\"`, `6`, `\"fonts/a.ttf\"`); per lingua (`{ \"en\": \"Lv.\", \"it\": \"Liv.\" }`, `en` obbligatorio, scelta come per i dati); set (`{ \"items\": { \"fire\": …, \"water\": … }, \"fallback\": … }`, ogni elemento singolo o per lingua); pattern, solo per testi, immagini e font (`{ \"pattern\": \"artwork/{key}.png\", \"fallback\": … }`). Colori, numeri e vero/falso accettano un valore o un riferimento del loro tipo (`\"{type1.color}\"`, `\"{!hasType2}\"`, `\"{typeColor[type1.key]}\"`; un intero va bene dove serve un decimale); testi, immagini e font si compongono di testi e numeri (`\"{species.name} Lv. {level}\"`, `\"{BASE}/{species:D4}.png\"`), oppure sono un riferimento del loro tipo (`\"{frame}\"`, `\"{species.sprite}\"`). `{language}` è sempre la lingua dell'elemento. Un ciclo, una chiave sconosciuta, un tipo sbagliato o un nome uguale a una chiave dei dati sono errori."));
        sb.AppendLine(T(
            "- **Sets**: `\"{set[key]}\"` takes the value of `key` and picks that item (`\"{typeColor[move1.type.key]}\"`); `{ \"set\": \"artwork\", \"key\": [\"{species}-{form}\", \"{species}\"] }` also takes a formatted key or candidates tried in order. The first key the set has wins (for a pattern of images or fonts, the first whose file exists), then the `fallback`; without it, a key with nothing is an error when the card is made. A key contains letters, digits, `-` and `_`, in parts separated by `/` (`shiny/female/3`): it cannot leave the folder of a pattern. Files of a pattern are not listed (a set can hold thousands of artworks).",
            "- **Set**: `\"{set[chiave]}\"` prende il valore di `chiave` e sceglie quell'elemento (`\"{typeColor[move1.type.key]}\"`); `{ \"set\": \"artwork\", \"key\": [\"{species}-{form}\", \"{species}\"] }` accetta anche una chiave con formato o candidati provati in ordine. Vince la prima chiave che il set ha (per un pattern di immagini o font, la prima il cui file esiste), poi il `fallback`; senza, una chiave senza elemento è un errore quando si crea la card. Una chiave contiene lettere, cifre, `-` e `_`, in parti separate da `/` (`shiny/female/3`): non può uscire dalla cartella di un pattern. I file di un pattern non sono elencati (un set può contenere migliaia di artwork)."));
        sb.AppendLine(T(
            "- **Images and fonts**: `\"{frame}\"` = a value of `values.image` or an image of the data pack (`\"{species.sprite}\"`); `\"{artwork[pokeapi.id]}\"` or `{ \"set\": …, \"key\": … }` = the item of a set; any other text is a file path, also composed (`\"badges/{type1.key}.png\"`). A font is `\"{name}\"` of `values.font`, a path, or `\"system:Family\"` (an installed font: the card then depends on the computer).",
            "- **Immagini e font**: `\"{frame}\"` = un valore di `values.image` o un'immagine del pacchetto dati (`\"{species.sprite}\"`); `\"{artwork[pokeapi.id]}\"` o `{ \"set\": …, \"key\": … }` = l'elemento di un set; ogni altro testo è il percorso di un file, anche composto (`\"badges/{type1.key}.png\"`). Un font è `\"{nome}\"` di `values.font`, un percorso, oppure `\"system:Famiglia\"` (un font installato: la card dipende allora dal computer)."));
        sb.AppendLine(T(
            "- **File paths**: relative to the template folder (they cannot leave it), `file:///C:/folder/file.png`, or `https://…` (downloaded once per session). A path with `{language}` tries the language, its base language and English, and takes the first file that exists. Fixed paths are checked when the template opens; paths that depend on the data when the card is made.",
            "- **Percorsi dei file**: relativi alla cartella del template (non possono uscirne), `file:///C:/cartella/file.png`, oppure `https://…` (scaricato una volta per sessione). Un percorso con `{language}` prova la lingua, la lingua base e l'inglese, e prende il primo file che esiste. I percorsi fissi si verificano all'apertura del template; quelli che dipendono dai dati quando si crea la card."));
        sb.AppendLine(T(
            "- **Styles** (`styles.<name>`): properties shared by elements, with an optional `\"type\"`. With `\"type\"` a style can set every property of that type of element except `name`, `style` and `children`; without it, only properties the element using it has. The element's own properties win; required properties can come from the style.",
            "- **Stili** (`styles.<nome>`): proprietà condivise fra elementi, con un `\"type\"` facoltativo. Con `\"type\"` uno stile può impostare ogni proprietà di quel tipo di elemento tranne `name`, `style` e `children`; senza, solo proprietà che l'elemento che lo usa possiede. Le proprietà dell'elemento hanno la precedenza; quelle obbligatorie possono arrivare dallo stile."));
        return sb.ToString();
    }

    static readonly Dictionary<string, Tx> Stats = new()
    {
        ["hp"] = new("HP", "PS"),
        ["atk"] = new("Attack", "Attacco"),
        ["def"] = new("Defense", "Difesa"),
        ["spa"] = new("Sp. Atk", "Attacco Speciale"),
        ["spd"] = new("Sp. Def", "Difesa Speciale"),
        ["spe"] = new("Speed", "Velocità"),
    };

    /// <summary>What a data key means, or null when this file does not know the key (generation then fails).</summary>
    public static string? Describe(string key, string lang)
    {
        string T(string en, string it) => lang == "it" ? it : en;
        switch (key)
        {
            case "pokeapi.id":
                return T("Name of the Pokémon's images in the PokeAPI sprites repository, for its form: `6`, `10034` (Mega Charizard X), `10103` (Alolan Vulpix), `666-tundra` (Vivillon).",
                         "Nome delle immagini del Pokémon nel repository degli sprite di PokeAPI, per la sua forma: `6`, `10034` (MegaCharizard X), `10103` (Vulpix di Alola), `666-tundra` (Vivillon).");
            case "pokeapi.path":
                return T("Path of the Pokémon's image relative to a PokeAPI image collection (`…/sprites/pokemon/other/home/`, `…/sprites/pokemon/`…), with the variants that exist: `shiny/`, `female/`, e.g. `shiny/female/3`. A key for an image set; a form without its own image gives the species'. The variants are checked in the HOME collection, the most complete: other collections may lack some images.",
                         "Percorso dell'immagine del Pokémon relativo a una raccolta di immagini di PokeAPI (`…/sprites/pokemon/other/home/`, `…/sprites/pokemon/`…), con le varianti che esistono: `shiny/`, `female/`, ad esempio `shiny/female/3`. Una chiave per un set di immagini; una forma senza immagine propria dà quella della specie. Le varianti sono verificate nella raccolta HOME, la più completa: altre raccolte possono non avere alcune immagini.");
            case "pokeapi.artwork":
                return T("The official artwork of PokeAPI (`…/sprites/pokemon/other/official-artwork/`), with `shiny/` when it has it (no female images), e.g. `shiny/10034`.",
                         "L'artwork ufficiale di PokeAPI (`…/sprites/pokemon/other/official-artwork/`), con `shiny/` quando c'è (senza immagini femminili), ad esempio `shiny/10034`.");
        }
        // pokeapi.type1.id … pokeapi.move4.type.id: PokeAPI's number of a type, described with the type's other keys.
        if (key.StartsWith("pokeapi.", StringComparison.Ordinal) && key.EndsWith(".id", StringComparison.Ordinal))
            key = key["pokeapi.".Length..^".id".Length] + ".id.pokeapi";
        var parts = key.Split('.');
        string StatName(string s) => Stats.TryGetValue(s, out var t) ? t.In(lang) : T("all six together", "tutte e sei insieme");

        // Types: typeN.*, teratype.*, moveN.type.*
        string? TypeOwner() => parts[0] switch
        {
            "type1" => T("first type", "primo tipo"),
            "type2" => T("second type", "secondo tipo"),
            "teratype" => T("Tera Type", "teratipo"),
            _ when parts[0].StartsWith("move", StringComparison.Ordinal) && parts.Length > 2 && parts[1] == "type" => T($"type of move {parts[0][4..]}", $"tipo della mossa {parts[0][4..]}"),
            _ => null,
        };
        if (TypeOwner() is { } owner)
        {
            var field = string.Join('.', parts.Skip(parts[0].StartsWith("move", StringComparison.Ordinal) ? 2 : 1));
            var note = parts[0] switch
            {
                "type2" => T(" With one type it repeats the first type.", " Con un solo tipo ripete il primo tipo."),
                "teratype" => T(" Without a Tera Type it repeats the first type.", " Senza teratipo ripete il primo tipo."),
                _ => "",
            };
            var text = field switch
            {
                "key" => T($"Identifier of the {owner}: English, lowercase (`fire`). For image sets.", $"Identificativo del {owner}: inglese, minuscolo (`fire`). Per i set di immagini."),
                "name" => T($"Name of the {owner}, translated.", $"Nome del {owner}, tradotto."),
                "color" => T($"Color of the {owner} (Scarlet/Violet colors).", $"Colore del {owner} (colori di Scarlatto/Violetto)."),
                "id" => T($"Number of the {owner} in PKHeX: normal 0 … fairy 17, stellar 18.", $"Numero del {owner} in PKHeX: normal 0 … fairy 17, stellar 18."),
                "id.pokeapi" => T($"Number of the {owner} in PokeAPI (`id` + 1): normal 1 … fairy 18, stellar 19.", $"Numero del {owner} in PokeAPI (`id` + 1): normal 1 … fairy 18, stellar 19."),
                _ => null,
            };
            return text is null ? null : text + note;
        }
        if (parts.Length == 2 && parts[0] is "iv" or "ev" or "stat" or "base")
        {
            var what = parts[0] switch
            {
                "iv" => T("IV (individual value)", "IV (valore individuale)"),
                "ev" => T("EV (effort value)", "EV (punti allenamento)"),
                "stat" => T("Stat at the current level", "Statistica al livello attuale"),
                _ => T("Base stat of the species", "Statistica base della specie"),
            };
            return parts[1] == "total" ? T($"{what}: sum of the six.", $"{what}: somma delle sei.") : $"{what}: {StatName(parts[1])}.";
        }
        if (parts.Length == 3 && parts[0] == "nature" && parts[1] is "up" or "down")
            return parts[1] == "up"
                ? T($"True if the nature raises {StatName(parts[2])}.", $"Vero se la natura aumenta {StatName(parts[2])}.")
                : T($"True if the nature lowers {StatName(parts[2])}.", $"Vero se la natura diminuisce {StatName(parts[2])}.");
        if (parts[0].StartsWith("move", StringComparison.Ordinal) && parts.Length == 2)
        {
            var n = parts[0][4..];
            return parts[1] switch
            {
                "name" => T($"Name of move {n}, translated; empty when the slot is empty.", $"Nome della mossa {n}, tradotto; vuoto se lo slot è vuoto."),
                "pp" => T($"Current PP of move {n} (the maximum is not in the data); 0 when the slot is empty.", $"PP attuali della mossa {n} (il massimo non è nei dati); 0 se lo slot è vuoto."),
                _ => null,
            };
        }
        if (key.StartsWith("hasMove", StringComparison.Ordinal))
            return T($"True if move slot {key[7..]} is not empty.", $"Vero se lo slot della mossa {key[7..]} non è vuoto.");
        return key switch
        {
            "species" => T("National Pokédex number.", "Numero del Pokédex nazionale."),
            "species.name" => T("Species name, translated.", "Nome della specie, tradotto."),
            "species.sprite" => T("Game sprite of the Pokémon (from PKHeX, about 68×56, pixel art).", "Sprite di gioco del Pokémon (da PKHeX, circa 68×56, pixel art)."),
            "form" => T("Form number: 0 for the base form.", "Numero della forma: 0 per la forma base."),
            "nickname" => T("Nickname, or the species name if it has none (not translated).", "Soprannome, o il nome della specie se non ne ha (non tradotto)."),
            "isNicknamed" => T("True if the Pokémon has a nickname.", "Vero se il Pokémon ha un soprannome."),
            "level" => T("Level, 1 … 100.", "Livello, 1 … 100."),
            "friendship" => T("Friendship, 0 … 255.", "Amicizia, 0 … 255."),
            "isShiny" => T("True if the Pokémon is shiny.", "Vero se il Pokémon è cromatico."),
            "hasType2" => T("True if the Pokémon has two types.", "Vero se il Pokémon ha due tipi."),
            "hasTera" => T("True if the Pokémon has a Tera Type (Scarlet/Violet Pokémon only).", "Vero se il Pokémon ha un teratipo (solo Pokémon di Scarlatto/Violetto)."),
            "hasMega" => T("True if the species has a Mega Evolution and the Pokémon's game allows it (X/Y to Sun/Moon, Legends Z-A).", "Vero se la specie ha una megaevoluzione e il gioco del Pokémon la permette (da X/Y a Sole/Luna, Leggende Z-A)."),
            "gender.key" => T("Gender: `male`, `female` or `neutral`.", "Sesso: `male`, `female` o `neutral`."),
            "gender.symbol" => T("`♂`, `♀`, or empty when genderless.", "`♂`, `♀`, o vuoto se senza sesso."),
            "gender.isMale" => T("True if male.", "Vero se maschio."),
            "gender.isFemale" => T("True if female.", "Vero se femmina."),
            "gender.isNeutral" => T("True if genderless.", "Vero se senza sesso."),
            "nature.name" => T("Nature, translated.", "Natura, tradotta."),
            "ability.name" => T("Ability, translated.", "Abilità, tradotta."),
            "heldItem.name" => T("Held item, translated; empty without an item.", "Strumento tenuto, tradotto; vuoto senza strumento."),
            "heldItem.sprite" => T("Image of the held item; a transparent pixel without an item.", "Immagine dello strumento tenuto; un pixel trasparente senza strumento."),
            "hasHeldItem" => T("True if the Pokémon holds an item.", "Vero se il Pokémon tiene uno strumento."),
            "ball.name" => T("Poké Ball it was caught in, translated.", "Poké Ball in cui è stato catturato, tradotta."),
            "ball.sprite" => T("Image of the ball.", "Immagine della ball."),
            _ => null,
        };
    }

    static string Example(CardValue v, string lang) => v switch
    {
        LocalizedValue l => l.GetText(lang).Length == 0 ? "`\"\"`" : $"`{l.GetText(lang)}`",
        ImageValue => "PNG",
        TextValue t => t.Value.Length == 0 ? "`\"\"`" : $"`{t.Value}`",
        _ => $"`{v.ToText()}`",
    };

    static string Kind(CardValue v, string lang) => (v, lang) switch
    {
        (LocalizedValue, "it") => "testo tradotto",
        (LocalizedValue, _) => "translated text",
        (ImageValue, "it") => "immagine",
        (TextValue, "it") => "testo",
        (IntValue, "it") => "intero",
        (FloatValue, "it") => "decimale",
        (BoolValue, "it") => "vero/falso",
        (ColorValue, "it") => "colore",
        (BoolValue, _) => "true/false",
        _ => v.Kind.ToString().ToLowerInvariant(),
    };

    public static string DataKeys(string lang, DataPack pack)
    {
        string T(string en, string it) => lang == "it" ? it : en;
        var sb = new StringBuilder();
        sb.AppendLine(T("# Data keys", "# Chiavi dei dati"));
        sb.AppendLine();
        sb.AppendLine(Switcher("data-keys", lang));
        sb.AppendLine();
        sb.AppendLine(T(
            "The values and images of a Pokémon that a template can use in bindings (`\"{level}\"`). Every data pack made by the PokePic plugin has all these keys, for every Pokémon, so a template that uses them works with any Pokémon. How bindings and formats work is explained in the [guide](guide.md).",
            "I valori e le immagini di un Pokémon che un template può usare nei binding (`\"{level}\"`). Ogni pacchetto dati creato dal plugin PokePic ha tutte queste chiavi, per ogni Pokémon, quindi un template che le usa funziona con qualunque Pokémon. Come funzionano binding e formati lo spiega la [guida](guide.it.md)."));
        sb.AppendLine();
        sb.AppendLine(T(
            "*Generated by `build/generate-reference.cs` from what PokePic.Adapter produces for a PKHeX Pokémon (the examples: Charizard Lv. 50 from Scarlet/Violet, nicknamed \"Drago\", with two moves): do not edit by hand.*",
            "*Generato da `build/generate-reference.cs` da ciò che PokePic.Adapter produce per un Pokémon di PKHeX (gli esempi: Charizard Lv. 50 di Scarlatto/Violetto, soprannominato \"Drago\", con due mosse): non modificarlo a mano.*"));
        sb.AppendLine();
        sb.AppendLine(T("| Key | Kind | Meaning | Example |", "| Chiave | Tipo | Significato | Esempio |"));
        sb.AppendLine("|---|---|---|---|");
        foreach (var (key, value) in pack.Data.Values)
            sb.AppendLine($"| `{key}` | {Kind(value, lang)} | {Describe(key, lang)} | {Example(value, lang)} |");

        // Domains: an image set keyed by data needs a file (or a fallback) for every value.
        var types = GameInfo.GetStrings("en").types.Select(s => s.ToLowerInvariant()).ToArray();
        var regular = types.Take(types.Length - 1).ToArray();
        sb.AppendLine();
        sb.AppendLine(T("## Possible values", "## Valori possibili"));
        sb.AppendLine();
        sb.AppendLine(T(
            "A set chosen by a key (`{set[key]}`) needs an item (a file or a value), or a `fallback`, for every value the key can take:",
            "Un set scelto da una chiave (`{set[chiave]}`) ha bisogno di un elemento (un file o un valore), o di un `fallback`, per ogni valore che la chiave può assumere:"));
        sb.AppendLine();
        sb.AppendLine(T(
            $"- `type1.key`, `type2.key`, `move1.type.key` … `move4.type.key`: {string.Join(", ", regular.Select(s => $"`{s}`"))} ({regular.Length} values).",
            $"- `type1.key`, `type2.key`, `move1.type.key` … `move4.type.key`: {string.Join(", ", regular.Select(s => $"`{s}`"))} ({regular.Length} valori)."));
        sb.AppendLine(T($"- `teratype.key`: the same, plus `{types[^1]}`.", $"- `teratype.key`: gli stessi, più `{types[^1]}`."));
        sb.AppendLine(T(
            $"- `*.id`: 0 … {regular.Length - 1} in the order above, {types.Length - 1} = `{types[^1]}` (Tera Type only). `pokeapi.*.id`: 1 … {types.Length}.",
            $"- `*.id`: 0 … {regular.Length - 1} nell'ordine qui sopra, {types.Length - 1} = `{types[^1]}` (solo teratipo). `pokeapi.*.id`: 1 … {types.Length}."));
        sb.AppendLine(T("- `gender.key`: `male`, `female`, `neutral`.", "- `gender.key`: `male`, `female`, `neutral`."));
        sb.AppendLine(T(
            $"- `species`: 1 … {(int)Species.MAX_COUNT - 1}. `form`: 0 for the base form, otherwise the form number in PKHeX (Mega forms included).",
            $"- `species`: 1 … {(int)Species.MAX_COUNT - 1}. `form`: 0 per la forma base, altrimenti il numero della forma in PKHeX (megaevoluzioni comprese)."));
        sb.AppendLine(T(
            "- `level` 1 … 100, `friendship` 0 … 255, `iv.*` 0 … 31 (`iv.total` up to 186), `ev.*` 0 … 252 (255 before X/Y; `ev.total` up to 510), `base.*` 1 … 255.",
            "- `level` 1 … 100, `friendship` 0 … 255, `iv.*` 0 … 31 (`iv.total` fino a 186), `ev.*` 0 … 252 (255 prima di X/Y; `ev.total` fino a 510), `base.*` 1 … 255."));
        sb.AppendLine();
        sb.AppendLine(T(
            "Empty slots keep their keys: without a third move `move3.name` is empty and `move3.type.*` is `normal`; `type2.*` repeats `type1.*` with one type; `teratype.*` repeats `type1.*` without a Tera Type; without an item `heldItem.sprite` is a transparent pixel. Use `hasMove3`, `hasType2`, `hasTera`, `hasHeldItem` in `visible` to hide those parts.",
            "Gli slot vuoti mantengono le loro chiavi: senza terza mossa `move3.name` è vuoto e `move3.type.*` è `normal`; con un solo tipo `type2.*` ripete `type1.*`; senza teratipo `teratype.*` ripete `type1.*`; senza strumento `heldItem.sprite` è un pixel trasparente. Usa `hasMove3`, `hasType2`, `hasTera`, `hasHeldItem` in `visible` per nascondere quelle parti."));
        return sb.ToString();
    }

    public static string DataKeysJson(DataPack pack)
    {
        var o = new JsonObject();
        foreach (var (key, value) in pack.Data.Values)
            o[key] = value.Kind.ToString().ToLowerInvariant();
        return o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }
}
