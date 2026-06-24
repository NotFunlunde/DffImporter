using UnityEngine;

namespace BfbbImport
{
    /// <summary>
    /// Shared helpers for unpacking packed-pixel color formats (RGB565, ARGB1555, ARGB4444)
    /// and for Color32 interpolation. Used by both DxtDecoder and TxdParser.
    /// </summary>
    public static class ColorUtils
    {
        public static Color32 Unpack565(ushort p)
        {
            byte r = (byte)(((p >> 11) & 0x1F) * 255 / 31);
            byte g = (byte)(((p >> 5) & 0x3F) * 255 / 63);
            byte b = (byte)((p & 0x1F) * 255 / 31);
            return new Color32(r, g, b, 255);
        }

        public static Color32 Unpack1555(ushort p)
        {
            byte a = (byte)(((p >> 15) & 0x1) * 255);
            byte r = (byte)(((p >> 10) & 0x1F) * 255 / 31);
            byte g = (byte)(((p >> 5) & 0x1F) * 255 / 31);
            byte b = (byte)((p & 0x1F) * 255 / 31);
            return new Color32(r, g, b, a);
        }

        public static Color32 Unpack4444(ushort p)
        {
            byte a = (byte)(((p >> 12) & 0xF) * 255 / 15);
            byte r = (byte)(((p >> 8) & 0xF) * 255 / 15);
            byte g = (byte)(((p >> 4) & 0xF) * 255 / 15);
            byte b = (byte)((p & 0xF) * 255 / 15);
            return new Color32(r, g, b, a);
        }

        public static Color32 LerpColor(Color32 a, Color32 b, float t)
        {
            return new Color32(
                (byte)(a.r + (b.r - a.r) * t),
                (byte)(a.g + (b.g - a.g) * t),
                (byte)(a.b + (b.b - a.b) * t),
                255);
        }
    }
}
