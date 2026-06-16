using UnityEngine;

/// <summary>
/// Pose-tolerant calibration math. The player will not hold a perfect straight T-pose when they
/// calibrate; a mid-joint tracker (elbow / knee) lets us reconstruct the limb's TRUE straightened
/// geometry and pose the avatar bone to MIRROR the player's actual bend, so the captured offset and
/// the measured limb length stop depending on the calibration pose.
///
/// Two corrections, both pure Vector3/Quaternion math, shared by the runtime calibration path and the
/// offline BasisCalibrationPoseSweep so the sweep exercises the real formulas:
///   1. Bend-invariant limb LENGTH (|root->mid| + |mid->end|) for device scale, instead of the
///      straight-line root->end distance that shrinks the moment an elbow/knee bends.
///   2. Pose-matched avatar bone TARGETS (avatar limb posed to the player's bend) as the reference for
///      BasisInput.CalculateOffset, instead of the avatar's straight T-pose bone.
/// </summary>
public struct BasisLimbCalibrationInput
{
    // Player-side tracked joints at calibration (world space): shoulder/elbow/hand or hip/knee/foot.
    public Vector3 PlayerRoot;
    public Vector3 PlayerMid;
    public Vector3 PlayerEnd;
    public bool HasMid;                 // a real mid-joint tracker (elbow/knee) is present

    // Avatar bone world poses captured in the clean T-pose, and the avatar segment lengths.
    public Vector3 AvatarRoot;
    public Vector3 AvatarMidTpose;
    public Vector3 AvatarEndTpose;
    public Quaternion AvatarMidRotTpose;
    public Quaternion AvatarEndRotTpose;
    public float AvatarUpperLen;        // avatar root->mid bone length
    public float AvatarLowerLen;        // avatar mid->end bone length
}

public struct BasisLimbCalibrationResult
{
    public bool Apply;                  // a mid tracker was usable; compensation produced

    public float PlayerUpperLen;        // |mid - root|
    public float PlayerLowerLen;        // |end - mid|
    public float PlayerLimbLength;      // bend-invariant total = upper + lower (feed device scale)
    public float StraightLineDist;      // |end - root| (the bent, pose-dependent length used today)
    public float BendAngleDeg;          // interior angle at the mid joint (180 = straight)
    public float StraightDeficit;       // PlayerLimbLength - StraightLineDist (reach lost to the bend)

    // Avatar bone poses re-posed to MIRROR the player's bend (the corrected offset reference).
    public Vector3 AvatarMidMatched;
    public Vector3 AvatarEndMatched;
    public Quaternion AvatarMidRotMatched;
    public Quaternion AvatarEndRotMatched;

    public float QualityScore;          // 0..1 expected post-calibration correctness for this limb
}

public static class BasisCalibrationPoseCore
{
    const float k_Epsilon = 1e-6f;
    // A no-mid-tracker calibration degrades linearly with how far the limb is from straight; past this
    // bound it is treated as fully unreliable (the limb can't be reconstructed without a mid tracker).
    public const float k_MaxUncompensatedBendDeg = 45f;

    public static void Solve(in BasisLimbCalibrationInput i, out BasisLimbCalibrationResult r)
    {
        r = default;

        Vector3 upper = i.PlayerMid - i.PlayerRoot;
        Vector3 lower = i.PlayerEnd - i.PlayerMid;
        float upperLen = upper.magnitude;
        float lowerLen = lower.magnitude;

        r.PlayerUpperLen = upperLen;
        r.PlayerLowerLen = lowerLen;
        r.PlayerLimbLength = upperLen + lowerLen;
        r.StraightLineDist = (i.PlayerEnd - i.PlayerRoot).magnitude;
        r.StraightDeficit = Mathf.Max(0f, r.PlayerLimbLength - r.StraightLineDist);
        r.BendAngleDeg = BendAngle(i.PlayerRoot, i.PlayerMid, i.PlayerEnd);

        if (!i.HasMid || upperLen < k_Epsilon || lowerLen < k_Epsilon)
        {
            // No usable mid tracker: cannot reconstruct the bend, so fall back to the T-pose reference.
            // Quality decays with how bent the player was (that bend leaks straight into the offset).
            r.Apply = false;
            r.AvatarMidMatched = i.AvatarMidTpose;
            r.AvatarEndMatched = i.AvatarEndTpose;
            r.AvatarMidRotMatched = i.AvatarMidRotTpose;
            r.AvatarEndRotMatched = i.AvatarEndRotTpose;
            r.QualityScore = UncompensatedQuality(r.BendAngleDeg);
            return;
        }

        Vector3 upperDir = upper / upperLen;
        Vector3 lowerDir = lower / lowerLen;

        // Pose the avatar limb to mirror the player's bend: walk the AVATAR's segment lengths along the
        // PLAYER's segment directions, anchored at the avatar's root bone.
        r.AvatarMidMatched = i.AvatarRoot + upperDir * i.AvatarUpperLen;
        r.AvatarEndMatched = r.AvatarMidMatched + lowerDir * i.AvatarLowerLen;

        Vector3 tposeUpperDir = i.AvatarMidTpose - i.AvatarRoot;
        Vector3 tposeLowerDir = i.AvatarEndTpose - i.AvatarMidTpose;
        r.AvatarMidRotMatched = AlignRotation(i.AvatarMidRotTpose, tposeUpperDir, upperDir);
        r.AvatarEndRotMatched = AlignRotation(i.AvatarEndRotTpose, tposeLowerDir, lowerDir);

        r.QualityScore = CompensatedQuality(r.BendAngleDeg);
        r.Apply = true;
    }

    /// <summary>
    /// The captured inverse offset for the END bone (hand/foot), using either the pose-matched reference
    /// (the fix) or the avatar T-pose bone (today). Reuses the real BasisCalibrationMath formula so the
    /// sweep proves the runtime behaviour, not a copy.
    /// </summary>
    public static void CaptureEndOffset(bool compensated, Vector3 trackerPos, Quaternion trackerRot,
        in BasisLimbCalibrationResult r, in BasisLimbCalibrationInput i,
        out Vector3 offsetPos, out Quaternion offsetRot)
    {
        Vector3 bonePos = compensated ? r.AvatarEndMatched : i.AvatarEndTpose;
        Quaternion boneRot = compensated ? r.AvatarEndRotMatched : i.AvatarEndRotTpose;
        BasisCalibrationMath.ComputeInverseOffset(trackerPos, trackerRot, bonePos, boneRot, out offsetPos, out offsetRot);
    }

    /// <summary>Interior angle at the mid joint, between (root-mid) and (end-mid). 180 = straight.</summary>
    public static float BendAngle(Vector3 root, Vector3 mid, Vector3 end)
    {
        Vector3 a = root - mid;
        Vector3 b = end - mid;
        if (a.sqrMagnitude < k_Epsilon || b.sqrMagnitude < k_Epsilon)
        {
            return 180f;
        }
        return Vector3.Angle(a, b);
    }

    public static float BendDeviationDeg(float bendAngleDeg) => Mathf.Abs(180f - bendAngleDeg);

    // With a mid tracker the bend is reconstructed, so a bent pose still calibrates well; only a tiny
    // penalty for a near-folded limb where the segment directions get noisy.
    static float CompensatedQuality(float bendAngleDeg)
    {
        float dev = BendDeviationDeg(bendAngleDeg);
        return Mathf.Clamp01(1f - 0.15f * Mathf.Clamp01((dev - 90f) / 90f));
    }

    // Without a mid tracker the bend leaks into the offset, so quality falls off as the pose bends.
    static float UncompensatedQuality(float bendAngleDeg)
    {
        float dev = BendDeviationDeg(bendAngleDeg);
        return Mathf.Clamp01(1f - dev / k_MaxUncompensatedBendDeg);
    }

    static Quaternion AlignRotation(Quaternion baseRot, Vector3 fromDir, Vector3 toDir)
    {
        if (fromDir.sqrMagnitude < k_Epsilon || toDir.sqrMagnitude < k_Epsilon)
        {
            return baseRot;
        }
        return Quaternion.FromToRotation(fromDir.normalized, toDir.normalized) * baseRot;
    }
}
