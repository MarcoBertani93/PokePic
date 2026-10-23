using System.Collections.Immutable;
using PokePic.Core.Data;
using PokePic.Core.Editing;
using PokePic.Core.Json;
using PokePic.Core.Model;
using PokePic.Core.Resolution;

namespace PokePic.Core.Tests;

/// <summary>
/// The template's "values": a section per kind, four forms (single, by language, items, pattern), references between values.
/// </summary>
public class ValuesTests
{
    private const string Values = """
        "values": {
          "text": {
            "title": "{species.name:upper} Lv. {lv}",
            "header": "{title} ({type1.key})",
            "lvLabel": { "en": "Level {level}", "it": "Livello {level}" },
            "typeShort": { "items": { "psychic": { "en": "PSY", "it": "PSI" } }, "fallback": "???" },
            "code": { "pattern": "T-{key}" }
          },
          "int": { "lv": "{level}", "pad": 4, "max": { "items": { "42": 100, "50": 120 } } },
          "float": { "ratio": 1.5, "half": "{lv}" },
          "bool": { "single": "{!hasType2}", "debug": false, "isPsychic": { "items": { "psychic": true }, "fallback": false } },
          "color": {
            "accent": "{type1.color}",
            "border": "#30303080",
            "typeColor": { "items": { "psychic": "#F85888", "fire": "#F08030" }, "fallback": "#888888" },
            "byLanguage": { "en": "#FF0000", "it": "#00FF00" },
            "tint": "{typeColor[type1.key]}"
          },
          "image": { "frame": "images/frame.png", "art": { "pattern": "art/{key}.png", "fallback": "art/none.png" }, "sprite": "{species.sprite}" },
          "font": { "body": { "en": "fonts/latin.ttf", "ja": "fonts/japanese.ttf" }, "ui": "system:Arial" }
        },
        """;

    private static CardTemplate Parse(string extra = Values)
    {
        var result = TemplateParser.Parse(TestData.Template("", extra));
        Assert.True(result.Success, string.Join("\n", result.Issues));
        return result.Template!;
    }

    private static IReadOnlyList<TemplateIssue> Load(string elements, string extra = Values) =>
        TemplateLoader.Load(TestData.Template(elements, extra), TestData.Sample.Schema,
            new MemoryAssets("images/frame.png", "art/none.png", "fonts/latin.ttf", "fonts/japanese.ttf")).Issues;

    private static string Text(BindingResolver r, string text, string? language = null) =>
        r.Resolve(BindingSyntax.ParseText(text, out _)!, "p", language);

    [Fact]
    public void Each_section_gives_the_kind_and_every_form_is_parsed()
    {
        var values = Parse().Values;

        Assert.Equal(ValueKind.Color, values["accent"].Kind);
        Assert.Equal(new PlainDefinition(new SingleBody(new AliasExpr(new Binding("type1.color")))), values["accent"].Definition);
        Assert.IsType<ByLanguageBody>(((PlainDefinition)values["lvLabel"].Definition).Body);
        Assert.IsType<ItemsDefinition>(values["typeColor"].Definition);
        Assert.IsType<PatternDefinition>(values["art"].Definition);
        Assert.Equal(ValueKind.Font, values["body"].Kind);
        Assert.Equal("values.int.pad", values["pad"].Path);
    }

    [Fact]
    public void Values_resolve_through_data_and_other_values_in_the_language()
    {
        var r = BindingResolver.For(Parse(), TestData.Sample, "it");

        Assert.Equal("HYPNO Lv. 42 (psychic)", Text(r, "{header}"));
        Assert.Equal("Livello 42", Text(r, "{lvLabel}"));
        Assert.Equal("Level 42", Text(r, "{lvLabel}", "ja"));
        Assert.Equal(new Rgba(239, 65, 121), r.Resolve(new Bound<Rgba>(new Binding("accent")), "p"));
        Assert.Equal(new Rgba(0, 255, 0), r.Resolve(new Bound<Rgba>(new Binding("byLanguage")), "p"));
        Assert.True(r.Resolve(new Bound<bool>(new Binding("single")), "p"));
        Assert.Equal(4f, r.Resolve(new Bound<float>(new Binding("pad")), "p"));
        Assert.Equal(42f, r.Resolve(new Bound<float>(new Binding("half")), "p")); // an int fits where a decimal goes
    }

    [Fact]
    public void Sets_pick_an_item_by_the_key_value_with_their_language_and_fallback()
    {
        var r = BindingResolver.For(Parse(), TestData.Sample, "it");

        Assert.Equal(new Rgba(0xF8, 0x58, 0x88), r.Resolve(new Bound<Rgba>(new Binding("tint")), "p"));
        Assert.Equal(100f, r.Resolve(new Bound<float>(new Binding("level", Set: "max")), "p")); // 42 → "42"
        Assert.False(r.Resolve(new Bound<bool>(new Binding("type1.key", Negate: true, Set: "isPsychic")), "p"));
        Assert.Equal("PSI · psi", Text(r, "{typeShort[type1.key]} · {typeShort[type1.key]:lower}"));
        Assert.Equal("T-psychic", Text(r, "{code[type1.key]}"));
        Assert.Equal(new Rgba(0xF0, 0x80, 0x30), r.Resolve(new LookedUp<Rgba>(new SetLookup("typeColor", [TextTemplate.FromLiteral("x"), TextTemplate.FromLiteral("fire")])), "p"));

        var other = BindingResolver.For(Parse(), TestData.Create(v =>
        {
            v["type1.key"] = new TextValue("ice");
            v["level"] = new IntValue(7);
        }));
        Assert.Equal("???", Text(other, "{typeShort[type1.key]}"));
        Assert.Equal(new Rgba(0x88, 0x88, 0x88), other.Resolve(new Bound<Rgba>(new Binding("tint")), "p"));
        var ex = Assert.Throws<TemplateException>(() => other.Resolve(new Bound<float>(new Binding("level", Set: "max")), "p"));
        Assert.Contains("no fallback", ex.Issues[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Images_and_fonts_resolve_to_files_or_data_images()
    {
        var r = BindingResolver.For(Parse(), TestData.Sample, "ja", (file, _) => file != "art/psychic.png");

        Assert.Equal(new FileValue(ValueKind.Image, "images/frame.png"), r.ValueOf("frame", "p"));
        Assert.IsType<ImageValue>(r.ValueOf("sprite", "p")); // an image of the data
        Assert.Equal(new FileValue(ValueKind.Font, "fonts/japanese.ttf"), r.ValueOf("body", "p"));
        Assert.Equal(new FileValue(ValueKind.Font, "system:Arial"), r.ValueOf("ui", "p"));
        var art = r.ResolveFile(new ComposedExpr(BindingSyntax.ParseText("{art[type1.key]}", out _)!), ValueKind.Image, "p");
        Assert.Equal(new FileValue(ValueKind.Image, "art/none.png"), art); // no file for the key: the fallback
    }

    [Fact]
    public void Elements_use_values_with_braces_and_the_kind_is_checked()
    {
        var issues = Load("""
            { "type": "text", "rect": [0, 0, 1, 1], "text": "{lvLabel}", "font": "{body}", "size": 8 },
            { "type": "image", "rect": [0, 0, 1, 1], "image": "{frame}" },
            { "type": "image", "rect": [0, 0, 1, 1], "image": "{art[type1.key]}" },
            { "type": "shape", "rect": ["{pad}", 0, 1, 1], "fill": "{tint}", "visible": "{isPsychic[type1.key]}" },
            { "type": "shape", "rect": [0, 0, 1, 1], "fill": "{max[level]}" },
            { "type": "shape", "rect": [0, 0, 1, 1], "fill": "{typeColor}" },
            { "type": "image", "rect": [0, 0, 1, 1], "image": "{accent}" },
            { "type": "text", "rect": [0, 0, 1, 1], "text": "{frame}", "font": "{frame}", "size": 8 }
            """);

        Assert.Equal(["elements[4].fill", "elements[5].fill", "elements[6].image", "elements[7].text", "elements[7].font"], issues.Select(i => i.Path));
        Assert.Contains("is int, expected color", issues[0].Message, StringComparison.Ordinal);
        Assert.Contains("is a set", issues[1].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Aliases_must_have_the_kind_of_their_section()
    {
        var issues = Load("", """
            "values": {
              "color": { "a": "{level}", "b": "{nope}" },
              "int": { "c": "{weight}" },
              "bool": { "d": "{level}" },
              "text": { "e": "x {nope2}", "f": "{frame}" },
              "image": { "frame": "images/frame.png" }
            },
            """);

        Assert.Equal(["values.bool.d", "values.color.a", "values.color.b", "values.int.c", "values.text.e", "values.text.f"], issues.Select(i => i.Path).Order());
    }

    [Fact]
    public void Literals_and_forms_are_checked_when_parsed()
    {
        var result = TemplateParser.Parse(TestData.Template("", """
            "values": {
              "int": { "a": 1.5, "b": { "pattern": "{key}" } },
              "color": { "c": "red", "d": { "it": "#FFF" }, "e": { "items": { } }, "f": { "items": { "a.b": "#FFF" } } },
              "text": { "language": "x", "dup": "x", "g": { "items": { "x": "1" }, "pattern": "{key}" } },
              "image": { "h": "" },
              "bool": { "dup": true },
              "nope": { }
            },
            """));

        Assert.Equal(
            ["values.bool.dup", "values.color.c", "values.color.d", "values.color.e.items", "values.color.f.items.a.b",
             "values.image.h", "values.int.a", "values.int.b.pattern", "values.nope", "values.text.g", "values.text.language"],
            result.Issues.Select(i => i.Path).Order());
    }

    [Fact]
    public void Cycles_and_data_names_are_reported()
    {
        var issues = Load("", """
            "values": {
              "text": { "a": "{b}", "b": "{s[type1.key]}", "level": "x" },
              "color": { "s": { "items": { "psychic": "{c}" } }, "c": "#FFF" }
            },
            """);

        Assert.Contains(issues, i => i.Path == "values.text.level" && i.Message.Contains("already a data value", StringComparison.Ordinal));
        Assert.DoesNotContain(issues, i => i.Message.Contains("refers to itself", StringComparison.Ordinal));

        var loop = Load("", """ "values": { "text": { "a": "{b}", "b": "{t[type1.key]}", "t": { "items": { "psychic": "{a}" } } } }, """);
        Assert.Equal(["values.text.a", "values.text.b", "values.text.t"], loop.Where(i => i.Message.Contains("refers to itself", StringComparison.Ordinal)).Select(i => i.Path).Order());
    }

    [Fact]
    public void Lookup_syntax_is_parsed()
    {
        var text = BindingSyntax.ParseText("{!isPsychic[type1.key]} {typeShort[ type1.key ]:upper}", out var error);
        Assert.Null(error);
        var bindings = text!.Bindings.ToList();
        Assert.Equal(new Binding("type1.key", Negate: true, Set: "isPsychic"), bindings[0]);
        Assert.Equal(new Binding("type1.key", "upper", Set: "typeShort"), bindings[1]);
        Assert.Equal("{typeShort[type1.key]:upper}", bindings[1].ToString());

        Assert.Null(BindingSyntax.ParseText("{[type1.key]}", out _));
        Assert.Null(BindingSyntax.ParseText("{set[]}", out _));
        Assert.Null(BindingSyntax.ParseText("{set[a]b}", out _));
    }

    [Fact]
    public void Language_is_always_a_text_value()
    {
        var r = BindingResolver.For(Parse(), TestData.Sample, "fr");
        Assert.Equal("fr / ja", Text(r, "{language} / ") + Text(r, "{language}", "ja"));
    }
}

public class MultiElementTests
{
    private static TemplateDocument Doc() => TemplateDocument.TryParse(TestData.Template("""
        { "name": "A", "type": "shape", "rect": [0, 0, 1, 1], "fill": "#000" },
        { "name": "G", "type": "group", "position": [10, 20], "children": [
          { "name": "B", "type": "shape", "rect": [1, 1, 1, 1], "fill": "#000" }
        ] },
        { "name": "C", "type": "shape", "rect": [5, 5, 1, 1], "fill": "#000" }
        """), out _)!;

    [Fact]
    public void Group_many_wraps_elements_in_drawing_order_and_keeps_positions()
    {
        var doc = Doc();

        var group = doc.GroupMany(["elements[2]", "elements[1].children[0]", "elements[0]"], "New");

        Assert.Equal("elements[1]", group); // after G, where C (the frontmost) was
        var children = doc.ChildrenOf(group).Select(c => c!["name"]!.GetValue<string>());
        Assert.Equal(["A", "B", "C"], children);
        Assert.Equal("[11,21,1,1]", doc.GetElement($"{group}.children[1]")!["rect"]!.ToJsonString()); // B kept its canvas position
        Assert.Empty(doc.ChildrenOf("elements[0]")); // G is now empty
    }

    [Fact]
    public void Top_level_drops_elements_inside_selected_groups()
    {
        Assert.Equal(["elements[1]", "elements[2]"], TemplateDocument.TopLevel(["elements[1]", "elements[1].children[0]", "elements[2]"]));
    }

    [Fact]
    public void Paths_sort_in_drawing_order()
    {
        string[] paths = ["elements[10]", "elements[2].children[1]", "elements[2]", "elements[1]"];
        Assert.Equal(["elements[1]", "elements[2]", "elements[2].children[1]", "elements[10]"], paths.Order(TemplateDocument.PathComparer.Instance));
    }

    [Fact]
    public void Path_of_finds_a_node_by_identity()
    {
        var doc = Doc();
        var b = doc.GetElement("elements[1].children[0]")!;
        doc.Move("elements[0]", null, 2);
        Assert.Equal("elements[0].children[0]", doc.PathOf(b));
    }
}

public class SystemFontTests
{
    [Fact]
    public void System_fonts_need_no_declaration_but_a_family()
    {
        var ok = TemplateLoader.Load(
            TestData.Template("""{ "type": "text", "rect": [0, 0, 1, 1], "text": "x", "font": "system:Arial", "size": 8 }""", """ "values": { "font": { "ui": "system:Segoe UI" } }, """),
            TestData.Sample.Schema, new MemoryAssets());
        Assert.True(ok.Success, string.Join("\n", ok.Issues));

        var empty = TemplateLoader.Load(
            TestData.Template("""{ "type": "text", "rect": [0, 0, 1, 1], "text": "x", "font": "system: ", "size": 8 }"""),
            TestData.Sample.Schema, new MemoryAssets());
        Assert.Equal("elements[0].font", Assert.Single(empty.Issues).Path);
    }
}
