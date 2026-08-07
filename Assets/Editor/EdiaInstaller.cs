#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.PackageManager.UI; // for Sample API
using UnityEngine;

namespace Edia.Installer
{
    public class EdiaInstaller : EditorWindow
    {
        // Unity XR packages
        private const string PackageNameXri = "com.unity.xr.interaction.toolkit";
        private const string PackageNameXrHands = "com.unity.xr.hands";
        private const string PackageNameXrManagement = "com.unity.xr.management";

        /// <summary>The Unity XR packages the EDIA rig needs, as (package id, display name) pairs.
        ///
        /// Deliberately no provider plug-in. XR Plugin Management is the mechanism through which a project
        /// selects one, but which provider is right depends on the headset and is not EDIA's call: Quest goes
        /// through OpenXR, Vive eye tracking needs HTC's SRanipal runtime, Varjo ships its own XR plug-in.
        /// Installing OpenXR here would impose one of those on everyone, and — since enabling a loader is a
        /// project setting the installer does not write — it would sit there doing nothing except adding failing
        /// Project Validation rules. Choosing and enabling a provider is left to the researcher, who knows
        /// which headset the study runs on.</summary>
        private static readonly (string Package, string DisplayName)[] XrPackages =
        {
            (PackageNameXri,          "XR Interaction Toolkit"),
            (PackageNameXrHands,      "XR Hands"),
            (PackageNameXrManagement, "XR Plugin Management"),
        };

        private static bool AllXrPackagesInstalled()
        {
            return XrPackages.All(p => IsPackageInstalled(p.Package));
        }

        // XR samples
        private const string XriSampleStarterAssets = "Starter Assets";
        private const string XriSampleHandsInteractionDemo = "Hands Interaction Demo";
        private const string XriSampleXrDeviceSimulator = "XR Device Simulator";
        private const string XrHandsSampleHandVisualizer = "HandVisualizer";

        /// <summary>
        /// Describes one installable EDIA module. The <see cref="Requires"/> keys refer to other
        /// entries in <see cref="_ediaPackages"/>, in any direction: that list is ordered for
        /// reading, not for resolving. Selection is propagated to a fixpoint and the install queue
        /// is ordered by walking requirements first, so neither depends on the listed order.
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

        // All EDIA modules currently shipped from this workspace, in the order they are listed in the window.
        // That order is chosen for the reader — Core first, because it is what people come for — and carries
        // no meaning for dependency resolution or install order; see PropagateRequirements and
        // SelectedInInstallOrder, both of which follow Requires rather than this list's order.
        //
        // EDIA UXF: every published EDIA Core (up to and including v0.6.1) references UXF types directly
        // and does not compile without it, so Core hard-requires it here — selecting Core (or anything that
        // needs Core) pulls UXF in as a locked-on dependency.
        // REVISIT once a Core release ships with UXF absorbed into the package: from that version on, UXF
        // must no longer be forced, or it installs a redundant package alongside the merged Core.
        private static readonly List<PackageDef> _ediaPackages = new List<PackageDef>
        {
            new PackageDef("core", "EDIA Core", "com.edia.core", "edia_core", requires: new[] { "uxf" }),
            new PackageDef("uxf", "EDIA UXF", "com.edia.uxf", "edia_uxf"),
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

        /// <summary>A hint that only appears when a step is blocked on an earlier one ("do X first"), marked
        /// with Unity's info icon so it reads as a condition to act on rather than as more body text.</summary>
        private void DrawInfoNote(string text)
        {
            _introStyle ??= new GUIStyle(EditorStyles.wordWrappedLabel);

            EditorGUILayout.BeginHorizontal();
            // Shared built-in content: hand it to the label as-is, never mutate it.
            GUILayout.Label(EditorGUIUtility.IconContent("console.infoicon"),
                            GUILayout.Width(IconHeight + 4f), GUILayout.Height(IconHeight + 4f));
            GUILayout.Label(text, _introStyle);
            EditorGUILayout.EndHorizontal();

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

        /// <summary>The samples the EDIA rig relies on, as (owning package, sample name) pairs. Single source of
        /// truth for Step 2's checklist, its import run and the Step 3 gate, so a sample can't be checked in one
        /// place but forgotten in another.
        ///
        /// The order matters and is not alphabetical: Hands Interaction Demo comes last because its own project
        /// validation rules check that the Hand Visualizer and Starter Assets samples are already present. Import
        /// it first and those rules fail, which makes the sample force Unity's Project Validation window open.</summary>
        private static readonly (string Package, string Sample, string FriendlyLabel)[] RequiredSamples =
        {
            (PackageNameXrHands, XrHandsSampleHandVisualizer,   "XR Hands Hand Visualizer"),
            (PackageNameXri,     XriSampleStarterAssets,        "XRI Starter Assets"),
            (PackageNameXri,     XriSampleXrDeviceSimulator,    "XRI XR Device Simulator"),
            (PackageNameXri,     XriSampleHandsInteractionDemo, "XRI Hands Interaction Demo"),
        };

        private static bool AllRequiredSamplesImported()
        {
            return RequiredSamples.All(s => IsSampleInstalled(s.Package, s.Sample));
        }

        /// <summary>Logs a one-off "step complete" line to the Console once a pending async step (XR packages or
        /// required samples) has finished — i.e. once its items report as installed/imported after the import and
        /// any domain reload. The pending flag lives in SessionState so it survives that reload.</summary>
        private void CheckStepCompletion()
        {
            if (SessionState.GetBool(PendingXrKey, false) && AllXrPackagesInstalled())
            {
                Debug.Log("[EDIA Installer] Step 1 complete: XR packages are installed.");
                SessionState.SetBool(PendingXrKey, false);
            }

            if (SessionState.GetBool(PendingSamplesKey, false) && AllRequiredSamplesImported() && AreTmpEssentialsImported())
            {
                Debug.Log("[EDIA Installer] Step 2 complete: required samples and TextMeshPro essentials imported.");
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
        // Repeats until nothing changes, so a requirement listed after the module needing it is still picked
        // up: ticking a headset module selects EDIA Eye, which selects Core, which selects UXF, all in one call.
        private static void PropagateRequirements()
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var pkg in _ediaPackages)
                {
                    if (!pkg.Install) continue;
                    foreach (var reqKey in pkg.Requires)
                    {
                        var required = _ediaPackages.FirstOrDefault(p => p.Key == reqKey);
                        if (required == null || required.Install) continue;

                        required.Install = true;
                        changed = true;
                    }
                }
            }
        }

        // The selected modules, ordered so every module comes after the ones it requires. The window's list
        // order cannot be used for this: it is arranged for reading (Core above UXF, which Core depends on).
        private static List<PackageDef> SelectedInInstallOrder()
        {
            var ordered = new List<PackageDef>();
            var visited = new HashSet<string>();

            foreach (var pkg in _ediaPackages)
            {
                if (pkg.Install)
                    AddAfterRequirements(pkg, ordered, visited);
            }

            return ordered;
        }

        private static void AddAfterRequirements(PackageDef pkg, List<PackageDef> ordered, HashSet<string> visited)
        {
            if (!visited.Add(pkg.Key))
                return;

            foreach (var reqKey in pkg.Requires)
            {
                var required = _ediaPackages.FirstOrDefault(p => p.Key == reqKey);
                if (required != null)
                    AddAfterRequirements(required, ordered, visited);
            }

            ordered.Add(pkg);
        }

        [System.Serializable]
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

#region Install state across domain reloads

        // Installing a package triggers a domain reload, which wipes every static field — including the
        // queue and the in-flight request. Without persistence a multi-module install silently stops after
        // the first package. The queue is therefore mirrored into SessionState (which survives the reload)
        // and picked up again by ResumeAfterReload below.
        private const string KeyInstalling = "EdiaInstaller.Installing";
        private const string KeyQueueJson  = "EdiaInstaller.QueueJson";

        [System.Serializable]
        private class InstallState
        {
            public List<PackageToInstall> Queue = new List<PackageToInstall>();
            public PackageToInstall Current;
        }

        private static void SaveState()
        {
            var state = new InstallState
            {
                Queue   = _installQueue.ToList(),
                Current = _currentPackage
            };

            SessionState.SetBool(KeyInstalling, _isInstallingEdia);
            SessionState.SetString(KeyQueueJson, JsonUtility.ToJson(state));
        }

        private static void ClearState()
        {
            SessionState.EraseBool(KeyInstalling);
            SessionState.EraseString(KeyQueueJson);
        }

        [InitializeOnLoadMethod]
        private static void ResumeAfterReload()
        {
            // Step 2 uses the same survive-the-reload mechanism; re-arm it here rather than adding a second
            // load hook. Does nothing unless a sample import is actually still pending.
            ResumeSamplesAfterTmp();

            if (!SessionState.GetBool(KeyInstalling, false))
                return;

            var json = SessionState.GetString(KeyQueueJson, "");
            if (string.IsNullOrEmpty(json))
                return;

            var state = JsonUtility.FromJson<InstallState>(json);
            if (state == null)
            {
                ClearState();
                return;
            }

            _installQueue     = new Queue<PackageToInstall>(state.Queue);
            _currentPackage   = state.Current;
            _isInstallingEdia = true;

            // The AddRequest that caused this reload is gone, so PackageProgress cannot be reattached;
            // the package it was installing is already applied. Continue with the next queue item on the
            // first editor tick, once the reload has fully settled.
            EditorApplication.update -= OnResumeTick;
            EditorApplication.update += OnResumeTick;
        }

        private static void OnResumeTick()
        {
            EditorApplication.update -= OnResumeTick;

            if (!_isInstallingEdia)
                return;

            StartNextInstall();
            GetWindowIfOpen()?.Repaint();
        }

#endregion

        /// <summary>
        /// Guard for the one-time auto-open, stored per project in UserSettings/EditorUserSettings.asset.
        /// Deliberately not EditorPrefs: that lives in the user's registry and is shared by every project on
        /// the machine, so the window would auto-open in the first project only and never again.
        /// To make a project forget it opened once, delete that asset with the editor closed.
        /// </summary>
        private const string KeyAutoShown = "Edia.Installer.AutoShown";

        /// <summary>Set when this script is imported, so importing the unitypackage opens the window even in a
        /// project that has already seen it once. Lives in SessionState because the import is followed by a
        /// domain reload, and the window can only be opened on the other side of it.</summary>
        private const string KeyAutoOpenPending = "EdiaInstaller.AutoOpenPending";

        internal static void RequestAutoOpen()
        {
            SessionState.SetBool(KeyAutoOpenPending, true);

            // If the import does not trigger a reload (script unchanged), no InitializeOnLoadMethod will run,
            // so open from here too. Whichever fires first clears the flag; the other one then does nothing.
            EditorApplication.delayCall += TryAutoOpen;
        }

        [InitializeOnLoadMethod]
        private static void ScheduleAutoOpen()
        {
            EditorApplication.delayCall += TryAutoOpen;
        }

        private static void TryAutoOpen()
        {
            if (Application.isBatchMode) return;

            // Mid-install the editor reloads repeatedly; the queue reopens the window itself when needed.
            if (SessionState.GetBool(KeyInstalling, false)) return;

            bool justImported = SessionState.GetBool(KeyAutoOpenPending, false);
            if (justImported)
                SessionState.EraseBool(KeyAutoOpenPending);
            else if (!string.IsNullOrEmpty(EditorUserSettings.GetConfigValue(KeyAutoShown)))
                return; // already shown once in this project, and this load is not an import

            EditorUserSettings.SetConfigValue(KeyAutoShown, "1");
            ShowWindow();
        }

        [MenuItem("EDIA/Installer")]
        public static void ShowWindow()
        {
            var window = GetWindow<EdiaInstaller>("EDIA Installer");
            window.minSize = new Vector2(560, 300);
        }

        // Packages and samples can also change while the window sits in the background (Package Manager, a
        // colleague's commit, a manual sample import), so returning to it re-probes rather than trusting the cache.
        private void OnFocus()
        {
            InvalidateStateCache();
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

            // -------- STEP 4: EDIA Configurator --------
            DrawStepHeader(4, "EDIA Configurator");
            DrawConfiguratorSection();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider); // separator line
            EditorGUILayout.LabelField("Status:", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(_statusMessage);

            EditorGUILayout.EndScrollView();
        }

        // ----- STEP 1: XR DEPENDENCIES -----
        private void DrawXrDependenciesSection()
        {
            bool xrDone = AllXrPackagesInstalled();

            DrawIntro("The EDIA rig needs these three. Your headset's provider is not installed here — enable it " +
                      "yourself in Project Settings > XR Plug-in Management.");

            foreach (var (package, displayName) in XrPackages)
                DrawStatusRow(displayName, IsPackageInstalled(package));

            EditorGUILayout.Space();

            // Nothing left to install once both packages are present.
            EditorGUI.BeginDisabledGroup(_isInstallingEdia || _actionQueued || xrDone);
            if (GUILayout.Button("Install", GUILayout.Height(26)))
            {
                RunAfterThisGuiPass(InstallXrPackages);
            }
            EditorGUI.EndDisabledGroup();
        }

        // ----- STEP 2: REQUIRED SAMPLES -----
        private void DrawSamplesSection()
        {
            bool xrReady = AllXrPackagesInstalled();

            bool samplesDone = AllRequiredSamplesImported() && AreTmpEssentialsImported();

            DrawIntro("The rig reuses assets from these samples and EDIA's UI needs TextMeshPro's resources; " +
                      "without them the rig has broken references. Imported in the order listed.");

            if (!xrReady)
                DrawInfoNote("Install the XR dependencies in Step 1 first — these samples ship with those packages.");

            // Listed in the order they are imported, TextMeshPro first: the samples reference TMP, so importing
            // them before its essentials exist leaves broken references. Reading the checklist top to bottom
            // should tell you what happens when you press Install.
            DrawStatusRow("TextMeshPro Essentials", AreTmpEssentialsImported());
            DrawStatusRow("Hand Visualizer", IsSampleInstalled(PackageNameXrHands, XrHandsSampleHandVisualizer));
            DrawStatusRow("Starter Assets", IsSampleInstalled(PackageNameXri, XriSampleStarterAssets));
            DrawStatusRow("XR Device Simulator", IsSampleInstalled(PackageNameXri, XriSampleXrDeviceSimulator));
            DrawStatusRow("Hands Interaction Demo", IsSampleInstalled(PackageNameXri, XriSampleHandsInteractionDemo));

            EditorGUILayout.Space();

            // Nothing left to import once all samples and the TMP essentials are present. The button also stays
            // disabled while the deferred sample import is still pending: that phase outlives the click that
            // started it (and a domain reload), so _actionQueued no longer covers it.
            bool sampleImportPending = SessionState.GetBool(KeySamplesAfterTmp, false);

            EditorGUI.BeginDisabledGroup(_isInstallingEdia || _actionQueued || sampleImportPending || !xrReady || samplesDone);
            if (GUILayout.Button("Install", GUILayout.Height(26)))
            {
                RunAfterThisGuiPass(InstallSamples);
            }
            EditorGUI.EndDisabledGroup();
        }

        // Importing samples or installing packages must not happen inside OnGUI. Importing a sample forces a
        // synchronous domain reload from within the GUI pass; when the editor comes back it processes the same
        // event again, the button fires a second time, and the import starts over. That loop re-imported the XR
        // samples dozens of times, recompiled the scripts each round, and drove the editor from 3 GB to 17 GB of
        // memory while the whole UI crawled. Queuing the work makes it run once, after the pass has finished.
        private static bool _actionQueued;

        private static void RunAfterThisGuiPass(System.Action action)
        {
            if (_actionQueued) return; // a second click while the first is still queued must not start it twice

            _actionQueued = true;
            EditorApplication.delayCall += () =>
            {
                try
                {
                    action();
                }
                finally
                {
                    _actionQueued = false;
                    GetWindowIfOpen()?.Repaint();
                }
            };
        }

        // ----- INSTALLED-STATE CACHE -----
        // Every status row asks whether a package or sample is present, and OnGUI runs on each repaint (twice
        // per event: once to lay out, once to draw). Probing directly from those rows meant roughly 40
        // PackageInfo.FindForAssetPath calls and 12 Sample.FindByPackage calls per repaint, each building a
        // fresh managed object graph — FindByPackage also re-reads the package's manifest. Simply moving the
        // mouse over the window allocated gigabytes per second and kept the garbage collector saturated.
        // The answers only change when something is installed, so they are probed at most once per interval
        // and reused by every row in between.
        private const double StateCacheSeconds = 1.0;

        private static double _stateCacheStamp = double.NegativeInfinity;
        private static readonly Dictionary<string, string> _packageVersions = new Dictionary<string, string>();
        private static readonly Dictionary<string, bool> _samplesImported = new Dictionary<string, bool>();
        private static bool _tmpEssentialsImported;

        /// <summary>Forces the next query to probe again. Call after anything that installs or imports, so the
        /// window does not keep reporting the state from before that action.</summary>
        private static void InvalidateStateCache()
        {
            _stateCacheStamp = double.NegativeInfinity;
        }

        private static void RefreshStateCacheIfStale()
        {
            if (EditorApplication.timeSinceStartup - _stateCacheStamp < StateCacheSeconds)
                return;

            _stateCacheStamp = EditorApplication.timeSinceStartup;

            _packageVersions.Clear();
            foreach (var (package, _) in XrPackages)
                _packageVersions[package] = ProbePackageVersion(package);
            foreach (var pkg in _ediaPackages)
                _packageVersions[pkg.PackageName] = ProbePackageVersion(pkg.PackageName);

            _samplesImported.Clear();
            foreach (var (package, sample, _) in RequiredSamples)
                _samplesImported[package + "/" + sample] = ProbeSampleImported(package, sample);

            _tmpEssentialsImported = ProbeTmpEssentials();
        }

        // The uncached probes. Everything drawn goes through the cache above; only the cache refresh and the
        // install actions (which must not act on a stale answer) call these directly.
        private static string ProbePackageVersion(string packageName)
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/" + packageName);
            return info?.version;
        }

        private static bool ProbeSampleImported(string packageName, string sampleName)
        {
            if (ProbePackageVersion(packageName) == null) return false;

            var samples = Sample.FindByPackage(packageName, null); // use current installed version
            if (samples == null) return false;

            foreach (var sample in samples)
            {
                if (!sample.displayName.Contains(sampleName))
                    continue;

                return sample.isImported;
            }
            return false;
        }

        private static bool IsPackageInstalled(string packageName)
        {
            return IsPackageInstalled(packageName, out _);
        }

        private static bool IsPackageInstalled(string packageName, out string version)
        {
            RefreshStateCacheIfStale();

            // Not every caller asks about a listed module (the Configurator step checks Core by name), so probe.
            if (!_packageVersions.TryGetValue(packageName, out version))
            {
                version = ProbePackageVersion(packageName);
                _packageVersions[packageName] = version;
            }

            return version != null;
        }

        private static bool IsSampleInstalled(string packageName, string sampleName)
        {
            RefreshStateCacheIfStale();

            string key = packageName + "/" + sampleName;
            if (!_samplesImported.TryGetValue(key, out bool imported))
            {
                imported = ProbeSampleImported(packageName, sampleName);
                _samplesImported[key] = imported;
            }

            return imported;
        }


        private void InstallXrPackages()
        {
            InvalidateStateCache(); // act on what is installed now, not on what the rows last showed
            var missing = XrPackages.Where(p => !IsPackageInstalled(p.Package)).ToList();

            if (missing.Count == 0)
            {
                _statusMessage = "XR packages are already installed.";
                Repaint();
                return;
            }

            foreach (var (package, displayName) in missing)
            {
                Debug.Log($"[EDIA Installer] Requesting install of {displayName}...");
                Client.Add(package); // async; we don't track completion in code
            }

            SessionState.SetBool(PendingXrKey, true); // log completion once all report installed (after reload)
            _statusMessage = "Requested XR packages via Package Manager. Unity may reload while importing.";
            Repaint();
        }


        // Step 2 runs in two phases, in this order: the TextMeshPro essential resources on their own, and the
        // XR samples only once that import has settled. The samples reference TMP types and assets, so importing
        // them while the essentials are still missing bakes broken references into exactly the prefabs the rig
        // uses. Phase two cannot be the next statement here: TMP's import is an asset import followed by a
        // compilation and domain reload, which wipes every static field — see ScheduleSampleImportAfterTmp.
        private void InstallSamples() {
            InvalidateStateCache(); // act on what is imported now, not on what the rows last showed

            if (AllRequiredSamplesImported() && AreTmpEssentialsImported())
            {
                _statusMessage = "Required samples and TextMeshPro essentials are already imported.";
                Repaint();
                return;
            }

            SessionState.SetBool(PendingSamplesKey, true); // log completion once all report imported

            // Written before the import, not after: TMP's importer can take the domain down with it, and
            // everything below the call would then never run. These flags are what carry phase two across —
            // assume an import is coming, so a reload from inside the call still lands in the waiting state.
            SessionState.SetBool(KeySamplesAfterTmp, true);
            SessionState.SetBool(KeyAwaitTmpImport, true);

            _statusMessage = "Importing TextMeshPro essential resources; the XR samples follow once it has settled.";
            Repaint();

            var outcome = TryImportTmpEssentials();
            InvalidateStateCache(); // the TMP row must pick the import up, not wait out the cache interval

            // Corrected once we know what happened: only a started import has an asset import and reload to
            // wait for. AlreadyPresent and Unavailable never produce one — and both return without importing,
            // so this line is always reached in those two cases — and phase two must not sit waiting for a
            // reload that is not coming.
            SessionState.SetBool(KeyAwaitTmpImport, outcome == TmpImportOutcome.Started);
            ScheduleSampleImportAfterTmp();
        }

#region Step 2 phase two: the samples, after TMP has settled

        // The intent to still import the samples is kept in SessionState — the same mechanism the EDIA install
        // queue above uses — because it has to survive the domain reload that TMP's import triggers. Static
        // fields do not.
        private const string KeySamplesAfterTmp = "EdiaInstaller.SamplesAfterTmp";
        private const string KeyAwaitTmpImport  = "EdiaInstaller.AwaitTmpImport";

        // If TMP's resources never show up — a failed import, a TMP version that skips the request silently —
        // the samples are imported anyway once this expires, rather than leaving Step 2 unfinished with no way
        // out other than restarting the editor.
        private const double TmpSettleTimeoutSeconds = 120.0;
        private static double _tmpSettleDeadline;

        private static void ScheduleSampleImportAfterTmp()
        {
            // A fresh deadline on every registration, including the one after a domain reload: the grace period
            // should measure the wait for TMP, not the time the editor spent reloading.
            _tmpSettleDeadline = EditorApplication.timeSinceStartup + TmpSettleTimeoutSeconds;

            EditorApplication.update -= OnSamplesAfterTmpTick;
            EditorApplication.update += OnSamplesAfterTmpTick;
        }

        /// <summary>Re-arms phase two after a domain reload. Called from <see cref="ResumeAfterReload"/>, which
        /// runs on every load; without the flag set it does nothing.</summary>
        private static void ResumeSamplesAfterTmp()
        {
            if (!SessionState.GetBool(KeySamplesAfterTmp, false))
                return;

            ScheduleSampleImportAfterTmp();
        }

        private static void OnSamplesAfterTmpTick()
        {
            if (!SessionState.GetBool(KeySamplesAfterTmp, false))
            {
                EditorApplication.update -= OnSamplesAfterTmpTick;
                return;
            }

            // Let the asset database and the compiler finish first. Importing the samples while the project is
            // still settling is what leaves their TMP references unresolved.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            bool awaitingTmp = SessionState.GetBool(KeyAwaitTmpImport, false);
            // The uncached probe: this runs per tick, and the cached one refreshes every package and sample.
            bool tmpReady = ProbeTmpEssentials();

            if (awaitingTmp && !tmpReady && EditorApplication.timeSinceStartup < _tmpSettleDeadline)
                return;

            EditorApplication.update -= OnSamplesAfterTmpTick;
            SessionState.EraseBool(KeyAwaitTmpImport);

            if (awaitingTmp && !tmpReady)
                Debug.LogWarning("[EDIA Installer] The TextMeshPro essential resources did not appear in time. " +
                                 "Importing the samples now; if their text is unstyled afterwards, import the " +
                                 "essentials via Window > TextMeshPro > Import TMP Essential Resources and press " +
                                 "Install again.");

            InvalidateStateCache(); // decide on what the TMP import actually left behind
            if (AllRequiredSamplesImported())
            {
                SessionState.EraseBool(KeySamplesAfterTmp);
                GetWindowIfOpen()?.Repaint();
                return;
            }

            _statusMessage = "Importing the required XR samples...";
            ImportRequiredSamples();

            // Reached only when the pass ran to completion. A sample import can take the domain down halfway,
            // in which case the flag is still set and ResumeAfterReload re-arms the tick, which then imports
            // whatever is left. Every pass imports at least one sample, so that terminates.
            SessionState.EraseBool(KeySamplesAfterTmp);
            _statusMessage = "Step 2 imports requested. Unity may reload while importing.";
            GetWindowIfOpen()?.Repaint();
        }

        private static void ImportRequiredSamples()
        {
            // Before the samples, not after: their validation rules run on the domain load that follows the
            // import, and a rule that fails there makes the sample force Unity's Project Validation window open.
            TryReserveTeleportInteractionLayer();

            foreach (var (package, sample, label) in RequiredSamples)
                TryImportSampleByName(package, sample, label);

            InvalidateStateCache(); // the rows must pick the imports up, not wait out the cache interval
        }

        // XRI reserves interaction layer 31 for teleportation, and its Starter Assets sample treats any other
        // name there as a validation issue. That matters more than it sounds: the sample opens Unity's Project
        // Validation window by itself whenever one of its rules fails, on every domain load — and that page is
        // ruinously expensive while rules are failing (it re-evaluates them on every repaint, which took the
        // editor from 3 GB to 14 GB in twelve seconds here). Naming the layer up front keeps the sample quiet.
        //
        // Resolved by reflection so the installer still compiles in a project where XRI is absent, and the layer
        // is only claimed when it is free: silently renaming a layer a project already uses would break whatever
        // depends on it, which is the kind of thing an installer must never do behind someone's back.
        private const int TeleportInteractionLayer = 31;
        private const string TeleportInteractionLayerName = "Teleport";

        private static void TryReserveTeleportInteractionLayer()
        {
            var settingsType = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UnityEngine.XR.Interaction.Toolkit.InteractionLayerSettings"))
                .FirstOrDefault(t => t != null);

            if (settingsType == null)
                return; // XRI not installed; Step 1 gates Step 2, so this only happens if that gate is bypassed

            // FlattenHierarchy because Instance is a static member of the generic base (ScriptableSettings<T>),
            // and NonPublic because the type and its layer accessors are internal to XRI. Getting either flag
            // wrong makes every lookup below return null and this method do nothing at all — which is exactly
            // what happened on the first attempt, silently, so a failed lookup now says so.
            const System.Reflection.BindingFlags StaticFlags = System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.FlattenHierarchy;
            const System.Reflection.BindingFlags InstanceFlags = System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance;

            var instance = settingsType.GetProperty("Instance", StaticFlags)?.GetValue(null);
            var getLayerName = settingsType.GetMethod("GetLayerNameAt", InstanceFlags);
            var setLayerName = settingsType.GetMethod("SetLayerNameAt", InstanceFlags);

            if (instance == null || getLayerName == null || setLayerName == null)
            {
                Debug.LogWarning($"[EDIA Installer] Could not reach XRI's interaction layer settings, so layer " +
                                 $"{TeleportInteractionLayer} was not named \"{TeleportInteractionLayerName}\". " +
                                 "XRI's Starter Assets sample will report it in Project Validation; use its Fix " +
                                 "button there.");
                return;
            }

            string current = getLayerName.Invoke(instance, new object[] { TeleportInteractionLayer }) as string;

            if (string.Equals(current, TeleportInteractionLayerName, System.StringComparison.OrdinalIgnoreCase))
                return;

            if (!string.IsNullOrEmpty(current))
            {
                Debug.LogWarning($"[EDIA Installer] Interaction layer {TeleportInteractionLayer} is already named " +
                                 $"'{current}', so it was left untouched. XRI's Starter Assets sample expects " +
                                 $"\"{TeleportInteractionLayerName}\" there and will keep reporting it in Project " +
                                 "Validation; rename it yourself if that layer is free to repurpose.");
                return;
            }

            setLayerName.Invoke(instance, new object[] { TeleportInteractionLayer, TeleportInteractionLayerName });
            EditorUtility.SetDirty((Object)instance);
            AssetDatabase.SaveAssets(); // the settings asset must be on disk before the samples' rules read it

            Debug.Log($"[EDIA Installer] Reserved interaction layer {TeleportInteractionLayer} as " +
                      $"\"{TeleportInteractionLayerName}\", which XRI's teleportation locomotion expects.");
        }

#endregion

        // TextMeshPro's essential resources (its settings asset, shaders and default font) ship inside the
        // TMP/uGUI package as a .unitypackage and must be imported once per project. Until that happens, any
        // TMP text component throws "ArgumentNullException: ... Parameter name: shader" on validate, which is
        // what EDIA's UI prefabs run into. Importing them is a missing dependency, not a project preference,
        // so the installer handles it here.
        private const string TmpSettingsAssetPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

        private static bool ProbeTmpEssentials()
        {
            // Not AssetPathToGUID: that keeps returning a GUID for an asset that was just deleted (its default
            // includes recently deleted assets), so a project where TMP was removed still reported the
            // essentials as present and Step 2 ticked a box for something that was not there. Loading the asset
            // answers the question that is actually being asked — is it in the project right now.
            return AssetDatabase.LoadMainAssetAtPath(TmpSettingsAssetPath) != null;
        }

        private static bool AreTmpEssentialsImported()
        {
            RefreshStateCacheIfStale();
            return _tmpEssentialsImported;
        }

        /// <summary>What the essentials import did, which is what decides whether the sample import has to wait:
        /// only an import that actually started has an asset import and domain reload still to come.</summary>
        private enum TmpImportOutcome
        {
            AlreadyPresent, // nothing was imported, so nothing has to settle
            Started,        // TMP's importer ran; its assets and any reload still have to land
            Unavailable     // TMP's importer could not be resolved or threw; the essentials stay missing
        }

        /// <summary>Runs TMP's own "Import TMP Essential Resources" routine. Resolved by reflection because the
        /// installer must compile in any project, including one where the TMP/uGUI package is absent — a compile
        /// error here would take the whole installer down instead of just this one step.</summary>
        private static TmpImportOutcome TryImportTmpEssentials()
        {
            if (AreTmpEssentialsImported())
            {
                Debug.Log("[EDIA Installer] TextMeshPro essential resources already imported.");
                return TmpImportOutcome.AlreadyPresent;
            }

            var importerType = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("TMPro.TMP_PackageResourceImporter"))
                .FirstOrDefault(t => t != null);

            var importMethod = importerType?.GetMethod(
                "ImportResources",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

            if (importMethod == null)
            {
                Debug.LogWarning("[EDIA Installer] Could not import the TextMeshPro essential resources automatically. " +
                                 "Import them manually via Window > TextMeshPro > Import TMP Essential Resources.");
                return TmpImportOutcome.Unavailable;
            }

            try
            {
                Debug.Log("[EDIA Installer] Importing TextMeshPro essential resources...");
                importMethod.Invoke(null, new object[] { true, false, false }); // essentials, no examples, silent
                return TmpImportOutcome.Started;
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[EDIA Installer] Failed to import the TextMeshPro essential resources:\n" + ex);
                return TmpImportOutcome.Unavailable;
            }
        }

        // ----- STEP 3: EDIA PACKAGES -----
        private void DrawEdiaSection() {

            GUIContent warnIconMsg = EditorGUIUtility.IconContent("Toolbar Minus");
            warnIconMsg.text = "Not Installed";
            GUIContent installedIconMsg = EditorGUIUtility.IconContent("TestPassed");

            bool xrReady = AllXrPackagesInstalled();
            bool samplesReady = AllRequiredSamplesImported();

            DrawIntro("Pick the EDIA modules to install. Selecting a headset eye-tracking module " +
                      "(PICO/Quest/Varjo/Vive) also selects EDIA Eye and EDIA Core automatically.");

            // The EDIA rig references the Step-2 samples (Starter Assets locomotion/teleport, Hand Visualizer
            // meshes). Installing modules before those samples exist reproduces the "missing Samples" breakage
            // (dangling teleport refs, hand-mesh NRE), so Step 3 is gated on both XR packages AND samples.
            if (!xrReady)
                DrawInfoNote("Install the XR dependencies in Step 1 first.");
            else if (!samplesReady)
                DrawInfoNote("Import the required samples in Step 2 first — the EDIA rig references them, and " +
                             "installing modules without the samples leaves broken references.");

            EditorGUI.BeginDisabledGroup(_isInstallingEdia || _actionQueued || !xrReady || !samplesReady);

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
                RunAfterThisGuiPass(StartEdiaInstalls);
            }

            EditorGUI.EndDisabledGroup();
        }

        // ----- STEP 4: EDIA CONFIGURATOR -----
        // There is deliberately no Project Validation step. What remains after Step 3 is project *settings* —
        // which XR provider to enable, which interaction profiles, target-platform and rendering options — and
        // those are headset-specific choices a Quest study answers differently from a Vive one. Sending everyone
        // to Unity's validation page made that worse rather than better: it lists items that do not apply to
        // their hardware, and while any rule is failing that page re-evaluates all of them on every repaint,
        // which is expensive enough to make the editor crawl. Researchers configure their own headset from here.
        //
        // Its own step rather than a second half of another one: creating the layers is a separate action, in a
        // different window, and one that is easy to skip past when it hangs off the bottom of another step.
        private void DrawConfiguratorSection()
        {
            DrawIntro("Press \"Setup layers\" in the Configurator. EDIA's rig and UI rely on its own layers " +
                      "(e.g. the message-panel layer); without them the panels and interactors behave " +
                      "incorrectly. This is not automated on purpose: writing layers could overwrite layers your " +
                      "project already uses, so you stay in control of that.");

            // Opened via the menu item so the installer keeps no compile-time dependency on edia_core;
            // the Configurator only exists once Step 3 has installed Core.
            EditorGUI.BeginDisabledGroup(!IsPackageInstalled("com.edia.core"));
            if (GUILayout.Button("Open EDIA Configurator", GUILayout.Height(26)))
            {
                if (!EditorApplication.ExecuteMenuItem("EDIA/Configurator"))
                    Debug.LogWarning("[EDIA Installer] Could not open the EDIA Configurator. " +
                                     "Open it manually via the EDIA > Configurator menu.");
            }
            EditorGUI.EndDisabledGroup();

            if (!IsPackageInstalled("com.edia.core"))
                DrawInfoNote("Install EDIA Core in Step 3 first — the Configurator ships with it.");
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

            PropagateRequirements();

            _installQueue.Clear();

            // Queued dependencies-first, not in window order, so a module never installs before what it needs.
            foreach (var pkg in SelectedInInstallOrder())
            {
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
                ClearState();
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
                ClearState();
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
                ClearState();
                Debug.Log("[EDIA Installer] Step 3 complete: selected EDIA packages installed.");
                GetWindowIfOpen()?.Repaint();
                return;
            }

            _currentPackage = _installQueue.Dequeue();

            _statusMessage = $"Installing: {_currentPackage.DisplayName}...";
            Debug.Log($"[EDIA Installer] Installing {_currentPackage.DisplayName} from {_currentPackage.GitUrl}");

            try
            {
                // Persist before the request: installing triggers a domain reload that wipes the statics.
                SaveState();

                _addRequest = Client.Add(_currentPackage.GitUrl);
                EditorApplication.update += PackageProgress;
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[EDIA Installer] Exception while starting install:\n" + ex);
                _statusMessage = "Error starting install. See Console.";
                _isInstallingEdia = false;
                ClearState();
            }

            GetWindowIfOpen()?.Repaint();
        }

        private static void PackageProgress()
        {
            if (_addRequest == null)
            {
                EditorApplication.update -= PackageProgress;

                // A domain reload can drop the in-flight request; if work remains, carry on with it.
                if (_isInstallingEdia && _installQueue.Count > 0)
                {
                    StartNextInstall();
                    return;
                }

                _isInstallingEdia = false;
                ClearState();
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
            InvalidateStateCache(); // a module just appeared (or did not); re-probe before drawing its row again

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

    /// <summary>
    /// Opens the installer when this script is imported — which is exactly what importing the unitypackage does.
    /// The per-project guard alone was not enough: it fires on the first load in a project and never again, so
    /// importing the installer into a project that had already seen it once (a test project being reset, a
    /// project where someone removed and re-added it) silently opened nothing.
    /// </summary>
    internal sealed class EdiaInstallerImportWatcher : AssetPostprocessor
    {
        private const string ScriptFileName = "EdiaInstaller.cs";

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
                                                   string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (var path in importedAssets)
            {
                if (!path.EndsWith(ScriptFileName, System.StringComparison.Ordinal))
                    continue;

                EdiaInstaller.RequestAutoOpen();
                return;
            }
        }
    }
}
#endif
