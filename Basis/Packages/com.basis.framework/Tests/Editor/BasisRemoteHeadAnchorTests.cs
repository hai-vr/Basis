using Basis.Scripts.Common;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.Tests.Remote
{
    /// <summary>
    /// Pins the contract between how a remote avatar's eye/mouth anchors are authored
    /// (BasisRemoteBoneDriver.AddRemotePlayer) and how they are placed each frame
    /// (BasisRemoteBoneJob.Execute → BasisRemoteBoneMath.HeadWorldFrame + ComposeHeadChain).
    ///
    /// The anchors have no bone of their own. They are authored as a single point per avatar —
    /// BasisAvatar.AvatarEyePosition / AvatarMouthPosition, a packed (height, forward) pair measured
    /// relative to the animator root — and the driver has to turn that into a world pose that tracks
    /// the networked head. The invariant, and the only thing that makes the anchors usable, is that
    /// they must move exactly as if they were parented to the head bone at T-pose. Every test here
    /// builds that parented ground truth out of real Transforms and requires the driver's arithmetic
    /// to reproduce it.
    ///
    /// Four separate defects used to break this, and each one is invisible on some perfectly ordinary
    /// class of avatar, which is why the symptom was per-avatar rather than universal:
    ///
    ///  1. BasisRemoteAvatarDriver passed the authored point through the translation-only
    ///     BasisHelpers.ConvertFromLocalSpace(local, origin) overload (BasisHelpers.cs:71), so the
    ///     avatar root's ROTATION was never applied — the authored forward offset pointed along
    ///     world +Z instead of out of the avatar's face. Zero error only when the remote happened to
    ///     be registered facing world-forward. Covered by AuthoredForwardFollowsAvatarFacing and
    ///     AnchorsTrackHeadWhenRootIsYawedAtRegistration.
    ///
    ///  2. AddRemotePlayer then subtracted the head's WORLD position from that, so the offset was a
    ///     world-axes delta rather than a root-relative one, and the job rotated it a second time.
    ///     Covered by the same two tests.
    ///
    ///  3. The job composed the head frame as mul(headWorld, tposeHeadFromRoot) instead of
    ///     mul(headWorld, conjugate(tposeHeadFromRoot)). That collapses to the same value only when
    ///     the head bone binds axis-aligned with the root, which is the common case for Unity
    ///     humanoid rigs and never the case for rigs imported with a rotated bind pose. Covered by
    ///     HeadWorldFrameCollapsesToRootRotationAtTpose and AnchorsTrackHeadWhenHeadBindIsRotated,
    ///     with the failure magnitude documented in DroppingTheConjugateMisplacesARotatedBind.
    ///
    ///  4. The authored point was then differenced against TposeFromRoot. Both are root-relative,
    ///     but they are not the same METRE: the SDK bakes the authored value through
    ///     BasisHelpers.ConvertToLocalSpace, which undoes the root's rotation and leaves its scale
    ///     in, whereas TposeFromRoot goes through root.worldToLocalMatrix and divides that scale
    ///     back out. TposeWorld is the frame that matches — BasisTransformMapping documents it as
    ///     root-relative "at world scale (no division by localScale)". Correct on any avatar whose
    ///     animator root sits at scale 1, wrong on every avatar that does not. Covered by
    ///     AnchorsTrackHeadWhenTheRootCarriesTheAvatarsScale, with the failure magnitude documented
    ///     in PairingAuthoredMetresWithTheScaleDividedFrameMisplacesAScaledRoot.
    ///
    /// The anchor rotation matters as much as the position: BasisLocalEyeDriver's gaze selector dots
    /// forward(rot_CenterEye) against the direction to the viewer to decide who is looking at whom
    /// (BasisLocalEyeDriver.cs:696), and the remote's voice AudioSource is parented to the mouth
    /// marker (BasisAudioReceiver.cs:522). Both want the avatar's facing, not the head bone's local
    /// axes — see AnchorRotationFacesTheAvatarForward.
    /// </summary>
    public sealed class BasisRemoteHeadAnchorTests
    {
        private const float Tolerance = 1e-4f;

        // A rig whose head does NOT bind axis-aligned with the root — the case defect 3 hides on.
        private static readonly Quaternion RotatedBind = Quaternion.Euler(24f, -37f, 15f);

        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
            {
                Object.DestroyImmediate(_root);
                _root = null;
            }
        }

        /// <summary>
        /// Builds root → head, poses them as a T-pose, and captures what the driver is handed at
        /// registration: the head's T-pose coords in the root-relative rendered-metre frame, plus the
        /// root scale those metres were measured at. Scale is baked into the root the way an avatar
        /// carries its own model scale.
        /// </summary>
        private void BuildRig(Quaternion rootRotation, Vector3 rootPosition, float rootScale, Quaternion headBind,
            Vector3 headLocalPosition, out Transform head, out BasisCalibratedCoords tposeHeadWorld, out float3 tposeRootScale)
        {
            _root = new GameObject("RemoteRoot");
            _root.transform.SetPositionAndRotation(rootPosition, rootRotation);
            _root.transform.localScale = Vector3.one * rootScale;

            GameObject headObject = new GameObject("Head");
            headObject.transform.SetParent(_root.transform, false);
            headObject.transform.SetLocalPositionAndRotation(headLocalPosition, headBind);
            head = headObject.transform;

            tposeHeadWorld = RecordTposeWorld(_root.transform, head);
            tposeRootScale = _root.transform.lossyScale;
        }

        /// <summary>
        /// The TposeWorld branch of BasisTransformMapping.ComputePoses: the root's rotation undone,
        /// its scale left in. Deliberately NOT worldToLocalMatrix — that is TposeFromRoot, the frame
        /// defect 4 was about.
        /// </summary>
        private static BasisCalibratedCoords RecordTposeWorld(Transform root, Transform bone)
        {
            root.GetPositionAndRotation(out Vector3 rootPosition, out Quaternion rootRotation);
            Quaternion invRoot = Quaternion.Inverse(rootRotation);
            return new BasisCalibratedCoords
            {
                position = invRoot * (bone.position - rootPosition),
                rotation = invRoot * bone.rotation,
            };
        }

        /// <summary>
        /// The authoring step from AddRemotePlayer — the production call, not a copy of it, so a
        /// change to either half of the contract has to come through these tests.
        /// </summary>
        private static float3 AuthorOffset(float3 authoredMetres, in BasisCalibratedCoords tposeHeadWorld, float3 tposeRootScale)
        {
            return BasisRemoteBoneMath.HeadAnchorOffset(authoredMetres, (float3)tposeHeadWorld.position, tposeRootScale);
        }

        /// <summary>
        /// The per-frame placement from BasisRemoteBoneJob.Execute, for one anchor.
        /// </summary>
        private static void PlaceAnchor(Transform head, in BasisCalibratedCoords tposeHeadWorld, float3 offset,
            float nowScale, out float3 position, out quaternion rotation)
        {
            head.GetPositionAndRotation(out Vector3 headWorldPosition, out Quaternion headWorldRotation);
            rotation = BasisRemoteBoneMath.HeadWorldFrame((quaternion)headWorldRotation, (quaternion)tposeHeadWorld.rotation);
            position = (float3)headWorldPosition + math.mul(rotation, offset * nowScale);
        }

        /// <summary>
        /// Ground truth: an object dropped at the authored point while the rig is in T-pose and then
        /// rigidly parented to the head. Wherever the head goes, this is where the anchor belongs.
        ///
        /// Placed with rotation only — BasisHelpers.ConvertFromLocalSpace(local, origin, rotation),
        /// the exact inverse of the ConvertToLocalSpace the SDK bakes the value with. Transform-
        /// Point would additionally apply the root's scale, which is precisely the extra factor
        /// defect 4 was, and would make these tests agree with the bug instead of catching it.
        /// </summary>
        private Transform AttachTruthToHead(Transform head, Vector3 authoredMetres, string name)
        {
            _root.transform.GetPositionAndRotation(out Vector3 rootPosition, out Quaternion rootRotation);
            GameObject truth = new GameObject(name);
            truth.transform.position = rootPosition + rootRotation * authoredMetres;
            truth.transform.SetParent(head, worldPositionStays: true);
            return truth.transform;
        }

        private static void AssertMatches(Transform truth, float3 actual, string what)
        {
            float error = math.distance((float3)truth.position, actual);
            Assert.That(error, Is.LessThan(Tolerance), $"{what} is {error * 100f:0.###} cm off the head-parented truth");
        }

        [Test]
        public void HeadWorldFrameCollapsesToRootRotationAtTpose()
        {
            // The frame's whole job is to carry root-relative offsets into world. At T-pose, before
            // any animation, that has to be exactly the root's own world rotation — for any bind.
            foreach (Quaternion bind in new[] { Quaternion.identity, RotatedBind, Quaternion.Euler(0f, 90f, 0f) })
            {
                TearDown();
                Quaternion rootRotation = Quaternion.Euler(0f, 118f, 0f);
                BuildRig(rootRotation, new Vector3(3f, 0f, -2f), 1f, bind, new Vector3(0f, 1.5f, 0f),
                    out Transform head, out BasisCalibratedCoords tposeHead, out _);

                quaternion frame = BasisRemoteBoneMath.HeadWorldFrame((quaternion)head.rotation, (quaternion)tposeHead.rotation);

                float degrees = Quaternion.Angle((Quaternion)frame, rootRotation);
                Assert.That(degrees, Is.LessThan(1e-3f), $"bind {bind.eulerAngles} left the T-pose frame {degrees:0.###}° off the root");
            }
        }

        [Test]
        public void AnchorsTrackHeadWhenBindIsAxisAligned()
        {
            // The case that always worked. Kept so a "fix" that only repairs rotated binds and
            // breaks the common rig cannot land.
            BuildRig(Quaternion.identity, Vector3.zero, 1f, Quaternion.identity, new Vector3(0f, 1.5f, 0f),
                out Transform head, out BasisCalibratedCoords tposeHead, out float3 rootScale);

            Vector3 authoredEye = new Vector3(0f, 1.6f, 0.08f);
            Vector3 authoredMouth = new Vector3(0f, 1.52f, 0.11f);
            float3 eyeOffset = AuthorOffset(authoredEye, tposeHead, rootScale);
            float3 mouthOffset = AuthorOffset(authoredMouth, tposeHead, rootScale);

            Transform eyeTruth = AttachTruthToHead(head, authoredEye, "EyeTruth");
            Transform mouthTruth = AttachTruthToHead(head, authoredMouth, "MouthTruth");

            head.rotation = Quaternion.Euler(-18f, 62f, 7f);

            PlaceAnchor(head, tposeHead, eyeOffset, 1f, out float3 eye, out _);
            PlaceAnchor(head, tposeHead, mouthOffset, 1f, out float3 mouth, out _);

            AssertMatches(eyeTruth, eye, "eye");
            AssertMatches(mouthTruth, mouth, "mouth");
        }

        [Test]
        public void AnchorsTrackHeadWhenHeadBindIsRotated()
        {
            // Defect 3. The head bone binds at an angle to the root, so mul(headWorld, tpose) and
            // mul(headWorld, conjugate(tpose)) diverge and the anchors swing off the face.
            BuildRig(Quaternion.identity, Vector3.zero, 1f, RotatedBind, new Vector3(0f, 1.5f, 0f),
                out Transform head, out BasisCalibratedCoords tposeHead, out float3 rootScale);

            Vector3 authoredEye = new Vector3(0f, 1.6f, 0.08f);
            Vector3 authoredMouth = new Vector3(0f, 1.52f, 0.11f);
            float3 eyeOffset = AuthorOffset(authoredEye, tposeHead, rootScale);
            float3 mouthOffset = AuthorOffset(authoredMouth, tposeHead, rootScale);

            Transform eyeTruth = AttachTruthToHead(head, authoredEye, "EyeTruth");
            Transform mouthTruth = AttachTruthToHead(head, authoredMouth, "MouthTruth");

            head.rotation = Quaternion.Euler(11f, -95f, -4f);

            PlaceAnchor(head, tposeHead, eyeOffset, 1f, out float3 eye, out _);
            PlaceAnchor(head, tposeHead, mouthOffset, 1f, out float3 mouth, out _);

            AssertMatches(eyeTruth, eye, "eye");
            AssertMatches(mouthTruth, mouth, "mouth");
        }

        [Test]
        public void AnchorsTrackHeadWhenRootIsYawedAtRegistration()
        {
            // Defects 1 and 2. A remote is registered wherever it happens to be standing, not facing
            // world-forward, and the authored point has to be read in the avatar's frame regardless.
            BuildRig(Quaternion.Euler(0f, 143f, 0f), new Vector3(-4f, 0.5f, 9f), 1f, RotatedBind,
                new Vector3(0.02f, 1.48f, -0.01f), out Transform head, out BasisCalibratedCoords tposeHead, out float3 rootScale);

            Vector3 authoredEye = new Vector3(0f, 1.6f, 0.08f);
            float3 eyeOffset = AuthorOffset(authoredEye, tposeHead, rootScale);
            Transform eyeTruth = AttachTruthToHead(head, authoredEye, "EyeTruth");

            head.rotation = Quaternion.Euler(-6f, 200f, 3f);

            PlaceAnchor(head, tposeHead, eyeOffset, 1f, out float3 eye, out _);

            AssertMatches(eyeTruth, eye, "eye");
        }

        [Test]
        public void AnchorsTrackHeadWhenTheRootCarriesTheAvatarsScale()
        {
            // Defect 4. The model is authored small and sized by its animator root, so the authored
            // metres and the head's T-pose metres are both post-scale while the offset the job wants
            // is pre-scale. Registration is the only place that knows the difference.
            const float modelScale = 2.35f;
            BuildRig(Quaternion.Euler(0f, -70f, 0f), new Vector3(1f, 0f, 1f), modelScale, RotatedBind,
                new Vector3(0f, 0.64f, 0f), out Transform head, out BasisCalibratedCoords tposeHead, out float3 rootScale);

            Vector3 authoredEye = new Vector3(0f, 1.6f, 0.08f);
            Vector3 authoredMouth = new Vector3(0f, 1.52f, 0.11f);
            float3 eyeOffset = AuthorOffset(authoredEye, tposeHead, rootScale);
            float3 mouthOffset = AuthorOffset(authoredMouth, tposeHead, rootScale);

            Transform eyeTruth = AttachTruthToHead(head, authoredEye, "EyeTruth");
            Transform mouthTruth = AttachTruthToHead(head, authoredMouth, "MouthTruth");

            head.rotation = Quaternion.Euler(25f, 14f, -9f);

            PlaceAnchor(head, tposeHead, eyeOffset, modelScale, out float3 eye, out _);
            PlaceAnchor(head, tposeHead, mouthOffset, modelScale, out float3 mouth, out _);

            AssertMatches(eyeTruth, eye, "scaled eye");
            AssertMatches(mouthTruth, mouth, "scaled mouth");
        }

        [Test]
        public void AnchorsFollowARuntimeResizeAwayFromTheModelScale()
        {
            // The offset is stored in model units precisely so a networked resize is a pure multiply.
            // Register at the model's own scale, then render at another and require the anchor to
            // stay the same fraction of the way up the avatar.
            const float modelScale = 2f;
            const float liveScale = 3f;
            BuildRig(Quaternion.identity, Vector3.zero, modelScale, Quaternion.identity,
                new Vector3(0f, 0.75f, 0f), out Transform head, out BasisCalibratedCoords tposeHead, out float3 rootScale);

            Vector3 authoredEye = new Vector3(0f, 1.6f, 0.08f);
            float3 eyeOffset = AuthorOffset(authoredEye, tposeHead, rootScale);

            // Head-to-eye at model scale, then the same leg at the live scale.
            Vector3 headAtModelScale = head.position;
            PlaceAnchor(head, tposeHead, eyeOffset, modelScale, out float3 atModel, out _);
            PlaceAnchor(head, tposeHead, eyeOffset, liveScale, out float3 atLive, out _);

            float legModel = math.distance((float3)headAtModelScale, atModel);
            float legLive = math.distance((float3)headAtModelScale, atLive);
            Assert.That(legLive / legModel, Is.EqualTo(liveScale / modelScale).Within(1e-3f),
                "the head-to-anchor leg did not scale linearly with the avatar");
        }

        [Test]
        public void AuthoredForwardFollowsAvatarFacing()
        {
            // The authored Vector2's second component is a FORWARD offset — eyes and mouth sit in
            // front of the head's centre. Registered on an avatar facing world -Z, that offset has
            // to come out pointing at world -Z, not +Z. This is the defect-1 signature on its own:
            // with the translation-only conversion the anchor landed behind the head instead.
            BuildRig(Quaternion.Euler(0f, 180f, 0f), Vector3.zero, 1f, Quaternion.identity,
                new Vector3(0f, 1.5f, 0f), out Transform head, out BasisCalibratedCoords tposeHead, out float3 rootScale);

            const float forward = 0.1f;
            Vector3 authoredEye = new Vector3(0f, 1.5f, forward);
            float3 eyeOffset = AuthorOffset(authoredEye, tposeHead, rootScale);

            PlaceAnchor(head, tposeHead, eyeOffset, 1f, out float3 eye, out _);

            // Head is at the same height, so the whole offset is the forward leg.
            Assert.That(eye.z, Is.EqualTo(-forward).Within(Tolerance), "authored forward did not follow the avatar's facing");
        }

        [Test]
        public void AnchorRotationFacesTheAvatarForward()
        {
            // The anchors are handed the head-carried root frame as their rotation, so forward() is
            // the avatar's facing. The gaze selector and the voice AudioSource both depend on this;
            // handing over the head bone's own rotation would give the bind axes instead.
            BuildRig(Quaternion.identity, Vector3.zero, 1f, RotatedBind, new Vector3(0f, 1.5f, 0f),
                out Transform head, out BasisCalibratedCoords tposeHead, out _);

            // Turn the whole avatar 90° right by turning the head the same way the network would.
            Quaternion turn = Quaternion.Euler(0f, 90f, 0f);
            head.rotation = turn * head.rotation;

            PlaceAnchor(head, tposeHead, float3.zero, 1f, out _, out quaternion rotation);

            float3 facing = math.mul(rotation, math.forward());
            float degrees = Vector3.Angle((Vector3)facing, turn * Vector3.forward);
            Assert.That(degrees, Is.LessThan(1e-3f), $"anchor forward is {degrees:0.###}° off the avatar's facing");
        }

        [Test]
        public void DroppingTheConjugateMisplacesARotatedBind()
        {
            // Regression witness for defect 3. Documents what the old composition actually did, so
            // the fix cannot be quietly reverted and so the size of the error is on record: the
            // anchors land far enough out to sit off the face entirely on a rotated bind.
            BuildRig(Quaternion.identity, Vector3.zero, 1f, RotatedBind, new Vector3(0f, 1.5f, 0f),
                out Transform head, out BasisCalibratedCoords tposeHead, out float3 rootScale);

            Vector3 authoredEye = new Vector3(0f, 1.6f, 0.08f);
            float3 eyeOffset = AuthorOffset(authoredEye, tposeHead, rootScale);
            Transform eyeTruth = AttachTruthToHead(head, authoredEye, "EyeTruth");

            head.rotation = Quaternion.Euler(11f, -95f, -4f);

            // The old line: mul(headWorld, tposeHeadFromRoot), no conjugate.
            quaternion stale = math.mul((quaternion)head.rotation, (quaternion)tposeHead.rotation);
            float3 staleEye = (float3)head.position + math.mul(stale, eyeOffset);

            float error = math.distance((float3)eyeTruth.position, staleEye);
            Assert.That(error, Is.GreaterThan(0.02f), "the conjugate-less composition should be visibly wrong on a rotated bind");

            PlaceAnchor(head, tposeHead, eyeOffset, 1f, out float3 fixedEye, out _);
            AssertMatches(eyeTruth, fixedEye, "eye");
        }

        [Test]
        public void PairingAuthoredMetresWithTheScaleDividedFrameMisplacesAScaledRoot()
        {
            // Regression witness for defect 4, and the reason the bug looked avatar-specific: run the
            // same rig at scale 1 and at scale 2.35 through the old pairing. At 1 the two frames are
            // the same numbers and the error is nil; at 2.35 the anchor is tens of centimetres out.
            foreach (float modelScale in new[] { 1f, 2.35f })
            {
                TearDown();
                BuildRig(Quaternion.identity, Vector3.zero, modelScale, Quaternion.identity,
                    new Vector3(0f, 1.5f / modelScale, 0f), out Transform head,
                    out BasisCalibratedCoords tposeHead, out float3 rootScale);

                Vector3 authoredEye = new Vector3(0f, 1.6f, 0.08f);
                Transform eyeTruth = AttachTruthToHead(head, authoredEye, "EyeTruth");

                // The old pairing: authored rendered metres differenced against the scale-DIVIDED
                // head position, with no conversion between the two frames.
                float3 tposeHeadFromRoot = (float3)tposeHead.position / modelScale;
                float3 staleOffset = (float3)authoredEye - tposeHeadFromRoot;
                PlaceAnchor(head, tposeHead, staleOffset, modelScale, out float3 staleEye, out _);
                float staleError = math.distance((float3)eyeTruth.position, staleEye);

                float3 fixedOffset = AuthorOffset(authoredEye, tposeHead, rootScale);
                PlaceAnchor(head, tposeHead, fixedOffset, modelScale, out float3 fixedEye, out _);

                if (modelScale == 1f)
                {
                    Assert.That(staleError, Is.LessThan(Tolerance), "at root scale 1 the two frames coincide, so the old pairing must look fine");
                }
                else
                {
                    Assert.That(staleError, Is.GreaterThan(0.1f), $"at root scale {modelScale} the old pairing should be badly wrong");
                }
                AssertMatches(eyeTruth, fixedEye, $"eye at root scale {modelScale}");
            }
        }
    }
}
