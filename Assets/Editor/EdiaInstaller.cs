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
        // Unity XR packages
        private const string PackageNameXri = "com.unity.xr.interaction.toolkit";
        private const string PackageNameXrHands = "com.unity.xr.hands";

        // XR samples
        private const string XriSampleStarterAssets = "Starter Assets";
        private const string XriSampleHandsInteractionDemo = "Hands Interaction Demo";
        private const string XrHandsSampleHandVisualizer = "HandVisualizer";

        /// <summary>
        /// Describes one installable EDIA module. The <see cref="Requires"/> keys must refer
        /// to modules listed earlier in <see cref="_ediaPackages"/>, so a single forward pass
        /// over that list is enough both to propagate "required" toggles and to install in
        /// dependency order.
        /// </summary>
        private class PackageDef
        {
            public readonly string Key;
            public readonly string DisplayName;
            public readonly string PackageName; // as in package.json "name"
            public readonly string RepoName;    // edia-toolbox GitHub repo name
            public readonly string[] Requires;  // Keys of modules that must be installed alongside this one
            public readonly int Indent;         // 0 = top-level module, 1 = headset-specific sub-module

            public bool Install;
            public string Version = "main";
            public string InstalledVersion;

            public PackageDef(string key, string displayName, string packageName, string repoName, int indent = 0, params string[] requires)
            {
                Key = key;
                DisplayName = displayName;
                PackageName = packageName;
                RepoName = repoName;
                Indent = indent;
                Requires = requires ?? System.Array.Empty<string>();
            }

            public string GitUrl => $"https://github.com/edia-toolbox/{RepoName}.git?path=/Assets/{PackageName}#{Version}";
        }

        // All EDIA modules currently shipped from this workspace, in dependency order
        // (a module's Requires always point to entries earlier in this list).
        private static readonly List<PackageDef> _ediaPackages = new List<PackageDef>
        {
            new PackageDef("core", "EDIA Core", "com.edia.core", "edia_core"),
            new PackageDef("lsl", "EDIA LSL", "com.edia.lsl", "edia_lsl", requires: new[] { "core" }),
            new PackageDef("rcas", "EDIA Rcas", "com.edia.rcas", "edia_rcas", requires: new[] { "core" }),
            new PackageDef("survey", "EDIA Survey", "com.edia.survey", "edia_survey", requires: new[] { "core" }),
            new PackageDef("eye", "EDIA Eye", "com.edia.eye", "edia_eye", requires: new[] { "core" }),
            new PackageDef("eye.pico", "  - PICO", "com.edia.eye.pico", "edia_eye_pico", indent: 1, requires: new[] { "eye" }),
            new PackageDef("eye.quest", "  - Quest", "com.edia.eye.quest", "edia_eye_quest", indent: 1, requires: new[] { "eye" }),
            new PackageDef("eye.varjo", "  - Varjo", "com.edia.eye.varjo", "edia_eye_varjo", indent: 1, requires: new[] { "eye" }),
            new PackageDef("eye.vive", "  - Vive", "com.edia.eye.vive", "edia_eye_vive", indent: 1, requires: new[] { "eye" }),
        };

        // Package Manager requests (for EDIA queue)
        private static AddRequest _addRequest;
        private static ListRequest _listRequest;

        // Global state (EDIA queue only)
        private static bool _isInstallingEdia;
        private static string _statusMessage = "Idle";

        const float NameWidth    = 130f;
        const float ToggleWidth  = 30f;
        const float LabelWidth   = 55f;
        const float FieldWidth   = 50f;
        const float IconWidth    = 90f;
        const float IconHeight   = 16f;
        const float VersionTextWidth = 70f;

        void DrawPackageRow(PackageDef pkg, GUIContent installedIconMsg, GUIContent warnIconMsg)
        {
            EditorGUILayout.BeginHorizontal();

            GUILayout.Label(pkg.DisplayName, GUILayout.Width(NameWidth));

            // Modules required by another selected module are shown as forced-on and locked.
            EditorGUI.BeginDisabledGroup(IsForcedOn(pkg));
            pkg.Install = GUILayout.Toggle(pkg.Install, GUIContent.none, GUILayout.Width(ToggleWidth));
            EditorGUI.EndDisabledGroup();

            GUILayout.Label("branch", GUILayout.Width(LabelWidth));
            pkg.Version = GUILayout.TextField(pkg.Version, GUILayout.Width(FieldWidth));

            if (IsPackageInstalled(pkg.PackageName, out pkg.InstalledVersion))
            {
                GUILayout.Label(installedIconMsg, GUILayout.Width(IconWidth), GUILayout.Height(IconHeight));
                GUILayout.Label(pkg.InstalledVersion, GUILayout.Width(VersionTextWidth));
            }
            else
            {
                GUILayout.Label(warnIconMsg, GUILayout.Width(IconWidth), GUILayout.Height(IconHeight));
            }

            EditorGUILayout.EndHorizontal();
        }

        // True if some other selected module requires this one (so its toggle is forced on).
        private static bool IsForcedOn(PackageDef pkg)
        {
            return _ediaPackages.Any(p => p.Install && p.Requires.Contains(pkg.Key));
        }

        // Turns on the toggle of every module required (directly or transitively) by a selected module.
        // A single forward pass suffices because Requires only ever points earlier in the list.
        private static void PropagateRequirements()
        {
            foreach (var pkg in _ediaPackages)
            {
                if (!pkg.Install) continue;
                foreach (var reqKey in pkg.Requires)
                {
                    var required = _ediaPackages.First(p => p.Key == reqKey);
                    required.Install = true;
                }
            }
        }

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

        [MenuItem("EDIA/Installer")]
        public static void ShowWindow()
        {
            var window = GetWindow<EdiaInstaller>("EDIA Installer");
            window.minSize = new Vector2(520, 160);
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

            EditorGUI.BeginDisabledGroup(_isInstallingEdia || !xrReady);

            // Selecting a module auto-selects the modules it requires (e.g. any eye-tracking
            // headset module pulls in "EDIA Eye", which in turn pulls in "EDIA Core").
            PropagateRequirements();

            foreach (var pkg in _ediaPackages)
            {
                DrawPackageRow(pkg, installedIconMsg, warnIconMsg);
            }

            EditorGUILayout.HelpBox(
                "Select a headset-specific eye-tracking module (PICO/Quest/Varjo/Vive) to also install " +
                "EDIA Eye and EDIA Core automatically.",
                MessageType.None);

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

            // Modules are selected in dependency order already (see _ediaPackages), so the
            // queue built from them installs dependencies before the modules that need them.
            PropagateRequirements();

            _installQueue.Clear();

            foreach (var pkg in _ediaPackages)
            {
                if (!pkg.Install) continue;

                _installQueue.Enqueue(new PackageToInstall(
                    pkg.PackageName,
                    pkg.GitUrl,
                    $"{pkg.DisplayName.Trim()} ({pkg.Version})"
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
