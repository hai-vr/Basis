using System;
namespace Basis.Scripts.Rendering
{
    [Flags]
    public enum BasisVisibilityFlags : uint
    {
        None = 0u,
        Active = 1u << 0,
        Dynamic = 1u << 1,
        AlwaysVisible = 1u << 2,
        CullEligible = 1u << 3,

        /// <summary>
        /// Bounds are authored once and never move, so the entry needs no root transform. Without
        /// this a root-less entry is forced <see cref="AlwaysVisible"/>, because nothing would ever
        /// refresh its centre and it would be culled against a stale position.
        /// </summary>
        Static = 1u << 4,
    }
}
