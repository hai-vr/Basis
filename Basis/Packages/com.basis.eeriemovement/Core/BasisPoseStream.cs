using Unity.Collections;
using Unity.Mathematics;

using UnityEngine;
namespace Basis.IK
{
    public struct BasisBoneHandle
    {
        public int IndexPlusOne;

        public int Index => IndexPlusOne - 1;

        public static BasisBoneHandle Unbound => default;

        public static BasisBoneHandle FromIndex(int index) => new BasisBoneHandle { IndexPlusOne = index + 1 };

        public bool IsValid(BasisPoseStream stream) => IndexPlusOne > 0 && IndexPlusOne <= stream.Count;

        public Vector3 GetPosition(BasisPoseStream stream) =>
            IsValid(stream) ? stream.GetWorldPosition(Index) : Vector3.zero;

        public Quaternion GetRotation(BasisPoseStream stream) =>
            IsValid(stream) ? stream.GetWorldRotation(Index) : Quaternion.identity;

        public void GetPositionAndRotation(BasisPoseStream stream, out Vector3 position, out Quaternion rotation)
        {
            if (IsValid(stream))
            {
                stream.GetWorldPositionAndRotation(Index, out position, out rotation);
                return;
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
        }

        public void SetPosition(BasisPoseStream stream, Vector3 position)
        {
            if (IsValid(stream))
            {
                stream.SetWorldPosition(Index, position);
            }
        }

        public void SetRotation(BasisPoseStream stream, Quaternion rotation)
        {
            if (IsValid(stream))
            {
                stream.SetWorldRotation(Index, rotation);
            }
        }
    }

    public static class BasisQuaternionExt
    {
        const float k_FloatMin = 1e-10f;

        public static Quaternion FromToRotation(Vector3 from, Vector3 to)
        {
            Vector3 fromN = from.normalized;
            Vector3 toN = to.normalized;
            if (fromN.sqrMagnitude < 0.5f || toN.sqrMagnitude < 0.5f)
            {
                return Quaternion.identity;
            }

            float theta = Vector3.Dot(fromN, toN);
            if (theta >= 1f)
            {
                return Quaternion.identity;
            }

            Vector3 rotAxis = Vector3.Cross(fromN, toN);
            if (theta > -1f && rotAxis.sqrMagnitude >= k_FloatMin)
            {
                return Quaternion.AngleAxis(Mathf.Acos(theta) * Mathf.Rad2Deg, rotAxis.normalized);
            }

            if (theta > 0f)
            {
                return Quaternion.identity;
            }

            Vector3 axis = Vector3.Cross(fromN, Vector3.right);
            if (axis.sqrMagnitude < k_FloatMin)
            {
                axis = Vector3.Cross(fromN, Vector3.up);
            }
            if (axis.sqrMagnitude < k_FloatMin)
            {
                return Quaternion.identity;
            }

            return Quaternion.AngleAxis(180f, axis.normalized);
        }

        public static Quaternion NormalizeSafe(Quaternion q)
        {
            float dot = Quaternion.Dot(q, q);
            if (dot > k_FloatMin)
            {
                float rsqrt = 1.0f / Mathf.Sqrt(dot);
                return new Quaternion(q.x * rsqrt, q.y * rsqrt, q.z * rsqrt, q.w * rsqrt);
            }

            return Quaternion.identity;
        }
    }

    public struct BasisAffineTransform
    {
        public Vector3 translation;
        public Quaternion rotation;

        public BasisAffineTransform(Vector3 translation, Quaternion rotation)
        {
            this.translation = translation;
            this.rotation = rotation;
        }
    }

    public struct BasisPoseStream
    {
        public const int MaxDepth = 64;

        public NativeArray<float3> LocalPosition;
        public NativeArray<quaternion> LocalRotation;
        public NativeArray<float3> LocalScale;
        [ReadOnly] public NativeArray<int> Parent;
        [ReadOnly] public NativeArray<float> BindLength;
        [ReadOnly] public NativeArray<byte> TranslationFree;

        public NativeArray<float3> WorldPositionCache;
        public NativeArray<quaternion> WorldRotationCache;
        public NativeArray<float3> WorldScaleCache;

        public NativeArray<int> WorldCacheStamp;

        public float3 AnchorPosition;
        public quaternion AnchorRotation;
        public float3 AnchorScale;

        public float deltaTime;
        public int Count;

        public void InvalidateWorldCache()
        {
            if (!WorldCacheStamp.IsCreated)
            {
                return;
            }
            int next = WorldCacheStamp[Count] + 1;
            WorldCacheStamp[Count] = next != 0 ? next : 1;
        }

        public unsafe void GetWorld(int index, out float3 position, out quaternion rotation, out float3 scale)
        {
            bool cached = WorldCacheStamp.IsCreated;
            int generation = cached ? WorldCacheStamp[Count] : 0;

            int* chain = stackalloc int[MaxDepth];
            int depth = 0;
            int walk = index;
            while (walk >= 0 && depth < MaxDepth)
            {
                if (cached && WorldCacheStamp[walk] == generation)
                {
                    break;
                }
                chain[depth++] = walk;
                walk = Parent[walk];
            }

            if (cached && walk >= 0 && depth < MaxDepth)
            {
                position = WorldPositionCache[walk];
                rotation = WorldRotationCache[walk];
                scale = WorldScaleCache[walk];
            }
            else
            {
                position = AnchorPosition;
                rotation = AnchorRotation;
                scale = AnchorScale;
            }

            bool store = cached && depth < MaxDepth;
            for (int i = depth - 1; i >= 0; i--)
            {
                int bone = chain[i];
                position += math.mul(rotation, LocalPosition[bone] * scale);
                rotation = math.mul(rotation, LocalRotation[bone]);
                scale *= LocalScale[bone];
                if (store)
                {
                    WorldPositionCache[bone] = position;
                    WorldRotationCache[bone] = rotation;
                    WorldScaleCache[bone] = scale;
                    WorldCacheStamp[bone] = generation;
                }
            }
        }

        public void GetParentWorld(int index, out float3 position, out quaternion rotation, out float3 scale)
        {
            int parent = Parent[index];
            if (parent < 0)
            {
                position = AnchorPosition;
                rotation = AnchorRotation;
                scale = AnchorScale;
                return;
            }
            GetWorld(parent, out position, out rotation, out scale);
        }

        public Vector3 GetWorldPosition(int index)
        {
            GetWorld(index, out float3 position, out _, out _);
            return position;
        }

        public Quaternion GetWorldRotation(int index)
        {
            GetWorld(index, out _, out quaternion rotation, out _);
            return rotation;
        }

        public void GetWorldPositionAndRotation(int index, out Vector3 position, out Quaternion rotation)
        {
            GetWorld(index, out float3 p, out quaternion r, out _);
            position = p;
            rotation = r;
        }

        public static quaternion SafeNormalize(quaternion q)
        {
            float4 x = q.value;
            float lengthSq = math.lengthsq(x);
            if (math.isfinite(lengthSq))
            {
                return lengthSq > math.FLT_MIN_NORMAL
                    ? new quaternion(x * math.rsqrt(lengthSq))
                    : quaternion.identity;
            }

            float scale = math.cmax(math.abs(x));
            if (!math.isfinite(scale) || scale <= 0f)
            {
                return quaternion.identity;
            }
            x /= scale;
            float rescaledLengthSq = math.lengthsq(x);
            return math.isfinite(rescaledLengthSq) && rescaledLengthSq > math.FLT_MIN_NORMAL
                ? new quaternion(x * math.rsqrt(rescaledLengthSq))
                : quaternion.identity;
        }

        public void SetWorldRotation(int index, Quaternion rotation)
        {
            GetParentWorld(index, out _, out quaternion parentRotation, out _);
            LocalRotation[index] = SafeNormalize(math.mul(math.inverse(parentRotation), (quaternion)rotation));
            InvalidateWorldCache();
        }

        public void SetWorldPosition(int index, Vector3 position)
        {
            GetParentWorld(index, out float3 parentPosition, out quaternion parentRotation, out float3 parentScale);
            float3 local = math.mul(math.inverse(parentRotation), (float3)position - parentPosition);
            local = new float3(
                parentScale.x != 0f ? local.x / parentScale.x : 0f,
                parentScale.y != 0f ? local.y / parentScale.y : 0f,
                parentScale.z != 0f ? local.z / parentScale.z : 0f);

            if (TranslationFree.IsCreated && index < TranslationFree.Length && TranslationFree[index] == 0)
            {
                float bind = BindLength[index];
                float length = math.length(local);
                if (bind > 0f && length > bind && length > 1e-6f)
                {
                    local *= bind / length;
                }
            }

            LocalPosition[index] = local;
            InvalidateWorldCache();
        }
    }
}
