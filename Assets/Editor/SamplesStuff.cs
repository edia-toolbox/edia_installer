using UnityEditor;
using UnityEditor.PackageManager.UI;
using System.Linq;
using UnityEngine;

namespace Editor {
    public class SampleImporter : EditorWindow {
        // Change this to the package you want to import the sample from.
        private string packageName = "com.unity.timeline";
        private bool showSamples = true;

        [MenuItem("Example/Import Sample")]
        public static void ShowWindow() {
            GetWindow<SampleImporter>("Sample Importer");
        }

        private void OnGUI() {
            GUILayout.Label("Import Sample from Package", EditorStyles.boldLabel);
            packageName = EditorGUILayout.TextField("Package Name", packageName);
            // This part adds an action function to the button. 
            if (GUILayout.Button("Show Samples")) {
                showSamples = true;
            }

            if (showSamples)
                ShowSamples(packageName);
        }

        private void ShowSamples(string packageName) {
            // Setting the version to null automatically takes the recommended or latest package version.
            // You can optionally import a specific version of the package by replacing "null" by the specific version of that package.
            // Get all samples from the package.
            var samples = Sample.FindByPackage(packageName, null);
            // If nothing is returned, then this package doesn't contain samples.
            if (samples == null || !samples.Any()) {
                GUILayout.Label($"No samples found for package: {packageName}", EditorStyles.boldLabel);
                return;
            }

            foreach (var sample in samples) {
                GUILayout.Label($"Found sample: {sample.displayName}");
                // Give the option to import the sample if it isn't in the project.
                // If the sample is already in the project, only show a message in the Example window.
                if (!sample.isImported) {
                    GUILayout.Label($"Sample '{sample.displayName}' is not imported.");
                }
                else {
                    GUILayout.Label($"Sample '{sample.displayName}' is imported.");
                }
            }
        }
    }
}