using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PokePic.Core.Model;

/// <summary>
/// 8-bit per channel, non-premultiplied color.
/// </summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    public static Rgba Transparent => new(0, 0, 0, 0);

    /// <summary>
    /// Parses "#RGB", "#RGBA", "#RRGGBB" or "#RRGGBBAA".
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out Rgba? color)
    {
        color = null;
        if (string.IsNullOrEmpty(text) || text[0] != '#')
            return false;

        var hex = text.AsSpan(1);
        foreach (var c in hex)
        {
            if (!char.IsAsciiHexDigit(c))
                return false;
        }

        switch (hex.Length)
        {
            case 3:
            case 4:
                color = new Rgba(
                    Nibble(hex[0]),
                    Nibble(hex[1]),
                    Nibble(hex[2]),
                    hex.Length == 4 ? Nibble(hex[3]) : (byte)255);
                return true;
            case 6:
            case 8:
                color = new Rgba(
                    Byte(hex[0..2]),
                    Byte(hex[2..4]),
                    Byte(hex[4..6]),
                    hex.Length == 8 ? Byte(hex[6..8]) : (byte)255);
                return true;
            default:
                return false;
        }

        static byte Nibble(char c)
        {
            var v = byte.Parse(c.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return (byte)(v * 17);
        }

        static byte Byte(ReadOnlySpan<char> s) => byte.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    public override string ToString() => A == 255
        ? string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}")
        : string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}{A:X2}");
}
