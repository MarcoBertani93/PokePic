using PokePic.Core.Data;
using PokePic.Core.Model;
using PokePic.Core.Resolution;

namespace PokePic.Core.Tests;

public class BindingResolverTests
{
    private readonly BindingResolver _resolver = new(TestData.Sample);

    [Theory]
    [InlineData("Lv. {level}", "Lv. 42")]
    [InlineData("{level:D3}", "042")]
    [InlineData("{species.name:upper}", "HYPNO")]
    [InlineData("{species.name:lower}", "hypno")]
    [InlineData("{weight:F1} kg", "75.6 kg")]
    [InlineData("{hasType2} {!hasType2}", "false true")]
    [InlineData("{type1.color}", "#EF4179")]
    public void Text_is_formatted_invariantly(string template, string expected)
    {
        var text = BindingSyntax.ParseText(template, out _)!;
        Assert.Equal(expected, _resolver.Resolve(text, "p"));
    }

    [Fact]
    public void Bound_values_are_resolved()
    {
        Assert.False(_resolver.Resolve(new Bound<bool>(new Binding("hasType2")), "p"));
        Assert.True(_resolver.Resolve(new Bound<bool>(new Binding("hasType2", Negate: true)), "p"));
        Assert.Equal(42f, _resolver.Resolve(new Bound<float>(new Binding("level")), "p"));
        Assert.Equal(new Rgba(239, 65, 121), _resolver.Resolve(new Bound<Rgba>(new Binding("type1.color")), "p"));
    }

    [Fact]
    public void Missing_key_throws_with_path()
    {
        var ex = Assert.Throws<TemplateException>(() => _resolver.Resolve(new Bound<bool>(new Binding("nope")), "elements[3].visible"));
        Assert.Equal("elements[3].visible", Assert.Single(ex.Issues).Path);
    }
}

public class DataPackTests
{
    [Fact]
    public void Sections_are_read_with_their_kinds()
    {
        var result = DataPackSerializer.Read("""
            {
              "formatVersion": 1,
              "values": {
                "text": { "species.name": "Hypno" },
                "int": { "level": 42 },
                "float": { "weight": 75.6 },
                "bool": { "hasType2": false },
                "color": { "type1.color": "#EF4179" },
                "image": { "species.sprite": "hypno.png", "ball.sprite": { "png": "AQID" } }
              }
            }
            """, new MemoryAssets("hypno.png"));

        Assert.True(result.Success, string.Join("\n", result.Issues));
        var data = result.Pack!.Data;
        Assert.Equal(new TextValue("Hypno"), data.Values["species.name"]);
        Assert.Equal(new IntValue(42), data.Values["level"]);
        Assert.Equal(new FloatValue(75.6), data.Values["weight"]);
        Assert.Equal(new BoolValue(false), data.Values["hasType2"]);
        Assert.Equal(new ColorValue(new Rgba(239, 65, 121)), data.Values["type1.color"]);
        Assert.Equal([1, 2, 3], Assert.IsType<ImageValue>(data.Values["ball.sprite"]).Image.Encoded);
        Assert.Equal(ValueKind.Image, data.Schema.Values["species.sprite"]);
    }

    [Fact]
    public void Key_in_two_sections_is_rejected()
    {
        var result = DataPackSerializer.Read("""{ "formatVersion": 1, "values": { "text": { "level": "x" }, "int": { "level": 1 } } }""", new MemoryAssets());
        Assert.Equal("values.int.level", Assert.Single(result.Issues).Path);
    }

    [Fact]
    public void Wrong_value_type_is_rejected()
    {
        var result = DataPackSerializer.Read("""{ "formatVersion": 1, "values": { "int": { "level": 1.5 } } }""", new MemoryAssets());
        Assert.Equal("values.int.level", Assert.Single(result.Issues).Path);
    }

    [Fact]
    public void Unsupported_version_is_rejected()
    {
        var result = DataPackSerializer.Read("""{ "formatVersion": 99 }""", new MemoryAssets());
        Assert.False(result.Success);
    }

    [Fact]
    public void Write_then_read_returns_the_same_data()
    {
        var pack = new DataPack("Hypno", "test", TestData.Sample);

        var json = DataPackSerializer.Write(pack);
        var read = DataPackSerializer.Read(json, new MemoryAssets());

        Assert.True(read.Success, string.Join("\n", read.Issues));
        Assert.Equal(pack.Description, read.Pack!.Description);
        Assert.Equal(pack.Data.Values, read.Pack.Data.Values);
    }
}
