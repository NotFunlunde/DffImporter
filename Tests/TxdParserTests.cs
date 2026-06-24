using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using BfbbImport;

namespace DffImporter.Tests
{
    [TestFixture]
    public class TxdParserTests
    {
        private static void WriteChunkHeader(BinaryWriter bw, uint type, uint size, uint libraryId = 0x0310)
        {
            bw.Write(type);
            bw.Write(size);
            bw.Write(libraryId);
        }

        private static void WriteFixedString(BinaryWriter bw, string s, int length)
        {
            byte[] buf = new byte[length];
            if (s != null)
                Encoding.ASCII.GetBytes(s).CopyTo(buf, 0);
            bw.Write(buf);
        }

        /// <summary>
        /// Builds a minimal .txd stream with a single uncompressed 32-bit BGRA texture.
        /// </summary>
        private static MemoryStream BuildMinimalTxd(string texName, int width, int height, byte[] pixelData,
            uint platformId = 9, uint rasterFormat = 0x0500, byte depth = 32, byte compression = 0)
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

            // Build TEXTURE_NATIVE content
            var nativeContent = new MemoryStream();
            var nw = new BinaryWriter(nativeContent, Encoding.ASCII, leaveOpen: true);

            // STRUCT inside TEXTURE_NATIVE
            var structContent = new MemoryStream();
            var sw = new BinaryWriter(structContent, Encoding.ASCII, leaveOpen: true);

            sw.Write(platformId);        // platformId
            sw.Write((ushort)0);         // filterAddressing
            sw.Write((ushort)0);         // filterAddressing2
            WriteFixedString(sw, texName, 32); // name
            WriteFixedString(sw, "", 32);      // maskName

            sw.Write(rasterFormat);      // rasterFormat
            sw.Write((uint)0);           // d3dFormatOrFourCC
            sw.Write((ushort)width);     // width
            sw.Write((ushort)height);    // height
            sw.Write(depth);             // depth
            sw.Write((byte)1);           // numLevels
            sw.Write((byte)0);           // rasterType
            sw.Write(compression);       // compression

            // Pixel data (level 0)
            sw.Write(pixelData.Length);
            sw.Write(pixelData);

            sw.Flush();
            byte[] structBytes = structContent.ToArray();
            WriteChunkHeader(nw, RwChunk.STRUCT, (uint)structBytes.Length);
            nw.Write(structBytes);

            nw.Flush();
            byte[] nativeBytes = nativeContent.ToArray();

            // Build TEXTURE_DICTIONARY
            var dictContent = new MemoryStream();
            var dw = new BinaryWriter(dictContent, Encoding.ASCII, leaveOpen: true);

            // STRUCT: numTextures (as uint16)
            WriteChunkHeader(dw, RwChunk.STRUCT, 4);
            dw.Write((ushort)1); // numTextures
            dw.Write((ushort)0); // padding

            // TEXTURE_NATIVE
            WriteChunkHeader(dw, RwChunk.TEXTURE_NATIVE, (uint)nativeBytes.Length);
            dw.Write(nativeBytes);

            dw.Flush();
            byte[] dictBytes = dictContent.ToArray();

            WriteChunkHeader(bw, RwChunk.TEXTURE_DICTIONARY, (uint)dictBytes.Length);
            bw.Write(dictBytes);

            bw.Flush();
            ms.Position = 0;
            return ms;
        }

        // --- Tests ---

        [Test]
        public void Parse_32Bit_BGRA_DecodesPixelColors()
        {
            // 2x2 texture, 32-bit BGRA
            // Pixel 0: B=255, G=0, R=0, A=255 -> should decode as Color32(0, 0, 255, 255) = blue
            // Pixel 1: B=0, G=255, R=0, A=255 -> Color32(0, 255, 0, 255) = green
            // Pixel 2: B=0, G=0, R=255, A=255 -> Color32(255, 0, 0, 255) = red
            // Pixel 3: B=128, G=128, R=128, A=128 -> Color32(128, 128, 128, 128) = gray
            byte[] pixels = {
                255, 0, 0, 255,     // BGRA: blue
                0, 255, 0, 255,     // BGRA: green
                0, 0, 255, 255,     // BGRA: red
                128, 128, 128, 128, // BGRA: gray
            };

            using var ms = BuildMinimalTxd("test_32bit", 2, 2, pixels, rasterFormat: 0x0500, depth: 32);
            var result = TxdParser.Parse(ms);

            Assert.That(result.ContainsKey("test_32bit"), Is.True);
            var tex = result["test_32bit"];
            Assert.That(tex.width, Is.EqualTo(2));
            Assert.That(tex.height, Is.EqualTo(2));

            // Pixel 0 (BGRA -> RGBA): (R=0, G=0, B=255, A=255) = blue
            Assert.That(tex.pixels[0].r, Is.EqualTo(0));
            Assert.That(tex.pixels[0].g, Is.EqualTo(0));
            Assert.That(tex.pixels[0].b, Is.EqualTo(255));
            Assert.That(tex.pixels[0].a, Is.EqualTo(255));

            // Pixel 2 (BGRA -> RGBA): (R=255, G=0, B=0, A=255) = red
            Assert.That(tex.pixels[2].r, Is.EqualTo(255));
            Assert.That(tex.pixels[2].g, Is.EqualTo(0));
            Assert.That(tex.pixels[2].b, Is.EqualTo(0));
        }

        [Test]
        public void Parse_24Bit_RGB_DecodesWithFullAlpha()
        {
            // 2x1 texture, 24-bit BGR
            byte[] pixels = {
                255, 0, 0,   // BGR: blue
                0, 0, 255,   // BGR: red
            };

            using var ms = BuildMinimalTxd("test_24bit", 2, 1, pixels,
                rasterFormat: 0x0600, depth: 24);
            var result = TxdParser.Parse(ms);

            Assert.That(result.ContainsKey("test_24bit"), Is.True);
            var tex = result["test_24bit"];

            // Pixel 0: BGR(255, 0, 0) -> RGBA(0, 0, 255, 255)
            Assert.That(tex.pixels[0].r, Is.EqualTo(0));
            Assert.That(tex.pixels[0].b, Is.EqualTo(255));
            Assert.That(tex.pixels[0].a, Is.EqualTo(255));

            // Pixel 1: BGR(0, 0, 255) -> RGBA(255, 0, 0, 255)
            Assert.That(tex.pixels[1].r, Is.EqualTo(255));
            Assert.That(tex.pixels[1].b, Is.EqualTo(0));
        }

        [Test]
        public void Parse_16Bit_565_DecodesCorrectly()
        {
            // 1x1 texture, 16-bit RGB565
            // Pure red: R=31, G=0, B=0 -> 0xF800
            ushort px = 0xF800;
            byte[] pixels = { (byte)(px & 0xFF), (byte)(px >> 8) };

            using var ms = BuildMinimalTxd("test_565", 1, 1, pixels,
                rasterFormat: 0x0200, depth: 16);
            var result = TxdParser.Parse(ms);

            Assert.That(result.ContainsKey("test_565"), Is.True);
            var tex = result["test_565"];
            Assert.That(tex.pixels[0].r, Is.EqualTo(255));
            Assert.That(tex.pixels[0].g, Is.EqualTo(0));
            Assert.That(tex.pixels[0].b, Is.EqualTo(0));
            Assert.That(tex.pixels[0].a, Is.EqualTo(255));
        }

        [Test]
        public void Parse_16Bit_1555_DecodesAlpha()
        {
            // 1x1 texture, 16-bit ARGB1555
            // A=1, R=31, G=0, B=0 -> 0xFC00
            ushort px = 0xFC00; // 1_11111_00000_00000
            byte[] pixels = { (byte)(px & 0xFF), (byte)(px >> 8) };

            using var ms = BuildMinimalTxd("test_1555", 1, 1, pixels,
                rasterFormat: 0x0100, depth: 16);
            var result = TxdParser.Parse(ms);

            Assert.That(result.ContainsKey("test_1555"), Is.True);
            var tex = result["test_1555"];
            Assert.That(tex.pixels[0].r, Is.EqualTo(255));
            Assert.That(tex.pixels[0].a, Is.EqualTo(255)); // alpha bit = 1
        }

        [Test]
        public void Parse_16Bit_1555_ZeroAlpha()
        {
            // A=0, R=31, G=0, B=0 -> 0x7C00
            ushort px = 0x7C00; // 0_11111_00000_00000
            byte[] pixels = { (byte)(px & 0xFF), (byte)(px >> 8) };

            using var ms = BuildMinimalTxd("test_1555_noalpha", 1, 1, pixels,
                rasterFormat: 0x0100, depth: 16);
            var result = TxdParser.Parse(ms);

            var tex = result["test_1555_noalpha"];
            Assert.That(tex.pixels[0].r, Is.EqualTo(255));
            Assert.That(tex.pixels[0].a, Is.EqualTo(0));
        }

        [Test]
        public void Parse_16Bit_4444_DecodesCorrectly()
        {
            // 1x1 texture, 16-bit ARGB4444
            // A=15, R=15, G=0, B=0 -> 0xFF00
            ushort px = 0xFF00;
            byte[] pixels = { (byte)(px & 0xFF), (byte)(px >> 8) };

            using var ms = BuildMinimalTxd("test_4444", 1, 1, pixels,
                rasterFormat: 0x0300, depth: 16);
            var result = TxdParser.Parse(ms);

            var tex = result["test_4444"];
            Assert.That(tex.pixels[0].r, Is.EqualTo(255));
            Assert.That(tex.pixels[0].g, Is.EqualTo(0));
            Assert.That(tex.pixels[0].b, Is.EqualTo(0));
            Assert.That(tex.pixels[0].a, Is.EqualTo(255));
        }

        [Test]
        public void Parse_8BitPaletted_DecodesFromPalette()
        {
            // 2x2 paletted texture: 256-entry palette + index data
            // Palette: entry 0 = red, entry 1 = green, entry 2 = blue, rest = black
            byte[] paletteData = new byte[256 * 4]; // 256 entries, 4 bytes each (RGBA)
            paletteData[0] = 255; paletteData[1] = 0; paletteData[2] = 0; paletteData[3] = 255; // red
            paletteData[4] = 0; paletteData[5] = 255; paletteData[6] = 0; paletteData[7] = 255; // green
            paletteData[8] = 0; paletteData[9] = 0; paletteData[10] = 255; paletteData[11] = 255; // blue

            byte[] indexData = { 0, 1, 2, 0 }; // indices into palette

            // For paletted, the pixel data comes AFTER the palette, but our builder puts
            // pixelData directly. We need a custom stream.
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

            // Build TEXTURE_NATIVE STRUCT
            var structContent = new MemoryStream();
            var sw = new BinaryWriter(structContent, Encoding.ASCII, leaveOpen: true);

            sw.Write((uint)9);          // platformId = D3D9
            sw.Write((ushort)0);         // filterAddressing
            sw.Write((ushort)0);
            WriteFixedString(sw, "pal_test", 32);
            WriteFixedString(sw, "", 32);

            sw.Write((uint)0x2500);      // rasterFormat = PAL8 | 8888
            sw.Write((uint)0);           // d3dFormat
            sw.Write((ushort)2);         // width
            sw.Write((ushort)2);         // height
            sw.Write((byte)8);           // depth = 8 bit (paletted)
            sw.Write((byte)1);           // numLevels
            sw.Write((byte)0);           // rasterType
            sw.Write((byte)0);           // compression = none

            // Palette (256 * 4 bytes)
            sw.Write(paletteData);

            // Level 0 data size + index data
            sw.Write(indexData.Length);
            sw.Write(indexData);

            sw.Flush();
            byte[] structBytes = structContent.ToArray();

            // TEXTURE_NATIVE content
            var nativeContent = new MemoryStream();
            var nw = new BinaryWriter(nativeContent, Encoding.ASCII, leaveOpen: true);
            WriteChunkHeader(nw, RwChunk.STRUCT, (uint)structBytes.Length);
            nw.Write(structBytes);
            nw.Flush();
            byte[] nativeBytes = nativeContent.ToArray();

            // TEXTURE_DICTIONARY content
            var dictContent = new MemoryStream();
            var dw = new BinaryWriter(dictContent, Encoding.ASCII, leaveOpen: true);
            WriteChunkHeader(dw, RwChunk.STRUCT, 4);
            dw.Write((ushort)1);
            dw.Write((ushort)0);
            WriteChunkHeader(dw, RwChunk.TEXTURE_NATIVE, (uint)nativeBytes.Length);
            dw.Write(nativeBytes);
            dw.Flush();
            byte[] dictBytes = dictContent.ToArray();

            WriteChunkHeader(bw, RwChunk.TEXTURE_DICTIONARY, (uint)dictBytes.Length);
            bw.Write(dictBytes);
            bw.Flush();

            ms.Position = 0;
            var result = TxdParser.Parse(ms);

            Assert.That(result.ContainsKey("pal_test"), Is.True);
            var tex = result["pal_test"];
            Assert.That(tex.pixels[0].r, Is.EqualTo(255)); // palette[0] = red
            Assert.That(tex.pixels[0].g, Is.EqualTo(0));
            Assert.That(tex.pixels[1].g, Is.EqualTo(255)); // palette[1] = green
            Assert.That(tex.pixels[2].b, Is.EqualTo(255)); // palette[2] = blue
        }

        [Test]
        public void Parse_DXT1Compressed_DecodesCorrectly()
        {
            // 4x4 DXT1: single block = 8 bytes
            // Pure red: c0=0xF800 (red), c1=0, all index 0
            byte[] blockData = new byte[8];
            blockData[0] = 0x00; blockData[1] = 0xF8; // c0 = 0xF800
            // rest is zero (c1=0, indices=0)

            using var ms = BuildMinimalTxd("dxt1_test", 4, 4, blockData,
                rasterFormat: 0x0500, depth: 16, compression: 1);
            var result = TxdParser.Parse(ms);

            Assert.That(result.ContainsKey("dxt1_test"), Is.True);
            var tex = result["dxt1_test"];
            Assert.That(tex.pixels[0].r, Is.EqualTo(255));
        }

        [Test]
        public void Parse_DXT3Compressed_DecodesCorrectly()
        {
            // 4x4 DXT3: single block = 16 bytes
            byte[] blockData = new byte[16];
            // Alpha: full opaque
            for (int i = 0; i < 8; i++) blockData[i] = 0xFF;
            // Color: green
            ushort c0 = 0x07E0;
            blockData[8] = (byte)(c0 & 0xFF);
            blockData[9] = (byte)(c0 >> 8);

            using var ms = BuildMinimalTxd("dxt3_test", 4, 4, blockData,
                rasterFormat: 0x0500, depth: 16, compression: 3);
            var result = TxdParser.Parse(ms);

            Assert.That(result.ContainsKey("dxt3_test"), Is.True);
            var tex = result["dxt3_test"];
            Assert.That(tex.pixels[0].g, Is.EqualTo(255));
            Assert.That(tex.pixels[0].a, Is.EqualTo(255));
        }

        [Test]
        public void Parse_UnsupportedPlatform_SkipsTexture()
        {
            // Platform ID 2 (PS2) is not supported
            byte[] dummyPixels = new byte[4]; // 1x1 dummy
            using var ms = BuildMinimalTxd("ps2_tex", 1, 1, dummyPixels,
                platformId: 2, rasterFormat: 0x0500, depth: 32);
            var result = TxdParser.Parse(ms);

            // Should not contain the texture (skipped with warning)
            Assert.That(result.ContainsKey("ps2_tex"), Is.False);
        }

        [Test]
        public void Parse_XboxPlatformId_Accepted()
        {
            byte[] pixels = { 0, 0, 255, 255 }; // 1x1 BGRA = red
            using var ms = BuildMinimalTxd("xbox_tex", 1, 1, pixels,
                platformId: 5, rasterFormat: 0x0500, depth: 32);
            var result = TxdParser.Parse(ms);

            Assert.That(result.ContainsKey("xbox_tex"), Is.True);
        }

        [Test]
        public void Parse_D3D8PlatformId_Accepted()
        {
            byte[] pixels = { 0, 0, 255, 255 };
            using var ms = BuildMinimalTxd("d3d8_tex", 1, 1, pixels,
                platformId: 8, rasterFormat: 0x0500, depth: 32);
            var result = TxdParser.Parse(ms);

            Assert.That(result.ContainsKey("d3d8_tex"), Is.True);
        }

        [Test]
        public void Parse_D3D9PlatformId_Accepted()
        {
            byte[] pixels = { 0, 0, 255, 255 };
            using var ms = BuildMinimalTxd("d3d9_tex", 1, 1, pixels,
                platformId: 9, rasterFormat: 0x0500, depth: 32);
            var result = TxdParser.Parse(ms);

            Assert.That(result.ContainsKey("d3d9_tex"), Is.True);
        }

        [Test]
        public void Parse_CaseInsensitiveLookup()
        {
            byte[] pixels = { 0, 0, 255, 255 };
            using var ms = BuildMinimalTxd("MyTexture", 1, 1, pixels);
            var result = TxdParser.Parse(ms);

            Assert.That(result.ContainsKey("mytexture"), Is.True);
            Assert.That(result.ContainsKey("MYTEXTURE"), Is.True);
        }

        [Test]
        public void Parse_InvalidRootChunk_ThrowsInvalidDataException()
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
            WriteChunkHeader(bw, 0x9999, 0);
            bw.Flush();
            ms.Position = 0;

            Assert.Throws<InvalidDataException>(() => TxdParser.Parse(ms));
        }

        [Test]
        public void Parse_EmptyDictionary_ReturnsEmptyDict()
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

            var dictContent = new MemoryStream();
            var dw = new BinaryWriter(dictContent, Encoding.ASCII, leaveOpen: true);
            WriteChunkHeader(dw, RwChunk.STRUCT, 4);
            dw.Write((ushort)0); // numTextures = 0
            dw.Write((ushort)0);
            dw.Flush();
            byte[] dictBytes = dictContent.ToArray();

            WriteChunkHeader(bw, RwChunk.TEXTURE_DICTIONARY, (uint)dictBytes.Length);
            bw.Write(dictBytes);
            bw.Flush();

            ms.Position = 0;
            var result = TxdParser.Parse(ms);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void Parse_16Bit_Green565()
        {
            // Pure green: R=0, G=63, B=0 -> 0x07E0
            ushort px = 0x07E0;
            byte[] pixels = { (byte)(px & 0xFF), (byte)(px >> 8) };

            using var ms = BuildMinimalTxd("green_565", 1, 1, pixels,
                rasterFormat: 0x0200, depth: 16);
            var result = TxdParser.Parse(ms);

            var tex = result["green_565"];
            Assert.That(tex.pixels[0].r, Is.EqualTo(0));
            Assert.That(tex.pixels[0].g, Is.EqualTo(255));
            Assert.That(tex.pixels[0].b, Is.EqualTo(0));
        }

        [Test]
        public void Parse_16Bit_Blue565()
        {
            // Pure blue: R=0, G=0, B=31 -> 0x001F
            ushort px = 0x001F;
            byte[] pixels = { (byte)(px & 0xFF), (byte)(px >> 8) };

            using var ms = BuildMinimalTxd("blue_565", 1, 1, pixels,
                rasterFormat: 0x0200, depth: 16);
            var result = TxdParser.Parse(ms);

            var tex = result["blue_565"];
            Assert.That(tex.pixels[0].r, Is.EqualTo(0));
            Assert.That(tex.pixels[0].g, Is.EqualTo(0));
            Assert.That(tex.pixels[0].b, Is.EqualTo(255));
        }
    }
}
