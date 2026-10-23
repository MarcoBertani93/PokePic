using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace PokePic.Png;

/// <summary>
/// Stores the payload as JSON in an uncompressed <c>iTXt</c> chunk with keyword "pokepic", just before <c>IEND</c>.
/// <code>
/// { "formatVersion": 1, "templateId": "spotlight", "templateVersion": 1,
///   "display": { "name": "Drago", "species": "Charizard", "level": "50" },
///   "pkmFormat": "pk9", "pkm": "&lt;base64&gt;", "checksum": "&lt;sha-256 hex&gt;" }
/// </code>
/// </summary>
public sealed class PngPayloadCodec : IPngPayloadCodec
{
    public const string Keyword = "pokepic";

    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public byte[] Embed(byte[] png, CardPayload payload)
    {
        var chunks = Chunks(png) ?? throw new InvalidDataException("Not a PNG file.");
        var end = chunks.FirstOrDefault(c => c.Type == "IEND");
        if (end.Type is null)
            throw new InvalidDataException("The PNG has no IEND chunk.");

        // iTXt: keyword, 0, compression flag 0, compression method 0, language tag "", 0, translated keyword "", 0, text.
        var data = new List<byte>();
        data.AddRange(Encoding.Latin1.GetBytes(Keyword));
        data.AddRange([0, 0, 0, 0, 0]);
        data.AddRange(Encoding.UTF8.GetBytes(Serialize(payload)));

        using var output = new MemoryStream(png.Length + data.Count + 64);
        output.Write(png.AsSpan(0, end.Start));
        WriteChunk(output, "iTXt", [.. data]);
        output.Write(png.AsSpan(end.Start));
        return output.ToArray();
    }

    public CardPayloadReadResult Read(byte[] png)
    {
        if (Chunks(png) is not { } chunks)
            return CardPayloadReadResult.NotACard("The file is not a PNG.");

        foreach (var chunk in chunks.Where(c => c.Type == "iTXt"))
        {
            var data = png.AsSpan(chunk.DataStart, chunk.Length);
            int zero = data.IndexOf((byte)0);
            if (zero < 0 || Encoding.Latin1.GetString(data[..zero]) != Keyword)
                continue;
            // After the keyword: compression flag and method, then language tag and translated keyword (both null-terminated).
            var rest = data[(zero + 1)..];
            if (rest.Length < 2 || rest[0] != 0)
                return new(null, "The PokePic data in the PNG is compressed, which is not supported.");
            rest = rest[2..];
            for (int i = 0; i < 2; i++)
            {
                int end = rest.IndexOf((byte)0);
                if (end < 0)
                    return new(null, "The PokePic data in the PNG is damaged.");
                rest = rest[(end + 1)..];
            }
            return Deserialize(Encoding.UTF8.GetString(rest));
        }
        return CardPayloadReadResult.NotACard("The PNG is not a PokePic card: it has no PokePic data.");
    }

    private static string Serialize(CardPayload payload)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("formatVersion", CardPayload.FormatVersion);
            w.WriteString("templateId", payload.TemplateId);
            w.WriteNumber("templateVersion", payload.TemplateVersion);
            w.WriteStartObject("display");
            foreach (var (key, value) in payload.Display)
                w.WriteString(key, value);
            w.WriteEndObject();
            w.WriteString("pkmFormat", payload.PkmFormat);
            w.WriteBase64String("pkm", payload.Pkm.AsSpan());
            w.WriteString("checksum", payload.Checksum);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static CardPayloadReadResult Deserialize(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("formatVersion", out var version) || version.ValueKind != JsonValueKind.Number)
                return new(null, "The PokePic data has no format version.");
            if (version.GetInt32() != CardPayload.FormatVersion)
                return new(null, $"Unsupported PokePic data format {version.GetInt32()}; this version reads format {CardPayload.FormatVersion}.");

            string Text(string name) => root.TryGetProperty(name, out var e) && e.ValueKind == JsonValueKind.String
                ? e.GetString()!
                : throw new InvalidDataException($"The PokePic data has no \"{name}\".");

            var pkm = root.TryGetProperty("pkm", out var p) && p.ValueKind == JsonValueKind.String
                ? p.GetBytesFromBase64()
                : throw new InvalidDataException("The PokePic data has no \"pkm\".");
            if (CardPayload.Hash(pkm) != Text("checksum"))
                return new(null, "The Pokémon data in the PNG does not match its checksum: the file was changed.");

            var display = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
            if (root.TryGetProperty("display", out var d) && d.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in d.EnumerateObject())
                    display[property.Name] = property.Value.ToString();
            }
            int templateVersion = root.TryGetProperty("templateVersion", out var tv) && tv.ValueKind == JsonValueKind.Number ? tv.GetInt32() : 0;

            return new(new CardPayload(Text("templateId"), templateVersion, display.ToImmutable(), Text("pkmFormat"), [.. pkm]), null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or FormatException or InvalidOperationException)
        {
            return new(null, $"The PokePic data in the PNG is damaged: {ex.Message}");
        }
    }

    private readonly record struct Chunk(string Type, int Start, int DataStart, int Length);

    /// <summary>The chunks of a PNG, or null if the bytes are not one.</summary>
    private static List<Chunk>? Chunks(byte[] png)
    {
        if (png.Length < Signature.Length || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
            return null;
        var list = new List<Chunk>();
        int offset = Signature.Length;
        while (offset + 12 <= png.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset));
            if (length < 0 || offset + 12 + length > png.Length)
                return null;
            var type = Encoding.Latin1.GetString(png, offset + 4, 4);
            list.Add(new Chunk(type, offset, offset + 8, length));
            offset += 12 + length;
            if (type == "IEND")
                break;
        }
        return list;
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, (uint)data.Length);
        output.Write(buffer);
        var typeBytes = Encoding.Latin1.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(buffer, Crc32([.. typeBytes, .. data]));
        output.Write(buffer);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    /// <summary>The CRC of PNG chunks (ISO 3309), over type and data.</summary>
    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        uint c = 0xFFFFFFFFu;
        foreach (var b in bytes)
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }
}
