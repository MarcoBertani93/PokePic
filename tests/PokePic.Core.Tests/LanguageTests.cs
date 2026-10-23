using System.Collections.Immutable;
using PokePic.Core.Data;
using PokePic.Core.Json;
using PokePic.Core.Model;
using PokePic.Core.Rendering;
using PokePic.Core.Resolution;

namespace PokePic.Core.Tests;

public class LanguageTests
{
    private static LocalizedValue Charizard => LocalizedValue.Texts(new Dictionary<string, string>
    {
        ["en"] = "Charizard",
        ["it"] = "Charizard IT",
        ["ja"] = "リザードン",
        ["zh-Hans"] = "喷火龙",
    });

    [Theory]
    [InlineData("it", "it")]
    [InlineData("IT", "it")]
    [InlineData("it-IT", "it")]
    [InlineData("zh-hans", "zh-Hans")]
    [InlineData("ko", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    public void Unknown_languages_fall_back_to_english(string? requested, string expected) =>
        Assert.Equal(expected, Languages.Pick(Charizard.Values.Keys, requested));

    [Fact]
    public void Localized_value_requires_english_and_one_kind()
    {
        Assert.Throws<ArgumentException>(() => LocalizedValue.Texts([KeyValuePair.Create("it", "x")]));
        Assert.Throws<ArgumentException>(() => new LocalizedValue(ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal,
            [KeyValuePair.Create("en", (CardValue)new IntValue(1)), KeyValuePair.Create("it", (CardValue)new TextValue("x"))])));
    }

    [Fact]
    public void Resolver_uses_its_language_unless_a_text_overrides_it()
    {
        var data = TestData.Create(v =>
        {
            v["species.name"] = Charizard;
            v["nickname"] = new TextValue("Drago");
        });
        var text = BindingSyntax.ParseText("{nickname}: {species.name:upper}", out _)!;

        Assert.Equal("Drago: CHARIZARD", new BindingResolver(data).Resolve(text, "p"));
        Assert.Equal("Drago: CHARIZARD IT", new BindingResolver(data, "it").Resolve(text, "p"));
        Assert.Equal("Drago: リザードン", new BindingResolver(data, "it").Resolve(text, "p", language: "ja"));
        Assert.Equal("Drago: CHARIZARD", new BindingResolver(data, "ko").Resolve(text, "p"));
    }

    [Fact]
    public void Template_language_overrides_the_default_passed_by_the_caller()
    {
        var template = TemplateParser.Parse(TestData.Template("", """ "language": "ja", """)).Template!;

        Assert.Equal("ja", new RenderOptions(RenderMode.PixelPerfect, 1, "it").EffectiveLanguage(template));
        Assert.Equal("it", new RenderOptions(RenderMode.PixelPerfect, 1, "it").EffectiveLanguage(template with { Language = null }));
    }

    [Fact]
    public void Text_element_language_is_parsed_and_validated()
    {
        var ok = TemplateParser.Parse(TestData.Template("""{ "type": "text", "rect": [0, 0, 1, 1], "text": "x", "font": "f", "size": 8, "language": "ja" }"""));
        Assert.Equal("ja", Assert.IsType<TextElement>(Assert.Single(ok.Template!.Elements)).Language);

        var bad = TemplateParser.Parse(TestData.Template("""{ "type": "text", "rect": [0, 0, 1, 1], "text": "x", "font": "f", "size": 8, "language": "italiano" }"""));
        Assert.Equal("elements[0].language", Assert.Single(bad.Issues).Path);
    }

    [Fact]
    public void Data_pack_reads_and_writes_localized_texts_and_the_default_language()
    {
        var read = DataPackSerializer.Read("""
            {
              "formatVersion": 1,
              "language": "it",
              "values": {
                "text": { "nickname": "Drago", "species.name": { "en": "Charizard", "ja": "リザードン" } },
                "int": { "size": { "en": 1, "ja": 2 } },
                "color": { "accent": { "en": "#FF0000", "ja": "#0000FF" } },
                "image": { "logo": { "en": { "png": "AQID" }, "ja": { "png": "BAUG" } } }
              }
            }
            """, new MemoryAssets());

        Assert.True(read.Success, string.Join("\n", read.Issues));
        Assert.Equal("it", read.Pack!.Language);
        var name = Assert.IsType<LocalizedValue>(read.Pack.Data.Values["species.name"]);
        Assert.Equal("リザードン", name.GetText("ja"));
        var resolver = new BindingResolver(read.Pack.Data, "ja");
        Assert.Equal(new IntValue(2), resolver.ValueOf("size", "p"));
        Assert.Equal(new ColorValue(new Rgba(0, 0, 255)), resolver.ValueOf("accent", "p"));
        Assert.Equal([4, 5, 6], Assert.IsType<ImageValue>(resolver.ValueOf("logo", "p")).Image.Encoded);

        var again = DataPackSerializer.Read(DataPackSerializer.Write(read.Pack), new MemoryAssets());
        Assert.True(again.Success, string.Join("\n", again.Issues));
        Assert.Equal(read.Pack.Data.Values, again.Pack!.Data.Values);
        Assert.Equal("it", again.Pack.Language);
    }

    [Theory]
    [InlineData("""{ "it": "Charizard" }""", "values.text.species.name")]
    [InlineData("""{ "en": "Charizard", "italiano": "x" }""", "values.text.species.name.italiano")]
    [InlineData("""{ "en": 3 }""", "values.text.species.name.en")]
    public void Invalid_localized_texts_are_reported(string value, string path)
    {
        var read = DataPackSerializer.Read($$"""{ "formatVersion": 1, "values": { "text": { "species.name": {{value}} } } }""", new MemoryAssets());
        Assert.Contains(read.Issues, i => i.Path == path);
    }
}
