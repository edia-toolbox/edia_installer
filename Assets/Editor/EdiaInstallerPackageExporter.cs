#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Project-only helper: exports the installer's single script (<c>EdiaInstaller.cs</c>) as a
    /// <c>.unitypackage</c> into <c>/LatestRelease/</c>, overwriting the existing file. The exporter itself is
    /// not part of the package, so it never ships with the installer — its menu item only exists in this repo.
    /// </summary>
    public static class EdiaInstallerPackageExporter
    {
        private const string ScriptAssetPath   = "Assets/Editor/EdiaInstaller.cs";
        private const string OutputRelativePath = "LatestRelease/EdiaInstaller.unitypackage";

        [MenuItem("EDIA/Export Installer Package")]
        public static void ExportInstallerPackage()
        {
            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(ScriptAssetPath)))
            {
                Debug.LogError($"[EDIA Installer] Export aborted: '{ScriptAssetPath}' was not found in the project.");
                return;
            }

            // Application.dataPath is <project>/Assets; the package goes next to Assets, at the project root.
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputPath   = Path.Combine(projectRoot, OutputRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            // Default options export exactly this asset (plus its .meta, preserving the GUID) — no folder recursion
            // and no interactive dialog, so it silently overwrites the existing package.
            AssetDatabase.ExportPackage(ScriptAssetPath, outputPath, ExportPackageOptions.Default);

            Debug.Log($"[EDIA Installer] Exported {ScriptAssetPath} -> {OutputRelativePath}");
        }
    }
}
#endif
