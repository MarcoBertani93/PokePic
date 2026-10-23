using System.Collections.Immutable;
using PokePic.Core;
using PokePic.Core.Assets;
using PokePic.Core.Data;
using PokePic.Core.Model;
using PokePic.Core.Rendering;
using SkiaSharp;

namespace PokePic.Rendering.Tests;

internal static class Support
{
    public static string AssetsDirectory => Path.Combine(AppContext.BaseDirectory, "Assets");

    public static string RepositoryRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PokePic.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root (PokePic.slnx) not found.");
        }
    }

    public static CardData SceneData(string type = "fire", bool hasType2 = true) => new(
        new Dictionary<string, CardValue>
        {
            ["color1"] = new ColorValue(new Rgba(230, 40, 41)),
            ["type"] = new TextValue(type),
            ["level"] = new IntValue(42),
            ["hasType2"] = new BoolValue(hasType2),
            ["sprite"] = new ImageValue(CardImage.FromBytes(File.ReadAllBytes(Path.Combine(AssetsDirectory, "hypno.png")))),
        }.ToImmutableSortedDictionary(StringComparer.Ordinal));

    /// <summary>
    /// Loads (and validates) a template from the test assets.
    /// </summary>
    public static (CardTemplate Template, IAssetProvider Assets) LoadScene(CardData data, string file = "scene.json")
    {
        var path = Path.Combine(AssetsDirectory, file);
        var result = TemplateLoader.LoadFile(path, data.Schema);
        Assert.True(result.Success, string.Join("\n", result.Issues));
        return (result.Template!, new FileSystemAssetProvider(AssetsDirectory));
    }

    public static RenderedCard Render(CardTemplate template, IAssetProvider assets, CardData data, RenderMode mode, float scale, string language = "en", string? languageOverride = null)
    {
        using var prepared = new SkiaCardRenderer().Prepare(template, assets);
        return prepared.Render(data, new RenderOptions(mode, scale, language, languageOverride));
    }

    /// <summary>
    /// Decodes a PNG into unpremultiplied RGBA pixels.
    /// </summary>
    public static (int Width, int Height, byte[] Rgba) Decode(byte[] png)
    {
        using var codec = SKCodec.Create(new MemoryStream(png)) ?? throw new InvalidDataException("Not an image.");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var result = codec.GetPixels(info, out byte[] pixels);
        if (result != SKCodecResult.Success)
            throw new InvalidDataException($"Decode failed: {result}.");
        return (info.Width, info.Height, pixels);
    }
}

/// <summary>
/// Pixel-by-pixel comparison with reference images in tests/golden.
/// Set POKEPIC_UPDATE_GOLDEN=1 to (re)generate them — and explain in the commit why rendering changed.
/// </summary>
internal static class Golden
{
    public static void AssertMatches(string name, byte[] png)
    {
        var goldenPath = Path.Combine(Support.RepositoryRoot, "tests", "golden", name + ".png");
        if (Environment.GetEnvironmentVariable("POKEPIC_UPDATE_GOLDEN") == "1")
        {
            File.WriteAllBytes(goldenPath, png);
            return;
        }

        var actualPath = Path.Combine(Support.RepositoryRoot, "tests", "golden-actual", name + ".png");
        if (!File.Exists(goldenPath))
        {
            Save(actualPath, png);
            Assert.Fail($"Golden image {goldenPath} is missing. Actual output saved to {actualPath}.");
        }

        var expected = Support.Decode(File.ReadAllBytes(goldenPath));
        var actual = Support.Decode(png);
        if ((expected.Width, expected.Height) != (actual.Width, actual.Height))
        {
            Save(actualPath, png);
            Assert.Fail($"Size {actual.Width}x{actual.Height} differs from golden {expected.Width}x{expected.Height}. Actual saved to {actualPath}.");
        }

        int differing = 0;
        for (int i = 0; i < actual.Rgba.Length; i += 4)
        {
            if (!actual.Rgba.AsSpan(i, 4).SequenceEqual(expected.Rgba.AsSpan(i, 4)))
                differing++;
        }
        if (differing > 0)
        {
            Save(actualPath, png);
            Assert.Fail($"{differing} pixels differ from {goldenPath}. Actual saved to {actualPath}.");
        }
    }

    private static void Save(string path, byte[] png)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, png);
    }
}
