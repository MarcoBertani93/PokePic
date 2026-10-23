using PokePic.Core.Json;

namespace PokePic.Core.Tests;

public class StyleTests
{
    private static IReadOnlyList<TemplateIssue> Issues(string styles, string elements) =>
        TemplateParser.Parse(TestData.Template(elements, "\"styles\": {" + styles + "},")).Issues;

    private const string Image = """{ "type": "image", "rect": [0, 0, 1, 1], "image": "{species.sprite}", "style": "label" }""";

    [Fact]
    public void A_typed_style_is_checked_where_it_is_defined_and_used_only_by_its_type()
    {
        var wrongProperty = Issues(""" "label": { "type": "text", "fit": "contain" } """, "");
        Assert.Equal("styles.label.fit", Assert.Single(wrongProperty).Path);

        var wrongElement = Issues(""" "label": { "type": "text", "font": "f" } """, Image);
        var issue = Assert.Single(wrongElement);
        Assert.Equal("elements[0].style", issue.Path);
        Assert.Contains("is for text elements", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_untyped_style_reports_the_properties_the_element_does_not_have_once()
    {
        var issues = Issues(""" "label": { "font": "f", "size": 8, "opacity": 0.5 } """, Image);

        var issue = Assert.Single(issues);
        Assert.Equal("elements[0].style", issue.Path);
        Assert.Contains("font, size", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Common_properties_of_an_untyped_style_apply_to_any_element()
    {
        var result = TemplateParser.Parse(TestData.Template(Image, """ "styles": { "label": { "opacity": 0.5 } }, """));

        Assert.True(result.Success, string.Join("\n", result.Issues));
        Assert.Equal(0.5f, result.Template!.Elements[0].Opacity);
    }
}

public class ImageScaleTests
{
    [Fact]
    public void Scale_is_only_for_fit_scale()
    {
        var issues = TemplateParser.Parse(TestData.Template("""{ "type": "image", "rect": [0, 0, 1, 1], "image": "{species.sprite}", "fit": "contain", "scale": 2 }""")).Issues;
        Assert.Equal("elements[0].scale", Assert.Single(issues).Path);
    }
}
