using System.Text.Json.Nodes;
using PokePic.Core.Editing;
using PokePic.Core.Json;
using PokePic.Core.Model;

namespace PokePic.Core.Tests;

public class GroupTests
{
    [Fact]
    public void Group_children_are_parsed_with_nested_paths()
    {
        var result = TemplateParser.Parse(TestData.Template("""
            { "type": "group", "name": "G", "position": [10, 20], "children": [
              { "type": "shape", "rect": [0, 0, 1, 1], "fill": "#000" },
              { "type": "group", "children": [ { "type": "line", "from": [0, 0], "to": [1, 0], "color": "#000" } ] }
            ] }
            """));

        Assert.True(result.Success, string.Join("\n", result.Issues));
        var group = Assert.IsType<GroupElement>(Assert.Single(result.Template!.Elements));
        Assert.Equal("elements[0].children[0]", group.Children[0].Path);
        var inner = Assert.IsType<GroupElement>(group.Children[1]);
        Assert.Equal("elements[0].children[1].children[0]", inner.Children[0].Path);
        Assert.Equal(new PointValue(new Literal<float>(0), new Literal<float>(0)), inner.Position);
    }

    [Fact]
    public void Errors_inside_groups_are_validated()
    {
        var result = TemplateLoader.Load(
            TestData.Template("""{ "type": "group", "children": [ { "type": "shape", "rect": [0, 0, 1, 1], "fill": "{nope}" } ] }"""),
            TestData.Sample.Schema,
            new MemoryAssets());

        Assert.Equal("elements[0].children[0].fill", Assert.Single(result.Issues).Path);
    }

    [Fact]
    public void Layout_applies_group_offsets_and_hit_tests_the_topmost_leaf()
    {
        var template = TemplateParser.Parse(TestData.Template("""
            { "type": "shape", "rect": [0, 0, 100, 50], "fill": "#000" },
            { "type": "group", "position": [10, 20], "children": [
              { "type": "shape", "rect": [5, 5, 10, 10], "fill": "#FFF" }
            ] }
            """)).Template!;

        var boxes = ElementLayout.Compute(template, TestData.Sample);

        Assert.Equal(3, boxes.Count);
        Assert.Equal(new RectF(15, 25, 10, 10), boxes[2].Bounds);
        Assert.Equal(new RectF(15, 25, 10, 10), boxes[1].Bounds); // group = union of its children
        Assert.Equal("elements[1].children[0]", ElementLayout.HitTest(boxes, 16, 26)!.Element.Path);
        Assert.Equal("elements[0]", ElementLayout.HitTest(boxes, 2, 2)!.Element.Path);
    }
}

public class TemplateDocumentTests
{
    private static TemplateDocument Doc(string elements) =>
        TemplateDocument.TryParse(TestData.Template(elements), out _) ?? throw new InvalidOperationException();

    private static string Names(TemplateDocument doc, string? parent = null) =>
        string.Join(",", doc.ChildrenOf(parent).Select(e => e!["name"]!.GetValue<string>()));

    private const string ThreeShapes = """
        { "name": "A", "type": "shape", "rect": [0, 0, 1, 1], "fill": "#000" },
        { "name": "B", "type": "shape", "rect": [0, 0, 1, 1], "fill": "#000" },
        { "name": "G", "type": "group", "position": [10, 20], "children": [] }
        """;

    [Fact]
    public void Move_within_the_same_parent_reorders()
    {
        var doc = Doc(ThreeShapes);
        Assert.Equal("elements[1]", doc.Move("elements[0]", null, 1));
        Assert.Equal("B,A,G", Names(doc));
    }

    [Fact]
    public void Move_into_a_group_after_a_preceding_sibling_adjusts_the_path()
    {
        var doc = Doc(ThreeShapes);

        var path = doc.Move("elements[0]", "elements[2]", 0);

        Assert.Equal("elements[1].children[0]", path);
        Assert.Equal("B,G", Names(doc));
        Assert.Equal("A", Names(doc, "elements[1]"));
    }

    [Fact]
    public void Move_between_groups_keeps_the_canvas_position()
    {
        var doc = Doc(ThreeShapes);                        // A at (0,0), G at (10,20)

        var inside = doc.Move("elements[0]", "elements[2]", 0)!;
        Assert.Equal([-10, -20, 1, 1], Rect(doc, inside));

        var outside = doc.Move(inside, null, 0)!;
        Assert.Equal([0, 0, 1, 1], Rect(doc, outside));
    }

    [Fact]
    public void Move_into_itself_or_a_non_group_is_refused()
    {
        var doc = Doc(ThreeShapes);
        Assert.Null(doc.Move("elements[2]", "elements[2]", 0));
        Assert.Null(doc.Move("elements[0]", "elements[1]", 0));
    }

    [Fact]
    public void Group_and_ungroup_keep_canvas_positions()
    {
        var doc = Doc(ThreeShapes);
        doc.Move("elements[1]", "elements[2]", 0, keepCanvasPosition: false);   // B into G at (10,20)
        Assert.Equal([0, 0, 1, 1], Rect(doc, "elements[1].children[0]"));

        var first = doc.Ungroup("elements[1]");

        Assert.Equal("elements[1]", first);
        Assert.Equal("A,B", Names(doc));
        Assert.Equal([10, 20, 1, 1], Rect(doc, "elements[1]"));

        var group = doc.Group("elements[0]", "New");
        Assert.Equal("New,B", Names(doc));
        Assert.Equal("A", Names(doc, group));
    }

    [Fact]
    public void Translate_moves_literal_coordinates_and_gradients_but_not_bindings()
    {
        var doc = Doc("""
            { "type": "shape", "rect": ["{level}", 5, 10, 10],
              "fill": { "linear": { "from": [0, 0], "to": [10, 0], "stops": [[0, "#000"], [1, "#FFF"]] } } }
            """);

        Assert.True(doc.Translate("elements[0]", 3, 4));

        var shape = doc.GetElement("elements[0]")!;
        Assert.Equal("[\"{level}\",9,10,10]", shape["rect"]!.ToJsonString());
        Assert.Equal("[3,4]", shape["fill"]!["linear"]!["from"]!.ToJsonString());
    }

    [Fact]
    public void Duplicate_inserts_a_copy_above()
    {
        var doc = Doc(ThreeShapes);
        Assert.Equal("elements[1]", doc.Duplicate("elements[0]"));
        Assert.Equal("A,A copy,B,G", Names(doc));
    }

    [Fact]
    public void Formatting_is_stable_and_parses_back_to_the_same_template()
    {
        var path = Path.Combine(RepositoryPaths.Root, "resources", "templates", "pixelcard", "template.json");
        var original = File.ReadAllText(path);
        var doc = TemplateDocument.TryParse(original, out _)!;

        var formatted = doc.ToJson();

        Assert.Equal(formatted, TemplateDocument.TryParse(formatted, out _)!.ToJson());
        Assert.Equal(original.ReplaceLineEndings("\n"), formatted);
        Assert.True(TemplateParser.Parse(formatted).Success);
    }

    [Fact]
    public void Element_span_covers_the_whole_object()
    {
        var json = TestData.Template("""
            { "name": "A", "type": "shape", "rect": [0, 0, 1, 1], "fill": "#000" },
            { "name": "Pokémon", "type": "group", "children": [ { "name": "B", "type": "line", "from": [0, 0], "to": [1, 0], "color": "#000" } ] }
            """);

        var span = TemplateLoader.FindSpan(json, "elements[1].children[0]");

        Assert.NotNull(span);
        var text = json.Substring(span.Value.Start, span.Value.Length);
        Assert.StartsWith("{ \"name\": \"B\"", text, StringComparison.Ordinal);
        Assert.EndsWith("}", text, StringComparison.Ordinal);
        Assert.Null(TemplateLoader.FindSpan(json, "elements[9]"));
    }

    [Fact]
    public void Numbers_are_written_as_integers_when_whole()
    {
        Assert.Equal("3", TemplateDocument.NumberNode(3.0).ToJsonString());
        Assert.Equal("2.5", TemplateDocument.NumberNode(2.5).ToJsonString());
    }

    private static int[] Rect(TemplateDocument doc, string path) =>
        [.. ((JsonArray)doc.GetElement(path)!["rect"]!).Select(n => n!.GetValue<int>())];
}
