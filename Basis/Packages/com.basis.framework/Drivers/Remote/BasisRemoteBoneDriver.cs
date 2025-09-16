using Basis.Scripts.Common;
using Basis.Scripts.Drivers;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
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
    public float3 tposeLocal_scaled_Hips;
    public float3 tposeLocal_scaled_Mouth;
    public float3 offsets_scaled_Neck;
    public float3 offsets_scaled_Chest;
    public float3 offsets_scaled_Spine;
    public float3 offsets_scaled_CenterEye;
    public float3 offsets_scaled_Mouth;
}
// ---------------- Split outputs (by consumer) ----------------
public struct MouthPose
{
    public float3 pos;
    public quaternion rot;
}
public struct NameplateData
{
    public float3 hips;
    public float diffHipToHeadMouthY;
}
// ---------------- Nameplate Apply ----------------
[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
public struct MappedNameplateApplyJob : IJobParallelForTransform
{
    // Precompute 1 + 1/1.25 = 1.8f once; Burst will constant-fold this.
    private const float kY = 1.8f;
    public float3 CameraPosition;
    [ReadOnly] public NativeArray<NameplateData> NamePlateIn;
    public void Execute(int jobIndex, TransformAccess tx)
    {
        var data = NamePlateIn[jobIndex];
        float3 hips = data.hips;
        // y = hips.y + diff * 1.8
        float3 nameplatePos = new float3(hips.x, hips.y + data.diffHipToHeadMouthY * kY, hips.z);

        // Face the camera (yaw only) with zero-distance guard.
        float3 toCam = CameraPosition - nameplatePos;
        float2 xz = new float2(toCam.x, toCam.z);
        float yaw = math.lengthsq(xz) > 1e-12f ? math.atan2(xz.x, xz.y) : 0f;
        quaternion rot = quaternion.RotateY(yaw);

        tx.SetPositionAndRotation(nameplatePos, rot);
    }
}
// ---------------- Simulation ----------------
[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
public struct BoneSimJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<RemoteAuthoring> Authoring;
    [ReadOnly] public NativeArray<RemoteFrameInput> In;
    public NativeArray<RemoteScaleCache> ScaleCache;
    [WriteOnly] public NativeArray<MouthPose> MouthOut;
    [WriteOnly] public NativeArray<NameplateData> NameplateOut;
    public void Execute(int i)
    {
        var a = Authoring[i];
        var f = In[i];
        var sc = ScaleCache[i];

        sc.tposeLocal_scaled_Hips = a.tposeLocal_unscaled_Hips * f.nowScale;
        sc.tposeLocal_scaled_Mouth = a.tposeLocal_unscaled_Mouth * f.nowScale;
        sc.offsets_scaled_Neck = a.offsets_unscaled_Neck * f.nowScale;
        sc.offsets_scaled_Chest = a.offsets_unscaled_Chest * f.nowScale;
        sc.offsets_scaled_Spine = a.offsets_unscaled_Spine * f.nowScale;
        sc.offsets_scaled_CenterEye = a.offsets_unscaled_CenterEye * f.nowScale;
        sc.offsets_scaled_Mouth = a.offsets_unscaled_Mouth * f.nowScale;

        ScaleCache[i] = sc;
        quaternion headR = math.mul(f.tposeHeadRot, f.headWRot);
        float3 headP = f.headWPos - f.rootWorld;
        float3 hipsP = f.hipsWPos - f.rootWorld;
        // Only compute what we need for consumers
        float3 mouthP = headP + math.mul(headR, sc.offsets_scaled_Mouth);
        MouthOut[i] = new MouthPose
        {
            pos = mouthP,
            // Consumers want world-space rot for mouth (same as head in this rig)
            rot = headR
        };
        NameplateOut[i] = new NameplateData
        {
            hips = hipsP,
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
    [WriteOnly] public NativeArray<float3> hipsPosition;
    [WriteOnly] public NativeArray<quaternion> hipsRotation;
    public void Execute(int index, TransformAccess tx)
    {
        hipsPosition[index] = tx.position;
        hipsRotation[index] = tx.rotation;
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
    public NativeArray<RemoteFrameInput> FrameOutput;
    public void Execute(int i)
    {
        FrameOutput[i] = new RemoteFrameInput
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
[BurstCompile]
struct ApplyMouthJob : IJobParallelForTransform
{
    [ReadOnly] public NativeArray<MouthPose> In;
    [ReadOnly] public NativeArray<float3> rootWorld; // sTmpRootPos
    public void Execute(int index, TransformAccess tx)
    {
        var pose = In[index];
        // Sim positions are root-relative; add rootWorld to get absolute
        float3 worldPos = rootWorld[index] + pose.pos;
        tx.SetPositionAndRotation(worldPos, pose.rot);
    }
}
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
public static class RemoteBoneJobSystem
{
    // Persistent SoA
    static NativeList<RemoteAuthoring> sAuthoring;
    static NativeList<RemoteFrameInput> sIn;
    static NativeList<RemoteScaleCache> sScale;
    // Split outputs
    static NativeList<MouthPose> sMouthOut;
    static NativeList<NameplateData> sNameplateOut;
    // Cached TPose quats (job friendly)
    static NativeList<quaternion> sTPoseHeadRot;
    static NativeList<quaternion> sTPoseHipsRot;
    // Transform access arrays (roots / heads / hips / mouthTargets / avatar scale targets / nameplates)
    static TransformAccessArray sRoots;
    static TransformAccessArray sHeads;
    static TransformAccessArray sHips;
    static TransformAccessArray sMouthTargets;
    static TransformAccessArray sAvatarTransforms;
    static TransformAccessArray sAvatarNamePlate;
    // Temp per-frame buffers (reused)
    static NativeArray<float3> sTmpRootPos, sTmpHeadPos, sTmpHipsPos;
    static NativeArray<float3> sTmpRootScale;
    static NativeArray<quaternion> sTmpHeadRot, sTmpHipsRot;
    // Bookkeeping
    static readonly Dictionary<int, int> sKeyToIndex = new Dictionary<int, int>();
    static NativeList<int> sIndexToKey; // O(1) back-map for removals
    static JobHandle sPending;
    static bool sInitialized;
    public static void Initialize(int initialCapacity = 0)
    {
        if (sInitialized) return;

        sAuthoring = new NativeList<RemoteAuthoring>(initialCapacity, Allocator.Persistent);
        sIn = new NativeList<RemoteFrameInput>(initialCapacity, Allocator.Persistent);
        sScale = new NativeList<RemoteScaleCache>(initialCapacity, Allocator.Persistent);
        sMouthOut = new NativeList<MouthPose>(initialCapacity, Allocator.Persistent);
        sNameplateOut = new NativeList<NameplateData>(initialCapacity, Allocator.Persistent);
        sTPoseHeadRot = new NativeList<quaternion>(initialCapacity, Allocator.Persistent);
        sTPoseHipsRot = new NativeList<quaternion>(initialCapacity, Allocator.Persistent);
        sRoots = new TransformAccessArray(initialCapacity);
        sHeads = new TransformAccessArray(initialCapacity);
        sHips = new TransformAccessArray(initialCapacity);
        sMouthTargets = new TransformAccessArray(initialCapacity);
        sAvatarTransforms = new TransformAccessArray(initialCapacity);
        sAvatarNamePlate = new TransformAccessArray(initialCapacity);
        sIndexToKey = new NativeList<int>(initialCapacity, Allocator.Persistent);
        sInitialized = true;
    }
    public static void Dispose()
    {
        CompletePending();

        if (sAuthoring.IsCreated) sAuthoring.Dispose();
        if (sIn.IsCreated) sIn.Dispose();
        if (sScale.IsCreated) sScale.Dispose();
        if (sMouthOut.IsCreated) sMouthOut.Dispose();
        if (sNameplateOut.IsCreated) sNameplateOut.Dispose();
        if (sTPoseHeadRot.IsCreated) sTPoseHeadRot.Dispose();
        if (sTPoseHipsRot.IsCreated) sTPoseHipsRot.Dispose();
        if (sRoots.isCreated) sRoots.Dispose();
        if (sHeads.isCreated) sHeads.Dispose();
        if (sHips.isCreated) sHips.Dispose();
        if (sMouthTargets.isCreated) sMouthTargets.Dispose();
        if (sAvatarTransforms.isCreated) sAvatarTransforms.Dispose();
        if (sAvatarNamePlate.isCreated) sAvatarNamePlate.Dispose();
        if (sIndexToKey.IsCreated) sIndexToKey.Dispose();
        DisposeTempBuffers();
        sKeyToIndex.Clear();
        sInitialized = false;
    }
    static void CompletePending()
    {
        sPending.Complete();
        sPending = default;
    }
    public static int AddRemotePlayer(int key, Transform Root, Transform head, Transform Hip, Transform Mouth, Transform Avatar, Transform namePlate, BasisCalibratedCoords tposeHead, BasisCalibratedCoords tposeHips, float3 authoredCenterEyeWorld, float3 authoredMouthWorld)
    {
        if (!sInitialized) Initialize();
        CompletePending();

        // Compute authoring offsets (one-time main-thread read is fine)
        float3 rootWorld = Root.position;
        float3 ToAvatarLocal(float3 world) => world - rootWorld;

        float3 tHead = ToAvatarLocal(head.position);
        float3 tNeck = float3.zero;
        float3 tChest = float3.zero;
        float3 tSpine = float3.zero;
        float3 tHips = ToAvatarLocal(Hip.position);
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
        sScale.Add(new RemoteScaleCache());
        sMouthOut.Add(default);
        sNameplateOut.Add(default);
        // Cache TPose quats
        sTPoseHeadRot.Add((quaternion)tposeHead.rotation);
        sTPoseHipsRot.Add((quaternion)tposeHips.rotation);
        // Register transforms into TAAs
        sRoots.Add(Root);
        sHeads.Add(head);
        sHips.Add(Hip);
        sMouthTargets.Add(Mouth);
        sAvatarTransforms.Add(Avatar);
        sAvatarNamePlate.Add(namePlate);
        // Bookkeeping
        sKeyToIndex[key] = idx;
        sIndexToKey.Add(key);
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
            // Move last element into idx in SoA lists
            sAuthoring[idx] = sAuthoring[last];
            sIn[idx] = sIn[last];
            sScale[idx] = sScale[last];
            sMouthOut[idx] = sMouthOut[last];
            sNameplateOut[idx] = sNameplateOut[last];
            sTPoseHeadRot[idx] = sTPoseHeadRot[last];
            sTPoseHipsRot[idx] = sTPoseHipsRot[last];
            int movedKey = sIndexToKey[last];
            sIndexToKey[idx] = movedKey;
            sKeyToIndex[movedKey] = idx;
            // Remove transforms at idx via swap-back (moves last into idx)
            sRoots.RemoveAtSwapBack(idx);
            sHeads.RemoveAtSwapBack(idx);
            sHips.RemoveAtSwapBack(idx);
            sMouthTargets.RemoveAtSwapBack(idx);
            sAvatarTransforms.RemoveAtSwapBack(idx);
            sAvatarNamePlate.RemoveAtSwapBack(idx);
            // Trim the last element from lists
            sAuthoring.RemoveAt(last);
            sIn.RemoveAt(last);
            sScale.RemoveAt(last);
            sMouthOut.RemoveAt(last);
            sNameplateOut.RemoveAt(last);
            sTPoseHeadRot.RemoveAt(last);
            sTPoseHipsRot.RemoveAt(last);
            sIndexToKey.RemoveAt(last);
        }
        else
        {
            // Removing last: just RemoveAt for SoA
            sAuthoring.RemoveAt(last);
            sIn.RemoveAt(last);
            sScale.RemoveAt(last);
            sMouthOut.RemoveAt(last);
            sNameplateOut.RemoveAt(last);
            sTPoseHeadRot.RemoveAt(last);
            sTPoseHipsRot.RemoveAt(last);
            sIndexToKey.RemoveAt(last);
            // And swap-back remove for TAAs
            sRoots.RemoveAtSwapBack(last);
            sHeads.RemoveAtSwapBack(last);
            sHips.RemoveAtSwapBack(last);
            sMouthTargets.RemoveAtSwapBack(last);
            sAvatarTransforms.RemoveAtSwapBack(last);
            sAvatarNamePlate.RemoveAtSwapBack(last);
        }
        sKeyToIndex.Remove(key);
        return true;
    }
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
            sAvatarTransforms.capacity = newCap;
            sAvatarNamePlate.capacity = newCap;
        }
    }

    public static JobHandle Schedule(int batchSize = 64)
    {
        if (!sInitialized) return default;

        // If we have nothing to do, make sure any previous work is completed.
        if (sAuthoring.Length == 0)
        {
            CompletePending();
            return default;
        }

        EnsureTempBuffers(sAuthoring.Length);

        // 1) Chain this frame to the previous frame.
        var input = sPending;

        var hRoot = new GatherRootJob
        {
            rootPos = sTmpRootPos,
            rootScale = sTmpRootScale
        }.Schedule(sRoots, input);

        var hHead = new GatherHeadJob
        {
            headPos = sTmpHeadPos,
            headRot = sTmpHeadRot
        }.Schedule(sHeads, input);

        var hHips = new GatherHipsJob
        {
            hipsPosition = sTmpHipsPos,
            hipsRotation = sTmpHipsRot
        }.Schedule(sHips, input);

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
            FrameOutput = sIn.AsDeferredJobArray()
        }.Schedule(sAuthoring.Length, batchSize, deps);

        var sim = new BoneSimJob
        {
            Authoring = sAuthoring.AsDeferredJobArray(),
            In = sIn.AsDeferredJobArray(),
            ScaleCache = sScale.AsDeferredJobArray(),
            MouthOut = sMouthOut.AsDeferredJobArray(),
            NameplateOut = sNameplateOut.AsDeferredJobArray()
        }.Schedule(sAuthoring.Length, batchSize, combine);

        // World-space mouth writeback (depends on sim, uses sTmpRootPos from gathers)
        var applyMouth = new ApplyMouthJob
        {
            In = sMouthOut.AsDeferredJobArray(),
            rootWorld = sTmpRootPos
        }.Schedule(sMouthTargets, sim);

        // Local scale writeback only needs gathered root scale -> overlap with sim
        var applyScale = new ApplyAvatarScaleJob
        {
            rootScale = sTmpRootScale
        }.Schedule(sAvatarTransforms, hRoot);

        // Nameplate uses NameplateOut -> must depend on sim
        var applyNamePlate = new MappedNameplateApplyJob
        {
            CameraPosition = BasisLocalCameraDriver.Position,
            NamePlateIn = sNameplateOut.AsDeferredJobArray(),
        }.Schedule(sAvatarNamePlate, sim);

        var finalHandle = JobHandle.CombineDependencies(applyMouth, applyScale, applyNamePlate);
        sPending = finalHandle;
        return finalHandle;
    }
    public static void Complete(JobHandle handle)
    {
        handle.Complete();
        CompleteAndApply();
    }
    static void CompleteAndApply()
    {
        if (!sInitialized) return;
        CompletePending();
    }
    public static float3 GetOutgoingHead(int key)
    {
        if (!TryGetIndex(key, out int idx))
        {
            return float3.zero;
        }
        var input = sIn[idx];
        return input.headWPos - input.rootWorld;
    }
    static bool TryGetIndex(int key, out int idx) => sKeyToIndex.TryGetValue(key, out idx);
}
