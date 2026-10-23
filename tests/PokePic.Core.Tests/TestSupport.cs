using System.Collections.Immutable;
using PokePic.Core.Assets;
using PokePic.Core.Data;
using PokePic.Core.Model;

namespace PokePic.Core.Tests;

/// <summary>
/// Assets held in memory, keyed by relative path.
/// </summary>
internal sealed class MemoryAssets(params string[] existing) : IAssetProvider
{
    private readonly HashSet<string> _files = new(existing, StringComparer.Ordinal);

    public bool Exists(string path) => _files.Contains(path);

    public bool DirectoryExists(string path) => path.Length == 0 || _files.Any(f => f.StartsWith(path + "/", StringComparison.Ordinal));

    public byte[] Read(string path) => Exists(path) ? [1, 2, 3] : throw new FileNotFoundException(path);

    public string Describe(string path) => $"memory:{path}";
}

internal static class TestData
{
    public static CardData Create(Action<Dictionary<string, CardValue>>? values = null, params string[] images)
    {
        var dict = new Dictionary<string, CardValue>(StringComparer.Ordinal);
        values?.Invoke(dict);
        foreach (var image in images)
            dict[image] = new ImageValue(CardImage.FromBytes([1]));
        return new CardData(dict.ToImmutableSortedDictionary(StringComparer.Ordinal));
    }

    public static CardData Sample => Create(v =>
    {
        v["species.name"] = new TextValue("Hypno");
        v["level"] = new IntValue(42);
        v["hasType2"] = new BoolValue(false);
        v["type1.color"] = new ColorValue(new Rgba(239, 65, 121));
        v["type1.key"] = new TextValue("psychic");
        v["weight"] = new FloatValue(75.6);
    }, "species.sprite");

    /// <summary>
    /// Wraps elements in a minimal template.
    /// </summary>
    public static string Template(string elements, string extra = "") => $$"""
        {
          "id": "test",
          "canvas": { "width": 100, "height": 50 },
          {{extra}}
          "elements": [ {{elements}} ]
        }
        """;
}

internal static class RepositoryPaths
{
    /// <summary>
    /// Folder containing PokePic.slnx.
    /// </summary>
    public static string Root
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PokePic.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root (PokePic.slnx) not found.");
        }
    }
}
