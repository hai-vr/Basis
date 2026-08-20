using System;
namespace Basis.Scripts.Drivers
{
    [Flags]
    public enum BasisPlayspaceGizmoLayers
    {
        None = 0,
        Boundary = 1 << 0,
        Origin = 1 << 1,
        Offset = 1 << 2,
        Hands = 1 << 3,
        Readouts = 1 << 4,
    }
}
