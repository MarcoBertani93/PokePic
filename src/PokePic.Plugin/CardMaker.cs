using System.Collections.Immutable;
using PKHeX.Core;
using PokePic.Adapter;
using PokePic.Core;
using PokePic.Core.Assets;
using PokePic.Core.Data;
using PokePic.Core.Rendering;
using PokePic.Png;
using PokePic.Rendering;

namespace PokePic.Plugin;

/// <param name="Png">The card with the Pokémon inside, or null when the template has problems.</param>
/// <param name="FileName">"{template}_{Pokémon}.png".</param>
public sealed record CardResult(byte[]? Png, IReadOnlyList<string> Problems, string FileName);

/// <summary>
/// Makes cards: the Pokémon becomes a data pack, the template is loaded and validated against it, the card is
/// rendered with the template's mode and scale and gets the Pokémon's bytes in its "pokepic" chunk.
/// </summary>
public sealed class CardMaker
{
    private readonly WebAssetCache _web = new();
    private readonly ISpriteSource _sprites;
    private readonly IPngPayloadCodec _codec = new PngPayloadCodec();
    private readonly SkiaCardRenderer _renderer = new();

    public CardMaker() : this(new PkhexSprites())
    {
    }

    internal CardMaker(ISpriteSource sprites)
    {
        NativeSkia.Register();
        _sprites = sprites;
    }

    public DataPack DataPack(PKM pkm, string language) => PkmAdapter.ToDataPack(pkm, language, _sprites);

    /// <summary>Retries web files that could not be downloaded.</summary>
    public void Refresh() => _web.ForgetFailures();

    public CardResult Make(string templatePath, PKM pkm, string language)
    {
        var pack = DataPack(pkm, language);
        var loaded = TemplateLoader.LoadFile(templatePath, pack.Data.Schema, _web);
        if (loaded.Template is not { } template || !loaded.Success)
            return new CardResult(null, [.. loaded.Issues.Select(i => i.ToString())], "");

        var fileName = CardFileName.For(template.Name ?? template.Id, pack.EffectiveName);
        try
        {
            using var prepared = _renderer.Prepare(template, new FileSystemAssetProvider(Path.GetDirectoryName(Path.GetFullPath(templatePath))!, _web));
            var card = prepared.Render(pack.Data, RenderOptions.FromTemplate(template, language));
            var species = pack.Data.Values.GetValueOrDefault("species.name") is { } name ? LocalizedValue.In(name, language).ToText() : "";
            var payload = new CardPayload(
                template.Id,
                template.Version,
                ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, new Dictionary<string, string>
                {
                    ["name"] = pkm.Nickname,
                    ["species"] = species,
                    ["level"] = pkm.CurrentLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["template"] = template.Name ?? template.Id,
                }),
                pkm.Extension,
                [.. PkmFile.Bytes(pkm)]);
            return new CardResult(_codec.Embed(card.Png, payload), [], fileName);
        }
        catch (TemplateException ex)
        {
            return new CardResult(null, [.. ex.Issues.Select(i => i.ToString())], fileName);
        }
    }

    /// <summary>The Pokémon of a card; or why it cannot be read, and whether the file is a card at all.</summary>
    public ImportResult Import(byte[] png, ITrainerInfo trainer)
    {
        var read = _codec.Read(png);
        if (read.Payload is not { } payload)
            return new ImportResult(null, read.Error, read.IsCard);
        return PkmFile.Read([.. payload.Pkm], payload.PkmFormat, trainer) is { } pkm
            ? new ImportResult(pkm, null, IsCard: true)
            : new ImportResult(null, $"The card contains a .{payload.PkmFormat} Pokémon that PKHeX cannot read.", IsCard: true);
    }
}

/// <param name="IsCard">False for a PNG that is not a PokePic card: PKHeX can then try it as any other file.</param>
public sealed record ImportResult(PKM? Pkm, string? Error, bool IsCard);
