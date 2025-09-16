using Basis.Scripts.Common;
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs; // TransformAccessArray & IJobParallelForTransform

// ---------------- Bone indices ----------------
public static class BoneIdx
{
    public const int Head = 0;
    public const int Neck = 1;
    public const int Chest = 2;
    public const int Spine = 3;
    public const int Hips = 4;
    public const int CenterEye = 5;
    public const int Mouth = 6;
    public const int BoneCount = 7;
}

// ---------------- SoA structs ----------------
public struct RemoteAuthoring
{
    public float3 tposeLocal_unscaled_Head;
    public float3 tposeLocal_unscaled_Neck;
    public float3 tposeLocal_unscaled_Chest;
    public float3 tposeLocal_unscaled_Spine;
    public float3 tposeLocal_unscaled_Hips;
    public float3 tposeLocal_unscaled_CenterEye;
    public float3 tposeLocal_unscaled_Mouth;

    public float3 offsets_unscaled_Neck;
    public float3 offsets_unscaled_Chest;
    public float3 offsets_unscaled_Spine;      // Chest→Spine in this chain
    public float3 offsets_unscaled_CenterEye;
    public float3 offsets_unscaled_Mouth;
}

public struct RemoteFrameInput
{
    public float3 rootWorld;
    public float3 headWPos;
    public float3 hipsWPos;
    public quaternion headWRot;
    public quaternion hipsWRot;
    public quaternion tposeHeadRot;
    public quaternion tposeHipsRot;
    public float3 nowScale;
}

public struct RemoteScaleCache
{
    public float3 lastScale;
    public float3 tposeLocal_scaled_Hips;
    public float3 tposeLocal_scaled_Mouth;
    public float3 offsets_scaled_Neck;
    public float3 offsets_scaled_Chest;
    public float3 offsets_scaled_Spine;
    public float3 offsets_scaled_CenterEye;
    public float3 offsets_scaled_Mouth;
}

public struct RemoteFrameOutput
{
    public float3 pos_Head, pos_Neck, pos_Chest, pos_Spine, pos_Hips, pos_CenterEye, pos_Mouth;
    public quaternion rot_Head, rot_Neck, rot_Chest, rot_Spine, rot_Hips, rot_CenterEye, rot_Mouth;
    public float diffHipToHeadMouthY;
}

[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
public struct BoneSimJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<RemoteAuthoring> Authoring;
    [ReadOnly] public NativeArray<RemoteFrameInput> In;
    public NativeArray<RemoteScaleCache> ScaleCache;
    public NativeArray<RemoteFrameOutput> Out;

    public void Execute(int i)
    {
        var a = Authoring[i];
        var f = In[i];
        var sc = ScaleCache[i];

        if (!f.nowScale.Equals(sc.lastScale))
        {
            sc.tposeLocal_scaled_Hips = a.tposeLocal_unscaled_Hips * f.nowScale;
            sc.tposeLocal_scaled_Mouth = a.tposeLocal_unscaled_Mouth * f.nowScale;

            sc.offsets_scaled_Neck = a.offsets_unscaled_Neck * f.nowScale;
            sc.offsets_scaled_Chest = a.offsets_unscaled_Chest * f.nowScale;
            sc.offsets_scaled_Spine = a.offsets_unscaled_Spine * f.nowScale;
            sc.offsets_scaled_CenterEye = a.offsets_unscaled_CenterEye * f.nowScale;
            sc.offsets_scaled_Mouth = a.offsets_unscaled_Mouth * f.nowScale;

            sc.lastScale = f.nowScale;
            ScaleCache[i] = sc;
        }

        quaternion headR = math.mul(f.tposeHeadRot, f.headWRot);
        quaternion hipsR = math.mul(f.tposeHipsRot, f.hipsWRot);

        float3 headP = f.headWPos - f.rootWorld;
        float3 hipsP = f.hipsWPos - f.rootWorld;

        float3 neckP = headP + math.mul(headR, sc.offsets_scaled_Neck);
        float3 chestP = neckP + math.mul(headR, sc.offsets_scaled_Chest);
        float3 spineP = chestP + math.mul(headR, sc.offsets_scaled_Spine);
        float3 eyeP = headP + math.mul(headR, sc.offsets_scaled_CenterEye);
        float3 mouthP = headP + math.mul(headR, sc.offsets_scaled_Mouth);

        Out[i] = new RemoteFrameOutput
        {
            pos_Head = headP,
            pos_Neck = neckP,
            pos_Chest = chestP,
            pos_Spine = spineP,
            pos_Hips = hipsP,
            pos_CenterEye = eyeP,
            pos_Mouth = mouthP,
            rot_Head = headR,
            rot_Neck = headR,
            rot_Chest = headR,
            rot_Spine = headR,
            rot_Hips = hipsR,
            rot_CenterEye = headR,
            rot_Mouth = headR,
            diffHipToHeadMouthY = sc.tposeLocal_scaled_Mouth.y - sc.tposeLocal_scaled_Hips.y
        };
    }
}

// ---------- Gather jobs (Transform → temp SoA) ----------
[BurstCompile]
struct GatherRootJob : IJobParallelForTransform
{
    [WriteOnly] public NativeArray<float3> rootPos;
    [WriteOnly] public NativeArray<float3> rootScale;

    public void Execute(int index, TransformAccess tx)
    {
        rootPos[index] = tx.position;

        // derive world scale from matrix (no API call to lossyScale in jobs)
        var m = tx.localToWorldMatrix;
        float3 sx = new float3(m.m00, m.m10, m.m20);
        float3 sy = new float3(m.m01, m.m11, m.m21);
        float3 sz = new float3(m.m02, m.m12, m.m22);
        rootScale[index] = new float3(math.length(sx), math.length(sy), math.length(sz));
    }
}

[BurstCompile]
struct GatherHeadJob : IJobParallelForTransform
{
    [WriteOnly] public NativeArray<float3> headPos;
    [WriteOnly] public NativeArray<quaternion> headRot;

    public void Execute(int index, TransformAccess tx)
    {
        headPos[index] = tx.position;
        headRot[index] = tx.rotation;
    }
}

[BurstCompile]
struct GatherHipsJob : IJobParallelForTransform
{
    [WriteOnly] public NativeArray<float3> hipsPos;
    [WriteOnly] public NativeArray<quaternion> hipsRot;

    public void Execute(int index, TransformAccess tx)
    {
        hipsPos[index] = tx.position;
        hipsRot[index] = tx.rotation;
    }
}

[BurstCompile]
struct CombineInputsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> rootPos;
    [ReadOnly] public NativeArray<float3> rootScale;
    [ReadOnly] public NativeArray<float3> headPos;
    [ReadOnly] public NativeArray<quaternion> headRot;
    [ReadOnly] public NativeArray<float3> hipsPos;
    [ReadOnly] public NativeArray<quaternion> hipsRot;
    [ReadOnly] public NativeArray<quaternion> tposeHeadRot;
    [ReadOnly] public NativeArray<quaternion> tposeHipsRot;

    public NativeArray<RemoteFrameInput> InOut;

    public void Execute(int i)
    {
        InOut[i] = new RemoteFrameInput
        {
            rootWorld = rootPos[i],
            headWPos = headPos[i],
            hipsWPos = hipsPos[i],
            headWRot = headRot[i],
            hipsWRot = hipsRot[i],
            tposeHeadRot = tposeHeadRot[i],
            tposeHipsRot = tposeHipsRot[i],
            nowScale = rootScale[i]
        };
    }
}

// ---------- Apply job (Mouth → Transform) ----------
[BurstCompile]
struct ApplyMouthJob : IJobParallelForTransform
{
    [ReadOnly] public NativeArray<RemoteFrameOutput> Out;
    [ReadOnly] public NativeArray<float3> rootWorld; // sTmpRootPos

    public void Execute(int index, TransformAccess tx)
    {
        var o = Out[index];

        // Sim positions are root-relative; add rootWorld to get absolute
        float3 worldPos = rootWorld[index] + o.pos_Mouth;
        tx.position = worldPos;
        tx.rotation = o.rot_Mouth; // rot_* are world rotations
    }
}

// ---------- Apply job (AvatarTransform scale) ----------
[BurstCompile]
struct ApplyAvatarScaleJob : IJobParallelForTransform
{
    [ReadOnly] public NativeArray<float3> rootScale; // sTmpRootScale

    public void Execute(int index, TransformAccess tx)
    {
        // Writes local scale to match root world scale (simple case).
        tx.localScale = rootScale[index];
    }
}

// ---------------- Static Manager ----------------
public static class RemoteBoneJobSystem
{
    // Persistent SoA
    static NativeList<RemoteAuthoring> sAuthoring;
    static NativeList<RemoteFrameInput> sIn;
    static NativeList<RemoteScaleCache> sScale;
    static NativeList<RemoteFrameOutput> sOut;

    // Cached TPose quats (job friendly)
    static NativeList<quaternion> sTPoseHeadRot;
    static NativeList<quaternion> sTPoseHipsRot;

    // Transform access arrays (roots / heads / hips / mouthTargets / avatar scale targets)
    static TransformAccessArray sRoots;
    static TransformAccessArray sHeads;
    static TransformAccessArray sHips;
    static TransformAccessArray sMouthTargets;
    static TransformAccessArray sAvatarTransforms; // NEW

    // Temp per-frame buffers (reused)
    static NativeArray<float3> sTmpRootPos, sTmpHeadPos, sTmpHipsPos;
    static NativeArray<float3> sTmpRootScale;
    static NativeArray<quaternion> sTmpHeadRot, sTmpHipsRot;

    // Bookkeeping
    static readonly Dictionary<int, int> sKeyToIndex = new Dictionary<int, int>();
    static readonly List<PerEntityRefs> sIndexToRefs = new List<PerEntityRefs>();
    static JobHandle sPending;
    static bool sInitialized;

    struct PerEntityRefs
    {
        public int RemotePlayerDataIndex;
        public bool HasNameplate;
        public Func<bool> IsNameplateVisible;
    }

    // -------- Lifecycle --------
    public static void Initialize(int initialCapacity = 0)
    {
        if (sInitialized) return;

        sAuthoring = new NativeList<RemoteAuthoring>(initialCapacity, Allocator.Persistent);
        sIn = new NativeList<RemoteFrameInput>(initialCapacity, Allocator.Persistent);
        sScale = new NativeList<RemoteScaleCache>(initialCapacity, Allocator.Persistent);
        sOut = new NativeList<RemoteFrameOutput>(initialCapacity, Allocator.Persistent);

        sTPoseHeadRot = new NativeList<quaternion>(initialCapacity, Allocator.Persistent);
        sTPoseHipsRot = new NativeList<quaternion>(initialCapacity, Allocator.Persistent);

        sRoots = new TransformAccessArray(initialCapacity);
        sHeads = new TransformAccessArray(initialCapacity);
        sHips = new TransformAccessArray(initialCapacity);
        sMouthTargets = new TransformAccessArray(initialCapacity);
        sAvatarTransforms = new TransformAccessArray(initialCapacity); // NEW

        sInitialized = true;
    }

    public static void Dispose()
    {
        CompletePending();

        if (sAuthoring.IsCreated) sAuthoring.Dispose();
        if (sIn.IsCreated) sIn.Dispose();
        if (sScale.IsCreated) sScale.Dispose();
        if (sOut.IsCreated) sOut.Dispose();

        if (sTPoseHeadRot.IsCreated) sTPoseHeadRot.Dispose();
        if (sTPoseHipsRot.IsCreated) sTPoseHipsRot.Dispose();

        if (sRoots.isCreated) sRoots.Dispose();
        if (sHeads.isCreated) sHeads.Dispose();
        if (sHips.isCreated) sHips.Dispose();
        if (sMouthTargets.isCreated) sMouthTargets.Dispose();
        if (sAvatarTransforms.isCreated) sAvatarTransforms.Dispose(); // NEW

        DisposeTempBuffers();

        sKeyToIndex.Clear();
        sIndexToRefs.Clear();
        sInitialized = false;
    }

    static void CompletePending()
    {
        sPending.Complete();
        sPending = default;
    }

    // -------- Add / Remove --------
    public static int AddRemotePlayer(
        int key,
        Transform remotePlayerRoot,
        Transform head, Transform hips,
        Transform mouthTarget,             // target transform to write mouth pose
        Transform avatarTransform,         // NEW: target transform to set scale
        BasisCalibratedCoords tposeHead, BasisCalibratedCoords tposeHips,
        float3 authoredCenterEyeWorld,
        float3 authoredMouthWorld,
        int remotePlayerDataIndex,
        Func<bool> isNameplateVisible      // nullable
    )
    {
        if (!sInitialized) Initialize();
        CompletePending();

        // Compute authoring offsets (one-time main-thread read is fine)
        float3 rootWorld = remotePlayerRoot.position;
        float3 ToAvatarLocal(float3 world) => world - rootWorld;

        float3 tHead = ToAvatarLocal(head.position);
        float3 tNeck = float3.zero;
        float3 tChest = float3.zero;
        float3 tSpine = float3.zero;
        float3 tHips = ToAvatarLocal(hips.position);
        float3 tEye = ToAvatarLocal(authoredCenterEyeWorld);
        float3 tMouth = ToAvatarLocal(authoredMouthWorld);

        float3 offNeck = tNeck - tHead;
        float3 offChest = tChest - tNeck;
        float3 offSpine = tSpine - tChest;
        float3 offEye = tEye - tHead;
        float3 offMouth = tMouth - tHead;

        var a = new RemoteAuthoring
        {
            tposeLocal_unscaled_Head = tHead,
            tposeLocal_unscaled_Neck = tNeck,
            tposeLocal_unscaled_Chest = tChest,
            tposeLocal_unscaled_Spine = tSpine,
            tposeLocal_unscaled_Hips = tHips,
            tposeLocal_unscaled_CenterEye = tEye,
            tposeLocal_unscaled_Mouth = tMouth,

            offsets_unscaled_Neck = offNeck,
            offsets_unscaled_Chest = offChest,
            offsets_unscaled_Spine = offSpine,
            offsets_unscaled_CenterEye = offEye,
            offsets_unscaled_Mouth = offMouth
        };

        int idx = sAuthoring.Length;

        // Ensure TAA capacity (double strategy)
        EnsureTaaCapacity(idx + 1);

        // Append SoA
        sAuthoring.Add(a);
        sIn.Add(default);
        sScale.Add(new RemoteScaleCache { lastScale = new float3(1, 1, 1) });
        sOut.Add(default);

        // Cache TPose quats
        sTPoseHeadRot.Add((quaternion)tposeHead.rotation);
        sTPoseHipsRot.Add((quaternion)tposeHips.rotation);

        // Register transforms into TAAs
        sRoots.Add(remotePlayerRoot);
        sHeads.Add(head);
        sHips.Add(hips);
        sMouthTargets.Add(mouthTarget);
        sAvatarTransforms.Add(avatarTransform); // NEW

        // Index→refs for nameplate updater
        if (sIndexToRefs.Count == idx) sIndexToRefs.Add(default);
        sIndexToRefs[idx] = new PerEntityRefs
        {
            RemotePlayerDataIndex = remotePlayerDataIndex,
            HasNameplate = isNameplateVisible != null,
            IsNameplateVisible = isNameplateVisible
        };

        sKeyToIndex[key] = idx;
        return key;
    }

    public static bool RemoveRemotePlayer(int key)
    {
        if (!sInitialized) return false;
        CompletePending();

        if (!sKeyToIndex.TryGetValue(key, out int idx)) return false;

        int last = sAuthoring.Length - 1;
        if (idx != last)
        {
            // Swap-back SoA
            sAuthoring[idx] = sAuthoring[last];
            sIn[idx] = sIn[last];
            sScale[idx] = sScale[last];
            sOut[idx] = sOut[last];

            sTPoseHeadRot[idx] = sTPoseHeadRot[last];
            sTPoseHipsRot[idx] = sTPoseHipsRot[last];

            sIndexToRefs[idx] = sIndexToRefs[last];

            // Swap-back in TAAs (keeps arrays aligned by index)
            sRoots.RemoveAtSwapBack(idx);
            sHeads.RemoveAtSwapBack(idx);
            sHips.RemoveAtSwapBack(idx);
            sMouthTargets.RemoveAtSwapBack(idx);
            sAvatarTransforms.RemoveAtSwapBack(idx); // NEW

            // Patch moved key (safe: don't modify while enumerating)
            int movedKey = -1;
            foreach (var kv in sKeyToIndex)
            {
                if (kv.Value == last) { movedKey = kv.Key; break; }
            }
            if (movedKey != -1) sKeyToIndex[movedKey] = idx;
        }
        else
        {
            // Removing last: just RemoveAt for TAAs
            sRoots.RemoveAtSwapBack(last);
            sHeads.RemoveAtSwapBack(last);
            sHips.RemoveAtSwapBack(last);
            sMouthTargets.RemoveAtSwapBack(last);
            sAvatarTransforms.RemoveAtSwapBack(last); // NEW
        }

        sAuthoring.RemoveAt(last);
        sIn.RemoveAt(last);
        sScale.RemoveAt(last);
        sOut.RemoveAt(last);
        sTPoseHeadRot.RemoveAt(last);
        sTPoseHipsRot.RemoveAt(last);
        sIndexToRefs.RemoveAt(last);
        sKeyToIndex.Remove(key);
        return true;
    }

    // -------- Frame steps --------
    static void EnsureTempBuffers(int count)
    {
        if (count <= 0) return;

        void AllocOrResize<T>(ref NativeArray<T> arr, int len) where T : struct
        {
            if (arr.IsCreated)
            {
                if (arr.Length != len)
                {
                    arr.Dispose();
                    arr = new NativeArray<T>(len, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
                }
            }
            else
            {
                arr = new NativeArray<T>(len, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }
        }

        AllocOrResize(ref sTmpRootPos, count);
        AllocOrResize(ref sTmpRootScale, count);
        AllocOrResize(ref sTmpHeadPos, count);
        AllocOrResize(ref sTmpHeadRot, count);
        AllocOrResize(ref sTmpHipsPos, count);
        AllocOrResize(ref sTmpHipsRot, count);
    }

    static void DisposeTempBuffers()
    {
        if (sTmpRootPos.IsCreated) sTmpRootPos.Dispose();
        if (sTmpRootScale.IsCreated) sTmpRootScale.Dispose();
        if (sTmpHeadPos.IsCreated) sTmpHeadPos.Dispose();
        if (sTmpHeadRot.IsCreated) sTmpHeadRot.Dispose();
        if (sTmpHipsPos.IsCreated) sTmpHipsPos.Dispose();
        if (sTmpHipsRot.IsCreated) sTmpHipsRot.Dispose();
    }

    static void EnsureTaaCapacity(int needed)
    {
        // Grow capacity exponentially to amortize resizes
        if (sRoots.capacity < needed)
        {
            int newCap = math.max(needed, math.max(4, sRoots.capacity * 2));
            sRoots.capacity = newCap;
            sHeads.capacity = newCap;
            sHips.capacity = newCap;
            sMouthTargets.capacity = newCap;
            sAvatarTransforms.capacity = newCap; // NEW
        }
    }

    public static JobHandle Schedule(int batchSize = 64)
    {
        if (!sInitialized || sAuthoring.Length == 0) return default;

        EnsureTempBuffers(sAuthoring.Length);

        var hRoot = new GatherRootJob
        {
            rootPos = sTmpRootPos,
            rootScale = sTmpRootScale
        }.Schedule(sRoots);

        var hHead = new GatherHeadJob
        {
            headPos = sTmpHeadPos,
            headRot = sTmpHeadRot
        }.Schedule(sHeads);

        var hHips = new GatherHipsJob
        {
            hipsPos = sTmpHipsPos,
            hipsRot = sTmpHipsRot
        }.Schedule(sHips);

        var deps = JobHandle.CombineDependencies(hRoot, hHead, hHips);

        var combine = new CombineInputsJob
        {
            rootPos = sTmpRootPos,
            rootScale = sTmpRootScale,
            headPos = sTmpHeadPos,
            headRot = sTmpHeadRot,
            hipsPos = sTmpHipsPos,
            hipsRot = sTmpHipsRot,
            tposeHeadRot = sTPoseHeadRot.AsDeferredJobArray(),
            tposeHipsRot = sTPoseHipsRot.AsDeferredJobArray(),
            InOut = sIn.AsDeferredJobArray()
        }.Schedule(sAuthoring.Length, batchSize, deps);

        var sim = new BoneSimJob
        {
            Authoring = sAuthoring.AsDeferredJobArray(),
            In = sIn.AsDeferredJobArray(),
            ScaleCache = sScale.AsDeferredJobArray(),
            Out = sOut.AsDeferredJobArray()
        }.Schedule(sAuthoring.Length, batchSize, combine);

        // Apply mouth pose to target transforms (world space)
        var applyMouth = new ApplyMouthJob
        {
            Out = sOut.AsDeferredJobArray(),
            rootWorld = sTmpRootPos
        }.Schedule(sMouthTargets, sim);

        // Apply avatar scale from gathered root world scale
        var applyScale = new ApplyAvatarScaleJob
        {
            rootScale = sTmpRootScale
        }.Schedule(sAvatarTransforms, deps); // needs only gather jobs

        var finalHandle = JobHandle.CombineDependencies(applyMouth, applyScale);
        sPending = finalHandle;
        return finalHandle;
    }

    public static void Complete(Action<int, float3, float> nameplateUpdater, JobHandle handle)
    {
        handle.Complete();
        CompleteAndApply(nameplateUpdater);
    }

    static void CompleteAndApply(Action<int, float3, float> nameplateUpdater)
    {
        if (!sInitialized) return;

        CompletePending();

        for (int i = 0; i < sOut.Length; i++)
        {
            var o = sOut[i];
            var r = sIndexToRefs[i];

            if (r.HasNameplate && r.IsNameplateVisible())
            {
                // (dataIndex, hipsPos, diff)
                nameplateUpdater?.Invoke(r.RemotePlayerDataIndex, o.pos_Hips, o.diffHipToHeadMouthY);
            }
        }
    }

    // -------- Accessors by key --------
    public static float3 GetOutgoingPosition(int key, int boneIndex)
    {
        if (!TryGetIndex(key, out int idx)) return float3.zero;
        var o = sOut[idx];
        switch (boneIndex)
        {
            case BoneIdx.Head: return o.pos_Head;
            case BoneIdx.Neck: return o.pos_Neck;
            case BoneIdx.Chest: return o.pos_Chest;
            case BoneIdx.Spine: return o.pos_Spine;
            case BoneIdx.Hips: return o.pos_Hips;
            case BoneIdx.CenterEye: return o.pos_CenterEye;
            case BoneIdx.Mouth: return o.pos_Mouth;
            default: return float3.zero;
        }
    }

    public static quaternion GetOutgoingRotation(int key, int boneIndex)
    {
        if (!TryGetIndex(key, out int idx)) return quaternion.identity;
        var o = sOut[idx];
        switch (boneIndex)
        {
            case BoneIdx.Head: return o.rot_Head;
            case BoneIdx.Neck: return o.rot_Neck;
            case BoneIdx.Chest: return o.rot_Chest;
            case BoneIdx.Spine: return o.rot_Spine;
            case BoneIdx.Hips: return o.rot_Hips;
            case BoneIdx.CenterEye: return o.rot_CenterEye;
            case BoneIdx.Mouth: return o.rot_Mouth;
            default: return quaternion.identity;
        }
    }

    public static bool TryGetOutgoingPose(int key, int boneIndex, out float3 position, out quaternion rotation)
    {
        position = float3.zero;
        rotation = quaternion.identity;

        if (!TryGetIndex(key, out int idx)) return false;
        var o = sOut[idx];

        // single switch for both values
        switch (boneIndex)
        {
            case BoneIdx.Head: position = o.pos_Head; rotation = o.rot_Head; return true;
            case BoneIdx.Neck: position = o.pos_Neck; rotation = o.rot_Neck; return true;
            case BoneIdx.Chest: position = o.pos_Chest; rotation = o.rot_Chest; return true;
            case BoneIdx.Spine: position = o.pos_Spine; rotation = o.rot_Spine; return true;
            case BoneIdx.Hips: position = o.pos_Hips; rotation = o.rot_Hips; return true;
            case BoneIdx.CenterEye: position = o.pos_CenterEye; rotation = o.rot_CenterEye; return true;
            case BoneIdx.Mouth: position = o.pos_Mouth; rotation = o.rot_Mouth; return true;
            default: return false;
        }
    }

    static bool TryGetIndex(int key, out int idx) => sKeyToIndex.TryGetValue(key, out idx);
}
