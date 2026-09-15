using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Basis.Editor
{
    public class BasisAndroidEntryPointBuildPreprocess : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android) return;
            if (PlayerSettings.Android.applicationEntry == AndroidApplicationEntry.Activity) return;

            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            Debug.Log("[BasisAndroid] Application Entry Point forced to Activity: PICO OS rejects GameActivity and Assets/Plugins/Android/AndroidManifest.xml declares com.unity3d.player.UnityPlayerActivity.");
        }
    }
}
