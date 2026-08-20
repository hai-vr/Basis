using System;

namespace Basis.BasisUI
{
    [Flags]
    public enum BasisFrameCostSide
    {
        None = 0,
        Cpu = 1,
        Gpu = 2,
        Both = Cpu | Gpu
    }
}
