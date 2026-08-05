using System.Runtime.CompilerServices;

namespace Basis.Network.Core.Compression
{
    /// <summary>
    /// Rig-neutral ("generic") bone rotation space — the wire representation for humanoid pose.
    ///
    /// THE PROBLEM THIS SOLVES
    /// ───────────────────────
    /// The wire used to carry a bone's T-pose-relative LOCAL delta:
    ///
    ///     d = conj(T) * C          T = rest local rotation, C = current local rotation
    ///
    /// d is a rotation expressed in the BONE'S OWN rest frame — the local axes the modeller
    /// happened to author. One rig runs +X down the forearm, the next runs +Y down it, a third
    /// is rolled 30° about the bone. The same physical "bend the elbow 90°" is a different d on
    /// each of them, so d is only meaningful to the rig that produced it: replay it on any other
    /// avatar and the limb bends about the wrong axis. That is what makes the old stream
    /// non-remappable.
    ///
    /// THE REPRESENTATION
    /// ──────────────────
    /// Let F be the bone's rest orientation RELATIVE TO THE AVATAR ROOT (BasisTransformMapping's
    /// TposeFromRoot rotation — the root's rotation divided out of the bone's rest world
    /// rotation). Conjugating the local delta by it re-expresses the same rotation in root axes:
    ///
    ///     g = F * d * conj(F)                                              [ENCODE]
    ///     d = conj(F) * g * F                                              [DECODE]
    ///
    /// g is "how far this joint has turned away from its own rest pose, measured in the
    /// character's own axes" (+X right, +Y up, +Z forward — Unity's humanoid root convention).
    /// Nothing about the source rig survives in it: two avatars whose forearm bones point down
    /// completely different local axes produce the SAME g for the same visible elbow bend, and a
    /// receiver reconstructs its own rig's d from its own F.
    ///
    /// Composing encode-on-A with decode-on-B gives the standard bone-axis retarget,
    /// d_B = conj(F_B) * F_A * d_A * conj(F_A) * F_B, but neither side ever needs to know the
    /// other's rig — the shared root frame is the whole contract.
    ///
    /// WHY THIS SHAPE AND NOT THE OBVIOUS ONE
    /// ──────────────────────────────────────
    /// The obvious rig-neutral quantity is the bone's absolute root-space orientation, or its
    /// root-space rotation from rest INCLUDING ancestors. Both are avatar-agnostic too, and both
    /// are unusable here: a bone's accumulated rotation carries every ancestor's motion, so a
    /// finger's value swings through the shoulder's full range. The per-bone quantisation budget
    /// in <see cref="BasisBoneRotationCompression"/> (5 bits and a 0.58 component range on a
    /// finger distal) is sized to that joint's OWN range of motion and would clip hopelessly.
    ///
    /// Conjugation is an isometry of SO(3): angle(g) == angle(d), exactly, for every bone. So
    /// this representation is a pure change of axes with no change of magnitude, and every
    /// downstream tuning stays valid unaltered — the BPC tables, the MAX_COMPONENT ranges, the
    /// deadband thresholds, the delta-compression byte statistics, and the packet size. It is
    /// also flat: no root-to-leaf hierarchy walk on either side, so the existing per-bone
    /// parallel jobs keep their shape.
    ///
    /// FOLDED OPERATORS
    /// ────────────────
    /// Expanded against the raw transform values the round trip is
    ///
    ///     g  = (F_A * conj(T_A)) * C_A * conj(F_A)
    ///     C_B = (T_B * conj(F_B)) * g * F_B
    ///
    /// and the parenthesised factors are constant for the life of an avatar. BuildEncodeOperators
    /// / BuildDecodeOperators precompute them, leaving the per-bone hot path at two quaternion
    /// multiplies instead of the old one — see the Burst jobs on either end.
    ///
    /// LEGACY EQUIVALENCE
    /// ──────────────────
    /// F == identity collapses the encode operators to (conj(T), identity) and the decode
    /// operators to (T, identity), i.e. g == d and C == T * g: the old scheme, exactly. A bone
    /// whose rest frame is unknown therefore degrades to the previous behaviour rather than to
    /// garbage, which is what the missing-bone and un-calibrated paths rely on.
    ///
    /// CONTRACT
    /// ────────
    /// Both ends must measure F in the same frame: the avatar's own root, with the rig in its
    /// canonical Unity humanoid T-pose (which fixes root-space facing at +Z / up at +Y). Both
    /// ends already record exactly that during calibration, so neither has to transmit anything
    /// about its rig. This is a wire-format change — see BasisNetworkVersion.
    ///
    /// Pure C#, no Unity dependency, so the server, the headless console client and the edit-mode
    /// tests all run the identical math the Burst jobs do.
    /// </summary>
    public static class BasisGenericBoneRotation
    {
        /// <summary>
        /// Minimal unit-quaternion POD in (x, y, z, w) order — the same component order as
        /// UnityEngine.Quaternion and Unity.Mathematics.quaternion, so the Unity wrappers
        /// reinterpret rather than convert.
        /// </summary>
        public readonly struct Quat
        {
            public readonly float x, y, z, w;

            public Quat(float x, float y, float z, float w)
            {
                this.x = x;
                this.y = y;
                this.z = z;
                this.w = w;
            }

            public static Quat Identity => new Quat(0f, 0f, 0f, 1f);

            public override string ToString() => $"({x:F6}, {y:F6}, {z:F6}, {w:F6})";
        }

        /// <summary>Hamilton product, matching Unity's a*b convention (apply b, then a).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quat Mul(in Quat a, in Quat b)
        {
            return new Quat(
                a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
                a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
                a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
                a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        }

        /// <summary>Inverse of a UNIT quaternion. Every rotation here is unit-length by construction.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quat Conjugate(in Quat q) => new Quat(-q.x, -q.y, -q.z, q.w);

        /// <summary>
        /// Renormalises, falling back to identity on a degenerate input. Rest frames come from
        /// calibration and can be (0,0,0,0) for a bone the rig does not have.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quat Normalize(in Quat q)
        {
            float lenSq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (lenSq < 1e-12f) return Quat.Identity;
            float inv = 1f / (float)System.Math.Sqrt(lenSq);
            return new Quat(q.x * inv, q.y * inv, q.z * inv, q.w * inv);
        }

        // ────────────────────────────────────────────────────────────
        //  Single-bone conversions (reference form — clarity over speed)
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Rig-local delta → generic. <paramref name="restFrame"/> is F, the bone's rest rotation
        /// relative to the avatar root.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quat LocalDeltaToGeneric(in Quat restFrame, in Quat localDelta)
            => Mul(Mul(restFrame, localDelta), Conjugate(restFrame));

        /// <summary>Generic → rig-local delta, using THIS rig's rest frame.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quat GenericToLocalDelta(in Quat restFrame, in Quat generic)
            => Mul(Mul(Conjugate(restFrame), generic), restFrame);

        /// <summary>
        /// Current local rotation → generic, in one step. Equivalent to
        /// <c>LocalDeltaToGeneric(restFrame, conj(tposeLocal) * currentLocal)</c>.
        /// </summary>
        public static Quat ToGeneric(in Quat restFrame, in Quat tposeLocal, in Quat currentLocal)
            => LocalDeltaToGeneric(restFrame, Mul(Conjugate(tposeLocal), currentLocal));

        /// <summary>
        /// Generic → current local rotation for a rig described by
        /// (<paramref name="restFrame"/>, <paramref name="tposeLocal"/>).
        /// </summary>
        public static Quat FromGeneric(in Quat restFrame, in Quat tposeLocal, in Quat generic)
            => Mul(tposeLocal, GenericToLocalDelta(restFrame, generic));

        // ────────────────────────────────────────────────────────────
        //  Folded operators — what the per-frame jobs actually use
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Precomputes the pair that turns a live local rotation into the generic wire value:
        /// <c>g = pre * currentLocal * post</c>.
        /// </summary>
        public static void BuildEncodeOperators(in Quat restFrame, in Quat tposeLocal, out Quat pre, out Quat post)
        {
            Quat f = Normalize(restFrame);
            Quat t = Normalize(tposeLocal);
            pre = Mul(f, Conjugate(t));
            post = Conjugate(f);
        }

        /// <summary>
        /// Precomputes the pair that turns a generic wire value into THIS rig's local rotation:
        /// <c>currentLocal = pre * g * post</c>.
        /// </summary>
        public static void BuildDecodeOperators(in Quat restFrame, in Quat tposeLocal, out Quat pre, out Quat post)
        {
            Quat f = Normalize(restFrame);
            Quat t = Normalize(tposeLocal);
            pre = Mul(t, Conjugate(f));
            post = f;
        }

        /// <summary>Applies a folded operator pair. Same expression on both ends.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quat Apply(in Quat pre, in Quat middle, in Quat post)
            => Mul(Mul(pre, middle), post);

        // ────────────────────────────────────────────────────────────
        //  Whole-skeleton operator tables
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds per-slot encode operators in <see cref="BasisBoneRotationCompression.BONE_WRITE_ORDER"/>
        /// order. <paramref name="restFrameByBone"/> and <paramref name="tposeLocalByBone"/> are
        /// indexed by HumanBodyBones enum value (length &gt;= 55); the outputs are indexed by wire
        /// slot (length <see cref="BasisBoneRotationCompression.SyncBoneCount"/>).
        /// </summary>
        public static void BuildEncodeOperatorTable(
            Quat[] restFrameByBone, Quat[] tposeLocalByBone, Quat[] outPre, Quat[] outPost)
        {
            int[] order = BasisBoneRotationCompression.BONE_WRITE_ORDER;
            for (int slot = 0; slot < BasisBoneRotationCompression.SyncBoneCount; slot++)
            {
                int bone = order[slot];
                BuildEncodeOperators(restFrameByBone[bone], tposeLocalByBone[bone], out outPre[slot], out outPost[slot]);
            }
        }

        /// <summary>Slot-order decode operators; mirror of <see cref="BuildEncodeOperatorTable"/>.</summary>
        public static void BuildDecodeOperatorTable(
            Quat[] restFrameByBone, Quat[] tposeLocalByBone, Quat[] outPre, Quat[] outPost)
        {
            int[] order = BasisBoneRotationCompression.BONE_WRITE_ORDER;
            for (int slot = 0; slot < BasisBoneRotationCompression.SyncBoneCount; slot++)
            {
                int bone = order[slot];
                BuildDecodeOperators(restFrameByBone[bone], tposeLocalByBone[bone], out outPre[slot], out outPost[slot]);
            }
        }
    }
}
