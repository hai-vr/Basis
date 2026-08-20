namespace Basis.Scripts.Drivers
{
    public enum BasisLocoCondition : byte
    {
        IsJumpingTrue = 0,
        CrouchedTrue = 1,
        CrouchedFalse = 2,
        IsFallingTrue = 3,
        IsFallingFalse = 4,
        LandingTrigger = 5,
        ProneTrue = 6,
        ProneFalse = 7,
    }
}
