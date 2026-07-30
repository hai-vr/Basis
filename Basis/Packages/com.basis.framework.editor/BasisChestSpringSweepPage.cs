using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    public class BasisChestSpringSweepPage : BasisIKSweepPage
    {
        public override string Group => "Spine & Torso";
        public override string Title => "Chest Spring Sweep";
        public override int Order => 70;
        public override string Description =>
            "Stability sweep of the chest-follow spring across hz x damping x fps (incl. low fps where " +
            "explicit Euler explodes). Proves the implicit step never diverges, well-damped configs settle, " +
            "and over-damped configs do not overshoot. Same integrator step as the live ApplyChestSpring.";

        BasisChestSpringSweepConfig _cfg = BasisChestSpringSweepConfig.Default();
        string _path;
        BasisChestSpringSweepSummary _last;
        bool _hasResult;

        public override void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisChestSpringSweep.DefaultPath();
        }

        public override void Draw()
        {
            BasisEditorUI.SectionTitle("Configuration");
            _cfg.SettleSeconds = EditorGUILayout.Slider("Settle Time (s)", _cfg.SettleSeconds, 0.5f, 8f);

            EditorGUILayout.Space();
            BasisEditorUI.SectionTitle("Output");
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Chest Spring Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (BasisEditorUI.PrimaryButton("Run Sweep", 32f))
            {
                _last = BasisChestSpringSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[ChestSpringSweep] {_last.Configs} configs -> {_last.Path}");
                else Debug.LogError($"[ChestSpringSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    var g = BasisIKTestGates.GateChestSpring(_last);
                    BasisEditorUI.PassFail(g.pass, g.reason);
                    BasisEditorUI.Readout(
                        $"configs={_last.Configs} diverged={_last.DivergedCount} (explicit Euler would: {_last.ExplicitDivergedCount})\n" +
                        $"settleErr={_last.MaxFinalErrSettling:F3} overdampedOvershoot={_last.MaxOverdampedOvershoot:F3} maxAbs={_last.MaxAbsPos:F2}\n" +
                        _last.Path);
                    if (BasisEditorUI.SecondaryButton("Reveal CSV")) EditorUtility.RevealInFinder(_last.Path);
                }
                else
                {
                    BasisEditorUI.Help("Sweep failed: " + _last.Error, MessageType.Error);
                }
            }

        }
    }
}
