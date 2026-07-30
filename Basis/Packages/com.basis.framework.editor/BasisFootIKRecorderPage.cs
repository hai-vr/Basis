using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // frame to CSV. Play-mode only (the foot driver runs on the live local player).
    public class BasisFootIKRecorderPage : BasisIKSweepPage
    {
        public override string Group => "Recorders";
        public override string Title => "Foot IK Recorder";
        public override int Order => 20;
        public override string Description =>
            "Records the procedural foot-placement system per frame: each foot's plant/step phase, " +
            "positions, knee hint, and planted-foot SLIDE (skating). Enter play mode, move around, " +
            "then Start. The headline metric is total planted slide.";

        BasisFootIKDiagnostics _rec;

        public override void OnInspectorUpdate() { Host.Repaint(); }

        BasisFootIKDiagnostics Find()
        {
            if (_rec == null)
            {
                _rec = Object.FindAnyObjectByType<BasisFootIKDiagnostics>();
            }
            return _rec;
        }

        public override void Draw()
        {
            if (!Application.isPlaying)
            {
                BasisEditorUI.Help("Enter play mode and load a moving avatar to record.", MessageType.Warning);
                return;
            }

            var rec = Find();
            if (rec == null)
            {
                if (BasisEditorUI.PrimaryButton("Create Recorder In Scene", 30f))
                {
                    var go = new GameObject("BasisFootIKRecorder");
                    rec = go.AddComponent<BasisFootIKDiagnostics>();
                    rec.AutoStart = false;
                    _rec = rec;
                }
                return;
            }

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Status");
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
                if (BasisEditorUI.SecondaryButton("Reveal CSV"))
                {
                    EditorUtility.RevealInFinder(rec.ResolvedLogPath);
                }
            }
        }
    }
}
