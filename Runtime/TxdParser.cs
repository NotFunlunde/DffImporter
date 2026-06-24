using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BfbbImport
{
    /// <summary>
    /// Parses a RenderWare .txd (Texture Dictionary) and produces Unity Texture2D objects.
    ///
    /// Supports the D3D8/D3D9/Xbox-family raster header layout (these three share essentially
    /// the same struct in RW3), covering: 8-bit paletted (32-bit palette), 16-bit (1555/4444/0565),
    /// 24/32-bit RGB(A), and DXT1/DXT3 compressed rasters.
    ///
    /// Xbox-specific handling: the original Xbox build's GPU (NV2A) stores some uncompressed
    /// rasters in a Z-order/Morton "swizzled" tile layout rather than row-major. When the raster
    /// format's swizzle flag is set, the raw bytes are unswizzled before being interpreted as
    /// pixels (see XboxUnswizzle.cs). DXT-compressed blocks are not swizzled this way and are
    /// decoded directly.
    ///
    /// PS2/GameCube-native rasters are NOT supported — those use an entirely different header
    /// (GS register dumps, 4-bit palette indices, etc.) and are skipped with a warning rather
    /// than risking garbage pixels.
    /// </summary>
    public static class TxdParser
    {
        // rwPLATFORMID values that share the generic (non-PS2) raster header layout.
        private const uint PLATFORM_XBOX = 5;
        private const uint PLATFORM_D3D8 = 8;
        private const uint PLATFORM_D3D9 = 9;

        private static bool IsSupportedPlatform(uint id) => id == PLATFORM_XBOX || id == PLATFORM_D3D8 || id == PLATFORM_D3D9;

        public static Dictionary<string, Texture2D> Parse(Stream stream)
        {
            var result = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
            using var r = new RwReader(stream);

            long dictEnd = r.ReadChunkHeader(out var header);
            if (header.Type != RwChunk.TEXTURE_DICTIONARY)
                throw new InvalidDataException($"Expected TEXTURE_DICTIONARY chunk, got 0x{header.Type:X4}.");

            long structEnd = r.ReadChunkHeader(out var sh);
            if (sh.Type != RwChunk.STRUCT) throw new InvalidDataException("Expected STRUCT in TEXTURE_DICTIONARY.");
            ushort numTextures = r.ReadUInt16();
            r.Seek(structEnd);

            for (int i = 0; i < numTextures && r.Position < dictEnd; i++)
            {
                long texEnd = r.ReadChunkHeader(out var th);
                if (th.Type != RwChunk.TEXTURE_NATIVE)
                {
                    r.Seek(texEnd);
                    continue;
                }

                var (name, tex) = ReadTextureNative(r, texEnd);
                if (tex != null) result[name] = tex;
                r.Seek(texEnd);
            }

            return result;
        }

        private static (string name, Texture2D tex) ReadTextureNative(RwReader r, long texEnd)
        {
            long structEnd = r.ReadChunkHeader(out var sh);
            if (sh.Type != RwChunk.STRUCT) throw new InvalidDataException("Expected STRUCT in TEXTURE_NATIVE.");

            uint platformId = r.ReadUInt32();
            ushort filterAddressing = r.ReadUInt16();
            ushort filterAddressing2 = r.ReadUInt16();
            string name = r.ReadFixedString(32);
            string maskName = r.ReadFixedString(32);

            if (!IsSupportedPlatform(platformId))
            {
                Debug.LogWarning($"[DffImporter] Texture '{name}' uses unsupported native platform id {platformId} " +
                                  "(expected Xbox=5, D3D8=8, or D3D9=9 — PS2/GameCube-native rasters use a different " +
                                  "header and aren't handled). Skipping this texture.");
                r.Seek(structEnd);
                return (name, null);
            }

            uint rasterFormat = r.ReadUInt32();
            uint d3dFormatOrFourCC = r.ReadUInt32(); // interpretation depends on rasterFormat
            ushort width = r.ReadUInt16();
            ushort height = r.ReadUInt16();
            byte depth = r.ReadByte();
            byte numLevels = r.ReadByte();
            byte rasterType = r.ReadByte();
            byte compression = r.ReadByte(); // 0 = none, otherwise FOURCC-style flag (DXT)

            Texture2D tex = null;
            try
            {
                tex = DecodeRaster(r, width, height, depth, numLevels, rasterFormat, compression, platformId, name);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[DffImporter] Failed to decode texture '{name}': {ex.Message}");
            }

            r.Seek(structEnd);
            return (name, tex);
        }

        // RW raster format flags
        private const uint FORMAT_MASK = 0x0F00;
        private const uint FMT_1555 = 0x0100;
        private const uint FMT_565 = 0x0200;
        private const uint FMT_4444 = 0x0300;
        private const uint FMT_8888 = 0x0500;
        private const uint FMT_888 = 0x0600;
        private const uint FMT_PAL8 = 0x2000;     // 8-bit paletted flag
        private const uint RASTER_SWIZZLED = 0x10000; // Z-order/Morton tiled layout (Xbox/NV2A, occasionally PS2)

        // Guard against absurd texture dimensions from malformed files that would cause
        // out-of-memory or integer-overflow issues. 8192x8192 is well beyond any RW3-era
        // texture and keeps the pixel buffer under ~256 MB.
        private const int MAX_TEXTURE_DIMENSION = 8192;
        private const int MAX_RASTER_DATA_SIZE = 128 * 1024 * 1024; // 128 MB

        private static Texture2D DecodeRaster(RwReader r, int width, int height, int depth, int numLevels,
            uint rasterFormat, byte compression, uint platformId, string name)
        {
            if (width <= 0 || height <= 0 || width > MAX_TEXTURE_DIMENSION || height > MAX_TEXTURE_DIMENSION)
                throw new InvalidDataException($"Texture '{name}' has invalid dimensions {width}x{height}.");

            // Use long arithmetic to detect overflow before allocating.
            long pixelCount = (long)width * height;
            if (pixelCount > MAX_TEXTURE_DIMENSION * MAX_TEXTURE_DIMENSION)
                throw new InvalidDataException($"Texture '{name}' pixel count {pixelCount} exceeds safe limit.");

            bool isPaletted8 = (rasterFormat & FMT_PAL8) != 0 && depth == 8;
            bool isSwizzled = (rasterFormat & RASTER_SWIZZLED) != 0;
            Color32[] palette = null;

            if (isPaletted8)
            {
                int palCount = 256;
                palette = new Color32[palCount];
                for (int p = 0; p < palCount; p++)
                {
                    byte pr = r.ReadByte(), pg = r.ReadByte(), pb = r.ReadByte(), pa = r.ReadByte();
                    palette[p] = new Color32(pr, pg, pb, pa);
                }
            }

            // Read level 0 only (further mips follow per numLevels, but Unity regenerates them).
            int dataSize = r.ReadInt32();
            if (dataSize < 0 || dataSize > MAX_RASTER_DATA_SIZE)
                throw new InvalidDataException($"Texture '{name}' declares raster data size {dataSize} which exceeds safe limit.");
            byte[] data = r.ReadBytes(dataSize);

            for (int lvl = 1; lvl < numLevels; lvl++)
            {
                int lvlSize = r.ReadInt32();
                r.Skip(lvlSize);
            }

            var pixels = new Color32[(int)pixelCount];

            if (compression == 1) // DXT1
            {
                DxtDecoder.DecompressDxt1(data, width, height, pixels);
            }
            else if (compression == 3) // DXT3
            {
                DxtDecoder.DecompressDxt3(data, width, height, pixels);
            }
            else
            {
                // Uncompressed: unswizzle first (Xbox Z-order tiling) if flagged, then interpret bytes.
                if (isSwizzled)
                {
                    int bpp = isPaletted8 ? 1 : depth / 8;
                    try
                    {
                        data = XboxUnswizzle.Unswizzle(data, width, height, bpp);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[DffImporter] Unswizzle failed for '{name}' ({width}x{height}, {bpp}bpp): {ex.Message}. " +
                                          "Texture may appear scrambled.");
                    }
                }

                if (isPaletted8)
                {
                    for (int i = 0; i < width * height; i++)
                        pixels[i] = palette[data[i]];
                }
                else
                {
                    DecodeUncompressed(data, width, height, depth, rasterFormat, pixels);
                }
            }

            var tex = new Texture2D(width, height, TextureFormat.RGBA32, true) { name = name };
            tex.SetPixels32(pixels);
            tex.Apply(true);
            return tex;
        }

        private static void DecodeUncompressed(byte[] data, int width, int height, int depth, uint rasterFormat, Color32[] outPixels)
        {
            int n = width * height;
            uint baseFmt = rasterFormat & FORMAT_MASK;

            if (depth == 32)
            {
                for (int i = 0; i < n; i++)
                {
                    int o = i * 4;
                    byte b = data[o + 0], g2 = data[o + 1], rr = data[o + 2], a = data[o + 3]; // stored BGRA
                    outPixels[i] = new Color32(rr, g2, b, a);
                }
            }
            else if (depth == 24)
            {
                for (int i = 0; i < n; i++)
                {
                    int o = i * 3;
                    byte b = data[o + 0], g2 = data[o + 1], rr = data[o + 2];
                    outPixels[i] = new Color32(rr, g2, b, 255);
                }
            }
            else if (depth == 16)
            {
                for (int i = 0; i < n; i++)
                {
                    ushort px = (ushort)(data[i * 2] | (data[i * 2 + 1] << 8));
                    outPixels[i] = baseFmt == FMT_4444 ? Unpack4444(px)
                                   : baseFmt == FMT_565 ? Unpack565(px)
                                   : Unpack1555(px);
                }
            }
            else
            {
                throw new NotSupportedException($"Unsupported raster bit depth {depth} for uncompressed decode.");
            }
        }

        private static Color32 Unpack1555(ushort p)
        {
            byte a = (byte)(((p >> 15) & 0x1) * 255);
            byte r = (byte)(((p >> 10) & 0x1F) * 255 / 31);
            byte g = (byte)(((p >> 5) & 0x1F) * 255 / 31);
            byte b = (byte)((p & 0x1F) * 255 / 31);
            return new Color32(r, g, b, a);
        }

        private static Color32 Unpack565(ushort p)
        {
            byte r = (byte)(((p >> 11) & 0x1F) * 255 / 31);
            byte g = (byte)(((p >> 5) & 0x3F) * 255 / 63);
            byte b = (byte)((p & 0x1F) * 255 / 31);
            return new Color32(r, g, b, 255);
        }

        private static Color32 Unpack4444(ushort p)
        {
            byte a = (byte)(((p >> 12) & 0xF) * 255 / 15);
            byte r = (byte)(((p >> 8) & 0xF) * 255 / 15);
            byte g = (byte)(((p >> 4) & 0xF) * 255 / 15);
            byte b = (byte)((p & 0xF) * 255 / 15);
            return new Color32(r, g, b, a);
        }
    }
}
