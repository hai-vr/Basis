using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ IK Clip Eval.
    public class BasisIKClipEvalWindow : EditorWindow
    {
        GameObject _model;
        readonly List<AnimationClip> _clips = new List<AnimationClip>();
        int _fps = 30;
        BasisIKHintMode _hint = BasisIKHintMode.None;
        bool _solveShoulders = true;
        string _path;
        BasisIKClipEvalSummary _last;
        bool _hasResult;
        Vector2 _scroll;

        [MenuItem("Basis/Debug/IK/Clip Eval")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisIKClipEvalWindow>("IK Clip Eval");
            w.minSize = new Vector2(420, 560);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisIKClipEval.DefaultPath();
            if (_clips.Count == 0) _clips.Add(null);
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Samples humanoid clips and solves the extracted limb cores on a real skeleton, " +
                "logging solved-vs-truth elbow/knee per frame. Edit-mode, deterministic.\n" +
                "Sanity check: set Hint Mode = Truth — mean elbow error should be near 0 (validates wiring).",
                MessageType.Info);

            _model = (GameObject)EditorGUILayout.ObjectField("Humanoid Model", _model, typeof(GameObject), false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Clips", EditorStyles.boldLabel);
            int remove = -1;
            for (int i = 0; i < _clips.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                _clips[i] = (AnimationClip)EditorGUILayout.ObjectField(_clips[i], typeof(AnimationClip), false);
                if (GUILayout.Button("−", GUILayout.Width(24))) remove = i;
                EditorGUILayout.EndHorizontal();
            }
            if (remove >= 0) _clips.RemoveAt(remove);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add Clip")) _clips.Add(null);
            if (GUILayout.Button("Clear")) { _clips.Clear(); _clips.Add(null); }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            _fps = EditorGUILayout.IntField("Sample FPS", _fps);
            _hint = (BasisIKHintMode)EditorGUILayout.EnumPopup("Hint Mode", _hint);
            _solveShoulders = EditorGUILayout.Toggle("Solve Shoulders", _solveShoulders);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("IK Clip Eval CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_model == null))
            {
                if (GUILayout.Button("Run Eval", GUILayout.Height(32)))
                {
                    var cfg = new BasisIKClipEvalConfig
                    {
                        Model = _model,
                        Clips = new List<AnimationClip>(_clips),
                        Fps = _fps,
                        HintMode = _hint,
                        SolveShoulders = _solveShoulders,
                    };
                    _last = BasisIKClipEval.Run(cfg, _path);
                    _hasResult = true;
                    if (_last.Ok) Debug.Log($"[IKClipEval] {_last.Rows} rows ({_last.Frames} frames) -> {_last.Path}");
                    else Debug.LogError($"[IKClipEval] failed: {_last.Error}");
                }
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    EditorGUILayout.HelpBox(
                        $"{_last.Rows} rows, {_last.Frames} frames.\n" +
                        $"Elbow: mean pos err {_last.MeanElbowErrM * 100f:F1} cm, swivel err {_last.MeanElbowSwivelErrDeg:F1}°\n" +
                        $"Knee:  mean pos err {_last.MeanKneeErrM * 100f:F1} cm, swivel err {_last.MeanKneeSwivelErrDeg:F1}°\n" +
                        _last.Path, MessageType.None);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Reveal CSV")) EditorUtility.RevealInFinder(_last.Path);
                    if (GUILayout.Button("Copy Path")) EditorGUIUtility.systemCopyBuffer = _last.Path;
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    EditorGUILayout.HelpBox("Eval failed: " + _last.Error, MessageType.Error);
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
