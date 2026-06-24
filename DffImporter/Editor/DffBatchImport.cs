using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BfbbImport.Editor
{
    /// <summary>
    /// Two batch workflows for .dff models:
    ///   1. Right-click a folder already inside the project → "BfBB DFF/Reimport All .dff In Folder"
    ///      — force-reimports every .dff under that folder (recursive). Useful after changing
    ///      importer settings/code, since Unity won't always notice on its own.
    ///   2. Menu bar → "Tools/BfBB DFF/Batch Import External Folder..."
    ///      — picks a folder OUTSIDE the project (e.g. extracted game files), copies every
    ///      .dff/.txd pair into a chosen location under Assets preserving subfolder structure,
    ///      then imports all of them in one pass.
    /// </summary>
    public static class DffBatchImport
    {
        // --- Workflow 1: reimport existing in-project .dff files ---------------------------

        [MenuItem("Assets/BfBB DFF/Reimport All .dff In Folder", false, 20)]
        private static void ReimportFolder()
        {
            var folders = Selection.objects
                .Select(AssetDatabase.GetAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && AssetDatabase.IsValidFolder(p))
                .ToArray();

            if (folders.Length == 0)
            {
                EditorUtility.DisplayDialog("BfBB DFF Batch Import", "Select one or more folders in the Project window first.", "OK");
                return;
            }

            var dffPaths = AssetDatabase.FindAssets("", folders)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => p.EndsWith(".dff", System.StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToArray();

            if (dffPaths.Length == 0)
            {
                EditorUtility.DisplayDialog("BfBB DFF Batch Import", "No .dff files found under the selected folder(s).", "OK");
                return;
            }

            int done = 0;
            foreach (var path in dffPaths)
            {
                EditorUtility.DisplayProgressBar("Reimporting .dff files", path, (float)done / dffPaths.Length);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                done++;
            }
            EditorUtility.ClearProgressBar();

            Debug.Log($"[DffImporter] Reimported {done} .dff file(s).");
        }

        [MenuItem("Assets/BfBB DFF/Reimport All .dff In Folder", true)]
        private static bool ReimportFolderValidate()
        {
            return Selection.objects.Any(o => AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(o)));
        }

        // --- Workflow 2: import an external folder of .dff/.txd into the project -----------

        [MenuItem("Tools/BfBB DFF/Batch Import External Folder...")]
        private static void BatchImportExternalFolder()
        {
            string sourceFolder = EditorUtility.OpenFolderPanel(
                "Select folder containing .dff / .txd files", "", "");
            if (string.IsNullOrEmpty(sourceFolder)) return;

            string destFolder = EditorUtility.SaveFolderPanel(
                "Choose destination inside your project's Assets folder", Application.dataPath, "ImportedModels");
            if (string.IsNullOrEmpty(destFolder)) return;

            if (!PathUtils.Normalize(destFolder).StartsWith(PathUtils.Normalize(Application.dataPath)))
            {
                EditorUtility.DisplayDialog("BfBB DFF Batch Import",
                    "The destination must be inside this project's Assets folder.", "OK");
                return;
            }

            var sourceFiles = Directory.GetFiles(sourceFolder, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".dff", System.StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".txd", System.StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (sourceFiles.Length == 0)
            {
                EditorUtility.DisplayDialog("BfBB DFF Batch Import", "No .dff or .txd files found in that folder.", "OK");
                return;
            }

            int copied = 0;
            foreach (var srcFile in sourceFiles)
            {
                string relative = Path.GetRelativePath(sourceFolder, srcFile);
                string destFile = Path.Combine(destFolder, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destFile));

                EditorUtility.DisplayProgressBar("Copying files into project", relative, (float)copied / sourceFiles.Length);
                File.Copy(srcFile, destFile, overwrite: true);
                copied++;
            }
            EditorUtility.ClearProgressBar();

            AssetDatabase.Refresh(); // triggers import for every newly-copied .dff via DffImporter

            Debug.Log($"[DffImporter] Copied {copied} file(s) (.dff/.txd) into " +
                      $"'{PathUtils.ToAssetsRelative(destFolder)}' and triggered import.");
        }
    }
}
