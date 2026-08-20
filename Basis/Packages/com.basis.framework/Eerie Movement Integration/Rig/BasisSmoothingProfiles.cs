using Basis.Scripts.Device_Management;
using Basis.Scripts.TransformBinders.BoneControl;
using Unity.Mathematics;
namespace Basis.Scripts.Drivers
{
    public struct BasisSmoothingProfile
    {
        public float MinCutoff, Beta, DerivativeCutoff, PositionHz, RotationHz;
        public BasisSmoothingProfile(float minCutoff, float beta, float derivativeCutoff, float positionHz, float rotationHz)
        {
            MinCutoff = minCutoff;
            Beta = beta;
            DerivativeCutoff = derivativeCutoff;
            PositionHz = positionHz;
            RotationHz = rotationHz;
        }
    }
    public static class BasisSmoothingProfiles
    {
        public const int GroupCount = 7;
        private const float SmoothingReferenceFps = 90f;
        public static float FramerateIndependentAlpha(float speed, float dt)
        {
            float alphaAtRef = math.saturate(math.max(0f, speed) / SmoothingReferenceFps);

            if (alphaAtRef >= 0.999f) return 1f;
            if (alphaAtRef <= 0f) return 0f;
            float rate = -SmoothingReferenceFps * math.log(1f - alphaAtRef);
            return 1f - math.exp(-rate * math.max(0f, dt));
        }
        public const string PresetOff = "Off";
        public const string PresetLight = "Light";
        public const string PresetStandard = "Standard";
        public const string PresetHeavy = "Heavy";
        public const string PresetOptical = "Optical";
        public const string PresetCustom = "Custom";
        public const string PresetAuto = "Auto";
        public static readonly string[] PresetOrder =
        {
            PresetAuto,
            PresetOff,
            PresetLight,
            PresetStandard,
            PresetHeavy,
            PresetOptical,
            PresetCustom,
        };
        public static readonly string[] PresetLocalizationKeys =
        {
            "settings.bodyTracking.smoothing.preset.auto",
            "settings.bodyTracking.smoothing.preset.off",
            "settings.bodyTracking.smoothing.preset.light",
            "settings.bodyTracking.smoothing.preset.standard",
            "settings.bodyTracking.smoothing.preset.heavy",
            "settings.bodyTracking.smoothing.preset.optical",
            "settings.bodyTracking.smoothing.preset.custom",
        };
        private static readonly BasisSmoothingProfile Light = new(8f, 2f, 3f, 30f, 35f);
        private static readonly BasisSmoothingProfile Heavy = new(2f, 5f, 2f, 10f, 12f);
        private static readonly BasisSmoothingProfile Optical = new(1f, 9f, 1.5f, 8f, 10f);
        public static bool IsOff(string preset) => preset == PresetOff;
        public static bool IsCustom(string preset) => preset == PresetCustom;
        public static bool IsAuto(string preset) => preset == PresetAuto;
        public static string PresetForHardware(BasisTrackingHardware hardware)
        {
            switch (hardware)
            {
                case BasisTrackingHardware.Simulated: return PresetOff;
                case BasisTrackingHardware.Lighthouse: case BasisTrackingHardware.InsideOut: return PresetLight;
                case BasisTrackingHardware.Optical: return PresetOptical;
                case BasisTrackingHardware.Estimated: case BasisTrackingHardware.Inertial: return PresetHeavy;
                default: return PresetStandard;
            }
        }
        public static bool TryGetGroupForRole(BasisBoneTrackedRole role, out int group)
        {
            switch (role)
            {
                case BasisBoneTrackedRole.CenterEye: case BasisBoneTrackedRole.Head: group = (int)BasisSmoothingGroup.Head;
                    return true;
                case BasisBoneTrackedRole.LeftHand: case BasisBoneTrackedRole.RightHand: case BasisBoneTrackedRole.LeftShoulder: case BasisBoneTrackedRole.RightShoulder: group = (int)BasisSmoothingGroup.Hands;
                    return true;
                case BasisBoneTrackedRole.LeftLowerArm: case BasisBoneTrackedRole.RightLowerArm: group = (int)BasisSmoothingGroup.Elbows;
                    return true;
                case BasisBoneTrackedRole.Chest: group = (int)BasisSmoothingGroup.Chest;
                    return true;
                case BasisBoneTrackedRole.Hips: group = (int)BasisSmoothingGroup.Hips;
                    return true;
                case BasisBoneTrackedRole.LeftLowerLeg: case BasisBoneTrackedRole.RightLowerLeg: group = (int)BasisSmoothingGroup.Knees;
                    return true;
                case BasisBoneTrackedRole.LeftFoot: case BasisBoneTrackedRole.RightFoot: case BasisBoneTrackedRole.LeftToes: case BasisBoneTrackedRole.RightToes: group = (int)BasisSmoothingGroup.Feet;
                    return true;
                default: group = -1;
                    return false;
            }
        }
        public static bool TryGetPreset(string preset, out BasisSmoothingProfile profile)
        {
            switch (preset)
            {
                case PresetLight: profile = Light;
                    return true;
                case PresetHeavy: profile = Heavy;
                    return true;
                case PresetOptical: profile = Optical;
                    return true;
                default: profile = default;
                    return false;
            }
        }
        public static readonly byte[] SlotGroup = BuildSlotGroups();
        private static byte[] BuildSlotGroups()
        {
            byte[] map = new byte[BasisLocalRigDriver.SlotCount];
            map[BasisLocalRigDriver.sHead] = (byte)BasisSmoothingGroup.Head;
            map[BasisLocalRigDriver.sLeftHand] = (byte)BasisSmoothingGroup.Hands;
            map[BasisLocalRigDriver.sRightHand] = (byte)BasisSmoothingGroup.Hands;
            map[BasisLocalRigDriver.sLeftShoulder] = (byte)BasisSmoothingGroup.Hands;
            map[BasisLocalRigDriver.sRightShoulder] = (byte)BasisSmoothingGroup.Hands;
            map[BasisLocalRigDriver.sLeftLowerArm] = (byte)BasisSmoothingGroup.Elbows;
            map[BasisLocalRigDriver.sRightLowerArm] = (byte)BasisSmoothingGroup.Elbows;
            map[BasisLocalRigDriver.sChest] = (byte)BasisSmoothingGroup.Chest;
            map[BasisLocalRigDriver.sHips] = (byte)BasisSmoothingGroup.Hips;
            map[BasisLocalRigDriver.sLeftLowerLeg] = (byte)BasisSmoothingGroup.Knees;
            map[BasisLocalRigDriver.sRightLowerLeg] = (byte)BasisSmoothingGroup.Knees;
            map[BasisLocalRigDriver.sLeftFoot] = (byte)BasisSmoothingGroup.Feet;
            map[BasisLocalRigDriver.sRightFoot] = (byte)BasisSmoothingGroup.Feet;
            map[BasisLocalRigDriver.sLeftToe] = (byte)BasisSmoothingGroup.Feet;
            map[BasisLocalRigDriver.sRightToe] = (byte)BasisSmoothingGroup.Feet;
            return map;
        }
    }
}
