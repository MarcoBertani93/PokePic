using System.Globalization;
using PokePic.Core;
using PokePic.Core.Data;
using PokePic.Core.Model;
using PokePic.Core.Rendering;
using PokePic.Rendering;

// pokepic render --template <template.json> [--data <pack.json>] [--language it] [--mode pixelPerfect|highResolution] [--scale N] --out <card.png>
// pokepic check  --template <template.json> [--data <pack.json>]

if (args.Length == 0 || args[0] is not ("render" or "check" or "format"))
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  pokepic render --template <template.json> [--data <pack.json>] [--language it] [--mode pixelPerfect|highResolution] [--scale N] --out <card.png>");
    Console.Error.WriteLine("  pokepic check  --template <template.json> [--data <pack.json>]");
    Console.Error.WriteLine("  pokepic format --template <template.json>   (rewrites the file in the editor's layout; comments are removed)");
    return 2;
}

var options = ParseOptions(args[1..]);
if (!options.TryGetValue("template", out var templatePath))
{
    Console.Error.WriteLine("Missing --template.");
    return 2;
}

if (args[0] == "format")
{
    var document = PokePic.Core.Editing.TemplateDocument.TryParse(File.ReadAllText(templatePath), out var parseError);
    if (document is null)
    {
        Console.Error.WriteLine($"Invalid JSON: {parseError}");
        return 1;
    }
    File.WriteAllText(templatePath, document.ToJson());
    Console.WriteLine($"Formatted {templatePath}.");
    return 0;
}

var web = new PokePic.Core.Assets.WebAssetCache();
var data = CardData.Empty;
var language = Languages.Fallback;
if (options.TryGetValue("data", out var dataPath))
{
    var pack = DataPackSerializer.ReadFile(dataPath, web);
    if (!pack.Success)
        return Fail("Data pack", pack.Issues);
    data = pack.Pack!.Data;
    language = pack.Pack.Language ?? language;
}
if (options.TryGetValue("language", out var languageOption))
    language = languageOption;

var loaded = TemplateLoader.LoadFile(templatePath, data.Schema, web);
if (!loaded.Success)
    return Fail("Template", loaded.Issues);

if (args[0] == "check")
{
    Console.WriteLine("Template is valid.");
    return 0;
}

if (!options.TryGetValue("out", out var outPath))
{
    Console.Error.WriteLine("Missing --out.");
    return 2;
}

var template = loaded.Template!;
var renderOptions = RenderOptions.FromTemplate(template, language);
if (options.TryGetValue("mode", out var mode))
    renderOptions = renderOptions with { Mode = Enum.Parse<RenderMode>(mode, ignoreCase: true) };
if (options.TryGetValue("scale", out var scale))
    renderOptions = renderOptions with { Scale = float.Parse(scale, CultureInfo.InvariantCulture) };

var assets = new PokePic.Core.Assets.FileSystemAssetProvider(Path.GetDirectoryName(Path.GetFullPath(templatePath))!, web);
try
{
    using var prepared = new SkiaCardRenderer().Prepare(template, assets);
    var card = prepared.Render(data, renderOptions);
    File.WriteAllBytes(outPath, card.Png);
    Console.WriteLine($"Written {outPath} ({card.Width}x{card.Height}).");
    return 0;
}
catch (TemplateException ex)
{
    return Fail("Render", ex.Issues);
}

static int Fail(string what, IReadOnlyList<TemplateIssue> issues)
{
    Console.Error.WriteLine($"{what} errors:");
    foreach (var issue in issues)
        Console.Error.WriteLine($"  {issue}");
    return 1;
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i + 1 < args.Length; i += 2)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"Unexpected argument \"{args[i]}\".");
        result[args[i][2..]] = args[i + 1];
    }
    return result;
}
