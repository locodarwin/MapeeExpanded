using System.Drawing;
using System.Globalization;

namespace Mapper
{
    // Reads the colour strings styles use: #RGB, #ARGB, #RRGGBB, #AARRGGBB or a known colour name
    public static class ColorParser
    {
        public static Color Parse(string value)
        {
            if (!value.StartsWith('#'))
            {
                Color named = Color.FromName(value.Trim());
                if (!named.IsKnownColor) throw new FormatException($"Unknown colour '{value}'");
                return named;
            }

            string hex = value[1..].Trim();
            if (hex.Length is 3 or 4)
            {
                hex = string.Concat(hex.Select(c => new string(c, 2)));
            }
            if (hex.Length == 6) hex = "FF" + hex;
            if (hex.Length != 8) throw new FormatException($"Unknown colour '{value}'");

            uint argb = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
        }
    }
}
