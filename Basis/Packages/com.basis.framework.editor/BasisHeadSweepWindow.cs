using UnityEditor;
using UnityEngine;

namespace Basis.IK.Debugging
{
    // Basis ▸ Debug ▸ IK ▸ Head Sweep.
    public class BasisHeadSweepWindow : EditorWindow
    {
        BasisHeadSweepConfig _cfg = BasisHeadSweepConfig.Default();
        string _path;
        BasisHeadSweepSummary _last;
        bool _hasResult;
        Vector2 _scroll;

        [MenuItem("Basis/Debug/IK/Head Sweep")]
        public static void ShowWindow()
        {
            var w = GetWindow<BasisHeadSweepWindow>("Head Sweep");
            w.minSize = new Vector2(380, 560);
        }

        void OnEnable()
        {
            if (string.IsNullOrEmpty(_path)) _path = BasisHeadSweep.DefaultPath();
        }

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "Sweeps head gaze pitch and records the cervical-lordosis response (neck/upper-chest " +
                "bend, extreme-region hips/chest offsets, head-pitch clamp). Same math as the live rig. " +
                "Defaults match the production FBIK settings.", MessageType.Info);

            EditorGUILayout.LabelField("Sweep", EditorStyles.boldLabel);
            _cfg.PitchMin = EditorGUILayout.FloatField("Pitch Min (deg, +=down)", _cfg.PitchMin);
            _cfg.PitchMax = EditorGUILayout.FloatField("Pitch Max (deg)", _cfg.PitchMax);
            _cfg.PitchSteps = EditorGUILayout.IntField("Pitch Steps", _cfg.PitchSteps);
            _cfg.Yaw = EditorGUILayout.FloatField("Yaw (deg)", _cfg.Yaw);
            _cfg.HasUpperChest = EditorGUILayout.Toggle("Has UpperChest", _cfg.HasUpperChest);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Lordosis Params (FBIK defaults)", EditorStyles.boldLabel);
            _cfg.BaseDeg = EditorGUILayout.FloatField("Base Deg", _cfg.BaseDeg);
            _cfg.PitchGainDeg = EditorGUILayout.FloatField("Pitch Gain Deg", _cfg.PitchGainDeg);
            _cfg.NeckShare = EditorGUILayout.Slider("Neck Share", _cfg.NeckShare, 0f, 1f);
            _cfg.MaxHeadPitchDeg = EditorGUILayout.FloatField("Max Head Pitch Deg", _cfg.MaxHeadPitchDeg);
            _cfg.ExtremeStartDeg = EditorGUILayout.FloatField("Extreme Start Deg", _cfg.ExtremeStartDeg);
            _cfg.ExtremeFullDeg = EditorGUILayout.FloatField("Extreme Full Deg", _cfg.ExtremeFullDeg);
            _cfg.ExtremeRollForwardMaxDeg = EditorGUILayout.FloatField("Extreme Roll Fwd Max", _cfg.ExtremeRollForwardMaxDeg);
            _cfg.ExtremeRollBackwardMaxDeg = EditorGUILayout.FloatField("Extreme Roll Back Max", _cfg.ExtremeRollBackwardMaxDeg);
            _cfg.ExtremeHipsHorizontalMax = EditorGUILayout.FloatField("Extreme Hips Horiz Max", _cfg.ExtremeHipsHorizontalMax);
            _cfg.ExtremeChestHorizontalMax = EditorGUILayout.FloatField("Extreme Chest Horiz Max", _cfg.ExtremeChestHorizontalMax);
            _cfg.ExtremeHipsDownMax = EditorGUILayout.FloatField("Extreme Hips Down Max", _cfg.ExtremeHipsDownMax);
            _cfg.ExtremeChestDownMax = EditorGUILayout.FloatField("Extreme Chest Down Max", _cfg.ExtremeChestDownMax);
            _cfg.ExtremeHipsDownLookUp = EditorGUILayout.FloatField("Extreme Hips Down LookUp", _cfg.ExtremeHipsDownLookUp);
            _cfg.ExtremeChestDownLookUp = EditorGUILayout.FloatField("Extreme Chest Down LookUp", _cfg.ExtremeChestDownLookUp);

            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            _path = EditorGUILayout.TextField("CSV Path", _path);
            if (GUILayout.Button("…", GUILayout.Width(28)))
            {
                string picked = EditorUtility.SaveFilePanel("Head Sweep CSV", System.IO.Path.GetDirectoryName(_path),
                    System.IO.Path.GetFileName(_path), "csv");
                if (!string.IsNullOrEmpty(picked)) _path = picked;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();
            if (GUILayout.Button("Run Sweep", GUILayout.Height(32)))
            {
                _last = BasisHeadSweep.Run(_cfg, _path);
                _hasResult = true;
                if (_last.Ok) Debug.Log($"[HeadSweep] {_last.Rows} rows -> {_last.Path}");
                else Debug.LogError($"[HeadSweep] failed: {_last.Error}");
            }

            if (_hasResult)
            {
                EditorGUILayout.Space();
                if (_last.Ok)
                {
                    EditorGUILayout.HelpBox(
                        $"Wrote {_last.Rows} rows.\n" +
                        $"max neck bend {_last.MaxNeckDeg:F1}°; extreme region onsets at |pitch| {_last.ExtremeOnsetPitch:F0}°; " +
                        $"head clamp engages at |pitch| {_last.ClampOnsetPitch:F0}°\n{_last.Path}", MessageType.None);
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
