using UnityEditor;
using UnityEngine;

/// <summary>
/// Arms <see cref="BasisFiniteWatchdog"/> and shows its report. The watchdog names the FIRST
/// object whose transform/bounds went non-finite — the injection site behind an
/// "Invalid AABB" / "IsFinite(distanceForSort)" console storm — then disarms itself.
/// </summary>
public class BasisFiniteWatchdogWindow : EditorWindow
{
    Vector2 _scroll;

    [MenuItem("Basis/Debug/Finite Watchdog", false, 623)]
    static void Open()
    {
        var window = GetWindow<BasisFiniteWatchdogWindow>("Finite Watchdog");
        window.minSize = new Vector2(420f, 220f);
    }

    void OnGUI()
    {
        BasisEditorUI.Header("Finite Watchdog",
            "Hunts the first non-finite value behind Invalid AABB / IsFinite spam.");

        EditorGUILayout.HelpBox(
            "Hunts the first NaN behind 'Invalid AABB' / 'IsFinite(distanceForSort)' spam. " +
            "While armed: cameras + local avatar root are checked every frame, every renderer's " +
            "bounds on the sweep cadence. The first hit logs one [FiniteWatchdog] error naming " +
            "the object and its ancestor chain, then the watchdog disarms.",
            MessageType.Info);

        BasisFiniteWatchdog.Enabled = EditorGUILayout.ToggleLeft("Enabled (scans while in play mode)", BasisFiniteWatchdog.Enabled);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        BasisFiniteWatchdog.FullSweepIntervalSeconds = EditorGUILayout.Slider(
            new GUIContent("Renderer sweep interval (s)", "How often the all-renderers bounds sweep runs. The per-frame camera/root checks are free."),
            BasisFiniteWatchdog.FullSweepIntervalSeconds, 0.25f, 10f);

        using (new EditorGUILayout.HorizontalScope())
        {
            string state = !BasisFiniteWatchdog.Enabled ? "Off"
                : BasisFiniteWatchdog.Disarmed ? "TRIPPED — report below"
                : Application.isPlaying ? "Armed, scanning" : "Armed, waiting for play mode";
            EditorGUILayout.LabelField("State", state);
            if (BasisFiniteWatchdog.Disarmed && GUILayout.Button("Re-arm", GUILayout.Width(80f)))
            {
                BasisFiniteWatchdog.Rearm();
            }
        }

        if (!string.IsNullOrEmpty(BasisFiniteWatchdog.LastReport))
        {
            BasisEditorUI.SectionTitle("Last report");
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(BasisFiniteWatchdog.LastReport, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Copy report to clipboard"))
            {
                EditorGUIUtility.systemCopyBuffer = BasisFiniteWatchdog.LastReport;
            }
        }
#endif
    }

    void OnInspectorUpdate()
    {
        Repaint();
    }
}
