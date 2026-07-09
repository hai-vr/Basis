#if BASIS_FRAMEWORK_EXISTS
using Basis.Integration.SlimeVR;
using UnityEditor;
using UnityEngine;

namespace Basis.Integration.SlimeVR.Editor
{
    /// <summary>Live view of the SlimeVR connection: body measurements, skeleton parts, trackers and resets.</summary>
    public class BasisSlimeVRDebugWindow : EditorWindow
    {
        private Vector2 _scroll;

        [MenuItem("Basis/Tests And Debug/SlimeVR Debug")]
        public static void ShowWindow()
        {
            GetWindow<BasisSlimeVRDebugWindow>("SlimeVR Debug");
        }

        private void OnEnable()
        {
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter play mode to talk to SlimeVR.", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Enabled", BasisSlimeVRSettings.Enable.RawValue ? "Yes" : "No");
            EditorGUILayout.LabelField("Connected", BasisSlimeVRBridge.IsConnected ? "Yes" : "No");
            EditorGUILayout.LabelField("Auto Apply", BasisSlimeVRSettings.ApplyBodyMeasurements.RawValue ? "Yes" : "No");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Body Measurements", EditorStyles.boldLabel);
            if (BasisSlimeVRBridge.HasBodyMetrics)
            {
                var metrics = BasisSlimeVRBridge.LastBodyMetrics;
                EditorGUILayout.LabelField("Eye Height", $"{metrics.EyeHeightMeters:F3} m");
                EditorGUILayout.LabelField("Full Height (est.)", $"{metrics.FullHeightMeters:F3} m");
                EditorGUILayout.LabelField("Wrist Span", $"{metrics.WristSpanMeters:F3} m");
                EditorGUILayout.LabelField("Controller Span", $"{metrics.ControllerSpanMeters:F3} m");
                EditorGUILayout.LabelField("Applied PlayerEyeHeight", $"{BasisHeightDriver.PlayerEyeHeight:F3} m");
                EditorGUILayout.LabelField("Applied PlayerArmSpan", $"{BasisHeightDriver.PlayerArmSpan:F3} m");
                EditorGUILayout.LabelField("DeviceScale", $"{BasisHeightDriver.DeviceScale:F4}");
            }
            else
            {
                EditorGUILayout.LabelField("(none received yet)");
            }

            var config = BasisSlimeVRBridge.LastSkeletonConfig;
            if (config != null && config.Parts.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Skeleton Parts ({config.Parts.Count})", EditorStyles.boldLabel);
                foreach (var part in config.Parts)
                {
                    EditorGUILayout.LabelField(part.Key.ToString(), $"{part.Value:F4} m");
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Trackers ({BasisSlimeVRBridge.Trackers.Count})", EditorStyles.boldLabel);
            foreach (var tracker in BasisSlimeVRBridge.Trackers)
            {
                string name = !string.IsNullOrEmpty(tracker.CustomName) ? tracker.CustomName
                    : !string.IsNullOrEmpty(tracker.DisplayName) ? tracker.DisplayName
                    : tracker.DeviceName ?? "(unnamed)";
                string details = $"{tracker.BodyPart} | {tracker.Status}";
                if (tracker.IsSynthetic)
                {
                    details += " | synthetic";
                }
                if (tracker.IsHmd)
                {
                    details += " | HMD";
                }
                if (tracker.HasBattery)
                {
                    details += $" | {tracker.BatteryPercent:F0}% ({tracker.BatteryVoltage:F2}V)";
                }
                if (tracker.HasRssi)
                {
                    details += $" | {tracker.RssiDbm} dBm";
                }
                EditorGUILayout.LabelField(name, details);
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Config"))
            {
                BasisSlimeVRBridge.RefreshBodyMeasurements();
            }
            if (GUILayout.Button("Yaw Reset"))
            {
                BasisSlimeVRBridge.TriggerYawReset();
            }
            if (GUILayout.Button("Full Reset"))
            {
                BasisSlimeVRBridge.TriggerFullReset();
            }
            if (GUILayout.Button("Mounting Reset"))
            {
                BasisSlimeVRBridge.TriggerMountingReset();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
        }
    }
}
#endif
