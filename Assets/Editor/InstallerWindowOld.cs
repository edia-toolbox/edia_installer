#if UNITY_EDITOR
using System.Reflection.Emit;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Editor {
    
    public class InstallerWindowOld : EditorWindow
    {
        // Default Git URL – change this to your repo
        private const string DefaultGitUrl =
            "https://github.com/edia-toolbox/edia-core.git?path=/Assets/com.edia.core#main";

        private string _gitUrl = DefaultGitUrl;

        private static AddRequest _addRequest;
        private static bool _isInstalling;
        private static bool _installCore;
        private static bool _installUxf;
        private static string _uxfVersion;
        private static string _coreVersion;
        private static string _statusMessage = "Idle";

        [MenuItem("Tools/EDIA Installer Old")]
        public static void ShowWindow()
        {
            var window = GetWindow<InstallerWindowOld>("EDIA Installer");
            window.minSize = new Vector2(500, 60);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Git URL of Unity package (UPM-compatible):", EditorStyles.boldLabel);

            EditorGUI.BeginDisabledGroup(_isInstalling);
            
            EditorGUILayout.BeginHorizontal();
            _installUxf = EditorGUILayout.Toggle("EDIA UXF", _installUxf);
            _uxfVersion = EditorGUILayout.TextField("version", "main");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            _installCore = EditorGUILayout.Toggle("EDIA Core", _installCore);
            _coreVersion = EditorGUILayout.TextField("version","main");
            EditorGUILayout.EndHorizontal();
            
            
            if (_installCore) _installUxf = true;
            
            
            if (GUILayout.Button("Installing Packages"))
            {
                StartInstall();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status: " + _statusMessage);
        }

        private void StartInstalls() {
            if (_installUxf) {
                installUxf();
            }
                
        }

        private void installUxf() {
            // check if edia_uxf is installed
                
            // if not -> install it
            // Install EDIA UXF
            // string gitUrlEdiaUxf = "https://github.com/edia-toolbox/edia_uxf.git?path=/Assets/com.edia.uxf#main";
            // StartInstall(gitUrlEdiaUxf);
        }
        
        private void installEdiaCore() {
            
            // check if edia_core is already installed
            
            // Check if IATK is installed otherwise install it
            
            // CHeck if Hands is installed otherwise install it
            
            
            // Install Samples from IATK
            
            // Install Samples from Hands 
            
            StartInstall();
            
            string gitUrl = "https://github.com/edia-toolbox/edia_core.git?path=/Assets/com.edia.core#main";
            StartInstall(gitUrl);
        }
        
        private void StartInstall(string gitUrl = null)
        {


            // Kick off the add request
            try
            {
                _statusMessage = "Starting install...";
                _isInstalling = true;
                _addRequest = Client.Add(gitUrl);
                EditorApplication.update += PackageProgress;
            }
            catch (System.Exception ex)
            {
                Debug.LogError("[EDIA Installer] Exception while starting install:\n" + ex);
                _statusMessage = "Error starting install. See Console.";
                _isInstalling = false;
            }

            Repaint();
        }

        private void PackageProgress()
        {
            if (_addRequest == null)
            {
                EditorApplication.update -= PackageProgress;
                _isInstalling = false;
                _statusMessage = "No request.";
                Repaint();
                return;
            }

            if (!_addRequest.IsCompleted)
            {
                // Still working; you could update a spinner here if you want.
                return;
            }

            // Done – stop listening
            EditorApplication.update -= PackageProgress;
            _isInstalling = false;

            if (_addRequest.Status == StatusCode.Success)
            {
                Debug.Log("[EDIA Installer] Package installed: " + _addRequest.Result.packageId);
                _statusMessage = "Install succeeded: " + _addRequest.Result.name;
            }
            else
            {
                Debug.LogError("[GitPackageInstaller] Install failed: " + _addRequest.Error);
                _statusMessage = "Install failed. See Console.";
            }

            _addRequest = null;
            Repaint();
        }
    }
}
#endif
