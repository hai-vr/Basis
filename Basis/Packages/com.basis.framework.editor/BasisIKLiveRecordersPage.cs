using System;
using Basis.Scripts.Drivers;
using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    /// <summary>
    /// The play-mode IK recorders, in one place.
    ///
    /// Knee swivel, leg crouch and foot rotation each used to own a pair of "Start (record)" /
    /// "Stop + Dump CSV" menu entries — six entries for three recorders that behave identically.
    /// They are one list here: enter Play, start the one you need, reproduce the misbehaviour, stop.
    /// Each dumps a CSV next to the other sweep output and logs a PASS/FAIL summary.
    /// </summary>
    public class BasisIKLiveRecordersPage : BasisIKSweepPage
    {
        public override string Group => "Recorders";
        public override string Title => "Live Recorders";
        public override int Order => 10;
        public override string Description =>
            "Capture what the live rig is actually doing. Enter Play, start a recorder, spend a few seconds " +
            "in the pose where the problem shows, then stop — you get a CSV plus a console summary naming " +
            "the frame and the field that went wrong.";

        private sealed class Recorder
        {
            public string Name;
            public string Hint;
            public Func<bool> IsRecording;
            public Action Start;
            public Action Stop;
        }

        private static readonly Recorder[] Recorders =
        {
            new Recorder
            {
                Name = "Knee Swivel",
                Hint = "Stand, shift weight, take a step, sit — whatever makes the bad knee misbehave. The console " +
                       "prints a LEFT vs RIGHT table and names the asymmetry. Use it when one knee is wrong and the " +
                       "other is not: the solve is mirror-symmetric, so that means the legs are getting different data.",
                IsRecording = () => BasisLegSwivelDebug.Enabled,
                Start = BasisLegSwivelDebug.Start,
                Stop = BasisLegSwivelDebug.StopAndDump,
            },
            new Recorder
            {
                Name = "Leg Crouch",
                Hint = "Crouch up and down a few times, especially with your feet a bit behind you. The summary flags " +
                       "any frame where the knee shot up and includes the live foot-driver knee hint at that frame.",
                IsRecording = () => BasisLegCrouchDebug.Enabled,
                Start = BasisLegCrouchDebug.Start,
                Stop = BasisLegCrouchDebug.StopAndDump,
            },
            new Recorder
            {
                Name = "Foot Rotation",
                Hint = "Walk (foot IK disengages = correct-foot reference) and then stand still (it engages). The " +
                       "summary names which rotation source foot IK should be feeding from.",
                IsRecording = () => BasisFootRotationDebug.Enabled,
                Start = BasisFootRotationDebug.Start,
                Stop = BasisFootRotationDebug.StopAndDump,
            },
        };

        public override void OnInspectorUpdate()
        {
            if (Application.isPlaying) Host.Repaint();
        }

        public override void Draw()
        {
            if (!Application.isPlaying)
            {
                BasisEditorUI.Help("Enter Play mode to record. The recorders read the live rig.", MessageType.Warning);
            }

            foreach (Recorder rec in Recorders)
            {
                bool recording = rec.IsRecording();
                using (BasisEditorUI.Card(rec.Name))
                {
                    BasisEditorUI.PillRow("State", recording ? "RECORDING" : "idle",
                        recording ? BasisEditorUI.State.Bad : BasisEditorUI.State.Neutral);
                    BasisEditorUI.Note(rec.Hint);
                    GUILayout.Space(4f);

                    EditorGUILayout.BeginHorizontal();
                    using (new EditorGUI.DisabledScope(!Application.isPlaying || recording))
                    {
                        if (BasisEditorUI.PrimaryButton("Start Recording", 26f))
                        {
                            rec.Start();
                            Debug.Log($"[IK Recorders] {rec.Name} recording — reproduce the problem, then stop.");
                        }
                    }
                    using (new EditorGUI.DisabledScope(!recording))
                    {
                        if (BasisEditorUI.SecondaryButton("Stop + Dump CSV", 26f)) rec.Stop();
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            using (BasisEditorUI.Card("Output"))
            {
                BasisEditorUI.Row("Folder", Application.persistentDataPath);
                if (BasisEditorUI.SecondaryButton("Reveal Output Folder"))
                {
                    EditorUtility.RevealInFinder(Application.persistentDataPath);
                }
            }
        }
    }
}
