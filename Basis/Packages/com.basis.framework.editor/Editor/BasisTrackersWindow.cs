using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices.Simulation;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Simulated trackers, in one window.
///
/// Spawning a test rig used to mean walking a menu of fourteen entries — nine "Create …" presets
/// plus show/hide/destroy — with no indication of which needed play mode or what each preset
/// actually spawns. They are buttons here, grouped by what you are doing: pick a rig, tweak
/// visibility, tear it down.
/// </summary>
public sealed class BasisTrackersWindow : EditorWindow
{
    private string _physicalName = "oculus_quest_pro_controller_left";
    private string _uniqueName = "oculus_quest_pro_controller_left";
    private bool _hasRole;
    private BasisBoneTrackedRole _role;
    private Vector2 _scroll;

    [MenuItem("Basis/Trackers/Simulated Trackers", false, 160)]
    public static void ShowWindow()
    {
        BasisTrackersWindow w = GetWindow<BasisTrackersWindow>();
        w.titleContent = new GUIContent("Trackers");
        w.minSize = new Vector2(420, 460);
        w.Show();
    }

    private void OnGUI()
    {
        _scroll = BasisEditorUI.BeginPage(_scroll);
        BasisEditorUI.Header("Simulated Trackers",
            "Spawn fake XR devices against the local player so full-body paths can be exercised without hardware.");

        if (!Application.isPlaying)
        {
            BasisEditorUI.Help("Enter Play mode — every action here drives the live local player.", MessageType.Warning);
        }

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            using (BasisEditorUI.Card("Rig Presets"))
            {
                BasisEditorUI.Note("Full Set / 3 Point / Max also T-pose the avatar, so run Full Body calibration afterwards.");
                GUILayout.Space(4f);

                Preset("Full Set (hands + body pucks)", BasisMenuItemsEditor.CreateFullSet);
                Preset("3 Point (head + hands)", BasisMenuItemsEditor.CreatePuck3Tracker);
                Preset("Max (every supported role)", BasisMenuItemsEditor.CreateFullMaxTracker);
                Preset("Max, Normal Positions", BasisMenuItemsEditor.CreateFullMaxTrackerUnModifedPos);
            }

            using (BasisEditorUI.Card("Single Devices"))
            {
                Preset("Both Hands (Index controllers)", BasisMenuItemsEditor.CreateLRTracker);
                Preset("Left Controller", BasisMenuItemsEditor.CreateViveLeftTracker);
                Preset("Right Controller", BasisMenuItemsEditor.CreateViveRightTracker);
                Preset("Vive Puck (unroled)", BasisMenuItemsEditor.CreatePuckTracker);
                Preset("Unknown Device", BasisMenuItemsEditor.CreateUnknowonTracker);
            }

            using (BasisEditorUI.Card("Custom Device"))
            {
                BasisEditorUI.Note("Spawn a device by its OpenVR/OpenXR model name — useful for reproducing a specific user's hardware.");
                _physicalName = EditorGUILayout.TextField("Physical Name", _physicalName);
                _uniqueName = EditorGUILayout.TextField("Unique ID", _uniqueName);
                _hasRole = EditorGUILayout.Toggle("Has Default Role", _hasRole);
                using (new EditorGUI.DisabledScope(!_hasRole))
                {
                    _role = (BasisBoneTrackedRole)EditorGUILayout.EnumPopup("Role", _role);
                }

                GUILayout.Space(4f);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_physicalName) || string.IsNullOrWhiteSpace(_uniqueName)))
                {
                    if (BasisEditorUI.PrimaryButton("Create Device"))
                    {
                        BasisSimulateXR sim = BasisMenuItemsEditor.FindSimulate();
                        if (sim == null)
                        {
                            Debug.LogWarning("[Trackers] No SimulateXR management is active — nothing to spawn into.");
                        }
                        else
                        {
                            if (_hasRole) sim.CreatePhysicalTrackedDevice(_uniqueName, _physicalName, _role, true);
                            else sim.CreatePhysicalTrackedDevice(_uniqueName, _physicalName);
                            BasisDeviceManagement.VisibleTrackers(true);
                        }
                    }
                }
            }

            using (BasisEditorUI.Card("Visibility"))
            {
                EditorGUILayout.BeginHorizontal();
                if (BasisEditorUI.SecondaryButton("Show Trackers")) BasisMenuItemsEditor.ShowTrackersEditor();
                if (BasisEditorUI.SecondaryButton("Hide Trackers")) BasisMenuItemsEditor.HideTrackersEditor();
                EditorGUILayout.EndHorizontal();
            }

            using (BasisEditorUI.Card("Teardown"))
            {
                BasisEditorUI.Note("Destroy And Restore re-creates every previously connected device as a Vive puck — the quickest way back to a clean calibration.");
                EditorGUILayout.BeginHorizontal();
                if (BasisEditorUI.SecondaryButton("Destroy All")) BasisMenuItemsEditor.DestroyXRInput();
                if (BasisEditorUI.SecondaryButton("Destroy And Restore")) BasisMenuItemsEditor.DestroyAndRebuildXRInput();
                EditorGUILayout.EndHorizontal();
            }
        }

        BasisEditorUI.EndPage();
    }

    private static void Preset(string label, System.Action action)
    {
        if (BasisEditorUI.SecondaryButton(label, 24f)) action();
    }
}
