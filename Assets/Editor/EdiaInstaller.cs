#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using UnityEditor;
using Upm = UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Edia.Installer {
    
    // v0.3.0 (2026-05-07, eioe)

    public class EdiaInstaller : EditorWindow {
        // EDIA package IDs (as in their package.json)
        private const string PackageNameUxf = "com.edia.uxf";
        private const string PackageNameCore = "com.edia.core";
        private const string PackageNameLsl = "com.edia.lsl";
        private const string PackageNameEye = "com.edia.eye";
        private const string PackageNameRcas = "com.edia.rcas";
        private const string PackageNameEyeQuest = "com.edia.eye.quest";

        // Unity XR packages
        private const string PackageNameXri = "com.unity.xr.interaction.toolkit";
        private const string PackageNameXrHands = "com.unity.xr.hands";

        // XR samples
        private const string XriSampleStarterAssets = "Starter Assets";
        private const string XriSampleHandsInteractionDemo = "Hands Interaction Demo";
        private const string XriSampleXrDeviceSimulator = "XR Device Simulator";
        private const string XrHandsSampleHandVisualizer = "HandVisualizer";

        // EDIA Git base URLs (without version/branch part)
        private const string GitBaseUxf = "https://github.com/edia-toolbox/edia_uxf.git";
        private const string GitBaseCore = "https://github.com/edia-toolbox/edia_core.git";
        private const string GitBaseLsl = "https://github.com/edia-toolbox/edia_lsl.git";
        private const string GitBaseEye = "https://github.com/edia-toolbox/edia_eye.git";
        private const string GitBaseRcas = "https://github.com/edia-toolbox/edia_rcas.git";
        private const string GitBaseEyeQuest = "https://github.com/edia-toolbox/edia_eye_quest.git";

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
        private static bool _installRcas;
        private static bool _installEyeQuest;
        private static string _uxfVersion = "main";
        private static string _coreVersion = "main";
        private static string _lslVersion = "main";
        private static string _eyeVersion = "main";
        private static string _rcasVersion = "main";
        private static string _eyeQuestVersion = "main";
        private static string _uxfVersionInstalled;
        private static string _coreVersionInstalled;
        private static string _lslVersionInstalled;
        private static string _eyeVersionInstalled;
        private static string _eyeQuestVersionInstalled;
        private static string _rcasVersionInstalled;

        const float NameWidth = 120f;
        const float ToggleWidth = 30f;
        const float LabelWidth = 110f;
        const float FieldWidth = 30f;
        const float IconWidth = 90f;
        const float IconHeight = 16f;
        const float VersionTextWidth = 70f;

        private enum InstallStepKind {
            Package,
            Sample
        }

        // Data structure for queued installer work
        [Serializable]
        private struct InstallStep {
            public InstallStepKind Kind;
            public string PackageName;
            public string RequestArgument;
            public string DisplayName;
            public string SampleNameFragment;

            public InstallStep(InstallStepKind kind, string packageName, string requestArgument, string displayName,
                string sampleNameFragment = null) {
                Kind = kind;
                PackageName = packageName;
                RequestArgument = requestArgument;
                DisplayName = displayName;
                SampleNameFragment = sampleNameFragment;
            }
        }

        private const string KeyInstalling = "EDIA_INSTALLING";
        private const string KeyQueueJson = "EDIA_QUEUE_JSON";
        private const string KeyCurrentJson = "EDIA_CURRENT_JSON";

        // save installation state across domain reloads
        [Serializable]
        private class InstallState {
            public List<InstallStep> Queue = new();
            public InstallStep Current;
        }

        private static void SaveState() {
            var state = new InstallState {
                Queue = _installQueue.ToList(),
                Current = _currentPackage
            };

            SessionState.SetBool(KeyInstalling, _isInstallingEdia);
            SessionState.SetString(KeyQueueJson, JsonUtility.ToJson(state));
        }

        private static bool TryLoadState(out InstallState state) {
            state = null;
            if (!SessionState.GetBool(KeyInstalling, false))
                return false;

            var json = SessionState.GetString(KeyQueueJson, "");
            if (string.IsNullOrEmpty(json))
                return false;

            state = JsonUtility.FromJson<InstallState>(json);
            return state != null;
        }


        [InitializeOnLoadMethod]
        private static void ResumeAfterReload() {
            if (!TryLoadState(out var state))
                return;

            _installQueue = new Queue<InstallStep>(state.Queue);
            _currentPackage = state.Current;
            _isInstallingEdia = true;

            // After a domain reload, any in-flight _addRequest is gone.
            // So do NOT attach PackageProgress here.
            EditorApplication.update -= OnResumeTick;
            EditorApplication.update += OnResumeTick;
        }

        private static void OnResumeTick() {
            // Run once, then detach.
            EditorApplication.update -= OnResumeTick;

            // If we already finished earlier, clean up.
            if (!_isInstallingEdia)
                return;

            // Continue with the next package.
            // This recreates _addRequest and reattaches PackageProgress.
            StartNextInstall();

            GetWindowIfOpen()?.Repaint();
        }

        private static void ClearState() {
            Debug.Log("Clearing state of EDIA Installer.");
            SessionState.EraseBool(KeyInstalling);
            SessionState.EraseString(KeyQueueJson);
        }


        void DrawPackageRow(string displayName, string packageName, ref bool installFlag, ref string desiredVersion,
            ref string installedVersion, GUIContent installedIconMsg, GUIContent warnIconMsg) {
            EditorGUILayout.BeginHorizontal();

            GUILayout.Label(displayName, GUILayout.Width(NameWidth));
            installFlag = GUILayout.Toggle(installFlag, GUIContent.none, GUILayout.Width(ToggleWidth));

            GUILayout.Label(new GUIContent(
                    "version | branch",
                    "Specify either a release version (e.g. 'v0.1.2' or 'exp-validet') or a git branch (e.g. 'main'). " +
                    "You cannot install branches which have a '.' or '-' in the branch name."
                ),
                GUILayout.Width(LabelWidth));
            desiredVersion = GUILayout.TextField(
                IsPackageInstalled(packageName, out _, out var source) ? 
                    source :
                    desiredVersion, 
                GUILayout.MinWidth(FieldWidth),
                GUILayout.ExpandWidth(true));

            if (IsPackageInstalled(packageName, out installedVersion, out _)) {
                GUILayout.Label(installedIconMsg, GUILayout.Width(IconWidth), GUILayout.Height(IconHeight));
                GUILayout.Label(installedVersion, GUILayout.Width(VersionTextWidth));
            }
            else {
                GUILayout.Label(warnIconMsg, GUILayout.Width(IconWidth), GUILayout.Height(IconHeight));
                GUILayout.Label("", GUILayout.Width(VersionTextWidth));
            }

            EditorGUILayout.EndHorizontal();
        }


        private static Queue<InstallStep> _installQueue = new Queue<InstallStep>();
        private static InstallStep _currentPackage;

        [MenuItem("EDIA/EDIA Installer")]
        public static void ShowWindow() {
            var window = GetWindow<EdiaInstaller>("EDIA Installer");
            window.minSize = new Vector2(500, 120);
        }

        private void OnGUI() {
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
        private void DrawXrSection() {
            EditorGUILayout.LabelField("1) XR Dependencies", EditorStyles.boldLabel);

            string version_xri = "";
            string version_xrhands = "";
            bool xriInstalled = IsPackageInstalled(PackageNameXri, out version_xri, out _);
            bool xrHandsInstalled = IsPackageInstalled(PackageNameXrHands, out version_xrhands, out _);
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
            }
            else {
                EditorGUILayout.LabelField(warnIconMsg);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("XR Hands: ");
            if (xrHandsInstalled) {
                EditorGUILayout.LabelField(greenIconMsg);
                EditorGUILayout.LabelField($"(v{version_xrhands})");
            }
            else {
                EditorGUILayout.LabelField(warnIconMsg);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            EditorGUI.BeginDisabledGroup(_isInstallingEdia);
            if (GUILayout.Button("Install / Update XR Packages (XRI + XR Hands)", GUILayout.Height(24))) {
                InstallXrPackages();
            }

            EditorGUI.EndDisabledGroup();

            if (!xrReady) {
                EditorGUILayout.HelpBox(
                    "Install XR Interaction Toolkit and XR Hands first. " +
                    "You can then install EDIA packages and XR samples.",
                    MessageType.Info);
            }
            else {
                EditorGUILayout.LabelField("Required Samples:", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[ XRI ] {XriSampleStarterAssets}");
                if (!IsSampleInstalled(PackageNameXri, XriSampleStarterAssets))
                    EditorGUILayout.LabelField(warnIconMsg);
                else {
                    EditorGUILayout.LabelField(greenIconMsg);
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[ XRI ] {XriSampleHandsInteractionDemo}");
                if (!IsSampleInstalled(PackageNameXri, XriSampleHandsInteractionDemo))
                    EditorGUILayout.LabelField(warnIconMsg);
                else {
                    EditorGUILayout.LabelField(greenIconMsg);
                }

                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[ XRI ] {XriSampleXrDeviceSimulator}");
                if (!IsSampleInstalled(PackageNameXri, XriSampleXrDeviceSimulator))
                    EditorGUILayout.LabelField(warnIconMsg);
                else {
                    EditorGUILayout.LabelField(greenIconMsg);
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[ XR Hands ] {XrHandsSampleHandVisualizer}");
                if (!IsSampleInstalled(PackageNameXrHands, XrHandsSampleHandVisualizer))
                    EditorGUILayout.LabelField(warnIconMsg);
                else {
                    EditorGUILayout.LabelField(greenIconMsg);
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUI.BeginDisabledGroup(_isInstallingEdia);
            if (GUILayout.Button("Install required Samples (XRI + XR Hands)", GUILayout.Height(24))) {
                InstallSamples();
            }

            EditorGUI.EndDisabledGroup();
        }

        private static bool IsPackageInstalled(string packageName) {
            // Uses PackageInfo to check synchronously if the package exists
            var info = Upm.PackageInfo.FindForAssetPath("Packages/" + packageName);
            return info != null;
        }

        private static bool IsPackageInstalled(string packageName, out string version, out string source) {
            // Uses PackageInfo to check synchronously if the package exists
            var info = Upm.PackageInfo.FindForAssetPath("Packages/" + packageName);
            if (info != null) {
                if (info.source == Upm.PackageSource.Git)
                    source = info.packageId.Contains("#") ? 
                        info.packageId.Substring(info.packageId.LastIndexOf('#') + 1) : 
                        info.packageId;
                else
                    source = info.packageId;
                version = info.version;
                return true;
            }
            source = null;
            version = null;
            return false;
        }

        private static bool IsSampleInstalled(string packageName, string sampleName) {
            if (!IsPackageInstalled(packageName)) return false;

            try {
                var samples = Upm.UI.Sample.FindByPackage(packageName, null); // use current installed version

                if (samples == null || !samples.Any()) {
                    return false;
                }

                foreach (var sample in samples) {
                    if (!sample.displayName.Contains(sampleName))
                        continue;

                    return sample.isImported;
                }
            }
            catch {
                // Tendentially hacky. Works around the race condition that package might seem available during its install
                // but Samples are not yet accessible.
                // This just avoids a few errors in the console until the situation resolves.
                // While there are cleaner ways for this, I do not expect this to create major headaches. 
                return false;
            }

            return false;
        }


        private void InstallXrPackages() {
            _installQueue.Clear();

            bool needXri = !IsPackageInstalled(PackageNameXri);
            bool needHands = !IsPackageInstalled(PackageNameXrHands);

            if (!needXri && !needHands) {
                _statusMessage = "XR packages are already installed.";
                Repaint();
                return;
            }

            if (needXri) {
                _installQueue.Enqueue(new InstallStep(
                    InstallStepKind.Package,
                    PackageNameXri,
                    PackageNameXri,
                    "XR Interaction Toolkit"
                ));
            }

            if (needHands) {
                _installQueue.Enqueue(new InstallStep(
                    InstallStepKind.Package,
                    PackageNameXrHands,
                    PackageNameXrHands,
                    "XR Hands"
                ));
            }

            StartQueuedInstalls("Installing XR dependencies...");
        }


        private void InstallSamples() {
            _installQueue.Clear();

            _installQueue.Enqueue(new InstallStep(
                InstallStepKind.Sample,
                PackageNameXrHands,
                null,
                "XR Hands Hand Visualizer",
                XrHandsSampleHandVisualizer
            ));

            _installQueue.Enqueue(new InstallStep(
                InstallStepKind.Sample,
                PackageNameXri,
                null,
                "XRI Starter Assets",
                XriSampleStarterAssets
            ));
            
            _installQueue.Enqueue(new InstallStep(
                InstallStepKind.Sample,
                PackageNameXri,
                null,
                "XRI XR Device Simulator",
                XriSampleXrDeviceSimulator
            ));

            _installQueue.Enqueue(new InstallStep(
                InstallStepKind.Sample,
                PackageNameXri,
                null,
                "XRI Hands Interaction Demo",
                XriSampleHandsInteractionDemo
            ));

            StartQueuedInstalls("Importing required XR samples...");
        }
#region EDIA modules

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
            _uxfVersion = _uxfVersionInstalled ?? _uxfVersion ?? string.Empty;
            _coreVersion = _coreVersionInstalled ?? _coreVersion ?? string.Empty;
            _lslVersion = _lslVersionInstalled ?? _lslVersion ?? string.Empty;
            _eyeVersion = _eyeVersionInstalled ?? _eyeVersion ?? string.Empty;
            _rcasVersion = _rcasVersionInstalled ?? _rcasVersion ?? string.Empty;
            _eyeQuestVersion = _eyeQuestVersionInstalled ?? _eyeQuestVersion ?? string.Empty;

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
                "EDIA RCAS",
                PackageNameRcas,
                ref _installRcas,
                ref _rcasVersion,
                ref _rcasVersionInstalled,
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

            DrawPackageRow(
                "EDIA Eye Quest",
                PackageNameEyeQuest,
                ref _installEyeQuest,
                ref _eyeQuestVersion,
                ref _eyeQuestVersionInstalled,
                installedIconMsg,
                warnIconMsg);

            // Dependency rules inside EDIA:
            if (_installCore) _installUxf = true;
            if (_installLsl) _installCore = true;
            if (_installEye) _installCore = true;
            if (_installRcas) _installCore = true;
            if (_installEyeQuest) _installCore = true;
            if (_installEyeQuest) _installEye = true;

            EditorGUILayout.Space();

            if (GUILayout.Button("Install EDIA Packages", GUILayout.Height(30))) {
                StartEdiaInstalls();
            }

            EditorGUI.EndDisabledGroup();
        }

        // Entry point when EDIA button is pressed
        private void StartEdiaInstalls() {
            if (_isInstallingEdia) {
                _statusMessage = "Already installing EDIA packages. Please wait.";
                Repaint();
                return;
            }

            // Build a fresh queue based on user choices, in dependency order
            _installQueue.Clear();

            if (_installUxf) {
                string url = ParseVersionToGitString(_uxfVersion, GitBaseUxf, PackageNameUxf);
                _installQueue.Enqueue(new InstallStep(
                    InstallStepKind.Package,
                    PackageNameUxf,
                    url,
                    $"EDIA UXF ({_uxfVersion})"
                ));
            }

            if (_installCore) {
                string url = ParseVersionToGitString(_coreVersion, GitBaseCore, PackageNameCore);
                _installQueue.Enqueue(new InstallStep(
                    InstallStepKind.Package,
                    PackageNameCore,
                    url,
                    $"EDIA Core ({_coreVersion})"
                ));
            }

            if (_installLsl) {
                string url = ParseVersionToGitString(_lslVersion, GitBaseLsl, PackageNameLsl);
                _installQueue.Enqueue(new InstallStep(
                    InstallStepKind.Package,
                    PackageNameLsl,
                    url,
                    $"EDIA LSL ({_lslVersion})"
                ));
            }

            if (_installEye) {
                string url = ParseVersionToGitString(_eyeVersion, GitBaseEye, PackageNameEye);
                _installQueue.Enqueue(new InstallStep(
                    InstallStepKind.Package,
                    PackageNameEye,
                    url,
                    $"EDIA Eye ({_eyeVersion})"
                ));
            }

            if (_installRcas) {
                string url = ParseVersionToGitString(_rcasVersion, GitBaseRcas, PackageNameRcas);
                _installQueue.Enqueue(new InstallStep(
                    InstallStepKind.Package,
                    PackageNameRcas,
                    url,
                    $"EDIA RCAS ({_rcasVersion})"
                ));
            }
            
            if (_installEyeQuest) {
                string url = ParseVersionToGitString(_eyeQuestVersion, GitBaseEyeQuest, PackageNameEyeQuest);
                _installQueue.Enqueue(new InstallStep(
                    InstallStepKind.Package,
                    PackageNameEyeQuest,
                    url,
                    $"EDIA Eye Quest ({_eyeQuestVersion})"
                ));
            }

            if (_installQueue.Count == 0) {
                _statusMessage = "Nothing selected to install.";
                Repaint();
                return;
            }

            _statusMessage = "Checking already installed EDIA packages...";
            _isInstallingEdia = true;

            // Ask Package Manager for the list of installed packages
            _listRequest = Upm.Client.List(true);
            EditorApplication.update += OnListProgress;

            Repaint();
        }

        private void StartQueuedInstalls(string initialStatus) {
            if (_isInstallingEdia) {
                _statusMessage = "Already installing packages. Please wait.";
                Repaint();
                return;
            }

            if (_installQueue.Count == 0) {
                _statusMessage = "Nothing to install.";
                Repaint();
                return;
            }

            _statusMessage = initialStatus;
            _isInstallingEdia = true;
            StartNextInstall();
            Repaint();
        }

        private string ParseVersionToGitString(string version, string baseString, string pckgName) {
            // Standard versions (e.g., v0.6.2 or 0.6.2)
            if (version.Contains('.')) {
                if (version.StartsWith("v"))
                    version = version.Substring(1);
                if (System.Version.TryParse(version, out _))
                    return baseString + "#v" + version;
                Debug.LogError("Invalid version format. Must match SemVer (X.Y.Z).");
            }

            // special releases (e.g., exp-validet)
            if (version.Contains('-')) {
                return baseString + "#" + version;
            }

            // branches
            Debug.Log($"Trying to import {pckgName} from git, using branch: {version}");
            return baseString + $"?path=/Assets/{pckgName}" + "#" + version;
        }

        // Handle result of Client.List: filter out already installed packages
        private static void OnListProgress() {
            if (!_listRequest.IsCompleted)
                return;

            EditorApplication.update -= OnListProgress;

            if (_listRequest.Status != Upm.StatusCode.Success) {
                Debug.LogError("[EDIA Installer] Failed to list packages: " + _listRequest.Error);
                _isInstallingEdia = false;
                _statusMessage = "Failed to list packages. See Console.";
                GetWindowIfOpen()?.Repaint();
                return;
            }

            var installedNames = new HashSet<string>(_listRequest.Result.Select(p => p.name));
            var remaining = new Queue<InstallStep>();

            foreach (var pkg in _installQueue) {
                if (pkg.Kind == InstallStepKind.Package && installedNames.Contains(pkg.PackageName)) {
                    Debug.Log($"[EDIA Installer] {pkg.DisplayName} already installed ({pkg.PackageName}), skipping.");
                }
                else {
                    remaining.Enqueue(pkg);
                }
            }

            _installQueue = remaining;

            if (_installQueue.Count == 0) {
                _statusMessage = "All selected EDIA packages are already installed.";
                _isInstallingEdia = false;
                GetWindowIfOpen()?.Repaint();
                return;
            }

            // Start installing the first one
            StartNextInstall();
        }

        private static void StartNextInstall() {
            if (_installQueue.Count == 0) {
                _statusMessage = "All EDIA installations completed.";
                _isInstallingEdia = false;

                ClearState();

                GetWindowIfOpen()?.Repaint();
                return;
            }

            _currentPackage = _installQueue.Dequeue();

            _statusMessage = $"{(_currentPackage.Kind == InstallStepKind.Package ? "Installing" : "Importing")}: {_currentPackage.DisplayName}...";
            Debug.Log($"[EDIA Installer] Starting {_currentPackage.Kind}: {_currentPackage.DisplayName}");

            try {
                SaveState();

                if (_currentPackage.Kind == InstallStepKind.Package) {
                    _addRequest = Upm.Client.Add(_currentPackage.RequestArgument);
                    EditorApplication.update += PackageProgress;
                }
                else {
                    ImportCurrentSample();
                }
            }
            catch (System.Exception ex) {
                Debug.LogError("[EDIA Installer] Exception while starting install:\n" + ex);
                _statusMessage = "Error starting install. See Console.";
                _isInstallingEdia = false;

                ClearState();
            }

            GetWindowIfOpen()?.Repaint();
        }

        private static void PackageProgress() {
            if (_addRequest == null) {
                Debug.Log("Finishing installs; queue is empty.");
                EditorApplication.update -= PackageProgress;

                // If we're installing and still have queued work, continue.
                if (_isInstallingEdia && _installQueue != null && _installQueue.Count > 0) {
                    StartNextInstall();
                    return;
                }

                _isInstallingEdia = false;
                _statusMessage = "No active request.";
                GetWindowIfOpen()?.Repaint();
                return;
            }

            if (!_addRequest.IsCompleted)
                return;

            EditorApplication.update -= PackageProgress;

            if (_addRequest.Status == Upm.StatusCode.Success) {
                Debug.Log("[EDIA Installer] Package installed: " + _addRequest.Result.packageId);
                _statusMessage = $"Install succeeded: {_currentPackage.DisplayName}";
            }
            else {
                Debug.LogError("[EDIA Installer] Install failed: " + _addRequest.Error);
                _statusMessage = $"Install FAILED: {_currentPackage.DisplayName}. See Console.";
            }

            _addRequest = null;

            // Continue with next in queue, if any
            StartNextInstall();
        }

        private static void ImportCurrentSample() {
            TryImportSampleByName(
                _currentPackage.PackageName,
                _currentPackage.SampleNameFragment,
                _currentPackage.DisplayName
            );

            _statusMessage = $"Import finished: {_currentPackage.DisplayName}";
            StartNextInstall();
        }

        /// <summary>
        /// Try to import a sample whose displayName contains the given text.
        /// </summary>
        private static void TryImportSampleByName(string packageName, string sampleNameFragment, string friendlyLabel) {
            try {
                var samples = Upm.UI.Sample.FindByPackage(packageName, null); // use current installed version

                if (samples == null || !samples.Any()) {
                    Debug.LogWarning($"[EDIA Installer] No samples found for package '{packageName}'. " +
                                     $"Cannot import {friendlyLabel}.");
                    return;
                }

                foreach (var sample in samples) {
                    if (!sample.displayName.Contains(sampleNameFragment))
                        continue;

                    if (sample.isImported) {
                        Debug.Log($"[EDIA Installer] {friendlyLabel} sample already imported.");
                    }
                    else {
                        Debug.Log($"[EDIA Installer] Importing {friendlyLabel} sample...");
                        sample.Import(Upm.UI.Sample.ImportOptions.None);
                    }

                    return;
                }

                Debug.LogWarning(
                    $"[EDIA Installer] Could not find sample '{sampleNameFragment}' in package '{packageName}'.");
            }
            catch (System.Exception ex) {
                Debug.LogError(
                    $"[EDIA Installer] Failed to import sample '{friendlyLabel}' from '{packageName}': {ex}");
            }
        }

        // Helper to repaint from static methods
        private static EdiaInstaller GetWindowIfOpen() {
            return UnityEngine.Resources.FindObjectsOfTypeAll<EdiaInstaller>().FirstOrDefault();
        }

#endregion
        
    }
}



#endif
