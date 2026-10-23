using PKHeX.Core;

namespace PokePic.Adapter;

/// <summary>
/// A Pokémon as the bytes of a PKHeX file (e.g. a .pk9), which a card stores and gives back on import.
/// </summary>
public static class PkmFile
{
    /// <summary>The decrypted party data: what PKHeX writes when it saves a Pokémon to a file.</summary>
    /// <remarks>Written from a copy: writing refreshes the checksum, and the Pokémon in PKHeX must not change.</remarks>
    public static byte[] Bytes(PKM pkm)
    {
        var copy = pkm.Clone();
        var data = new byte[copy.SIZE_PARTY];
        copy.WriteDecryptedDataParty(data);
        return data;
    }

    /// <summary>The name PKHeX gives the Pokémon's file, without extension ("0025 - Pikachu - AFC21B886487").</summary>
    public static string Name(PKM pkm)
    {
        var copy = pkm.Clone();
        copy.RefreshChecksum(); // the name includes the checksum: as when PKHeX saves the file
        return copy.FileNameWithoutExtension;
    }

    /// <param name="format">The file extension without the dot, e.g. "pk9" (<see cref="PKM.Extension"/>).</param>
    public static PKM? Read(byte[] bytes, string format, ITrainerInfo trainer) =>
        FileUtil.TryGetPKM(bytes, out var pkm, "." + format, trainer) ? pkm : null;
}
