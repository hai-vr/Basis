using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ IK ▸ Foot IK Recorder. Records the procedural foot-placement state per
    // frame to CSV. Play-mode only (the foot driver runs on the live local player).
    public class BasisFootIKRecorderWindow : EditorWindow
    {
        BasisFootIKDiagnostics _rec;

        [MenuItem("Basis/Debug/IK/Foot IK Recorder")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisFootIKRecorderWindow>("Foot IK Recorder");
            w.minSize = new Vector2(360, 320);
        }

        void OnInspectorUpdate() { Repaint(); }

        BasisFootIKDiagnostics Find()
        {
            if (_rec == null)
            {
                _rec = Object.FindAnyObjectByType<BasisFootIKDiagnostics>();
            }
            return _rec;
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Records the procedural foot-placement system per frame: each foot's plant/step phase, " +
                "positions, knee hint, and planted-foot SLIDE (skating). Enter play mode, move around, " +
                "then Start. The headline metric is total planted slide.", MessageType.Info);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter play mode and load a moving avatar to record.", MessageType.Warning);
                return;
            }

            var rec = Find();
            if (rec == null)
            {
                if (GUILayout.Button("Create Recorder In Scene", GUILayout.Height(30)))
                {
                    var go = new GameObject("BasisFootIKRecorder");
                    rec = go.AddComponent<BasisFootIKDiagnostics>();
                    rec.AutoStart = false;
                    _rec = rec;
                }
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Logging", rec.IsLogging ? "YES" : "no");
            EditorGUILayout.LabelField("Rows written", rec.SnapshotsWritten.ToString());
            EditorGUILayout.LabelField("Total planted slide", (rec.TotalPlantedSlideM * 100f).ToString("F1") + " cm");
            EditorGUILayout.LabelField("Steps (L / R)", rec.LeftSteps + " / " + rec.RightSteps);
            if (!string.IsNullOrEmpty(rec.ResolvedLogPath))
            {
                EditorGUILayout.LabelField("Path", rec.ResolvedLogPath);
            }

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(rec.IsLogging))
            {
                if (GUILayout.Button("Start")) rec.StartLogging();
            }
            using (new EditorGUI.DisabledScope(!rec.IsLogging))
            {
                if (GUILayout.Button("Stop")) rec.StopLogging();
                if (GUILayout.Button("Flush")) rec.Flush();
            }
            EditorGUILayout.EndHorizontal();

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(rec.ResolvedLogPath)))
            {
                if (GUILayout.Button("Reveal CSV"))
                {
                    EditorUtility.RevealInFinder(rec.ResolvedLogPath);
                }
            }
        }
    }
}
