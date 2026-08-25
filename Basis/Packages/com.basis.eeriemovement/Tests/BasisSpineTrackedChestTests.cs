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
                spineTolerance = 0.001f,
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
            Vector3 chestPos = bones[2].position + new Vector3(0.01f, -0.005f, 0.02f), headPos = bones[5].position + new Vector3(0.03f, -0.015f, 0.05f);
            Solve(ref job, headPos, gaze, chestPos, chestRot);

            float chestRotErr = Quaternion.Angle(skeleton.Stream.GetRotation(job.handleChest), chestRot);
            float headPosErr = (skeleton.Stream.GetPosition(job.handleHead) - headPos).magnitude, headRotErr = Quaternion.Angle(skeleton.Stream.GetRotation(job.handleHead), gaze);
            float chestPosErr = (skeleton.Stream.GetPosition(job.handleChest) - chestPos).magnitude;
            TestContext.WriteLine($"chest rot err {chestRotErr:F4} deg, chest pos err {chestPosErr * 1000f:F2} mm, head pos err {headPosErr * 1000f:F2} mm, head rot err {headRotErr:F4} deg");
            Assert.Less(chestRotErr, 0.01f, "a tracked chest must keep its tracker rotation through the head solve");
            Assert.Less(headPosErr, 0.002f, "the head must still be reached by the joints above the tracked chest");
            Assert.Less(headRotErr, 0.01f, "the head must stay pinned to the gaze");
            Assert.Less(chestPosErr, 0.02f, "the lumbar must carry the chest toward the tracked position");
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
        public void TrackedChest_StretchesTheSpine_BeforeTheHeadDetaches()
        {
            float upperReach = heights[5] - heights[2];
            Vector3 chestPos = bones[2].position, headPos = chestPos + Vector3.up * (upperReach * 1.02f);

            var stretch = Job(chestTracked: true);
            Solve(ref stretch, headPos, Quaternion.identity, chestPos, Quaternion.identity);
            float errStretch = (skeleton.Stream.GetPosition(stretch.handleHead) - headPos).magnitude;

            SetUpAgain();
            var rigid = Job(chestTracked: true);
            rigid.spineStretchMax = 0f;
            Solve(ref rigid, headPos, Quaternion.identity, chestPos, Quaternion.identity);
            float errRigid = (skeleton.Stream.GetPosition(rigid.handleHead) - headPos).magnitude;

            TestContext.WriteLine($"head err with stretch {errStretch * 1000f:F2} mm, without {errRigid * 1000f:F2} mm");
            Assert.Less(errStretch, 0.0015f, "a 2% over-reach must be absorbed by the spine stretch");
            Assert.Greater(errRigid, 0.005f, "with stretch off the head must be projected onto the chain's reach");
        }
        void SetUpAgain()
        {
            TearDown();
            SetUp();
        }
    }
}
