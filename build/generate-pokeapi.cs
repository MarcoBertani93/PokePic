#:project ../src/PokePic.Adapter/PokePic.Adapter.csproj
#:property PublishAot=false
#:property JsonSerializerIsReflectionEnabledByDefault=true
// Generates src/PokePic.Adapter/PokeApi.json: for every form PKHeX knows, the name of its images in the PokeAPI
// sprites repository and which variants exist (shiny, female). PokePic.Adapter turns it into pokeapi.id,
// pokeapi.path and pokeapi.artwork.
//   - PokeAPI data: pokemon.csv and pokemon_forms.csv of github.com/PokeAPI/pokeapi;
//   - the files that exist: the tree of github.com/PokeAPI/sprites (cloned without blobs: only names are read);
//   - PKHeX forms: FormConverter, in every game context (names can change with the context: Arceus in Gen 4);
//   - forms are matched by name; build/pokeapi-overrides.json settles the others. An unmatched form fails.
// Downloads go to artifacts/pokeapi-cache (delete it to refresh). Run it when PKHeX or PokeAPI add forms:
//   dotnet run build/generate-pokeapi.cs
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PKHeX.Core;

var root = Path.GetFullPath(Path.Combine(AppContext.GetData("EntryPointFileDirectoryPath") as string ?? "build", ".."));
var cache = Path.Combine(root, "artifacts", "pokeapi-cache");
Directory.CreateDirectory(cache);

// ---------------------------------------------------------------- PokeAPI data

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("PokePic-generate-pokeapi");
string Csv(string name)
{
    var file = Path.Combine(cache, name);
    if (!File.Exists(file))
        File.WriteAllText(file, http.GetStringAsync($"https://raw.githubusercontent.com/PokeAPI/pokeapi/master/data/v2/csv/{name}").Result);
    return file;
}
var pokemonSpecies = File.ReadAllLines(Csv("pokemon.csv")).Skip(1).Select(l => l.Split(','))
    .ToDictionary(c => int.Parse(c[0], CultureInfo.InvariantCulture), c => int.Parse(c[2], CultureInfo.InvariantCulture));
var forms = File.ReadAllLines(Csv("pokemon_forms.csv")).Skip(1).Select(l => l.Split(','))
    .Select(c => new ApiForm(c[1], c[2], int.Parse(c[3], CultureInfo.InvariantCulture), c[5] == "1"))
    .ToList();
var formsOfPokemon = forms.GroupBy(f => f.Pokemon).ToDictionary(g => g.Key, g => g.Count());
var byIdentifier = forms.ToDictionary(f => f.Identifier);

var treeFile = Path.Combine(cache, "sprites-tree.txt");
if (!File.Exists(treeFile))
{
    var repo = Path.Combine(cache, "sprites-repo");
    if (!Directory.Exists(repo))
        Run("git", $"clone -q --filter=blob:none --no-checkout --depth 1 https://github.com/PokeAPI/sprites.git \"{repo}\"");
    File.WriteAllText(treeFile, Run("git", $"-C \"{repo}\" ls-tree -r --name-only HEAD sprites/pokemon/other/home sprites/pokemon/other/official-artwork"));
}
var files = File.ReadAllLines(treeFile).ToHashSet(StringComparer.Ordinal);
// pokeapi.path is a path relative to any image collection of PokeAPI: they all have the same layout ("<name>",
// "shiny/<name>", "female/<name>", "shiny/female/<name>"). Which variants exist is checked in other/home, the most
// complete collection; the official artwork, which has no female images, is checked on its own.
bool PathExists(string path) => files.Contains($"sprites/pokemon/other/home/{path}.png");
bool Artwork(string path) => files.Contains($"sprites/pokemon/other/official-artwork/{path}.png");

// The name of a form's images: the pokemon id, or "id-form" for the cosmetic forms of a pokemon (Vivillon).
string Name(ApiForm f) => formsOfPokemon[f.Pokemon] > 1 && !f.IsDefault ? $"{f.Pokemon}-{f.Form}" : $"{f.Pokemon}";

// ---------------------------------------------------------------- PKHeX forms

var overrides = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "build", "pokeapi-overrides.json")))!.AsObject()
    .Where(kv => !kv.Key.StartsWith("//", StringComparison.Ordinal))
    .ToDictionary(kv => kv.Key, kv => kv.Value!.GetValue<string>());
var strings = GameInfo.GetStrings("en");
var contexts = Enum.GetValues<EntityContext>().Where(c => c.IsValid).OrderByDescending(c => c).ToArray();
static string Normalize(string s)
{
    var decomposed = s.Normalize(NormalizationForm.FormD);
    return new string(decomposed.Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}

var table = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
var problems = new List<string>();
var report = new StringBuilder();

void Add(string key, string name, string what)
{
    var entry = new JsonObject();
    if (name != key.Split('-', '@')[0])
        entry["id"] = name;
    // Images that exist: without its own image a form uses the species' one (the Adapter decides).
    if (!PathExists(name)) entry["noImage"] = true;
    if (!Artwork(name)) entry["noArtwork"] = true;
    if (PathExists($"female/{name}")) entry["female"] = true;
    if (PathExists($"shiny/female/{name}")) entry["femaleShiny"] = true;
    if (!PathExists($"shiny/{name}")) entry["noShiny"] = true;
    if (!Artwork($"shiny/{name}")) entry["noArtworkShiny"] = true;
    if (entry.Count > 0)
        table[key] = entry;
    report.AppendLine($"{key,-12} {name,-14} {what}");
}

string? Resolve(ushort species, string key, IEnumerable<string> pkhexNames, int form, List<ApiForm> candidates)
{
    if (overrides.TryGetValue(key, out var identifier))
    {
        if (identifier.Length == 0)
            return species.ToString(CultureInfo.InvariantCulture);
        if (!byIdentifier.TryGetValue(identifier, out var forced))
        {
            problems.Add($"{key}: override \"{identifier}\" is not a PokeAPI form.");
            return null;
        }
        return Name(forced);
    }
    var names = pkhexNames.Select(Normalize).Where(n => n.Length > 0 && n != "normal").ToArray();
    var found = candidates.Where(f => names.Contains(Normalize(f.Form))).ToList();
    // The first form is the species' image unless its name says otherwise (Vivillon's first form is Icy Snow,
    // PokeAPI's default is Meadow).
    if (form == 0)
        return found.Count == 1 ? Name(found[0]) : species.ToString(CultureInfo.InvariantCulture);
    if (found.Count == 0)
        found = candidates.Where(f => f.Form.Length > 0 && names.Any(n => n.Length > 0 && (Normalize(f.Form).StartsWith(n, StringComparison.Ordinal) || n.StartsWith(Normalize(f.Form), StringComparison.Ordinal)))).ToList();
    if (found.Count == 1)
        return Name(found[0]);
    problems.Add($"{key} {strings.specieslist[species]} [{string.Join(" / ", pkhexNames)}]: {(found.Count == 0 ? "no PokeAPI form" : "several: " + string.Join(", ", found.Select(f => f.Identifier)))} (PokeAPI: {string.Join(", ", candidates.Select(f => f.Identifier))})");
    return null;
}

for (ushort species = 1; species < (ushort)Species.MAX_COUNT; species++)
{
    var candidates = forms.Where(f => pokemonSpecies[f.Pokemon] == species).ToList();
    // Form names in every context; the most recent context gives the main name, older ones may differ.
    var perForm = new SortedDictionary<int, List<(EntityContext Context, string Name)>>();
    foreach (var context in contexts)
    {
        var list = FormConverter.GetFormList(species, strings.Types, strings.forms, GameInfo.GenderSymbolASCII, context);
        for (int i = 0; i < list.Length; i++)
        {
            if (!perForm.TryGetValue(i, out var names))
                perForm[i] = names = [];
            names.Add((context, list[i]));
        }
    }
    if (perForm.Count == 0)
        perForm[0] = [(contexts[0], "")];

    foreach (var (form, names) in perForm)
    {
        if (species == (ushort)Species.Alcremie)
        {
            // Alcremie: the form is the cream, the form argument the sweet.
            string[] sweets = ["strawberry", "berry", "love", "star", "clover", "flower", "ribbon"];
            var cream = names[0].Name.Length == 0 ? "vanilla-cream" : names[0].Name.ToLowerInvariant().Replace(' ', '-');
            for (int sweet = 0; sweet < sweets.Length; sweet++)
            {
                var id = $"alcremie-{cream}-{sweets[sweet]}-sweet";
                if (byIdentifier.TryGetValue(id, out var f))
                    Add($"{species}-{form}-{sweet}", Name(f), id);
                else
                    problems.Add($"{species}-{form}-{sweet}: no PokeAPI form \"{id}\".");
            }
            continue;
        }
        var main = names[0].Name;
        var key = $"{species}-{form}";
        var name = Resolve(species, key, names.Where(n => n.Name == main).Select(n => n.Name).Distinct(), form, candidates);
        if (name is not null)
            Add(key, name, main);
        // Older contexts where the same form number has another name (Arceus in Gen 4, Pikachu in Gen 6).
        foreach (var other in names.Where(n => n.Name != main && n.Name.Length > 0).GroupBy(n => n.Name))
        {
            foreach (var (context, _) in other)
            {
                var contextKey = $"{key}@{context}";
                var contextName = Resolve(species, contextKey, [other.Key], form, candidates);
                if (contextName is not null && contextName != name)
                    Add(contextKey, contextName, $"{other.Key} ({context})");
            }
        }
    }
}

File.WriteAllText(Path.Combine(cache, "report.txt"), report.ToString());
if (problems.Count > 0)
{
    Console.Error.WriteLine($"{problems.Count} forms without a PokeAPI match: add them to build/pokeapi-overrides.json.");
    foreach (var p in problems)
        Console.Error.WriteLine("  " + p);
    return 1;
}

var output = new JsonObject
{
    ["//"] = "Generated by build/generate-pokeapi.cs from PokeAPI (pokemon_forms.csv, sprites tree) and PKHeX forms. Key: species-form[-formArgument][@Context]. id: image name when not the species number. Flags for pokeapi.path (a path relative to any PokeAPI image collection, its variants checked in other/home, the most complete one): noImage: no image of this form (the species one is used); noShiny: no shiny image; female/femaleShiny: a female image exists (normal/shiny). Flags for pokeapi.artwork (other/official-artwork): noArtwork: no image of this form; noArtworkShiny: no shiny image.",
};
foreach (var (key, entry) in table)
    output[key] = entry;
var target = Path.Combine(root, "src", "PokePic.Adapter", "PokeApi.json");
File.WriteAllText(target, output.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine($"{table.Count} entries written to {Path.GetRelativePath(root, target)}; report: {Path.GetRelativePath(root, Path.Combine(cache, "report.txt"))}");
return 0;

static string Run(string file, string arguments)
{
    using var process = Process.Start(new ProcessStartInfo(file, arguments) { RedirectStandardOutput = true, UseShellExecute = false })!;
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"{file} {arguments} failed ({process.ExitCode}).");
    return output;
}

record ApiForm(string Identifier, string Form, int Pokemon, bool IsDefault);
