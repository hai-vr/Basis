using System;
using UnityEngine;

/// <summary>
/// Builds structurally valid synthetic far avatar payloads that survive the defensive
/// TryParse (sane positions/rotations/scales, parents-first skeleton, in-range indices).
/// </summary>
internal static class BasisFarLodTestPayloads
{
    public static BasisFarLodPayload Create(int vertexCount = 24, int boneCount = 4, int seed = 1234)
    {
        System.Random random = new System.Random(seed);
        BasisFarLodPayload payload = new BasisFarLodPayload
        {
            BoneHumanBodyBone = new byte[boneCount],
            BoneParentIndex = new byte[boneCount],
            BoneRestLocalPosition = new Vector3[boneCount],
            BoneRestLocalRotation = new Quaternion[boneCount],
            MinBrightness = 0.1f,
            MaxBrightness = 2f,
            AvatarEyePosition = new Vector2(1.6f, 0.05f),
            AvatarMouthPosition = new Vector2(1.5f, 0.08f),
            AuthoredRootScale = Vector3.one,
            TposeHeadFromRootPosition = new Vector3(0f, 1.6f, 0f),
            TposeHeadFromRootRotation = Quaternion.identity,
            TposeHipsFromRootPosition = new Vector3(0f, 0.9f, 0f),
            TposeHipsFromRootRotation = Quaternion.identity,
            PositionBoundsMin = new Vector3(-1f, 0f, -1f),
            PositionBoundsMax = new Vector3(1f, 2f, 1f),
            LocalBoundsCenter = new Vector3(0f, 0.2f, 0f),
            LocalBoundsExtents = new Vector3(0.6f, 1.1f, 0.4f),
            VertexCount = vertexCount,
            PositionsQ = new ushort[vertexCount * 3],
            NormalsOct = new ushort[vertexCount],
            UvQ = new ushort[vertexCount * 2],
            BoneIndexA = new byte[vertexCount],
            BoneIndexB = new byte[vertexCount],
            BoneWeightA = new byte[vertexCount],
            Textures = Array.Empty<BasisFarLodPayload.FarLodTexture>(),
        };

        for (int i = 0; i < boneCount; i++)
        {
            payload.BoneHumanBodyBone[i] = (byte)i;
            payload.BoneParentIndex[i] = (byte)(i == 0 ? 0xFF : i - 1);
            payload.BoneRestLocalPosition[i] = new Vector3(
                NextFloat(random, -0.4f, 0.4f),
                NextFloat(random, 0f, 0.5f),
                NextFloat(random, -0.4f, 0.4f));
            payload.BoneRestLocalRotation[i] = RandomUnitQuaternion(random);
        }

        for (int i = 0; i < vertexCount; i++)
        {
            payload.PositionsQ[i * 3 + 0] = (ushort)random.Next(0, 65536);
            payload.PositionsQ[i * 3 + 1] = (ushort)random.Next(0, 65536);
            payload.PositionsQ[i * 3 + 2] = (ushort)random.Next(0, 65536);
            payload.NormalsOct[i] = (ushort)random.Next(0, 65536);
            payload.UvQ[i * 2 + 0] = (ushort)random.Next(0, 65536);
            payload.UvQ[i * 2 + 1] = (ushort)random.Next(0, 65536);
            payload.BoneIndexA[i] = (byte)random.Next(0, boneCount);
            payload.BoneIndexB[i] = (byte)random.Next(0, boneCount);
            payload.BoneWeightA[i] = (byte)random.Next(0, 256);
        }

        int triangleCount = vertexCount - 2;
        payload.Indices = new ushort[triangleCount * 3];
        for (int i = 0; i < triangleCount; i++)
        {
            payload.Indices[i * 3 + 0] = 0;
            payload.Indices[i * 3 + 1] = (ushort)(i + 1);
            payload.Indices[i * 3 + 2] = (ushort)(i + 2);
        }

        return payload;
    }

    public static string CreateBase64(int vertexCount = 24, int boneCount = 4, int seed = 1234)
    {
        return Convert.ToBase64String(Create(vertexCount, boneCount, seed).Serialize());
    }

    /// <summary>Structurally invalid blob (wrong magic) — TryParse refuses it WITHOUT logging.</summary>
    public static string CreateRefusedBase64()
    {
        return Convert.ToBase64String(new byte[64]);
    }

    public static Quaternion RandomUnitQuaternion(System.Random random)
    {
        Quaternion q = new Quaternion(
            NextFloat(random, -1f, 1f),
            NextFloat(random, -1f, 1f),
            NextFloat(random, -1f, 1f),
            NextFloat(random, -1f, 1f));
        float magnitude = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        if (magnitude < 1e-4f)
        {
            return Quaternion.identity;
        }
        return new Quaternion(q.x / magnitude, q.y / magnitude, q.z / magnitude, q.w / magnitude);
    }

    public static float NextFloat(System.Random random, float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }
}
