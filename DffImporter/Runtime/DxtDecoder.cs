using UnityEngine;

namespace BfbbImport
{
    /// <summary>Minimal block decompressor for DXT1/DXT3, used for legacy RW texture rasters.</summary>
    public static class DxtDecoder
    {
        public static void DecompressDxt1(byte[] data, int width, int height, Color32[] outPixels)
        {
            int blocksWide = (width + 3) / 4;
            int blocksHigh = (height + 3) / 4;
            int offset = 0;

            for (int by = 0; by < blocksHigh; by++)
            {
                for (int bx = 0; bx < blocksWide; bx++)
                {
                    ushort c0 = (ushort)(data[offset] | (data[offset + 1] << 8));
                    ushort c1 = (ushort)(data[offset + 2] | (data[offset + 3] << 8));
                    Color32 col0 = ColorUtils.Unpack565(c0);
                    Color32 col1 = ColorUtils.Unpack565(c1);
                    bool transparentMode = c0 <= c1;

                    Color32[] palette = new Color32[4];
                    palette[0] = col0;
                    palette[1] = col1;
                    if (!transparentMode)
                    {
                        palette[2] = ColorUtils.LerpColor(col0, col1, 1f / 3f);
                        palette[3] = ColorUtils.LerpColor(col0, col1, 2f / 3f);
                    }
                    else
                    {
                        palette[2] = ColorUtils.LerpColor(col0, col1, 0.5f);
                        palette[3] = new Color32(0, 0, 0, 0);
                    }

                    uint bits = (uint)(data[offset + 4] | (data[offset + 5] << 8) | (data[offset + 6] << 16) | (data[offset + 7] << 24));
                    offset += 8;

                    for (int py = 0; py < 4; py++)
                    {
                        for (int px = 0; px < 4; px++)
                        {
                            int idx = (int)((bits >> (2 * (py * 4 + px))) & 0x3);
                            int x = bx * 4 + px, y = by * 4 + py;
                            if (x < width && y < height)
                                outPixels[y * width + x] = palette[idx];
                        }
                    }
                }
            }
        }

        public static void DecompressDxt3(byte[] data, int width, int height, Color32[] outPixels)
        {
            int blocksWide = (width + 3) / 4;
            int blocksHigh = (height + 3) / 4;
            int offset = 0;

            for (int by = 0; by < blocksHigh; by++)
            {
                for (int bx = 0; bx < blocksWide; bx++)
                {
                    // 8 bytes explicit alpha (4 bits per pixel)
                    byte[] alphaBytes = new byte[8];
                    for (int i = 0; i < 8; i++) alphaBytes[i] = data[offset + i];
                    offset += 8;

                    ushort c0 = (ushort)(data[offset] | (data[offset + 1] << 8));
                    ushort c1 = (ushort)(data[offset + 2] | (data[offset + 3] << 8));
                    Color32 col0 = ColorUtils.Unpack565(c0);
                    Color32 col1 = ColorUtils.Unpack565(c1);
                    Color32[] palette = new Color32[4];
                    palette[0] = col0;
                    palette[1] = col1;
                    palette[2] = ColorUtils.LerpColor(col0, col1, 1f / 3f);
                    palette[3] = ColorUtils.LerpColor(col0, col1, 2f / 3f);

                    uint bits = (uint)(data[offset + 4] | (data[offset + 5] << 8) | (data[offset + 6] << 16) | (data[offset + 7] << 24));
                    offset += 8;

                    for (int py = 0; py < 4; py++)
                    {
                        for (int px = 0; px < 4; px++)
                        {
                            int pixelNum = py * 4 + px;
                            int idx = (int)((bits >> (2 * pixelNum)) & 0x3);
                            int alphaByte = alphaBytes[pixelNum / 2];
                            int alphaNibble = (pixelNum % 2 == 0) ? (alphaByte & 0xF) : ((alphaByte >> 4) & 0xF);
                            byte alpha = (byte)(alphaNibble * 255 / 15);

                            int x = bx * 4 + px, y = by * 4 + py;
                            if (x < width && y < height)
                            {
                                var c = palette[idx];
                                outPixels[y * width + x] = new Color32(c.r, c.g, c.b, alpha);
                            }
                        }
                    }
                }
            }
        }
    }
}
