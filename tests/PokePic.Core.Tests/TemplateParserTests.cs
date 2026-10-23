using PokePic.Core.Json;
using PokePic.Core.Model;

namespace PokePic.Core.Tests;

public class TemplateParserTests
{
    [Fact]
    public void Minimal_template_is_parsed_with_defaults()
    {
        var result = TemplateParser.Parse("""
            {
              // comments and trailing commas are allowed
              "id": "t",
              "canvas": { "width": 10, "height": 20, },
              "elements": [],
            }
            """);

        Assert.True(result.Success, string.Join("\n", result.Issues));
        var t = result.Template!;
        Assert.Equal(("t", 1, 10, 20), (t.Id, t.Version, t.Canvas.Width, t.Canvas.Height));
        Assert.Equal(RenderMode.HighResolution, t.Canvas.DefaultMode);
        Assert.Empty(t.Elements);
    }

    [Fact]
    public void Elements_keep_their_order()
    {
        var result = TemplateParser.Parse(TestData.Template("""
            { "type": "shape", "rect": [0, 0, 1, 1], "fill": "#000" },
            { "type": "line", "from": [0, 0], "to": [1, 1], "color": "#000" },
            { "type": "image", "rect": [0, 0, 1, 1], "image": "{species.sprite}" }
            """));

        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Collection(result.Template!.Elements,
            e => Assert.IsType<ShapeElement>(e),
            e => Assert.IsType<LineElement>(e),
            e => Assert.IsType<ImageElement>(e));
    }

    [Fact]
    public void Unknown_property_is_reported_with_path_and_line()
    {
        var result = TemplateParser.Parse(TestData.Template("""
            { "type": "shape", "rect": [0, 0, 1, 1], "fill": "#000",
              "colr": "#FFF" }
            """));

        var issue = Assert.Single(result.Issues);
        Assert.Equal("elements[0].colr", issue.Path);
        Assert.Contains("Unknown property", issue.Message, StringComparison.Ordinal);
        Assert.Equal(6, issue.Line);
    }

    [Fact]
    public void Invalid_json_reports_the_line()
    {
        var result = TemplateParser.Parse("{\n  \"id\": \"x\",\n  oops\n}");

        Assert.False(result.Success);
        Assert.Equal(3, Assert.Single(result.Issues).Line);
    }

    [Fact]
    public void Style_properties_are_defaults_overridden_by_the_element()
    {
        var result = TemplateParser.Parse(TestData.Template(
            """{ "type": "text", "style": "label", "rect": [0, 0, 10, 10], "text": "x", "color": "#FF0000" }""",
            """
            "values": { "font": { "pixel": "pixel.ttf" } },
            "styles": { "label": { "font": "{pixel}", "size": 8, "color": "#000000", "align": "right" } },
            """));

        Assert.True(result.Success, string.Join("\n", result.Issues));
        var text = Assert.IsType<TextElement>(Assert.Single(result.Template!.Elements));
        Assert.Equal("{pixel}", Assert.IsType<ComposedExpr>(text.Font).Text.SingleBinding?.ToString());
        Assert.Equal(8, text.Size);
        Assert.Equal(HorizontalAlign.Right, text.Align);
        Assert.Equal(new Literal<Rgba>(new Rgba(255, 0, 0)), text.Color);
    }

    [Fact]
    public void Unknown_style_is_reported()
    {
        var result = TemplateParser.Parse(TestData.Template("""{ "type": "shape", "style": "nope", "rect": [0, 0, 1, 1], "fill": "#000" }"""));
        Assert.Contains(result.Issues, i => i.Path == "elements[0].style");
    }

    [Theory]
    [InlineData("\"images/frame.png\"", typeof(ComposedExpr))]
    [InlineData("\"{species.sprite}\"", typeof(ComposedExpr))]
    [InlineData("\"{types[type1.key]}\"", typeof(ComposedExpr))]
    [InlineData("{ \"set\": \"types\", \"key\": \"{type1.key}\" }", typeof(LookupExpr))]
    public void Image_sources_are_recognized(string json, Type expected)
    {
        var result = TemplateParser.Parse(TestData.Template($$"""{ "type": "image", "rect": [0, 0, 1, 1], "image": {{json}} }"""));

        Assert.True(result.Success, string.Join("\n", result.Issues));
        var image = Assert.IsType<ImageElement>(Assert.Single(result.Template!.Elements));
        Assert.IsType(expected, image.Source);
    }

    [Fact]
    public void Bound_properties_are_parsed_as_bindings()
    {
        var result = TemplateParser.Parse(TestData.Template("""
            { "type": "shape", "rect": [0, "{level}", 1, 1], "fill": "{type1.color}", "visible": "{!hasType2}" }
            """));

        Assert.True(result.Success, string.Join("\n", result.Issues));
        var shape = Assert.IsType<ShapeElement>(Assert.Single(result.Template!.Elements));
        Assert.Equal(new Bound<float>(new Binding("level")), shape.Rect.Y);
        Assert.Equal(new SolidFill(new Bound<Rgba>(new Binding("type1.color"))), shape.Fill);
        Assert.Equal(new Bound<bool>(new Binding("hasType2", Negate: true)), shape.Visible);
    }

    [Fact]
    public void Pixel_perfect_requires_integer_scale()
    {
        var result = TemplateParser.Parse("""{ "id": "t", "canvas": { "width": 1, "height": 1, "mode": "pixelPerfect", "scale": 2.5 }, "elements": [] }""");
        Assert.Contains(result.Issues, i => i.Path == "canvas.scale");
    }

    [Fact]
    public void Unknown_element_type_is_reported()
    {
        var result = TemplateParser.Parse(TestData.Template("""{ "type": "circle" }"""));
        Assert.Contains(result.Issues, i => i.Path == "elements[0].type");
    }

    [Fact]
    public void Gradient_stops_must_be_ordered()
    {
        var result = TemplateParser.Parse(TestData.Template("""
            { "type": "shape", "rect": [0, 0, 1, 1], "fill": { "linear": { "from": [0, 0], "to": [1, 0], "stops": [[1, "#000"], [0, "#FFF"]] } } }
            """));
        Assert.Contains(result.Issues, i => i.Message.Contains("increasing", StringComparison.Ordinal));
    }
}
