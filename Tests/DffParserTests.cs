using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using BfbbImport;

namespace DffImporter.Tests
{
    [TestFixture]
    public class DffParserTests
    {
        /// <summary>
        /// Helper to write a RW chunk header (12 bytes: type, size, libraryId).
        /// </summary>
        private static void WriteChunkHeader(BinaryWriter bw, uint type, uint size, uint libraryId = 0x0310)
        {
            bw.Write(type);
            bw.Write(size);
            bw.Write(libraryId);
        }

        /// <summary>
        /// Builds a minimal valid .dff stream with the given number of frames, geometries, and atomics.
        /// All geometries have zero triangles and a single morph target with vertices.
        /// </summary>
        private static MemoryStream BuildMinimalDff(
            int numFrames = 1, int numGeometries = 1, int numAtomics = 1,
            string[]? frameNames = null, bool addSkin = false, int vertsPerGeom = 3)
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

            // We build the inner content first, then wrap it in the CLUMP chunk.
            var inner = new MemoryStream();
            var iw = new BinaryWriter(inner, Encoding.ASCII, leaveOpen: true);

            // CLUMP -> STRUCT (numAtomics)
            uint clumpStructSize = 4; // just numAtomics
            WriteChunkHeader(iw, RwChunk.STRUCT, clumpStructSize);
            iw.Write(numAtomics);

            // FRAME_LIST
            WriteFrameList(iw, numFrames, frameNames);

            // GEOMETRY_LIST
            WriteGeometryList(iw, numGeometries, addSkin, vertsPerGeom);

            // ATOMICs
            for (int i = 0; i < numAtomics; i++)
                WriteAtomic(iw, frameIndex: i % numFrames, geometryIndex: i % numGeometries);

            iw.Flush();
            byte[] innerBytes = inner.ToArray();

            // Now write the CLUMP wrapper
            WriteChunkHeader(bw, RwChunk.CLUMP, (uint)innerBytes.Length);
            bw.Write(innerBytes);

            bw.Flush();
            ms.Position = 0;
            return ms;
        }

        private static void WriteFrameList(BinaryWriter bw, int numFrames, string[] names)
        {
            // FRAME_LIST content: STRUCT + extensions
            var content = new MemoryStream();
            var cw = new BinaryWriter(content, Encoding.ASCII, leaveOpen: true);

            // STRUCT: numFrames + frame data
            uint perFrame = 4 * 3 * 3 + 4 * 3 + 4 + 4; // 3 Vec3s (right,up,at) + pos Vec3 + parentIndex + creationFlags = 56
            uint structBodySize = 4 + (perFrame * (uint)numFrames);
            WriteChunkHeader(cw, RwChunk.STRUCT, structBodySize);
            cw.Write(numFrames);
            for (int i = 0; i < numFrames; i++)
            {
                // right vector (1,0,0)
                cw.Write(1.0f); cw.Write(0.0f); cw.Write(0.0f);
                // up vector (0,1,0)
                cw.Write(0.0f); cw.Write(1.0f); cw.Write(0.0f);
                // at vector (0,0,1)
                cw.Write(0.0f); cw.Write(0.0f); cw.Write(1.0f);
                // position
                cw.Write((float)i); cw.Write(0.0f); cw.Write(0.0f);
                // parent index
                cw.Write(i == 0 ? -1 : 0);
                // creation flags
                cw.Write((uint)0);
            }

            // Frame extensions (one EXTENSION per frame)
            for (int i = 0; i < numFrames; i++)
            {
                var extContent = new MemoryStream();
                var ew = new BinaryWriter(extContent, Encoding.ASCII, leaveOpen: true);

                if (names != null && i < names.Length && names[i] != null)
                {
                    byte[] nameBytes = Encoding.ASCII.GetBytes(names[i]);
                    // Pad to 4-byte alignment
                    int paddedLen = ((nameBytes.Length + 3) / 4) * 4;
                    byte[] padded = new byte[paddedLen];
                    Array.Copy(nameBytes, padded, nameBytes.Length);

                    WriteChunkHeader(ew, RwChunk.STRING, (uint)padded.Length);
                    ew.Write(padded);
                }

                ew.Flush();
                byte[] extBytes = extContent.ToArray();
                WriteChunkHeader(cw, RwChunk.EXTENSION, (uint)extBytes.Length);
                cw.Write(extBytes);
            }

            cw.Flush();
            byte[] flBytes = content.ToArray();
            WriteChunkHeader(bw, RwChunk.FRAME_LIST, (uint)flBytes.Length);
            bw.Write(flBytes);
        }

        private static void WriteGeometryList(BinaryWriter bw, int numGeometries, bool addSkin, int vertsPerGeom)
        {
            var content = new MemoryStream();
            var cw = new BinaryWriter(content, Encoding.ASCII, leaveOpen: true);

            // STRUCT: numGeometries
            WriteChunkHeader(cw, RwChunk.STRUCT, 4);
            cw.Write(numGeometries);

            for (int g = 0; g < numGeometries; g++)
                WriteGeometry(cw, addSkin, vertsPerGeom);

            cw.Flush();
            byte[] glBytes = content.ToArray();
            WriteChunkHeader(bw, RwChunk.GEOMETRY_LIST, (uint)glBytes.Length);
            bw.Write(glBytes);
        }

        private static void WriteGeometry(BinaryWriter bw, bool addSkin, int numVertices)
        {
            var content = new MemoryStream();
            var cw = new BinaryWriter(content, Encoding.ASCII, leaveOpen: true);

            // STRUCT for geometry
            var structContent = new MemoryStream();
            var sw = new BinaryWriter(structContent, Encoding.ASCII, leaveOpen: true);

            uint flags = 0; // no texturing, no prelit, no native
            int numTriangles = 1;
            int numMorphTargets = 1;

            sw.Write(flags);
            sw.Write(numTriangles);
            sw.Write(numVertices);
            sw.Write(numMorphTargets);

            // Triangle (v2, v1, matId, v3) - note the RW ordering
            sw.Write((ushort)1); // v2
            sw.Write((ushort)0); // v1
            sw.Write((ushort)0); // matId
            sw.Write((ushort)2); // v3

            // Morph target 0: bounding sphere + vertices
            sw.Write(0.0f); sw.Write(0.0f); sw.Write(0.0f); sw.Write(10.0f); // bounding sphere
            sw.Write(1); // hasVertices
            sw.Write(0); // hasNormals
            for (int v = 0; v < numVertices; v++)
            {
                sw.Write((float)v); sw.Write((float)(v * 2)); sw.Write((float)(v * 3));
            }

            sw.Flush();
            byte[] structBytes = structContent.ToArray();
            WriteChunkHeader(cw, RwChunk.STRUCT, (uint)structBytes.Length);
            cw.Write(structBytes);

            // MATERIAL_LIST
            WriteMaterialList(cw, 1);

            // EXTENSION (with optional SKIN_PLG)
            if (addSkin)
            {
                var extContent = new MemoryStream();
                var ew = new BinaryWriter(extContent, Encoding.ASCII, leaveOpen: true);

                WriteSkinPlugin(ew, numVertices, numBones: 2);

                ew.Flush();
                byte[] extBytes = extContent.ToArray();
                WriteChunkHeader(cw, RwChunk.EXTENSION, (uint)extBytes.Length);
                cw.Write(extBytes);
            }
            else
            {
                WriteChunkHeader(cw, RwChunk.EXTENSION, 0);
            }

            cw.Flush();
            byte[] geomBytes = content.ToArray();
            WriteChunkHeader(bw, RwChunk.GEOMETRY, (uint)geomBytes.Length);
            bw.Write(geomBytes);
        }

        private static void WriteSkinPlugin(BinaryWriter bw, int numVertices, int numBones)
        {
            var content = new MemoryStream();
            var cw = new BinaryWriter(content, Encoding.ASCII, leaveOpen: true);

            cw.Write((byte)numBones);      // numBones
            cw.Write((byte)numBones);      // numUsedBones
            cw.Write((byte)4);             // maxWeightsPerVertex
            cw.Write((byte)0);             // padding

            // Used bone indices
            for (int i = 0; i < numBones; i++)
                cw.Write((byte)i);

            // Bone indices (4 per vertex)
            for (int v = 0; v < numVertices; v++)
            {
                cw.Write((byte)0);
                cw.Write((byte)(numBones > 1 ? 1 : 0));
                cw.Write((byte)0);
                cw.Write((byte)0);
            }

            // Bone weights (4 per vertex)
            for (int v = 0; v < numVertices; v++)
            {
                cw.Write(0.7f);
                cw.Write(0.3f);
                cw.Write(0.0f);
                cw.Write(0.0f);
            }

            // Inverse bone matrices (4x4 per bone)
            for (int b = 0; b < numBones; b++)
                for (int i = 0; i < 16; i++)
                    cw.Write(i % 5 == 0 ? 1.0f : 0.0f); // identity-ish

            cw.Flush();
            byte[] skinBytes = content.ToArray();
            WriteChunkHeader(bw, RwChunk.SKIN_PLG, (uint)skinBytes.Length);
            bw.Write(skinBytes);
        }

        private static void WriteMaterialList(BinaryWriter bw, int numMaterials)
        {
            var content = new MemoryStream();
            var cw = new BinaryWriter(content, Encoding.ASCII, leaveOpen: true);

            // STRUCT: numMaterials + shared indices
            uint structSize = (uint)(4 + 4 * numMaterials);
            WriteChunkHeader(cw, RwChunk.STRUCT, structSize);
            cw.Write(numMaterials);
            for (int i = 0; i < numMaterials; i++)
                cw.Write(-1); // not shared

            // Each material
            for (int i = 0; i < numMaterials; i++)
                WriteMaterial(cw);

            cw.Flush();
            byte[] mlBytes = content.ToArray();
            WriteChunkHeader(bw, RwChunk.MATERIAL_LIST, (uint)mlBytes.Length);
            bw.Write(mlBytes);
        }

        private static void WriteMaterial(BinaryWriter bw)
        {
            var content = new MemoryStream();
            var cw = new BinaryWriter(content, Encoding.ASCII, leaveOpen: true);

            // STRUCT
            uint structSize = 4 + 4 + 4 + 4 + 4 * 3; // flags + color + unused + isTextured + ambient/specular/diffuse
            WriteChunkHeader(cw, RwChunk.STRUCT, structSize);
            cw.Write((uint)0);       // flags
            cw.Write((byte)255);     // r
            cw.Write((byte)128);     // g
            cw.Write((byte)64);      // b
            cw.Write((byte)255);     // a
            cw.Write((int)0);        // unused
            cw.Write((int)0);        // isTextured = false
            cw.Write(1.0f);          // ambient
            cw.Write(1.0f);          // specular
            cw.Write(1.0f);          // diffuse

            // EXTENSION
            WriteChunkHeader(cw, RwChunk.EXTENSION, 0);

            cw.Flush();
            byte[] matBytes = content.ToArray();
            WriteChunkHeader(bw, RwChunk.MATERIAL, (uint)matBytes.Length);
            bw.Write(matBytes);
        }

        private static void WriteAtomic(BinaryWriter bw, int frameIndex, int geometryIndex)
        {
            var content = new MemoryStream();
            var cw = new BinaryWriter(content, Encoding.ASCII, leaveOpen: true);

            // STRUCT: frameIndex, geometryIndex, renderFlags, unused
            WriteChunkHeader(cw, RwChunk.STRUCT, 16);
            cw.Write(frameIndex);
            cw.Write(geometryIndex);
            cw.Write(0x05); // render flags
            cw.Write(0);    // unused

            // EXTENSION
            WriteChunkHeader(cw, RwChunk.EXTENSION, 0);

            cw.Flush();
            byte[] atomicBytes = content.ToArray();
            WriteChunkHeader(bw, RwChunk.ATOMIC, (uint)atomicBytes.Length);
            bw.Write(atomicBytes);
        }

        // =================== TESTS ===================

        [Test]
        public void Parse_MinimalDff_ReturnsClump()
        {
            using var ms = BuildMinimalDff();
            var clump = DffParser.Parse(ms);

            Assert.That(clump, Is.Not.Null);
            Assert.That(clump.Frames, Has.Count.EqualTo(1));
            Assert.That(clump.Geometries, Has.Count.EqualTo(1));
            Assert.That(clump.Atomics, Has.Count.EqualTo(1));
        }

        [Test]
        public void Parse_MultipleFrames_AllParsed()
        {
            using var ms = BuildMinimalDff(numFrames: 4, numAtomics: 1);
            var clump = DffParser.Parse(ms);

            Assert.That(clump.Frames, Has.Count.EqualTo(4));
            Assert.That(clump.Frames[0].ParentIndex, Is.EqualTo(-1)); // root
            Assert.That(clump.Frames[1].ParentIndex, Is.EqualTo(0));
            Assert.That(clump.Frames[2].ParentIndex, Is.EqualTo(0));
            Assert.That(clump.Frames[3].ParentIndex, Is.EqualTo(0));
        }

        [Test]
        public void Parse_FrameNames_Assigned()
        {
            using var ms = BuildMinimalDff(numFrames: 2, numAtomics: 1,
                frameNames: new[] { "Root", "Child1" });
            var clump = DffParser.Parse(ms);

            Assert.That(clump.Frames[0].Name, Is.EqualTo("Root"));
            Assert.That(clump.Frames[1].Name, Is.EqualTo("Child1"));
        }

        [Test]
        public void Parse_FramePositions_Correct()
        {
            using var ms = BuildMinimalDff(numFrames: 3, numAtomics: 1);
            var clump = DffParser.Parse(ms);

            // Our builder sets position = (i, 0, 0) for frame i
            Assert.That(clump.Frames[0].LocalMatrix.GetColumn(3).x, Is.EqualTo(0.0f));
            Assert.That(clump.Frames[1].LocalMatrix.GetColumn(3).x, Is.EqualTo(1.0f));
            Assert.That(clump.Frames[2].LocalMatrix.GetColumn(3).x, Is.EqualTo(2.0f));
        }

        [Test]
        public void Parse_GeometryVertices_Correct()
        {
            using var ms = BuildMinimalDff(vertsPerGeom: 4);
            var clump = DffParser.Parse(ms);

            var geom = clump.Geometries[0];
            Assert.That(geom.Vertices, Has.Length.EqualTo(4));

            // Vertex 0 = (0, 0, 0)
            Assert.That(geom.Vertices[0].x, Is.EqualTo(0.0f));
            Assert.That(geom.Vertices[0].y, Is.EqualTo(0.0f));
            Assert.That(geom.Vertices[0].z, Is.EqualTo(0.0f));

            // Vertex 2 = (2, 4, 6) based on our builder
            Assert.That(geom.Vertices[2].x, Is.EqualTo(2.0f));
            Assert.That(geom.Vertices[2].y, Is.EqualTo(4.0f));
            Assert.That(geom.Vertices[2].z, Is.EqualTo(6.0f));
        }

        [Test]
        public void Parse_Triangles_Correct()
        {
            using var ms = BuildMinimalDff();
            var clump = DffParser.Parse(ms);

            var geom = clump.Geometries[0];
            Assert.That(geom.Triangles, Has.Count.EqualTo(1));
            Assert.That(geom.Triangles[0].v1, Is.EqualTo(0));
            Assert.That(geom.Triangles[0].v2, Is.EqualTo(1));
            Assert.That(geom.Triangles[0].v3, Is.EqualTo(2));
            Assert.That(geom.Triangles[0].matId, Is.EqualTo(0));
        }

        [Test]
        public void Parse_Materials_ColorCorrect()
        {
            using var ms = BuildMinimalDff();
            var clump = DffParser.Parse(ms);

            var mat = clump.Geometries[0].Materials[0];
            Assert.That(mat.Color.r, Is.EqualTo(255));
            Assert.That(mat.Color.g, Is.EqualTo(128));
            Assert.That(mat.Color.b, Is.EqualTo(64));
            Assert.That(mat.Color.a, Is.EqualTo(255));
        }

        [Test]
        public void Parse_AtomicIndices_Correct()
        {
            using var ms = BuildMinimalDff(numFrames: 2, numGeometries: 2, numAtomics: 2);
            var clump = DffParser.Parse(ms);

            Assert.That(clump.Atomics, Has.Count.EqualTo(2));
            Assert.That(clump.Atomics[0].FrameIndex, Is.EqualTo(0));
            Assert.That(clump.Atomics[0].GeometryIndex, Is.EqualTo(0));
            Assert.That(clump.Atomics[1].FrameIndex, Is.EqualTo(1));
            Assert.That(clump.Atomics[1].GeometryIndex, Is.EqualTo(1));
        }

        [Test]
        public void Parse_WithSkin_ParsesSkinData()
        {
            using var ms = BuildMinimalDff(addSkin: true, vertsPerGeom: 3);
            var clump = DffParser.Parse(ms);

            var skin = clump.Geometries[0].Skin;
            Assert.That(skin, Is.Not.Null);
            Assert.That(skin.NumBones, Is.EqualTo(2));
            Assert.That(skin.BoneIndices, Has.Length.EqualTo(3 * 4));
            Assert.That(skin.BoneWeights, Has.Length.EqualTo(3 * 4));
            Assert.That(skin.InverseBoneMatrices, Has.Length.EqualTo(2));

            // Check bone weights for first vertex
            Assert.That(skin.BoneWeights[0], Is.EqualTo(0.7f).Within(0.001f));
            Assert.That(skin.BoneWeights[1], Is.EqualTo(0.3f).Within(0.001f));
        }

        [Test]
        public void Parse_WithoutSkin_SkinIsNull()
        {
            using var ms = BuildMinimalDff(addSkin: false);
            var clump = DffParser.Parse(ms);

            Assert.That(clump.Geometries[0].Skin, Is.Null);
        }

        [Test]
        public void Parse_MultipleGeometries_AllParsed()
        {
            using var ms = BuildMinimalDff(numGeometries: 3, numAtomics: 3, numFrames: 3);
            var clump = DffParser.Parse(ms);

            Assert.That(clump.Geometries, Has.Count.EqualTo(3));
        }

        [Test]
        public void Parse_InvalidRootChunk_ThrowsInvalidDataException()
        {
            var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
            bw.Write((uint)0x9999); // invalid chunk type
            bw.Write((uint)0);
            bw.Write((uint)0);
            ms.Position = 0;

            Assert.Throws<InvalidDataException>(() => DffParser.Parse(ms));
        }

        [Test]
        public void Parse_NoGeometry_ThrowsOnMissingChunk()
        {
            // CLUMP with only a STRUCT and no FRAME_LIST or GEOMETRY_LIST
            var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

            // CLUMP header
            var inner = new MemoryStream();
            var iw = new BinaryWriter(inner, Encoding.ASCII, leaveOpen: true);

            WriteChunkHeader(iw, RwChunk.STRUCT, 4);
            iw.Write(0); // numAtomics

            iw.Flush();
            byte[] innerBytes = inner.ToArray();
            WriteChunkHeader(bw, RwChunk.CLUMP, (uint)innerBytes.Length);
            bw.Write(innerBytes);

            bw.Flush();
            ms.Position = 0;

            Assert.Throws<InvalidDataException>(() => DffParser.Parse(ms));
        }

        [Test]
        public void Parse_BoneIndexAssignment_WorksCorrectly()
        {
            // Build a DFF with HAnim bone IDs on some frames
            // Our builder doesn't add HAnim, but we can verify AssignBoneIndices
            // only assigns to frames with BoneId >= 0 (which none have in our test data)
            using var ms = BuildMinimalDff(numFrames: 3);
            var clump = DffParser.Parse(ms);

            // Without HAnim plugin data, all BoneId should be -1
            foreach (var f in clump.Frames)
            {
                Assert.That(f.BoneId, Is.EqualTo(-1));
                Assert.That(f.BoneIndex, Is.EqualTo(-1));
            }
        }

        [Test]
        public void Parse_GeometryFlags_Stored()
        {
            using var ms = BuildMinimalDff();
            var clump = DffParser.Parse(ms);

            // Our builder sets flags = 0
            Assert.That(clump.Geometries[0].Flags, Is.EqualTo(0u));
        }
    }
}
