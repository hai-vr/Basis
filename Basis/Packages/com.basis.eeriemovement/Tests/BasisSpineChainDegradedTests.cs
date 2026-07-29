using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using Basis.IK;

namespace Basis.Tests.IK
{
    /// <summary>
    /// "On SOME avatars the head jitters around forward and does not follow the HMD" regression tests.
    ///
    /// Two rig-dependent failure modes, both invisible on a full canonical rig:
    ///
    /// 1. OPTIONAL BONES. Neck, upperChest and chest are optional humanoid bones. The chain used to be
    ///    built at a fixed length with unbound handles standing in for absent bones, and
    ///    SolveSequentialSpineIK returns when ANY chain handle is invalid -- so on a neckless or chestless
    ///    avatar the entire spine solve, including the head pin welded to the HMD, silently switched off.
    ///    The head then kept the animator's pose: it wobbled with the idle/locomotion animation near
    ///    body-forward and ignored the HMD. The chain now carries only the bones the avatar has, and the
    ///    chest is identified by chainChestIdx instead of index-from-length arithmetic.
    ///
    /// 2. ROLLED HIPS BINDS. The CCD's twist axis was the raw hips BONE's up, which is a rig convention --
    ///    a Blender-bound rig (hips rolled -90 about X) put that axis along body-forward, so the
    ///    anti-corkscrew twist relax stripped bend as twist and kept twist as bend on exactly those rigs.
    ///    The axis is now bind-cancelled (hipsRot * inv(offsetRotationHips)), the same cancellation
    ///    DistributeSpineBend, the crouch offset and the arm-swing follow already ship. A zero (unset)
    ///    bind falls back to the raw bone frame, which keeps every hand-built job bit-identical.
    /// </summary>
    public class BasisSpineChainDegradedTests
    {
        GameObject _root;
        BasisPoseSkeleton _skeleton;
        NativeArray<BasisBoneHandle> _chain;

        [TearDown]
        public void TearDown()
        {
            DisposeRig();
        }

        void DisposeRig()
        {
            if (_chain.IsCreated)
            {
                _chain.Dispose();
            }
            _skeleton?.Dispose();
            _skeleton = null;
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
                _root = null;
            }
        }

        Transform[] BuildBones(string[] names, float[] heights, Quaternion[] worldRots = null)
        {
            DisposeRig();
            _root = new GameObject("DegradedChainRig");
            var bones = new Transform[names.Length];
            Transform parent = _root.transform;
            for (int i = 0; i < names.Length; i++)
            {
                var go = new GameObject(names[i]);
                go.transform.SetPositionAndRotation(new Vector3(0f, heights[i], 0f),
                    worldRots != null ? worldRots[i] : Quaternion.identity);
                go.transform.SetParent(parent, true);
                bones[i] = go.transform;
                parent = go.transform;
            }
            _skeleton = new BasisPoseSkeleton();
            _skeleton.Build(bones[0], bones);
            _skeleton.GatherNow();
            return bones;
        }

        NativeArray<BasisBoneHandle> BindChainTipFirst(Transform[] chainRootFirst)
        {
            var chain = new NativeArray<BasisBoneHandle>(chainRootFirst.Length, Allocator.Persistent);
            for (int i = 0; i < chainRootFirst.Length; i++)
            {
                chain[i] = chainRootFirst[chainRootFirst.Length - 1 - i] != null
                    ? _skeleton.Bind(chainRootFirst[chainRootFirst.Length - 1 - i])
                    : BasisBoneHandle.Unbound;
            }
            return chain;
        }

        BasisEerieMovement CcdJob()
        {
            return new BasisEerieMovement
            {
                spineMaxIterations = 20,
                spineTolerance = 0.001f,
                spineCCDRelax = 1.0f,
                spineTwistKeep = 0.25f,
                spineNeckTwistKeep = 0.9f,
                neckMaxConeDeg = 45f,
                maxChestDeltaDeg = 30f,
                thoracicBendStiffen = 0.3f,
                spineTautBandFrac = 0.015f,
                chestIkWeight = 0.5f,
                chestIkIterations = 8,
                chestIkHeadRestoreSweeps = 2,
                chestPullMaxDist = 0.5f,
                targetOffsetHead = Quaternion.identity,
                offsetRotationHips = Quaternion.identity,
                playerUp = Vector3.up,
                chestIkTarget = false,
                spineAnatomicalRom = false,
            };
        }

        // ------------------------------------------------ optional bones must not switch the head pin off

        [Test]
        public void NecklessChain_HeadIsStillPinnedToTheGaze()
        {
            var bones = BuildBones(
                new[] { "Hips", "Spine", "Chest", "Head" },
                new[] { 0.95f, 1.06f, 1.21f, 1.57f });
            _chain = BindChainTipFirst(bones);

            var job = CcdJob();
            job.chainHeadToSpine = _chain;
            job.chainChestIdx = 1;
            job.handleHips = _skeleton.Bind(bones[0]);
            job.handleSpine = _skeleton.Bind(bones[1]);
            job.handleChest = _skeleton.Bind(bones[2]);
            job.handleHead = _skeleton.Bind(bones[3]);
            job.enabledSpineIK = true;
            job.ikLockMode = BasisIKLockMode.LockHead;
            job.hasHipsTracker = true;
            job.minHeadSpineHeight = 0.62f;
            job.tposeLengthNeckToHips = new Vector3(0f, 0.5f, 0f);
            job.targetOffsetChest = Quaternion.identity;
            job.targetRotationHips = Quaternion.identity;
            job.targetRotationChest = Quaternion.identity;
            job.targetPositionHips = bones[0].position;

            Quaternion gaze = Quaternion.Euler(15f, 30f, 0f);
            Vector3 headTarget = bones[3].position - new Vector3(0f, 0.002f, 0f);
            job.targetPositionHead = headTarget;
            job.targetRotationHead = gaze;

            job.SolveSpine(_skeleton.Stream);

            float rotErr = Quaternion.Angle(_chain[0].GetRotation(_skeleton.Stream), gaze);
            float posErr = (_chain[0].GetPosition(_skeleton.Stream) - headTarget).magnitude;
            TestContext.WriteLine($"head rot err {rotErr:F4} deg, pos err {posErr * 1000f:F2} mm");
            Assert.Less(rotErr, 0.1f, "a neckless avatar's head must still be pinned to the HMD gaze");
            Assert.Less(posErr, 0.01f, "a neckless avatar's head must still reach its target");
        }

        [Test]
        public void MinimalChain_HeadIsStillPinnedToTheGaze()
        {
            var bones = BuildBones(
                new[] { "Hips", "Spine", "Head" },
                new[] { 0.95f, 1.21f, 1.57f });
            _chain = BindChainTipFirst(bones);

            var job = CcdJob();
            job.chainHeadToSpine = _chain;
            job.chainChestIdx = -1;
            job.handleHips = _skeleton.Bind(bones[0]);
            job.handleSpine = _skeleton.Bind(bones[1]);
            job.handleHead = _skeleton.Bind(bones[2]);
            job.enabledSpineIK = true;
            job.ikLockMode = BasisIKLockMode.LockHead;
            job.hasHipsTracker = true;
            job.minHeadSpineHeight = 0.62f;
            job.tposeLengthNeckToHips = new Vector3(0f, 0.5f, 0f);
            job.targetOffsetChest = Quaternion.identity;
            job.targetRotationHips = Quaternion.identity;
            job.targetRotationChest = Quaternion.identity;
            job.targetPositionHips = bones[0].position;

            Quaternion gaze = Quaternion.Euler(-10f, -40f, 0f);
            Vector3 headTarget = bones[2].position - new Vector3(0f, 0.002f, 0f);
            job.targetPositionHead = headTarget;
            job.targetRotationHead = gaze;

            job.SolveSpine(_skeleton.Stream);

            float rotErr = Quaternion.Angle(_chain[0].GetRotation(_skeleton.Stream), gaze);
            float posErr = (_chain[0].GetPosition(_skeleton.Stream) - headTarget).magnitude;
            TestContext.WriteLine($"head rot err {rotErr:F4} deg, pos err {posErr * 1000f:F2} mm");
            Assert.Less(rotErr, 0.1f, "a hips/spine/head-only avatar's head must still be pinned to the HMD gaze");
            Assert.Less(posErr, 0.01f, "a hips/spine/head-only avatar's head must still reach its target");
        }

        [Test]
        public void ChestlessChain_ChestTargetDoesNotPullTheNeck()
        {
            var bones = BuildBones(
                new[] { "Hips", "Spine", "Neck", "Head" },
                new[] { 0.95f, 1.21f, 1.45f, 1.57f });
            _chain = BindChainTipFirst(bones);
            int neckIdx = _skeleton.Bind(bones[2]).Index;

            var job = CcdJob();
            job.chainHeadToSpine = _chain;
            job.chainChestIdx = -1;
            job.handleHips = _skeleton.Bind(bones[0]);
            job.handleSpine = _skeleton.Bind(bones[1]);
            job.handleNeck = _skeleton.Bind(bones[2]);
            job.handleHead = _skeleton.Bind(bones[3]);

            Vector3 headTarget = bones[3].position - new Vector3(0f, 0.002f, 0f);

            job.chestIkTarget = false;
            _skeleton.GatherNow();
            job.SolveSequentialSpineIK(_skeleton.Stream, headTarget, Quaternion.identity);
            Quaternion neckWithoutChestIk = _skeleton.Stream.GetWorldRotation(neckIdx);

            job.chestIkTarget = true;
            job.targetPositionChestRaw = bones[2].position + new Vector3(0f, -0.1f, 0.3f);
            _skeleton.GatherNow();
            job.SolveSequentialSpineIK(_skeleton.Stream, headTarget, Quaternion.identity);
            Quaternion neckWithChestIk = _skeleton.Stream.GetWorldRotation(neckIdx);

            Assert.Less(Quaternion.Angle(neckWithoutChestIk, neckWithChestIk), 1e-4f,
                "with no chest bone the chest IK target must be inert, not re-aimed at the neck");
        }

        [Test]
        public void UnboundHandleInsideTheChain_LeavesThePoseUntouched()
        {
            var bones = BuildBones(
                new[] { "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head" },
                new[] { 0.95f, 1.06f, 1.21f, 1.33f, 1.45f, 1.57f });
            _chain = BindChainTipFirst(new[] { bones[0], bones[1], bones[2], bones[3], null, bones[5] });

            var job = CcdJob();
            job.chainHeadToSpine = _chain;

            Quaternion gaze = Quaternion.Euler(0f, 25f, 0f);
            job.SolveSequentialSpineIK(_skeleton.Stream, bones[5].position, gaze);

            Assert.Less(Quaternion.Angle(_skeleton.Stream.GetWorldRotation(_skeleton.Bind(bones[5]).Index), Quaternion.identity), 1e-4f,
                "a chain with a genuinely unbound handle must stay a no-op");
        }

        // -------------------------------------------- the CCD twist axis must not be a rig convention

        static readonly string[] FullNames = { "Hips", "Spine", "Chest", "UpperChest", "Neck", "Head" };
        static readonly float[] FullHeights = { 0.95f, 1.06f, 1.21f, 1.33f, 1.45f, 1.57f };

        (Quaternion neck, Quaternion chest, Vector3 tip, Quaternion head) SolveOnBind(Quaternion hipsWorldRest, Quaternion hipsBind)
        {
            var rots = new Quaternion[6];
            for (int i = 0; i < 6; i++)
            {
                rots[i] = i == 0 ? hipsWorldRest : Quaternion.identity;
            }
            var bones = BuildBones(FullNames, FullHeights, rots);
            _chain = BindChainTipFirst(bones);

            var job = CcdJob();
            job.chainHeadToSpine = _chain;
            job.handleHips = _skeleton.Bind(bones[0]);
            job.offsetRotationHips = hipsBind;

            Vector3 target = bones[5].position + new Vector3(0.10f, -0.06f, 0.04f);
            job.SolveSequentialSpineIK(_skeleton.Stream, target, Quaternion.identity);

            return (
                _skeleton.Stream.GetWorldRotation(_skeleton.Bind(bones[4]).Index),
                _skeleton.Stream.GetWorldRotation(_skeleton.Bind(bones[2]).Index),
                _chain[0].GetPosition(_skeleton.Stream),
                _chain[0].GetRotation(_skeleton.Stream));
        }

        [Test]
        public void RolledHipsBind_SolvesIdenticallyToTheCanonicalRig()
        {
            var canonical = SolveOnBind(Quaternion.identity, Quaternion.identity);
            Quaternion blenderRoll = Quaternion.Euler(-90f, 0f, 0f);
            var rolled = SolveOnBind(blenderRoll, blenderRoll);

            float neckDelta = Quaternion.Angle(canonical.neck, rolled.neck);
            float chestDelta = Quaternion.Angle(canonical.chest, rolled.chest);
            float tipDelta = (canonical.tip - rolled.tip).magnitude;
            TestContext.WriteLine($"neck {neckDelta:F4} deg, chest {chestDelta:F4} deg, tip {tipDelta * 1000f:F3} mm");

            Assert.Less(neckDelta, 0.1f, "the solved neck must not depend on the hips bone's bind convention");
            Assert.Less(chestDelta, 0.1f, "the solved chest must not depend on the hips bone's bind convention");
            Assert.Less(tipDelta, 0.001f, "the solved head position must not depend on the hips bone's bind convention");
        }

        [Test]
        public void UnsetHipsBind_FallsBackToTheRawBoneFrame()
        {
            var withIdentity = SolveOnBind(Quaternion.identity, Quaternion.identity);
            var withDefault = SolveOnBind(Quaternion.identity, default);

            Assert.Less(Quaternion.Angle(withIdentity.neck, withDefault.neck), 1e-3f,
                "a zero (unset) hips bind must solve exactly like the identity bind");
            Assert.Less(Quaternion.Angle(withIdentity.chest, withDefault.chest), 1e-3f);
        }
    }
}
