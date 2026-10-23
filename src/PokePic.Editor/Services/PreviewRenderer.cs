using System.Diagnostics;
using PokePic.Core;
using PokePic.Core.Assets;
using PokePic.Core.Data;
using PokePic.Core.Editing;
using PokePic.Core.Model;
using PokePic.Core.Rendering;
using PokePic.Rendering;

namespace PokePic.Editor.Services;

/// <param name="Card">The rendered card, or null if the template has problems.</param>
/// <param name="Boxes">Canvas bounds of every element, for selection; empty if the template did not load.</param>
public sealed record PreviewOutcome(RenderedCard? Card, IReadOnlyList<ElementBox> Boxes, IReadOnlyList<TemplateIssue> Issues, long ElapsedMilliseconds);

/// <summary>
/// Parse → validate → prepare → render, in one call. Safe to run on a background thread.
/// </summary>
public sealed class PreviewRenderer
{
    private readonly ICardRenderer _renderer = new SkiaCardRenderer();

    public PreviewOutcome Render(string json, CardData data, IAssetProvider assets, RenderOptions options)
    {
        var stopwatch = Stopwatch.StartNew();
        var loaded = TemplateLoader.Load(json, data.Schema, assets);
        if (!loaded.Success)
            return new PreviewOutcome(null, [], loaded.Issues, stopwatch.ElapsedMilliseconds);

        var template = loaded.Template!;
        var boxes = ElementLayout.Compute(template, data);
        try
        {
            using var prepared = _renderer.Prepare(template, assets);
            var card = prepared.Render(data, options);
            return new PreviewOutcome(card, boxes, [], stopwatch.ElapsedMilliseconds);
        }
        catch (TemplateException ex)
        {
            return new PreviewOutcome(null, boxes, TemplateLoader.Locate(json, ex.Issues), stopwatch.ElapsedMilliseconds);
        }
        catch (ArgumentException ex)
        {
            return new PreviewOutcome(null, boxes, [new TemplateIssue("", ex.Message)], stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Element bounds without validation or rendering: fast enough to follow the mouse while dragging.
    /// </summary>
    public static IReadOnlyList<ElementBox> Layout(string json, CardData data)
    {
        var parsed = Core.Json.TemplateParser.Parse(json);
        return parsed.Template is null ? [] : ElementLayout.Compute(parsed.Template, data);
    }

    /// <summary>
    /// Line of the element with the given JSON path.
    /// </summary>
    public static int? LineOf(string json, string path) => TemplateLoader.Locate(json, [new TemplateIssue(path, "")])[0].Line;
}
