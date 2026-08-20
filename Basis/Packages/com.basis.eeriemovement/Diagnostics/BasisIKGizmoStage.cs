using System;
namespace Basis.IK
{
    [Flags]
    public enum BasisIKGizmoStage
    {
        None = 0,
        Targets = 1 << 0,
        Spine = 1 << 1,
        Shoulders = 1 << 2,
        Legs = 1 << 3,
        Arms = 1 << 4,
        Toes = 1 << 5,
        Overrides = 1 << 6,
        Skeleton = 1 << 7,
        Scratch = 1 << 8,
        Frames = 1 << 9,
        Limits = 1 << 10,
        Reach = 1 << 11,
        Numbers = 1 << 12,
    }
}
