using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes an <see cref="AnimationClip"/>'s rotation curves into a <see cref="BasisMotionClip"/> for a
/// <c>Sequence</c> movement. Samples the clip at a fixed rate onto an in-scene root (via
/// <see cref="AnimationMode"/>, which restores the pose afterwards) and records each animated bone's
/// absolute local rotation. Paths are stored relative to the chosen root, so set the same root on the
/// Sequence movement. Rotation only in v1 — position/scale curves are ignored.
/// </summary>
public class BasisMotionClipBaker : EditorWindow
{
    [SerializeField] private AnimationClip _clip;
    [SerializeField] private GameObject _root;
    [SerializeField] private float _frameRate = 60f;

    [MenuItem("Basis/Build/Bake Authored Motion Clip", false, 320)]
    private static void Open() => GetWindow<BasisMotionClipBaker>(true, "Bake Authored Motion Clip");

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "Samples an AnimationClip's rotation curves into a BasisMotionClip. Paths are baked relative " +
            "to Root (the avatar the clip was authored on) — set the same Root on the Sequence movement.",
            MessageType.Info);

        _clip = (AnimationClip)EditorGUILayout.ObjectField("Animation Clip", _clip, typeof(AnimationClip), false);
        _root = (GameObject)EditorGUILayout.ObjectField("Root (in scene)", _root, typeof(GameObject), true);
        _frameRate = EditorGUILayout.FloatField("Frame Rate", _frameRate);

        using (new EditorGUI.DisabledScope(_clip == null || _root == null || _frameRate <= 0f))
        {
            if (BasisEditorUI.PrimaryButton("Bake", 28f)) Bake();
        }
    }

    private void Bake()
    {
        // Distinct rotation-animated paths, in binding order.
        var paths = new List<string>();
        var seen = new HashSet<string>();
        foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(_clip))
        {
            if (b.type != typeof(Transform)) continue;
            if (!b.propertyName.StartsWith("m_LocalRotation") && !b.propertyName.StartsWith("localEulerAngles")) continue;
            if (seen.Add(b.path)) paths.Add(b.path);
        }
        if (paths.Count == 0)
        {
            EditorUtility.DisplayDialog("Bake Authored Motion Clip", "The clip animates no transform rotations.", "OK");
            return;
        }

        // Resolve each path under the root; drop any that aren't present.
        var keepPaths = new List<string>();
        var keepTransforms = new List<Transform>();
        for (int i = 0; i < paths.Count; i++)
        {
            Transform tf = string.IsNullOrEmpty(paths[i]) ? _root.transform : _root.transform.Find(paths[i]);
            if (tf == null) { BasisDebug.LogWarning($"[BasisMotionClipBaker] Path not under root, skipping: {paths[i]}", BasisDebug.LogTag.AuthoredMotion); continue; }
            keepPaths.Add(paths[i]);
            keepTransforms.Add(tf);
        }
        if (keepTransforms.Count == 0)
        {
            EditorUtility.DisplayDialog("Bake Authored Motion Clip",
                "No animated paths resolved under Root. Is Root the avatar the clip was authored on?", "OK");
            return;
        }

        int transformCount = keepTransforms.Count;
        int frameCount = Mathf.Max(1, Mathf.CeilToInt(_clip.length * _frameRate));
        var rotation = new Vector4[transformCount * frameCount];

        AnimationMode.StartAnimationMode();
        try
        {
            for (int f = 0; f < frameCount; f++)
            {
                float time = frameCount > 1 ? f / _frameRate : 0f;
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(_root, _clip, time);
                AnimationMode.EndSampling();

                for (int k = 0; k < transformCount; k++)
                {
                    Quaternion q = keepTransforms[k].localRotation;
                    rotation[k * frameCount + f] = new Vector4(q.x, q.y, q.z, q.w);
                }
            }
        }
        finally
        {
            AnimationMode.StopAnimationMode();
        }

        string assetPath = EditorUtility.SaveFilePanelInProject(
            "Save Motion Clip", _clip.name + " Motion", "asset", "Choose where to save the baked BasisMotionClip.");
        if (string.IsNullOrEmpty(assetPath)) return;

        var motion = CreateInstance<BasisMotionClip>();
        motion.frameRate = _frameRate;
        motion.frameCount = frameCount;
        motion.transformCount = transformCount;
        motion.paths = keepPaths.ToArray();
        motion.rotationSamples = rotation;
        AssetDatabase.CreateAsset(motion, assetPath);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(motion);
        BasisDebug.Log($"[BasisMotionClipBaker] Baked {transformCount} transform(s) × {frameCount} frame(s) → {assetPath}", BasisDebug.LogTag.AuthoredMotion);
    }
}
