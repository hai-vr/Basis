using Basis.Scripts.BasisSdk.Interactions;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Sync
{
    public sealed class BasisSeatPlayspaceOffsetTests
    {
        const float AvatarEyeTposeHeight = 1.62f;
        const float AvatarHipsTposeHeight = 0.94f;

        static Vector3 SeatedEyeInPlayspace(Vector3 unscaledEye, Quaternion unscaledEyeRot, float deviceScale,
            bool vr = true, float eyeTposeHeight = AvatarEyeTposeHeight)
        {
            BasisSeatFit.ComposePlayspaceOffset(unscaledEye, unscaledEyeRot, deviceScale, eyeTposeHeight, vr,
                out Vector3 offsetPosition, out Quaternion offsetRotation);

            BasisCalibrationMath.ScaleDeviceCoord(unscaledEye, unscaledEyeRot, deviceScale,
                offsetPosition, offsetRotation, out Vector3 scaledPos, out _);
            return scaledPos;
        }

        static Vector3 SeatedEyeInPlayspace_WithDisabledHeightAnchor(Vector3 unscaledEye, Quaternion unscaledEyeRot, float deviceScale)
        {
            Quaternion offsetRotation = Quaternion.Inverse(BasisSeatFit.YawOnly(unscaledEyeRot));
            Vector3 offsetPosition = offsetRotation * -(unscaledEye * deviceScale);
            offsetPosition.y = 0f;

            BasisCalibrationMath.ScaleDeviceCoord(unscaledEye, unscaledEyeRot, deviceScale,
                offsetPosition, offsetRotation, out Vector3 scaledPos, out _);
            return scaledPos;
        }

        [Test]
        public void Sitting_DropsTheEyeOnTheAvatarsOwnEyeHeight()
        {
            var poses = new[]
            {
                (eye: new Vector3(0f, 1.70f, 0f), rot: Quaternion.identity, scale: 1f),
                (eye: new Vector3(0.85f, 1.52f, -1.30f), rot: Quaternion.Euler(0f, 128f, 0f), scale: 1.07f),
                (eye: new Vector3(-1.9f, 1.88f, 2.4f), rot: Quaternion.Euler(14f, -63f, 6f), scale: 0.86f),
                (eye: new Vector3(0.05f, 1.05f, 0.02f), rot: Quaternion.Euler(-22f, 8f, 0f), scale: 1.54f),
            };

            foreach (var pose in poses)
            {
                Vector3 seated = SeatedEyeInPlayspace(pose.eye, pose.rot, pose.scale);

                Assert.AreEqual(0f, seated.x, 1e-4f,
                    $"the seated eye sat {seated.x:F3} m sideways of the play-space origin for a player "
                    + $"standing at {pose.eye}. Sitting is supposed to re-centre the tracking space on the seat.");
                Assert.AreEqual(0f, seated.z, 1e-4f,
                    $"the seated eye sat {seated.z:F3} m fore/aft of the play-space origin for a player "
                    + $"standing at {pose.eye}.");
                Assert.AreEqual(AvatarEyeTposeHeight, seated.y, 1e-4f,
                    $"the seated eye landed at {seated.y:F3} m instead of the avatar's own {AvatarEyeTposeHeight:F2} m "
                    + $"eye height, for a player {pose.eye.y:F2} m tall at device scale {pose.scale:F2}. With the "
                    + "height anchor disabled this tracked the player's real height instead of the avatar's.");
            }
        }

        [Test]
        public void SeatedEyeHeight_IsInvariantToVerticalPlayspaceShifts()
        {
            Vector3 eye = new Vector3(0.3f, 1.66f, -0.4f);
            Quaternion rot = Quaternion.Euler(0f, 35f, 0f);
            const float deviceScale = 0.97f;

            foreach (float shift in new[] { -1.5f, -0.42f, 0f, 0.42f, 1.5f })
            {
                Vector3 shifted = eye + new Vector3(0f, shift, 0f);

                Vector3 anchored = SeatedEyeInPlayspace(shifted, rot, deviceScale);
                Assert.AreEqual(AvatarEyeTposeHeight, anchored.y, 1e-4f,
                    $"a {shift:+0.00;-0.00} m vertical tracking shift moved the seated eye to {anchored.y:F3} m. "
                    + "The play-space mover's drag, seated mode's lift and the grounding offset all land in "
                    + "this device Y, so any of them would drag the avatar around on the chair.");

                Vector3 disabled = SeatedEyeInPlayspace_WithDisabledHeightAnchor(shifted, rot, deviceScale);
                Assert.AreEqual(shift * deviceScale, disabled.y - (eye.y * deviceScale), 1e-4f,
                    $"the disabled-anchor path is supposed to pass a {shift:+0.00;-0.00} m shift straight "
                    + "through to the seated eye — that is the behaviour the anchor replaces. It no longer "
                    + "does, so the invariance gate above is not discriminating.");
            }
        }

        [Test]
        public void PlayersOfDifferentHeights_SitIdentically()
        {
            Quaternion rot = Quaternion.Euler(0f, 12f, 0f);
            const float deviceScale = 1f;

            Vector3 shortPlayer = SeatedEyeInPlayspace(new Vector3(0f, 1.48f, 0f), rot, deviceScale);
            Vector3 tallPlayer = SeatedEyeInPlayspace(new Vector3(0f, 1.94f, 0f), rot, deviceScale);

            Assert.AreEqual(shortPlayer.y, tallPlayer.y, 1e-4f,
                $"a 1.48 m and a 1.94 m player seated the same avatar at {shortPlayer.y:F3} m and "
                + $"{tallPlayer.y:F3} m. The seated pose belongs to the avatar, not to the headset wearer.");

            Vector3 shortDisabled = SeatedEyeInPlayspace_WithDisabledHeightAnchor(new Vector3(0f, 1.48f, 0f), rot, deviceScale);
            Vector3 tallDisabled = SeatedEyeInPlayspace_WithDisabledHeightAnchor(new Vector3(0f, 1.94f, 0f), rot, deviceScale);
            Assert.Greater(Mathf.Abs(tallDisabled.y - shortDisabled.y), 0.4f,
                "the disabled-anchor path is supposed to seat the two players differently — that is the "
                + "defect. It no longer does, so the gate above is not discriminating.");
        }

        [Test]
        public void SeatedEye_SitsOneSpineAboveThePinnedHips()
        {
            Quaternion seatRot = Quaternion.Euler(0f, 61f, 0f);
            Matrix4x4 seatToWorld = Matrix4x4.TRS(new Vector3(2f, 0f, -3f), seatRot, Vector3.one);

            Assert.IsTrue(BasisSeatFit.BuildFrame(new Vector3(0f, 0.45f, -0.25f), new Vector3(0f, -0.05f, 0.25f),
                new Vector3(0f, 0.45f, 0.25f), 90f, out BasisSeatFitFrame frame));

            var legs = new BasisSeatFitLegs { UpperLegLength = 0.42f, LowerLegLength = 0.40f, FootThickness = 0.08f };
            BasisSeatFitResult fit = BasisSeatFit.Solve(frame, legs);
            BasisSeatFit.ComposeHipsWorld(seatToWorld, seatRot, frame.SpineRotation, fit.Back,
                out Vector3 hipsPos, out Quaternion hipsRot);

            BasisSeatFit.ComposeSeatedRoot(hipsPos, hipsRot, Quaternion.identity,
                new Vector3(0f, AvatarHipsTposeHeight, 0f), out Vector3 rootPos, out Quaternion rootRot);

            Vector3 eyeInPlayspace = SeatedEyeInPlayspace(new Vector3(0.4f, 1.73f, -0.2f), Quaternion.Euler(0f, 200f, 0f), 1.03f);
            Vector3 eyeWorld = rootPos + rootRot * eyeInPlayspace;

            float spine = AvatarEyeTposeHeight - AvatarHipsTposeHeight;
            Assert.AreEqual(hipsPos.y + spine, eyeWorld.y, 1e-3f,
                $"the seated avatar's eyes ended up at {eyeWorld.y:F3} m with its hips pinned at "
                + $"{hipsPos.y:F3} m — a {(eyeWorld.y - hipsPos.y):F3} m torso instead of the avatar's own "
                + $"{spine:F3} m. Hips on the cushion and head one spine above it is the pose the whole "
                + "seat path exists to produce.");
        }

        [Test]
        public void Sitting_AlignsTrackingSpaceYawWithTheSeat()
        {
            foreach (float yaw in new[] { 0f, 37f, 145f, -95f, 359f })
            {
                Quaternion headRot = Quaternion.Euler(9f, yaw, -4f);
                BasisSeatFit.ComposePlayspaceOffset(new Vector3(0.2f, 1.7f, 0.3f), headRot, 1f,
                    AvatarEyeTposeHeight, true, out _, out Quaternion offsetRotation);

                Quaternion seatedHeading = offsetRotation * BasisSeatFit.YawOnly(headRot);

                Assert.Less(Quaternion.Angle(seatedHeading, Quaternion.identity), 1e-2f,
                    $"a player facing {yaw:F0} degrees kept {Quaternion.Angle(seatedHeading, Quaternion.identity):F2} "
                    + "degrees of heading after sitting. Sitting is supposed to align the tracking space to the "
                    + "seat, so the residual heading must be zero and only the seat's own rotation should show.");
            }
        }

        [Test]
        public void Desktop_TakesTheYawAlignmentWithoutTranslating()
        {
            Quaternion headRot = Quaternion.Euler(0f, 88f, 0f);
            BasisSeatFit.ComposePlayspaceOffset(new Vector3(0.4f, 1.6f, 0.9f), headRot, 1.2f,
                AvatarEyeTposeHeight, false, out Vector3 offsetPosition, out Quaternion offsetRotation);

            Assert.AreEqual(Vector3.zero, offsetPosition, "desktop must not translate the tracking space");
            Assert.Less(Quaternion.Angle(offsetRotation * BasisSeatFit.YawOnly(headRot), Quaternion.identity), 1e-2f,
                "desktop still needs the yaw alignment so the seated avatar faces the seat");
        }

        [Test]
        public void HeightReanchor_MovesTheOccupantVerticallyOnly()
        {
            Vector3 sitEye = new Vector3(0.2f, 1.70f, -0.5f);
            Quaternion sitRot = Quaternion.Euler(0f, 74f, 0f);

            BasisSeatFit.ComposePlayspaceOffset(sitEye, sitRot, 1f, AvatarEyeTposeHeight, true,
                out Vector3 offsetPosition, out Quaternion offsetRotation);

            Vector3 movedEye = sitEye + new Vector3(0.6f, 0.09f, -0.35f);
            const float newDeviceScale = 1.4f;
            const float newEyeTposeHeight = 2.21f;

            Vector3 reanchored = offsetPosition;
            reanchored.y = BasisSeatFit.ComposePlayspaceHeightOffset(movedEye, offsetRotation, newDeviceScale, newEyeTposeHeight);

            Assert.AreEqual(offsetPosition.x, reanchored.x, 1e-6f, "re-anchoring must not re-centre X");
            Assert.AreEqual(offsetPosition.z, reanchored.z, 1e-6f, "re-anchoring must not re-centre Z");

            BasisCalibrationMath.ScaleDeviceCoord(movedEye, sitRot, newDeviceScale, reanchored, offsetRotation,
                out Vector3 scaled, out _);
            Assert.AreEqual(newEyeTposeHeight, scaled.y, 1e-4f,
                $"after a scale change the seated eye sat at {scaled.y:F3} m instead of the new avatar's "
                + $"{newEyeTposeHeight:F2} m eye height. Resizing while seated must not lift the avatar off the chair.");
        }

        [Test]
        public void ClearedOffset_LeavesTheDevicePoseUntouched()
        {
            Vector3 eye = new Vector3(0.31f, 1.66f, -0.22f);
            Quaternion rot = Quaternion.Euler(11f, 47f, -3f);
            const float deviceScale = 1.12f;

            BasisCalibrationMath.ScaleDeviceCoord(eye, rot, deviceScale, Vector3.zero, Quaternion.identity,
                out Vector3 scaled, out Quaternion scaledRot);

            Assert.Less((scaled - eye * deviceScale).magnitude, 1e-5f,
                "with the offset cleared the device pose is just the scaled room pose again");
            Assert.Less(Quaternion.Angle(scaledRot, rot), 1e-3f,
                "with the offset cleared the device rotation is untouched");
        }
    }
}
