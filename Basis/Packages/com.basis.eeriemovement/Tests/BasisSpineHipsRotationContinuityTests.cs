using System.Text;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using Basis.IK;
namespace Basis.Tests.IK
{
    public class BasisSpineHipsRotationContinuityTests
    {
        static readonly string[] names = { "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head" };
        static readonly float[] heights = { 0.95f, 1.06f, 1.21f, 1.33f, 1.45f, 1.57f };
        const float maxStepDeg = 3f;
        GameObject root;
        BasisPoseSkeleton skeleton;
        NativeArray<BasisBoneHandle> chain;
        Transform[] bones;
        [SetUp]
        public void SetUp()
        {
            root = new GameObject("HipsRotationRig");
            bones = new Transform[names.Length];
            Transform parent = root.transform;
            for (int i = 0; i < names.Length; i++)
            {
                var go = new GameObject(names[i]);
                go.transform.SetPositionAndRotation(new Vector3(0f, heights[i], 0f), Quaternion.identity);
                go.transform.SetParent(parent, true);
                bones[i] = go.transform;
                parent = go.transform;
            }
            skeleton = new BasisPoseSkeleton();
            skeleton.Build(bones[0], bones);
            skeleton.GatherNow();
            chain = new NativeArray<BasisBoneHandle>(names.Length, Allocator.Persistent);
            for (int i = 0; i < names.Length; i++)
            {
                chain[i] = skeleton.Bind(bones[names.Length - 1 - i]);
            }
        }
        [TearDown]
        public void TearDown()
        {
            if (chain.IsCreated) chain.Dispose();
            skeleton?.Dispose();
            skeleton = null;
            if (root != null) Object.DestroyImmediate(root);
        }
        BasisEerieMovement Job(bool hipsTracked)
        {
            var job = new BasisEerieMovement
            {
                chainHeadToSpine = chain,
                chainChestIdx = 3,
                handleHips = skeleton.Bind(bones[0]),
                handleSpine = skeleton.Bind(bones[1]),
                handleChest = skeleton.Bind(bones[2]),
                handleUpperChest = skeleton.Bind(bones[3]),
                handleNeck = skeleton.Bind(bones[4]),
                handleHead = skeleton.Bind(bones[5]),
                spineMaxIterations = 20,
                spineTolerance = 0.0005f,
                spineCCDRelax = 1.0f,
                spineTwistKeep = 0.25f,
                spineNeckTwistKeep = 0.9f,
                neckMaxConeDeg = 45f,
                maxChestDeltaDeg = 90f,
                spineTautBandFrac = 0.015f,
                spineBendPitch = 0.40f, spineBendYaw = 0.10f, spineBendRoll = 0.30f,
                chestBendPitch = 0.20f, chestBendYaw = 0.15f, chestBendRoll = 0.15f,
                upperChestBendPitch = 0.15f, upperChestBendYaw = 0.15f, upperChestBendRoll = 0.15f,
                spineMaxForwardDeg = 60f, spineMaxBackwardDeg = 25f, spineMaxLateralDeg = 25f,
                spineSquishBoost = 0.5f, spineGazeFollow = 0.25f,
                anatDifferentialStiffness = true, anatPelvicTwistRouting = true,
                bendTwistCoupling = 0.15f,
                neckYawShare = 0.5f,
                spineStretchMax = 0.03f,
                minHeadSpineHeight = 0.62f,
                ikLockMode = BasisIKLockMode.LockHead,
                offsetRotationHead = Quaternion.identity,
                offsetRotationHips = Quaternion.identity,
                offsetRotationChest = Quaternion.identity,
                playerUp = Vector3.up,
                chestIkTarget = true,
                spineAnatomicalRom = false,
                tposeHeadToNeckLocal = new Vector3(0f, -0.12f, 0f),
                tposeLengthNeckToHips = new Vector3(0f, 0.5f, 0f),
                trunkCounterbalance = 0.38f, trunkCounterbalanceMaxSpineFrac = 0.45f,
                moveBodyBackWhenCrouching = 1f, standingHeadHeight = 1.57f,
                hipHingeStartDeg = 40f, hipHingeMaxAddDeg = 52f,
                targetPositionHips = new Vector3(0f, 0.95f, 0f),
                targetRotationHips = Quaternion.identity,
            };
            BasisEeriePlanner.Bind(ref job);
            BasisEeriePlanner.Frame(ref job, new BasisEerieFrameFacts { hipsTracked = hipsTracked });
            return job;
        }
        void Solve(ref BasisEerieMovement job, Vector3 hipsPos, Quaternion hipsRot, Vector3 headPos, Quaternion headRot, Quaternion[] outRots)
        {
            job.targetPositionHips = hipsPos;
            job.targetRotationHips = hipsRot;
            job.targetPositionHead = headPos;
            job.targetRotationHead = headRot;
            skeleton.GatherNow();
            job.poseStream = skeleton.Stream;
            job.SolveSpine();
            for (int i = 0; i < names.Length; i++) outRots[i] = skeleton.Stream.GetRotation(chain[i]);
        }
        float Sweep(ref BasisEerieMovement job, int steps, System.Func<int, (Vector3 hips, Quaternion hipsRot, Vector3 head, Quaternion headRot)> at, StringBuilder report, out int worstStep, out int worstJoint, out float worstHeadErr)
        {
            var prev = new Quaternion[names.Length];
            var cur = new Quaternion[names.Length];
            float worst = 0f;
            int worstHeadStep = -1;
            worstStep = -1;
            worstJoint = -1;
            worstHeadErr = 0f;
            for (int s = 0; s <= steps; s++)
            {
                var (hips, hipsRot, head, headRot) = at(s);
                Solve(ref job, hips, hipsRot, head, headRot, cur);
                float headErr = (skeleton.Stream.GetPosition(job.handleHead) - head).magnitude;
                if (headErr > worstHeadErr)
                {
                    worstHeadErr = headErr;
                    worstHeadStep = s;
                }
                if (s > 0)
                {
                    for (int i = 0; i < names.Length; i++)
                    {
                        float step = Quaternion.Angle(prev[i], cur[i]);
                        if (step > worst)
                        {
                            worst = step;
                            worstStep = s;
                            worstJoint = i;
                        }
                    }
                }
                for (int i = 0; i < names.Length; i++) prev[i] = cur[i];
            }
            report.AppendLine($"  worst per-step joint change {worst:F2} deg at step {worstStep} on chain[{worstJoint}] ({(worstJoint >= 0 ? names[names.Length - 1 - worstJoint] : "-")}), worst head error {worstHeadErr * 1000f:F2} mm at step {worstHeadStep}");
            return worst;
        }
        [Test]
        public void TrackedHipsYaw_SweptThroughAHeadTurn_DoesNotPopTheSpine()
        {
            var job = Job(hipsTracked: true);
            var report = new StringBuilder("tracked hips yaw -170..170 deg, 1 deg steps, head fixed at rest:\n");
            Vector3 hips = bones[0].position, head = bones[5].position;
            float worst = Sweep(ref job, 340, s => (hips, Quaternion.Euler(0f, -170f + s, 0f), head, Quaternion.identity), report, out _, out _, out float headErr);
            TestContext.WriteLine(report.ToString());
            Assert.Less(worst, maxStepDeg, "a 1 deg hips yaw step must not pop any spine joint");
            Assert.Less(headErr, 0.002f, "the head must stay on target through the whole yaw sweep");
        }
        [Test]
        public void TrackedHipsPitchAndRoll_Swept_DoNotPopTheSpine()
        {
            var job = Job(hipsTracked: true);
            var report = new StringBuilder("tracked hips pitch -50..50 then roll -40..40, 1 deg steps, head fixed at rest:\n");
            Vector3 hips = bones[0].position, head = bones[5].position;
            float worstPitch = Sweep(ref job, 100, s => (hips, Quaternion.Euler(-50f + s, 0f, 0f), head, Quaternion.identity), report, out _, out _, out float headErrPitch);
            float worstRoll = Sweep(ref job, 80, s => (hips, Quaternion.Euler(0f, 0f, -40f + s), head, Quaternion.identity), report, out _, out _, out float headErrRoll);
            TestContext.WriteLine(report.ToString());
            Assert.Less(worstPitch, maxStepDeg, "a 1 deg hips pitch step must not pop any spine joint");
            Assert.Less(worstRoll, maxStepDeg, "a 1 deg hips roll step must not pop any spine joint");
            Assert.Less(Mathf.Max(headErrPitch, headErrRoll), 0.002f, "the head must stay on target through the pitch/roll sweeps");
        }
        [Test]
        public void HeadOrbit_AroundTheChainAxis_DoesNotPopTheSpine()
        {
            var job = Job(hipsTracked: true);
            var report = new StringBuilder("head orbits a 6 cm circle 3 cm below rest (compressed, crossing every bow plane), 2 deg steps:\n");
            Vector3 hips = bones[0].position, restHead = bones[5].position;
            float worst = Sweep(ref job, 180, s => { float t = s * 2f * Mathf.Deg2Rad; return (hips, Quaternion.identity, restHead + new Vector3(0.06f * Mathf.Cos(t), -0.03f, 0.06f * Mathf.Sin(t)), Quaternion.identity); }, report, out _, out _, out float headErr);
            TestContext.WriteLine(report.ToString());
            Assert.Less(worst, maxStepDeg, "a 2 deg head orbit step must not pop any spine joint");
            Assert.Less(headErr, 0.006f, "the head must stay near its target around the whole orbit (band residual + solver polish)");
        }
        [Test]
        public void SynthesizedHips_HeadLeansThroughEveryDirection_DoesNotPopTheSpine()
        {
            var job = Job(hipsTracked: false);
            var report = new StringBuilder("headset-only: head leans 12 cm around the compass, 2 deg steps:\n");
            Vector3 hips = bones[0].position, restHead = bones[5].position;
            float worst = Sweep(ref job, 180, s => { float t = s * 2f * Mathf.Deg2Rad; return (hips, Quaternion.identity, restHead + new Vector3(0.12f * Mathf.Cos(t), -0.02f, 0.12f * Mathf.Sin(t)), Quaternion.identity); }, report, out _, out _, out float headErr);
            TestContext.WriteLine(report.ToString());
            Assert.Less(worst, maxStepDeg, "a 2 deg lean-direction step must not pop any spine joint");
            Assert.Less(headErr, 0.002f, "the head must stay on target around the whole lean circle");
        }
    }
}
