using System.Collections.Generic;
using Basis.Cinematics;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// Shared rig for the body-mode tests. Shots are configured so the whole solve stays in managed
    /// code — no composer aim, no noise — which keeps these runnable outside the Unity runtime and
    /// isolates the body stage from the aim stage.
    /// </summary>
    public static class ShotFixture
    {
        public static BasisCameraSubject Subject(Vector3 anchor = default, float yawDegrees = 0f, float scale = 1f)
            => new BasisCameraSubject
            {
                Valid = true,
                AnchorPos = anchor,
                LookPoint = anchor + Vector3.up * 0.2f,
                GroundPos = anchor - Vector3.up * 1.6f,
                Yaw = BasisCameraDamping.Yaw(yawDegrees),
                Scale = scale,
                Radius = 0.45f,
            };

        public static BasisCameraSolveContext Context(BasisCameraSubject subject, float deltaTime = 1f / 60f)
            => new BasisCameraSolveContext
            {
                Subject = subject,
                Fov = 40f,
                Aspect = 16f / 9f,
                DeltaTime = deltaTime,
                Time = 0f,
                ManualRotation = Quaternion.identity,
            };

        public static BasisCameraSolveContext Context() => Context(Subject());

        /// <summary>Strips every stage that would reach a native Unity call, leaving the body solve.</summary>
        public static BasisCameraShot BodyOnly(BasisCameraShot shot)
        {
            shot.aimMode = BasisCameraAimMode.None;
            shot.noise = BasisCameraNoiseSettings.ForProfile(BasisCameraNoiseProfile.Off);
            shot.positionDamping = Vector3.zero;
            shot.rotationDamping = Vector3.zero;
            return shot;
        }

        /// <summary>Runs the shot to rest so the assertion sees its settled pose, not its first step.</summary>
        public static BasisCameraPose Settle(BasisCameraShot shot, BasisCameraShotState state,
            BasisCameraSolveContext context, int frames = 240)
        {
            BasisCameraPose pose = default;
            for (int Frame = 0; Frame < frames; Frame++)
            {
                pose = BasisCameraDirector.SolveShot(shot, state, context);
            }
            return pose;
        }
    }

    public class BasisCameraTransposerTests
    {
        [Test]
        public void TheCameraSettlesOnItsAuthoredOffset()
        {
            BasisCameraShot shot = ShotFixture.BodyOnly(new BasisCameraShot());
            shot.bodyMode = BasisCameraBodyMode.Transposer;
            shot.positionOffset = new Vector3(1f, 0.5f, 2f);

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), ShotFixture.Context());

            Assert.That(pose.Position.x, Is.EqualTo(1f).Within(1e-3f));
            Assert.That(pose.Position.y, Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(pose.Position.z, Is.EqualTo(2f).Within(1e-3f));
        }

        [Test]
        public void SubjectYawBinding_CarriesTheOffsetRoundAsTheSubjectTurns()
        {
            BasisCameraShot shot = ShotFixture.BodyOnly(new BasisCameraShot());
            shot.bindingMode = BasisCameraBindingMode.SubjectYaw;
            shot.positionOffset = new Vector3(0f, 0f, 2f);

            BasisCameraSubject turned = ShotFixture.Subject(Vector3.zero, 90f);
            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), ShotFixture.Context(turned));

            Assert.That(pose.Position.x, Is.EqualTo(2f).Within(1e-2f),
                "A subject facing +X should be filmed from +X, so the shot stays in front of them.");
            Assert.That(pose.Position.z, Is.EqualTo(0f).Within(1e-2f));
        }

        [Test]
        public void WorldSpaceBinding_IgnoresWhichWayTheSubjectFaces()
        {
            BasisCameraShot shot = ShotFixture.BodyOnly(new BasisCameraShot());
            shot.bindingMode = BasisCameraBindingMode.WorldSpace;
            shot.positionOffset = new Vector3(0f, 0f, 2f);

            BasisCameraSubject turned = ShotFixture.Subject(Vector3.zero, 90f);
            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), ShotFixture.Context(turned));

            Assert.That(pose.Position.z, Is.EqualTo(2f).Within(1e-2f),
                "World binding is what stops a locked-off shot swinging every time the subject glances.");
            Assert.That(pose.Position.x, Is.EqualTo(0f).Within(1e-2f));
        }

        [Test]
        public void SimpleFollowBinding_HoldsDistanceWithoutSwingingRoundToTheFront()
        {
            BasisCameraShot shot = ShotFixture.BodyOnly(new BasisCameraShot());
            shot.bindingMode = BasisCameraBindingMode.SimpleFollow;
            shot.positionOffset = new Vector3(0f, 0f, 3f);

            var state = new BasisCameraShotState();
            state.Seed(new Vector3(5f, 0f, 0f), Quaternion.identity, 40f);

            BasisCameraSubject turned = ShotFixture.Subject(Vector3.zero, 180f);
            BasisCameraPose pose = ShotFixture.Settle(shot, state, ShotFixture.Context(turned));

            Assert.That(pose.Position.x, Is.EqualTo(3f).Within(1e-2f),
                "The camera stays on the side it was already on and only corrects its distance.");
            Assert.That(pose.Position.magnitude, Is.EqualTo(3f).Within(1e-2f));
        }

        [Test]
        public void TheOffsetScalesWithTheAvatar()
        {
            BasisCameraShot shot = ShotFixture.BodyOnly(new BasisCameraShot());
            shot.positionOffset = new Vector3(0f, 0f, 2f);

            BasisCameraPose small = ShotFixture.Settle(shot, new BasisCameraShotState(),
                ShotFixture.Context(ShotFixture.Subject(Vector3.zero, 0f, 0.5f)));
            BasisCameraPose large = ShotFixture.Settle(shot, new BasisCameraShotState(),
                ShotFixture.Context(ShotFixture.Subject(Vector3.zero, 0f, 2f)));

            Assert.That(small.Position.z, Is.EqualTo(1f).Within(1e-2f));
            Assert.That(large.Position.z, Is.EqualTo(4f).Within(1e-2f));
        }

        [Test]
        public void TheShotTracksTheSubjectWhenTheyMove()
        {
            BasisCameraShot shot = ShotFixture.BodyOnly(new BasisCameraShot());
            shot.positionOffset = new Vector3(0f, 0f, 2f);
            var state = new BasisCameraShotState();

            ShotFixture.Settle(shot, state, ShotFixture.Context());
            BasisCameraPose moved = ShotFixture.Settle(shot, state,
                ShotFixture.Context(ShotFixture.Subject(new Vector3(10f, 0f, 0f))));

            Assert.That(moved.Position.x, Is.EqualTo(10f).Within(1e-2f));
        }

        [Test]
        public void DampingMakesTheShotLagRatherThanTeleport()
        {
            BasisCameraShot shot = ShotFixture.BodyOnly(new BasisCameraShot());
            shot.positionOffset = Vector3.zero;
            shot.positionDamping = new Vector3(1f, 1f, 1f);

            var state = new BasisCameraShotState();
            state.Seed(Vector3.zero, Quaternion.identity, 40f);

            BasisCameraPose afterOneFrame = BasisCameraDirector.SolveShot(shot, state,
                ShotFixture.Context(ShotFixture.Subject(new Vector3(100f, 0f, 0f))));

            Assert.That(afterOneFrame.Position.x, Is.GreaterThan(0f), "It must start moving.");
            Assert.That(afterOneFrame.Position.x, Is.LessThan(20f), "But nowhere near arrive in one frame.");
        }

        [Test]
        public void ALockedOffShotIgnoresTheSubjectEntirely()
        {
            BasisCameraShot shot = ShotFixture.BodyOnly(new BasisCameraShot());
            shot.bodyMode = BasisCameraBodyMode.HardLock;

            var state = new BasisCameraShotState();
            state.Seed(new Vector3(7f, 2f, -3f), Quaternion.identity, 40f);

            BasisCameraPose pose = ShotFixture.Settle(shot, state,
                ShotFixture.Context(ShotFixture.Subject(new Vector3(50f, 0f, 50f))));

            Assert.That(pose.Position, Is.EqualTo(new Vector3(7f, 2f, -3f)));
        }
    }

    public class BasisCameraFramingModeTests
    {
        private static BasisCameraShot FramingShot()
        {
            BasisCameraShot shot = ShotFixture.BodyOnly(new BasisCameraShot());
            shot.bodyMode = BasisCameraBodyMode.Framing;
            shot.positionOffset = new Vector3(0f, 0f, 1f);
            shot.framingScreenFraction = 0.3f;
            shot.framingMinDistance = 0.1f;
            shot.framingMaxDistance = 100f;
            return shot;
        }

        [Test]
        public void TheCameraSitsAtTheDistanceThatHoldsTheSubjectAtTheRequestedSize()
        {
            BasisCameraShot shot = FramingShot();
            BasisCameraSubject subject = ShotFixture.Subject();

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), ShotFixture.Context(subject));

            float expected = BasisCameraFraming.DistanceToFit(subject.Radius, 40f, 16f / 9f, shot.framingScreenFraction);
            Assert.That(pose.Position.magnitude, Is.EqualTo(expected).Within(1e-2f));
        }

        [Test]
        public void ABiggerSubjectPushesTheCameraBack()
        {
            BasisCameraShot shot = FramingShot();

            BasisCameraSubject small = ShotFixture.Subject();
            small.Radius = 0.4f;
            BasisCameraSubject large = ShotFixture.Subject();
            large.Radius = 3f;

            float near = ShotFixture.Settle(shot, new BasisCameraShotState(), ShotFixture.Context(small)).Position.magnitude;
            float far = ShotFixture.Settle(shot, new BasisCameraShotState(), ShotFixture.Context(large)).Position.magnitude;

            Assert.That(far, Is.GreaterThan(near),
                "This is what keeps a spreading group in frame without touching the controls.");
        }

        [Test]
        public void FramingKeepsTheDirectionOfTheAuthoredOffset()
        {
            BasisCameraShot shot = FramingShot();
            shot.positionOffset = new Vector3(1f, 1f, 1f);

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), ShotFixture.Context());
            Vector3 direction = pose.Position.normalized;

            Assert.That(direction.x, Is.EqualTo(direction.y).Within(1e-3f));
            Assert.That(direction.y, Is.EqualTo(direction.z).Within(1e-3f));
        }

        [Test]
        public void TheDistanceIsClampedBetweenTheAuthoredLimits()
        {
            BasisCameraShot shot = FramingShot();
            shot.framingMinDistance = 5f;
            shot.framingMaxDistance = 6f;

            BasisCameraSubject tiny = ShotFixture.Subject();
            tiny.Radius = 0.01f;
            float near = ShotFixture.Settle(shot, new BasisCameraShotState(), ShotFixture.Context(tiny)).Position.magnitude;
            Assert.That(near, Is.EqualTo(5f).Within(1e-2f));

            BasisCameraSubject huge = ShotFixture.Subject();
            huge.Radius = 500f;
            float far = ShotFixture.Settle(shot, new BasisCameraShotState(), ShotFixture.Context(huge)).Position.magnitude;
            Assert.That(far, Is.EqualTo(6f).Within(1e-2f));
        }

        [Test]
        public void ZoomFramingHoldsTheCameraStillAndChangesTheLensInstead()
        {
            BasisCameraShot shot = FramingShot();
            shot.framingUsesZoom = true;
            shot.positionOffset = new Vector3(0f, 0f, 4f);

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), ShotFixture.Context());

            Assert.That(pose.Position.z, Is.EqualTo(4f).Within(1e-2f), "Zoom framing must not dolly.");
            Assert.That(pose.Fov, Is.Not.EqualTo(40f).Within(1e-3f), "It has to actually move the lens.");
        }

        [Test]
        public void ZeroOffsetDoesNotDivideByZero()
        {
            BasisCameraShot shot = FramingShot();
            shot.positionOffset = Vector3.zero;

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), ShotFixture.Context(), 4);

            Assert.That(float.IsNaN(pose.Position.x), Is.False);
            Assert.That(float.IsNaN(pose.Position.z), Is.False);
        }
    }

    public class BasisCameraOrbitModeTests
    {
        private static BasisCameraShot OrbitShot()
        {
            BasisCameraShot shot = ShotFixture.BodyOnly(new BasisCameraShot());
            shot.bodyMode = BasisCameraBodyMode.Orbital;
            shot.orbit = BasisCameraOrbitSettings.Default;
            shot.orbit.headingDamping = 0f;
            return shot;
        }

        [Test]
        public void TheCameraSitsOnTheRingTheVerticalAxisSelects()
        {
            BasisCameraShot shot = OrbitShot();
            shot.orbit.verticalAxis = 0.5f;
            shot.orbit.heading = 0f;

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), ShotFixture.Context());

            Assert.That(pose.Position.z, Is.EqualTo(shot.orbit.middle.radius).Within(1e-2f));
            Assert.That(pose.Position.y, Is.EqualTo(shot.orbit.middle.height).Within(1e-2f));
        }

        [Test]
        public void SweepingUpRaisesTheCamera()
        {
            BasisCameraShot low = OrbitShot();
            low.orbit.verticalAxis = 0f;
            BasisCameraShot high = OrbitShot();
            high.orbit.verticalAxis = 1f;

            float lowY = ShotFixture.Settle(low, new BasisCameraShotState(), ShotFixture.Context()).Position.y;
            float highY = ShotFixture.Settle(high, new BasisCameraShotState(), ShotFixture.Context()).Position.y;

            Assert.That(highY, Is.GreaterThan(lowY));
        }

        [Test]
        public void HeadingWalksTheCameraRoundTheSubject()
        {
            BasisCameraShot shot = OrbitShot();
            shot.orbit.heading = 90f;

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), ShotFixture.Context());

            Assert.That(pose.Position.x, Is.EqualTo(shot.orbit.middle.radius).Within(1e-2f));
            Assert.That(pose.Position.z, Is.EqualTo(0f).Within(1e-2f));
        }

        [Test]
        public void TheOrbitTurnsWithTheSubjectWhenAskedTo()
        {
            BasisCameraShot shot = OrbitShot();
            shot.orbit.followSubjectHeading = true;
            shot.orbit.heading = 0f;

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(),
                ShotFixture.Context(ShotFixture.Subject(Vector3.zero, 90f)));

            Assert.That(pose.Position.x, Is.EqualTo(shot.orbit.middle.radius).Within(1e-2f));
        }

        [Test]
        public void TheOrbitStaysWorldLockedWhenNotFollowingTheSubject()
        {
            BasisCameraShot shot = OrbitShot();
            shot.orbit.followSubjectHeading = false;
            shot.orbit.heading = 0f;

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(),
                ShotFixture.Context(ShotFixture.Subject(Vector3.zero, 90f)));

            Assert.That(pose.Position.z, Is.EqualTo(shot.orbit.middle.radius).Within(1e-2f));
        }

        [Test]
        public void TheOrbitFollowsTheSubjectAround()
        {
            BasisCameraShot shot = OrbitShot();

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(),
                ShotFixture.Context(ShotFixture.Subject(new Vector3(20f, 0f, -5f))));

            Assert.That(pose.Position.x, Is.EqualTo(20f).Within(1e-2f));
        }
    }

    public class BasisCameraDollyModeTests
    {
        private static readonly List<Vector3> Track = new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(5f, 0f, 0f),
            new Vector3(10f, 0f, 0f),
        };

        private static BasisCameraShot DollyShot()
        {
            BasisCameraShot shot = ShotFixture.BodyOnly(new BasisCameraShot());
            shot.bodyMode = BasisCameraBodyMode.Dolly;
            shot.dollyAutoTrack = false;
            shot.dollyDamping = 0f;
            shot.dollySpeed = 0f;
            shot.dollyOffset = Vector3.zero;
            return shot;
        }

        private static BasisCameraSolveContext WithTrack(BasisCameraSubject subject)
        {
            BasisCameraSolveContext context = ShotFixture.Context(subject);
            context.DollyPoints = Track;
            context.DollyLooped = false;
            return context;
        }

        [Test]
        public void TheCameraRidesToTheTrackPositionItWasGiven()
        {
            BasisCameraShot shot = DollyShot();
            shot.dollyPosition = 1f;

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), WithTrack(ShotFixture.Subject()));

            Assert.That(pose.Position.x, Is.EqualTo(5f).Within(1e-2f));
        }

        [Test]
        public void AutoTrackSlidesToWhicheverPartOfTheTrackIsNearestTheSubject()
        {
            BasisCameraShot shot = DollyShot();
            shot.dollyAutoTrack = true;

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(),
                WithTrack(ShotFixture.Subject(new Vector3(10f, 0f, 3f))));

            Assert.That(pose.Position.x, Is.EqualTo(10f).Within(0.2f));
        }

        [Test]
        public void AutoTrackFollowsTheSubjectAlongTheTrack()
        {
            BasisCameraShot shot = DollyShot();
            shot.dollyAutoTrack = true;
            var state = new BasisCameraShotState();

            float near = ShotFixture.Settle(shot, state, WithTrack(ShotFixture.Subject(new Vector3(0f, 0f, 3f)))).Position.x;
            float far = ShotFixture.Settle(shot, state, WithTrack(ShotFixture.Subject(new Vector3(10f, 0f, 3f)))).Position.x;

            Assert.That(far, Is.GreaterThan(near + 5f));
        }

        [Test]
        public void ATrackWithNoPointsFallsBackToTheAuthoredOffsetInsteadOfCollapsing()
        {
            BasisCameraShot shot = DollyShot();
            shot.positionOffset = new Vector3(0f, 0f, 2f);

            BasisCameraSolveContext context = ShotFixture.Context();
            context.DollyPoints = new List<Vector3>();

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), context);

            Assert.That(pose.Position.z, Is.EqualTo(2f).Within(1e-2f),
                "Picking Dolly before laying a track must not drop the camera to world zero.");
        }

        [Test]
        public void ANullTrackIsHandledLikeAnEmptyOne()
        {
            BasisCameraShot shot = DollyShot();
            shot.positionOffset = new Vector3(0f, 0f, 2f);

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), ShotFixture.Context());

            Assert.That(pose.Position.z, Is.EqualTo(2f).Within(1e-2f));
        }

        [Test]
        public void TrackSpeedCarriesTheCameraAlongOverTime()
        {
            BasisCameraShot shot = DollyShot();
            shot.dollySpeed = 2f;
            var state = new BasisCameraShotState();

            BasisCameraPose pose = ShotFixture.Settle(shot, state, WithTrack(ShotFixture.Subject()), 60);

            Assert.That(pose.Position.x, Is.GreaterThan(0.5f), "One second at 2 m/s should have covered ground.");
        }

        [Test]
        public void TheDollyPositionIsClampedToTheTrackOnAnOpenPath()
        {
            BasisCameraShot shot = DollyShot();
            shot.dollyPosition = 99f;

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), WithTrack(ShotFixture.Subject()));

            Assert.That(pose.Position.x, Is.EqualTo(10f).Within(1e-2f));
        }
    }

    public class BasisCameraOcclusionTests
    {
        /// <summary>A wall that leaves <paramref name="free"/> metres clear from the subject outward.</summary>
        private static BasisCameraOcclusionProbe Wall(float free)
        {
            return (Vector3 target, Vector3 desired, out float freeDistance) =>
            {
                freeDistance = free;
                return true;
            };
        }

        private static BasisCameraOcclusionProbe Clear()
        {
            return (Vector3 target, Vector3 desired, out float freeDistance) =>
            {
                freeDistance = 0f;
                return false;
            };
        }

        private static BasisCameraShot OccludedShot()
        {
            BasisCameraShot shot = ShotFixture.BodyOnly(new BasisCameraShot());
            shot.positionOffset = new Vector3(0f, 0f, 10f);
            shot.avoidOcclusion = true;
            shot.occlusionPadding = 0.5f;
            shot.occlusionMinDistance = 0.4f;
            shot.occlusionReturnDamping = 0f;
            return shot;
        }

        private static BasisCameraSolveContext WithProbe(BasisCameraOcclusionProbe probe)
        {
            // Aim straight up so the look point sits directly above the anchor and the pull-in runs
            // along a clean axis, keeping the arithmetic in the assertion obvious.
            BasisCameraSubject subject = ShotFixture.Subject();
            subject.LookPoint = subject.AnchorPos;

            BasisCameraSolveContext context = ShotFixture.Context(subject);
            context.OcclusionProbe = probe;
            return context;
        }

        [Test]
        public void AWallPullsTheCameraInToSitInFrontOfIt()
        {
            BasisCameraShot shot = OccludedShot();

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), WithProbe(Wall(4f)));

            Assert.That(pose.Position.z, Is.EqualTo(3.5f).Within(1e-2f), "Four metres clear, half a metre of padding.");
        }

        [Test]
        public void AClearLineOfSightLeavesTheFramingExactlyAsAuthored()
        {
            BasisCameraShot shot = OccludedShot();

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), WithProbe(Clear()));

            Assert.That(pose.Position.z, Is.EqualTo(10f).Within(1e-2f));
        }

        [Test]
        public void OcclusionIsIgnoredEntirelyWhenTheShotDoesNotAskForIt()
        {
            BasisCameraShot shot = OccludedShot();
            shot.avoidOcclusion = false;

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), WithProbe(Wall(1f)));

            Assert.That(pose.Position.z, Is.EqualTo(10f).Within(1e-2f));
        }

        [Test]
        public void TheCameraNeverPushesCloserThanTheMinimum()
        {
            BasisCameraShot shot = OccludedShot();

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), WithProbe(Wall(0.05f)));

            Assert.That(pose.Position.z, Is.EqualTo(0.4f).Within(1e-3f),
                "Past the minimum the camera would be inside the subject's head.");
        }

        [Test]
        public void OcclusionNeverPushesTheCameraFurtherOutThanTheShotAsksFor()
        {
            BasisCameraShot shot = OccludedShot();

            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), WithProbe(Wall(500f)));

            Assert.That(pose.Position.z, Is.EqualTo(10f).Within(1e-2f));
        }

        [Test]
        public void ThePullInIsImmediateButTheReturnIsEased()
        {
            BasisCameraShot shot = OccludedShot();
            shot.occlusionReturnDamping = 1f;

            var state = new BasisCameraShotState();
            ShotFixture.Settle(shot, state, WithProbe(Clear()));

            BasisCameraPose blocked = BasisCameraDirector.SolveShot(shot, state, WithProbe(Wall(2f)));
            Assert.That(blocked.Position.z, Is.EqualTo(1.5f).Within(1e-2f),
                "A damped pull-in would show a frame of wall, so it has to be instant.");

            BasisCameraPose released = BasisCameraDirector.SolveShot(shot, state, WithProbe(Clear()));
            Assert.That(released.Position.z, Is.GreaterThan(1.5f), "It must start coming back.");
            Assert.That(released.Position.z, Is.LessThan(4f), "But ease out, not snap.");
        }

        [Test]
        public void AMissingProbeIsTreatedAsAClearShot()
        {
            BasisCameraShot shot = OccludedShot();

            BasisCameraSubject subject = ShotFixture.Subject();
            subject.LookPoint = subject.AnchorPos;
            BasisCameraPose pose = ShotFixture.Settle(shot, new BasisCameraShotState(), ShotFixture.Context(subject));

            Assert.That(pose.Position.z, Is.EqualTo(10f).Within(1e-2f));
        }
    }
}
