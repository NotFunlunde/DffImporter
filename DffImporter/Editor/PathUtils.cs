using UnityEngine;

namespace BfbbImport.Editor
{
    /// <summary>
    /// Shared path helpers for editor scripts dealing with Unity asset paths.
    /// </summary>
    public static class PathUtils
    {
        /// <summary>Normalizes a file path to use forward slashes (Unity convention).</summary>
        public static string Normalize(string path) => path.Replace('\\', '/');

        /// <summary>Converts an absolute path inside the project's Assets folder to an
        /// "Assets/..." relative path. Returns the normalized input if it's outside Assets.</summary>
        public static string ToAssetsRelative(string absolutePath)
        {
            string dataPath = Normalize(Application.dataPath);
            string norm = Normalize(absolutePath);
            return norm.StartsWith(dataPath) ? "Assets" + norm.Substring(dataPath.Length) : norm;
        }
    }
}
