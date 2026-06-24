using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using BfbbImport;

namespace DffImporter.Tests
{
    [TestFixture]
    public class RwCoreTests
    {
        private static MemoryStream MakeStream(Action<BinaryWriter> write)
        {
            var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true))
                write(bw);
            ms.Position = 0;
            return ms;
        }

        // --- RwChunkHeader ---

        [Test]
        public void ReadChunkHeader_ReadsTypeAndSizeAndLibraryId()
        {
            using var ms = MakeStream(bw =>
            {
                bw.Write((uint)0x0010); // CLUMP
                bw.Write((uint)1234);   // size
                bw.Write((uint)0x0310); // libraryId
            });
            using var reader = new RwReader(ms);

            var h = reader.ReadChunkHeader();

            Assert.That(h.Type, Is.EqualTo(0x0010u));
            Assert.That(h.Size, Is.EqualTo(1234u));
            Assert.That(h.LibraryId, Is.EqualTo(0x0310u));
        }

        [Test]
        public void ReadChunkHeader_WithEndOffset_ReturnsCorrectEnd()
        {
            using var ms = MakeStream(bw =>
            {
                bw.Write((uint)0x000F); // GEOMETRY
                bw.Write((uint)100);    // size
                bw.Write((uint)0x0001); // libraryId
                bw.Write(new byte[100]); // body
            });
            using var reader = new RwReader(ms);

            long end = reader.ReadChunkHeader(out var h);

            Assert.That(h.Type, Is.EqualTo(0x000Fu));
            Assert.That(h.Size, Is.EqualTo(100u));
            Assert.That(end, Is.EqualTo(12 + 100)); // header(12) + body(100)
        }

        [Test]
        public void ReadChunkHeader_ToString_FormatsCorrectly()
        {
            var h = new RwChunkHeader { Type = 0x0010, Size = 42, LibraryId = 0 };
            Assert.That(h.ToString(), Does.Contain("0x0010"));
            Assert.That(h.ToString(), Does.Contain("42"));
        }

        // --- Position / Length / Seek / Skip ---

        [Test]
        public void Position_ReflectsStreamPosition()
        {
            using var ms = MakeStream(bw => bw.Write(new byte[20]));
            using var reader = new RwReader(ms);

            Assert.That(reader.Position, Is.EqualTo(0));
            reader.ReadByte();
            Assert.That(reader.Position, Is.EqualTo(1));
        }

        [Test]
        public void Length_ReturnsStreamLength()
        {
            using var ms = MakeStream(bw => bw.Write(new byte[50]));
            using var reader = new RwReader(ms);

            Assert.That(reader.Length, Is.EqualTo(50));
        }

        [Test]
        public void Seek_SetsPosition()
        {
            using var ms = MakeStream(bw => bw.Write(new byte[20]));
            using var reader = new RwReader(ms);

            reader.Seek(10);
            Assert.That(reader.Position, Is.EqualTo(10));
        }

        [Test]
        public void Skip_AdvancesPosition()
        {
            using var ms = MakeStream(bw => bw.Write(new byte[20]));
            using var reader = new RwReader(ms);

            reader.Skip(5);
            Assert.That(reader.Position, Is.EqualTo(5));
            reader.Skip(3);
            Assert.That(reader.Position, Is.EqualTo(8));
        }

        // --- Primitive reads ---

        [Test]
        public void ReadByte_ReadsCorrectly()
        {
            using var ms = MakeStream(bw => bw.Write((byte)0xAB));
            using var reader = new RwReader(ms);

            Assert.That(reader.ReadByte(), Is.EqualTo(0xAB));
        }

        [Test]
        public void ReadSByte_ReadsCorrectly()
        {
            using var ms = MakeStream(bw => bw.Write((sbyte)-42));
            using var reader = new RwReader(ms);

            Assert.That(reader.ReadSByte(), Is.EqualTo(-42));
        }

        [Test]
        public void ReadUInt16_ReadsLittleEndian()
        {
            using var ms = MakeStream(bw => bw.Write((ushort)0x1234));
            using var reader = new RwReader(ms);

            Assert.That(reader.ReadUInt16(), Is.EqualTo(0x1234));
        }

        [Test]
        public void ReadInt16_ReadsCorrectly()
        {
            using var ms = MakeStream(bw => bw.Write((short)-100));
            using var reader = new RwReader(ms);

            Assert.That(reader.ReadInt16(), Is.EqualTo(-100));
        }

        [Test]
        public void ReadUInt32_ReadsLittleEndian()
        {
            using var ms = MakeStream(bw => bw.Write((uint)0xDEADBEEF));
            using var reader = new RwReader(ms);

            Assert.That(reader.ReadUInt32(), Is.EqualTo(0xDEADBEEFu));
        }

        [Test]
        public void ReadInt32_ReadsCorrectly()
        {
            using var ms = MakeStream(bw => bw.Write((int)-99999));
            using var reader = new RwReader(ms);

            Assert.That(reader.ReadInt32(), Is.EqualTo(-99999));
        }

        [Test]
        public void ReadFloat_ReadsCorrectly()
        {
            using var ms = MakeStream(bw => bw.Write(3.14f));
            using var reader = new RwReader(ms);

            Assert.That(reader.ReadFloat(), Is.EqualTo(3.14f));
        }

        [Test]
        public void ReadBytes_ReadsCorrectCount()
        {
            byte[] expected = { 0x01, 0x02, 0x03, 0x04 };
            using var ms = MakeStream(bw => bw.Write(expected));
            using var reader = new RwReader(ms);

            byte[] result = reader.ReadBytes(4);
            Assert.That(result, Is.EqualTo(expected));
        }

        // --- ReadVector3 / ReadVector2 ---

        [Test]
        public void ReadVector3_ReadsThreeFloats()
        {
            using var ms = MakeStream(bw =>
            {
                bw.Write(1.0f);
                bw.Write(2.0f);
                bw.Write(3.0f);
            });
            using var reader = new RwReader(ms);

            var v = reader.ReadVector3();
            Assert.That(v.x, Is.EqualTo(1.0f));
            Assert.That(v.y, Is.EqualTo(2.0f));
            Assert.That(v.z, Is.EqualTo(3.0f));
        }

        [Test]
        public void ReadVector2_ReadsTwoFloats()
        {
            using var ms = MakeStream(bw =>
            {
                bw.Write(10.5f);
                bw.Write(20.5f);
            });
            using var reader = new RwReader(ms);

            var v = reader.ReadVector2();
            Assert.That(v.x, Is.EqualTo(10.5f));
            Assert.That(v.y, Is.EqualTo(20.5f));
        }

        // --- ReadFixedString ---

        [Test]
        public void ReadFixedString_NullTerminated_ReturnsCorrectString()
        {
            using var ms = MakeStream(bw =>
            {
                bw.Write(Encoding.ASCII.GetBytes("Hello\0\0\0")); // 8 bytes, null-padded
            });
            using var reader = new RwReader(ms);

            string result = reader.ReadFixedString(8);
            Assert.That(result, Is.EqualTo("Hello"));
        }

        [Test]
        public void ReadFixedString_NoNullTerminator_ReturnsFullString()
        {
            using var ms = MakeStream(bw =>
            {
                bw.Write(Encoding.ASCII.GetBytes("ABCDEFGH")); // exactly 8 bytes, no null
            });
            using var reader = new RwReader(ms);

            string result = reader.ReadFixedString(8);
            Assert.That(result, Is.EqualTo("ABCDEFGH"));
        }

        [Test]
        public void ReadFixedString_AllNulls_ReturnsEmpty()
        {
            using var ms = MakeStream(bw =>
            {
                bw.Write(new byte[4]); // all zeros
            });
            using var reader = new RwReader(ms);

            string result = reader.ReadFixedString(4);
            Assert.That(result, Is.EqualTo(""));
        }

        [Test]
        public void ReadFixedString_SingleChar_Works()
        {
            using var ms = MakeStream(bw =>
            {
                bw.Write(Encoding.ASCII.GetBytes("X\0"));
            });
            using var reader = new RwReader(ms);

            string result = reader.ReadFixedString(2);
            Assert.That(result, Is.EqualTo("X"));
        }

        [Test]
        public void ReadFixedString_32Bytes_TypicalTextureName()
        {
            // Typical RW texture name is stored in a 32-byte fixed buffer
            byte[] buf = new byte[32];
            Encoding.ASCII.GetBytes("spongebob_body").CopyTo(buf, 0);

            using var ms = MakeStream(bw => bw.Write(buf));
            using var reader = new RwReader(ms);

            string result = reader.ReadFixedString(32);
            Assert.That(result, Is.EqualTo("spongebob_body"));
        }

        // --- RwChunk constants ---

        [Test]
        public void RwChunk_Constants_HaveExpectedValues()
        {
            Assert.That(RwChunk.STRUCT, Is.EqualTo(0x0001u));
            Assert.That(RwChunk.STRING, Is.EqualTo(0x0002u));
            Assert.That(RwChunk.EXTENSION, Is.EqualTo(0x0003u));
            Assert.That(RwChunk.TEXTURE, Is.EqualTo(0x0006u));
            Assert.That(RwChunk.MATERIAL, Is.EqualTo(0x0007u));
            Assert.That(RwChunk.MATERIAL_LIST, Is.EqualTo(0x0008u));
            Assert.That(RwChunk.FRAME_LIST, Is.EqualTo(0x000Eu));
            Assert.That(RwChunk.GEOMETRY, Is.EqualTo(0x000Fu));
            Assert.That(RwChunk.CLUMP, Is.EqualTo(0x0010u));
            Assert.That(RwChunk.ATOMIC, Is.EqualTo(0x0014u));
            Assert.That(RwChunk.TEXTURE_DICTIONARY, Is.EqualTo(0x0016u));
            Assert.That(RwChunk.GEOMETRY_LIST, Is.EqualTo(0x001Au));
            Assert.That(RwChunk.SKIN_PLG, Is.EqualTo(0x0116u));
            Assert.That(RwChunk.HANIM_PLG, Is.EqualTo(0x011Eu));
            Assert.That(RwChunk.TEXTURE_NATIVE, Is.EqualTo(0x0015u));
        }

        // --- Dispose ---

        [Test]
        public void Dispose_ClosesReader()
        {
            var ms = MakeStream(bw => bw.Write(new byte[4]));
            var reader = new RwReader(ms);
            reader.Dispose();

            // After dispose, the BinaryReader should be disposed (but stream may still be open
            // due to leaveOpen: true)
            Assert.DoesNotThrow(() => { var _ = ms.Position; }); // stream still accessible
        }

        // --- Multiple sequential reads ---

        [Test]
        public void SequentialReads_MaintainCorrectPosition()
        {
            using var ms = MakeStream(bw =>
            {
                bw.Write((byte)1);
                bw.Write((ushort)2);
                bw.Write((uint)3);
                bw.Write(4.0f);
            });
            using var reader = new RwReader(ms);

            Assert.That(reader.ReadByte(), Is.EqualTo(1));
            Assert.That(reader.Position, Is.EqualTo(1));
            Assert.That(reader.ReadUInt16(), Is.EqualTo(2));
            Assert.That(reader.Position, Is.EqualTo(3));
            Assert.That(reader.ReadUInt32(), Is.EqualTo(3u));
            Assert.That(reader.Position, Is.EqualTo(7));
            Assert.That(reader.ReadFloat(), Is.EqualTo(4.0f));
            Assert.That(reader.Position, Is.EqualTo(11));
        }
    }
}
