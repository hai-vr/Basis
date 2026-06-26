using Basis.Scripts.Networking.Sync;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
using static Basis.Tests.Sync.BasisSyncTestSupport;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// The far side's final step: <see cref="ApplySyncTransformsJob"/> writes the interpolated pool values onto the
    /// bound transforms. Verifies the pos+rot / pos-only / rot-only / scale paths, world vs local space, the -1
    /// "unbound component untouched" rule, and the Active==0 skip — driven through the real Burst job over a real
    /// TransformAccessArray.
    /// </summary>
    public class BasisSyncApplyTransformTests
    {
        [Test]
        public void ApplyTransforms_WritesEachBoundChannel_AndSkipsInactive()
        {
            var parent = new GameObject("apply-parent");
            parent.transform.SetPositionAndRotation(new Vector3(100f, 200f, 300f), Quaternion.Euler(0f, 40f, 0f));

            GameObject[] gos = null;
            var contOut = new NativeArray<float>(12, Allocator.TempJob);
            var rotOut = new NativeArray<quaternion>(3, Allocator.TempJob);
            var posIdx = new NativeArray<int>(5, Allocator.TempJob);
            var rotIdx = new NativeArray<int>(5, Allocator.TempJob);
            var scaleIdx = new NativeArray<int>(5, Allocator.TempJob);
            var worldSpace = new NativeArray<byte>(5, Allocator.TempJob);
            var active = new NativeArray<byte>(5, Allocator.TempJob);
            var bindSlot = new NativeArray<int>(5, Allocator.TempJob);
            TransformAccessArray taa = default;

            try
            {
                gos = new GameObject[5];
                for (int i = 0; i < 5; i++)
                {
                    gos[i] = new GameObject("apply-" + i);
                    gos[i].transform.SetParent(parent.transform, false);
                }
                // Pre-seed the inactive object so we can prove the job leaves it alone.
                gos[4].transform.localPosition = new Vector3(9f, 9f, 9f);

                // binding 0: pos+rot, local;  1: pos only, world;  2: rot only, local;  3: scale;  4: pos+rot, INACTIVE.
                contOut[0] = 1f; contOut[1] = 2f; contOut[2] = 3f;     // b0 pos
                contOut[3] = 5f; contOut[4] = 6f; contOut[5] = 7f;     // b1 pos
                contOut[6] = 2f; contOut[7] = 3f; contOut[8] = 4f;     // b3 scale
                contOut[9] = 0f; contOut[10] = 0f; contOut[11] = 0f;   // b4 pos (skipped)
                rotOut[0] = Quat(10f, 20f, 30f);
                rotOut[1] = Quat(45f, 0f, 0f);
                rotOut[2] = quaternion.identity;

                posIdx[0] = 0; rotIdx[0] = 0; scaleIdx[0] = -1; worldSpace[0] = 0;
                posIdx[1] = 3; rotIdx[1] = -1; scaleIdx[1] = -1; worldSpace[1] = 1;
                posIdx[2] = -1; rotIdx[2] = 1; scaleIdx[2] = -1; worldSpace[2] = 0;
                posIdx[3] = -1; rotIdx[3] = -1; scaleIdx[3] = 6; worldSpace[3] = 0;
                posIdx[4] = 9; rotIdx[4] = 2; scaleIdx[4] = -1; worldSpace[4] = 0;

                for (int i = 0; i < 5; i++) { active[i] = 1; bindSlot[i] = i; }
                active[4] = 0;

                taa = new TransformAccessArray(5);
                for (int i = 0; i < 5; i++) taa.Add(gos[i].transform);

                new ApplySyncTransformsJob
                {
                    ContOut = contOut,
                    RotOut = rotOut,
                    PosIdx = posIdx,
                    RotIdx = rotIdx,
                    ScaleIdx = scaleIdx,
                    WorldSpace = worldSpace,
                    Active = active,
                    BindSlot = bindSlot,
                }.Schedule(taa).Complete();

                // b0: pos+rot in local space
                AssertVec(new Vector3(1f, 2f, 3f), gos[0].transform.localPosition, "b0 local position");
                Assert.LessOrEqual(Quaternion.Angle(gos[0].transform.localRotation, ToUnity(Quat(10f, 20f, 30f))), 0.01f, "b0 local rotation");

                // b1: world position only
                AssertVec(new Vector3(5f, 6f, 7f), gos[1].transform.position, "b1 world position");

                // b2: local rotation only
                Assert.LessOrEqual(Quaternion.Angle(gos[2].transform.localRotation, ToUnity(Quat(45f, 0f, 0f))), 0.01f, "b2 local rotation");

                // b3: scale only
                AssertVec(new Vector3(2f, 3f, 4f), gos[3].transform.localScale, "b3 scale");

                // b4: inactive — must be left exactly as pre-seeded
                AssertVec(new Vector3(9f, 9f, 9f), gos[4].transform.localPosition, "b4 inactive position untouched");
            }
            finally
            {
                if (taa.isCreated) taa.Dispose();
                contOut.Dispose(); rotOut.Dispose();
                posIdx.Dispose(); rotIdx.Dispose(); scaleIdx.Dispose();
                worldSpace.Dispose(); active.Dispose(); bindSlot.Dispose();
                if (gos != null) foreach (GameObject go in gos) if (go != null) Object.DestroyImmediate(go);
                Object.DestroyImmediate(parent);
            }
        }

        static void AssertVec(Vector3 expected, Vector3 actual, string what)
        {
            Assert.LessOrEqual(Vector3.Distance(expected, actual), 1e-3f, $"{what}: expected {expected} got {actual}");
        }
    }
}
