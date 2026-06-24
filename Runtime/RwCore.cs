using System;
using System.IO;
using System.Text;

namespace BfbbImport
{
    /// <summary>
    /// RenderWare 3.x chunk type IDs relevant to BfBB-era .dff / .txd files.
    /// Not exhaustive — only what's needed to read geometry, materials, frames, and skin data.
    /// </summary>
    public static class RwChunk
    {
        public const uint STRUCT = 0x0001;
        public const uint STRING = 0x0002;
        public const uint EXTENSION = 0x0003;
        public const uint TEXTURE = 0x0006;
        public const uint MATERIAL = 0x0007;
        public const uint MATERIAL_LIST = 0x0008;
        public const uint FRAME_LIST = 0x000E;
        public const uint GEOMETRY = 0x000F;
        public const uint CLUMP = 0x0010;
        public const uint ATOMIC = 0x0014;
        public const uint TEXTURE_DICTIONARY = 0x0016;
        public const uint GEOMETRY_LIST = 0x001A;
        public const uint MATERIAL_EFFECTS_PLG = 0x0120;
        public const uint SKIN_PLG = 0x0116;
        public const uint HANIM_PLG = 0x011E; // hierarchical animation (bone) plugin
        public const uint USER_DATA_PLG = 0x0011;
        public const uint RIGHT_TO_RENDER = 0x001f;
        public const uint TEXTURE_NATIVE = 0x0015;
    }

    /// <summary>Header for every RW chunk: type, size of body in bytes, library version.</summary>
    public struct RwChunkHeader
    {
        public uint Type;
        public uint Size;
        public uint LibraryId; // packed version/build

        public override string ToString() => $"Chunk(0x{Type:X4}, size={Size})";
    }

    /// <summary>
    /// Thin wrapper over BinaryReader with RW-specific helpers (chunk headers, fixed strings).
    /// All RW3 PC-format data is little-endian.
    /// </summary>
    public class RwReader : IDisposable
    {
        public readonly BinaryReader BR;
        public readonly Stream Base;

        public RwReader(Stream s)
        {
            Base = s;
            BR = new BinaryReader(s, Encoding.ASCII, leaveOpen: true);
        }

        public long Position => Base.Position;
        public long Length => Base.Length;

        public RwChunkHeader ReadChunkHeader()
        {
            return new RwChunkHeader
            {
                Type = BR.ReadUInt32(),
                Size = BR.ReadUInt32(),
                LibraryId = BR.ReadUInt32()
            };
        }

        /// <summary>Reads a chunk header and returns the absolute stream offset where its body ends.</summary>
        public long ReadChunkHeader(out RwChunkHeader header)
        {
            header = ReadChunkHeader();
            return Base.Position + header.Size;
        }

        public void Seek(long pos) => Base.Position = pos;
        public void Skip(long count) => Base.Position += count;

        public byte ReadByte() => BR.ReadByte();
        public sbyte ReadSByte() => BR.ReadSByte();
        public ushort ReadUInt16() => BR.ReadUInt16();
        public short ReadInt16() => BR.ReadInt16();
        public uint ReadUInt32() => BR.ReadUInt32();
        public int ReadInt32() => BR.ReadInt32();
        public float ReadFloat() => BR.ReadSingle();
        public byte[] ReadBytes(int n) => BR.ReadBytes(n);

        public UnityEngine.Vector3 ReadVector3() => new UnityEngine.Vector3(ReadFloat(), ReadFloat(), ReadFloat());
        public UnityEngine.Vector2 ReadVector2() => new UnityEngine.Vector2(ReadFloat(), ReadFloat());

        /// <summary>RW strings inside STRING chunks are null-padded fixed buffers.</summary>
        public string ReadFixedString(int length)
        {
            var bytes = BR.ReadBytes(length);
            int end = Array.IndexOf(bytes, (byte)0);
            if (end < 0) end = bytes.Length;
            return Encoding.ASCII.GetString(bytes, 0, end);
        }

        public void Dispose() => BR.Dispose();
    }
}
