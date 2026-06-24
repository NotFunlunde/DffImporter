using System.Collections.Generic;
using UnityEngine;

namespace BfbbImport
{
    public class RwFrame
    {
        public string Name = "";
        public int ParentIndex = -1;
        public Matrix4x4 LocalMatrix = Matrix4x4.identity;
        public int BoneId = -1;      // from HAnim plugin, -1 if not a bone
        public int BoneIndex = -1;   // index into skin bone array, -1 if not used by any skin
    }

    public class RwMaterial
    {
        public Color32 Color = new Color32(255, 255, 255, 255);
        public string TextureName;
        public string MaskName;
    }

    public class RwSkin
    {
        public int NumBones;
        public byte[] BoneIndices;   // 4 per vertex
        public float[] BoneWeights;  // 4 per vertex
        public Matrix4x4[] InverseBoneMatrices;
    }

    public class RwGeometry
    {
        public uint Flags;
        public Vector3[] Vertices;
        public Vector3[] Normals;
        public Color32[] PrelitColors;
        public List<Vector2[]> TexCoordSets = new List<Vector2[]>();
        // Triangles store (v1, v2, v3, materialId) per face, already reordered to Unity winding.
        public List<(int v1, int v2, int v3, int matId)> Triangles = new List<(int, int, int, int)>();
        public List<RwMaterial> Materials = new List<RwMaterial>();
        public RwSkin Skin;
    }

    public class RwAtomic
    {
        public int FrameIndex;
        public int GeometryIndex;
    }

    public class RwClump
    {
        public List<RwFrame> Frames = new List<RwFrame>();
        public List<RwGeometry> Geometries = new List<RwGeometry>();
        public List<RwAtomic> Atomics = new List<RwAtomic>();
    }

    public class RwTexture
    {
        public string Name;
        public string MaskName;
        public Texture2D Texture; // populated by TxdParser when a matching raster is found
    }
}
