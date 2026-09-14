using System.Globalization;
using UnityEngine;

namespace Basis.Scripts.Device_Management
{
    [System.Flags]
    public enum BasisDeviceOffsetAxes
    {
        None = 0,
        PositionX = 1,
        PositionY = 2,
        PositionZ = 4,
        RotationX = 8,
        RotationY = 16,
        RotationZ = 32,
        Position = PositionX | PositionY | PositionZ,
        Rotation = RotationX | RotationY | RotationZ,
    }

    public static class BasisDeviceOffsetMath
    {
        public const float PositionLimit = 0.5f;
        public const float PitchLimit = 90f;
        public const float MinimumHandSpan = 0.01f;
        private const float MinimumUpSquared = 0.0025f;
        private const float IdentityPositionEpsilon = 1e-5f;
        private const float IdentityRotationEpsilon = 1e-7f;
        private const float GimbalThreshold = 0.999999f;

        public static Quaternion Inverse(Quaternion rotation)
        {
            return new Quaternion(-rotation.x, -rotation.y, -rotation.z, rotation.w);
        }

        public static Quaternion Normalize(Quaternion rotation)
        {
            float magnitude = Mathf.Sqrt((rotation.x * rotation.x) + (rotation.y * rotation.y) + (rotation.z * rotation.z) + (rotation.w * rotation.w));
            if (magnitude < 1e-6f || float.IsNaN(magnitude) || float.IsInfinity(magnitude))
            {
                return Quaternion.identity;
            }
            float inverse = 1f / magnitude;
            return new Quaternion(rotation.x * inverse, rotation.y * inverse, rotation.z * inverse, rotation.w * inverse);
        }

        public static bool IsIdentity(Vector3 position, Quaternion rotation)
        {
            return position.sqrMagnitude <= IdentityPositionEpsilon * IdentityPositionEpsilon && Mathf.Abs(rotation.w) >= 1f - IdentityRotationEpsilon;
        }

        public static Vector3 ClampPosition(Vector3 position)
        {
            return new Vector3(Mathf.Clamp(position.x, -PositionLimit, PositionLimit), Mathf.Clamp(position.y, -PositionLimit, PositionLimit), Mathf.Clamp(position.z, -PositionLimit, PositionLimit));
        }

        public static float WrapDegrees(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f)
            {
                degrees -= 360f;
            }
            else if (degrees < -180f)
            {
                degrees += 360f;
            }
            return degrees;
        }

        public static Quaternion FromEuler(Vector3 degrees)
        {
            Vector3 half = degrees * (Mathf.Deg2Rad * 0.5f);
            Quaternion yaw = new Quaternion(0f, Mathf.Sin(half.y), 0f, Mathf.Cos(half.y));
            Quaternion pitch = new Quaternion(Mathf.Sin(half.x), 0f, 0f, Mathf.Cos(half.x));
            Quaternion roll = new Quaternion(0f, 0f, Mathf.Sin(half.z), Mathf.Cos(half.z));
            return yaw * pitch * roll;
        }

        public static Vector3 ToEuler(Quaternion rotation)
        {
            Quaternion q = Normalize(rotation);
            float sinPitch = 2f * ((q.w * q.x) - (q.y * q.z));
            if (sinPitch >= GimbalThreshold || sinPitch <= -GimbalThreshold)
            {
                float m00 = 1f - (2f * ((q.y * q.y) + (q.z * q.z)));
                float m01 = 2f * ((q.x * q.y) - (q.w * q.z));
                float yaw = sinPitch > 0f ? Mathf.Atan2(m01, m00) : Mathf.Atan2(-m01, m00);
                return new Vector3(sinPitch > 0f ? PitchLimit : -PitchLimit, yaw * Mathf.Rad2Deg, 0f);
            }
            float x = Mathf.Asin(sinPitch) * Mathf.Rad2Deg;
            float y = Mathf.Atan2(2f * ((q.x * q.z) + (q.w * q.y)), 1f - (2f * ((q.x * q.x) + (q.y * q.y)))) * Mathf.Rad2Deg;
            float z = Mathf.Atan2(2f * ((q.x * q.y) + (q.w * q.z)), 1f - (2f * ((q.x * q.x) + (q.z * q.z)))) * Mathf.Rad2Deg;
            return new Vector3(x, y, z);
        }

        public static Vector3 NearestEuler(Quaternion rotation, Vector3 reference)
        {
            Vector3 primary = ToEuler(rotation);
            Vector3 first = Unwrap(primary, reference);
            Vector3 second = Unwrap(new Vector3(180f - primary.x, primary.y + 180f, primary.z + 180f), reference);
            return (first - reference).sqrMagnitude <= (second - reference).sqrMagnitude ? first : second;
        }

        public static void Compose(Vector3 parentPosition, Quaternion parentRotation, Vector3 localPosition, Quaternion localRotation, out Vector3 position, out Quaternion rotation)
        {
            position = parentPosition + (parentRotation * localPosition);
            rotation = parentRotation * localRotation;
        }

        public static void Relative(Vector3 parentPosition, Quaternion parentRotation, Vector3 position, Quaternion rotation, out Vector3 localPosition, out Quaternion localRotation)
        {
            Quaternion inverse = Inverse(parentRotation);
            localPosition = inverse * (position - parentPosition);
            localRotation = inverse * rotation;
        }

        public static void ApplyScaled(ref Vector3 position, ref Quaternion rotation, Vector3 offsetPosition, Quaternion offsetRotation, float deviceScale)
        {
            position += rotation * (offsetPosition * deviceScale);
            rotation *= offsetRotation;
        }

        public static void Retarget(Vector3 devicePosition, Quaternion deviceRotation, Vector3 offsetPosition, Quaternion offsetRotation, ref Vector3 position, ref Quaternion rotation)
        {
            Relative(devicePosition, deviceRotation, position, rotation, out Vector3 localPosition, out Quaternion localRotation);
            Compose(devicePosition, deviceRotation, offsetPosition, offsetRotation, out Vector3 virtualPosition, out Quaternion virtualRotation);
            Compose(virtualPosition, virtualRotation, localPosition, localRotation, out position, out rotation);
        }

        public static void FollowFrame(Vector3 anchorFramePosition, Quaternion anchorFrameRotation, Vector3 anchorPosition, Quaternion anchorRotation, Vector3 framePosition, Quaternion frameRotation, out Vector3 position, out Quaternion rotation)
        {
            position = anchorPosition + (framePosition - anchorFramePosition);
            rotation = Normalize(frameRotation * Inverse(anchorFrameRotation) * anchorRotation);
        }

        public static void SolveOffset(Vector3 physicalPosition, Quaternion physicalRotation, Vector3 heldPosition, Quaternion heldRotation, out Vector3 offsetPosition, out Quaternion offsetRotation)
        {
            Relative(physicalPosition, physicalRotation, heldPosition, heldRotation, out offsetPosition, out offsetRotation);
            offsetPosition = ClampPosition(offsetPosition);
            offsetRotation = Normalize(offsetRotation);
        }

        public static void ConstrainOffset(BasisDeviceOffsetAxes axes, Vector3 basePosition, Vector3 baseEuler, Vector3 targetPosition, Quaternion targetRotation, ref Vector3 eulerReference, out Vector3 position, out Quaternion rotation)
        {
            position = ClampPosition(new Vector3((axes & BasisDeviceOffsetAxes.PositionX) != 0 ? targetPosition.x : basePosition.x, (axes & BasisDeviceOffsetAxes.PositionY) != 0 ? targetPosition.y : basePosition.y, (axes & BasisDeviceOffsetAxes.PositionZ) != 0 ? targetPosition.z : basePosition.z));
            Vector3 euler = NearestEuler(targetRotation, eulerReference);
            eulerReference = euler;
            if ((axes & BasisDeviceOffsetAxes.Rotation) == BasisDeviceOffsetAxes.Rotation)
            {
                rotation = Normalize(targetRotation);
                return;
            }
            rotation = FromEuler(new Vector3((axes & BasisDeviceOffsetAxes.RotationX) != 0 ? Mathf.Clamp(euler.x, -PitchLimit, PitchLimit) : baseEuler.x, (axes & BasisDeviceOffsetAxes.RotationY) != 0 ? euler.y : baseEuler.y, (axes & BasisDeviceOffsetAxes.RotationZ) != 0 ? euler.z : baseEuler.z));
        }

        public static bool TryBuildTwoHandFrame(Vector3 firstPosition, Quaternion firstRotation, Vector3 secondPosition, Quaternion secondRotation, Vector3 upFallback, out Vector3 position, out Quaternion rotation)
        {
            position = (firstPosition + secondPosition) * 0.5f;
            rotation = firstRotation;
            Vector3 across = secondPosition - firstPosition;
            float span = across.magnitude;
            if (span < MinimumHandSpan)
            {
                return false;
            }
            Vector3 right = across / span;
            Vector3 up = Perpendicular(right, (firstRotation * Vector3.up) + (secondRotation * Vector3.up));
            if (up.sqrMagnitude < MinimumUpSquared)
            {
                up = Perpendicular(right, upFallback);
                if (up.sqrMagnitude < MinimumUpSquared)
                {
                    return false;
                }
            }
            up.Normalize();
            rotation = FromBasis(right, up, Vector3.Cross(right, up));
            return true;
        }

        public static Quaternion FromBasis(Vector3 right, Vector3 up, Vector3 forward)
        {
            float trace = right.x + up.y + forward.z;
            Quaternion rotation;
            if (trace > 0f)
            {
                float scale = Mathf.Sqrt(trace + 1f) * 2f;
                rotation = new Quaternion((up.z - forward.y) / scale, (forward.x - right.z) / scale, (right.y - up.x) / scale, 0.25f * scale);
            }
            else if (right.x > up.y && right.x > forward.z)
            {
                float scale = Mathf.Sqrt(1f + right.x - up.y - forward.z) * 2f;
                rotation = new Quaternion(0.25f * scale, (right.y + up.x) / scale, (forward.x + right.z) / scale, (up.z - forward.y) / scale);
            }
            else if (up.y > forward.z)
            {
                float scale = Mathf.Sqrt(1f + up.y - right.x - forward.z) * 2f;
                rotation = new Quaternion((right.y + up.x) / scale, 0.25f * scale, (up.z + forward.y) / scale, (forward.x - right.z) / scale);
            }
            else
            {
                float scale = Mathf.Sqrt(1f + forward.z - right.x - up.y) * 2f;
                rotation = new Quaternion((forward.x + right.z) / scale, (up.z + forward.y) / scale, 0.25f * scale, (right.y - up.x) / scale);
            }
            return Normalize(rotation);
        }

        public static string Format(Vector3 position, Quaternion rotation)
        {
            CultureInfo culture = CultureInfo.InvariantCulture;
            return position.x.ToString("G9", culture) + "," + position.y.ToString("G9", culture) + "," + position.z.ToString("G9", culture) + "," + rotation.x.ToString("G9", culture) + "," + rotation.y.ToString("G9", culture) + "," + rotation.z.ToString("G9", culture) + "," + rotation.w.ToString("G9", culture);
        }

        public static bool TryParse(string text, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            string[] parts = text.Split(',');
            if (parts.Length != 7)
            {
                return false;
            }
            float[] values = new float[7];
            for (int index = 0; index < values.Length; index++)
            {
                if (!float.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out values[index]) || float.IsNaN(values[index]) || float.IsInfinity(values[index]))
                {
                    return false;
                }
            }
            position = ClampPosition(new Vector3(values[0], values[1], values[2]));
            rotation = Normalize(new Quaternion(values[3], values[4], values[5], values[6]));
            return true;
        }

        private static Vector3 Unwrap(Vector3 degrees, Vector3 reference)
        {
            return new Vector3(reference.x + WrapDegrees(degrees.x - reference.x), reference.y + WrapDegrees(degrees.y - reference.y), reference.z + WrapDegrees(degrees.z - reference.z));
        }

        private static Vector3 Perpendicular(Vector3 axis, Vector3 direction)
        {
            return direction - (axis * Vector3.Dot(direction, axis));
        }
    }
}
