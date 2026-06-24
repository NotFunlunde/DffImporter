using System.Collections.Generic;
using System.IO;
using UnityEditor;
#if UNITY_2020_2_OR_NEWER
using UnityEditor.AssetImporters;
#else
using UnityEditor.Experimental.AssetImporters;
#endif
using UnityEngine;

namespace BfbbImport.Editor
{
    /// <summary>
    /// Makes .dff files (Battle for Bikini Bottom / RenderWare 3.x models) importable as native
    /// Unity assets. Drop a .dff into the Project window and it becomes a prefab-like asset with
    /// its mesh(es), materials, and (if present) skinned bone hierarchy.
    ///
    /// Texture lookup: if a same-named .txd file sits next to the .dff (e.g. SpongeBob.dff +
    /// SpongeBob.txd), it's parsed automatically and its rasters matched to materials by name.
    /// You can also drop loose .txd files anywhere in the project; on next reimport of a .dff,
    /// any .txd in the same folder is checked.
    /// </summary>
    [ScriptedImporter(1, "dff")]
    public class DffImporter : ScriptedImporter
    {
        [Tooltip("Shader used for generated materials. Defaults to URP/Lit if available, else Standard.")]
        public Shader shaderOverride;

        [Tooltip("Uniform scale applied to the imported model (BfBB models often import very large/small).")]
        public float importScale = 1f;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            Debug.Log($"[DffImporter] OnImportAsset running for '{ctx.assetPath}'");

            RwClump clump;
            try
            {
                using var fs = File.OpenRead(ctx.assetPath);
                clump = DffParser.Parse(fs);
            }
            catch (System.Exception ex)
            {
                ctx.LogImportError($"Failed to parse DFF '{ctx.assetPath}': {ex.Message}");
                return;
            }

            var textures = LoadAdjacentTextures(ctx.assetPath, ctx);

            Shader shader = shaderOverride;
            if (shader == null)
            {
                string[] candidates =
                {
                    "Universal Render Pipeline/Lit",
                    "HDRP/Lit",
                    "Standard",
                    "Legacy Shaders/Diffuse",
                    "Mobile/Diffuse",
                    "Sprites/Default", // ships in every Unity project, used only as a last-resort fallback
                };

                foreach (var candidateName in candidates)
                {
                    shader = Shader.Find(candidateName);
                    if (shader != null) break;
                }
            }

            if (shader == null)
            {
                ctx.LogImportError("No suitable shader found, even after checking URP/HDRP/Standard/legacy/Sprites fallbacks. " +
                                    "This usually means the relevant shaders aren't included in your project (e.g. URP/HDRP " +
                                    "package not installed, or Standard stripped from build settings). Assign 'shaderOverride' " +
                                    "on the DffImporter asset in the Inspector to any shader you have available, then reimport.");
                return;
            }

            string rootName = Path.GetFileNameWithoutExtension(ctx.assetPath);
            GameObject root = ClumpBuilder.Build(clump, rootName, textures, shader);
            root.transform.localScale = Vector3.one * importScale;

            // Register the root and every sub-asset (meshes, materials) with the import context
            // so Unity tracks/serializes them as part of this asset.
            ctx.AddObjectToAsset("root", root);
            ctx.SetMainObject(root);

            RegisterSubAssets(ctx, root.transform);
        }

        private static void RegisterSubAssets(AssetImportContext ctx, Transform t)
        {
            var seenMeshes = new HashSet<Mesh>();
            var seenMats = new HashSet<Material>();
            RegisterRecursive(ctx, t, seenMeshes, seenMats, isRoot: true);
        }

        private static void RegisterRecursive(AssetImportContext ctx, Transform t, HashSet<Mesh> seenMeshes, HashSet<Material> seenMats, bool isRoot)
        {
            // Every GameObject in the hierarchy must be explicitly registered with the import
            // context, or Unity discards it when serializing the asset — the root alone is not
            // enough. (The root itself was already added by the caller before this runs.)
            if (!isRoot)
                ctx.AddObjectToAsset(t.gameObject.name + "_" + t.GetInstanceID(), t.gameObject);
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null && seenMeshes.Add(mf.sharedMesh))
                ctx.AddObjectToAsset(mf.sharedMesh.name + "_" + mf.sharedMesh.GetInstanceID(), mf.sharedMesh);

            var smr = t.GetComponent<SkinnedMeshRenderer>();
            if (smr != null && smr.sharedMesh != null && seenMeshes.Add(smr.sharedMesh))
                ctx.AddObjectToAsset(smr.sharedMesh.name + "_" + smr.sharedMesh.GetInstanceID(), smr.sharedMesh);

            var renderer = t.GetComponent<Renderer>();
            if (renderer != null)
            {
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat != null && seenMats.Add(mat))
                        ctx.AddObjectToAsset(mat.name + "_" + mat.GetInstanceID(), mat);
                }
            }

            for (int i = 0; i < t.childCount; i++)
                RegisterRecursive(ctx, t.GetChild(i), seenMeshes, seenMats, isRoot: false);
        }

        private static Dictionary<string, Texture2D> LoadAdjacentTextures(string dffAssetPath, AssetImportContext ctx)
        {
            var combined = new Dictionary<string, Texture2D>(System.StringComparer.OrdinalIgnoreCase);
            string dir = Path.GetDirectoryName(dffAssetPath);
            string baseName = Path.GetFileNameWithoutExtension(dffAssetPath);

            // Prefer a same-named .txd, but also pick up any other .txd in the same folder
            // (BfBB sometimes shares one dictionary across several models in a level).
            var candidates = new List<string>();
            string sameNamed = Path.Combine(dir, baseName + ".txd").Replace('\\', '/');
            if (File.Exists(sameNamed)) candidates.Add(sameNamed);

            if (Directory.Exists(dir))
            {
                foreach (var f in Directory.GetFiles(dir, "*.txd"))
                {
                    string norm = f.Replace('\\', '/');
                    if (!candidates.Contains(norm)) candidates.Add(norm);
                }
            }

            foreach (var txdPath in candidates)
            {
                try
                {
                    using var fs = File.OpenRead(txdPath);
                    var dict = TxdParser.Parse(fs);
                    foreach (var kv in dict)
                        combined[kv.Key] = kv.Value; // last-loaded .txd wins on name collisions
                    ctx.DependsOnSourceAsset(txdPath);
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[DffImporter] Failed to parse adjacent TXD '{txdPath}': {ex.Message}");
                }
            }

            return combined;
        }
    }
}
