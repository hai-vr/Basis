using System.Text;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using UnityEngine;

/// <summary>
/// Editor/dev-build diagnostic for the "Invalid AABB" / "IsFinite(distanceForSort)" console
/// spam: those asserts name no object, and by the time they print, the first NaN has already
/// latched (the pose gather reads transforms back, jiggle integrates it), so every later frame
/// is corrupt and the true injector is unfindable. While armed this scans a cheap set every
/// frame (cameras, local avatar root) and every renderer's bounds on a slow cadence, then logs
/// the FIRST non-finite offender with its full ancestor chain — the deepest non-finite
/// ancestor is where the NaN entered — and disarms so the report stays readable.
///
/// Off unless <see cref="Enabled"/> is set — toggle it from Basis/Debug/Finite Watchdog.
/// </summary>
public static class BasisFiniteWatchdog
{
    /// <summary>Master toggle, off by default; the event driver only ticks the scan while this is set.</summary>
    public static bool Enabled;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static bool sDisarmed;
    static float sNextFullSweepTime;

    /// <summary>The renderer sweep walks every renderer's bounds, so it runs on this cadence, not per frame.</summary>
    public static float FullSweepIntervalSeconds = 2f;
    /// <summary>Beyond this a value is reported even when finite — culling breaks on absurd magnitudes too.</summary>
    const float k_AbsurdMagnitude = 1e12f;

    /// <summary>True once a report has fired; re-arm from the debug window to hunt again.</summary>
    public static bool Disarmed => sDisarmed;
    /// <summary>The full text of the last report, for the debug window.</summary>
    public static string LastReport { get; private set; } = string.Empty;

    public static void Rearm()
    {
        sDisarmed = false;
        LastReport = string.Empty;
    }

    public static void Tick()
    {
        if (!Enabled || sDisarmed)
        {
            return;
        }
        try
        {
            if (!IsSane(BasisLocalCameraDriver.Position))
            {
                Report("BasisLocalCameraDriver.Position (camera static)", null, BasisLocalCameraDriver.Position);
                return;
            }

            Camera[] cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                Camera cam = cameras[i];
                if (cam != null && !IsSane(cam.transform.position))
                {
                    Report($"Camera '{cam.name}' transform", cam.transform, cam.transform.position);
                    return;
                }
            }

            if (BasisLocalPlayer.PlayerReady && BasisLocalPlayer.Instance != null)
            {
                var avatar = BasisLocalPlayer.Instance.BasisAvatar;
                if (avatar != null && avatar.transform != null && !IsSane(avatar.transform.position))
                {
                    Report("local avatar root", avatar.transform, avatar.transform.position);
                    return;
                }
            }

            if (Time.unscaledTime < sNextFullSweepTime)
            {
                return;
            }
            sNextFullSweepTime = Time.unscaledTime + Mathf.Max(0.25f, FullSweepIntervalSeconds);

            Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude);
            Renderer first = null;
            int offenderCount = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r == null || !r.enabled)
                {
                    continue;
                }
                Bounds b = r.bounds;
                if (!IsSane(b.center) || !IsSane(b.extents))
                {
                    offenderCount++;
                    if (first == null)
                    {
                        first = r;
                    }
                }
            }
            if (first != null)
            {
                Bounds bounds = first.bounds;
                var detail = new StringBuilder(512);
                detail.Append($"bounds center={bounds.center} extents={bounds.extents}");
                if (first is SkinnedMeshRenderer skinned)
                {
                    DescribeSkinned(skinned, detail);
                }
                Report($"{first.GetType().Name} '{first.name}' ({offenderCount} non-finite renderer(s) this sweep). {detail}", first.transform, bounds.center);
            }
        }
        catch (System.Exception e)
        {
            sDisarmed = true;
            BasisDebug.LogError($"[FiniteWatchdog] scan threw and disarmed: {e}", BasisDebug.LogTag.Core);
        }
    }

    static bool IsSane(Vector3 v)
    {
        return float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)
            && Mathf.Abs(v.x) < k_AbsurdMagnitude && Mathf.Abs(v.y) < k_AbsurdMagnitude && Mathf.Abs(v.z) < k_AbsurdMagnitude;
    }

    /// <summary>
    /// NaN world bounds with a finite root bone + finite ancestors means the bounds came from
    /// the skinned vertex path — the culprit is a skeleton bone outside the ancestor chain, a
    /// blendshape weight, or the mesh data itself. Enumerate exactly which, so the report names
    /// the system that wrote it (jiggle/finger/eye bone names vs viseme/blink weight indices).
    /// </summary>
    static void DescribeSkinned(SkinnedMeshRenderer skinned, StringBuilder detail)
    {
        Transform rootBone = skinned.rootBone;
        detail.Append(rootBone != null
            ? $" rootBone='{rootBone.name}' rootBonePos={rootBone.position} rootBoneScale={rootBone.lossyScale}"
            : " rootBone=<null>");
        detail.Append($" localBounds center={skinned.localBounds.center} extents={skinned.localBounds.extents}");
        detail.Append($" updateWhenOffscreen={skinned.updateWhenOffscreen}");

        Transform[] bones = skinned.bones;
        int badBones = 0;
        for (int i = 0; i < bones.Length; i++)
        {
            Transform bone = bones[i];
            if (bone == null)
            {
                detail.Append($"\n  BONE[{i}] <destroyed>");
                badBones++;
                continue;
            }
            Vector3 wp = bone.position;
            Vector3 ls = bone.localScale;
            Quaternion lr = bone.localRotation;
            bool ok = IsSane(wp) && IsSane(ls)
                && float.IsFinite(lr.x) && float.IsFinite(lr.y) && float.IsFinite(lr.z) && float.IsFinite(lr.w);
            if (!ok && badBones < 12)
            {
                detail.Append($"\n  BONE[{i}] BAD '{bone.name}' worldPos={wp} localRot=({lr.x:F3},{lr.y:F3},{lr.z:F3},{lr.w:F3}) localScale={ls}");
                for (Transform walk = bone.parent; walk != null; walk = walk.parent)
                {
                    Vector3 plp = walk.localPosition;
                    Quaternion plr = walk.localRotation;
                    bool pOk = IsSane(plp) && float.IsFinite(plr.x) && float.IsFinite(plr.y) && float.IsFinite(plr.z) && float.IsFinite(plr.w) && IsSane(walk.localScale);
                    if (!pOk)
                    {
                        detail.Append($" ← parent BAD '{walk.name}' localPos={plp}");
                    }
                }
            }
            if (!ok)
            {
                badBones++;
            }
        }
        detail.Append($"\n  skeleton: {badBones}/{bones.Length} bones non-finite or destroyed");

        Mesh mesh = skinned.sharedMesh;
        if (mesh != null && mesh.blendShapeCount > 0)
        {
            int badWeights = 0;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                float w = skinned.GetBlendShapeWeight(i);
                if (!float.IsFinite(w) || Mathf.Abs(w) > k_AbsurdMagnitude)
                {
                    if (badWeights < 8)
                    {
                        detail.Append($"\n  BLENDSHAPE[{i}] BAD '{mesh.GetBlendShapeName(i)}' weight={w}");
                    }
                    badWeights++;
                }
            }
            detail.Append($"\n  blendshapes: {badWeights}/{mesh.blendShapeCount} weights non-finite");
        }
        detail.Append("\n  (0 bad bones AND 0 bad weights with NaN bounds ⇒ the shared mesh's own vertex data is NaN)");
    }

    static void Report(string what, Transform offender, Vector3 value)
    {
        sDisarmed = true;
        var sb = new StringBuilder(512);
        sb.AppendLine($"[FiniteWatchdog] FIRST non-finite/absurd value detected: {what} = {value}. Watchdog disarmed — everything after this frame is downstream corruption, THIS is the injection site.");
        if (offender != null)
        {
            sb.AppendLine("Ancestor chain (deepest non-finite ancestor is where the NaN entered):");
            for (Transform walk = offender; walk != null; walk = walk.parent)
            {
                Vector3 lp = walk.localPosition;
                Quaternion lr = walk.localRotation;
                Vector3 ls = walk.localScale;
                bool ok = IsSane(lp)
                    && float.IsFinite(lr.x) && float.IsFinite(lr.y) && float.IsFinite(lr.z) && float.IsFinite(lr.w)
                    && IsSane(ls);
                sb.AppendLine($"  {(ok ? "ok " : "BAD")} '{walk.name}' localPos={lp} localRot=({lr.x:F3},{lr.y:F3},{lr.z:F3},{lr.w:F3}) localScale={ls}");
            }
        }
        LastReport = sb.ToString();
        BasisDebug.LogError(LastReport, BasisDebug.LogTag.Core);
    }
#else
    public static void Tick()
    {
    }
#endif
}
