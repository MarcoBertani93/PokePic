using PokePic.Core.Data;

namespace PokePic.Core.Tests;

public class TemplateValidatorTests
{
    private static TemplateLoadResult Load(string json, params string[] files) =>
        TemplateLoader.Load(json, TestData.Sample.Schema, new MemoryAssets(files));

    [Fact]
    public void Valid_template_loads()
    {
        var result = Load(TestData.Template("""
            { "type": "text", "rect": [0, 0, 10, 10], "text": "Lv. {level:D3} {species.name:upper}", "font": "{pixel}", "size": 8, "visible": "{!hasType2}" },
            { "type": "image", "rect": [0, 0, 10, 10], "image": { "set": "types", "key": "{type1.key}" } },
            { "type": "image", "rect": [0, 0, 10, 10], "image": "{types[type1.key]}" },
            { "type": "image", "rect": [0, 0, 10, 10], "image": "{species.sprite}" },
            { "type": "image", "rect": [0, 0, 10, 10], "image": "types/psychic.png" },
            { "type": "shape", "rect": [0, 0, "{level}", 10], "fill": "{type1.color}" }
            """,
            """
            "values": { "font": { "pixel": "pixel.ttf" }, "image": { "types": { "pattern": "types/{key}.png" } } },
            """),
            "pixel.ttf", "types/psychic.png");

        Assert.True(result.Success, string.Join("\n", result.Issues));
    }

    [Theory]
    [InlineData("fire", true)]
    [InlineData("10034", true)]
    [InlineData("666-tundra", true)]
    [InlineData("shiny/female/3", true)]
    [InlineData("", false)]
    [InlineData("../fire", false)]
    [InlineData("shiny//3", false)]
    [InlineData("/3", false)]
    [InlineData("shiny/", false)]
    [InlineData("a.b", false)]
    [InlineData("a\\b", false)]
    public void Set_keys_are_parts_separated_by_slashes_that_cannot_leave_the_folder(string key, bool valid)
    {
        Assert.Equal(valid, Model.TemplateValue.IsValidKey(key));
    }

    [Fact]
    public void A_literal_key_with_folders_finds_its_file()
    {
        var result = Load(TestData.Template("""
            { "type": "image", "rect": [0, 0, 10, 10], "image": { "set": "home", "key": "shiny/female/3" } }
            """,
            """
            "values": { "image": { "home": { "pattern": "home/{key}.png" } } },
            """),
            "home/shiny/female/3.png");

        Assert.True(result.Success, string.Join("\n", result.Issues));
    }

    [Fact]
    public void Missing_data_key_is_reported_with_line()
    {
        var result = Load(TestData.Template("""{ "type": "shape", "rect": [0, 0, 1, 1], "fill": "{type9.color}" }"""));

        var issue = Assert.Single(result.Issues);
        Assert.Equal("elements[0].fill", issue.Path);
        Assert.Contains("type9.color", issue.Message, StringComparison.Ordinal);
        Assert.NotNull(issue.Line);
    }

    [Fact]
    public void Wrong_kind_is_reported()
    {
        var result = Load(TestData.Template("""{ "type": "shape", "rect": [0, 0, 1, 1], "fill": "{level}" }"""));
        Assert.Contains("expected color", Assert.Single(result.Issues).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Visible_requires_a_boolean()
    {
        var result = Load(TestData.Template("""{ "type": "shape", "rect": [0, 0, 1, 1], "fill": "#000", "visible": "{level}" }"""));
        Assert.Equal("elements[0].visible", Assert.Single(result.Issues).Path);
    }

    [Fact]
    public void Missing_asset_file_is_reported()
    {
        var result = Load(TestData.Template("", """ "values": { "image": { "frame": "frame.png" } }, """));
        var issue = Assert.Single(result.Issues);
        Assert.Equal("values.image.frame", issue.Path);
        Assert.Contains("memory:frame.png", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_font_and_image_are_reported()
    {
        var result = Load(TestData.Template("""
            { "type": "text", "rect": [0, 0, 1, 1], "text": "x", "font": "{nope}", "size": 8 },
            { "type": "image", "rect": [0, 0, 1, 1], "image": "nope.png" },
            { "type": "image", "rect": [0, 0, 1, 1], "image": "{nope}" },
            { "type": "image", "rect": [0, 0, 1, 1], "image": { "set": "nope", "key": "x" } }
            """));

        Assert.Equal(
            ["elements[0].font", "elements[1].image", "elements[2].image", "elements[3].image"],
            result.Issues.Select(i => i.Path));
    }

    [Fact]
    public void Numeric_format_on_text_value_is_reported()
    {
        var result = Load(TestData.Template("""{ "type": "text", "rect": [0, 0, 1, 1], "text": "{species.name:D3}", "font": "f.ttf", "size": 8 }"""), "f.ttf");
        Assert.Contains("only valid for numbers", Assert.Single(result.Issues).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Literal_set_keys_must_have_an_item_or_a_fallback()
    {
        const string set = """ "values": { "image": { "types": { "pattern": "types/{key}.png" } }, "color": { "c": { "items": { "water": "#00F" } } } }, """;
        var missing = Load(TestData.Template("""
            { "type": "image", "rect": [0, 0, 1, 1], "image": { "set": "types", "key": "fire" } },
            { "type": "shape", "rect": [0, 0, 1, 1], "fill": { "set": "c", "key": "fire" } }
            """, set), "types/water.png");
        Assert.Equal(["elements[0].image", "elements[1].fill"], missing.Issues.Select(i => i.Path));

        var found = Load(TestData.Template("""
            { "type": "image", "rect": [0, 0, 1, 1], "image": { "set": "types", "key": ["fire", "water"] } },
            { "type": "shape", "rect": [0, 0, 1, 1], "fill": { "set": "c", "key": ["fire", "water"] } }
            """, set), "types/water.png");
        Assert.True(found.Success, string.Join("\n", found.Issues));
    }

    [Fact]
    public void Pattern_folder_and_fallback_are_checked()
    {
        var result = Load(
            TestData.Template("", """ "values": { "image": { "art": { "pattern": "artwork/{key}.jpg", "fallback": "artwork/unknown.png" }, "nope": { "pattern": "missing/{key}.png" } } }, """),
            "artwork/0006.jpg");

        Assert.Equal(["values.image.art.fallback", "values.image.nope.pattern"], result.Issues.Select(i => i.Path).Order());
    }

    [Fact]
    public void A_pattern_needs_the_key()
    {
        var result = Load(TestData.Template("", """ "values": { "image": { "art": { "pattern": "artwork/0006.png" } } }, """), "artwork/0006.png");
        Assert.Equal("values.image.art.pattern", Assert.Single(result.Issues).Path);
    }

    [Fact]
    public void Candidate_keys_are_validated_one_by_one()
    {
        var result = Load(
            TestData.Template("""{ "type": "image", "rect": [0, 0, 1, 1], "image": { "set": "art", "key": ["{species.name}-{nope}", "../secret"] } }""",
                """ "values": { "image": { "art": { "pattern": "artwork/{key}.jpg", "fallback": "artwork/unknown.png" } } }, """),
            "artwork/unknown.png");

        Assert.Equal(["elements[0].image.key[0]", "elements[0].image.key[1]"], result.Issues.Select(i => i.Path));
    }

    [Fact]
    public void Sample_template_is_valid_for_every_sample_data_pack()
    {
        var root = RepositoryPaths.Root;
        var packs = Directory.GetFiles(Path.Combine(root, "resources", "data"), "*.json");
        Assert.NotEmpty(packs);

        foreach (var packPath in packs)
        {
            var pack = DataPackSerializer.ReadFile(packPath);
            Assert.True(pack.Success, $"{packPath}: {string.Join("\n", pack.Issues)}");

            foreach (var templatePath in Directory.GetFiles(Path.Combine(root, "resources", "templates"), "template.json", SearchOption.AllDirectories))
            {
                var template = TemplateLoader.LoadFile(templatePath, pack.Pack!.Data.Schema);
                Assert.True(template.Success, $"{templatePath} + {packPath}: {string.Join("\n", template.Issues)}");
            }
        }
    }
}
