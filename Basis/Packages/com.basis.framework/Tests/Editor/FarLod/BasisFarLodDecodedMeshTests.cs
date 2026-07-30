using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Correctness of the worker-thread payload decode: round-trip integrity, the hand-rolled
/// rigid-TRS inverse against Unity's native matrix math, bone-weight layout, cache reuse,
/// and thread-parity of the whole parse+decode path.
/// </summary>
public class BasisFarLodDecodedMeshTests
{
    [Test]
    public void RoundTrip_SerializeParse_PreservesCoreFields()
    {
        BasisFarLodPayload source = BasisFarLodTestPayloads.Create(vertexCount: 32, boneCount: 6, seed: 7);
        BasisFarLodPayload parsed = BasisFarLodPayload.TryParseBase64(Convert.ToBase64String(source.Serialize()));

        Assert.NotNull(parsed, "structurally valid payload must parse");
        Assert.AreEqual(source.VertexCount, parsed.VertexCount);
        Assert.AreEqual(source.BoneCount, parsed.BoneCount);
        CollectionAssert.AreEqual(source.Indices, parsed.Indices);
        CollectionAssert.AreEqual(source.PositionsQ, parsed.PositionsQ);
        CollectionAssert.AreEqual(source.NormalsOct, parsed.NormalsOct);
        CollectionAssert.AreEqual(source.BoneWeightA, parsed.BoneWeightA);
        Assert.AreEqual(source.PositionBoundsMin, parsed.PositionBoundsMin);
        Assert.AreEqual(source.PositionBoundsMax, parsed.PositionBoundsMax);
    }

    [Test]
    public void TryParse_WrongMagic_RefusesSilently()
    {
        Assert.IsNull(BasisFarLodPayload.TryParseBase64(BasisFarLodTestPayloads.CreateRefusedBase64()));
    }

    [Test]
    public void InverseRigidTRS_MatchesNativeMatrixInverse()
    {
        System.Random random = new System.Random(42);
        for (int iteration = 0; iteration < 64; iteration++)
        {
            Vector3 position = new Vector3(
                BasisFarLodTestPayloads.NextFloat(random, -3f, 3f),
                BasisFarLodTestPayloads.NextFloat(random, -3f, 3f),
                BasisFarLodTestPayloads.NextFloat(random, -3f, 3f));
            Quaternion rotation = BasisFarLodTestPayloads.RandomUnitQuaternion(random);

            Matrix4x4 expected = Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
            Matrix4x4 actual = BasisFarLodPayload.InverseRigidTRS(position, rotation);

            for (int component = 0; component < 16; component++)
            {
                Assert.AreEqual(expected[component], actual[component], 1e-5f,
                    $"component {component} at iteration {iteration} (pos {position}, rot {rotation})");
            }
        }
    }

    [Test]
    public void Decoded_BindposesAndWeights_MatchIndependentMath()
    {
        BasisFarLodPayload payload = BasisFarLodPayload.TryParseBase64(BasisFarLodTestPayloads.CreateBase64(vertexCount: 48, boneCount: 8, seed: 21));
        Assert.NotNull(payload);
        BasisFarLodPayload.DecodedMeshData decoded = payload.PrepareDecodedMeshData();

        payload.ComputeBoneRootSpace(out Vector3[] bonePositions, out Quaternion[] boneRotations);
        Assert.AreEqual(payload.BoneCount, decoded.Bindposes.Length);
        for (int bone = 0; bone < payload.BoneCount; bone++)
        {
            Matrix4x4 expected = Matrix4x4.TRS(bonePositions[bone], boneRotations[bone], Vector3.one).inverse;
            for (int component = 0; component < 16; component++)
            {
                Assert.AreEqual(expected[component], decoded.Bindposes[bone][component], 1e-4f, $"bindpose {bone}[{component}]");
            }
        }

        int cursor = 0;
        for (int i = 0; i < payload.VertexCount; i++)
        {
            bool twoInfluences = payload.BoneIndexB[i] != payload.BoneIndexA[i] && payload.BoneWeightA[i] < 255;
            Assert.AreEqual(twoInfluences ? 2 : 1, decoded.BonesPerVertex[i], $"influences at vertex {i}");
            if (twoInfluences)
            {
                Assert.AreEqual(payload.BoneIndexA[i], decoded.BoneWeights[cursor].boneIndex);
                Assert.AreEqual(payload.BoneIndexB[i], decoded.BoneWeights[cursor + 1].boneIndex);
                Assert.AreEqual(1f, decoded.BoneWeights[cursor].weight + decoded.BoneWeights[cursor + 1].weight, 1e-5f);
                cursor += 2;
            }
            else
            {
                Assert.AreEqual(payload.BoneIndexA[i], decoded.BoneWeights[cursor].boneIndex);
                Assert.AreEqual(1f, decoded.BoneWeights[cursor].weight, 1e-6f);
                cursor += 1;
            }
        }
        Assert.AreEqual(cursor, decoded.BoneWeights.Length, "influence array exactly filled");
        CollectionAssert.AreEqual(payload.Indices, decoded.Triangles);
    }

    [Test]
    public void Decoded_IsCached_AndReusedByCreateMesh()
    {
        BasisFarLodPayload payload = BasisFarLodPayload.TryParseBase64(BasisFarLodTestPayloads.CreateBase64(seed: 33));
        Assert.NotNull(payload);

        BasisFarLodPayload.DecodedMeshData first = payload.PrepareDecodedMeshData();
        BasisFarLodPayload.DecodedMeshData second = payload.PrepareDecodedMeshData();
        Assert.AreSame(first, second, "decode must run once and cache");

        Mesh mesh = payload.CreateMesh();
        Assert.NotNull(mesh);
        Assert.AreEqual(payload.VertexCount, mesh.vertexCount);
        Matrix4x4[] meshBindposes = mesh.bindposes;
        Assert.AreEqual(first.Bindposes.Length, meshBindposes.Length);
        for (int bone = 0; bone < meshBindposes.Length; bone++)
        {
            Assert.AreEqual(first.Bindposes[bone], meshBindposes[bone], $"mesh bindpose {bone} comes from the decoded cache");
        }
        UnityEngine.Object.DestroyImmediate(mesh);
    }

    [Test]
    public void ReleaseMeshSourceData_KeepsSkeletonAndAnchors()
    {
        BasisFarLodPayload payload = BasisFarLodPayload.TryParseBase64(BasisFarLodTestPayloads.CreateBase64(seed: 77));
        Assert.NotNull(payload);
        Mesh mesh = payload.CreateMesh();
        Assert.NotNull(mesh, "mesh must build before release");

        payload.ReleaseMeshSourceData();

        Assert.IsNull(payload.PositionsQ);
        Assert.IsNull(payload.Indices);
        Assert.NotNull(payload.BoneRestLocalPosition, "skeleton survives — per-player builds read it");
        Assert.NotNull(payload.BoneRestLocalRotation);
        Assert.Greater(payload.BoneCount, 0);
        Assert.AreNotEqual(Vector3.zero, payload.LocalBoundsExtents, "renderer bounds survive");

        UnityEngine.Object.DestroyImmediate(mesh);
    }

    [Test]
    public void ParseAndDecode_OnWorkerThread_MatchesMainThread()
    {
        string base64 = BasisFarLodTestPayloads.CreateBase64(vertexCount: 96, boneCount: 10, seed: 55);

        BasisFarLodPayload mainThread = BasisFarLodPayload.TryParseBase64(base64);
        Assert.NotNull(mainThread);
        BasisFarLodPayload.DecodedMeshData mainDecoded = mainThread.PrepareDecodedMeshData();

        BasisFarLodPayload workerThread = null;
        BasisFarLodPayload.DecodedMeshData workerDecoded = null;
        Task worker = Task.Run(() =>
        {
            workerThread = BasisFarLodPayload.TryParseBase64(base64);
            workerDecoded = workerThread?.PrepareDecodedMeshData();
        });
        Assert.IsTrue(worker.Wait(TimeSpan.FromSeconds(30)), "worker parse must finish");
        Assert.NotNull(workerThread, "parse must succeed off the main thread");
        Assert.NotNull(workerDecoded, "decode must succeed off the main thread");

        CollectionAssert.AreEqual(mainDecoded.Vertices, workerDecoded.Vertices);
        CollectionAssert.AreEqual(mainDecoded.Normals, workerDecoded.Normals);
        CollectionAssert.AreEqual(mainDecoded.Uv, workerDecoded.Uv);
        CollectionAssert.AreEqual(mainDecoded.Triangles, workerDecoded.Triangles);
        CollectionAssert.AreEqual(mainDecoded.BonesPerVertex, workerDecoded.BonesPerVertex);
        Assert.AreEqual(mainDecoded.BoneWeights.Length, workerDecoded.BoneWeights.Length);
        CollectionAssert.AreEqual(mainDecoded.Bindposes, workerDecoded.Bindposes);
    }
}
