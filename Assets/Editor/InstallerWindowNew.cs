#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.PackageManager.UI; // for Sample API
using UnityEngine;

namespace Editor
{
    public class InstallerWindow : EditorWindow
    {
        // Package IDs as they appear in their package.json files
        private const string PackageNameUxf = "com.edia.uxf";
        private const string PackageNameCore = "com.edia.core";
        private const string PackageNameLsl = "com.edia.lsl";
        private const string PackageNameEye = "com.edia.eye";

        // Unity packages for samples
        private const string PackageNameXri = "com.unity.xr.interaction.toolkit";
        private const string PackageNameXrHands = "com.unity.xr.hands";

        // Sample names (as seen in Package Manager → Samples)
        private const string XriSampleStarterAssets = "Starter Assets";
        private const string XriSampleHandsInteractionDemo = "Hands Interaction Demo";
        private const string XrHandsSampleHandVisualizer = "HandVisualizer";

        // Base Git URLs (without version/branch part)
        private const string GitBaseUxf = "https://github.com/edia-toolbox/edia_uxf.git?path=/Assets/com.edia.uxf#";
        private const string GitBaseCore = "https://github.com/edia-toolbox/edia_core.git?path=/Assets/com.edia.core#";
        private const string GitBaseLsl = "https://github.com/edia-toolbox/edia_lsl.git?path=/Assets/com.edia.lsl#";
        private const string GitBaseEye = "https://github.com/edia-toolbox/edia_eye.git?path=/Assets/com.edia.eye#";

        // Package Manager requests
        private static AddRequest _addRequest;
        private static ListRequest _listRequest;

        // Global state
        private static bool _isInstalling;
        private static string _statusMessage = "Idle";

        // UI toggles and versions
        private static bool _installCore;
        private static bool _installUxf;
        private static bool _installLsl;
        private static bool _installEye;
        private static string _uxfVersion = "main";
        private static string _coreVersion = "main";
        private static string _lslVersion = "main";
        private static string _eyeVersion = "main";

        // Data structure for queued installs
        private struct PackageToInstall
        {
            public string PackageName;
            public string GitUrl;
            public string DisplayName;

            public PackageToInstall(string packageName, string gitUrl, string displayName)
            {
                PackageName = packageName;
                GitUrl = gitUrl;
                DisplayName = displayName;
            }
        }

        private static Queue<PackageToInstall> _installQueue = new Queue<PackageToInstall>();
        private static PackageToInstall _currentPackage;

        [MenuItem("Tools/EDIA Installer")]
        public static void ShowWindow()
        {
            var window = GetWindow<InstallerWindow>("EDIA Installer");
            window.minSize = new Vector2(500, 60);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("EDIA Package Installer", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUI.BeginDisabledGroup(_isInstalling);

            // Ensure we have defaults (avoid resetting every frame)
            if (string.IsNullOrEmpty(_uxfVersion)) _uxfVersion = "main";
            if (string.IsNullOrEmpty(_coreVersion)) _coreVersion = "main";
            if (string.IsNullOrEmpty(_lslVersion)) _lslVersion = "main";
            if (string.IsNullOrEmpty(_eyeVersion)) _eyeVersion = "main";

            // UXF row
            EditorGUILayout.BeginHorizontal();
            _installUxf = EditorGUILayout.Toggle("EDIA UXF", _installUxf);
            _uxfVersion = EditorGUILayout.TextField("version", _uxfVersion);
            EditorGUILayout.EndHorizontal();

            // Core row
            EditorGUILayout.BeginHorizontal();
            _installCore = EditorGUILayout.Toggle("EDIA Core", _installCore);
            _coreVersion = EditorGUILayout.TextField("version", _coreVersion);
            EditorGUILayout.EndHorizontal();

            // LSL row
            EditorGUILayout.BeginHorizontal();
            _installLsl = EditorGUILayout.Toggle("EDIA LSL", _installLsl);
            _lslVersion = EditorGUILayout.TextField("version", _lslVersion);
            EditorGUILayout.EndHorizontal();

            // Eye row
            EditorGUILayout.BeginHorizontal();
            _installEye = EditorGUILayout.Toggle("EDIA Eye", _installEye);
            _eyeVersion = EditorGUILayout.TextField("version", _eyeVersion);
            EditorGUILayout.EndHorizontal();

            // Dependency rules
            if (_installCore) _installUxf = true;
            if (_installLsl) _installCore = true;
            if (_installEye) _installCore = true;

            EditorGUILayout.Space();

            if (GUILayout.Button("Install Packages", GUILayout.Height(30)))
            {
                StartInstalls();
            }

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status: " + _statusMessage);
        }

        // Entry point when the button is pressed
        private void StartInstalls()
        {
            if (_isInstalling)
            {
                _statusMessage = "Already installing. Please wait.";
                Repaint();
                return;
            }

            // Build a fresh queue based on user choices, in dependency order
            _installQueue.Clear();

            if (_installUxf)
            {
                string url = GitBaseUxf + _uxfVersion;
                _installQueue.Enqueue(new PackageToInstall(
                    PackageNameUxf,
                    url,
                    $"EDIA UXF ({_uxfVersion})"
                ));
            }

            if (_installCore)
            {
                // XR Interaction Toolkit
                _installQueue.Enqueue(new PackageToInstall(
                    PackageNameXri,
                    PackageNameXri,               // Client.Add(name) works for registry packages
                    "XR Interaction Toolkit"
                ));

                // XR Hands
                _installQueue.Enqueue(new PackageToInstall(
                    PackageNameXrHands,
                    PackageNameXrHands,
                    "XR Hands"
                ));
                
                string url = GitBaseCore + _coreVersion;
                _installQueue.Enqueue(new PackageToInstall(
                    PackageNameCore,
                    url,
                    $"EDIA Core ({_coreVersion})"
                ));
            }

            if (_installLsl)
            {
                string url = GitBaseLsl + _lslVersion; // fixed: use _lslVersion
                _installQueue.Enqueue(new PackageToInstall(
                    PackageNameLsl,
                    url,
                    $"EDIA LSL ({_lslVersion})"
                ));
            }

            if (_installEye)
            {
                string url = GitBaseEye + _eyeVersion;
                _installQueue.Enqueue(new PackageToInstall(
                    PackageNameEye,
                    url,
                    $"EDIA Eye ({_eyeVersion})"
                ));
            }

            if (_installQueue.Count == 0)
            {
                _statusMessage = "Nothing selected to install.";
                Repaint();
                return;
            }

            _statusMessage = "Checking already installed packages...";
            _isInstalling = true;

            // Ask Package Manager for the list of installed packages
            _listRequest = Client.List(true);
            EditorApplication.update += OnListProgress;

            Repaint();
        }

        // Handle result of Client.List: filter out already installed packages
        private static void OnListProgress()
        {
            if (!_listRequest.IsCompleted)
                return;

            EditorApplication.update -= OnListProgress;

            if (_listRequest.Status != StatusCode.Success)
            {
                Debug.LogError("[EDIA Installer] Failed to list packages: " + _listRequest.Error);
                _isInstalling = false;
                _statusMessage = "Failed to list packages. See Console.";
                GetWindowIfOpen()?.Repaint();
                return;
            }

            var installedNames = new HashSet<string>(_listRequest.Result.Select(p => p.name));
            var remaining = new Queue<PackageToInstall>();

            foreach (var pkg in _installQueue)
            {
                if (installedNames.Contains(pkg.PackageName))
                {
                    Debug.Log($"[EDIA Installer] {pkg.DisplayName} already installed ({pkg.PackageName}), skipping.");
                }
                else
                {
                    remaining.Enqueue(pkg);
                }
            }

            _installQueue = remaining;

            if (_installQueue.Count == 0)
            {
                _statusMessage = "All selected packages are already installed.";
                _isInstalling = false;
                GetWindowIfOpen()?.Repaint();
                return;
            }

            // Start installing the first one
            StartNextInstall();
        }

        private static void StartNextInstall()
        {
            if (_installQueue.Count == 0)
            {
                _statusMessage = "All installations completed.";
                _isInstalling = false;
                GetWindowIfOpen()?.Repaint();
                return;
            }

            _currentPackage = _installQueue.Dequeue();

            _statusMessage = $"Installing: {_currentPackage.DisplayName}...";
            Debug.Log($"[EDIA Installer] Installing {_currentPackage.DisplayName} from {_currentPackage.GitUrl}");

            try
            {
                _addRequest = Client.Add(_currentPackage.GitUrl);
                EditorApplication.update += PackageProgress;
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[EDIA Installer] Exception while starting install:\n" + ex);
                _statusMessage = "Error starting install. See Console.";
                _isInstalling = false;
            }

            GetWindowIfOpen()?.Repaint();
        }

        private static void PackageProgress()
        {
            if (_addRequest == null)
            {
                EditorApplication.update -= PackageProgress;
                _isInstalling = false;
                _statusMessage = "No active request.";
                GetWindowIfOpen()?.Repaint();
                return;
            }

            if (!_addRequest.IsCompleted)
                return;

            EditorApplication.update -= PackageProgress;

            if (_addRequest.Status == StatusCode.Success)
            {
                Debug.Log("[EDIA Installer] Package installed: " + _addRequest.Result.packageId);
                _statusMessage = $"Install succeeded: {_currentPackage.DisplayName}";

                // When Core finishes, import XRI + XR Hands samples
                if (_currentPackage.PackageName == PackageNameCore)
                {
                    TryImportSampleByName(
                        PackageNameXri,
                        XriSampleStarterAssets,
                        "XRI Starter Assets"
                    );

                    TryImportSampleByName(
                        PackageNameXri,
                        XriSampleHandsInteractionDemo,
                        "XRI Hands Interaction Demo"
                    );

                    TryImportSampleByName(
                        PackageNameXrHands,
                        XrHandsSampleHandVisualizer,
                        "XR Hands Hand Visualizer"
                    );
                }
            }
            else
            {
                Debug.LogError("[EDIA Installer] Install failed: " + _addRequest.Error);
                _statusMessage = $"Install FAILED: {_currentPackage.DisplayName}. See Console.";
            }

            _addRequest = null;

            // Continue with next in queue, if any
            StartNextInstall();
        }

        /// <summary>
        /// Try to import a sample whose displayName contains the given text.
        /// </summary>
        private static void TryImportSampleByName(string packageName, string sampleNameFragment, string friendlyLabel)
        {
            try
            {
                var samples = Sample.FindByPackage(packageName, null); // null = current installed version

                if (samples == null || !samples.Any())
                {
                    Debug.LogWarning($"[EDIA Installer] No samples found for package '{packageName}'. " +
                                     $"Cannot import {friendlyLabel}.");
                    return;
                }

                foreach (var sample in samples)
                {
                    if (!sample.displayName.Contains(sampleNameFragment))
                        continue;

                    if (sample.isImported)
                    {
                        Debug.Log($"[EDIA Installer] {friendlyLabel} sample already imported.");
                    }
                    else
                    {
                        Debug.Log($"[EDIA Installer] Importing {friendlyLabel} sample...");
                        sample.Import(Sample.ImportOptions.None);
                    }

                    return;
                }

                Debug.LogWarning($"[EDIA Installer] Could not find sample '{sampleNameFragment}' in package '{packageName}'.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[EDIA Installer] Failed to import sample '{friendlyLabel}' from '{packageName}': {ex}");
            }
        }

        // Helper to repaint from static methods
        private static InstallerWindow GetWindowIfOpen()
        {
            return Resources.FindObjectsOfTypeAll<InstallerWindow>().FirstOrDefault();
        }
    }
}
#endif
