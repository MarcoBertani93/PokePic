using System.Collections.Immutable;
using PKHeX.Core;
using PokePic.Adapter;
using PokePic.Core;
using PokePic.Core.Assets;
using PokePic.Core.Data;
using PokePic.Core.Rendering;
using PokePic.Png;
using PokePic.Rendering;

namespace PokePic.Adapter.Tests;

public class AdapterTests
{
    /// <summary>Charizard Lv. 50 nicknamed "Drago", holding Leftovers, with four moves.</summary>
    private static PK9 Charizard()
    {
        var pk = new PK9
        {
            Species = (ushort)PKHeX.Core.Species.Charizard,
            CurrentLevel = 50,
            Nickname = "Drago",
            IsNicknamed = true,
            HeldItem = 234,
            Ball = (byte)PKHeX.Core.Ball.Ultra,
            Move1 = (ushort)Move.Flamethrower,
            Move2 = (ushort)Move.AirSlash,
            Move3 = (ushort)Move.DragonClaw,
            Move4 = (ushort)Move.Earthquake,
            IV_HP = 31, IV_ATK = 31, IV_DEF = 31, IV_SPA = 20, IV_SPD = 31, IV_SPE = 31,
            EV_ATK = 252, EV_SPE = 252, EV_HP = 4,
        };
        pk.SetMaximumPPCurrent([pk.Move1, pk.Move2, pk.Move3, pk.Move4]);
        return pk;
    }

    private sealed class NoSprites : ISpriteSource
    {
        public byte[]? Species(PKM pkm) => null;

        public byte[]? Ball(PKM pkm) => null;

        public byte[]? HeldItem(PKM pkm) => null;
    }

    private static string RepositoryRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PokePic.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new DirectoryNotFoundException("Repository root (PokePic.slnx) not found.");
        }
    }

    [Fact]
    public void A_pokemon_becomes_the_keys_the_templates_use()
    {
        var pack = PkmAdapter.ToDataPack(Charizard(), "it", new NoSprites());
        var v = pack.Data.Values;

        Assert.Equal(PkmFile.Name(Charizard()), pack.Name); // as PKHeX names its files
        Assert.Matches("^0006 .*- Drago - [0-9A-F]{12}$", pack.Name);
        Assert.Equal("it", pack.Language);
        Assert.Equal(new IntValue(6), v["species"]);
        Assert.Equal("Glurak", ((LocalizedValue)v["species.name"]).GetText("de"));
        Assert.Equal("Fuoco", ((LocalizedValue)v["type1.name"]).GetText("it"));
        Assert.Equal(new TextValue("flying"), v["type2.key"]);
        Assert.Equal(new BoolValue(true), v["hasType2"]);
        Assert.Equal(new TextValue("ground"), v["move4.type.key"]);
        Assert.Equal("Avanzi", ((LocalizedValue)v["heldItem.name"]).GetText("it"));
        Assert.Equal(new IntValue(175), v["iv.total"]);
        Assert.Equal(new IntValue(508), v["ev.total"]);
        Assert.True(((IntValue)v["stat.hp"]).Value > 100); // computed on a copy
        Assert.Equal(new Core.Model.Rgba(0xE6, 0x28, 0x29), ((ColorValue)v["type1.color"]).Value);
        Assert.IsType<ImageValue>(pack.Data.Values["species.sprite"]);
    }

    [Fact]
    public void Types_have_pkhex_and_pokeapi_numbers()
    {
        var v = PkmAdapter.ToDataPack(Charizard(), "en", new NoSprites()).Data.Values;

        Assert.Equal(new IntValue(9), v["type1.id"]);            // MoveType.Fire
        Assert.Equal(new IntValue(10), v["pokeapi.type1.id"]);
        Assert.Equal(new IntValue(2), v["type2.id"]);            // Flying
        Assert.Equal(new IntValue(15), v["move3.type.id"]);      // Dragon Claw: Dragon
        Assert.Equal(new IntValue(16), v["pokeapi.move3.type.id"]);
    }

    [Fact]
    public void Tera_type_exists_only_in_scarlet_violet_and_stellar_is_18()
    {
        var pk = Charizard();
        pk.TeraTypeOriginal = MoveType.Dragon;
        var v = PkmAdapter.ToDataPack(pk, "en", new NoSprites()).Data.Values;
        Assert.Equal(new BoolValue(true), v["hasTera"]);
        Assert.Equal(new TextValue("dragon"), v["teratype.key"]);
        Assert.Equal(new IntValue(15), v["teratype.id"]);

        pk.TeraTypeOverride = (MoveType)TeraTypeUtil.Stellar; // stored as 99
        v = PkmAdapter.ToDataPack(pk, "en", new NoSprites()).Data.Values;
        Assert.Equal(new TextValue("stellar"), v["teratype.key"]);
        Assert.Equal(new IntValue(18), v["teratype.id"]);
        Assert.Equal(new IntValue(19), v["pokeapi.teratype.id"]);

        // Before Scarlet/Violet there is no tera type: the keys repeat the first type.
        var old = PkmAdapter.ToDataPack(new PK7 { Species = (ushort)Species.Charizard, CurrentLevel = 50 }, "en", new NoSprites()).Data.Values;
        Assert.Equal(new BoolValue(false), old["hasTera"]);
        Assert.Equal(new TextValue("fire"), old["teratype.key"]);
    }

    [Fact]
    public void Mega_needs_a_species_with_a_mega_form_in_a_game_with_megas()
    {
        bool Mega(PKM pk) => ((BoolValue)PkmAdapter.ToDataPack(pk, "en", new NoSprites()).Data.Values["hasMega"]).Value;

        Assert.True(Mega(new PK7 { Species = (ushort)Species.Charizard, CurrentLevel = 50 }));  // Sun/Moon
        Assert.False(Mega(Charizard()));                                                          // Scarlet/Violet: no megas
        Assert.False(Mega(new PK7 { Species = (ushort)Species.Hypno, CurrentLevel = 50 }));     // no mega form
    }

    [Theory]
    [InlineData(6, 0, 0, false, false, "6", "6", "6")]
    [InlineData(6, 1, 0, false, false, "10034", "10034", "10034")]                  // Mega Charizard X
    [InlineData(37, 1, 0, false, false, "10103", "10103", "10103")]                 // Alolan Vulpix
    [InlineData(666, 0, 0, false, false, "666-icy-snow", "666-icy-snow", "666-icy-snow")] // PKHeX's first Vivillon is Icy Snow
    [InlineData(666, 2, 0, true, false, "666-tundra", "shiny/666-tundra", "shiny/666-tundra")]
    [InlineData(3, 0, 0, true, true, "3", "shiny/female/3", "shiny/3")]             // HOME has female images, the artwork does not
    [InlineData(3, 0, 0, false, false, "3", "3", "3")]
    [InlineData(869, 1, 3, false, false, "869-ruby-cream-star-sweet", "869-ruby-cream-star-sweet", "869-ruby-cream-star-sweet")]
    public void Pokeapi_images_follow_form_sex_and_shininess(ushort species, byte form, uint formArgument, bool shiny, bool female, string id, string path, string artwork)
    {
        var pk = new PK9 { Species = species, Form = form, CurrentLevel = 50, Gender = (byte)(female ? 1 : 0), PID = 0x12345678, ID32 = 0x0001E240 }; // not shiny (PID 0 and ID 0 would be)
        if (formArgument != 0)
            pk.FormArgument = formArgument;
        if (shiny)
            pk.SetShiny();
        var v = PkmAdapter.ToDataPack(pk, "en", new NoSprites()).Data.Values;

        Assert.Equal(new TextValue(id), v["pokeapi.id"]);
        Assert.Equal(new TextValue(path), v["pokeapi.path"]);
        Assert.Equal(new TextValue(artwork), v["pokeapi.artwork"]);
    }

    [Fact]
    public void A_form_without_its_own_pokeapi_image_uses_the_species_one()
    {
        // Cosplay Pikachu (Gen 6 form 1, Rock Star) has no HOME image.
        var pk = new PK6 { Species = (ushort)Species.Pikachu, Form = 1, CurrentLevel = 20, PID = 0x12345678, ID32 = 0x0001E240 };
        var v = PkmAdapter.ToDataPack(pk, "en", new NoSprites()).Data.Values;

        Assert.Equal(new TextValue("10080"), v["pokeapi.id"]);
        Assert.Equal(new TextValue("25"), v["pokeapi.path"]);
    }

    [Theory]
    [InlineData(Nature.Modest, "spa", "atk")]
    [InlineData(Nature.Timid, "spe", "atk")]
    [InlineData(Nature.Jolly, "spe", "spa")]
    [InlineData(Nature.Calm, "spd", "atk")]
    [InlineData(Nature.Impish, "def", "spa")]
    public void The_nature_marks_the_stat_it_raises_and_the_one_it_lowers(Nature nature, string up, string down)
    {
        var pk = Charizard();
        pk.Nature = nature;
        var v = PkmAdapter.ToDataPack(pk, "en", new NoSprites()).Data.Values;

        foreach (var stat in new[] { "atk", "def", "spa", "spd", "spe" })
        {
            Assert.Equal(new BoolValue(stat == up), v[$"nature.up.{stat}"]);
            Assert.Equal(new BoolValue(stat == down), v[$"nature.down.{stat}"]);
        }
    }

    [Fact]
    public void Neutral_natures_mark_nothing()
    {
        var pk = Charizard();
        pk.Nature = Nature.Hardy;
        var v = PkmAdapter.ToDataPack(pk, "en", new NoSprites()).Data.Values;
        Assert.DoesNotContain(v, kv => kv.Key.StartsWith("nature.", StringComparison.Ordinal) && kv.Value is BoolValue { Value: true });
    }

    [Fact]
    public void The_pokemon_in_pkhex_is_not_changed()
    {
        var pk = Charizard();
        var before = PkmFile.Bytes(pk);
        PkmAdapter.ToDataPack(pk, "en", new NoSprites());
        Assert.Equal(before, PkmFile.Bytes(pk));
    }

    [Fact]
    public void Every_template_works_with_the_data_of_a_real_pokemon()
    {
        var pack = PkmAdapter.ToDataPack(Charizard(), "en", new NoSprites());
        foreach (var path in Directory.GetFiles(Path.Combine(RepositoryRoot, "resources", "templates"), "template.json", SearchOption.AllDirectories))
        {
            var loaded = TemplateLoader.LoadFile(path, pack.Data.Schema);
            Assert.True(loaded.Success, $"{path}:\n{string.Join("\n", loaded.Issues)}");
        }

        var spotlight = Path.Combine(RepositoryRoot, "resources", "templates", "pixelcard", "template.json");
        var template = TemplateLoader.LoadFile(spotlight, pack.Data.Schema).Template!;
        using var prepared = new SkiaCardRenderer().Prepare(template, new FileSystemAssetProvider(Path.GetDirectoryName(spotlight)!));
        Assert.Equal(192, prepared.Render(pack.Data, RenderOptions.FromTemplate(template, "en")).Width / (int)template.Canvas.DefaultScale);
    }

    [Fact]
    public void A_card_brings_the_pokemon_back_byte_for_byte()
    {
        var pk = Charizard();
        var codec = new PngPayloadCodec();
        var payload = new CardPayload("spotlight", 1, ImmutableSortedDictionary<string, string>.Empty.Add("name", "Drago"), pk.Extension, [.. PkmFile.Bytes(pk)]);
        using var bitmap = new SkiaSharp.SKBitmap(4, 4);
        using var png = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);

        var card = codec.Embed(png.ToArray(), payload);
        var read = codec.Read(card);

        Assert.True(read.Success, read.Error);
        var back = PkmFile.Read([.. read.Payload!.Pkm], read.Payload.PkmFormat, new SimpleTrainerInfo());
        Assert.NotNull(back);
        Assert.Equal(PkmFile.Bytes(pk), PkmFile.Bytes(back!));
        Assert.Equal("Drago", read.Payload.Display["name"]);
        Assert.NotNull(SkiaSharp.SKBitmap.Decode(card)); // still a valid PNG
    }

    [Fact]
    public void Changed_or_missing_data_is_refused_with_a_reason()
    {
        var codec = new PngPayloadCodec();
        using var bitmap = new SkiaSharp.SKBitmap(1, 1);
        using var png = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        Assert.Contains("not a PokePic card", codec.Read(png.ToArray()).Error, StringComparison.Ordinal);
        Assert.False(codec.Read(png.ToArray()).IsCard); // PKHeX may try such a file itself
        Assert.Contains("not a PNG", codec.Read([1, 2, 3]).Error, StringComparison.Ordinal);

        var card = codec.Embed(png.ToArray(), new CardPayload("t", 1, ImmutableSortedDictionary<string, string>.Empty, "pk9", [1, 2, 3]));
        var text = System.Text.Encoding.Latin1.GetString(card);
        var tampered = System.Text.Encoding.Latin1.GetBytes(text.Replace("\"pkm\":\"AQID\"", "\"pkm\":\"AQIE\"", StringComparison.Ordinal));
        Assert.Contains("checksum", codec.Read(tampered).Error, StringComparison.Ordinal);
        Assert.True(codec.Read(tampered).IsCard); // a card that cannot be read: the user is told why
    }
}
