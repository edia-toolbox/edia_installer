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

        // A package/sample install is async and triggers a domain reload, so "step complete" cannot be logged
        // inline. Instead a pending flag is stored in SessionState (which survives the reload) when the install
        // is requested, and cleared with a Console log once the items report as installed (see CheckStepCompletion).
        const string PendingXrKey      = "EdiaInstaller.PendingXr";
        const string PendingSamplesKey = "EdiaInstaller.PendingSamples";

        const float NameWidth    = 200f; // shared name-column width: keeps checkboxes/toggles aligned across all steps
        const float ToggleWidth  = 30f;
        const float LabelWidth   = 55f;
        const float FieldWidth   = 50f;
        const float IconWidth    = 90f;
        const float IconHeight   = 16f;
        const float VersionTextWidth = 70f;

        // ---------- Step-header styling ----------
        private static readonly Color StepAccent = new Color(0.30f, 0.57f, 0.93f);
        private GUIStyle _stepTitleStyle;
        private GUIStyle _stepBadgeStyle;
        private GUIStyle _introStyle;
        private Vector2  _scroll;

        /// <summary>Draws a prominent "STEP N — Title" header with a numbered accent badge, so the three
        /// installation stages read as clearly separate steps.</summary>
        private void DrawStepHeader(int step, string title)
        {
            _stepTitleStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
            _stepBadgeStyle ??= new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white }
            };

            EditorGUILayout.Space(6);

            var row   = EditorGUILayout.GetControlRect(false, 26);
            var badge = new Rect(row.x, row.y + 1f, 24f, 24f);
            EditorGUI.DrawRect(badge, StepAccent);
            GUI.Label(badge, step.ToString(), _stepBadgeStyle);

            var label = new Rect(badge.xMax + 8f, row.y, row.width - badge.width - 8f, 26f);
            GUI.Label(label, $"STEP {step}  —  {title}", _stepTitleStyle);

            var underline = new Rect(row.x, row.yMax, row.width, 1f);
            EditorGUI.DrawRect(underline, new Color(StepAccent.r, StepAccent.g, StepAccent.b, 0.4f));

            EditorGUILayout.Space(4);
        }

        /// <summary>Plain wrapped description under a step title — no icon, reads like the other body text.</summary>
        private void DrawIntro(string text)
        {
            _introStyle ??= new GUIStyle(EditorStyles.wordWrappedLabel);
            GUILayout.Label(text, _introStyle);
            EditorGUILayout.Space(2);
        }

        /// <summary>A checklist row: item name in the shared name column, then a read-only checkbox that ticks
        /// once the item is installed/imported. Used by Steps 1 and 2 so their checkboxes line up vertically
        /// with the module toggles in Step 3.</summary>
        private void DrawStatusRow(string label, bool done)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(NameWidth));
            using (new EditorGUI.DisabledScope(true))
                GUILayout.Toggle(done, GUIContent.none, GUILayout.Width(ToggleWidth));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>Logs a one-off "step complete" line to the Console once a pending async step (XR packages or
        /// required samples) has finished — i.e. once its items report as installed/imported after the import and
        /// any domain reload. The pending flag lives in SessionState so it survives that reload.</summary>
        private void CheckStepCompletion()
        {
            if (SessionState.GetBool(PendingXrKey, false) &&
                IsPackageInstalled(PackageNameXri) && IsPackageInstalled(PackageNameXrHands))
            {
                Debug.Log("[EDIA Installer] Step 1 complete: XR Interaction Toolkit and XR Hands are installed.");
                SessionState.SetBool(PendingXrKey, false);
            }

            if (SessionState.GetBool(PendingSamplesKey, false) &&
                IsSampleInstalled(PackageNameXri, XriSampleStarterAssets) &&
                IsSampleInstalled(PackageNameXri, XriSampleHandsInteractionDemo) &&
                IsSampleInstalled(PackageNameXrHands, XrHandsSampleHandVisualizer))
            {
                Debug.Log("[EDIA Installer] Step 2 complete: required samples imported.");
                SessionState.SetBool(PendingSamplesKey, false);
            }
        }

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
            window.minSize = new Vector2(560, 300);
        }

        private void OnGUI()
        {
            CheckStepCompletion();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("EDIA Package Installer", EditorStyles.boldLabel);

            // -------- STEP 1: XR Dependencies --------
            DrawStepHeader(1, "XR Dependencies");
            DrawXrDependenciesSection();

            // -------- STEP 2: Required Samples --------
            DrawStepHeader(2, "Required Samples");
            DrawSamplesSection();

            // -------- STEP 3: EDIA Packages --------
            DrawStepHeader(3, "EDIA Packages");
            DrawEdiaSection();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider); // separator line
            EditorGUILayout.LabelField("Status:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_statusMessage);

            EditorGUILayout.EndScrollView();
        }

        // ----- STEP 1: XR DEPENDENCIES -----
        private void DrawXrDependenciesSection()
        {
            bool xriInstalled = IsPackageInstalled(PackageNameXri);
            bool xrHandsInstalled = IsPackageInstalled(PackageNameXrHands);
            bool xrDone = xriInstalled && xrHandsInstalled;

            DrawIntro("EDIA's XR rig is built on Unity's XR Interaction Toolkit and XR Hands. " +
                      "Both packages must be present before the rig or any EDIA module works.");

            DrawStatusRow("XR Interaction Toolkit", xriInstalled);
            DrawStatusRow("XR Hands", xrHandsInstalled);

            EditorGUILayout.Space();

            // Nothing left to install once both packages are present.
            EditorGUI.BeginDisabledGroup(_isInstallingEdia || xrDone);
            if (GUILayout.Button("Install", GUILayout.Height(26)))
            {
                InstallXrPackages();
            }
            EditorGUI.EndDisabledGroup();
        }

        // ----- STEP 2: REQUIRED SAMPLES -----
        private void DrawSamplesSection()
        {
            bool xrReady = IsPackageInstalled(PackageNameXri) && IsPackageInstalled(PackageNameXrHands);

            bool starterAssets = IsSampleInstalled(PackageNameXri, XriSampleStarterAssets);
            bool handsDemo      = IsSampleInstalled(PackageNameXri, XriSampleHandsInteractionDemo);
            bool handVisualizer = IsSampleInstalled(PackageNameXrHands, XrHandsSampleHandVisualizer);
            bool samplesDone    = starterAssets && handsDemo && handVisualizer;

            DrawIntro("The XR rig reuses assets that ship as samples with those packages — the Starter Assets " +
                      "locomotion/teleport setup and the Hand Visualizer meshes. Without them the rig has broken references.");

            if (!xrReady)
                DrawIntro("Install the XR dependencies in Step 1 first — these samples ship with those packages.");

            DrawStatusRow("Starter Assets", starterAssets);
            DrawStatusRow("Hands Interaction Demo", handsDemo);
            DrawStatusRow("Hand Visualizer", handVisualizer);

            EditorGUILayout.Space();

            // Nothing left to import once all three samples are present.
            EditorGUI.BeginDisabledGroup(_isInstallingEdia || !xrReady || samplesDone);
            if (GUILayout.Button("Install", GUILayout.Height(26)))
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

            SessionState.SetBool(PendingXrKey, true); // log completion once both report installed (after reload)
            _statusMessage = "Requested XR packages via Package Manager. Unity may reload while importing.";
            Repaint();
        }


        private void InstallSamples() {
            bool allAlreadyImported =
                IsSampleInstalled(PackageNameXri, XriSampleStarterAssets) &&
                IsSampleInstalled(PackageNameXri, XriSampleHandsInteractionDemo) &&
                IsSampleInstalled(PackageNameXrHands, XrHandsSampleHandVisualizer);

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

            if (!allAlreadyImported)
                SessionState.SetBool(PendingSamplesKey, true); // log completion once all three report imported
        }

        // ----- STEP 3: EDIA PACKAGES -----
        private void DrawEdiaSection() {

            GUIContent warnIconMsg = EditorGUIUtility.IconContent("Toolbar Minus");
            warnIconMsg.text = "Not Installed";
            GUIContent installedIconMsg = EditorGUIUtility.IconContent("TestPassed");

            bool xrReady = IsPackageInstalled(PackageNameXri) && IsPackageInstalled(PackageNameXrHands);

            DrawIntro("Pick the EDIA modules to install. Selecting a headset eye-tracking module " +
                      "(PICO/Quest/Varjo/Vive) also selects EDIA Eye and EDIA Core automatically.");

            if (!xrReady)
                DrawIntro("Install the XR dependencies in Step 1 first.");

            EditorGUI.BeginDisabledGroup(_isInstallingEdia || !xrReady);

            // Selecting a module auto-selects the modules it requires (e.g. any eye-tracking
            // headset module pulls in "EDIA Eye", which in turn pulls in "EDIA Core").
            PropagateRequirements();

            foreach (var pkg in _ediaPackages)
            {
                DrawPackageRow(pkg, installedIconMsg, warnIconMsg);
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Install", GUILayout.Height(26)))
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
                Debug.Log("[EDIA Installer] Step 3 complete: selected EDIA packages installed.");
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
