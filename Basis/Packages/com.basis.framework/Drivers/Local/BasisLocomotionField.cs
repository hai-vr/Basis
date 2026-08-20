using System;
namespace Basis.Scripts.Drivers
{
    [Flags]
    public enum BasisLocomotionField : byte
    {
        None = 0,
        JumpHeight = 1 << 0,
        WalkSpeed = 1 << 1,
        RunSpeed = 1 << 2,
        Gravity = 1 << 3,
        Mode = 1 << 4,
        All = JumpHeight | WalkSpeed | RunSpeed | Gravity | Mode,
    }
}
