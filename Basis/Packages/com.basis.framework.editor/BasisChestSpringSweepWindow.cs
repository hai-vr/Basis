using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ IK ▸ Chest Spring Sweep.
    public class BasisChestSpringSweepWindow : EditorWindow
    {
        BasisChestSpringSweepConfig _cfg = BasisChestSpringSweepConfig.Default();
        string _path;
        BasisChestSpringSweepSummary _last;
        bool _hasResult;
        Vector2 _scroll;

        [MenuItem("Basis/Debug/IK/Chest Spring Sweep")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisChestSpringSweepWindow>("Chest Spring Sweep");
            w.minSize = new Vector2(360, 380);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisChestSpringSweep.DefaultPath();
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Stability sweep of the chest-follow spring across hz x damping x fps (incl. low fps where " +
                "explicit Euler explodes). Proves the implicit step never diverges, well-damped configs settle, " +
                "and over-damped configs do not overshoot. Same integrator step as the live ApplyChestSpring.",
                MessageType.Info);

            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            _cfg.SettleSeconds = EditorGUILayout.Slider("Settle Time (s)", _cfg.SettleSeconds, 0.5f, 8f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
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
            if (GUILayout.Button("Run Sweep", GUILayout.Height(32)))
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
                    EditorGUILayout.HelpBox(
                        (g.pass ? "PASS  " : "FAIL  ") + g.reason + "\n" +
                        $"configs={_last.Configs} diverged={_last.DivergedCount} (explicit Euler would: {_last.ExplicitDivergedCount})\n" +
                        $"settleErr={_last.MaxFinalErrSettling:F3} overdampedOvershoot={_last.MaxOverdampedOvershoot:F3} maxAbs={_last.MaxAbsPos:F2}\n" +
                        _last.Path,
                        g.pass ? MessageType.Info : MessageType.Error);
                    if (GUILayout.Button("Reveal CSV")) EditorUtility.RevealInFinder(_last.Path);
                }
                else
                {
                    EditorGUILayout.HelpBox("Sweep failed: " + _last.Error, MessageType.Error);
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
