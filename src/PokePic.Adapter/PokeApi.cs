using System.Collections.Immutable;
using System.Text.Json;
using PKHeX.Core;

namespace PokePic.Adapter;

/// <summary>
/// The names of a Pokémon's images in the PokeAPI sprites repository (github.com/PokeAPI/sprites), from PokeApi.json,
/// which build/generate-pokeapi.cs generates. A form missing from the table uses the species number.
/// The image collections of PokeAPI (sprites/pokemon/, …/other/home/, …/other/showdown/…) share one layout:
/// "&lt;name&gt;", "shiny/&lt;name&gt;", "female/&lt;name&gt;", "shiny/female/&lt;name&gt;". The table says which variants exist,
/// as checked in other/home, the most complete collection; the official artwork has no female images and is checked
/// on its own.
/// </summary>
public static class PokeApi
{
    /// <param name="Id">The image name: "6", "10034" (Mega Charizard X), "666-tundra" (Vivillon).</param>
    /// <param name="Path">The path of the image, relative to a PokeAPI image collection, with the variants that exist: "shiny/female/3", "10034"…</param>
    /// <param name="Artwork">The official artwork, which has no female images: "shiny/10034"…</param>
    public sealed record Images(string Id, string Path, string Artwork);

    private sealed record Entry(string? Id, bool NoImage, bool NoArtwork, bool Female, bool FemaleShiny, bool NoShiny, bool NoArtworkShiny);

    private static readonly Entry Default = new(null, false, false, false, false, false, false);

    private static readonly ImmutableDictionary<string, Entry> Table = Load();

    public static Images For(PKM pk)
    {
        var species = pk.Species;
        uint formArgument = pk is IFormArgument f ? f.FormArgument : 0;
        var entry = Find(species, pk.Form, formArgument, pk.Context);
        var id = entry.Id ?? species.ToString(System.Globalization.CultureInfo.InvariantCulture);
        bool shiny = pk.IsShiny, female = pk.Gender == 1;

        // Without its own image a form shows the species (with the species' variants).
        var image = entry.NoImage ? Find(species, 0, 0, pk.Context) : entry;
        var imageId = entry.NoImage ? species.ToString(System.Globalization.CultureInfo.InvariantCulture) : id;
        var variant = (shiny && !image.NoShiny ? "shiny/" : "") + (female && (shiny ? image.FemaleShiny : image.Female) ? "female/" : "");

        var artwork = entry.NoArtwork ? Find(species, 0, 0, pk.Context) : entry;
        var artworkId = entry.NoArtwork ? species.ToString(System.Globalization.CultureInfo.InvariantCulture) : id;
        return new Images(id, variant + imageId, (shiny && !artwork.NoArtworkShiny ? "shiny/" : "") + artworkId);
    }

    private static Entry Find(ushort species, byte form, uint formArgument, EntityContext context)
    {
        if (species == (ushort)PKHeX.Core.Species.Alcremie && Table.TryGetValue($"{species}-{form}-{formArgument}", out var alcremie))
            return alcremie;
        return Table.TryGetValue($"{species}-{form}@{context}", out var inContext) ? inContext
            : Table.TryGetValue($"{species}-{form}", out var entry) ? entry
            : Default;
    }

    private static ImmutableDictionary<string, Entry> Load()
    {
        using var stream = typeof(PokeApi).Assembly.GetManifestResourceStream("PokePic.Adapter.PokeApi.json")
            ?? throw new InvalidOperationException("PokeApi.json is not embedded in PokePic.Adapter.");
        using var json = JsonDocument.Parse(stream);
        var table = ImmutableDictionary.CreateBuilder<string, Entry>(StringComparer.Ordinal);
        foreach (var property in json.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
                continue; // the comment
            var e = property.Value;
            bool Flag(string name) => e.TryGetProperty(name, out var v) && v.GetBoolean();
            table[property.Name] = new Entry(
                e.TryGetProperty("id", out var id) ? id.GetString() : null,
                Flag("noImage"), Flag("noArtwork"), Flag("female"), Flag("femaleShiny"), Flag("noShiny"), Flag("noArtworkShiny"));
        }
        return table.ToImmutable();
    }
}
