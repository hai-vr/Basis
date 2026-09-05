using UnityEngine;

namespace Basis.Scripts.Device_Management.Devices.OpenVR
{
    public struct BasisOpenVRWristLatch
    {
        public const float SettledSqrDistance = 1e-8f;
        public const float SettledAngle = 0.05f;

        public Vector3 Position;
        public Quaternion Rotation;
        public bool Latched;

        public static bool Settled(Vector3 position, Quaternion rotation, Vector3 lastPosition, Quaternion lastRotation)
        {
            return (position - lastPosition).sqrMagnitude < SettledSqrDistance && Quaternion.Angle(rotation, lastRotation) < SettledAngle;
        }

        public void Update(bool hold, Vector3 position, Quaternion rotation)
        {
            if (Latched && hold)
            {
                return;
            }
            Position = position;
            Rotation = rotation;
            Latched = true;
        }

        public void Reset()
        {
            Latched = false;
        }
    }
}
