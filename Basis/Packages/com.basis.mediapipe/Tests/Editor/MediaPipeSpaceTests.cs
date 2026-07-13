using Basis.MediaPipe;
using NUnit.Framework;
using UnityEngine;

namespace Basis.MediaPipe.Tests
{
    /// <summary>
    /// The coordinate normalization and the hand-rotation retarget. Between them these cover every sign that
    /// used to be hand-tuned: the selfie mirror, MediaPipe's y-down axis, its depth direction, and the palm
    /// frame that was previously hung off the hips.
    /// </summary>
    public class MediaPipeSpaceTests
    {
        private static Vector3[] Body()
        {
            Vector3[] pose = new Vector3[MediaPipeSpace.PoseCount];
            pose[MediaPipeSpace.Nose] = new Vector3(0f, 1.58f, 0.10f);
            pose[MediaPipeSpace.LeftEar] = new Vector3(-0.08f, 1.62f, -0.02f);
            pose[MediaPipeSpace.RightEar] = new Vector3(0.08f, 1.62f, -0.02f);
            pose[MediaPipeSpace.LeftShoulder] = new Vector3(-0.18f, 1.40f, 0f);
            pose[MediaPipeSpace.RightShoulder] = new Vector3(0.18f, 1.40f, 0f);
            pose[MediaPipeSpace.LeftHip] = new Vector3(-0.12f, 1.00f, 0f);
            pose[MediaPipeSpace.RightHip] = new Vector3(0.12f, 1.00f, 0f);
            pose[MediaPipeSpace.RightElbow] = new Vector3(0.38f, 1.26f, 0.12f);
            pose[MediaPipeSpace.RightWrist] = new Vector3(0.24f, 1.50f, 0.16f);
            return pose;
        }

        private static Vector3[] DepthReflected()
        {
            Vector3[] pose = Body();
            for (int i = 0; i < pose.Length; i++)
            {
                pose[i] = new Vector3(pose[i].x, pose[i].y, -pose[i].z);
            }
            return pose;
        }

        [Test]
        public void BodyFrame_ForwardPointsOutOfTheChest()
        {
            Assert.IsTrue(MediaPipeSpace.TryBodyFrame(Body(), out Vector3 shoulderCenter, out Quaternion frame));

            Vector3 forward = frame * Vector3.forward;
            Vector3 faceDir = Body()[MediaPipeSpace.Nose] - shoulderCenter;

            Assert.Greater(Vector3.Dot(forward, faceDir.normalized), 0.2f,
                "the body frame's forward must agree with the direction the face points");
            Assert.Greater(Vector3.Dot(frame * Vector3.up, Vector3.up), 0.9f, "torso up should point up");
            Assert.Greater(Vector3.Dot(frame * Vector3.right, Vector3.right), 0.9f,
                "the frame's right must match the shoulder line (left shoulder to right shoulder)");
        }

        [Test]
        public void DepthSign_AcceptsAWellFormedBody()
        {
            Assert.AreEqual(1f, MediaPipeSpace.DepthSign(Body()));
        }

        [Test]
        public void DepthSign_DetectsAReflectedCloud()
        {
            Assert.AreEqual(-1f, MediaPipeSpace.DepthSign(DepthReflected()),
                "a body whose nose falls behind its ears is depth-reflected and must be flagged");
        }

        [Test]
        public void DepthSign_CorrectionRestoresTheBody()
        {
            Vector3[] reflected = DepthReflected();
            MediaPipeSpace.ApplyDepthSign(reflected, MediaPipeSpace.DepthSign(reflected));

            Assert.AreEqual(1f, MediaPipeSpace.DepthSign(reflected),
                "after correction the cloud should read as well-formed");

            Assert.IsTrue(MediaPipeSpace.TryBodyFrame(reflected, out Vector3 shoulderCenter, out Quaternion frame));
            Vector3 faceDir = reflected[MediaPipeSpace.Nose] - shoulderCenter;
            Assert.Greater(Vector3.Dot(frame * Vector3.forward, faceDir.normalized), 0.2f);
        }

        [Test]
        public void ApplyDepthSign_IsANoOpWhenPositive()
        {
            Vector3[] pose = Body();
            Vector3[] expected = Body();
            MediaPipeSpace.ApplyDepthSign(pose, 1f);

            for (int i = 0; i < pose.Length; i++)
            {
                Assert.AreEqual(expected[i], pose[i]);
            }
        }

        [Test]
        public void SwapPoseSides_ExchangesAnatomicalPairs()
        {
            Vector3[] pose = Body();
            Vector3 leftShoulder = pose[MediaPipeSpace.LeftShoulder];
            Vector3 rightShoulder = pose[MediaPipeSpace.RightShoulder];

            MediaPipeSpace.SwapPoseSidesInPlace(pose);

            Assert.AreEqual(rightShoulder, pose[MediaPipeSpace.LeftShoulder]);
            Assert.AreEqual(leftShoulder, pose[MediaPipeSpace.RightShoulder]);
            Assert.AreEqual(Body()[MediaPipeSpace.Nose], pose[MediaPipeSpace.Nose], "the nose has no opposite side");
        }

        [Test]
        public void World_FlipsYAndUndoesTheMirror()
        {
            Vector3 raw = new Vector3(0.3f, 0.7f, 0.2f);

            Assert.AreEqual(new Vector3(0.3f, -0.7f, 0.2f), MediaPipeSpace.World(raw, false));
            Assert.AreEqual(new Vector3(-0.3f, -0.7f, 0.2f), MediaPipeSpace.World(raw, true));
        }

        [Test]
        public void Image_KeepsLandmarksInRangeWithYUp()
        {
            Vector3 raw = new Vector3(0.25f, 0.75f, 0.1f);

            Vector3 plain = MediaPipeSpace.Image(raw, false);
            Assert.AreEqual(0.25f, plain.x, 1e-5f);
            Assert.AreEqual(0.25f, plain.y, 1e-5f);

            Vector3 mirrored = MediaPipeSpace.Image(raw, true);
            Assert.AreEqual(0.75f, mirrored.x, 1e-5f);
            Assert.AreEqual(0.25f, mirrored.y, 1e-5f);
        }
    }

    /// <summary>
    /// The wrist rotation. It used to be built on the HIPS rotation, so a hand at rest adopted the body's
    /// orientation instead of the hand bone's. It is now a change of basis between the user's palm frame and
    /// the avatar's, corrected by a constant derived from the avatar's own knuckles.
    /// </summary>
    public class MediaPipeHandRotationTests
    {
        private static readonly Quaternion AvatarHandRest = Quaternion.Euler(20f, 35f, 50f);
        private static readonly Vector3 AvatarHandPos = new Vector3(0.4f, 1.1f, 0f);

        private static readonly Vector3 MiddleOffset = new Vector3(0.09f, 0f, 0f);
        private static readonly Vector3 IndexOffset = new Vector3(0.085f, 0f, 0.02f);
        private static readonly Vector3 PinkyOffset = new Vector3(0.08f, 0f, -0.02f);

        private static Vector3[] Body()
        {
            Vector3[] pose = new Vector3[MediaPipeSpace.PoseCount];
            pose[MediaPipeSpace.Nose] = new Vector3(0f, 1.58f, 0.10f);
            pose[MediaPipeSpace.LeftEar] = new Vector3(-0.08f, 1.62f, -0.02f);
            pose[MediaPipeSpace.RightEar] = new Vector3(0.08f, 1.62f, -0.02f);
            pose[MediaPipeSpace.LeftShoulder] = new Vector3(-0.18f, 1.40f, 0f);
            pose[MediaPipeSpace.RightShoulder] = new Vector3(0.18f, 1.40f, 0f);
            pose[MediaPipeSpace.LeftHip] = new Vector3(-0.12f, 1.00f, 0f);
            pose[MediaPipeSpace.RightHip] = new Vector3(0.12f, 1.00f, 0f);
            return pose;
        }

        /// <summary>A hand whose knuckles sit exactly where the avatar's do, rotated by <paramref name="orientation"/>.</summary>
        private static Vector3[] Hand(Quaternion orientation)
        {
            Vector3[] hand = new Vector3[MediaPipeSpace.HandCount];
            hand[MediaPipeSpace.HandWrist] = Vector3.zero;
            hand[MediaPipeSpace.HandMiddleMcp] = orientation * MiddleOffset;
            hand[MediaPipeSpace.HandIndexMcp] = orientation * IndexOffset;
            hand[MediaPipeSpace.HandPinkyMcp] = orientation * PinkyOffset;
            return hand;
        }

        private static MediaPipeHandConverter.AvatarHandRig Rig()
        {
            Assert.IsTrue(MediaPipeSpace.TryPalmFrame(
                AvatarHandPos,
                AvatarHandPos + AvatarHandRest * IndexOffset,
                AvatarHandPos + AvatarHandRest * MiddleOffset,
                AvatarHandPos + AvatarHandRest * PinkyOffset,
                false, out Quaternion palm));

            Quaternion correction = Quaternion.Inverse(palm) * AvatarHandRest;
            return new MediaPipeHandConverter.AvatarHandRig
            {
                Body = Quaternion.LookRotation(Vector3.forward, Vector3.up),
                LeftCorrection = correction,
                RightCorrection = correction,
                Valid = true,
            };
        }

        private static Quaternion Retarget(Quaternion userHandOrientation)
        {
            BasisMediaPipeResult result = new BasisMediaPipeResult
            {
                HasPose = true,
                HasRightHand = true,
                PoseWorldLandmarks = Body(),
                RightHandWorldLandmarks = Hand(userHandOrientation),
            };

            MediaPipeHandConverter converter = new MediaPipeHandConverter { UseRotation = true, PoseSmoothing = 0f };
            MediaPipeHandConverter.AvatarHandRig rig = Rig();

            Assert.IsTrue(converter.TryGetHandRotation(in result, in rig, false, out Quaternion rotation));
            return rotation;
        }

        [Test]
        public void HandMatchingTheAvatarsPose_ReproducesTheHandBoneRotation()
        {
            Quaternion rotation = Retarget(AvatarHandRest);

            Assert.Less(Quaternion.Angle(rotation, AvatarHandRest), 0.1f,
                $"a palm frame matching the avatar's should reproduce its hand rotation, got {rotation.eulerAngles}");
        }

        [Test]
        public void RotatingYourWrist_RotatesTheAvatarsWristTheSameWay()
        {
            Quaternion twist = Quaternion.AngleAxis(40f, Vector3.up);
            Quaternion rotation = Retarget(twist * AvatarHandRest);

            Assert.Less(Quaternion.Angle(rotation, twist * AvatarHandRest), 0.1f,
                "a wrist rotation in body space should apply the same rotation to the avatar's hand");
        }

        [Test]
        public void RollingYourPalmOver_RollsTheAvatarsPalmOver()
        {
            Quaternion roll = Quaternion.AngleAxis(90f, Vector3.forward);
            Quaternion rotation = Retarget(roll * AvatarHandRest);

            Assert.Less(Quaternion.Angle(rotation, roll * AvatarHandRest), 0.1f,
                "palm roll must not come out inverted, which is what the y-down palm normal used to cause");
        }

        [Test]
        public void RotationIsNotTheBodyRotation()
        {
            Quaternion rotation = Retarget(AvatarHandRest);

            Assert.Greater(Quaternion.Angle(rotation, Quaternion.identity), 5f,
                "the old code anchored the wrist to the hips, so a neutral hand collapsed onto the body rotation");
        }
    }
}
