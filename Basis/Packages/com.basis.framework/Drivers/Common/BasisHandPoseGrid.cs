using Basis.Scripts.Common;
using Basis.Scripts.Player;
using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.Scripts.Drivers
{
    /// <summary>
    /// The one copy of the grid interpolation. Kept as a free-standing static class so a Burst job
    /// can call it without touching the managed grid object that owns the cells.
    ///
    /// Local and remote MUST reconstruct through this same function. Two consequences if they don't:
    /// the hand a player sees on themselves stops matching the hand everyone else sees, and the
    /// remote apply path's write mask — which skips a localRotation write when the composed value is
    /// bit-identical to last frame — stops firing on settled fingers, dirtying every remote's finger
    /// subtree every frame.
    /// </summary>
    public static class BasisHandPoseSampler
    {
        public const int JointsPerFinger = BasisHandPoseGrid.JointsPerFinger;

        /// <summary>
        /// Bilinearly samples one joint's rotation for a finger at the given curl/splay.
        ///
        /// Percentages outside [-1, 1] clamp to the grid edge rather than reading out of bounds:
        /// MediaPipe's CurlGain/SplayGain and the controller remaps can both overshoot, and the
        /// clamp is on the cell INDEX as well as the blend factor so an overshoot saturates instead
        /// of wrapping onto another finger's cells.
        /// </summary>
        public static quaternion SampleJoint(
            in NativeArray<quaternion> cells, int fingerStride, int gridWidth, int gridHeight,
            float increment, int fingerIndex, int jointIndex, float2 percentage)
        {
            float fx = (percentage.x + 1f) / increment;
            float fy = (percentage.y + 1f) / increment;
            int x0 = math.clamp((int)math.floor(fx), 0, gridWidth - 2);
            int y0 = math.clamp((int)math.floor(fy), 0, gridHeight - 2);
            float tx = math.clamp(fx - x0, 0f, 1f);
            float ty = math.clamp(fy - y0, 0f, 1f);

            int fingerBase = fingerIndex * fingerStride;
            int g00 = fingerBase + (x0 * gridHeight + y0) * JointsPerFinger + jointIndex;
            int g10 = fingerBase + ((x0 + 1) * gridHeight + y0) * JointsPerFinger + jointIndex;
            int g01 = fingerBase + (x0 * gridHeight + y0 + 1) * JointsPerFinger + jointIndex;
            int g11 = fingerBase + ((x0 + 1) * gridHeight + y0 + 1) * JointsPerFinger + jointIndex;

            quaternion bottom = math.slerp(cells[g00], cells[g10], tx);
            quaternion top = math.slerp(cells[g01], cells[g11], tx);
            return math.slerp(bottom, top, ty);
        }
    }

    /// <summary>
    /// The baked map from a hand's twenty-scalar input (five curl/splay pairs per hand, which is what
    /// every Basis finger backend reduces to) onto that avatar's thirty finger joint rotations.
    ///
    /// Split out of BasisLocalHandDriver so a REMOTE player can run the identical reconstruction. The
    /// sampler below is the single copy of the interpolation — local and remote must agree bit for
    /// bit, or the hand a player sees on themselves is not the hand everyone else sees, and the
    /// remote apply path's write mask (which skips a transform write when the composed rotation is
    /// unchanged) stops firing on settled fingers.
    ///
    /// The grid is a property of the AVATAR ASSET, not of the player wearing it, so it is interned
    /// through BasisAvatarModelCache: a crowd in matching avatars bakes once.
    /// </summary>
    public sealed class BasisHandPoseGrid : IDisposable
    {
        public const int FingerCount = 10;
        public const int JointsPerFinger = 3;
        public const int JointCount = FingerCount * JointsPerFinger;
        public const float DefaultIncrement = 0.1f;

        /// <summary>Flat cells: [fingerIdx * FingerStride + gridIdx * 3 + jointIdx].</summary>
        public NativeArray<quaternion> Cells;
        public int GridWidth;
        public int GridHeight;
        public int FingerStride;
        public float Increment = DefaultIncrement;

        /// <summary>
        /// True only for a grid that is actually samplable. The array being allocated is not enough:
        /// a zero-length or single-column grid reports IsCreated on the NativeArray while every
        /// sample indexes out of bounds, and the sampler's clamp range (0 .. gridWidth - 2) inverts
        /// below two columns. Burst turns that into an abort rather than an exception, so the check
        /// belongs here where every caller already looks.
        /// </summary>
        public bool IsCreated =>
            Cells.IsCreated && Cells.Length > 0 && FingerStride > 0 && GridWidth >= 2 && GridHeight >= 2;

        public void Dispose()
        {
            if (Cells.IsCreated) Cells.Dispose();
            GridWidth = 0;
            GridHeight = 0;
            FingerStride = 0;
        }

        public quaternion SampleJoint(int fingerIndex, int jointIndex, float2 percentage)
            => BasisHandPoseSampler.SampleJoint(Cells, FingerStride, GridWidth, GridHeight, Increment,
                fingerIndex, jointIndex, percentage);

        /// <summary>Samples all thirty joints into <paramref name="destination"/>, flat-indexed finger*3+joint.</summary>
        public void SampleAll(in NativeArray<float2> percentages, ref NativeArray<quaternion> destination)
        {
            for (int finger = 0; finger < FingerCount; finger++)
            {
                float2 pct = percentages[finger];
                int baseIdx = finger * JointsPerFinger;
                for (int joint = 0; joint < JointsPerFinger; joint++)
                {
                    destination[baseIdx + joint] = SampleJoint(finger, joint, pct);
                }
            }
        }

        // ────────────────────────────────────────────────────────────
        //  Acquisition
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Fills <paramref name="target"/> with the grid for <paramref name="animator"/>'s avatar,
        /// restoring from <see cref="BasisAvatarModelCache"/> when it is already baked and baking
        /// (then storing) when it is not.
        ///
        /// The cache is keyed on the Avatar ASSET, so a crowd wearing the same avatar bakes once —
        /// which matters because a bake instantiates a hidden duplicate and runs 441 SetHumanPose
        /// calls, and remote players now need this too.
        /// </summary>
        public static bool TryAcquire(Animator animator, float increment, BasisHandPoseGrid target)
        {
            if (animator == null || target == null) return false;

            EntityId key = BasisAvatarModelCache.GetKey(animator);
            if (key != EntityId.None
                && BasisAvatarModelCache.TryGet(key, out var cached)
                && cached.HandPoseGrid != null)
            {
                target.RestoreFrom(cached.HandPoseGrid);
                return target.IsCreated;
            }

            if (!target.TryBake(animator, increment, out BakeResult bake)) return false;

            if (key != EntityId.None)
            {
                var entry = BasisAvatarModelCache.GetOrCreate(key);
                entry.HandPoseGrid = new BasisAvatarModelCache.HandPoseGridData
                {
                    NativeGridSnapshot = target.ToSnapshot(),
                    GridWidth = target.GridWidth,
                    GridHeight = target.GridHeight,
                    FingerStride = target.FingerStride,
                    TotalElements = target.Cells.Length,
                    Increment = target.Increment,

                    LeftThumb = bake.LeftThumb,
                    LeftIndex = bake.LeftIndex,
                    LeftMiddle = bake.LeftMiddle,
                    LeftRing = bake.LeftRing,
                    LeftLittle = bake.LeftLittle,
                    RightThumb = bake.RightThumb,
                    RightIndex = bake.RightIndex,
                    RightMiddle = bake.RightMiddle,
                    RightRing = bake.RightRing,
                    RightLittle = bake.RightLittle,

                    InitialPose = bake.RestPose,
                };
            }
            return target.IsCreated;
        }

        /// <summary>
        /// Expands ten curl/splay pairs into the thirty finger joint rotations, writing them into
        /// <paramref name="boneRotations"/> at the wire slots the finger joints occupy.
        ///
        /// The values written are this rig's LOCAL rotations, not generic-space ones. The receiver
        /// pairs this with identity decode operators on those slots, so the compose job's
        /// <c>DecodePre * value * DecodePost</c> passes them through untouched — which avoids a
        /// generic-space round trip that would only ever undo itself.
        /// </summary>
        public void ExpandInto(in NativeArray<float2> percentages, NativeArray<quaternion> boneRotations, int firstFingerSlot)
        {
            if (!IsCreated || !percentages.IsCreated || !boneRotations.IsCreated) return;

            // BONE_WRITE_ORDER groups the finger slots by joint tier — all ten proximals, then all
            // ten intermediates, then all ten distals — so the slot for (finger, joint) is
            // joint*10 + finger, not finger*3 + joint.
            for (int finger = 0; finger < FingerCount; finger++)
            {
                float2 pct = percentages[finger];
                for (int joint = 0; joint < JointsPerFinger; joint++)
                {
                    int slot = firstFingerSlot + joint * FingerCount + finger;
                    if ((uint)slot >= (uint)boneRotations.Length) continue;
                    boneRotations[slot] = SampleJoint(finger, joint, pct);
                }
            }
        }

        // ────────────────────────────────────────────────────────────
        //  Cache round-trip
        // ────────────────────────────────────────────────────────────

        public void RestoreFrom(BasisAvatarModelCache.HandPoseGridData cached)
        {
            Dispose();

            GridWidth = cached.GridWidth;
            GridHeight = cached.GridHeight;
            FingerStride = cached.FingerStride;
            Increment = cached.Increment > 0f ? cached.Increment : DefaultIncrement;

            Cells = new NativeArray<quaternion>(cached.TotalElements, Allocator.Persistent);
            float[] snapshot = cached.NativeGridSnapshot;
            for (int i = 0; i < cached.TotalElements; i++)
            {
                int b = i * 4;
                Cells[i] = new quaternion(snapshot[b], snapshot[b + 1], snapshot[b + 2], snapshot[b + 3]);
            }
        }

        public float[] ToSnapshot()
        {
            int total = Cells.Length;
            float[] snapshot = new float[total * 4];
            for (int i = 0; i < total; i++)
            {
                quaternion q = Cells[i];
                int b = i * 4;
                snapshot[b] = q.value.x;
                snapshot[b + 1] = q.value.y;
                snapshot[b + 2] = q.value.z;
                snapshot[b + 3] = q.value.w;
            }
            return snapshot;
        }

        // ────────────────────────────────────────────────────────────
        //  Bake
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Result of sampling Unity muscle space across the curl/splay square on a throwaway copy of
        /// the avatar. <paramref name="restPose"/> is the pose recorded before the sweep started.
        /// </summary>
        public struct BakeResult
        {
            public BasisPoseData RestPose;
            public float[] LeftThumb, LeftIndex, LeftMiddle, LeftRing, LeftLittle;
            public float[] RightThumb, RightIndex, RightMiddle, RightRing, RightLittle;
        }

        const int MuscleLeftThumb = 55;

        /// <summary>
        /// Bakes the grid off a hidden duplicate of <paramref name="source"/>. The duplicate is
        /// destroyed before returning; the source animator is never posed.
        /// </summary>
        public bool TryBake(Animator source, float increment, out BakeResult result)
        {
            result = default;
            if (source == null) return false;

            // Drop any previous grid up front so every failure path below leaves this object
            // unusable rather than holding the LAST avatar's fingers, which would silently pose the
            // new rig with the old one's curl map instead of falling back to the bind pose.
            Dispose();

            Increment = increment > 0f ? increment : DefaultIncrement;
            GridWidth = Mathf.RoundToInt(2f / Increment) + 1;
            GridHeight = GridWidth;

            GameObject copy = UnityEngine.Object.Instantiate(source.gameObject);
            copy.SetActive(false);
            try
            {
                if (!copy.TryGetComponent(out Animator animator)) return false;

                BasisTransformMapping mapping = new BasisTransformMapping();
                if (!BasisTransformMapping.AutoDetectReferences(animator, animator.transform, ref mapping, detectArmTwist: false))
                {
                    return false;
                }

                Transform[] joints = AggregateFingerTransforms(mapping);
                bool[] present = AggregateHasProximal(mapping);

                PutIntoTPose(animator);

                HumanPoseHandler poseHandler = new HumanPoseHandler(animator.avatar, animator.transform);
                try
                {
                    HumanPose tpose = new HumanPose();
                    poseHandler.GetHumanPose(ref tpose);

                    result.LeftThumb = CopyMuscles(tpose, 0);
                    result.LeftIndex = CopyMuscles(tpose, 4);
                    result.LeftMiddle = CopyMuscles(tpose, 8);
                    result.LeftRing = CopyMuscles(tpose, 12);
                    result.LeftLittle = CopyMuscles(tpose, 16);
                    result.RightThumb = CopyMuscles(tpose, 20);
                    result.RightIndex = CopyMuscles(tpose, 24);
                    result.RightMiddle = CopyMuscles(tpose, 28);
                    result.RightRing = CopyMuscles(tpose, 32);
                    result.RightLittle = CopyMuscles(tpose, 36);

                    result.RestPose = RecordPose(joints, present);

                    int gridCount = GridWidth * GridHeight;
                    FingerStride = gridCount * JointsPerFinger;

                    // Release only the cells. Dispose() also zeroes the dimensions, and calling it
                    // here would clear the FingerStride computed one line up — allocating 10 * 0
                    // cells and leaving a grid that reports IsCreated while every sample indexes
                    // out of bounds inside Burst.
                    if (Cells.IsCreated) Cells.Dispose();
                    Cells = new NativeArray<quaternion>(FingerCount * FingerStride, Allocator.Persistent);

                    var muscles = new[]
                    {
                        result.LeftThumb, result.LeftIndex, result.LeftMiddle, result.LeftRing, result.LeftLittle,
                        result.RightThumb, result.RightIndex, result.RightMiddle, result.RightRing, result.RightLittle,
                    };

                    for (int xi = 0; xi < GridWidth; xi++)
                    {
                        for (int yi = 0; yi < GridHeight; yi++)
                        {
                            float curl = -1f + xi * Increment;
                            float splay = -1f + yi * Increment;
                            BasisPoseData pose = SetAndRecord(curl, splay, poseHandler, ref tpose, joints, present, muscles);
                            WriteGridCell(xi * GridHeight + yi, pose);
                        }
                    }
                }
                finally
                {
                    poseHandler.Dispose();
                }
            }
            finally
            {
                DestroyCopy(copy);
            }

            return true;
        }

        /// <summary>
        /// Object.Destroy is deferred and logs an error outside play mode, which would leave the
        /// duplicate alive for the rest of the frame and fail any edit-mode caller. Editor tooling
        /// and the rig tests both bake outside play mode.
        /// </summary>
        static void DestroyCopy(GameObject copy)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(copy);
            else UnityEngine.Object.DestroyImmediate(copy);
        }

        static float[] CopyMuscles(HumanPose tpose, int fingerOffset)
        {
            float[] muscles = new float[4];
            Array.Copy(tpose.muscles, MuscleLeftThumb + fingerOffset, muscles, 0, 4);
            return muscles;
        }

        BasisPoseData SetAndRecord(float curl, float splay, HumanPoseHandler poseHandler, ref HumanPose pose,
            Transform[] joints, bool[] present, float[][] muscles)
        {
            for (int finger = 0; finger < FingerCount; finger++)
            {
                float[] slice = muscles[finger];
                Array.Fill(slice, curl);
                slice[1] = splay;
                Array.Copy(slice, 0, pose.muscles, MuscleLeftThumb + finger * 4, 4);
            }

            poseHandler.SetHumanPose(ref pose);
            return RecordPose(joints, present);
        }

        void WriteGridCell(int gridIdx, BasisPoseData pose)
        {
            WriteFinger(0, gridIdx, pose.LeftThumb);
            WriteFinger(1, gridIdx, pose.LeftIndex);
            WriteFinger(2, gridIdx, pose.LeftMiddle);
            WriteFinger(3, gridIdx, pose.LeftRing);
            WriteFinger(4, gridIdx, pose.LeftLittle);
            WriteFinger(5, gridIdx, pose.RightThumb);
            WriteFinger(6, gridIdx, pose.RightIndex);
            WriteFinger(7, gridIdx, pose.RightMiddle);
            WriteFinger(8, gridIdx, pose.RightRing);
            WriteFinger(9, gridIdx, pose.RightLittle);
        }

        void WriteFinger(int fingerIdx, int gridIdx, Quaternion[] finger)
        {
            int idx = fingerIdx * FingerStride + gridIdx * JointsPerFinger;
            Cells[idx] = finger[0];
            Cells[idx + 1] = finger[1];
            Cells[idx + 2] = finger[2];
        }

        static BasisPoseData RecordPose(Transform[] joints, bool[] present)
        {
            BasisPoseData pose = new BasisPoseData();
            int index = 0;

            void Assign(ref Quaternion[] finger)
            {
                finger[0] = present[index] ? joints[index].localRotation : Quaternion.identity; index++;
                finger[1] = present[index] ? joints[index].localRotation : Quaternion.identity; index++;
                finger[2] = present[index] ? joints[index].localRotation : Quaternion.identity; index++;
            }

            Assign(ref pose.LeftThumb);
            Assign(ref pose.LeftIndex);
            Assign(ref pose.LeftMiddle);
            Assign(ref pose.LeftRing);
            Assign(ref pose.LeftLittle);
            Assign(ref pose.RightThumb);
            Assign(ref pose.RightIndex);
            Assign(ref pose.RightMiddle);
            Assign(ref pose.RightRing);
            Assign(ref pose.RightLittle);

            return pose;
        }

        static Transform[] AggregateFingerTransforms(BasisTransformMapping m)
        {
            var all = new Transform[JointCount];
            var fingers = new[]
            {
                m.LeftThumb, m.LeftIndex, m.LeftMiddle, m.LeftRing, m.LeftLittle,
                m.RightThumb, m.RightIndex, m.RightMiddle, m.RightRing, m.RightLittle,
            };
            for (int f = 0; f < FingerCount; f++)
            {
                for (int j = 0; j < JointsPerFinger; j++) all[f * JointsPerFinger + j] = fingers[f][j];
            }
            return all;
        }

        static bool[] AggregateHasProximal(BasisTransformMapping m)
        {
            var all = new bool[JointCount];
            var has = new[]
            {
                m.HasLeftThumb, m.HasLeftIndex, m.HasLeftMiddle, m.HasLeftRing, m.HasLeftLittle,
                m.HasRightThumb, m.HasRightIndex, m.HasRightMiddle, m.HasRightRing, m.HasRightLittle,
            };
            for (int f = 0; f < FingerCount; f++)
            {
                for (int j = 0; j < JointsPerFinger; j++) all[f * JointsPerFinger + j] = has[f][j];
            }
            return all;
        }

        /// <summary>
        /// Poses the throwaway duplicate only. Deliberately does NOT stash the controller in
        /// BasisLocalAvatarDriver.SavedruntimeAnimatorController the way the local-only bake did:
        /// that static doubles as the local player's "currently T-posing" flag
        /// (BasisLocomotionPoseSystem), and a remote's bake setting it would freeze the LOCAL
        /// player's locomotion pose. The copy is destroyed either way, so nothing needs restoring.
        /// </summary>
        static void PutIntoTPose(Animator animator)
        {
            animator.logWarnings = false;
            animator.runtimeAnimatorController = BasisPlayerFactory.TposeController;
            animator.Update(Time.deltaTime);
        }
    }
}
