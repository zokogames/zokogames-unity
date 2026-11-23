using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZokoGames.Editor
{
    /// <summary>Menu items for the simulator settings asset and the WebGL template.</summary>
    public static class ZokoSDKEditor
    {
        const string SettingsPath = "Assets/Resources/" + ZokoSDKSettings.ResourceName + ".asset";

        [MenuItem("ZokoGames/Create Simulator Settings")]
        public static void CreateSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ZokoSDKSettings>(SettingsPath);
            if (existing == null)
            {
                Directory.CreateDirectory("Assets/Resources");
                existing = ScriptableObject.CreateInstance<ZokoSDKSettings>();
                AssetDatabase.CreateAsset(existing, SettingsPath);
                AssetDatabase.SaveAssets();
            }
            Selection.activeObject = existing;
            EditorGUIUtility.PingObject(existing);
        }

        [MenuItem("ZokoGames/Clear Simulated Cloud Save")]
        public static void ClearSimulatedData()
        {
            PlayerPrefs.DeleteKey("zoko-sdk-sim-data");
            PlayerPrefs.Save();
            Debug.Log("[ZokoGames] Simulated cloud save cleared.");
        }

        [MenuItem("ZokoGames/Install WebGL Template")]
        public static void InstallTemplate()
        {
            // Unity maps "Packages/<name>/..." to the package's real folder (embedded, added from disk or cached).
            string src = Path.GetFullPath("Packages/com.zokogames.sdk/WebGLTemplates/ZokoGames");
            string dst = Path.GetFullPath("Assets/WebGLTemplates/ZokoGames");
            if (!Directory.Exists(src)) { Debug.LogError("[ZokoGames] Template folder not found: " + src); return; }
            Directory.CreateDirectory(dst);
            string prefix = src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta")) continue;
                // Path.GetRelativePath is not in every API compatibility level, so strip the prefix by hand.
                string target = Path.Combine(dst, file.Substring(prefix.Length));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
            AssetDatabase.Refresh();
            PlayerSettings.WebGL.template = "PROJECT:ZokoGames";
            Debug.Log("[ZokoGames] WebGL template installed and selected (Player Settings > Resolution and Presentation).");
        }
    }
}
