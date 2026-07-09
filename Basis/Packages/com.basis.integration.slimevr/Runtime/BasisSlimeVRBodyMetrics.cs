using solarxr_protocol.rpc;

namespace Basis.Integration.SlimeVR
{
    /// <summary>
    /// Basis-relevant body measurements derived from a SlimeVR skeleton config.
    /// All values are metres in real (player) space.
    /// </summary>
    public readonly struct BasisSlimeVRBodyMetrics
    {
        /// <summary>
        /// SlimeVR's eyeHeightToHeightRatio: its internal user height (floor-to-eye) divided by
        /// the person's full height. Mirrors dev.slimevr.autobone.errors.BodyProportionError.
        /// </summary>
        public const float EyeHeightToFullHeightRatio = 0.936f;

        /// <summary>Standing floor-to-eye height (SlimeVR's user_height). 0 when unknown.</summary>
        public readonly float EyeHeightMeters;

        /// <summary>Estimated full head-to-floor height (EyeHeight / 0.936). 0 when unknown.</summary>
        public readonly float FullHeightMeters;

        /// <summary>
        /// Wrist-to-wrist span in T-pose: SHOULDERS_WIDTH + 2 x (UPPER_ARM + LOWER_ARM).
        /// 0 when the arm chain is missing from the config.
        /// </summary>
        public readonly float WristSpanMeters;

        /// <summary>
        /// Span between the palm points where hand trackers / controllers sit:
        /// wrist span + 2 x HAND_Y. This matches what Basis measures as the player arm span
        /// (distance between the two hand controllers), so it is the value fed to
        /// BasisHeightDriver.PlayerArmSpan. 0 when the arm chain is missing.
        /// </summary>
        public readonly float ControllerSpanMeters;

        public BasisSlimeVRBodyMetrics(float eyeHeight, float wristSpan, float controllerSpan)
        {
            EyeHeightMeters = eyeHeight;
            FullHeightMeters = eyeHeight > 0f ? eyeHeight / EyeHeightToFullHeightRatio : 0f;
            WristSpanMeters = wristSpan;
            ControllerSpanMeters = controllerSpan;
        }

        public bool HasEyeHeight => EyeHeightMeters > 0f;
        public bool HasArmSpan => ControllerSpanMeters > 0f;

        public static BasisSlimeVRBodyMetrics Derive(SlimeVRSkeletonConfig config)
        {
            float eyeHeight = config.UserHeightMeters > 0f ? config.UserHeightMeters : 0f;

            float wristSpan = 0f;
            float controllerSpan = 0f;
            if (config.TryGet(SkeletonBone.UPPER_ARM, out float upperArm)
                && config.TryGet(SkeletonBone.LOWER_ARM, out float lowerArm)
                && upperArm > 0f && lowerArm > 0f)
            {
                config.TryGet(SkeletonBone.SHOULDERS_WIDTH, out float shouldersWidth);
                config.TryGet(SkeletonBone.HAND_Y, out float handY);
                wristSpan = shouldersWidth + 2f * (upperArm + lowerArm);
                controllerSpan = wristSpan + 2f * (handY > 0f ? handY : 0f);
            }

            return new BasisSlimeVRBodyMetrics(eyeHeight, wristSpan, controllerSpan);
        }

        public override string ToString()
        {
            return $"eye {EyeHeightMeters:F3}m, full {FullHeightMeters:F3}m, span {ControllerSpanMeters:F3}m";
        }
    }
}
