using System.Collections.Immutable;
using System.Security.Cryptography;

namespace PokePic.Png;

/// <summary>
/// The Pokémon saved inside a card PNG. <see cref="Pkm"/> is the only source used to rebuild it on import:
/// <see cref="Display"/> is for showing the card in a list, never for the Pokémon.
/// </summary>
/// <param name="PkmFormat">The PKHeX file extension of <see cref="Pkm"/> (e.g. "pk9"), needed to read the bytes back.</param>
/// <param name="Pkm">The entity bytes as PKHeX stores them in a file.</param>
public sealed record CardPayload(
    string TemplateId,
    int TemplateVersion,
    ImmutableSortedDictionary<string, string> Display,
    string PkmFormat,
    ImmutableArray<byte> Pkm)
{
    public const int FormatVersion = 1;

    /// <summary>SHA-256 of <see cref="Pkm"/>, lowercase hex: tells whether the bytes were changed by hand.</summary>
    public string Checksum => Hash(Pkm.AsSpan());

    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}

/// <param name="IsCard">False when the file is not a PokePic card at all (not a PNG, or no "pokepic" chunk);
/// true also for a card that cannot be read (changed, damaged, unknown format).</param>
public sealed record CardPayloadReadResult(CardPayload? Payload, string? Error, bool IsCard = true)
{
    public bool Success => Payload is not null;

    public static CardPayloadReadResult NotACard(string reason) => new(null, reason, IsCard: false);
}

/// <summary>
/// Puts a <see cref="CardPayload"/> into a PNG and reads it back.
/// </summary>
public interface IPngPayloadCodec
{
    /// <exception cref="InvalidDataException">The bytes are not a PNG.</exception>
    byte[] Embed(byte[] png, CardPayload payload);

    /// <summary>Reads the payload; without a valid one, says why. Never falls back to anything else.</summary>
    CardPayloadReadResult Read(byte[] png);
}
