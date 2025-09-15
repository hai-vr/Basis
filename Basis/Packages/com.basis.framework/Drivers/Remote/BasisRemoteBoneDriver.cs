using Basis.Scripts.Common;
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

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

// ---------------- Static Manager ----------------
public static class RemoteBoneJobSystem
{
    // Persistent SoA
    static NativeList<RemoteAuthoring> sAuthoring;
    static NativeList<RemoteFrameInput> sIn;
    static NativeList<RemoteScaleCache> sScale;
    static NativeList<RemoteFrameOutput> sOut;

    // Bookkeeping
    static readonly Dictionary<int, int> sKeyToIndex = new Dictionary<int, int>();
    static readonly List<PerEntityRefs> sIndexToRefs = new List<PerEntityRefs>();
    static JobHandle sPending;
    static bool sInitialized;

    struct PerEntityRefs
    {
        public Transform Root;
        public Transform Head;
        public Transform Hips;
        public BasisCalibratedCoords TposeHead;
        public BasisCalibratedCoords TposeHips;
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
        sInitialized = true;
    }

    public static void Dispose()
    {
        CompletePending();
        if (sAuthoring.IsCreated) sAuthoring.Dispose();
        if (sIn.IsCreated) sIn.Dispose();
        if (sScale.IsCreated) sScale.Dispose();
        if (sOut.IsCreated) sOut.Dispose();
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
        BasisCalibratedCoords tposeHead, BasisCalibratedCoords tposeHips,
        float3 authoredCenterEyeWorld,
        float3 authoredMouthWorld,
        int remotePlayerDataIndex,
        Func<bool> isNameplateVisible // nullable
    )
    {
        if (!sInitialized) Initialize();

        CompletePending();

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
        sAuthoring.Add(a);
        sIn.Add(default);
        sScale.Add(new RemoteScaleCache { lastScale = new float3(1, 1, 1) });
        sOut.Add(default);

        if (sIndexToRefs.Count == idx) sIndexToRefs.Add(default);
        sIndexToRefs[idx] = new PerEntityRefs
        {
            Root = remotePlayerRoot,
            Head = head,
            Hips = hips,
            TposeHead = tposeHead,
            TposeHips = tposeHips,
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
            sAuthoring[idx] = sAuthoring[last];
            sIn[idx] = sIn[last];
            sScale[idx] = sScale[last];
            sOut[idx] = sOut[last];

            sIndexToRefs[idx] = sIndexToRefs[last];

            // patch moved key
            foreach (var kv in sKeyToIndex)
            {
                if (kv.Value == last) { sKeyToIndex[kv.Key] = idx; break; }
            }
        }

        sAuthoring.RemoveAt(last);
        sIn.RemoveAt(last);
        sScale.RemoveAt(last);
        sOut.RemoveAt(last);
        sIndexToRefs.RemoveAt(last);
        sKeyToIndex.Remove(key);
        return true;
    }

    // -------- Frame steps --------
    public static void GatherInputs()
    {
        if (!sInitialized) return;

        for (int i = 0; i < sAuthoring.Length; i++)
        {
            var r = sIndexToRefs[i];

            sIn[i] = new RemoteFrameInput
            {
                rootWorld =  r.Root.position,//transform
                headWPos = r.Head.position,//transform
                hipsWPos = r.Hips.position,//transform
                headWRot = (quaternion)r.Head.rotation,
                hipsWRot = (quaternion)r.Hips.rotation,
                tposeHeadRot = (quaternion)r.TposeHead.rotation,
                tposeHipsRot = (quaternion)r.TposeHips.rotation,
                nowScale = r.Root.lossyScale
            };
        }
    }

    public static JobHandle ScheduleSimulation(JobHandle dependsOn = default, int batchSize = 64)
    {
        if (!sInitialized || sAuthoring.Length == 0) return dependsOn;

        var job = new BoneSimJob
        {
            Authoring = sAuthoring.AsDeferredJobArray(),
            In = sIn.AsDeferredJobArray(),
            ScaleCache = sScale.AsDeferredJobArray(),
            Out = sOut.AsDeferredJobArray()
        };

        sPending = job.Schedule(sAuthoring.Length, math.max(1, batchSize), dependsOn);
        return sPending;
    }

    public static void CompleteAndApply(Action<int, float3, float> nameplateUpdater)
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

    public static float GetDiffHipToHeadMouthY(int key)
    {
        if (!TryGetIndex(key, out int idx)) return 0f;
        return sOut[idx].diffHipToHeadMouthY;
    }

    static bool TryGetIndex(int key, out int idx) => sKeyToIndex.TryGetValue(key, out idx);

    // -------- Convenience: single call frame driver --------
    public static JobHandle Schedule( int batchSize = 64)
    {
        GatherInputs();
      return ScheduleSimulation(default, batchSize);
    }
    public static void Complete(Action<int, float3, float> nameplateUpdater, JobHandle Handle)
    {
        Handle.Complete();
        CompleteAndApply(nameplateUpdater);
    }
}
