using System;
using Unity.Mathematics;
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

}
