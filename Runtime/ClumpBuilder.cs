using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BfbbImport
{
    /// <summary>
    /// Converts a parsed RwClump into a Unity scene graph: a root GameObject with one child
    /// per atomic (mesh), using SkinnedMeshRenderer when skin data is present and MeshRenderer
    /// otherwise. Frame hierarchy becomes the Transform hierarchy.
    /// </summary>
    public static class ClumpBuilder
    {
        public static GameObject Build(RwClump clump, string rootName, Dictionary<string, Texture2D> textures, Shader shader)
        {
            var root = new GameObject(rootName);

            // Create one Transform per frame, matching the frame hierarchy.
            var frameTransforms = new Transform[clump.Frames.Count];
            for (int i = 0; i < clump.Frames.Count; i++)
            {
                var f = clump.Frames[i];
                var go = new GameObject(string.IsNullOrEmpty(f.Name) ? $"Frame_{i}" : f.Name);
                frameTransforms[i] = go.transform;
            }
            for (int i = 0; i < clump.Frames.Count; i++)
            {
                var f = clump.Frames[i];
                var t = frameTransforms[i];
                t.SetParent(f.ParentIndex >= 0 ? frameTransforms[f.ParentIndex] : root.transform, false);
                ApplyLocalMatrix(t, f.LocalMatrix);
            }

            // Bone array for skinning, in the order bones were encountered (AssignBoneIndices order).
            var boneList = new List<Transform>();
            for (int i = 0; i < clump.Frames.Count; i++)
                if (clump.Frames[i].BoneId >= 0)
                    boneList.Add(frameTransforms[i]);
            Transform[] boneArray = boneList.ToArray();

            foreach (var atomic in clump.Atomics)
            {
                if (atomic.FrameIndex < 0 || atomic.FrameIndex >= frameTransforms.Length)
                {
                    Debug.LogWarning($"[DffImporter] Atomic references out-of-range frame index {atomic.FrameIndex} " +
                                      $"(model has {frameTransforms.Length} frames). Skipping this atomic.");
                    continue;
                }
                if (atomic.GeometryIndex < 0 || atomic.GeometryIndex >= clump.Geometries.Count)
                {
                    Debug.LogWarning($"[DffImporter] Atomic references out-of-range geometry index {atomic.GeometryIndex} " +
                                      $"(model has {clump.Geometries.Count} geometries). Skipping this atomic.");
                    continue;
                }

                var geom = clump.Geometries[atomic.GeometryIndex];
                var hostTransform = frameTransforms[atomic.FrameIndex];

                var mesh = BuildMesh(geom);
                var materials = BuildMaterials(geom, textures, shader);

                if (geom.Skin != null && boneArray.Length > 0)
                {
                    AssignSkin(mesh, geom.Skin, boneArray.Length);
                    var smr = hostTransform.gameObject.AddComponent<SkinnedMeshRenderer>();
                    smr.bones = boneArray;
                    smr.sharedMesh = mesh;
                    smr.sharedMaterials = materials;
                    smr.rootBone = boneArray.Length > 0 ? boneArray[0] : hostTransform;
                }
                else
                {
                    var mf = hostTransform.gameObject.AddComponent<MeshFilter>();
                    mf.sharedMesh = mesh;
                    var mr = hostTransform.gameObject.AddComponent<MeshRenderer>();
                    mr.sharedMaterials = materials;
                }
            }

            return root;
        }

        private static void ApplyLocalMatrix(Transform t, Matrix4x4 m)
        {
            t.localPosition = m.GetColumn(3);
            t.localRotation = m.rotation;
            // RW frame matrices are expected to be orthonormal (no scale); if a model uses
            // baked scale, that information is lost here and would need extracting separately.
        }

        private static Mesh BuildMesh(RwGeometry g)
        {
            var mesh = new Mesh();
            if (g.Vertices != null) mesh.vertices = g.Vertices;
            if (g.Normals != null) mesh.normals = g.Normals;
            if (g.PrelitColors != null) mesh.colors32 = g.PrelitColors;
            if (g.TexCoordSets.Count > 0) mesh.uv = g.TexCoordSets[0];
            if (g.TexCoordSets.Count > 1) mesh.uv2 = g.TexCoordSets[1];

            // Only create submeshes for matIds that actually have triangles — an empty submesh
            // produces a harmless but noisy "invalid MinMaxAABB" warning when Unity tries to
            // compute bounds for zero triangles.
            var trisByMat = new Dictionary<int, List<int>>();
            int vertCount = g.Vertices?.Length ?? 0;
            int skippedOutOfRange = 0;

            foreach (var (v1, v2, v3, matId) in g.Triangles)
            {
                if (v1 < 0 || v1 >= vertCount || v2 < 0 || v2 >= vertCount || v3 < 0 || v3 >= vertCount)
                {
                    skippedOutOfRange++;
                    continue; // out-of-range — almost certainly a chunk-parsing misalignment upstream
                }
                if (!trisByMat.TryGetValue(matId, out var list))
                    trisByMat[matId] = list = new List<int>();
                list.Add(v1); list.Add(v2); list.Add(v3);
            }

            if (skippedOutOfRange > 0)
            {
                Debug.LogWarning($"[DffImporter] Skipped {skippedOutOfRange} triangle(s) referencing out-of-range " +
                                  $"vertices (mesh has {vertCount} vertices). See the geometry misalignment warning " +
                                  "above for the likely cause.");
            }

            // Map each used matId to a submesh slot, preserving material array indices so
            // mat[matId] still lines up — Unity meshes need contiguous submesh indices 0..N-1,
            // so submesh i uses material g.Materials[usedMatIds[i]] (handled by caller via the
            // same materialCount-sized array; unused slots simply get zero triangles, which is
            // fine — only zero-triangle submeshes produce the warning, not zero-triangle materials
            // sharing a used submesh index).
            int materialCount = Mathf.Max(1, g.Materials.Count);
            mesh.subMeshCount = materialCount;
            for (int sub = 0; sub < materialCount; sub++)
            {
                // A submesh with zero triangles (a material genuinely unused by any triangle)
                // will still trigger Unity's "invalid MinMaxAABB" console warning when bounds are
                // computed for it — that's cosmetic and doesn't affect the imported mesh.
                mesh.SetTriangles(trisByMat.TryGetValue(sub, out var list) ? list : new List<int>(), sub);
            }

            if (g.Normals == null) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material[] BuildMaterials(RwGeometry g, Dictionary<string, Texture2D> textures, Shader shader)
        {
            int count = Mathf.Max(1, g.Materials.Count);
            var mats = new Material[count];

            for (int i = 0; i < count; i++)
            {
                var mat = new Material(shader);
                if (i < g.Materials.Count)
                {
                    var src = g.Materials[i];
                    mat.color = src.Color;
                    mat.name = string.IsNullOrEmpty(src.TextureName) ? $"Material_{i}" : src.TextureName;

                    if (!string.IsNullOrEmpty(src.TextureName) &&
                        textures != null && textures.TryGetValue(src.TextureName, out var tex))
                    {
                        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                        else if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
                    }
                }
                else
                {
                    mat.name = $"Material_{i}";
                }
                mats[i] = mat;
            }
            return mats;
        }

        private static void AssignSkin(Mesh mesh, RwSkin skin, int boneCount)
        {
            int vertCount = mesh.vertexCount;
            var boneWeights = new BoneWeight[vertCount];

            for (int v = 0; v < vertCount; v++)
            {
                var bw = new BoneWeight();
                int baseIdx = v * 4;
                bw.boneIndex0 = skin.BoneIndices[baseIdx + 0];
                bw.boneIndex1 = skin.BoneIndices[baseIdx + 1];
                bw.boneIndex2 = skin.BoneIndices[baseIdx + 2];
                bw.boneIndex3 = skin.BoneIndices[baseIdx + 3];
                bw.weight0 = skin.BoneWeights[baseIdx + 0];
                bw.weight1 = skin.BoneWeights[baseIdx + 1];
                bw.weight2 = skin.BoneWeights[baseIdx + 2];
                bw.weight3 = skin.BoneWeights[baseIdx + 3];
                boneWeights[v] = bw;
            }
            mesh.boneWeights = boneWeights;

            var bindPoses = new Matrix4x4[boneCount];
            int missingMatrices = 0;
            for (int b = 0; b < boneCount; b++)
            {
                if (b < skin.InverseBoneMatrices.Length)
                {
                    bindPoses[b] = skin.InverseBoneMatrices[b];
                }
                else
                {
                    bindPoses[b] = Matrix4x4.identity;
                    missingMatrices++;
                }
            }
            if (missingMatrices > 0)
            {
                Debug.LogWarning($"[DffImporter] Skin declares {skin.InverseBoneMatrices.Length} inverse bone matrices " +
                                  $"but the model has {boneCount} bones. {missingMatrices} bone(s) will use identity " +
                                  "bind poses, which may cause incorrect deformation.");
            }
            mesh.bindposes = bindPoses;
        }
    }
}
