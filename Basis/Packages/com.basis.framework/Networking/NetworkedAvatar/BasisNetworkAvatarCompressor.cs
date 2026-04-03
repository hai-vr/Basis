using Basis.Network.Core;
using Basis.Network.Core.Compression;
using Basis.Scripts.Networking.Compression;
using Basis.Scripts.Networking.Transmitters;
using Basis.Scripts.Profiler;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using System.Linq;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using static SerializableBasis;
using static Basis.Network.Core.Compression.BasisAvatarBitPacking;

namespace Basis.Scripts.Networking.NetworkedAvatar
{
    /// <summary>
    /// Compresses local avatar bone rotations as T-pose-relative deltas using
    /// "smallest three" quaternion encoding. Replaces the old muscle-based path.
    /// </summary>
    public static class BasisNetworkAvatarCompressor
    {
        static bool sInitialized;

        // Persistent T-pose local rotations captured during calibration (indexed by HumanBodyBones 0..54)
        static quaternion[] sTposeLocalRotations;

        // Scratch buffer for 54 delta quaternions (indexed by slot in BONE_WRITE_ORDER)
        static NativeArray<quaternion> sBoneDeltas;

        // Wire quality is locked to HIGH
        static readonly BitQuality WireQuality = BitQuality.High;

        // Outbound sequence counter
        static byte sLocalSequence;

        /// <summary>
        /// Called during local avatar calibration to capture T-pose bone rotations.
        /// Must be called while the avatar is in T-pose.
        /// </summary>
        public static void CaptureTPose(Animator animator)
        {
            sTposeLocalRotations = new quaternion[55]; // HumanBodyBones 0..54
            for (int i = 0; i < 55; i++)
            {
                Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone != null)
                {
                    sTposeLocalRotations[i] = bone.localRotation;
                }
                else
                {
                    sTposeLocalRotations[i] = quaternion.identity;
                }
            }
        }

        public static void Compress(BasisNetworkTransmitter transmitter, Animator animator)
        {
            Transform t = animator.transform;

            EnsureInitialized();

            // If T-pose hasn't been captured yet, do it now from the mapping's recorded poses
            if (sTposeLocalRotations == null)
            {
                CaptureTPoseFromAnimator();
            }

            // Extract current bone rotations and compute deltas from T-pose
            ExtractBoneDeltas(animator);

            CompressAvatarData(transmitter.storedAvatarData, animator, t);

            var data = transmitter.SendingOutAvatarData.Count == 0 ? null : transmitter.SendingOutAvatarData.Values.ToArray();
            transmitter.storedAvatarData.LASM.AdditionalAvatarDatas = data;
            transmitter.storedAvatarData.LASM.LinkedAvatarIndex = transmitter.LastLinkedAvatarIndex;

            bool hasAdditional = data != null && data.Length > 0;
            byte channel = BasisNetworkCommons.GetPlayerAvatarChannelForQuality((int)WireQuality, hasAdditional);

            transmitter.AvatarSendWriter.Put(sLocalSequence);
            unchecked { sLocalSequence++; }

            transmitter.storedAvatarData.LASM.SerializeForChannel(transmitter.AvatarSendWriter, WireQuality);

            BasisNetworkProfiler.AddToCounter(BasisNetworkProfilerCounter.LocalAvatarSync, transmitter.AvatarSendWriter.Length);

            BasisNetworkConnection.LocalPlayerPeer.Send(transmitter.AvatarSendWriter, channel, DeliveryMethod.Unreliable);

            transmitter.AvatarSendWriter.Reset();
            transmitter.ClearAdditional();
        }

        public static void InitalAvatarData(Animator animator, out BasisStoredAvatarData StoredAvatarData)
        {
            EnsureInitialized();

            if (sTposeLocalRotations == null)
            {
                CaptureTPoseFromAnimator();
            }

            ExtractBoneDeltas(animator);

            StoredAvatarData = new BasisStoredAvatarData();
            CompressAvatarData(StoredAvatarData, animator, animator.transform);
        }

        static void CompressAvatarData(BasisStoredAvatarData AvatarData, Animator animator, Transform ScaleTransform)
        {
            int needed = BasisAvatarBitPacking.ConvertToSize(WireQuality);
            AvatarData.LASM.DataQualityLevel = (byte)WireQuality;
            AvatarData.LASM.array ??= new byte[needed];
            if (AvatarData.LASM.array.Length != needed)
            {
                AvatarData.LASM.array = new byte[needed];
            }

            // Clear the array (bone rotation packing ORs into bytes)
            System.Array.Clear(AvatarData.LASM.array, 0, needed);

            int offset = 0;

            // Send the actual hips bone world position and rotation —
            // NOT animator.bodyPosition/bodyRotation which is a virtual body-center
            // that only SetHumanPose knows how to interpret.
            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
           UnityEngine.Vector3 hipsPos = hips != null ? hips.position : animator.transform.position;
            UnityEngine.Quaternion hipsRot = hips != null ? hips.rotation : animator.transform.rotation;

            // Position (hips world position)
            BasisUnityBitPackerExtensionsUnsafe.WritePosition(hipsPos, ref AvatarData.LASM.array, ref offset);

            // Bone rotations
            BasisBoneRotationUtils.CompressBoneRotations(sBoneDeltas, WireQuality, AvatarData.LASM.array, ref offset);

            // Scale
            BasisUnityBitPackerExtensionsUnsafe.CompressScale(ScaleTransform.localScale.y, ref AvatarData.LASM, ref offset);

            // Hips world rotation
            BasisUnityBitPackerExtensionsUnsafe.WriteCompressedQuaternionToBytes(hipsRot, ref AvatarData.LASM.array, ref offset);
        }

        /// <summary>
        /// Extracts bone local rotations from the animator and computes
        /// T-pose-relative delta quaternions for each bone.
        /// </summary>
        static void ExtractBoneDeltas(Animator animator)
        {
            for (int slot = 0; slot < BasisBoneRotationCompression.SyncBoneCount; slot++)
            {
                int boneEnum = BasisBoneRotationCompression.BONE_WRITE_ORDER[slot];
                Transform bone = animator.GetBoneTransform((HumanBodyBones)boneEnum);

                if (bone != null)
                {
                    quaternion current = bone.localRotation;
                    quaternion tpose = sTposeLocalRotations[boneEnum];
                    // Delta = inverse(tpose) * current → "how much this bone rotated from rest"
                    quaternion delta = math.mul(math.inverse(tpose), current);
                    sBoneDeltas[slot] = delta;
                }
                else
                {
                    // Missing bone: identity delta (no rotation from rest)
                    sBoneDeltas[slot] = quaternion.identity;
                }
            }
        }

        /// <summary>
        /// Fallback: reads actual bone.localRotation from the current animator.
        /// Only valid if bones are accessible. The avatar will NOT be in T-pose at this point,
        /// so we read from the live pose and accept that the first few frames may be slightly off
        /// until the avatar is reloaded. Logs a warning because CaptureTPose should have been called.
        /// </summary>
        static void CaptureTPoseFromAnimator()
        {
            BasisDebug.LogError("[BasisNetworkAvatarCompressor] CaptureTPose was not called during calibration! " +
                "Falling back to current bone rotations — avatar reload recommended.", BasisDebug.LogTag.Networking);

            sTposeLocalRotations = new quaternion[55];
            var player = BasisLocalPlayer.Instance;
            Animator animator = player?.BasisAvatar?.Animator;
            if (animator != null)
            {
                for (int i = 0; i < 55; i++)
                {
                    Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
                    sTposeLocalRotations[i] = bone != null ? (quaternion)bone.localRotation : quaternion.identity;
                }
            }
            else
            {
                for (int i = 0; i < 55; i++)
                    sTposeLocalRotations[i] = quaternion.identity;
            }
        }

        static void EnsureInitialized()
        {
            if (sInitialized) return;

            if (!sBoneDeltas.IsCreated)
            {
                sBoneDeltas = new NativeArray<quaternion>(BasisBoneRotationCompression.SyncBoneCount, Allocator.Persistent);
            }

            sInitialized = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void OnDomainReload()
        {
            Dispose();
        }

        public static void Dispose()
        {
            if (sBoneDeltas.IsCreated) sBoneDeltas.Dispose();
            sTposeLocalRotations = null;
            sInitialized = false;
        }
    }
}
