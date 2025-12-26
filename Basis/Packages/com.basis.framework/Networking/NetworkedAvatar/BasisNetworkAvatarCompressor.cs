using Basis.Network.Core;
using Basis.Scripts.Networking.Compression;
using Basis.Scripts.Networking.Transmitters;
using Basis.Scripts.Profiler;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static SerializableBasis;
namespace Basis.Scripts.Networking.NetworkedAvatar
{
    public static class BasisNetworkAvatarCompressor
    {
        const int UnityMuscleCount = 95;
        const int Skipped = 6;                 // eyes/jaw (15..20)
        const int muscleCount = UnityMuscleCount - Skipped; // 89
        static bool sInitialized;
        static float[] sMinManaged;        // length 95
        static float[] sInvManaged;        // 1/range or 0
        static float[] sMaxManaged;        // min + range
        // persistent native LUTs / buffers
        static NativeArray<int> sOrder;     // slot -> muscle index
        static NativeArray<byte> sIsByte;   // slot -> 1 if 8-bit, else 0
        static NativeArray<int> sOffsets;   // slot -> byte offset in packed
        static NativeArray<float> sMin;     // index by muscle idx
        static NativeArray<float> sInv;     //"
        static NativeArray<float> sMax;     //"
        static NativeArray<byte> sPacked;   //packed output, reused
        static NativeArray<float> sMusclesNative; // input scratch persistent
        static int sPackedSize;
        public static byte[] OutGoingBytes;
        public static void Compress(BasisNetworkTransmitter transmitter, Animator animator)
        {
            Transform AnimatorTransform = animator.transform;
            transmitter.PoseHandler ??= new HumanPoseHandler(animator.avatar, AnimatorTransform);

            EnsureInitialized(); // our compressor init

            // Get current pose from Animator
            transmitter.PoseHandler.GetHumanPose(ref transmitter.HumanPose);

            CompressAvatarData(transmitter.storedAvatarData, transmitter.HumanPose, animator, AnimatorTransform);

            var data = transmitter.SendingOutAvatarData.Count == 0 ? null : transmitter.SendingOutAvatarData.Values.ToArray();

            transmitter.storedAvatarData.LASM.AdditionalAvatarDatas = data;

            transmitter.storedAvatarData.LASM.Serialize(transmitter.AvatarSendWriter);

            BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.LocalAvatarSync, transmitter.AvatarSendWriter.Length);

            BasisNetworkConnection.LocalPlayerPeer.Send(transmitter.AvatarSendWriter,BasisNetworkCommons.PlayerAvatarChannel,DeliveryMethod.Sequenced);

            transmitter.AvatarSendWriter.Reset();
            transmitter.ClearAdditional();
        }
        public static void InitalAvatarData(Animator animator, out BasisStoredAvatarData StoredAvatarData)
        {
            EnsureInitialized();
            Transform Transform = animator.transform;
            var poseHandler = new HumanPoseHandler(animator.avatar, Transform);
            var humanPose = new HumanPose();
            poseHandler.GetHumanPose(ref humanPose);
            StoredAvatarData = new BasisStoredAvatarData();
            CompressAvatarData(StoredAvatarData, humanPose, animator, Transform);
        }
        [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
        public static void CompressAvatarData(BasisStoredAvatarData AvatarData, HumanPose pose, Animator animator,Transform ScaleTransform)
        {
            EnsureInitialized();

            int offset = 0;

            // Position
            BasisUnityBitPackerExtensionsUnsafe.WritePosition(animator.bodyPosition, ref AvatarData.LASM.array, ref offset);

            // Rotation
            BasisUnityBitPackerExtensionsUnsafe.WriteQuaternionToBytes(animator.bodyRotation, ref AvatarData.LASM.array, ref offset, BasisNetworkPlayer.RotationCompression);

            // Muscles (parallel, zero-GC)
            CompressAvatarMuscles_Parallel(ref pose, ref AvatarData.LASM, ref offset);

            // Scale
            CompressScale(ScaleTransform.localScale.y, ref AvatarData.LASM, ref offset);
        }
        public static void CompressAvatarMuscles_Parallel(ref HumanPose pose, ref LocalAvatarSyncMessage message, ref int offset)
        {
            EnsureMusclesBuffer(UnityMuscleCount);
            unsafe
            {
                fixed (float* src = pose.muscles)
                {
                    UnsafeUtility.MemCpy(sMusclesNative.GetUnsafePtr(), src, sizeof(float) * UnityMuscleCount);
                }
            }

            // launch job: each slot writes to its own offset in sPacked (no races)
            var job = new QuantizeWriteJob
            {
                Muscles = sMusclesNative,
                Min = sMin,
                Inv = sInv,
                Max = sMax,
                Order = sOrder,
                IsByte = sIsByte,
                Offsets = sOffsets,
                OutBytes = sPacked
            };

            var handle = job.Schedule(sOrder.Length, 32);
            handle.Complete();

            // copy packed into final message buffer (NO managed array hop)
            unsafe
            {
                void* srcPtr = sPacked.GetUnsafeReadOnlyPtr();
                fixed (byte* dst = message.array)
                {
                    UnsafeUtility.MemCpy(dst + offset, srcPtr, sPackedSize);
                }
            }
            offset += sPackedSize;
        }
        /// <summary>
        /// shared utils (unchanged)
        /// </summary>
        /// <param name="scale"></param>
        /// <param name="message"></param>
        /// <param name="offset"></param>
        public static void CompressScale(float scale, ref LocalAvatarSyncMessage message, ref int offset)
        {
            const float Min = 0.005f;
            const float Max = 150f;
            const float range = Max - Min;

            float clamped = math.clamp(scale, Min, Max);
            float normalized = (clamped - Min) / range;

            ushort compressed = (ushort)(normalized * BasisOrderedDataSet.UShortRangeDifference);
            BasisUnityBitPackerExtensionsUnsafe.WriteUShort(compressed, ref message.array, ref offset);
        }
        /// <summary>
        /// initialization / disposal
        /// </summary>
        static void EnsureInitialized()
        {
            if (sInitialized) return;

            // 1) load BasisMuscleRange into managed LUTs
            var minT = BasisOrderedDataSet.MinMuscle;   // length 95
            var rangeT = BasisOrderedDataSet.RangeMuscle; // length 95

            if (minT == null || rangeT == null || minT.Length != UnityMuscleCount || rangeT.Length != UnityMuscleCount)
            {
                Debug.LogError("[BasisNetworkAvatarCompressor] BasisMuscleRange tables invalid.");
                return;
            }

            sMinManaged = new float[UnityMuscleCount];
            sInvManaged = new float[UnityMuscleCount];
            sMaxManaged = new float[UnityMuscleCount];

            for (int Index = 0; Index < UnityMuscleCount; Index++)
            {
                sMinManaged[Index] = minT[Index];
                float r = rangeT[Index];
                sInvManaged[Index] = (r <= 0f) ? 0f : 1f / r;
                sMaxManaged[Index] = minT[Index] + r;
            }
            int length = BasisOrderedDataSet.WRITE_ORDER.Length;

            // 2) build offsets and packed size from IS_BYTE
            sPackedSize = 0;
            var offs = new int[length];
            for (int Index = 0; Index < length; Index++)
            {
                offs[Index] = sPackedSize;
                sPackedSize += BasisOrderedDataSet.IS_BYTE[Index] ? 1 : 2;
            }
            // 3) allocate persistent natives
            sOrder = new NativeArray<int>(length, Allocator.Persistent);
            sIsByte = new NativeArray<byte>(length, Allocator.Persistent);
            sOffsets = new NativeArray<int>(length, Allocator.Persistent);
            sMin = new NativeArray<float>(UnityMuscleCount, Allocator.Persistent);
            sInv = new NativeArray<float>(UnityMuscleCount, Allocator.Persistent);
            sMax = new NativeArray<float>(UnityMuscleCount, Allocator.Persistent);
            sPacked = new NativeArray<byte>(sPackedSize, Allocator.Persistent);

            // 4) fill natives
            for (int Index = 0; Index < length; Index++)
            {
                sOrder[Index] = BasisOrderedDataSet.WRITE_ORDER[Index];
                sIsByte[Index] = BasisOrderedDataSet.IS_BYTE[Index] ? (byte)1 : (byte)0;
                sOffsets[Index] = offs[Index];
            }
            for (int Index = 0; Index < UnityMuscleCount; Index++)
            {
                sMin[Index] = sMinManaged[Index];
                sInv[Index] = sInvManaged[Index];
                sMax[Index] = sMaxManaged[Index];
            }
            EnsureMusclesBuffer(UnityMuscleCount);
            sInitialized = true;
            // 5) create persistent input buffer
            // Debug.Log($"[BasisNetworkAvatarCompressor] Init: slots={WRITE_ORDER.Length}, packed={sPackedSize} bytes");
        }
        static void EnsureMusclesBuffer(int count)
        {
            if (!sMusclesNative.IsCreated || sMusclesNative.Length != count)
            {
                if (sMusclesNative.IsCreated)
                {
                    sMusclesNative.Dispose();
                }
                sMusclesNative = new NativeArray<float>(count, Allocator.Persistent);
            }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnDomainReload()
        {
            Dispose();
        }
        public static void Dispose()
        {
            if (sOrder.IsCreated) sOrder.Dispose();
            if (sIsByte.IsCreated) sIsByte.Dispose();
            if (sOffsets.IsCreated) sOffsets.Dispose();
            if (sMin.IsCreated) sMin.Dispose();
            if (sInv.IsCreated) sInv.Dispose();
            if (sMax.IsCreated) sMax.Dispose();
            if (sPacked.IsCreated) sPacked.Dispose();
            if (sMusclesNative.IsCreated) sMusclesNative.Dispose();

            sInitialized = false;
        }
        /// <summary>
        /// job
        /// </summary>
        [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
        struct QuantizeWriteJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float> Muscles;   // index by Unity muscle index
            [ReadOnly] public NativeArray<float> Min;
            [ReadOnly] public NativeArray<float> Inv;
            [ReadOnly] public NativeArray<float> Max;
            [ReadOnly] public NativeArray<int> Order;      // slot -> muscle idx
            [ReadOnly] public NativeArray<byte> IsByte;     // slot -> 1/0
            [ReadOnly] public NativeArray<int> Offsets;    // slot -> byte offset
            [NativeDisableParallelForRestriction]
            public NativeArray<byte> OutBytes;              // shared packed buffer
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static byte Quant8(float x01) => (byte)math.round(x01 * 255f);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            static ushort Quant16(float x01) => (ushort)math.round(x01 * 65535f);
            public void Execute(int slot)
            {
                int idx = Order[slot];
                float v = Muscles[idx];
                float min = Min[idx];
                float inv = Inv[idx];
                float max = Max[idx];
                float clamped = math.clamp(v, min, max);
                float norm = (inv == 0f) ? 0f : (clamped - min) * inv;
                int o = Offsets[slot];
                if (IsByte[slot] == 1)
                {
                    OutBytes[o] = Quant8(norm);
                }
                else
                {
                    ushort u = Quant16(norm);
                    OutBytes[o] = (byte)(u & 0xFF);
                    OutBytes[o + 1] = (byte)(u >> 8);
                }
            }
        }
    }
}
