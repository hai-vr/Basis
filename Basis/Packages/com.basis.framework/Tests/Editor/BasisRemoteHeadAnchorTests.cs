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
    /// The anchors have no bone of their own. They are authored as a single root-local point per
    /// avatar — BasisAvatar.AvatarEyePosition / AvatarMouthPosition, a packed (height, forward) pair
    /// in model metres above the animator root — and the driver has to turn that into a world pose
    /// that tracks the networked head. The invariant, and the only thing that makes the anchors
    /// usable, is that they must move exactly as if they were parented to the head bone at T-pose.
    /// Every test here builds that parented ground truth out of real Transforms and requires the
    /// driver's arithmetic to reproduce it.
    ///
    /// Three separate defects used to break this, and each one is invisible on a well-behaved rig,
    /// which is why the symptom was intermittent rather than universal:
    ///
    ///  1. BasisRemoteAvatarDriver passed the authored point through the translation-only
    ///     BasisHelpers.ConvertFromLocalSpace(local, origin) overload (BasisHelpers.cs:71), so the
    ///     avatar root's ROTATION was never applied — the authored forward offset pointed along
    ///     world +Z instead of out of the avatar's face. Zero error only when the remote happened to
    ///     be registered facing world-forward. Covered by AuthoredForwardFollowsAvatarFacing and
    ///     AnchorsTrackHeadWhenRootIsYawedAtRegistration.
    ///
    ///  2. AddRemotePlayer then subtracted the head's WORLD position from that, so the offset was a
    ///     world-axes delta rather than a root-local one, and the job rotated it a second time.
    ///     Covered by the same two tests.
    ///
    ///  3. The job composed the head frame as mul(headWorld, tposeHeadFromRoot) instead of
    ///     mul(headWorld, conjugate(tposeHeadFromRoot)). That collapses to the same value only when
    ///     the head bone binds axis-aligned with the root, which is the common case for Unity
    ///     humanoid rigs and never the case for rigs imported with a rotated bind pose. Covered by
    ///     HeadWorldFrameCollapsesToRootRotationAtTpose and AnchorsTrackHeadWhenHeadBindIsRotated,
    ///     with the failure magnitude documented in DroppingTheConjugateMisplacesARotatedBind.
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
        /// Builds root → head, poses them as a T-pose, and captures the two values the driver is
        /// handed at registration: the head's root-local T-pose coords (BasisTransformMapping.
        /// TposeFromRoot, recorded with root.worldToLocalMatrix) and nothing else. Scale is baked
        /// into the root the way a scaled remote avatar carries it.
        /// </summary>
        private void BuildRig(Quaternion rootRotation, Vector3 rootPosition, float rootScale, Quaternion headBind,
            Vector3 headLocalPosition, out Transform head, out BasisCalibratedCoords tposeHead)
        {
            _root = new GameObject("RemoteRoot");
            _root.transform.SetPositionAndRotation(rootPosition, rootRotation);
            _root.transform.localScale = Vector3.one * rootScale;

            GameObject headObject = new GameObject("Head");
            headObject.transform.SetParent(_root.transform, false);
            headObject.transform.SetLocalPositionAndRotation(headLocalPosition, headBind);
            head = headObject.transform;

            tposeHead = RecordFromRoot(_root.transform, head);
        }

        /// <summary>
        /// The two lines of BasisTransformMapping.ComputePoses that produce TposeFromRoot. Position
        /// goes through worldToLocalMatrix, so it comes back scale-normalised — model metres, the
        /// same units the authored Vector2 is in. Rotation is root-relative.
        /// </summary>
        private static BasisCalibratedCoords RecordFromRoot(Transform root, Transform bone)
        {
            root.GetPositionAndRotation(out _, out Quaternion rootRotation);
            return new BasisCalibratedCoords
            {
                position = root.worldToLocalMatrix.MultiplyPoint3x4(bone.position),
                rotation = Quaternion.Inverse(rootRotation) * bone.rotation,
            };
        }

        /// <summary>
        /// The authoring step from AddRemotePlayer — the production call, not a copy of it, so a
        /// change to either half of the contract has to come through these tests.
        /// </summary>
        private static float3 AuthorOffset(float3 authoredRootLocal, in BasisCalibratedCoords tposeHead)
        {
            return BasisRemoteBoneMath.HeadAnchorOffset(authoredRootLocal, (float3)tposeHead.position);
        }

        /// <summary>
        /// The per-frame placement from BasisRemoteBoneJob.Execute, for one anchor.
        /// </summary>
        private static void PlaceAnchor(Transform head, in BasisCalibratedCoords tposeHead, float3 offset,
            float nowScale, out float3 position, out quaternion rotation)
        {
            head.GetPositionAndRotation(out Vector3 headWorldPosition, out Quaternion headWorldRotation);
            rotation = BasisRemoteBoneMath.HeadWorldFrame((quaternion)headWorldRotation, (quaternion)tposeHead.rotation);
            position = (float3)headWorldPosition + math.mul(rotation, offset * nowScale);
        }

        /// <summary>
        /// Ground truth: an object dropped at the authored point while the rig is in T-pose and then
        /// rigidly parented to the head. Wherever the head goes, this is where the anchor belongs.
        /// </summary>
        private Transform AttachTruthToHead(Transform head, Vector3 authoredRootLocal, string name)
        {
            GameObject truth = new GameObject(name);
            truth.transform.position = _root.transform.TransformPoint(authoredRootLocal);
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
            // The frame's whole job is to carry root-local offsets into world. At T-pose, before any
            // animation, that has to be exactly the root's own world rotation — for any bind.
            foreach (Quaternion bind in new[] { Quaternion.identity, RotatedBind, Quaternion.Euler(0f, 90f, 0f) })
            {
                TearDown();
                Quaternion rootRotation = Quaternion.Euler(0f, 118f, 0f);
                BuildRig(rootRotation, new Vector3(3f, 0f, -2f), 1f, bind, new Vector3(0f, 1.5f, 0f),
                    out Transform head, out BasisCalibratedCoords tposeHead);

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
                out Transform head, out BasisCalibratedCoords tposeHead);

            Vector3 authoredEye = new Vector3(0f, 1.6f, 0.08f);
            Vector3 authoredMouth = new Vector3(0f, 1.52f, 0.11f);
            float3 eyeOffset = AuthorOffset(authoredEye, tposeHead);
            float3 mouthOffset = AuthorOffset(authoredMouth, tposeHead);

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
                out Transform head, out BasisCalibratedCoords tposeHead);

            Vector3 authoredEye = new Vector3(0f, 1.6f, 0.08f);
            Vector3 authoredMouth = new Vector3(0f, 1.52f, 0.11f);
            float3 eyeOffset = AuthorOffset(authoredEye, tposeHead);
            float3 mouthOffset = AuthorOffset(authoredMouth, tposeHead);

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
                new Vector3(0.02f, 1.48f, -0.01f), out Transform head, out BasisCalibratedCoords tposeHead);

            Vector3 authoredEye = new Vector3(0f, 1.6f, 0.08f);
            float3 eyeOffset = AuthorOffset(authoredEye, tposeHead);
            Transform eyeTruth = AttachTruthToHead(head, authoredEye, "EyeTruth");

            head.rotation = Quaternion.Euler(-6f, 200f, 3f);

            PlaceAnchor(head, tposeHead, eyeOffset, 1f, out float3 eye, out _);

            AssertMatches(eyeTruth, eye, "eye");
        }

        [Test]
        public void AnchorsTrackHeadWhenAvatarIsScaled()
        {
            // Offsets are authored in model metres and multiplied by the root's live world scale in
            // the job, so a scaled avatar has to land on the same head-parented truth.
            const float scale = 2.35f;
            BuildRig(Quaternion.Euler(0f, -70f, 0f), new Vector3(1f, 0f, 1f), scale, RotatedBind,
                new Vector3(0f, 1.5f, 0f), out Transform head, out BasisCalibratedCoords tposeHead);

            Vector3 authoredEye = new Vector3(0f, 1.6f, 0.08f);
            float3 eyeOffset = AuthorOffset(authoredEye, tposeHead);
            Transform eyeTruth = AttachTruthToHead(head, authoredEye, "EyeTruth");

            head.rotation = Quaternion.Euler(25f, 14f, -9f);

            PlaceAnchor(head, tposeHead, eyeOffset, scale, out float3 eye, out _);

            AssertMatches(eyeTruth, eye, "scaled eye");
        }

        [Test]
        public void AuthoredForwardFollowsAvatarFacing()
        {
            // The authored Vector2's second component is a FORWARD offset — eyes and mouth sit in
            // front of the head's centre. Registered on an avatar facing world -Z, that offset has
            // to come out pointing at world -Z, not +Z. This is the defect-1 signature on its own:
            // with the translation-only conversion the anchor landed behind the head instead.
            BuildRig(Quaternion.Euler(0f, 180f, 0f), Vector3.zero, 1f, Quaternion.identity,
                new Vector3(0f, 1.5f, 0f), out Transform head, out BasisCalibratedCoords tposeHead);

            const float forward = 0.1f;
            Vector3 authoredEye = new Vector3(0f, 1.5f, forward);
            float3 eyeOffset = AuthorOffset(authoredEye, tposeHead);

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
                out Transform head, out BasisCalibratedCoords tposeHead);

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
            // Regression witness. Documents what the old composition actually did, so the fix cannot
            // be quietly reverted and so the size of the error is on record: the anchors land far
            // enough out to sit off the face entirely on a rig with a rotated bind.
            BuildRig(Quaternion.identity, Vector3.zero, 1f, RotatedBind, new Vector3(0f, 1.5f, 0f),
                out Transform head, out BasisCalibratedCoords tposeHead);

            Vector3 authoredEye = new Vector3(0f, 1.6f, 0.08f);
            float3 eyeOffset = AuthorOffset(authoredEye, tposeHead);
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
    }
}
