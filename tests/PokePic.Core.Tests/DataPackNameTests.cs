using PokePic.Core.Data;

namespace PokePic.Core.Tests;

public class DataPackNameTests
{
    private static string? Name(string extra, string text, string sections = "") => DataPackSerializer.Read($$"""
        { "formatVersion": 1, "language": "de", {{extra}}
          "values": { {{sections}} "text": { {{text}} "species.name": { "en": "Charizard", "de": "Glurak" } } } }
        """, new MemoryAssets()).Pack!.EffectiveName;

    [Fact]
    public void The_name_is_the_pack_name_else_the_nickname_else_the_species_in_the_pack_language()
    {
        Assert.Equal("Drago", Name(""" "name": "Drago", """, """ "nickname": "X", """));
        Assert.Equal("Fiamma", Name("", """ "nickname": "Fiamma", """));
        Assert.Equal("Glurak", Name("", ""));
        Assert.Equal("Glurak", Name("", """ "nickname": "Charizard", """, """ "bool": { "isNicknamed": false }, """));
    }
}
