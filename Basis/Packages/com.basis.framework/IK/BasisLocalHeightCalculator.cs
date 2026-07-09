using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

public static class BasisLocalHeightCalculator
{
    // 30% tolerance band
    private const float EyeArmTolerance = 0.30f;
    public static void CalculatePlayerArmSpan()
    {
        bool hasLeft = BasisDeviceManagement.Instance.FindDevice(out BasisInput left, BasisBoneTrackedRole.LeftHand);
        bool hasRight = BasisDeviceManagement.Instance.FindDevice(out BasisInput right, BasisBoneTrackedRole.RightHand);

        if (!hasLeft && !hasRight)
        {
            // Keep the seeded/last-known span when the hands simply aren't tracked yet (boot, sleeping
            // controllers) — only fall back when we have nothing plausible at all.
            if (BasisHeightDriver.PlayerArmSpan < BasisHeightDriver.MinPlausibleBodyMeasure
                || BasisHeightDriver.PlayerArmSpan > BasisHeightDriver.MaxPlausibleBodyMeasure)
            {
                BasisDebug.LogWarning("No hands found. Using fallback.", BasisDebug.LogTag.Avatar);
                BasisHeightDriver.PlayerArmSpan = BasisHeightDriver.FallbackHeightInMeters;
            }
            return;
        }

        // If one hand missing, we can't do hand-to-hand; fall back to head->hand *2 as you did.
        var lockToInput = BasisLocalCameraDriver.Instance?.BasisLockToInput;
        if (!hasLeft || !hasRight)
        {
            if (lockToInput?.BasisInput == null)
            {
                if (BasisHeightDriver.PlayerArmSpan < BasisHeightDriver.MinPlausibleBodyMeasure
                    || BasisHeightDriver.PlayerArmSpan > BasisHeightDriver.MaxPlausibleBodyMeasure)
                {
                    BasisHeightDriver.PlayerArmSpan = BasisHeightDriver.FallbackHeightInMeters;
                }
                return;
            }

            // poll all inputs we have
            lockToInput.BasisInput.LatePollData();
            if (hasLeft) left.LatePollData();
            if (hasRight) right.LatePollData();

            var head = lockToInput.BasisInput.UnscaledDeviceCoord.position;
            var hand = hasLeft ? left.UnscaledDeviceCoord.position : right.UnscaledDeviceCoord.position;

            var headFlat = new Vector3(head.x, 0f, head.z);
            var handFlat = new Vector3(hand.x, 0f, hand.z);

            BasisHeightDriver.PlayerArmSpan = Vector3.Distance(headFlat, handFlat) * 2f;
            return;
        }

        // poll both hands as close together as possible
        left.LatePollData();
        right.LatePollData();

        Vector3 l = left.UnscaledDeviceCoord.position;
        Vector3 r = right.UnscaledDeviceCoord.position;

        Vector3 lFlat = new Vector3(l.x, 0f, l.z);
        Vector3 rFlat = new Vector3(r.x, 0f, r.z);
        float span = Vector3.Distance(lFlat, rFlat);

        BasisHeightDriver.PlayerArmSpan = span;
        BasisDebug.Log($"Player hand-to-hand arm span: {BasisHeightDriver.PlayerArmSpan}", BasisDebug.LogTag.Avatar);
    }

    public static void CalculatePlayerEyeHeight()
    {
        var headInput = BasisLocalCameraDriver.Instance?.BasisLockToInput?.BasisInput;
        BasisHeightDriver.PlayerCenterEyeVerticalOffset = headInput != null ? headInput.CenterEyeVerticalOffset : 0f;

        bool genuine = true;

        if (SMModuleSitStand.IsSteatedMode)
        {
            BasisHeightDriver.PlayerCenterEyeVerticalOffset = 0f;
            BasisHeightDriver.PlayerEyeHeight = BasisHeightDriver.FallbackHeightInMeters;
            // NOT genuine: this is the virtual standing eye, not the player's body. Leaving it genuine
            // locked 1.61 m in as the "known standing height", so leaving seated mode could never
            // restore the real one (the persisted-size seed only fills in when nothing genuine exists).
            genuine = false;
            BasisDebug.Log($"Seated mode; using standard eye height {BasisHeightDriver.PlayerEyeHeight}", BasisDebug.LogTag.Avatar);
        }
        else if (BasisHeightDriver.HasPitchCalibratedHeight)
        {
            BasisHeightDriver.PlayerEyeHeight = BasisHeightDriver.PitchCalibratedEyeHeight;
            BasisDebug.Log($"Using pitch-calibrated eye height: {BasisHeightDriver.PlayerEyeHeight}", BasisDebug.LogTag.Avatar);
        }
        else
        {
            var lockToInput = BasisLocalCameraDriver.Instance?.BasisLockToInput;
            if (lockToInput != null && lockToInput.BasisInput != null)
            {
                lockToInput.BasisInput.LatePollData();
                // Subtract the play-space mover's vertical offset so calibrating while lifted doesn't read
                // an inflated eye height (the offset is injected into UnscaledDeviceCoord by the device).
                BasisHeightDriver.PlayerEyeHeight = lockToInput.BasisInput.UnscaledDeviceCoord.position.y - BasisLocalPlayspaceMover.VerticalOffset;
                BasisDebug.Log($"Player raw eye height from device: {BasisHeightDriver.PlayerEyeHeight}", BasisDebug.LogTag.Avatar);
            }
            else
            {
                // Prefer avatar eye height if it looks valid; otherwise fall back to default player height.
                float fallback = BasisHeightDriver.AvatarEyeHeight > 0f ? BasisHeightDriver.AvatarEyeHeight : BasisHeightDriver.FallbackHeightInMeters;

                BasisHeightDriver.PlayerEyeHeight = fallback;
                genuine = false;

                BasisDebug.LogWarning("No attached input found for BasisLockToInput. Using fallback player eye height.", BasisDebug.LogTag.Avatar);
            }
        }
        if (BasisHeightDriver.PlayerEyeHeight <= 0f)
        {
            BasisHeightDriver.PlayerEyeHeight = BasisHeightDriver.FallbackHeightInMeters;
            genuine = false;
            BasisDebug.LogWarning($"Player eye height was invalid. Set to default: {BasisHeightDriver.FallbackHeightInMeters}", BasisDebug.LogTag.Avatar);
        }

        BasisHeightDriver.HasGenuinePlayerEyeHeight = genuine;
    }

    /// <summary>
    /// Captures one HMD sample for pitch calibration: pitchRadians is the gaze pitch (positive =
    /// looking up), eyeY is the HMD height with the play-space mover's vertical offset removed.
    /// Returns false when no HMD device is available.
    /// </summary>
    public static bool CaptureHMDPitchSample(out float pitchRadians, out float eyeY)
    {
        pitchRadians = 0f;
        eyeY = -1f;
        var lockToInput = BasisLocalCameraDriver.Instance?.BasisLockToInput;
        if (lockToInput != null && lockToInput.BasisInput != null)
        {
            lockToInput.BasisInput.LatePollData();
            var coord = lockToInput.BasisInput.UnscaledDeviceCoord;
            eyeY = coord.position.y - BasisLocalPlayspaceMover.VerticalOffset;
            Vector3 forward = coord.rotation * Vector3.forward;
            pitchRadians = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f));
            return true;
        }
        return false;
    }

    /// <summary>
    /// Recovers the level-gaze eye height from three (pitch, height) HMD samples. As the head
    /// pitches the HMD height follows Y(pitch) = P + A*sin(pitch) + B*cos(pitch) about the neck
    /// pivot; solving that system gives the level-gaze height Y(0) = P + B, independent of whether
    /// the "forward" pose was actually level. Falls back to the forward sample when the samples
    /// are too close together to solve or the result lands outside the up/down range.
    /// Each Vector2 is (x = pitch radians, y = eye height).
    /// </summary>
    public static float ComputePitchCalibratedHeight(Vector2 up, Vector2 down, Vector2 forward)
    {
        float s0 = Mathf.Sin(up.x), c0 = Mathf.Cos(up.x);
        float s1 = Mathf.Sin(down.x), c1 = Mathf.Cos(down.x);
        float s2 = Mathf.Sin(forward.x), c2 = Mathf.Cos(forward.x);
        float y0 = up.y, y1 = down.y, y2 = forward.y;

        float det = (s1 * c2 - c1 * s2) - s0 * (c2 - c1) + c0 * (s2 - s1);
        if (Mathf.Abs(det) < 1e-5f)
        {
            BasisDebug.LogWarning($"Pitch calibration: samples too close to solve (det={det:F6}); using forward height {forward.y:F4}", BasisDebug.LogTag.Avatar);
            return forward.y;
        }

        float detP = y0 * (s1 * c2 - c1 * s2) - s0 * (y1 * c2 - c1 * y2) + c0 * (y1 * s2 - s1 * y2);
        float detB = (s1 * y2 - y1 * s2) - s0 * (y2 - y1) + y0 * (s2 - s1);
        float corrected = (detP + detB) / det;

        float lo = Mathf.Min(up.y, down.y);
        float hi = Mathf.Max(up.y, down.y);
        if (float.IsNaN(corrected) || float.IsInfinity(corrected) || corrected < lo || corrected > hi)
        {
            BasisDebug.LogWarning($"Pitch calibration: solved height {corrected:F4} out of range [{lo:F4},{hi:F4}]; using forward height {forward.y:F4}", BasisDebug.LogTag.Avatar);
            return forward.y;
        }

     //   BasisDebug.Log($"Pitch calibration: up=({up.x:F3},{up.y:F4}) down=({down.x:F3},{down.y:F4}) forward=({forward.x:F3},{forward.y:F4}) corrected={corrected:F4}", BasisDebug.LogTag.Avatar);
        return corrected;
    }
    public static void CalculateAvatarEyeHeight()
    {
        BasisLocalPlayer Local = BasisLocalPlayer.Instance;
        if (Local == null)
        {
            BasisDebug.LogError("Missing BasisLocalPlayer");
            return;
        }
        BasisHeightDriver.AvatarEyeHeight = Local.LocalAvatarDriver.ActiveAvatarEyeHeight();
        BasisHeightDriver.AvatarEyeHeight = BasisHeightDriver.AvatarEyeHeight > 0f ? BasisHeightDriver.AvatarEyeHeight : BasisHeightDriver.FallbackHeightInMeters;
        if (BasisHeightDriver.AvatarEyeHeight <= 0f)
        {
            BasisHeightDriver.AvatarEyeHeight = BasisHeightDriver.FallbackHeightInMeters;
            BasisDebug.LogWarning($"Avatar eye height was invalid. Set to default: {BasisHeightDriver.FallbackHeightInMeters}", BasisDebug.LogTag.Avatar);
        }
    }
    public static void CalculateAvatarArmSpan()
    {
        BasisLocalPlayer Local = BasisLocalPlayer.Instance;
        if (Local == null)
        {
            BasisDebug.LogError("Missing BasisLocalPlayer");
            return;
        }

        // Preferred source: the load-time raw-joint T-pose snapshot (unscaled, root-local) — no live
        // bone read and no dependence on the avatar being physically T-posed or unscaled right now.
        if (BasisLocalAvatarDriver.HasTposeBoneSnapshot
            && BasisLocalAvatarDriver.TposeBoneSnapshot.TryGetValue(BasisBoneTrackedRole.LeftHand, out var leftBind)
            && BasisLocalAvatarDriver.TposeBoneSnapshot.TryGetValue(BasisBoneTrackedRole.RightHand, out var rightBind))
        {
            Vector3 lb = leftBind.position;
            Vector3 rb = rightBind.position;
            BasisHeightDriver.AvatarArmSpan = Vector3.Distance(new Vector3(lb.x, 0f, lb.z), new Vector3(rb.x, 0f, rb.z));
            BasisDebug.Log($"Current Avatar Arm Span (from T-pose snapshot): {BasisHeightDriver.AvatarArmSpan}", BasisDebug.LogTag.Avatar);
            return;
        }

        // Fallback (first capture during avatar load, before the snapshot exists): the avatar is
        // physically T-posed at that point, so live bones are valid.
        Animator animator = Local.BasisAvatar != null ? Local.BasisAvatar.Animator : null;
        Transform leftHand = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        Transform rightHand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;

        if (leftHand == null || rightHand == null)
        {
            BasisHeightDriver.AvatarArmSpan = BasisHeightDriver.AvatarEyeHeight;
            BasisDebug.LogWarning($"Avatar hand bones unavailable; arm span set to avatar eye height: {BasisHeightDriver.AvatarArmSpan}", BasisDebug.LogTag.Avatar);
            return;
        }

        Vector3 l = leftHand.position;
        Vector3 r = rightHand.position;

        Vector3 leftFlat = new Vector3(l.x, 0f, l.z);
        Vector3 rightFlat = new Vector3(r.x, 0f, r.z);

        float ArmLength = Vector3.Distance(leftFlat, rightFlat);
        BasisHeightDriver.AvatarArmSpan = ArmLength;
        BasisDebug.Log($"Current Avatar Arm Span: {BasisHeightDriver.AvatarArmSpan}", BasisDebug.LogTag.Avatar);
    }
    private static void ValidateEyeToArm(ref float eyeHeight, ref float armSpan, float fallbackEyeHeight, string label, float maxAbsoluteSpan)
    {
        // Eye height sanity
        if (eyeHeight <= 0f)
        {
            eyeHeight = fallbackEyeHeight;
            BasisDebug.LogWarning($"{label} eye height invalid; using fallback {fallbackEyeHeight}.", BasisDebug.LogTag.Avatar);
        }

        // Arm span sanity
        if (armSpan <= 0f)
        {
            // Your requested behavior: if arm span invalid, match eye height
            armSpan = eyeHeight;
            BasisDebug.LogWarning($"{label} arm span was invalid. Set to {label} eye height: {armSpan}", BasisDebug.LogTag.Avatar);
            return;
        }

        float minAllowed = eyeHeight * (1f - EyeArmTolerance);
        if (armSpan < minAllowed)
        {
            BasisDebug.LogWarning(
                $"{label} arm span ({armSpan}) is >{EyeArmTolerance:P0} smaller than {label} eye height ({eyeHeight}). " +
                $"Clamping to min allowed: {minAllowed}",
                BasisDebug.LogTag.Avatar
            );
            armSpan = minAllowed;
        }

        float maxAllowed = eyeHeight * (1f + EyeArmTolerance);
        if (armSpan > maxAllowed)
        {
            // Do NOT clamp the span down to the eye-implied band: arms cannot over-measure, so a
            // span far beyond the eye height almost always means the EYE was under-measured
            // (calibrated while physically seated/slouched with arms out) — clamping here destroyed
            // the one good measurement, and clamped authored long-armed avatars too. Only reject
            // spans beyond the caller's absolute plausibility cap.
            if (armSpan > maxAbsoluteSpan)
            {
                BasisDebug.LogWarning(
                    $"{label} arm span ({armSpan}) exceeds the absolute plausibility cap {maxAbsoluteSpan}. Clamping.",
                    BasisDebug.LogTag.Avatar
                );
                armSpan = maxAbsoluteSpan;
            }
            else
            {
                BasisDebug.Log(
                    $"{label} arm span ({armSpan}) is >{EyeArmTolerance:P0} larger than {label} eye height ({eyeHeight}); " +
                    "keeping it — the eye height was likely under-measured (seated/slouched capture).",
                    BasisDebug.LogTag.Avatar
                );
            }
        }
    }

    public static void ValidateEyeToArmSizesPlayer()
    {
        ValidateEyeToArm(
            ref BasisHeightDriver.PlayerEyeHeight,
            ref BasisHeightDriver.PlayerArmSpan,
            BasisHeightDriver.FallbackHeightInMeters,
            "Player",
            BasisHeightDriver.MaxPlausibleBodyMeasure
        );
    }

    public static void ValidateEyeToArmSizesAvatar()
    {
        // Avatar spans are authored geometry — arbitrarily long arms are legitimate, so no cap.
        ValidateEyeToArm(
            ref BasisHeightDriver.AvatarEyeHeight,
            ref BasisHeightDriver.AvatarArmSpan,
            BasisHeightDriver.FallbackHeightInMeters,
            "Avatar",
            float.MaxValue
        );
    }
}
