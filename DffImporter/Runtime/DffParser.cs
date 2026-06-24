using System;
using System.IO;
using UnityEngine;

namespace BfbbImport
{
    /// <summary>
    /// Parses a RenderWare 3.x .dff (Clump) stream into the plain data model in RwModel.cs.
    ///
    /// Scope / limitations:
    /// - Targets the generic, platform-independent RW3 chunk layout (the format used by most
    ///   PC-side BfBB modding tools and generic RW3 exporters of that era).
    /// - Does NOT handle "native" platform-packed geometry (e.g. PS2 native vertex buffers,
    ///   Xbox native geometry). If your source .dff was extracted directly from a PS2/GameCube
    ///   disc rather than a PC build, geometry chunks may be in a native binary layout this parser
    ///   does not decode — you'll get a clear exception rather than a silently-wrong mesh.
    /// - Skinning (bones) follows the standard SKIN_PLG layout (numBones, per-vertex 4x indices/weights,
    ///   inverse bone matrices). HAnim bone IDs are read from each frame's extension, if present.
    /// </summary>
    public static class DffParser
    {
        public static RwClump Parse(Stream stream)
        {
            using var r = new RwReader(stream);

            long clumpEnd = r.ReadChunkHeader(out var header);
            if (header.Type != RwChunk.CLUMP)
                throw new InvalidDataException($"Expected CLUMP chunk at root, got 0x{header.Type:X4}. " +
                                                 "This may not be a generic-format RW3 .dff, or the file is corrupt.");

            var clump = new RwClump();

            long structEnd = r.ExpectStruct("CLUMP");
            int numAtomics = r.ReadInt32();
            r.Seek(structEnd); // skip numLights/numCameras if present — not needed for import

            long flEnd = r.FindChunk(clumpEnd, RwChunk.FRAME_LIST);
            ReadFrameList(r, flEnd, clump);
            r.Seek(flEnd);

            long glEnd = r.FindChunk(clumpEnd, RwChunk.GEOMETRY_LIST);
            ReadGeometryList(r, glEnd, clump);
            r.Seek(glEnd);

            for (int i = 0; i < numAtomics; i++)
            {
                long atEnd = r.FindChunk(clumpEnd, RwChunk.ATOMIC);
                clump.Atomics.Add(ReadAtomic(r, atEnd));
                r.Seek(atEnd);
            }

            AssignBoneIndices(clump);
            return clump;
        }

        // --- frame list ---------------------------------------------------------

        private static void ReadFrameList(RwReader r, long listEnd, RwClump clump)
        {
            long structEnd = r.ExpectStruct("FRAME_LIST");
            int numFrames = r.ReadInt32();

            for (int i = 0; i < numFrames; i++)
            {
                var f = new RwFrame();
                Vector3 right = r.ReadVector3();
                Vector3 up = r.ReadVector3();
                Vector3 at = r.ReadVector3();
                Vector3 pos = r.ReadVector3();
                f.ParentIndex = r.ReadInt32();
                r.ReadUInt32(); // creation flags, unused

                var m = Matrix4x4.identity;
                m.SetColumn(0, new Vector4(right.x, right.y, right.z, 0));
                m.SetColumn(1, new Vector4(up.x, up.y, up.z, 0));
                m.SetColumn(2, new Vector4(at.x, at.y, at.z, 0));
                m.SetColumn(3, new Vector4(pos.x, pos.y, pos.z, 1));
                f.LocalMatrix = m;

                clump.Frames.Add(f);
            }
            r.Seek(structEnd);

            for (int i = 0; i < numFrames && r.Position < listEnd; i++)
            {
                long extEnd = r.ReadChunkHeader(out var eh);
                if (eh.Type != RwChunk.EXTENSION)
                    return; // layout mismatch — stop rather than guess

                ParseFrameExtension(r, extEnd, clump.Frames[i]);
                r.Seek(extEnd);
            }
        }

        private static void ParseFrameExtension(RwReader r, long extEnd, RwFrame frame)
        {
            while (r.Position < extEnd)
            {
                long chunkEnd = r.ReadChunkHeader(out var h);
                if (h.Type == RwChunk.STRING)
                {
                    frame.Name = r.ReadFixedString((int)h.Size);
                }
                else if (h.Type == RwChunk.HANIM_PLG)
                {
                    r.ReadInt32(); // version, unused
                    frame.BoneId = r.ReadInt32();
                    // bone hierarchy table intentionally not parsed — skin weights bind by array order
                }
                r.Seek(chunkEnd);
            }
        }

        // --- geometry list --------------------------------------------------------

        private static void ReadGeometryList(RwReader r, long listEnd, RwClump clump)
        {
            long structEnd = r.ExpectStruct("GEOMETRY_LIST");
            int numGeometries = r.ReadInt32();
            r.Seek(structEnd);

            for (int i = 0; i < numGeometries; i++)
            {
                long geomEnd = r.ReadChunkHeader(out var gh);
                if (gh.Type != RwChunk.GEOMETRY)
                    throw new InvalidDataException($"Expected GEOMETRY chunk, got 0x{gh.Type:X4}.");
                clump.Geometries.Add(ReadGeometry(r, geomEnd));
                r.Seek(geomEnd);
            }
        }

        private const uint FLAG_TEXTURED = 0x00000004;
        private const uint FLAG_PRELIT = 0x00000008;
        private const uint FLAG_TEXTURED2 = 0x00000080;
        private const uint FLAG_NATIVE = 0x01000000;

        private static RwGeometry ReadGeometry(RwReader r, long geomEnd)
        {
            var g = new RwGeometry();

            long structEnd = r.ExpectStruct("GEOMETRY");

            uint flags = r.ReadUInt32();
            g.Flags = flags;
            int numTriangles = r.ReadInt32();
            int numVertices = r.ReadInt32();
            int numMorphTargets = r.ReadInt32();

            int numTexSets = (int)((flags >> 16) & 0xFF);
            if (numTexSets == 0 && (flags & (FLAG_TEXTURED | FLAG_TEXTURED2)) != 0)
                numTexSets = 1;

            bool isNative = (flags & FLAG_NATIVE) != 0;

            // Non-native block: prelit colors, then UV sets, then triangles — all absent for
            // native (pre-instanced) geometry, which this importer doesn't support decoding.
            if (!isNative)
            {
                if ((flags & FLAG_PRELIT) != 0)
                {
                    g.PrelitColors = new Color32[numVertices];
                    for (int v = 0; v < numVertices; v++)
                        g.PrelitColors[v] = r.ReadColor32();
                }

                for (int t = 0; t < numTexSets; t++)
                {
                    var uvs = new Vector2[numVertices];
                    for (int v = 0; v < numVertices; v++)
                    {
                        var uv = r.ReadVector2();
                        uv.y = 1f - uv.y; // RW V is flipped relative to Unity
                        uvs[v] = uv;
                    }
                    g.TexCoordSets.Add(uvs);
                }

                for (int t = 0; t < numTriangles; t++)
                {
                    ushort v2 = r.ReadUInt16();
                    ushort v1 = r.ReadUInt16();
                    ushort matId = r.ReadUInt16();
                    ushort v3 = r.ReadUInt16();
                    g.Triangles.Add((v1, v2, v3, matId));
                }
            }

            // Morph targets: bounding sphere + hasVertices/hasNormals bools + the actual data.
            // This comes AFTER the block above, not before — only morph target 0 is kept.
            int morphIterations = Math.Max(numMorphTargets, 1);
            for (int m = 0; m < morphIterations; m++)
            {
                if (numMorphTargets > 0)
                {
                    r.ReadFloat(); r.ReadFloat(); r.ReadFloat(); r.ReadFloat(); // bounding sphere
                    int hasVertices = r.ReadInt32();
                    int hasNormals = r.ReadInt32();

                    if (m == 0)
                    {
                        if (hasVertices != 0)
                        {
                            g.Vertices = new Vector3[numVertices];
                            for (int v = 0; v < numVertices; v++) g.Vertices[v] = r.ReadVector3();
                        }
                        if (hasNormals != 0)
                        {
                            g.Normals = new Vector3[numVertices];
                            for (int v = 0; v < numVertices; v++) g.Normals[v] = r.ReadVector3();
                        }
                    }
                    else
                    {
                        if (hasVertices != 0) r.Skip(12L * numVertices);
                        if (hasNormals != 0) r.Skip(12L * numVertices);
                    }
                }
            }

            if (r.Position != structEnd)
            {
                Debug.LogWarning($"[DffImporter] Geometry struct misaligned: read up to byte {r.Position}, " +
                                  $"chunk declares end at {structEnd} (delta {structEnd - r.Position} bytes). " +
                                  $"flags=0x{flags:X8} numVerts={numVertices} numTris={numTriangles} " +
                                  $"numMorphTargets={numMorphTargets} numTexSets={numTexSets} " +
                                  $"prelit={(flags & FLAG_PRELIT) != 0}. " +
                                  "Triangle data for this geometry was very likely read from the wrong offset — " +
                                  "please report these exact numbers if you see this.");
            }
            r.Seek(structEnd);
            long matListEnd = r.FindChunk(geomEnd, RwChunk.MATERIAL_LIST);
            ReadMaterialList(r, matListEnd, g);
            r.Seek(matListEnd);

            if (r.Position < geomEnd)
            {
                long peekPos = r.Position;
                long extEnd = r.ReadChunkHeader(out var eh);
                if (eh.Type == RwChunk.EXTENSION)
                    ParseGeometryExtension(r, extEnd, g, numVertices);
                else
                    r.Seek(peekPos);
            }

            return g;
        }

        private static void ReadMaterialList(RwReader r, long listEnd, RwGeometry g)
        {
            long structEnd = r.ExpectStruct("MATERIAL_LIST");
            int numMaterials = r.ReadInt32();
            var sharedIndices = new int[numMaterials];
            for (int i = 0; i < numMaterials; i++) sharedIndices[i] = r.ReadInt32();
            r.Seek(structEnd);

            for (int i = 0; i < numMaterials; i++)
            {
                if (sharedIndices[i] >= 0)
                {
                    g.Materials.Add(g.Materials[sharedIndices[i]]);
                    continue;
                }
                long matEnd = r.ReadChunkHeader(out var mh);
                if (mh.Type != RwChunk.MATERIAL) throw new InvalidDataException("Expected MATERIAL chunk.");
                g.Materials.Add(ReadMaterial(r, matEnd));
                r.Seek(matEnd);
            }
        }

        private static RwMaterial ReadMaterial(RwReader r, long matEnd)
        {
            var mat = new RwMaterial();

            long structEnd = r.ExpectStruct("MATERIAL");

            r.ReadUInt32(); // flags, unused
            mat.Color = r.ReadColor32();
            r.ReadInt32(); // unused
            int isTextured = r.ReadInt32();
            r.ReadFloat(); r.ReadFloat(); r.ReadFloat(); // ambient, specular, diffuse
            r.Seek(structEnd);

            if (isTextured != 0)
            {
                long texEnd = r.FindChunk(matEnd, RwChunk.TEXTURE);
                ReadTextureChunk(r, texEnd, mat);
                r.Seek(texEnd);
            }

            return mat;
        }

        private static void ReadTextureChunk(RwReader r, long texEnd, RwMaterial mat)
        {
            long structEnd = r.ExpectStruct("TEXTURE");
            r.ReadUInt32(); // filter/addressing mode, not needed for import
            r.Seek(structEnd);

            long nameEnd = r.ReadChunkHeader(out var nh);
            if (nh.Type == RwChunk.STRING) mat.TextureName = r.ReadFixedString((int)nh.Size);
            r.Seek(nameEnd);

            if (r.Position < texEnd)
            {
                long maskEnd = r.ReadChunkHeader(out var mh2);
                if (mh2.Type == RwChunk.STRING) mat.MaskName = r.ReadFixedString((int)mh2.Size);
                r.Seek(maskEnd);
            }
        }

        private static void ParseGeometryExtension(RwReader r, long extEnd, RwGeometry g, int numVertices)
        {
            while (r.Position < extEnd)
            {
                long chunkEnd = r.ReadChunkHeader(out var h);
                if (h.Type == RwChunk.SKIN_PLG)
                    g.Skin = ReadSkin(r, numVertices);
                r.Seek(chunkEnd);
            }
        }

        private static RwSkin ReadSkin(RwReader r, int numVertices)
        {
            var skin = new RwSkin();
            byte numBones = r.ReadByte();
            byte numUsedBones = r.ReadByte();
            r.ReadByte(); // max weights per vertex, unused
            r.ReadByte(); // padding

            skin.NumBones = numBones;

            if (numUsedBones > 0)
                r.Skip(numUsedBones); // used-bone index table — not needed for direct array binding

            skin.BoneIndices = new byte[4 * numVertices];
            for (int i = 0; i < 4 * numVertices; i++) skin.BoneIndices[i] = r.ReadByte();

            skin.BoneWeights = new float[4 * numVertices];
            for (int i = 0; i < 4 * numVertices; i++) skin.BoneWeights[i] = r.ReadFloat();

            skin.InverseBoneMatrices = new Matrix4x4[numBones];
            for (int b = 0; b < numBones; b++)
            {
                var m = new Matrix4x4();
                for (int col = 0; col < 4; col++)
                    for (int row = 0; row < 4; row++)
                        m[row, col] = r.ReadFloat();
                skin.InverseBoneMatrices[b] = m;
            }

            return skin;
        }

        // --- atomics ---------------------------------------------------------------

        private static RwAtomic ReadAtomic(RwReader r, long atomicEnd)
        {
            long structEnd = r.ExpectStruct("ATOMIC");

            var a = new RwAtomic
            {
                FrameIndex = r.ReadInt32(),
                GeometryIndex = r.ReadInt32()
            };
            r.Seek(structEnd);
            return a;
        }

        private static void AssignBoneIndices(RwClump clump)
        {
            int boneIdx = 0;
            foreach (var f in clump.Frames)
                if (f.BoneId >= 0)
                    f.BoneIndex = boneIdx++;
        }
    }
}
