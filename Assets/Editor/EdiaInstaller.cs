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
    public class EdiaInstaller : EditorWindow
    {
        // EDIA package IDs (as in their package.json)
        private const string PackageNameUxf = "com.edia.uxf";
        private const string PackageNameCore = "com.edia.core";
        private const string PackageNameLsl = "com.edia.lsl";
        private const string PackageNameEye = "com.edia.eye";

        // Unity XR packages
        private const string PackageNameXri = "com.unity.xr.interaction.toolkit";
        private const string PackageNameXrHands = "com.unity.xr.hands";

        // XR samples
        private const string XriSampleStarterAssets = "Starter Assets";
        private const string XriSampleHandsInteractionDemo = "Hands Interaction Demo";
        private const string XrHandsSampleHandVisualizer = "HandVisualizer";

        // EDIA Git base URLs (without version/branch part)
        private const string GitBaseUxf = "https://github.com/edia-toolbox/edia_uxf.git?path=/Assets/com.edia.uxf#";
        private const string GitBaseCore = "https://github.com/edia-toolbox/edia_core.git?path=/Assets/com.edia.core#";
        private const string GitBaseLsl = "https://github.com/edia-toolbox/edia_lsl.git?path=/Assets/com.edia.lsl#";
        private const string GitBaseEye = "https://github.com/edia-toolbox/edia_eye.git?path=/Assets/com.edia.eye#";

        // Package Manager requests (for EDIA queue)
        private static AddRequest _addRequest;
        private static ListRequest _listRequest;

        // Global state (EDIA queue only)
        private static bool _isInstallingEdia;
        private static string _statusMessage = "Idle";

        // UI toggles and versions (EDIA)
        private static bool _installCore;
        private static bool _installUxf;
        private static bool _installLsl;
        private static bool _installEye;
        private static string _uxfVersion = "main";
        private static string _coreVersion = "main";
        private static string _lslVersion = "main";
        private static string _eyeVersion = "main";
        private static string _uxfVersionInstalled;
        private static string _coreVersionInstalled;
        private static string _lslVersionInstalled;
        private static string _eyeVersionInstalled;
        
        const float NameWidth    = 80f;
        const float ToggleWidth  = 30f;
        const float LabelWidth   = 55f;
        const float FieldWidth   = 50f;
        const float IconWidth    = 90f;
        const float IconHeight   = 16f;
        const float VersionTextWidth = 70f;

        void DrawPackageRow(string displayName, string packageName, ref bool installFlag, ref string desiredVersion,
            ref string installedVersion, GUIContent installedIconMsg, GUIContent warnIconMsg) {
            EditorGUILayout.BeginHorizontal();

            GUILayout.Label(displayName, GUILayout.Width(NameWidth));
            installFlag = GUILayout.Toggle(installFlag, GUIContent.none, GUILayout.Width(ToggleWidth));

            GUILayout.Label("branch", GUILayout.Width(LabelWidth));
            desiredVersion = GUILayout.TextField(desiredVersion, GUILayout.Width(FieldWidth));

            if (IsPackageInstalled(packageName, out installedVersion))
            {
                GUILayout.Label(installedIconMsg, GUILayout.Width(IconWidth), GUILayout.Height(IconHeight));
                GUILayout.Label(installedVersion, GUILayout.Width(VersionTextWidth));
            }
            else
            {
                GUILayout.Label(warnIconMsg, GUILayout.Width(IconWidth), GUILayout.Height(IconHeight));
            }
            
            EditorGUILayout.EndHorizontal();
        }
        
        // Data structure for queued EDIA installs
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
            var window = GetWindow<EdiaInstaller>("EDIA Installer");
            window.minSize = new Vector2(500, 120);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("EDIA Package Installer", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // -------- SECTION 1: XR Dependencies --------
            DrawXrSection();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider); // separator line
            EditorGUILayout.Space();

            // -------- SECTION 2: EDIA Packages --------
            DrawEdiaSection();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider); // separator line
            EditorGUILayout.LabelField("Status:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_statusMessage);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider); // separator line
        }

        // ----- XR SECTION -----
        private void DrawXrSection()
        {
            EditorGUILayout.LabelField("1) XR Dependencies", EditorStyles.boldLabel);

            string version_xri = "";
            string version_xrhands = "";
            bool xriInstalled = IsPackageInstalled(PackageNameXri, out version_xri);
            bool xrHandsInstalled = IsPackageInstalled(PackageNameXrHands, out version_xrhands);
            bool xrReady = xriInstalled && xrHandsInstalled;

            GUIContent warnIconMsg = EditorGUIUtility.IconContent("console.warnicon");
            warnIconMsg.text = " Not Installed";
            GUIContent greenIconMsg = EditorGUIUtility.IconContent("TestPassed");
            greenIconMsg.text = " Installed";
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("XR Interaction Toolkit: ");
            if (xriInstalled) {
                EditorGUILayout.LabelField(greenIconMsg);
                EditorGUILayout.LabelField($"(v{version_xri})");
            } else {
                EditorGUILayout.LabelField(warnIconMsg);
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("XR Hands: ");
            if (xrHandsInstalled) {
                EditorGUILayout.LabelField(greenIconMsg);
                EditorGUILayout.LabelField($"(v{version_xrhands})");
            } else {
                EditorGUILayout.LabelField(warnIconMsg);
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space();

            EditorGUI.BeginDisabledGroup(_isInstallingEdia);
            if (GUILayout.Button("Install / Update XR Packages (XRI + XR Hands)", GUILayout.Height(24)))
            {
                InstallXrPackages();
            }
            EditorGUI.EndDisabledGroup();

            if (!xrReady)
            {
                EditorGUILayout.HelpBox(
                    "Install XR Interaction Toolkit and XR Hands first. " +
                    "You can then install EDIA packages and XR samples.",
                    MessageType.Info);
            }
            else {
                EditorGUILayout.LabelField("Required Samples:", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("[ XRI ] Starter Assets: ");
                if (!IsSampleInstalled(PackageNameXri, "Starter Assets"))
                    EditorGUILayout.LabelField(warnIconMsg);
                else {
                    EditorGUILayout.LabelField(greenIconMsg);
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("[ XRI ] Hands Interaction Demo: ");
                if (!IsSampleInstalled(PackageNameXri, "Hands Interaction Demo"))
                    EditorGUILayout.LabelField(warnIconMsg);
                else {
                    EditorGUILayout.LabelField(greenIconMsg);
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("[ XR Hands ] Hand Visualizer: ");
                if (!IsSampleInstalled(PackageNameXrHands, "HandVisualizer"))
                    EditorGUILayout.LabelField(warnIconMsg);
                else {
                    EditorGUILayout.LabelField(greenIconMsg);
                }
                EditorGUILayout.EndHorizontal();
            }
            
            EditorGUI.BeginDisabledGroup(_isInstallingEdia);
            if (GUILayout.Button("Install required Samples (XRI + XR Hands)", GUILayout.Height(24)))
            {
                InstallSamples();
            }
            EditorGUI.EndDisabledGroup();
        }

        private static bool IsPackageInstalled(string packageName)
        {
            // Uses PackageInfo to check synchronously if the package exists
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + packageName);
            return info != null;
        }
        
        private static bool IsPackageInstalled(string packageName, out string version)
        {
            // Uses PackageInfo to check synchronously if the package exists
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + packageName);
            if (info != null) {
                version = info.version;
                return true;
            }
            version = null;
            return false;
        }

        private static bool IsSampleInstalled(string packageName, string sampleName) {
            if (!IsPackageInstalled(packageName)) return false;
            
            var samples = Sample.FindByPackage(packageName, null); // use current installed version

            if (samples == null || !samples.Any()) {
                return false;
            }

            foreach (var sample in samples) {
                if (!sample.displayName.Contains(sampleName))
                    continue;

                return sample.isImported;
            }
            return false;
        }
        

        private void InstallXrPackages()
        {
            bool needXri = !IsPackageInstalled(PackageNameXri);
            bool needHands = !IsPackageInstalled(PackageNameXrHands);

            if (!needXri && !needHands)
            {
                _statusMessage = "XR packages are already installed.";
                Repaint();
                return;
            }

            if (needXri)
            {
                Debug.Log("[EDIA Installer] Requesting install of XR Interaction Toolkit...");
                Client.Add(PackageNameXri); // async; we don't track completion in code
            }

            if (needHands)
            {
                Debug.Log("[EDIA Installer] Requesting install of XR Hands...");
                Client.Add(PackageNameXrHands);
            }

            _statusMessage = "Requested XR packages via Package Manager. Unity may reload while importing.";
            Repaint();
        }


        private void InstallSamples() {
            TryImportSampleByName(
                PackageNameXrHands,
                XrHandsSampleHandVisualizer,
                "XR Hands Hand Visualizer"
            );
                    
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
        }
        
        // ----- EDIA SECTION -----
        private void DrawEdiaSection() {

            GUIContent warnIconMsg = EditorGUIUtility.IconContent("Toolbar Minus");
            warnIconMsg.text = "Not Installed";
            GUIContent installedIconMsg = EditorGUIUtility.IconContent("TestPassed");

            EditorGUILayout.LabelField("2) Install EDIA Packages", EditorStyles.boldLabel);

            bool xrReady = IsPackageInstalled(PackageNameXri) && IsPackageInstalled(PackageNameXrHands);

            if (!xrReady) {
                EditorGUILayout.HelpBox(
                    "XR Interaction Toolkit and XR Hands must be installed before installing EDIA packages.",
                    MessageType.Warning);
            }

            // Ensure we have defaults (avoid resetting every frame)
            if (string.IsNullOrEmpty(_uxfVersion)) _uxfVersion = "main";
            if (string.IsNullOrEmpty(_coreVersion)) _coreVersion = "main";
            if (string.IsNullOrEmpty(_lslVersion)) _lslVersion = "main";
            if (string.IsNullOrEmpty(_eyeVersion)) _eyeVersion = "main";

            EditorGUI.BeginDisabledGroup(_isInstallingEdia || !xrReady);
            
            DrawPackageRow(
                "EDIA UXF",
                PackageNameUxf,
                ref _installUxf,
                ref _uxfVersion,
                ref _uxfVersionInstalled,
                installedIconMsg,
                warnIconMsg);

            DrawPackageRow(
                "EDIA Core",
                PackageNameCore,
                ref _installCore,
                ref _coreVersion,
                ref _coreVersionInstalled,
                installedIconMsg,
                warnIconMsg);

            DrawPackageRow(
                "EDIA LSL",
                PackageNameLsl,
                ref _installLsl,
                ref _lslVersion,
                ref _lslVersionInstalled,
                installedIconMsg,
                warnIconMsg);

            DrawPackageRow(
                "EDIA Eye",
                PackageNameEye,
                ref _installEye,
                ref _eyeVersion,
                ref _eyeVersionInstalled,
                installedIconMsg,
                warnIconMsg);
            

            // Dependency rules inside EDIA:
            if (_installCore) _installUxf = true;
            if (_installLsl) _installCore = true;
            if (_installEye) _installCore = true;

            EditorGUILayout.Space();

            if (GUILayout.Button("Install EDIA Packages", GUILayout.Height(30)))
            {
                StartEdiaInstalls();
            }

            EditorGUI.EndDisabledGroup();
        }

        // Entry point when EDIA button is pressed
        private void StartEdiaInstalls()
        {
            if (_isInstallingEdia)
            {
                _statusMessage = "Already installing EDIA packages. Please wait.";
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
                string url = GitBaseCore + _coreVersion;
                _installQueue.Enqueue(new PackageToInstall(
                    PackageNameCore,
                    url,
                    $"EDIA Core ({_coreVersion})"
                ));
            }

            if (_installLsl)
            {
                string url = GitBaseLsl + _lslVersion;
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

            _statusMessage = "Checking already installed EDIA packages...";
            _isInstallingEdia = true;

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
                _isInstallingEdia = false;
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
                _statusMessage = "All selected EDIA packages are already installed.";
                _isInstallingEdia = false;
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
                _statusMessage = "All EDIA installations completed.";
                _isInstallingEdia = false;
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
                _isInstallingEdia = false;
            }

            GetWindowIfOpen()?.Repaint();
        }

        private static void PackageProgress()
        {
            if (_addRequest == null)
            {
                EditorApplication.update -= PackageProgress;
                _isInstallingEdia = false;
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
                var samples = Sample.FindByPackage(packageName, null); // use current installed version

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
        private static EdiaInstaller GetWindowIfOpen()
        {
            return Resources.FindObjectsOfTypeAll<EdiaInstaller>().FirstOrDefault();
        }
    }
}
#endif
