namespace Basis.Scripts.Networking.NetworkedAvatar
{
    [System.Serializable]
    public class BasisAvatarBuffer
    {
        public Unity.Mathematics.quaternion rotation;
        public Unity.Mathematics.float3 Scale;
        public Unity.Mathematics.float3 Position;
        public float[] Muscles = new float[95];
        public double SecondsInterval;
    }
}
