using UnityEditor;
using UnityEngine;

namespace WordCraft.View
{
    /// <summary>
    /// Points the player icon at Assets/Art/Icon/AppIcon.png.
    ///
    ///   Unity -batchmode -quit -projectPath Client -executeMethod WordCraft.View.SetIcon.Apply
    ///
    /// A one-shot rather than a build step: the icon changes about once a year,
    /// and the result is a ProjectSettings diff that belongs in a commit anyone
    /// can read.
    /// </summary>
    public static class SetIcon
    {
        private const string Path = "Assets/Art/Icon/AppIcon.png";

        public static void Apply()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(Path);
            if (icon == null)
            {
                Debug.LogError("FAIL: no icon at " + Path);
                EditorApplication.Exit(1);
                return;
            }

            int[] sizes = PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Standalone);
            var icons = new Texture2D[sizes.Length];
            for (int i = 0; i < icons.Length; i++) icons[i] = icon;

            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Standalone, icons);
            AssetDatabase.SaveAssets();
            Debug.Log("OK: icon set for " + sizes.Length + " standalone sizes");
            EditorApplication.Exit(0);
        }
    }
}
