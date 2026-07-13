using Basis.MediaPipe;
using NUnit.Framework;
using UnityEngine;

namespace Basis.MediaPipe.Tests
{
    /// <summary>
    /// Ground truth for the webcam arm retarget. The landmarks here are already in the converter's space
    /// (Unity y-up, un-mirrored, anatomically labelled) — the same space <see cref="MediaPipeSpace"/> puts
    /// raw MediaPipe output into.
    ///
    /// The user is a ~1.75 m person with a 0.55 m arm; the avatar is deliberately shorter with a 0.47 m arm,
    /// so nothing passes by accident on matched proportions.
    /// </summary>
    public class MediaPipeArmRetargetTests
    {
        private const float UserHipY = 1.00f;
        private const float UserShoulderY = 1.40f;
        private const float UserNoseY = 1.58f;

        private const float AvatarHipY = 0.88f;
        private const float AvatarShoulderY = 1.24f;
        private const float AvatarHeadY = 1.42f;
        private const float AvatarUpperLen = 0.24f;
        private const float AvatarForeLen = 0.23f;

        private static Vector3[] Body()
        {
            Vector3[] pose = new Vector3[MediaPipeSpace.PoseCount];
            pose[MediaPipeSpace.Nose] = new Vector3(0f, UserNoseY, 0.10f);
            pose[MediaPipeSpace.LeftEar] = new Vector3(-0.08f, 1.62f, -0.02f);
            pose[MediaPipeSpace.RightEar] = new Vector3(0.08f, 1.62f, -0.02f);
            pose[MediaPipeSpace.LeftShoulder] = new Vector3(-0.18f, UserShoulderY, 0f);
            pose[MediaPipeSpace.RightShoulder] = new Vector3(0.18f, UserShoulderY, 0f);
            pose[MediaPipeSpace.LeftHip] = new Vector3(-0.12f, UserHipY, 0f);
            pose[MediaPipeSpace.RightHip] = new Vector3(0.12f, UserHipY, 0f);
            return pose;
        }

        /// <summary>Right arm hanging down at the side.</summary>
        private static Vector3[] ArmDown()
        {
            Vector3[] pose = Body();
            pose[MediaPipeSpace.RightElbow] = new Vector3(0.22f, 1.14f, 0.03f);
            pose[MediaPipeSpace.RightWrist] = new Vector3(0.24f, 0.88f, 0.06f);
            return pose;
        }

        /// <summary>Right hand held up beside the face: elbow out and up, wrist level with the jaw.</summary>
        private static Vector3[] HandAtFace()
        {
            Vector3[] pose = Body();
            pose[MediaPipeSpace.RightElbow] = new Vector3(0.38f, 1.26f, 0.12f);
            pose[MediaPipeSpace.RightWrist] = new Vector3(0.24f, 1.50f, 0.16f);
            return pose;
        }

        private static MediaPipeArmConverter.AvatarArmRig Rig()
        {
            Vector3 shoulderCenter = new Vector3(0f, AvatarShoulderY, 0f);
            Vector3 head = new Vector3(0f, AvatarHeadY, 0f);
            return new MediaPipeArmConverter.AvatarArmRig
            {
                LeftAnchor = new Vector3(-0.15f, AvatarShoulderY, 0f),
                RightAnchor = new Vector3(0.15f, AvatarShoulderY, 0f),
                LeftUpperLen = AvatarUpperLen,
                LeftForeLen = AvatarForeLen,
                RightUpperLen = AvatarUpperLen,
                RightForeLen = AvatarForeLen,
                Right = Vector3.right,
                Up = Vector3.up,
                Forward = Vector3.forward,
                HeadLocal = head,
                HeadMetric = Vector3.Distance(head, shoulderCenter),
                Valid = true,
            };
        }

        private static MediaPipeArmConverter Converter(float headAnchor = 1f) =>
            new MediaPipeArmConverter { Smoothing = 0f, HeadAnchor = headAnchor };

        private static Vector3 Retarget(Vector3[] pose, float headAnchor = 1f)
        {
            MediaPipeArmConverter converter = Converter(headAnchor);
            MediaPipeArmConverter.AvatarArmRig rig = Rig();
            Assert.IsTrue(converter.TryGetArm(pose, in rig, false, out Vector3 wrist, out _, out _),
                "pose retarget should succeed on a well-formed body");
            return wrist;
        }

        // The reported bug: the hand landed on a plane around the hips and could never climb past the ribs,
        // so a hand held at the face showed up at the waist. Anything at or below this is that bug returning.
        private const float WaistCeiling = AvatarHipY + 0.35f;

        [Test]
        public void HandBesideFace_LandsBesideAvatarHead_NotAtWaist()
        {
            Vector3 wrist = Retarget(HandAtFace());

            Assert.Greater(wrist.y, WaistCeiling,
                $"wrist at {wrist.y:F3} is still inside the old hips-anchored band (<= {WaistCeiling:F3})");
            Assert.That(wrist.y, Is.EqualTo(AvatarHeadY).Within(0.15f),
                $"hand held at the face should land near the avatar's head ({AvatarHeadY:F3}), got {wrist.y:F3}");
        }

        [Test]
        public void ArmDown_StaysDown()
        {
            Vector3 wrist = Retarget(ArmDown());

            Assert.Less(wrist.y, AvatarShoulderY - 0.3f,
                $"a hanging arm should stay low, got {wrist.y:F3}");
            Assert.Greater(wrist.y, 0.5f, "a hanging arm should not sink through the avatar");
        }

        [Test]
        public void UserRightHand_DrivesAvatarRightSide()
        {
            Vector3 wrist = Retarget(HandAtFace());
            Assert.Greater(wrist.x, 0f, "the user's right hand must stay on the avatar's right (+X)");
        }

        [Test]
        public void HandInFrontOfBody_KeepsItsDepth()
        {
            Vector3 wrist = Retarget(HandAtFace());
            Assert.Greater(wrist.z, 0.02f,
                $"the hand is held in front of the chest, so it should retarget forward (+Z), got {wrist.z:F3}");
        }

        /// <summary>
        /// Sitting closer to the camera, or simply being a bigger person, scales every world landmark. The
        /// retarget divides through by the user's own arm length, so the avatar must not move at all.
        /// </summary>
        [Test]
        public void RetargetIsInvariantToUserScale()
        {
            Vector3[] pose = HandAtFace();
            Vector3[] scaled = new Vector3[pose.Length];
            for (int i = 0; i < pose.Length; i++)
            {
                scaled[i] = pose[i] * 1.35f;
            }

            Vector3 baseline = Retarget(pose);
            Vector3 bigger = Retarget(scaled);

            Assert.That(Vector3.Distance(baseline, bigger), Is.LessThan(1e-4f),
                $"scale changed the target: {baseline} vs {bigger}");
        }

        /// <summary>The head anchor is what pulls a raised hand up to the avatar's face rather than merely
        /// scaling it by arm length, so with it off the hand should sit lower.</summary>
        [Test]
        public void HeadAnchor_LiftsRaisedHandTowardTheHead()
        {
            float anchored = Retarget(HandAtFace(), 1f).y;
            float reachOnly = Retarget(HandAtFace(), 0f).y;

            Assert.Greater(anchored, reachOnly,
                "head anchoring should raise a face-height hand relative to pure reach matching");
            Assert.That(Mathf.Abs(anchored - AvatarHeadY), Is.LessThan(Mathf.Abs(reachOnly - AvatarHeadY)),
                "head anchoring should land closer to the avatar's head than pure reach matching");
        }

        /// <summary>The head anchor must not disturb a hand hanging at the waist, only a raised one.</summary>
        [Test]
        public void HeadAnchor_DoesNotAffectLoweredHand()
        {
            float anchored = Retarget(ArmDown(), 1f).y;
            float reachOnly = Retarget(ArmDown(), 0f).y;

            Assert.That(anchored, Is.EqualTo(reachOnly).Within(1e-5f),
                "below the shoulder the head anchor should fade out entirely");
        }

        [Test]
        public void WristNeverLeavesTheAvatarsReach()
        {
            Vector3[] pose = HandAtFace();
            pose[MediaPipeSpace.RightWrist] = new Vector3(3f, 3f, 3f);

            Vector3 wrist = Retarget(pose);
            float reach = Vector3.Distance(wrist, Rig().RightAnchor);

            Assert.LessOrEqual(reach, AvatarUpperLen + AvatarForeLen,
                $"target {reach:F3} m exceeds the avatar's {AvatarUpperLen + AvatarForeLen:F3} m arm");
        }

        [Test]
        public void ElbowSitsBetweenShoulderAndWrist()
        {
            MediaPipeArmConverter converter = Converter();
            MediaPipeArmConverter.AvatarArmRig rig = Rig();
            Assert.IsTrue(converter.TryGetArm(HandAtFace(), in rig, false, out Vector3 wrist, out Vector3 elbow, out _));

            float upper = Vector3.Distance(rig.RightAnchor, elbow);
            Assert.That(upper, Is.EqualTo(AvatarUpperLen).Within(0.06f),
                $"elbow should sit about an upper-arm from the shoulder, got {upper:F3}");
            Assert.Greater(elbow.x, wrist.x,
                "with the hand tucked in at the face the elbow should be further out than the wrist");
        }
    }
}
