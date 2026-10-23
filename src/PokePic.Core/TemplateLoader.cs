using PokePic.Core.Assets;
using PokePic.Core.Data;
using PokePic.Core.Json;
using PokePic.Core.Model;
using PokePic.Core.Validation;

namespace PokePic.Core;

public sealed record TemplateLoadResult(CardTemplate? Template, IReadOnlyList<TemplateIssue> Issues)
{
    public bool Success => Template is not null && Issues.Count == 0;
}

/// <summary>
/// Parses and validates a template in one step. A template is usable only if <see cref="TemplateLoadResult.Success"/>.
/// </summary>
public static class TemplateLoader
{
    public static TemplateLoadResult Load(string json, DataSchema schema, IAssetProvider assets)
    {
        var parsed = TemplateParser.Parse(json);
        if (!parsed.Success)
            return new TemplateLoadResult(null, parsed.Issues);

        var issues = TemplateValidator.Validate(parsed.Template!, schema, assets);
        if (issues.Count == 0)
            return new TemplateLoadResult(parsed.Template, []);

        var located = JsonLineMap.Build(json, TemplateParser.ReaderOptions).Locate(issues);
        return new TemplateLoadResult(null, located);
    }

    /// <summary>
    /// Adds source lines to issues produced later, e.g. by the renderer.
    /// </summary>
    public static IReadOnlyList<TemplateIssue> Locate(string json, IEnumerable<TemplateIssue> issues) =>
        JsonLineMap.Build(json, TemplateParser.ReaderOptions).Locate(issues);

    /// <summary>
    /// Character range of the JSON value at <paramref name="path"/> (e.g. "elements[2]"), or null.
    /// </summary>
    public static (int Start, int Length)? FindSpan(string json, string path) =>
        JsonLineMap.Build(json, TemplateParser.ReaderOptions).FindSpan(path);

    /// <summary>
    /// Loads a template file; its assets are resolved relative to the file's folder.
    /// </summary>
    public static TemplateLoadResult LoadFile(string path, DataSchema schema, WebAssetCache? web = null)
    {
        var assets = new FileSystemAssetProvider(Path.GetDirectoryName(Path.GetFullPath(path))!, web);
        return Load(File.ReadAllText(path), schema, assets);
    }
}
