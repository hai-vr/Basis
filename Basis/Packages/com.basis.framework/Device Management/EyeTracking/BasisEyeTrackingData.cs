using Unity.Mathematics;

namespace Basis.Scripts.Device_Management.EyeTracking
{
    public enum BasisEyeSource
    {
        Hmd,
        Osc,
    }

    public struct BasisEyeTrackingData
    {
        public float3 GazeOrigin;
        public float3 GazeDirection;
        public bool HasWorldRay;

        public float2 LeftAngles;
        public float2 RightAngles;
        public bool HasPerEyeAngles;

        public float LeftOpenness;
        public float RightOpenness;
        public bool HasOpenness;

        public float3 LeftEyePosition;
        public float3 RightEyePosition;
        public bool HasEyePositions;
    }
}
