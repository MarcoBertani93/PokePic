using System.Text.Json;
using System.Text.Json.Nodes;

namespace PokePic.Plugin;

/// <summary>A template folder found in the templates folder.</summary>
/// <param name="Name">The template's "name" (else its id, else the folder name).</param>
internal sealed record TemplateEntry(string Name, string Folder)
{
    public string TemplatePath => Path.Combine(Folder, "template.json");

    public override string ToString() => Name;
}

/// <summary>
/// The plugin's folder "plugins/PokePic": the editor (PokePic.Editor.exe, a single file so that PKHeX does not
/// load it as a plugin) and "templates", with one folder per template.
/// </summary>
internal sealed class TemplateCatalog(string pluginFolder)
{
    public string Home { get; } = Path.Combine(pluginFolder, "PokePic");

    /// <summary>Where templates are looked for and created.</summary>
    public string Folder => Path.Combine(Home, "templates");

    public string EditorPath => Path.Combine(Home, "PokePic.Editor.exe");

    public IReadOnlyList<TemplateEntry> List()
    {
        if (!Directory.Exists(Folder))
            return [];
        return [.. Directory.EnumerateDirectories(Folder)
            .Where(d => File.Exists(Path.Combine(d, "template.json")))
            .Select(d => new TemplateEntry(NameOf(d), d))
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>The name is read from the JSON without validating the template: a broken template is still listed.</summary>
    private static string NameOf(string folder)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "template.json")), documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) as JsonObject;
            if (root?["name"] is JsonValue name && name.GetValueKind() == JsonValueKind.String)
                return name.GetValue<string>();
            if (root?["id"] is JsonValue id && id.GetValueKind() == JsonValueKind.String)
                return id.GetValue<string>();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // The error shows when the template is chosen; here the folder name is enough.
        }
        return Path.GetFileName(folder);
    }

    /// <summary>A new template folder with a minimal template (a background and the sprite: no fonts needed).</summary>
    public TemplateEntry Create(string name)
    {
        var folderName = new string([.. name.Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '-' : char.ToLowerInvariant(c))]);
        if (folderName.Length == 0)
            folderName = "template";
        var folder = Path.Combine(Folder, folderName);
        for (int i = 2; Directory.Exists(folder); i++)
            folder = Path.Combine(Folder, $"{folderName}-{i}");
        Directory.CreateDirectory(folder);

        var template = new JsonObject
        {
            ["id"] = Path.GetFileName(folder),
            ["version"] = 1,
            ["name"] = name.Trim(),
            ["canvas"] = new JsonObject { ["width"] = 192, ["height"] = 128, ["mode"] = "pixelPerfect", ["scale"] = 4 },
            ["elements"] = new JsonArray(
                new JsonObject { ["name"] = "Background", ["type"] = "shape", ["rect"] = new JsonArray(0, 0, 192, 128), ["radius"] = 8, ["fill"] = "{type1.color}" },
                new JsonObject { ["name"] = "Sprite", ["type"] = "image", ["rect"] = new JsonArray(48, 16, 96, 96), ["image"] = "{species.sprite}", ["fit"] = "contain" }),
        };
        File.WriteAllText(Path.Combine(folder, "template.json"), template.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return new TemplateEntry(name.Trim(), folder);
    }
}
