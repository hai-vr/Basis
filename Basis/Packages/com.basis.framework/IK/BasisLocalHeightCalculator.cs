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
            BasisDebug.LogWarning("No hands found. Using fallback.", BasisDebug.LogTag.Avatar);
            BasisHeightDriver.PlayerArmSpan = BasisHeightDriver.FallbackHeightInMeters;
            return;
        }

        // If one hand missing, we can't do hand-to-hand; fall back to head->hand *2 as you did.
        var lockToInput = BasisLocalCameraDriver.Instance?.BasisLockToInput;
        if (!hasLeft || !hasRight)
        {
            if (lockToInput?.BasisInput == null)
            {
                BasisHeightDriver.PlayerArmSpan = BasisHeightDriver.FallbackHeightInMeters;
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

        // Pose-tolerant span: wrist-to-wrist shrinks when the elbows are bent at calibration, which then
        // mis-scales the avatar. With elbow trackers we reconstruct the STRAIGHTENED span -- (elbow-to-elbow)
        // + each rigid forearm -- which is invariant to forearm bend and needs no shoulder. The elbows are
        // not role-assigned yet at scale time, so each is found as the nearest tracker on that hand's side
        // within forearm range; ambiguous -> fall back to wrist-to-wrist. Only ever extends the span (a bend
        // cannot make the true span shorter). Gated; off by default. See BasisCalibrationLimbCompensation.
        if (Basis.BasisUI.BasisSettingsDefaults.CalibrationPoseCompensation.RawValue)
        {
            BasisInput leftElbow = FindElbowNearHand(l, left, right);
            BasisInput rightElbow = FindElbowNearHand(r, right, left);
            if (leftElbow != null && rightElbow != null)
            {
                Vector3 le = leftElbow.UnscaledDeviceCoord.position;
                Vector3 re = rightElbow.UnscaledDeviceCoord.position;
                Vector3 leFlat = new Vector3(le.x, 0f, le.z);
                Vector3 reFlat = new Vector3(re.x, 0f, re.z);
                float straightened = Vector3.Distance(leFlat, reFlat) + Vector3.Distance(l, le) + Vector3.Distance(r, re);
                if (straightened > span)
                {
                    span = straightened;
                    BasisDebug.Log($"Player arm span (elbow-reconstructed, bend-invariant): {span}", BasisDebug.LogTag.Avatar);
                }
            }
        }

        BasisHeightDriver.PlayerArmSpan = span;
        BasisDebug.Log($"Player hand-to-hand arm span: {BasisHeightDriver.PlayerArmSpan}", BasisDebug.LogTag.Avatar);
    }

    // Find the elbow tracker for a hand BEFORE role classification: the nearest device to the hand within a
    // forearm-length range that sits clearly on that hand's side (closer to this hand than the other), which
    // excludes centre-line trackers (hips/chest) and the far hand/feet. Null if none qualifies.
    static BasisInput FindElbowNearHand(Vector3 handPos, BasisInput hand, BasisInput otherHand)
    {
        const float minForearm = 0.18f;
        const float maxForearm = 0.45f;
        var devices = BasisDeviceManagement.Instance != null ? BasisDeviceManagement.Instance.AllInputDevices : null;
        if (devices == null)
        {
            return null;
        }

        Vector3 otherHandPos = otherHand != null ? otherHand.UnscaledDeviceCoord.position : Vector3.zero;
        BasisInput best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < devices.Count; i++)
        {
            BasisInput d = devices[i];
            if (d == null || d == hand || d == otherHand)
            {
                continue;
            }
            d.LatePollData();
            Vector3 p = d.UnscaledDeviceCoord.position;
            if (p == Vector3.zero)
            {
                continue; // stale / unpolled
            }
            float dist = Vector3.Distance(p, handPos);
            if (dist < minForearm || dist > maxForearm || dist >= bestDist)
            {
                continue;
            }
            if (otherHand != null && Vector3.Distance(p, otherHandPos) <= dist)
            {
                continue; // centre-line tracker (hip/chest), not this arm's elbow
            }
            best = d;
            bestDist = dist;
        }
        return best;
    }

    public static void CalculatePlayerEyeHeight()
    {
        var headInput = BasisLocalCameraDriver.Instance?.BasisLockToInput?.BasisInput;
        BasisHeightDriver.PlayerCenterEyeVerticalOffset = headInput != null ? headInput.CenterEyeVerticalOffset : 0f;

        if (SMModuleSitStand.IsSteatedMode)
        {
            BasisHeightDriver.PlayerCenterEyeVerticalOffset = 0f;
            BasisHeightDriver.PlayerEyeHeight = BasisHeightDriver.FallbackHeightInMeters;
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

                BasisDebug.LogWarning("No attached input found for BasisLockToInput. Using fallback player eye height.", BasisDebug.LogTag.Avatar);
            }
        }
        if (BasisHeightDriver.PlayerEyeHeight <= 0f)
        {
            BasisHeightDriver.PlayerEyeHeight = BasisHeightDriver.FallbackHeightInMeters;
            BasisDebug.LogWarning($"Player eye height was invalid. Set to default: {BasisHeightDriver.FallbackHeightInMeters}", BasisDebug.LogTag.Avatar);
        }
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
    private static void ValidateEyeToArm(ref float eyeHeight, ref float armSpan, float fallbackEyeHeight, string label)
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
            BasisDebug.LogWarning(
                $"{label} arm span ({armSpan}) is >{EyeArmTolerance:P0} larger than {label} eye height ({eyeHeight}). " +
                $"Clamping to max allowed: {maxAllowed}",
                BasisDebug.LogTag.Avatar
            );
            armSpan = maxAllowed;
        }
    }

    public static void ValidateEyeToArmSizesPlayer()
    {
        ValidateEyeToArm(
            ref BasisHeightDriver.PlayerEyeHeight,
            ref BasisHeightDriver.PlayerArmSpan,
            BasisHeightDriver.FallbackHeightInMeters,
            "Player"
        );
    }

    public static void ValidateEyeToArmSizesAvatar()
    {
        ValidateEyeToArm(
            ref BasisHeightDriver.AvatarEyeHeight,
            ref BasisHeightDriver.AvatarArmSpan,
            BasisHeightDriver.FallbackHeightInMeters,
            "Avatar"
        );
    }
}
