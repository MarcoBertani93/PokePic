using System.Collections.Immutable;
using PKHeX.Core;
using PokePic.Core.Data;
using PokePic.Core.Model;

namespace PokePic.Adapter;

/// <summary>
/// PNG images of a Pokémon, from whatever draws them (in the plugin: PKHeX's own sprites). Null: no image.
/// </summary>
public interface ISpriteSource
{
    byte[]? Species(PKM pkm);

    byte[]? Ball(PKM pkm);

    byte[]? HeldItem(PKM pkm);
}

/// <summary>
/// The only place that knows <see cref="PKM"/>: turns a Pokémon into a <see cref="DataPack"/> with the keys the
/// templates use (see the samples in resources/data). Texts are localized in every language PKHeX has;
/// conditions and totals are computed here, because templates have no logic.
/// </summary>
public static class PkmAdapter
{
    /// <summary>PKHeX's language codes are PokePic's: "ja", "en", "fr", "it", "de", "es", "es-419", "ko", "zh-Hans", "zh-Hant".</summary>
    private static readonly string[] AllLanguages = GameLanguage.AllSupportedLanguages.ToArray();

    /// <param name="language">The language of whoever uses the card (PKHeX's current language).</param>
    public static DataPack ToDataPack(PKM pkm, string language, ISpriteSource sprites, string source = "PKHeX")
    {
        // Stats are computed on a copy: the Pokémon being edited in PKHeX is never touched.
        var pk = pkm.Clone();
        pk.ResetPartyStats();

        var values = new Dictionary<string, CardValue>(StringComparer.Ordinal);
        void Int(string key, long value) => values[key] = new IntValue(value);
        void Bool(string key, bool value) => values[key] = new BoolValue(value);
        void Text(string key, string value) => values[key] = new TextValue(value);
        void Localized(string key, Func<GameStrings, string> text) => values[key] = Localize(text);

        var personal = pk.PersonalInfo;
        int type1 = personal.Type1, type2 = personal.Type2;
        bool hasType2 = type2 != type1;

        Int("species", pk.Species);
        Int("form", pk.Form);
        Int("level", pk.CurrentLevel);
        Int("friendship", pk.CurrentFriendship);
        Localized("species.name", s => s.specieslist[pk.Species]);
        Text("nickname", pk.Nickname);
        Bool("isNicknamed", pk.IsNicknamed);
        Bool("isShiny", pk.IsShiny);

        AddType("type1", type1);
        Bool("hasType2", hasType2);
        // Without a second type the keys still exist (same as the first), so templates validate for every Pokémon.
        AddType("type2", type2);

        // Terastallization exists only in Scarlet/Violet (PK9); elsewhere the tera keys repeat the first type.
        bool hasTera = pk is ITeraType && pk.Context == EntityContext.Gen9;
        Bool("hasTera", hasTera);
        AddType("teratype", pk is ITeraType tera && hasTera ? TeraTypeNumber(tera.TeraType) : type1);

        // The names of its images in the PokeAPI sprites repository, for the form, the sex and shininess.
        var pokeApi = PokeApi.For(pk);
        Text("pokeapi.id", pokeApi.Id);
        Text("pokeapi.path", pokeApi.Path);
        Text("pokeapi.artwork", pokeApi.Artwork);

        // The species has a Mega Evolution and the Pokémon's game allows Mega Evolution (X/Y to Sun/Moon, Legends Z-A).
        Bool("hasMega", FormInfo.HasMegaForm(pk.Species) && pk.Context.IsMegaContext);

        int gender = pk.Gender;
        Text("gender.key", gender switch { 0 => "male", 1 => "female", _ => "neutral" });
        Text("gender.symbol", gender switch { 0 => "♂", 1 => "♀", _ => "" });
        Bool("gender.isMale", gender == 0);
        Bool("gender.isFemale", gender == 1);
        Bool("gender.isNeutral", gender is not (0 or 1));

        Localized("nature.name", s => s.natures[(int)pk.Nature]);
        Localized("ability.name", s => s.abilitylist[pk.Ability]);
        Localized("heldItem.name", s => pk.HeldItem == 0 ? "" : s.itemlist[pk.HeldItem]);
        Bool("hasHeldItem", pk.HeldItem != 0);
        Localized("ball.name", s => s.balllist[pk.Ball]);

        ushort[] moves = [pk.Move1, pk.Move2, pk.Move3, pk.Move4];
        int[] pp = [pk.Move1_PP, pk.Move2_PP, pk.Move3_PP, pk.Move4_PP];
        for (int i = 0; i < 4; i++)
        {
            var prefix = $"move{i + 1}";
            var move = moves[i];
            Bool($"has{char.ToUpperInvariant(prefix[0])}{prefix[1..]}", move != 0);
            Localized($"{prefix}.name", s => move == 0 ? "" : s.movelist[move]);
            Int($"{prefix}.pp", move == 0 ? 0 : pp[i]);
            AddType($"{prefix}.type", MoveInfo.GetType(move, pk.Context));
        }

        int[] ivs = [pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPA, pk.IV_SPD, pk.IV_SPE];
        int[] evs = [pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPA, pk.EV_SPD, pk.EV_SPE];
        int[] stats = [pk.Stat_HPMax, pk.Stat_ATK, pk.Stat_DEF, pk.Stat_SPA, pk.Stat_SPD, pk.Stat_SPE];
        string[] names = ["hp", "atk", "def", "spa", "spd", "spe"];
        for (int i = 0; i < names.Length; i++)
        {
            Int($"iv.{names[i]}", ivs[i]);
            Int($"ev.{names[i]}", evs[i]);
            Int($"stat.{names[i]}", stats[i]);
        }
        Int("iv.total", ivs.Sum());
        Int("ev.total", evs.Sum());
        Int("stat.total", stats.Sum());

        int[] bases = [personal.HP, personal.ATK, personal.DEF, personal.SPA, personal.SPD, personal.SPE];
        for (int i = 0; i < names.Length; i++)
            Int($"base.{names[i]}", bases[i]);
        Int("base.total", bases.Sum());

        // The stat the nature raises and the one it lowers (none for neutral natures); NatureAmp.Table is in the order atk, def, spa, spd, spe.
        var amps = NatureAmp.GetAmps(pk.Nature);
        string[] ampStats = ["atk", "def", "spa", "spd", "spe"];
        for (int i = 0; i < ampStats.Length; i++)
        {
            Bool($"nature.up.{ampStats[i]}", i < amps.Length && amps[i] > 0);
            Bool($"nature.down.{ampStats[i]}", i < amps.Length && amps[i] < 0);
        }

        values["species.sprite"] = Image(sprites.Species(pkm));
        values["ball.sprite"] = Image(sprites.Ball(pkm));
        values["heldItem.sprite"] = Image(pkm.HeldItem == 0 ? null : sprites.HeldItem(pkm));

        var data = new CardData(values.ToImmutableSortedDictionary(StringComparer.Ordinal));
        var species = GameInfo.GetStrings(language).specieslist[pk.Species];
        var shown = pk.IsNicknamed ? pk.Nickname : species;
        // The pack's name is the one PKHeX gives the Pokémon's file ("0025 - Pikachu - AFC21B886487"),
        // so exported cards are named like PKHeX's own exports.
        return new DataPack($"{shown} Lv. {pk.CurrentLevel}", source, data, language, PkmFile.Name(pkm));

        void AddType(string prefix, int type)
        {
            var english = GameInfo.GetStrings(Languages.Fallback).types;
            Text($"{prefix}.key", type < english.Length ? english[type].ToLowerInvariant() : "unknown");
            Localized($"{prefix}.name", s => type < s.types.Length ? s.types[type] : "");
            values[$"{prefix}.color"] = new ColorValue(TypeColors.Of(type));
            // PKHeX's type number (MoveType: normal 0 … fairy 17, stellar 18) and PokeAPI's (one more: normal 1 … stellar 19).
            Int($"{prefix}.id", type);
            Int($"pokeapi.{prefix}.id", type + 1);
        }
    }

    /// <summary>PKHeX stores the Stellar tera type as 99: here it is 18, after Fairy, as in PKHeX's type names.</summary>
    private static int TeraTypeNumber(MoveType type) =>
        (int)type == TeraTypeUtil.Stellar ? TeraTypeUtil.StellarTypeDisplayStringIndex : (int)type;

    private static LocalizedValue Localize(Func<GameStrings, string> text) =>
        LocalizedValue.Texts(AllLanguages.Select(l => KeyValuePair.Create(l, text(GameInfo.GetStrings(l)))));

    /// <summary>A missing image is a transparent pixel, so templates that use the key still work.</summary>
    private static ImageValue Image(byte[]? png) => new(CardImage.FromBytes(png ?? TransparentPixel));

    private static readonly byte[] TransparentPixel = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
}

/// <summary>Type colors of Pokémon Scarlet/Violet, in PKHeX's type order.</summary>
public static class TypeColors
{
    private static readonly Rgba[] Colors =
    [
        Hex(0x9FA19F), Hex(0xFF8000), Hex(0x81B9EF), Hex(0x9141CB), Hex(0x915121), Hex(0xAFA981), // normal … rock
        Hex(0x91A119), Hex(0x704170), Hex(0x60A1B8), Hex(0xE62829), Hex(0x2980EF), Hex(0x3FA129), // bug … grass
        Hex(0xFAC000), Hex(0xEF4179), Hex(0x3DCEF3), Hex(0x5061E1), Hex(0x624D4E), Hex(0xEF70EF), // electric … fairy
        Hex(0x40B5A5), // stellar
    ];

    public static Rgba Of(int type) => type >= 0 && type < Colors.Length ? Colors[type] : Hex(0x68A090);

    private static Rgba Hex(int rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
}
