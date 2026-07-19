namespace Basis.Scripts.Drivers
{
    public enum BasisSmoothingGroup : byte
    {
        Head = 0,
        Hands = 1,
        Elbows = 2,
        Chest = 3,
        Hips = 4,
        Knees = 5,
        Feet = 6,
    }

    public struct BasisSmoothingProfile
    {
        public float MinCutoff;
        public float Beta;
        public float DerivativeCutoff;
        public float PositionHz;
        public float RotationHz;

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

        public const string PresetOff = "Off";
        public const string PresetLight = "Light";
        public const string PresetStandard = "Standard";
        public const string PresetHeavy = "Heavy";
        public const string PresetOptical = "Optical";

        public static readonly string[] PresetOrder =
        {
            PresetOff,
            PresetLight,
            PresetStandard,
            PresetHeavy,
            PresetOptical,
        };

        public static readonly string[] PresetLocalizationKeys =
        {
            "settings.bodyTracking.smoothing.preset.off",
            "settings.bodyTracking.smoothing.preset.light",
            "settings.bodyTracking.smoothing.preset.standard",
            "settings.bodyTracking.smoothing.preset.heavy",
            "settings.bodyTracking.smoothing.preset.optical",
        };

        private static readonly BasisSmoothingProfile Light = new(8f, 2f, 3f, 30f, 35f);
        private static readonly BasisSmoothingProfile Heavy = new(2f, 5f, 2f, 10f, 12f);
        private static readonly BasisSmoothingProfile Optical = new(1f, 9f, 1.5f, 8f, 10f);

        public static bool IsOff(string preset) => preset == PresetOff;

        public static bool TryGetPreset(string preset, out BasisSmoothingProfile profile)
        {
            switch (preset)
            {
                case PresetLight:
                    profile = Light;
                    return true;
                case PresetHeavy:
                    profile = Heavy;
                    return true;
                case PresetOptical:
                    profile = Optical;
                    return true;
                default:
                    profile = default;
                    return false;
            }
        }

        public static readonly byte[] SlotGroup = BuildSlotGroups();

        private static byte[] BuildSlotGroups()
        {
            byte[] map = new byte[BasisLocalRigDriver.SlotCount];
            map[BasisLocalRigDriver.S_Head] = (byte)BasisSmoothingGroup.Head;
            map[BasisLocalRigDriver.S_LeftHand] = (byte)BasisSmoothingGroup.Hands;
            map[BasisLocalRigDriver.S_RightHand] = (byte)BasisSmoothingGroup.Hands;
            map[BasisLocalRigDriver.S_LeftShoulder] = (byte)BasisSmoothingGroup.Hands;
            map[BasisLocalRigDriver.S_RightShoulder] = (byte)BasisSmoothingGroup.Hands;
            map[BasisLocalRigDriver.S_LeftLowerArm] = (byte)BasisSmoothingGroup.Elbows;
            map[BasisLocalRigDriver.S_RightLowerArm] = (byte)BasisSmoothingGroup.Elbows;
            map[BasisLocalRigDriver.S_Chest] = (byte)BasisSmoothingGroup.Chest;
            map[BasisLocalRigDriver.S_Hips] = (byte)BasisSmoothingGroup.Hips;
            map[BasisLocalRigDriver.S_LeftLowerLeg] = (byte)BasisSmoothingGroup.Knees;
            map[BasisLocalRigDriver.S_RightLowerLeg] = (byte)BasisSmoothingGroup.Knees;
            map[BasisLocalRigDriver.S_LeftFoot] = (byte)BasisSmoothingGroup.Feet;
            map[BasisLocalRigDriver.S_RightFoot] = (byte)BasisSmoothingGroup.Feet;
            map[BasisLocalRigDriver.S_LeftToe] = (byte)BasisSmoothingGroup.Feet;
            map[BasisLocalRigDriver.S_RightToe] = (byte)BasisSmoothingGroup.Feet;
            return map;
        }
    }
}
