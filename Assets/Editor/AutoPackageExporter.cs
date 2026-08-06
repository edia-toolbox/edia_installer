using System.IO;
using UnityEditor;
using UnityEngine;

namespace Edia.Installer {

    [InitializeOnLoad]
    
    
    public sealed class AutoPackageExporter : AssetPostprocessor {

        private const string TargetScriptPath = "Assets/Editor/EdiaInstaller.cs";
        private const string OutputPackagePath = "Packages/EdiaInstaller.unitypackage";

        private const string ExportPendingKey = "Edia.AutoPackageExporter.ExportPending";
        

        static AutoPackageExporter() {
            EditorApplication.update -= TryExport;
            EditorApplication.update += TryExport;
        }
        
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        ) {
            foreach (string importedAsset in importedAssets) {
                if (importedAsset != TargetScriptPath)
                    continue;

                SessionState.SetBool(ExportPendingKey, true);
                
                return;
            }
        }


        private static void TryExport() {
            if (!SessionState.GetBool(ExportPendingKey, false))
                return;

            // Do not export while Unity is still compiling/importing.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) {
                return;
            }

            SessionState.SetBool(ExportPendingKey, false);
            ExportPackage();
        }

        private static void ExportPackage() {
            if (!File.Exists(TargetScriptPath)) {
                Debug.LogError($"Cannot export package. Target script does not exist: {TargetScriptPath}");
                return;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outputPath = Path.Combine(projectRoot, OutputPackagePath);

            string outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
                Directory.CreateDirectory(outputDirectory);

            AssetDatabase.ExportPackage(
                new[] { TargetScriptPath },
                outputPath,
                ExportPackageOptions.Default
            );

            Debug.Log($"Exported unitypackage: {outputPath}");
        }
    }
}