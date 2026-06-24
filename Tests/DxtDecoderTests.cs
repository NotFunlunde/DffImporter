using NUnit.Framework;
using UnityEngine;
using BfbbImport;

namespace DffImporter.Tests
{
    [TestFixture]
    public class DxtDecoderTests
    {
        // --- DXT1 Tests ---

        [Test]
        public void DecompressDxt1_SingleBlock_4x4_DecodesCorrectly()
        {
            // A single DXT1 block is 8 bytes: 2 bytes color0, 2 bytes color1, 4 bytes indices
            // c0 = pure red in RGB565: R=31, G=0, B=0 -> 0xF800
            // c1 = pure blue in RGB565: R=0, G=0, B=31 -> 0x001F
            // Since c0 > c1, we're in 4-color mode (no transparency)
            // All indices = 0 -> all pixels use c0 (red)
            ushort c0 = 0xF800; // red
            ushort c1 = 0x001F; // blue
            byte[] data = new byte[8];
            data[0] = (byte)(c0 & 0xFF);
            data[1] = (byte)(c0 >> 8);
            data[2] = (byte)(c1 & 0xFF);
            data[3] = (byte)(c1 >> 8);
            // indices: all zero (4 bytes of 0x00)
            data[4] = 0x00; data[5] = 0x00; data[6] = 0x00; data[7] = 0x00;

            var pixels = new Color32[4 * 4];
            DxtDecoder.DecompressDxt1(data, 4, 4, pixels);

            // All pixels should be red (c0)
            for (int i = 0; i < 16; i++)
            {
                Assert.That(pixels[i].r, Is.EqualTo(255), $"Pixel {i} red channel");
                Assert.That(pixels[i].b, Is.EqualTo(0), $"Pixel {i} blue channel");
            }
        }

        [Test]
        public void DecompressDxt1_AllIndex1_UsesColor1()
        {
            ushort c0 = 0xF800; // red
            ushort c1 = 0x001F; // blue
            byte[] data = new byte[8];
            data[0] = (byte)(c0 & 0xFF);
            data[1] = (byte)(c0 >> 8);
            data[2] = (byte)(c1 & 0xFF);
            data[3] = (byte)(c1 >> 8);
            // indices: all 01 = 0x55 per byte (01_01_01_01)
            data[4] = 0x55; data[5] = 0x55; data[6] = 0x55; data[7] = 0x55;

            var pixels = new Color32[4 * 4];
            DxtDecoder.DecompressDxt1(data, 4, 4, pixels);

            // All pixels should be blue (c1)
            for (int i = 0; i < 16; i++)
            {
                Assert.That(pixels[i].r, Is.EqualTo(0), $"Pixel {i} red channel");
                Assert.That(pixels[i].b, Is.EqualTo(255), $"Pixel {i} blue channel");
            }
        }

        [Test]
        public void DecompressDxt1_TransparentMode_Index3_ProducesTransparent()
        {
            // When c0 <= c1, index 3 = transparent black
            ushort c0 = 0x001F; // blue (c0 < c1)
            ushort c1 = 0xF800; // red
            byte[] data = new byte[8];
            data[0] = (byte)(c0 & 0xFF);
            data[1] = (byte)(c0 >> 8);
            data[2] = (byte)(c1 & 0xFF);
            data[3] = (byte)(c1 >> 8);
            // indices: all 11 = 0xFF per byte
            data[4] = 0xFF; data[5] = 0xFF; data[6] = 0xFF; data[7] = 0xFF;

            var pixels = new Color32[4 * 4];
            DxtDecoder.DecompressDxt1(data, 4, 4, pixels);

            // All pixels should be transparent black
            for (int i = 0; i < 16; i++)
            {
                Assert.That(pixels[i].r, Is.EqualTo(0), $"Pixel {i} red");
                Assert.That(pixels[i].g, Is.EqualTo(0), $"Pixel {i} green");
                Assert.That(pixels[i].b, Is.EqualTo(0), $"Pixel {i} blue");
                Assert.That(pixels[i].a, Is.EqualTo(0), $"Pixel {i} alpha");
            }
        }

        [Test]
        public void DecompressDxt1_OpaqueMode_Index2_InterpolatesOneThird()
        {
            // c0 > c1 -> 4-color mode, index 2 = 1/3 interpolation between c0 and c1
            ushort c0 = 0xF800; // red -> (255, 0, 0)
            ushort c1 = 0x001F; // blue -> (0, 0, 255)
            byte[] data = new byte[8];
            data[0] = (byte)(c0 & 0xFF);
            data[1] = (byte)(c0 >> 8);
            data[2] = (byte)(c1 & 0xFF);
            data[3] = (byte)(c1 >> 8);
            // indices: all 10 = 0xAA per byte (10_10_10_10)
            data[4] = 0xAA; data[5] = 0xAA; data[6] = 0xAA; data[7] = 0xAA;

            var pixels = new Color32[4 * 4];
            DxtDecoder.DecompressDxt1(data, 4, 4, pixels);

            // Index 2 = Lerp(c0, c1, 1/3) = (255*(2/3), 0, 255*(1/3)) = (170, 0, 85) approximately
            Assert.That(pixels[0].r, Is.InRange(165, 175), "Red channel of 1/3 interpolation");
            Assert.That(pixels[0].b, Is.InRange(80, 90), "Blue channel of 1/3 interpolation");
        }

        [Test]
        public void DecompressDxt1_8x4_TwoBlocks()
        {
            // 8x4 = 2 blocks wide, 1 block high = 2 blocks total = 16 bytes
            byte[] data = new byte[16];

            // Block 0 (left 4x4): all red
            ushort c0 = 0xF800;
            ushort c1 = 0x0000;
            data[0] = (byte)(c0 & 0xFF); data[1] = (byte)(c0 >> 8);
            data[2] = (byte)(c1 & 0xFF); data[3] = (byte)(c1 >> 8);
            data[4] = 0; data[5] = 0; data[6] = 0; data[7] = 0;

            // Block 1 (right 4x4): all blue
            ushort c0b = 0x001F;
            ushort c1b = 0x0000;
            data[8] = (byte)(c0b & 0xFF); data[9] = (byte)(c0b >> 8);
            data[10] = (byte)(c1b & 0xFF); data[11] = (byte)(c1b >> 8);
            data[12] = 0; data[13] = 0; data[14] = 0; data[15] = 0;

            var pixels = new Color32[8 * 4];
            DxtDecoder.DecompressDxt1(data, 8, 4, pixels);

            // Left half should be red
            Assert.That(pixels[0].r, Is.EqualTo(255));
            Assert.That(pixels[0].b, Is.EqualTo(0));
            // Right half should be blue
            Assert.That(pixels[4].r, Is.EqualTo(0));
            Assert.That(pixels[4].b, Is.EqualTo(255));
        }

        [Test]
        public void DecompressDxt1_NonMultipleOf4Width_HandlesGracefully()
        {
            // 5x5 texture: requires 2x2 blocks = 4 blocks = 32 bytes
            byte[] data = new byte[32];
            // Fill all blocks with a solid color
            ushort c0 = 0x07E0; // green
            for (int block = 0; block < 4; block++)
            {
                int off = block * 8;
                data[off] = (byte)(c0 & 0xFF); data[off + 1] = (byte)(c0 >> 8);
                data[off + 2] = 0; data[off + 3] = 0;
            }

            var pixels = new Color32[5 * 5];
            Assert.DoesNotThrow(() => DxtDecoder.DecompressDxt1(data, 5, 5, pixels));

            // Check that the visible pixels got decoded
            Assert.That(pixels[0].g, Is.EqualTo(255));
        }

        // --- DXT3 Tests ---

        [Test]
        public void DecompressDxt3_SingleBlock_FullAlpha_DecodesCorrectly()
        {
            // DXT3 block: 8 bytes alpha + 8 bytes color = 16 bytes
            byte[] data = new byte[16];

            // Alpha: all 0xFF (full opaque, each nibble = 0xF)
            for (int i = 0; i < 8; i++) data[i] = 0xFF;

            // Color: pure green, all index 0
            ushort c0 = 0x07E0; // green in RGB565
            data[8] = (byte)(c0 & 0xFF);
            data[9] = (byte)(c0 >> 8);
            data[10] = 0; data[11] = 0; // c1 = black
            data[12] = 0; data[13] = 0; data[14] = 0; data[15] = 0;

            var pixels = new Color32[4 * 4];
            DxtDecoder.DecompressDxt3(data, 4, 4, pixels);

            for (int i = 0; i < 16; i++)
            {
                Assert.That(pixels[i].g, Is.EqualTo(255), $"Pixel {i} green");
                Assert.That(pixels[i].a, Is.EqualTo(255), $"Pixel {i} alpha");
            }
        }

        [Test]
        public void DecompressDxt3_ZeroAlpha_ProducesTransparentPixels()
        {
            byte[] data = new byte[16];

            // Alpha: all zero
            for (int i = 0; i < 8; i++) data[i] = 0x00;

            // Color: white
            ushort c0 = 0xFFFF;
            data[8] = (byte)(c0 & 0xFF); data[9] = (byte)(c0 >> 8);
            data[10] = 0xFF; data[11] = 0xFF;
            data[12] = 0; data[13] = 0; data[14] = 0; data[15] = 0;

            var pixels = new Color32[4 * 4];
            DxtDecoder.DecompressDxt3(data, 4, 4, pixels);

            for (int i = 0; i < 16; i++)
                Assert.That(pixels[i].a, Is.EqualTo(0), $"Pixel {i} alpha should be 0");
        }

        [Test]
        public void DecompressDxt3_HalfAlpha_CorrectValues()
        {
            byte[] data = new byte[16];

            // Alpha nibble = 8 -> 8 * 255 / 15 = 136
            // 0x88 = each nibble is 8
            for (int i = 0; i < 8; i++) data[i] = 0x88;

            ushort c0 = 0xFFFF;
            data[8] = (byte)(c0 & 0xFF); data[9] = (byte)(c0 >> 8);
            data[10] = 0xFF; data[11] = 0xFF;
            data[12] = 0; data[13] = 0; data[14] = 0; data[15] = 0;

            var pixels = new Color32[4 * 4];
            DxtDecoder.DecompressDxt3(data, 4, 4, pixels);

            // 8 * 255 / 15 = 136
            Assert.That(pixels[0].a, Is.EqualTo(136));
        }

        [Test]
        public void DecompressDxt3_8x8_FourBlocks()
        {
            // 8x8 = 2x2 blocks = 4 blocks, each 16 bytes = 64 bytes
            byte[] data = new byte[64];

            for (int block = 0; block < 4; block++)
            {
                int off = block * 16;
                // Full alpha
                for (int i = 0; i < 8; i++) data[off + i] = 0xFF;
                // Red color, index 0
                ushort c0 = 0xF800;
                data[off + 8] = (byte)(c0 & 0xFF);
                data[off + 9] = (byte)(c0 >> 8);
            }

            var pixels = new Color32[8 * 8];
            DxtDecoder.DecompressDxt3(data, 8, 8, pixels);

            Assert.That(pixels[0].r, Is.EqualTo(255));
            Assert.That(pixels[0].a, Is.EqualTo(255));
            Assert.That(pixels[63].a, Is.EqualTo(255));
        }

        [Test]
        public void DecompressDxt3_DifferentAlphaPerPixel()
        {
            byte[] data = new byte[16];

            // First alpha byte: low nibble=0, high nibble=F -> pixel 0 alpha=0, pixel 1 alpha=255
            data[0] = 0xF0;
            for (int i = 1; i < 8; i++) data[i] = 0xFF;

            ushort c0 = 0xF800;
            data[8] = (byte)(c0 & 0xFF); data[9] = (byte)(c0 >> 8);
            data[10] = 0; data[11] = 0;
            data[12] = 0; data[13] = 0; data[14] = 0; data[15] = 0;

            var pixels = new Color32[4 * 4];
            DxtDecoder.DecompressDxt3(data, 4, 4, pixels);

            Assert.That(pixels[0].a, Is.EqualTo(0), "Pixel 0 alpha should be 0");
            Assert.That(pixels[1].a, Is.EqualTo(255), "Pixel 1 alpha should be 255");
        }

        // --- Shared behavior tests ---

        [Test]
        public void DecompressDxt1_OutputArraySizeCorrect()
        {
            byte[] data = new byte[8]; // 1 block for 4x4
            var pixels = new Color32[16];
            DxtDecoder.DecompressDxt1(data, 4, 4, pixels);

            // Just verifying it doesn't crash and writes to all 16 pixels
            Assert.That(pixels.Length, Is.EqualTo(16));
        }

        [Test]
        public void DecompressDxt1_SolidBlack_AllPixelsBlack()
        {
            // Both colors black, all indices 0
            byte[] data = new byte[8]; // all zeros
            var pixels = new Color32[16];
            DxtDecoder.DecompressDxt1(data, 4, 4, pixels);

            for (int i = 0; i < 16; i++)
            {
                Assert.That(pixels[i].r, Is.EqualTo(0));
                Assert.That(pixels[i].g, Is.EqualTo(0));
                Assert.That(pixels[i].b, Is.EqualTo(0));
            }
        }
    }
}
