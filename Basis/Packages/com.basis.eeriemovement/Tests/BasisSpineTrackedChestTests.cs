using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using Basis.IK;
namespace Basis.Tests.IK
{
    public class BasisSpineTrackedChestTests
    {
        static readonly string[] names = { "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head" };
        static readonly float[] heights = { 0.95f, 1.06f, 1.21f, 1.33f, 1.45f, 1.57f };
        GameObject root;
        BasisPoseSkeleton skeleton;
        NativeArray<BasisBoneHandle> chain;
        Transform[] bones;
        [SetUp]
        public void SetUp()
        {
            root = new GameObject("TrackedChestRig");
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
        BasisEerieMovement Job(bool chestTracked, bool chestIkTarget = true)
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
                neckYawShare = 0.5f,
                spineStretchMax = 0.03f,
                chestIkWeight = 0.5f,
                chestIkIterations = 8,
                chestIkHeadRestoreSweeps = 2,
                chestPosPullMaxDeg = 20f,
                chestPullMaxDist = 0.5f,
                minHeadSpineHeight = 0.62f,
                ikLockMode = BasisIKLockMode.LockHead,
                offsetRotationHead = Quaternion.identity,
                offsetRotationHips = Quaternion.identity,
                offsetRotationChest = Quaternion.identity,
                playerUp = Vector3.up,
                chestIkTarget = chestIkTarget,
                spineAnatomicalRom = false,
                tposeHeadToNeckLocal = new Vector3(0f, -0.12f, 0f),
                tposeLengthNeckToHips = new Vector3(0f, 0.5f, 0f),
                targetPositionHips = new Vector3(0f, 0.95f, 0f),
                targetRotationHips = Quaternion.identity,
            };
            BasisEeriePlanner.Bind(ref job);
            BasisEeriePlanner.Frame(ref job, new BasisEerieFrameFacts { hipsTracked = true, chestTracked = chestTracked });
            return job;
        }
        void Solve(ref BasisEerieMovement job, Vector3 headPos, Quaternion headRot, Vector3 chestPos, Quaternion chestRot)
        {
            job.targetPositionHead = headPos;
            job.targetRotationHead = headRot;
            job.targetPositionChest = chestPos;
            job.targetPositionChestRaw = chestPos;
            job.targetRotationChest = chestRot;
            skeleton.GatherNow();
            job.poseStream = skeleton.Stream;
            job.SolveSpine();
        }
        [Test]
        public void TrackedChest_KeepsTheTrackerRotation_WhileTheHeadIsReached()
        {
            var job = Job(chestTracked: true);
            Quaternion chestRot = Quaternion.Euler(8f, 20f, -4f), gaze = Quaternion.Euler(10f, 35f, 0f);
            Vector3 chestPos = bones[2].position, upperPos = chestPos + chestRot * Vector3.up * (heights[3] - heights[2]);
            Vector3 headPos = upperPos + chestRot * Quaternion.Euler(10f, 0f, 8f) * Vector3.up * (heights[5] - heights[3]);
            Solve(ref job, headPos, gaze, chestPos, chestRot);

            float chestRotErr = Quaternion.Angle(skeleton.Stream.GetRotation(job.handleChest), chestRot);
            float headPosErr = (skeleton.Stream.GetPosition(job.handleHead) - headPos).magnitude, headRotErr = Quaternion.Angle(skeleton.Stream.GetRotation(job.handleHead), gaze);
            float chestPosErr = (skeleton.Stream.GetPosition(job.handleChest) - chestPos).magnitude;
            TestContext.WriteLine($"chest rot err {chestRotErr:F4} deg, chest pos err {chestPosErr * 1000f:F2} mm, head pos err {headPosErr * 1000f:F2} mm, head rot err {headRotErr:F4} deg");
            Assert.Less(chestRotErr, 0.01f, "a tracked chest must keep its tracker rotation while the head is reachable above it");
            Assert.Less(headPosErr, 0.002f, "the head must be reached by the joints above the tracked chest");
            Assert.Less(headRotErr, 0.01f, "the head must stay pinned to the gaze");
            Assert.Less(chestPosErr, 0.02f, "the lumbar must carry the chest toward the tracked position");
        }
        [Test]
        public void TrackedChest_RelaxesTowardTheHead_WhenTheHeadIsOutOfItsReach()
        {
            var job = Job(chestTracked: true);
            Quaternion chestRot = Quaternion.Euler(55f, 0f, 0f), gaze = Quaternion.identity;
            Vector3 chestPos = bones[2].position, headPos = chestPos + Vector3.up * (heights[5] - heights[2]) + new Vector3(0f, 0.01f, -0.12f);
            Solve(ref job, headPos, gaze, chestPos, chestRot);

            float chestRotErr = Quaternion.Angle(skeleton.Stream.GetRotation(job.handleChest), chestRot);
            float headPosErr = (skeleton.Stream.GetPosition(job.handleHead) - headPos).magnitude, headRotErr = Quaternion.Angle(skeleton.Stream.GetRotation(job.handleHead), gaze);
            TestContext.WriteLine($"chest relaxed off its tracker by {chestRotErr:F2} deg, head pos err {headPosErr * 1000f:F2} mm, head rot err {headRotErr:F4} deg");
            Assert.Less(headPosErr, 0.01f, "when the joints above the chest cannot reach the head, the chest and lumbar must relax until the head is hit");
            Assert.Greater(chestRotErr, 1f, "the relax must actually move the chest off its tracker to get there");
            Assert.Less(headRotErr, 0.01f, "the head must stay pinned to the gaze");
        }
        [Test]
        public void UntrackedChest_IgnoresTheChestTargetEntirely()
        {
            Vector3 headPos = bones[5].position + new Vector3(0.02f, -0.01f, 0.03f);
            Quaternion gaze = Quaternion.Euler(5f, 15f, 0f);
            Vector3 farChest = bones[2].position + new Vector3(0.15f, 0f, 0.1f);

            var with = Job(chestTracked: false, chestIkTarget: true);
            Solve(ref with, headPos, gaze, farChest, Quaternion.identity);
            var chestWith = new Quaternion[names.Length];
            for (int i = 0; i < names.Length; i++) chestWith[i] = skeleton.Stream.GetRotation(chain[i]);

            SetUpAgain();
            var without = Job(chestTracked: false, chestIkTarget: false);
            Solve(ref without, headPos, gaze, farChest, Quaternion.identity);
            for (int i = 0; i < names.Length; i++)
            {
                Assert.Less(Quaternion.Angle(chestWith[i], skeleton.Stream.GetRotation(chain[i])), 1e-4f, $"chain joint {i} must be bit-identical with and without the chest IK target when no chest tracker is present");
            }
        }
        [Test]
        public void TrackedHips_StretchTheSpine_ThenYieldTheHips_BeforeTheHeadDetaches()
        {
            float reach = heights[5] - heights[0];
            Vector3 hipsPos = bones[0].position, headPos = hipsPos + Vector3.up * (reach * 1.02f);

            var stretch = Job(chestTracked: false);
            Solve(ref stretch, headPos, Quaternion.identity, bones[2].position, Quaternion.identity);
            float errStretch = (skeleton.Stream.GetPosition(stretch.handleHead) - headPos).magnitude, hipsMovedStretch = (skeleton.Stream.GetPosition(stretch.handleHips) - hipsPos).magnitude;

            SetUpAgain();
            var yield = Job(chestTracked: false);
            yield.spineStretchMax = 0f;
            Solve(ref yield, headPos, Quaternion.identity, bones[2].position, Quaternion.identity);
            float errYield = (skeleton.Stream.GetPosition(yield.handleHead) - headPos).magnitude, hipsMovedYield = (skeleton.Stream.GetPosition(yield.handleHips) - hipsPos).magnitude;

            SetUpAgain();
            var lockHips = Job(chestTracked: false);
            lockHips.spineStretchMax = 0f;
            lockHips.ikLockMode = BasisIKLockMode.LockHips;
            Solve(ref lockHips, headPos, Quaternion.identity, bones[2].position, Quaternion.identity);
            float errLockHips = (skeleton.Stream.GetPosition(lockHips.handleHead) - headPos).magnitude, hipsMovedLockHips = (skeleton.Stream.GetPosition(lockHips.handleHips) - hipsPos).magnitude;

            TestContext.WriteLine($"stretch: head err {errStretch * 1000f:F2} mm hips moved {hipsMovedStretch * 1000f:F2} mm | yield: head err {errYield * 1000f:F2} mm hips moved {hipsMovedYield * 1000f:F2} mm | lock hips: head err {errLockHips * 1000f:F2} mm hips moved {hipsMovedLockHips * 1000f:F2} mm");
            Assert.Less(errStretch, 0.0015f, "a 2% over-reach must be absorbed by the spine stretch");
            Assert.Less(hipsMovedStretch, 0.0005f, "the tracked hips must not move while the stretch can cover the gap");
            Assert.Less(errYield, 0.0015f, "in lock-head mode the head stays on the HMD even with the stretch off");
            Assert.Greater(hipsMovedYield, 0.005f, "in lock-head mode the tracked hips yield toward the head once the chain cannot span the gap");
            Assert.Greater(errLockHips, 0.005f, "in lock-hips mode the head is the one that gives");
            Assert.Less(hipsMovedLockHips, 0.0005f, "in lock-hips mode the tracked hips stay put");
        }
        NativeArray<BasisSpineRestFrame> BakeRestFrames()
        {
            var frames = new NativeArray<BasisSpineRestFrame>(names.Length, Allocator.Persistent);
            BasisSpineSegment[] segments = { BasisSpineSegment.Cervical, BasisSpineSegment.UpperThoracic, BasisSpineSegment.LowerThoracic, BasisSpineSegment.Lumbar };
            for (int i = 1; i <= 4; i++)
            {
                Transform bone = bones[names.Length - 1 - i], child = bones[names.Length - i], parent = bones[names.Length - 2 - i];
                BasisSpineRestFrame frame = BasisSpineAnatomy.BuildRestFrame(bone.position, child.position, bone.rotation, parent.rotation, Vector3.right);
                frame.Segment = segments[i - 1];
                frames[i] = frame;
            }
            return frames;
        }
        [Test]
        public void TrackedChest_IsClampedToTheHumanEnvelope_BetweenTrackedHipsAndTheHead()
        {
            var job = Job(chestTracked: true);
            job.spineAnatomicalRom = true;
            job.chainSpineRestFrames = BakeRestFrames();
            BasisEeriePlanner.Bind(ref job);
            BasisEeriePlanner.Frame(ref job, new BasisEerieFrameFacts { hipsTracked = true, chestTracked = true });
            try
            {
                Vector3 chestPos = bones[2].position, headPos = bones[5].position, benthHeadPos = chestPos + Quaternion.Euler(45f, 0f, 0f) * Vector3.up * (heights[5] - heights[2]);
                Solve(ref job, benthHeadPos, Quaternion.identity, chestPos, Quaternion.Euler(120f, 0f, 0f));
                float pitchKept = Quaternion.Angle(skeleton.Stream.GetRotation(job.handleChest), Quaternion.identity), pitchHeadErr = (skeleton.Stream.GetPosition(job.handleHead) - benthHeadPos).magnitude;
                Solve(ref job, headPos, Quaternion.identity, chestPos, Quaternion.Euler(0f, 90f, 0f));
                float yawKept = Quaternion.Angle(skeleton.Stream.GetRotation(job.handleChest), Quaternion.identity);
                Quaternion mildRot = Quaternion.Euler(20f, 0f, 0f);
                Vector3 mildHeadPos = chestPos + mildRot * Vector3.up * (heights[5] - heights[2]);
                Solve(ref job, mildHeadPos, Quaternion.identity, chestPos, mildRot);
                float mildKept = Quaternion.Angle(skeleton.Stream.GetRotation(job.handleChest), mildRot);
                float headRotErr = Quaternion.Angle(skeleton.Stream.GetRotation(job.handleHead), Quaternion.identity);
                TestContext.WriteLine($"120 deg pitch request -> {pitchKept:F1} deg kept (head err {pitchHeadErr * 1000f:F2} mm), 90 deg yaw request -> {yawKept:F1} deg kept, 20 deg pitch request kept within {mildKept:F3} deg, head rot err {headRotErr:F4}");
                Assert.Less(pitchKept, 90f, "a chest pitched 120 deg against tracked hips must be pulled back inside the human envelope between hips and head");
                Assert.Greater(pitchKept, 45f, "the clamp must be an envelope, not a reset to rest");
                Assert.Less(pitchHeadErr, 0.004f, "the head must still be reached above the clamped chest");
                Assert.Less(yawKept, 45f, "a chest yawed 90 deg against tracked hips must be pulled back toward the combined axial range");
                Assert.Less(mildKept, 0.01f, "a human-range chest rotation must pass through untouched");
                Assert.Less(headRotErr, 0.01f, "the head stays pinned to the gaze throughout");
            }
            finally
            {
                job.chainSpineRestFrames.Dispose();
            }
        }
        void SetUpAgain()
        {
            TearDown();
            SetUp();
        }
    }
}
