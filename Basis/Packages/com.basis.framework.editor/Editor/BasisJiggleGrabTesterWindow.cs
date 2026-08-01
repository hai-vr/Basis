using Basis.Scripts.BasisSdk.Interactions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Controls and live readout for <see cref="BasisJiggleGrabSceneDrag"/> — click-drag jiggle chains
/// in the Scene view while in play mode, so the pull can be tuned without a headset, a second
/// client, or even an avatar (any active JiggleRig in the scene is grabbable).
///
/// The same toggle is appended to every JiggleRig inspector, so this window is optional.
/// </summary>
public sealed class BasisJiggleGrabTesterWindow : EditorWindow
{
    [MenuItem("Basis/Debug/Jiggle Grab Tester", false, 607)]
    public static void ShowWindow()
    {
        BasisJiggleGrabTesterWindow window = GetWindow<BasisJiggleGrabTesterWindow>();
        window.titleContent = new GUIContent("Jiggle Grab");
        window.minSize = new Vector2(360, 320);
        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.update += Tick;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
    }

    private void Tick()
    {
        if (Application.isPlaying)
        {
            Repaint();
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Scene View Drag", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox(
                "Enter play mode to drag — JiggleRig has no ExecuteAlways and the simulation is pumped " +
                "by the event driver, so nothing jiggles in edit mode.",
                MessageType.Info);
        }

        BasisJiggleGrabSceneDrag.Enabled =
            EditorGUILayout.ToggleLeft("Enable scene drag (takes over left click)", BasisJiggleGrabSceneDrag.Enabled);

        EditorGUILayout.HelpBox(
            "While enabled, left click and drag in the Scene view to pull a jiggle chain. Left click no " +
            "longer selects objects; alt-orbit still works. Local only — nothing is networked.",
            MessageType.None);

        BasisJiggleGrabSceneDrag.PickRadius = EditorGUILayout.Slider("Pick Radius (m)",
            BasisJiggleGrabSceneDrag.PickRadius, BasisJiggleGrabSceneDrag.MinRadius, BasisJiggleGrabSceneDrag.MaxRadius);
        BasisJiggleGrabSceneDrag.RayLength = EditorGUILayout.Slider("Ray Length (m)",
            BasisJiggleGrabSceneDrag.RayLength, 1f, 100f);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime", EditorStyles.boldLabel);

        bool master = EditorGUILayout.ToggleLeft("Grabbing enabled (global setting mirror)", BasisJiggleGrabPermissions.MasterEnabled);
        if (master != BasisJiggleGrabPermissions.MasterEnabled)
        {
            BasisJiggleGrabPermissions.MasterEnabled = master;
        }

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.IntField("Announced grabs", BasisJiggleGrabDriver.AnnouncedGrabCount);
            EditorGUILayout.IntField("Applied grabs", BasisJiggleGrabDriver.AppliedGrabCount);
            EditorGUILayout.IntField("Constraints pushed", BasisJiggleGrabDriver.PushedConstraintCount);
            EditorGUILayout.Toggle("Dragging now", BasisJiggleGrabDriver.HasEditorGrab);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Last Grab Press", EditorStyles.boldLabel);
        string attempt = BasisJiggleGrabDriver.LastAttemptResult;
        float age = Application.isPlaying ? Time.unscaledTime - BasisJiggleGrabDriver.LastAttemptTime : 0f;
        EditorGUILayout.HelpBox(
            BasisJiggleGrabDriver.LastAttemptTime > 0f ? $"{attempt}   ({age:0.0}s ago)" : attempt,
            attempt.StartsWith("grabbed") ? MessageType.Info : MessageType.Warning);
        EditorGUILayout.LabelField("Written only when a grab button is freshly pressed — squeeze grip in " +
            "VR, then read this.", EditorStyles.miniLabel);

        EditorGUILayout.Space();
        if (GUILayout.Button("Release Drag"))
        {
            BasisJiggleGrabDriver.EndEditorGrab();
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("In-game controls", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Desktop: aim at a jiggle bone with nothing else under the cursor and hold left mouse.\n" +
            "VR: put a hand on the chain and squeeze grip.\n" +
            "The chain follows the grabbing hand bone, so on desktop it tracks your avatar's hand, " +
            "not the mouse.\n\n" +
            "How far a chain can be pulled is set per rig by Max Grab Stretch, as a multiple of a " +
            "bone's distance from the chain root.",
            MessageType.None);
    }
}
