using System;

namespace BfbbImport
{
    /// <summary>
    /// Unswizzles Xbox-native (NV2A) raster data. The original Xbox GPU stores textures in a
    /// Z-order (Morton-code) tiled layout for performance; this undoes that to get a normal
    /// row-major byte buffer matching width/height. Only applies to uncompressed rasters —
    /// DXT-compressed block data is not swizzled this way.
    /// </summary>
    public static class XboxUnswizzle
    {
        public static byte[] Unswizzle(byte[] src, int width, int height, int bytesPerPixel)
        {
            var dst = new byte[src.Length];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int swizzledIndex = SwizzledOffset(x, y, width, height);
                    int linearIndex = y * width + x;

                    long srcOffset = (long)swizzledIndex * bytesPerPixel;
                    long dstOffset = (long)linearIndex * bytesPerPixel;
                    if (srcOffset + bytesPerPixel > src.Length) continue; // guard against malformed/truncated data

                    Array.Copy(src, srcOffset, dst, dstOffset, bytesPerPixel);
                }
            }
            return dst;
        }

        /// <summary>Interleaves the bits of x and y (alternating, low bit first) until both
        /// coordinate masks exceed the corresponding texture dimension — the standard Xbox/NV2A
        /// Z-order addressing scheme, which also degrades gracefully for non-power-of-two sizes.</summary>
        private static int SwizzledOffset(int x, int y, int width, int height)
        {
            int offset = 0;
            int bit = 0;
            int maskX = 1, maskY = 1;

            while (maskX < width || maskY < height)
            {
                if (maskX < width)
                {
                    offset |= (x & maskX) << bit;
                    maskX <<= 1;
                    bit++;
                }
                if (maskY < height)
                {
                    offset |= (y & maskY) << bit;
                    maskY <<= 1;
                    bit++;
                }
            }
            return offset;
        }
    }
}
