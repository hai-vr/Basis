using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Centralized per-avatar-model runtime cache. Data that is deterministic for a given
/// humanoid Avatar asset (T-pose rotations, hand pose grids, calibration coords) is
/// stored here so that loading the same avatar a second time — whether for the local
/// player switching back, or multiple remote players sharing the same model — skips
/// the expensive bake/capture work and copies from cache instead.
///
/// Cache key: <c>Animator.avatar.GetInstanceID()</c> — unique per loaded Avatar asset,
/// stable across instances of the same model within a session.
///
/// Subsystems store their data in typed slots on <see cref="Entry"/>. A null slot means
/// "not yet cached"; subsystems populate their slot after the first computation and
/// read from it on subsequent loads.
/// </summary>
public static class BasisAvatarModelCache
{
    /// <summary>
    /// Per-avatar-model cache entry. Each subsystem owns a slot.
    /// Slots are classes (reference types) so they can be populated independently.
    /// </summary>
    public class Entry
    {
        /// <summary>Baked finger pose grid data (BasisLocalHandDriver).</summary>
        public HandPoseGridData HandPoseGrid;

        /// <summary>T-pose local rotations for all 55 humanoid bones (BasisTransformMapping.TposeLocal).</summary>
        public TposeLocalData TposeLocal;

        /// <summary>T-pose root-relative coords for all 55 humanoid bones (BasisTransformMapping.TposeFromRoot).</summary>
        public TposeFromRootData TposeFromRoot;
    }

    // ────────────────────────────────────────────────────────────
    //  Slot data types — each subsystem defines what it caches
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Cached hand pose grid: 441 grid cells × 10 fingers × 3 joints = quaternion data,
    /// plus the T-pose muscle reference arrays. Produced by BasisLocalHandDriver.ReInitialize().
    /// </summary>
    public class HandPoseGridData
    {
        public float[] NativeGridSnapshot; // raw quaternion floats (x,y,z,w per element)
        public int GridWidth;
        public int GridHeight;
        public int FingerStride;
        public int TotalElements;

        // T-pose muscle arrays (4 floats per finger, 10 fingers)
        public float[] LeftThumb, LeftIndex, LeftMiddle, LeftRing, LeftLittle;
        public float[] RightThumb, RightIndex, RightMiddle, RightRing, RightLittle;

        // Initial pose recorded from T-pose transforms
        public BasisPoseData InitialPose;
    }

    /// <summary>
    /// Cached T-pose local rotations for all humanoid bones.
    /// Indexed by HumanBodyBones enum value (0..54). Identity for missing bones.
    /// Used by BasisNetworkAvatarCompressor.CaptureTPose() and CaptureReceiverBoneData().
    /// </summary>
    public class TposeLocalData
    {
        /// <summary>Local rotation per bone in T-pose. Length = 55 (HumanBodyBones.LastBone).</summary>
        public quaternion[] Rotations;

        /// <summary>Local position per bone in T-pose. Length = 55.</summary>
        public float3[] Positions;
    }

    /// <summary>
    /// Cached T-pose root-relative coordinates for all humanoid bones.
    /// Used by IK calibration and remote bone job authoring.
    /// </summary>
    public class TposeFromRootData
    {
        /// <summary>Position relative to animator root, per bone. Length = 55.</summary>
        public float3[] Positions;

        /// <summary>Rotation relative to animator root, per bone. Length = 55.</summary>
        public quaternion[] Rotations;

        /// <summary>Computed avatar forward/up/right from T-pose geometry.</summary>
        public float3 AvatarForward;
        public float3 AvatarUp;
        public float3 AvatarRight;
    }

    // ────────────────────────────────────────────────────────────
    //  Storage
    // ────────────────────────────────────────────────────────────

    private static readonly Dictionary<int, Entry> _cache = new Dictionary<int, Entry>(16);

    /// <summary>
    /// Gets the cache key for an animator's avatar asset.
    /// Returns 0 if the avatar is null (caller should skip caching).
    /// </summary>
    public static int GetKey(Animator animator)
    {
        return animator != null && animator.avatar != null ? animator.avatar.GetInstanceID() : 0;
    }

    /// <summary>
    /// Gets or creates the cache entry for the given avatar asset key.
    /// </summary>
    public static Entry GetOrCreate(int key)
    {
        if (!_cache.TryGetValue(key, out Entry entry))
        {
            entry = new Entry();
            _cache[key] = entry;
        }
        return entry;
    }

    /// <summary>
    /// Tries to get an existing cache entry. Returns false if not cached.
    /// </summary>
    public static bool TryGet(int key, out Entry entry)
    {
        return _cache.TryGetValue(key, out entry);
    }

    /// <summary>
    /// Removes a specific avatar's cache entry (e.g., when its bundle is unloaded).
    /// </summary>
    public static void Remove(int key)
    {
        _cache.Remove(key);
    }

    /// <summary>Number of cached avatar models.</summary>
    public static int Count => _cache.Count;

    /// <summary>
    /// Clears all cached data. Called on domain reload.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void Clear()
    {
        _cache.Clear();
    }
}
