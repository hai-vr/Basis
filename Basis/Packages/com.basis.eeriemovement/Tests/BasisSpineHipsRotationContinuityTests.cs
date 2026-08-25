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
            var trace = new System.Collections.Generic.List<Quaternion[]>();
            float worst = 0f;
            int worstHeadStep = -1;
            worstStep = -1;
            worstJoint = -1;
            worstHeadErr = 0f;
            for (int s = 0; s <= steps; s++)
            {
                var (hips, hipsRot, head, headRot) = at(s);
                Solve(ref job, hips, hipsRot, head, headRot, cur);
                trace.Add((Quaternion[])cur.Clone());
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
            if (worst > maxStepDeg && worstJoint >= 0)
            {
                report.Append("    per-step deltas around the worst step:");
                for (int s = Mathf.Max(1, worstStep - 5); s <= Mathf.Min(trace.Count - 1, worstStep + 5); s++)
                {
                    report.Append($" [{s}] {Quaternion.Angle(trace[s - 1][worstJoint], trace[s][worstJoint]):F2}");
                }
                report.AppendLine();
            }
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
            // Pitching a TRACKED pelvis 50 deg while the head does not move is a geometrically inconsistent
            // pose -- something has to give. The pelvis is measured hardware the legs are solved from, so the
            // head is charged instead, bounded by the yield deadzone (6% of the spine's reach).
            Assert.Less(worstPitch, maxStepDeg, "a 1 deg hips pitch step must not pop any spine joint");
            Assert.Less(worstRoll, maxStepDeg, "a 1 deg hips roll step must not pop any spine joint");
            Assert.Less(Mathf.Max(headErrPitch, headErrRoll), 0.035f, "the head may give at most the pelvis-yield deadzone when a tracked pelvis is rotated away from it");
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
        // The legs hang off the pelvis (BuildLegFrame roots on the hips bone), so ANY pelvis displacement the
        // spine solve introduces lands in the legs. Every other fixture here is a perfectly straight spine,
        // where the chain's path length and its straight-line chord are equal -- which is exactly the case
        // that cannot see a rest-distance change. This one is authored with a real S-curve so it can.
        Transform[] BuildCurvedRig()
        {
            if (chain.IsCreated) chain.Dispose();
            skeleton?.Dispose();
            if (root != null) Object.DestroyImmediate(root);
            Vector3[] curved =
            {
                new Vector3(0f, 0.95f, 0f), new Vector3(0f, 1.06f, -0.03f), new Vector3(0f, 1.21f, -0.05f),
                new Vector3(0f, 1.33f, -0.02f), new Vector3(0f, 1.45f, 0.01f), new Vector3(0f, 1.57f, 0f),
            };
            root = new GameObject("CurvedSpineRig");
            bones = new Transform[names.Length];
            Transform parent = root.transform;
            for (int i = 0; i < names.Length; i++)
            {
                var go = new GameObject(names[i]);
                go.transform.SetPositionAndRotation(curved[i], Quaternion.identity);
                go.transform.SetParent(parent, true);
                bones[i] = go.transform;
                parent = go.transform;
            }
            skeleton = new BasisPoseSkeleton();
            skeleton.Build(bones[0], bones);
            skeleton.GatherNow();
            chain = new NativeArray<BasisBoneHandle>(names.Length, Allocator.Persistent);
            for (int i = 0; i < names.Length; i++) chain[i] = skeleton.Bind(bones[names.Length - 1 - i]);
            return bones;
        }
        [Test]
        public void CurvedSpine_RestingPelvis_IsNotDisplacedByTheSpineSolve()
        {
            BuildCurvedRig();
            float path = 0f;
            for (int i = 1; i < names.Length; i++) path += Vector3.Distance(bones[i - 1].position, bones[i].position);
            float chord = Vector3.Distance(bones[0].position, bones[5].position);

            var report = new StringBuilder($"curved spine: hips->head path {path * 100f:F2} cm vs chord {chord * 100f:F2} cm (delta {(path - chord) * 100f:F2} cm)\n");
            var cur = new Quaternion[names.Length];
            foreach (bool hipsTracked in new[] { true, false })
            {
                var job = Job(hipsTracked);
                Vector3 hipsTarget = bones[0].position, headTarget = bones[5].position;
                Solve(ref job, hipsTarget, Quaternion.identity, headTarget, Quaternion.identity, cur);
                Vector3 solvedHips = skeleton.Stream.GetPosition(job.handleHips);
                float drift = (solvedHips - hipsTarget).magnitude, headErr = (skeleton.Stream.GetPosition(job.handleHead) - headTarget).magnitude;
                report.AppendLine($"  hipsTracked={hipsTracked}: pelvis drift {drift * 1000f:F2} mm (up {(solvedHips.y - hipsTarget.y) * 1000f:F2}, fwd {(solvedHips.z - hipsTarget.z) * 1000f:F2}), head err {headErr * 1000f:F2} mm");
                Assert.Less(drift, 0.002f, $"the resting pelvis must not be displaced by the spine solve (hipsTracked={hipsTracked}) -- the legs are solved from it");
                Assert.Less(headErr, 0.002f, $"the head must still be reached on a curved spine (hipsTracked={hipsTracked})");
            }
            TestContext.WriteLine(report.ToString());
        }
        [Test]
        public void CurvedSpine_HeadSweptUpAndDown_DoesNotDragThePelvis()
        {
            BuildCurvedRig();
            var job = Job(hipsTracked: true);
            var report = new StringBuilder("curved spine, tracked hips: head swept +/-6 cm vertically and +/-6 cm forward, 1 mm steps:\n");
            Vector3 hips = bones[0].position, head = bones[5].position;
            var cur = new Quaternion[names.Length];
            float worstDrift = 0f;
            int worstStep = -1;
            for (int s = 0; s <= 120; s++)
            {
                Vector3 target = head + new Vector3(0f, (s - 60) * 0.001f, Mathf.Sin(s * 0.05f) * 0.06f);
                Solve(ref job, hips, Quaternion.identity, target, Quaternion.identity, cur);
                float drift = (skeleton.Stream.GetPosition(job.handleHips) - hips).magnitude;
                if (drift > worstDrift) { worstDrift = drift; worstStep = s; }
            }
            report.AppendLine($"  worst pelvis drift {worstDrift * 1000f:F2} mm at step {worstStep}");
            TestContext.WriteLine(report.ToString());
            Assert.Less(worstDrift, 0.005f, "a tracked pelvis must stay put through ordinary head motion -- the legs are solved from it, so pelvis drift IS leg error");
        }
        [Test]
        public void GrosslyDisplacedTrackedHips_StillYield_SoTheHeadStaysPinned()
        {
            BuildCurvedRig();
            var job = Job(hipsTracked: true);
            Vector3 hips = bones[0].position, head = bones[5].position;
            var cur = new Quaternion[names.Length];
            // The hips tracker parked 30 cm below/behind where the head can possibly reach: in lock-head the
            // pelvis is what gives, or the head comes off the HMD.
            Vector3 farHips = hips + new Vector3(0f, -0.25f, -0.15f);
            Solve(ref job, farHips, Quaternion.identity, head, Quaternion.identity, cur);
            float yielded = (skeleton.Stream.GetPosition(job.handleHips) - farHips).magnitude;
            float headErr = (skeleton.Stream.GetPosition(job.handleHead) - head).magnitude;
            TestContext.WriteLine($"hips tracker parked 29 cm out of reach: pelvis yielded {yielded * 1000f:F1} mm, head err {headErr * 1000f:F2} mm");
            Assert.Greater(yielded, 0.05f, "a grossly displaced hips tracker must still yield in lock-head mode");
            Assert.Less(headErr, 0.005f, "and the head must stay on the HMD once it has");
        }
        [Test]
        public void TrackedChestYaw_SweptThroughTheMiddle_DoesNotSnap()
        {
            var job = Job(hipsTracked: true);
            BasisEeriePlanner.Frame(ref job, new BasisEerieFrameFacts { hipsTracked = true, chestTracked = true });
            var report = new StringBuilder("tracked chest yaw -60..60 deg, 1 deg steps, hips + head fixed (the left-right sweep through the middle):\n");
            Vector3 hips = bones[0].position, head = bones[5].position, chest = bones[2].position;
            float worst = Sweep(ref job, 120, s =>
            {
                job.targetPositionChest = chest;
                job.targetPositionChestRaw = chest;
                job.targetRotationChest = Quaternion.Euler(0f, -60f + s, 0f);
                return (hips, Quaternion.identity, head, Quaternion.identity);
            }, report, out _, out _, out float headErr);
            TestContext.WriteLine(report.ToString());
            Assert.Less(worst, maxStepDeg, "a 1 deg tracked-chest yaw step must not snap any spine joint, especially through the middle where the upper chain can just reach the head");
            Assert.Less(headErr, 0.001f, "the head must not bob as the chest yaws -- it stays on the HMD throughout");
        }
        [Test]
        public void TrackedChestPitch_SweptThroughTheMiddle_DoesNotSnap()
        {
            var job = Job(hipsTracked: true);
            BasisEeriePlanner.Frame(ref job, new BasisEerieFrameFacts { hipsTracked = true, chestTracked = true });
            var report = new StringBuilder("tracked chest pitch -35..35 then roll -30..30, 1 deg steps, hips + head fixed:\n");
            Vector3 hips = bones[0].position, head = bones[5].position, chest = bones[2].position;
            float worstPitch = Sweep(ref job, 70, s =>
            {
                job.targetPositionChest = chest;
                job.targetPositionChestRaw = chest;
                job.targetRotationChest = Quaternion.Euler(-35f + s, 0f, 0f);
                return (hips, Quaternion.identity, head, Quaternion.identity);
            }, report, out _, out _, out float headErrPitch);
            float worstRoll = Sweep(ref job, 60, s =>
            {
                job.targetPositionChest = chest;
                job.targetPositionChestRaw = chest;
                job.targetRotationChest = Quaternion.Euler(0f, 0f, -30f + s);
                return (hips, Quaternion.identity, head, Quaternion.identity);
            }, report, out _, out _, out float headErrRoll);
            TestContext.WriteLine(report.ToString());
            Assert.Less(worstPitch, maxStepDeg, "a 1 deg tracked-chest pitch step must not snap any spine joint");
            Assert.Less(worstRoll, maxStepDeg, "a 1 deg tracked-chest roll step must not snap any spine joint");
            Assert.Less(Mathf.Max(headErrPitch, headErrRoll), 0.001f, "the head must not bob as the chest pitches or rolls -- it stays on the HMD throughout");
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
