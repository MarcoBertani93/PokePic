using PokePic.Core;
using PokePic.Core.Assets;
using PokePic.Core.Data;
using PokePic.Core.Json;
using PokePic.Core.Model;
using PokePic.Core.Rendering;

namespace PokePic.Rendering.Tests;

public class RenderingTests
{
    [Fact]
    public void Scene_pixel_perfect_matches_golden()
    {
        var data = Support.SceneData();
        var (template, assets) = Support.LoadScene(data);

        var card = Support.Render(template, assets, data, RenderMode.PixelPerfect, 1);

        Golden.AssertMatches("scene_pixelperfect_x1", card.Png);
    }

    [Fact]
    public void Spotlight_sample_pixel_perfect_matches_golden()
    {
        var root = Support.RepositoryRoot;
        var pack = DataPackSerializer.ReadFile(Path.Combine(root, "resources", "data", "charizard.json"));
        Assert.True(pack.Success, string.Join("\n", pack.Issues));
        var templatePath = Path.Combine(root, "resources", "templates", "pixelcard", "template.json");
        var loaded = TemplateLoader.LoadFile(templatePath, pack.Pack!.Data.Schema);
        Assert.True(loaded.Success, string.Join("\n", loaded.Issues));

        var card = Support.Render(loaded.Template!, new FileSystemAssetProvider(Path.GetDirectoryName(templatePath)!), pack.Pack.Data, RenderMode.PixelPerfect, 1, pack.Pack.Language!);

        Golden.AssertMatches("spotlight_charizard_pixelperfect_x1", card.Png);
    }

    [Fact]
    public void Default_language_template_language_and_text_language_apply_in_this_order()
    {
        var root = Support.RepositoryRoot;
        var pack = DataPackSerializer.ReadFile(Path.Combine(root, "resources", "data", "charizard.json")).Pack!;
        var templatePath = Path.Combine(root, "resources", "templates", "pixelcard", "template.json");
        var assets = new FileSystemAssetProvider(Path.GetDirectoryName(templatePath)!);
        var template = TemplateLoader.LoadFile(templatePath, pack.Data.Schema).Template!;

        byte[] Render(CardTemplate t, string language) => Support.Render(t, assets, pack.Data, RenderMode.PixelPerfect, 1, language).Png;

        var italian = Render(template, "it");
        var german = Render(template, "de");                       // "Decisa" → "Hart", "Lanciafiamme" → "Flammenwurf"…
        Assert.NotEqual(italian, german);
        Assert.Equal(german, Render(template with { Language = "de" }, "it"));   // the template wins over the default
        var english = Render(template, "en");
        Assert.NotEqual(italian, english);
        Assert.Equal(english, Render(template, "ko"));             // Korean is not in the data: English

        // The editor's preview language wins over the template's.
        var overridden = Support.Render(template with { Language = "de" }, assets, pack.Data, RenderMode.PixelPerfect, 1, "it", languageOverride: "en").Png;
        Assert.Equal(english, overridden);
    }

    [Fact]
    public void Pixel_perfect_produces_no_partial_alpha_from_antialiasing()
    {
        var data = Support.SceneData();
        var (template, assets) = Support.LoadScene(data);

        var (_, _, rgba) = Support.Decode(Support.Render(template, assets, data, RenderMode.PixelPerfect, 1).Png);

        for (int i = 3; i < rgba.Length; i += 4)
            Assert.True(rgba[i] is 0 or 255, $"Pixel {i / 4} has alpha {rgba[i]}.");
    }

    [Fact]
    public void High_resolution_is_antialiased()
    {
        var data = Support.SceneData();
        var (template, assets) = Support.LoadScene(data);

        var (_, _, rgba) = Support.Decode(Support.Render(template, assets, data, RenderMode.HighResolution, 2).Png);

        bool partial = false;
        for (int i = 3; i < rgba.Length && !partial; i += 4)
            partial = rgba[i] is > 0 and < 255;
        Assert.True(partial, "Expected anti-aliased edges with partial alpha.");
    }

    [Fact]
    public void Pixel_perfect_upscale_is_nearest_neighbor()
    {
        var data = Support.SceneData();
        var (template, assets) = Support.LoadScene(data);

        var x1 = Support.Decode(Support.Render(template, assets, data, RenderMode.PixelPerfect, 1).Png);
        var x3 = Support.Decode(Support.Render(template, assets, data, RenderMode.PixelPerfect, 3).Png);

        Assert.Equal((x1.Width * 3, x1.Height * 3), (x3.Width, x3.Height));
        for (int y = 0; y < x3.Height; y++)
        {
            for (int x = 0; x < x3.Width; x++)
            {
                var big = x3.Rgba.AsSpan((y * x3.Width + x) * 4, 4);
                var small = x1.Rgba.AsSpan((y / 3 * x1.Width + x / 3) * 4, 4);
                Assert.True(big.SequenceEqual(small), $"Pixel ({x},{y}) differs from base pixel ({x / 3},{y / 3}).");
            }
        }
    }

    [Theory]
    [InlineData(RenderMode.PixelPerfect, 2f)]
    [InlineData(RenderMode.HighResolution, 2.5f)]
    public void Rendering_is_deterministic(RenderMode mode, float scale)
    {
        var data = Support.SceneData();
        var (template, assets) = Support.LoadScene(data);

        var a = Support.Render(template, assets, data, mode, scale);
        var b = Support.Render(template, assets, data, mode, scale);

        Assert.Equal(a.Png, b.Png);
    }

    [Fact]
    public void Hidden_layer_is_not_drawn()
    {
        var visible = Support.SceneData(hasType2: false); // the last layer (magenta) becomes visible
        var (template, assets) = Support.LoadScene(visible);

        var (_, _, rgba) = Support.Decode(Support.Render(template, assets, visible, RenderMode.PixelPerfect, 1).Png);

        Assert.Equal([255, 0, 255, 255], rgba[..4]);
    }

    [Fact]
    public void Unknown_image_set_key_fails_with_the_element_path()
    {
        var data = Support.SceneData(type: "water");
        var (template, assets) = Support.LoadScene(data);

        var ex = Assert.Throws<TemplateException>(() => Support.Render(template, assets, data, RenderMode.PixelPerfect, 1));

        Assert.Equal("elements[5].image", Assert.Single(ex.Issues).Path);
    }

    [Fact]
    public void Image_set_uses_the_first_existing_candidate_then_the_fallback()
    {
        // Assets holds fire.png and hypno.png: "{type}" = "water" has no file, "hypno" has.
        var data = Support.SceneData(type: "water");
        var (scene, assets) = Support.LoadScene(data);
        CardTemplate WithKeys(params string[] keys) => scene with
        {
            Elements = [new ImageElement("elements[0]", null, new Literal<bool>(true), 1,
                new RectValue(new Literal<float>(0), new Literal<float>(0), new Literal<float>(64), new Literal<float>(48)),
                new LookupExpr(new SetLookup("types", [.. keys.Select(k => BindingSyntax.ParseText(k, out _)!)])),
                ImageFit.Contain, Anchor.Center, Flip.None, null)],
        };

        var hypno = Support.Render(WithKeys("hypno"), assets, data, RenderMode.PixelPerfect, 1).Png;
        Assert.Equal(hypno, Support.Render(WithKeys("{type}", "hypno"), assets, data, RenderMode.PixelPerfect, 1).Png);

        var ex = Assert.Throws<TemplateException>(() => Support.Render(WithKeys("{type}", "grass"), assets, data, RenderMode.PixelPerfect, 1));
        Assert.Contains("\"water\", \"grass\"", Assert.Single(ex.Issues).Message, StringComparison.Ordinal);

        var withFallback = WithKeys("{type}") with
        {
            Values = scene.Values.SetItem("types", new TemplateValue("values.image.types", Core.Data.ValueKind.Image, new PatternDefinition(
                new SingleBody(new ComposedExpr(BindingSyntax.ParseText("{key}.png", out _)!)),
                new SingleBody(new ComposedExpr(TextTemplate.FromLiteral("hypno.png")))))),
        };
        Assert.Equal(hypno, Support.Render(withFallback, assets, data, RenderMode.PixelPerfect, 1).Png);
    }

    [Fact]
    public void Image_set_key_from_data_cannot_leave_the_template_folder()
    {
        var data = Support.SceneData(type: "../fire");
        var (scene, assets) = Support.LoadScene(data);

        var ex = Assert.Throws<TemplateException>(() => Support.Render(scene, assets, data, RenderMode.PixelPerfect, 1));

        Assert.Contains("invalid key", Assert.Single(ex.Issues).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void System_fonts_are_loaded_by_family_and_missing_ones_are_reported()
    {
        var installed = SkiaSharp.SKTypeface.Default.FamilyName;
        string Json(string family) => $$"""
            { "id": "t", "canvas": { "width": 40, "height": 12 },
              "elements": [ { "type": "text", "rect": [0, 0, 40, 12], "text": "Abc", "font": "system:{{family}}", "size": 10 } ] }
            """;
        var renderer = new SkiaCardRenderer();

        using (var prepared = renderer.Prepare(TemplateParser.Parse(Json(installed)).Template!, new BytesAssets()))
        {
            var (_, _, rgba) = Support.Decode(prepared.Render(CardData.Empty, new RenderOptions(RenderMode.PixelPerfect, 1)).Png);
            Assert.Contains(rgba.Where((_, i) => i % 4 == 3), a => a == 255); // some text was drawn
        }

        var ex = Assert.Throws<TemplateException>(() => renderer.Prepare(TemplateParser.Parse(Json("No Such Font 4242")).Template!, new BytesAssets()));
        Assert.Equal("elements[0].font", Assert.Single(ex.Issues).Path);
    }

    [Fact]
    public void Localized_image_sets_follow_the_language_like_texts()
    {
        static byte[] Png(SkiaSharp.SKColor color)
        {
            using var bitmap = new SkiaSharp.SKBitmap(1, 1);
            bitmap.SetPixel(0, 0, color);
            using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
        string Json(string language) => $$"""
            { "id": "t", "canvas": { "width": 1, "height": 1 },
              "values": { "image": { "badges": { "pattern": "badges/{language}/{key}.png" } } },
              "elements": [ { "type": "image", "rect": [0, 0, 1, 1], "image": { "set": "badges", "key": "fire" }{{language}} } ] }
            """;
        var assets = new BytesAssets(("badges/it/fire.png", Png(SkiaSharp.SKColors.Red)), ("badges/en/fire.png", Png(SkiaSharp.SKColors.Blue)));
        byte[] Pixel(string json, string language)
        {
            using var prepared = new SkiaCardRenderer().Prepare(TemplateParser.Parse(json).Template!, assets);
            return Support.Decode(prepared.Render(CardData.Empty, new RenderOptions(RenderMode.PixelPerfect, 1, language)).Png).Rgba[..3];
        }

        Assert.Equal([255, 0, 0], Pixel(Json(""), "it"));
        Assert.Equal([255, 0, 0], Pixel(Json(""), "it-CH"));   // base language
        Assert.Equal([0, 0, 255], Pixel(Json(""), "de"));      // English
        Assert.Equal([0, 0, 255], Pixel(Json(""", "language": "en" """), "it")); // the element forces its language
    }

    [Theory]
    [InlineData("none", true)]
    [InlineData("crop", false)]
    [InlineData("ellipsis", false)]
    public void Text_overflow_decides_what_is_drawn_outside_the_rectangle(string overflow, bool outside)
    {
        var family = SkiaSharp.SKTypeface.Default.FamilyName;
        var json = $$"""
            { "id": "t", "canvas": { "width": 60, "height": 12 },
              "elements": [ { "type": "text", "rect": [0, 0, 20, 12], "text": "WWWWWWWWWW", "font": "system:{{family}}", "size": 10,
                              "overflow": "{{overflow}}", "ellipsis": "." } ] }
            """;
        using var prepared = new SkiaCardRenderer().Prepare(TemplateParser.Parse(json).Template!, new BytesAssets());
        var (width, _, rgba) = Support.Decode(prepared.Render(CardData.Empty, new RenderOptions(RenderMode.PixelPerfect, 1)).Png);

        bool Drawn(Func<int, bool> column) => Enumerable.Range(0, rgba.Length / 4).Any(i => column(i % width) && rgba[i * 4 + 3] > 0);
        Assert.True(Drawn(x => x < 20));
        Assert.Equal(outside, Drawn(x => x >= 20));
    }

    [Theory]
    [InlineData("""[0, 0, 4, 4], "fit": "scale", "scale": 2""", 4)]               // 1×1 image at scale 2: 2×2 pixels
    [InlineData("""[0, 0, 1, 1], "fit": "scale", "scale": 3""", 9)]               // drawn outside its rectangle
    [InlineData("""[0, 0, 1, 1], "fit": "scale", "scale": 3, "overflow": "crop" """, 1)] // only inside
    public void Images_can_have_a_fixed_scale_and_be_cropped(string placement, int drawnPixels)
    {
        using var bitmap = new SkiaSharp.SKBitmap(1, 1);
        bitmap.SetPixel(0, 0, SkiaSharp.SKColors.Red);
        using var png = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        var json = $$"""
            { "id": "t", "canvas": { "width": 4, "height": 4 }, "values": { "image": { "dot": "dot.png" } },
              "elements": [ { "type": "image", "image": "{dot}", "anchor": "topLeft", "rect": {{placement}} } ] }
            """;
        var parsed = TemplateParser.Parse(json);
        Assert.True(parsed.Success, string.Join("\n", parsed.Issues));
        using var prepared = new SkiaCardRenderer().Prepare(parsed.Template!, new BytesAssets(("dot.png", png.ToArray())));
        var (_, _, rgba) = Support.Decode(prepared.Render(CardData.Empty, new RenderOptions(RenderMode.PixelPerfect, 1)).Png);

        Assert.Equal(drawnPixels, Enumerable.Range(0, rgba.Length / 4).Count(i => rgba[i * 4 + 3] > 0));
    }

    [Fact]
    public void Prepare_rejects_undecodable_assets()
    {
        var parsed = TemplateParser.Parse("""{ "id": "t", "canvas": { "width": 4, "height": 4 }, "values": { "image": { "bad": "bad.png" } }, "elements": [] }""");
        var assets = new BytesAssets(("bad.png", [1, 2, 3, 4]));

        var ex = Assert.Throws<TemplateException>(() => new SkiaCardRenderer().Prepare(parsed.Template!, assets));

        Assert.Equal("values.image.bad", Assert.Single(ex.Issues).Path);
    }

    [Fact]
    public void Pixel_perfect_rejects_fractional_scale()
    {
        var data = Support.SceneData();
        var (template, assets) = Support.LoadScene(data);
        using var prepared = new SkiaCardRenderer().Prepare(template, assets);

        Assert.Throws<ArgumentException>(() => prepared.Render(data, new RenderOptions(RenderMode.PixelPerfect, 1.5f)));
    }

    private sealed class BytesAssets(params (string Path, byte[] Bytes)[] files) : IAssetProvider
    {
        public bool Exists(string path) => files.Any(f => f.Path == path);

        public bool DirectoryExists(string path) => path.Length == 0;

        public byte[] Read(string path) => files.First(f => f.Path == path).Bytes;

        public string Describe(string path) => path;
    }
}
