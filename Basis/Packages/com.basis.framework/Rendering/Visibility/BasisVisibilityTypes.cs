using System;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

namespace Basis.Scripts.Rendering
{
    public struct BasisVisibilityCamera
    {
        public float4 Plane0;
        public float4 Plane1;
        public float4 Plane2;
        public float4 Plane3;
        public float4 Plane4;
        public float4 Plane5;
        public float3 Position;
    }

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

    public static class BasisVisibilityMath
    {
        /// <summary>
        /// Largest axis scale of a local-to-world matrix. Bounds are stored unscaled and multiplied
        /// by this each frame, so an avatar rescaled after registration — network scale, height
        /// calibration — keeps bounds that match the mesh instead of a stale load-time size.
        /// </summary>
        public static float MaxAxisScale(Matrix4x4 localToWorld)
        {
            float3 c0 = new float3(localToWorld.m00, localToWorld.m10, localToWorld.m20);
            float3 c1 = new float3(localToWorld.m01, localToWorld.m11, localToWorld.m21);
            float3 c2 = new float3(localToWorld.m02, localToWorld.m12, localToWorld.m22);
            return math.max(math.length(c0), math.max(math.length(c1), math.length(c2)));
        }
    }

    internal static class BasisVisibilityMarkers
    {
        public static readonly ProfilerMarker CollectCameras = new ProfilerMarker("BasisVisibility.CollectCameras");
        public static readonly ProfilerMarker Dispatch = new ProfilerMarker("BasisVisibility.Dispatch");
        public static readonly ProfilerMarker Join = new ProfilerMarker("BasisVisibility.Join");
        public static readonly ProfilerMarker Apply = new ProfilerMarker("BasisVisibility.Apply");
    }
}
