using System.Collections.Generic;
using Basis.Cinematics;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// Shared rig for the modifier tests. Stacks are configured so the whole solve stays in managed
    /// code — the rotation slot holds rather than aims, and no shake is fitted — which keeps these
    /// runnable outside the Unity runtime and isolates the position solve from the rotation solve.
    /// </summary>
    public static class StackFixture
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
                OperatorPosition = Vector3.zero,
                OperatorRotation = Quaternion.identity,
            };

        public static BasisCameraSolveContext Context() => Context(Subject());

        /// <summary>
        /// A stack fitted with one position modifier and nothing that would reach a native Unity
        /// call, so the assertion sees the position solve on its own.
        /// </summary>
        public static BasisCameraModifierStack PositionOnly(BasisCameraPositionModifier modifier)
        {
            BasisCameraModifierStack stack = new BasisCameraModifierStack
            {
                positionModifier = modifier,
                rotationModifier = BasisCameraRotationModifier.Hold,
            };
            stack.follow.damping = Vector3.zero;
            stack.framing.damping = Vector3.zero;
            stack.dolly.damping = 0f;
            stack.orbit.headingDamping = 0f;
            stack.orbit.verticalDamping = 0f;
            return stack;
        }

        /// <summary>Writes the placement offset onto whichever block the fitted modifier reads.</summary>
        public static void Offset(BasisCameraModifierStack stack, Vector3 offset)
        {
            if (stack.positionModifier == BasisCameraPositionModifier.FrameSubject)
            {
                stack.framing.directionOffset = offset;
            }
            else
            {
                stack.follow.positionOffset = offset;
            }
        }

        public static void Binding(BasisCameraModifierStack stack, BasisCameraBindingMode mode)
        {
            if (stack.positionModifier == BasisCameraPositionModifier.FrameSubject)
            {
                stack.framing.bindingMode = mode;
            }
            else
            {
                stack.follow.bindingMode = mode;
            }
        }

        public static void Damping(BasisCameraModifierStack stack, Vector3 damping)
        {
            if (stack.positionModifier == BasisCameraPositionModifier.FrameSubject)
            {
                stack.framing.damping = damping;
            }
            else
            {
                stack.follow.damping = damping;
            }
        }

        /// <summary>Runs the stack to rest so the assertion sees its settled pose, not its first step.</summary>
        public static BasisCameraPose Settle(BasisCameraModifierStack stack, BasisCameraModifierState state,
            BasisCameraSolveContext context, int frames = 240)
        {
            BasisCameraPose pose = default;
            for (int Frame = 0; Frame < frames; Frame++)
            {
                pose = BasisCameraModifierSolver.Solve(stack, state, context);
            }
            return pose;
        }

        public static BasisCameraModifierState State() => new BasisCameraModifierState();
    }

    public class BasisCameraFollowModifierTests
    {
        [Test]
        public void TheCameraSettlesOnItsAuthoredOffset()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FollowSubject);
            StackFixture.Offset(stack, new Vector3(0.5f, 0f, 1.4f));
            stack.follow.lateralTracking = 0f;

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(), StackFixture.Context());

            Assert.That(pose.Position, Is.EqualTo(new Vector3(0.5f, 0f, 1.4f)).Using(Vec(1e-3f)));
        }

        [Test]
        public void SubjectYawBinding_CarriesTheOffsetRoundAsTheSubjectTurns()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FollowSubject);
            StackFixture.Offset(stack, new Vector3(0f, 0f, 2f));
            StackFixture.Binding(stack, BasisCameraBindingMode.SubjectYaw);
            stack.follow.lateralTracking = 0f;

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(),
                StackFixture.Context(StackFixture.Subject(yawDegrees: 90f)));

            Assert.That(pose.Position, Is.EqualTo(new Vector3(2f, 0f, 0f)).Using(Vec(1e-3f)),
                "A subject facing +X should have the camera out along +X.");
        }

        [Test]
        public void WorldSpaceBinding_IgnoresWhichWayTheSubjectFaces()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FollowSubject);
            StackFixture.Offset(stack, new Vector3(0f, 0f, 2f));
            StackFixture.Binding(stack, BasisCameraBindingMode.WorldSpace);
            stack.follow.lateralTracking = 0f;

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(),
                StackFixture.Context(StackFixture.Subject(yawDegrees: 90f)));

            Assert.That(pose.Position, Is.EqualTo(new Vector3(0f, 0f, 2f)).Using(Vec(1e-3f)));
        }

        [Test]
        public void TheOffsetScalesWithTheAvatar()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FollowSubject);
            StackFixture.Offset(stack, new Vector3(0f, 0f, 2f));
            stack.follow.lateralTracking = 0f;

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(),
                StackFixture.Context(StackFixture.Subject(scale: 2f)));

            Assert.That(pose.Position.z, Is.EqualTo(4f).Within(1e-3f));
        }

        [Test]
        public void TheCameraTracksTheSubjectWhenTheyMove()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FollowSubject);
            StackFixture.Offset(stack, new Vector3(0f, 0f, 2f));
            stack.follow.lateralTracking = 0f;

            BasisCameraModifierState state = StackFixture.State();
            StackFixture.Settle(stack, state, StackFixture.Context());
            BasisCameraPose pose = StackFixture.Settle(stack, state,
                StackFixture.Context(StackFixture.Subject(new Vector3(10f, 0f, 0f))));

            Assert.That(pose.Position, Is.EqualTo(new Vector3(10f, 0f, 2f)).Using(Vec(1e-3f)));
        }

        [Test]
        public void DampingMakesTheCameraLagRatherThanTeleport()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FollowSubject);
            StackFixture.Offset(stack, new Vector3(0f, 0f, 2f));
            StackFixture.Damping(stack, new Vector3(0.5f, 0.5f, 0.5f));
            stack.follow.lateralTracking = 0f;

            BasisCameraModifierState state = StackFixture.State();
            state.Seed(Vector3.zero, Quaternion.identity, 40f);

            BasisCameraPose first = BasisCameraModifierSolver.Solve(stack, state, StackFixture.Context());

            Assert.That(first.Position.z, Is.GreaterThan(0f), "It should have started moving.");
            Assert.That(first.Position.z, Is.LessThan(2f), "One damped frame must not arrive.");
        }

        [Test]
        public void AJumpFurtherThanTheSnapDistanceCuts()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FollowSubject);
            StackFixture.Offset(stack, new Vector3(0f, 0f, 2f));
            StackFixture.Damping(stack, new Vector3(2f, 2f, 2f));
            stack.follow.lateralTracking = 0f;
            stack.follow.teleportDistance = 10f;

            BasisCameraModifierState state = StackFixture.State();
            state.Seed(Vector3.zero, Quaternion.identity, 40f);

            BasisCameraPose pose = BasisCameraModifierSolver.Solve(stack, state,
                StackFixture.Context(StackFixture.Subject(new Vector3(500f, 0f, 0f))));

            Assert.That(pose.Position, Is.EqualTo(new Vector3(500f, 0f, 2f)).Using(Vec(1e-3f)),
                "Past the snap distance the camera jumps instead of sweeping the map.");
        }

        [Test]
        public void ALockedOffCameraIgnoresTheSubjectEntirely()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.LockedOff);

            BasisCameraModifierState state = StackFixture.State();
            state.Seed(new Vector3(3f, 2f, 1f), Quaternion.identity, 40f);

            BasisCameraPose pose = StackFixture.Settle(stack, state,
                StackFixture.Context(StackFixture.Subject(new Vector3(50f, 0f, 50f))));

            Assert.That(pose.Position, Is.EqualTo(new Vector3(3f, 2f, 1f)).Using(Vec(1e-4f)));
        }

        [Test]
        public void LateralTrackingClosesTheSideGapWhileTheSubjectStrafes()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FollowSubject);
            StackFixture.Offset(stack, new Vector3(2f, 0f, 2f));
            stack.follow.lateralTracking = 1f;

            BasisCameraModifierState state = StackFixture.State();
            BasisCameraSubject subject = StackFixture.Subject();

            // Walk the subject sideways for long enough that the filtered speed saturates.
            float sideOffset = 0f;
            for (int Frame = 0; Frame < 240; Frame++)
            {
                sideOffset += 4f / 60f;
                subject.AnchorPos = new Vector3(sideOffset, 0f, 0f);
                BasisCameraModifierSolver.Solve(stack, state, StackFixture.Context(subject));
            }

            Assert.That(state.SmoothedLateralSpeed, Is.GreaterThan(1f),
                "The strafe should have registered as lateral speed.");
        }

        [Test]
        public void StandingStillLeavesTheAuthoredSideOffsetAlone()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FollowSubject);
            StackFixture.Offset(stack, new Vector3(2f, 0f, 2f));
            stack.follow.lateralTracking = 1f;

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(), StackFixture.Context());

            Assert.That(pose.Position.x, Is.EqualTo(2f).Within(1e-3f));
        }

        [Test]
        public void AnInvalidSubjectHoldsTheCameraWhereItWas()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FollowSubject);

            BasisCameraModifierState state = StackFixture.State();
            state.Seed(new Vector3(1f, 2f, 3f), Quaternion.identity, 40f);

            BasisCameraSolveContext context = StackFixture.Context();
            context.Subject = default;

            BasisCameraPose pose = BasisCameraModifierSolver.Solve(stack, state, context);

            Assert.That(pose.Position, Is.EqualTo(new Vector3(1f, 2f, 3f)).Using(Vec(1e-4f)));
        }

        internal static IEqualityComparer<Vector3> Vec(float tolerance) => new VectorComparer(tolerance);

        private sealed class VectorComparer : IEqualityComparer<Vector3>
        {
            private readonly float _tolerance;
            public VectorComparer(float tolerance) => _tolerance = tolerance;
            public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= _tolerance;
            public int GetHashCode(Vector3 v) => v.GetHashCode();
        }
    }

    public class BasisCameraFramingModifierTests
    {
        [Test]
        public void TheCameraSitsAtTheDistanceThatHoldsTheSubjectAtTheRequestedSize()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FrameSubject);
            StackFixture.Offset(stack, new Vector3(0f, 0f, 3f));
            stack.framing.screenFraction = 0.35f;
            stack.framing.minDistance = 0.1f;
            stack.framing.maxDistance = 50f;

            BasisCameraSolveContext context = StackFixture.Context();
            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(), context);

            float expected = BasisCameraFraming.DistanceToFit(0.45f, context.Fov, context.Aspect, 0.35f);
            Assert.That(pose.Position.magnitude, Is.EqualTo(expected).Within(1e-3f));
        }

        [Test]
        public void ABiggerSubjectPushesTheCameraBack()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FrameSubject);
            StackFixture.Offset(stack, new Vector3(0f, 0f, 3f));
            stack.framing.minDistance = 0.1f;
            stack.framing.maxDistance = 50f;

            BasisCameraSubject small = StackFixture.Subject();
            BasisCameraSubject big = StackFixture.Subject();
            big.Radius = 1.8f;

            float near = StackFixture.Settle(stack, StackFixture.State(), StackFixture.Context(small)).Position.magnitude;
            float far = StackFixture.Settle(stack, StackFixture.State(), StackFixture.Context(big)).Position.magnitude;

            Assert.That(far, Is.GreaterThan(near));
        }

        [Test]
        public void TheDistanceIsClampedBetweenTheAuthoredLimits()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FrameSubject);
            StackFixture.Offset(stack, new Vector3(0f, 0f, 3f));
            stack.framing.screenFraction = 0.01f;
            stack.framing.minDistance = 0.5f;
            stack.framing.maxDistance = 4f;

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(), StackFixture.Context());

            Assert.That(pose.Position.magnitude, Is.EqualTo(4f).Within(1e-3f));
        }

        [Test]
        public void ZoomFramingHoldsTheCameraStillAndChangesTheLensInstead()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FrameSubject);
            StackFixture.Offset(stack, new Vector3(0f, 0f, 3f));
            stack.framing.usesZoom = true;
            stack.framing.damping = Vector3.zero;

            BasisCameraSolveContext context = StackFixture.Context();
            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(), context);

            Assert.That(pose.Position, Is.EqualTo(new Vector3(0f, 0f, 3f))
                .Using(BasisCameraFollowModifierTests.Vec(1e-3f)),
                "Zoom framing must not dolly.");
            Assert.That(pose.Fov, Is.Not.EqualTo(context.Fov).Within(1e-3f), "The lens should have been driven.");
            Assert.That(stack.DrivesLens, Is.True);
        }

        [Test]
        public void ZeroOffsetDoesNotDivideByZero()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FrameSubject);
            StackFixture.Offset(stack, Vector3.zero);

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(), StackFixture.Context());

            Assert.That(float.IsNaN(pose.Position.x), Is.False);
            Assert.That(pose.Position, Is.EqualTo(Vector3.zero)
                .Using(BasisCameraFollowModifierTests.Vec(1e-4f)));
        }
    }

    public class BasisCameraOrbitModifierTests
    {
        [Test]
        public void TheCameraSitsOnTheRingTheVerticalAxisSelects()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.Orbit);
            stack.orbit.verticalAxis = 1f;
            stack.orbit.followSubjectHeading = false;
            stack.orbit.heading = 0f;

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(), StackFixture.Context());

            Assert.That(pose.Position.y, Is.EqualTo(stack.orbit.top.height).Within(1e-2f));
        }

        [Test]
        public void SweepingUpRaisesTheCamera()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.Orbit);
            stack.orbit.followSubjectHeading = false;

            stack.orbit.verticalAxis = 0f;
            float low = StackFixture.Settle(stack, StackFixture.State(), StackFixture.Context()).Position.y;

            stack.orbit.verticalAxis = 1f;
            float high = StackFixture.Settle(stack, StackFixture.State(), StackFixture.Context()).Position.y;

            Assert.That(high, Is.GreaterThan(low));
        }

        [Test]
        public void HeadingWalksTheCameraRoundTheSubject()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.Orbit);
            stack.orbit.followSubjectHeading = false;

            stack.orbit.heading = 0f;
            Vector3 front = StackFixture.Settle(stack, StackFixture.State(), StackFixture.Context()).Position;

            stack.orbit.heading = 180f;
            Vector3 back = StackFixture.Settle(stack, StackFixture.State(), StackFixture.Context()).Position;

            Assert.That(Vector3.Distance(front, back), Is.GreaterThan(1f),
                "Half a turn should put the camera on the other side.");
        }

        [Test]
        public void TheVerticalSweepIsDampedWhenAskedTo()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.Orbit);
            stack.orbit.followSubjectHeading = false;
            stack.orbit.verticalDamping = 1f;
            stack.orbit.verticalAxis = 1f;

            BasisCameraModifierState state = StackFixture.State();
            BasisCameraModifierSolver.Solve(stack, state, StackFixture.Context());

            Assert.That(state.VerticalAxis, Is.GreaterThan(0f), "It should have started sweeping.");
            Assert.That(state.VerticalAxis, Is.LessThan(1f), "One damped frame must not arrive.");
        }
    }

    public class BasisCameraDollyModifierTests
    {
        private static IReadOnlyList<Vector3> Track() => new List<Vector3>
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(5f, 0f, 0f),
            new Vector3(10f, 0f, 0f),
        };

        private static BasisCameraSolveContext TrackContext(BasisCameraSubject subject, bool looped = false)
        {
            BasisCameraSolveContext context = StackFixture.Context(subject);
            context.DollyPoints = Track();
            context.DollyLooped = looped;
            return context;
        }

        [Test]
        public void TheCameraRidesToTheTrackPositionItWasGiven()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.DollyTrack);
            stack.dolly.autoTrack = false;
            stack.dolly.position = 1f;

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(), TrackContext(StackFixture.Subject()));

            Assert.That(pose.Position, Is.EqualTo(new Vector3(5f, 0f, 0f))
                .Using(BasisCameraFollowModifierTests.Vec(1e-2f)));
        }

        [Test]
        public void AutoTrackSlidesToWhicheverPartOfTheTrackIsNearestTheSubject()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.DollyTrack);
            stack.dolly.autoTrack = true;

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(),
                TrackContext(StackFixture.Subject(new Vector3(9.5f, 0f, 0f))));

            Assert.That(pose.Position.x, Is.GreaterThan(8f));
        }

        [Test]
        public void ATrackWithNoPointsHoldsTheCameraInsteadOfCollapsing()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.DollyTrack);

            BasisCameraModifierState state = StackFixture.State();
            state.Seed(new Vector3(1f, 2f, 3f), Quaternion.identity, 40f);

            BasisCameraSolveContext context = StackFixture.Context();
            context.DollyPoints = new List<Vector3>();

            BasisCameraPose pose = BasisCameraModifierSolver.Solve(stack, state, context);

            Assert.That(pose.Position, Is.EqualTo(new Vector3(1f, 2f, 3f))
                .Using(BasisCameraFollowModifierTests.Vec(1e-4f)));
        }

        [Test]
        public void ANullTrackIsHandledLikeAnEmptyOne()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.DollyTrack);

            BasisCameraModifierState state = StackFixture.State();
            state.Seed(new Vector3(4f, 5f, 6f), Quaternion.identity, 40f);

            Assert.That(() => BasisCameraModifierSolver.Solve(stack, state, StackFixture.Context()), Throws.Nothing);
            Assert.That(state.Position, Is.EqualTo(new Vector3(4f, 5f, 6f))
                .Using(BasisCameraFollowModifierTests.Vec(1e-4f)));
        }

        [Test]
        public void TrackSpeedCarriesTheCameraAlongOverTime()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.DollyTrack);
            stack.dolly.autoTrack = false;
            stack.dolly.speed = 2f;

            BasisCameraModifierState state = StackFixture.State();
            BasisCameraSolveContext context = TrackContext(StackFixture.Subject());

            for (int Frame = 0; Frame < 60; Frame++)
            {
                BasisCameraModifierSolver.Solve(stack, state, context);
            }

            Assert.That(state.DollyPosition, Is.GreaterThan(0f));
        }

        [Test]
        public void TheDollyPositionIsClampedToTheTrackOnAnOpenPath()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.DollyTrack);
            stack.dolly.autoTrack = false;
            stack.dolly.position = 99f;

            BasisCameraModifierState state = StackFixture.State();
            StackFixture.Settle(stack, state, TrackContext(StackFixture.Subject()));

            Assert.That(state.DollyPosition, Is.LessThanOrEqualTo(BasisCameraSpline.MaxPosition(3, false) + 1e-3f));
        }
    }

    public class BasisCameraOcclusionEffectTests
    {
        private static BasisCameraSolveContext WithProbe(BasisCameraSolveContext context, float freeDistance)
        {
            context.OcclusionProbe = (Vector3 target, Vector3 desired, out float free) =>
            {
                free = freeDistance;
                return true;
            };
            return context;
        }

        private static BasisCameraModifierStack OccludedStack()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FollowSubject);
            StackFixture.Offset(stack, new Vector3(0f, 0f, 4f));
            stack.follow.lateralTracking = 0f;
            stack.occlusion.padding = 0.25f;
            stack.occlusion.minDistance = 0.4f;
            stack.occlusion.returnDamping = 0f;
            stack.AddEffect(BasisCameraEffectModifier.AvoidOcclusion);
            return stack;
        }

        [Test]
        public void AWallPullsTheCameraInToSitInFrontOfIt()
        {
            BasisCameraModifierStack stack = OccludedStack();
            BasisCameraSolveContext context = WithProbe(StackFixture.Context(), 2f);

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(), context);

            float distance = Vector3.Distance(pose.Position, context.Subject.LookPoint);
            Assert.That(distance, Is.EqualTo(2f - 0.25f).Within(1e-2f));
        }

        [Test]
        public void OcclusionIsIgnoredEntirelyWhenTheStackDoesNotAskForIt()
        {
            BasisCameraModifierStack stack = OccludedStack();
            stack.RemoveEffect(BasisCameraEffectModifier.AvoidOcclusion);

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(),
                WithProbe(StackFixture.Context(), 0.5f));

            Assert.That(pose.Position.z, Is.EqualTo(4f).Within(1e-2f));
        }

        [Test]
        public void TheCameraNeverPushesCloserThanTheMinimum()
        {
            BasisCameraModifierStack stack = OccludedStack();
            BasisCameraSolveContext context = WithProbe(StackFixture.Context(), 0.05f);

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(), context);

            float distance = Vector3.Distance(pose.Position, context.Subject.LookPoint);
            Assert.That(distance, Is.GreaterThanOrEqualTo(0.4f - 1e-3f));
        }

        [Test]
        public void OcclusionNeverPushesTheCameraFurtherOutThanAuthored()
        {
            BasisCameraModifierStack stack = OccludedStack();
            BasisCameraSolveContext context = WithProbe(StackFixture.Context(), 100f);

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(), context);

            float distance = Vector3.Distance(pose.Position, context.Subject.LookPoint);
            Assert.That(distance, Is.LessThanOrEqualTo(Vector3.Distance(new Vector3(0f, 0f, 4f),
                context.Subject.LookPoint) + 1e-3f));
        }

        [Test]
        public void ThePullInIsImmediateButTheReturnIsEased()
        {
            BasisCameraModifierStack stack = OccludedStack();
            stack.occlusion.returnDamping = 1f;

            BasisCameraModifierState state = StackFixture.State();
            StackFixture.Settle(stack, state, WithProbe(StackFixture.Context(), 1f));
            float pulledIn = state.OcclusionDistance;

            BasisCameraSolveContext clear = StackFixture.Context();
            clear.OcclusionProbe = (Vector3 target, Vector3 desired, out float free) =>
            {
                free = 0f;
                return false;
            };
            BasisCameraModifierSolver.Solve(stack, state, clear);

            Assert.That(state.OcclusionDistance, Is.GreaterThan(pulledIn), "It should be easing back out.");
            Assert.That(state.OcclusionDistance, Is.LessThan(4f), "One frame must not complete the return.");
        }

        [Test]
        public void AMissingProbeIsTreatedAsAClearShot()
        {
            BasisCameraModifierStack stack = OccludedStack();

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(), StackFixture.Context());

            Assert.That(pose.Position.z, Is.EqualTo(4f).Within(1e-2f));
        }
    }

    public class BasisCameraOperatorChannelTests
    {
        [Test]
        public void FreeFlyHandsThePositionChannelBackToTheOperator()
        {
            BasisCameraModifierStack stack = new BasisCameraModifierStack
            {
                positionModifier = BasisCameraPositionModifier.FreeFly,
                rotationModifier = BasisCameraRotationModifier.Hold,
            };

            BasisCameraSolveContext context = StackFixture.Context();
            context.OperatorPosition = new Vector3(7f, 8f, 9f);

            BasisCameraPose pose = BasisCameraModifierSolver.Solve(stack, StackFixture.State(), context);

            Assert.That(pose.Position, Is.EqualTo(new Vector3(7f, 8f, 9f))
                .Using(BasisCameraFollowModifierTests.Vec(1e-4f)));
        }

        [Test]
        public void FreeLookHandsTheRotationChannelBackToTheOperator()
        {
            BasisCameraModifierStack stack = new BasisCameraModifierStack
            {
                positionModifier = BasisCameraPositionModifier.LockedOff,
                rotationModifier = BasisCameraRotationModifier.FreeLook,
            };

            BasisCameraSolveContext context = StackFixture.Context();
            context.OperatorRotation = BasisCameraDamping.Yaw(90f);

            BasisCameraPose pose = BasisCameraModifierSolver.Solve(stack, StackFixture.State(), context);

            Assert.That(Quaternion.Angle(pose.Rotation, BasisCameraDamping.Yaw(90f)), Is.LessThan(0.01f));
        }

        [Test]
        public void AStackCanTakeOneChannelAndLeaveTheOther()
        {
            // The combination the two slots exist to make possible: fly the camera by hand while
            // something else keeps it pointed. The old design had no way to express this.
            BasisCameraModifierStack stack = new BasisCameraModifierStack
            {
                positionModifier = BasisCameraPositionModifier.FreeFly,
                rotationModifier = BasisCameraRotationModifier.MatchSubject,
            };
            stack.matchSubject.damping = Vector3.zero;

            Assert.That(stack.DrivesPosition, Is.False);
            Assert.That(stack.DrivesRotation, Is.True);
            Assert.That(stack.DrivesAnything, Is.True);

            BasisCameraSolveContext context = StackFixture.Context(StackFixture.Subject(yawDegrees: 90f));
            context.OperatorPosition = new Vector3(1f, 2f, 3f);

            BasisCameraPose pose = BasisCameraModifierSolver.Solve(stack, StackFixture.State(), context);

            Assert.That(pose.Position, Is.EqualTo(new Vector3(1f, 2f, 3f))
                .Using(BasisCameraFollowModifierTests.Vec(1e-4f)));
        }

        [Test]
        public void HoldKeepsWhateverRotationItAlreadyHad()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.LockedOff);

            BasisCameraModifierState state = StackFixture.State();
            state.Seed(Vector3.zero, BasisCameraDamping.Yaw(45f), 40f);

            BasisCameraSolveContext context = StackFixture.Context();
            context.OperatorRotation = BasisCameraDamping.Yaw(-120f);

            BasisCameraPose pose = BasisCameraModifierSolver.Solve(stack, state, context);

            Assert.That(Quaternion.Angle(pose.Rotation, BasisCameraDamping.Yaw(45f)), Is.LessThan(0.01f));
        }

        [Test]
        public void ANullStackFallsBackToTheOperatorPose()
        {
            BasisCameraSolveContext context = StackFixture.Context();
            context.OperatorPosition = new Vector3(2f, 3f, 4f);

            BasisCameraPose pose = BasisCameraModifierSolver.Solve(null, null, context);

            Assert.That(pose.Position, Is.EqualTo(new Vector3(2f, 3f, 4f))
                .Using(BasisCameraFollowModifierTests.Vec(1e-4f)));
        }
    }

    public class BasisCameraLensEffectTests
    {
        [Test]
        public void TheLensOverrideDrivesTheFieldOfView()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.LockedOff);
            stack.lens.fov = 90f;
            stack.lens.damping = 0f;
            stack.AddEffect(BasisCameraEffectModifier.LensOverride);

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(), StackFixture.Context());

            Assert.That(stack.DrivesLens, Is.True);
            Assert.That(pose.Fov, Is.EqualTo(90f).Within(1e-3f));
        }

        [Test]
        public void WithoutTheOverrideTheOperatorsLensIsLeftAlone()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.LockedOff);
            stack.lens.fov = 90f;

            BasisCameraSolveContext context = StackFixture.Context();
            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(), context);

            Assert.That(stack.DrivesLens, Is.False);
            Assert.That(pose.Fov, Is.EqualTo(context.Fov).Within(1e-3f));
        }
    }

    public class BasisCameraShakeEffectTests
    {
        [Test]
        public void ShakeIsAppliedToTheOutputWithoutAccumulatingIntoTheSolveState()
        {
            // The wander has to be an offset on the finished pose, never folded back into the pose
            // the next frame continues from. Accumulated, it random-walks the camera away from
            // wherever it was put — which is exactly what a hand-flown camera with shake fitted is.
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.LockedOff);
            stack.shake = BasisCameraNoiseSettings.ForProfile(BasisCameraNoiseProfile.Shaky);
            stack.AddEffect(BasisCameraEffectModifier.Shake);

            BasisCameraModifierState state = StackFixture.State();
            state.Seed(new Vector3(5f, 1f, 5f), Quaternion.identity, 40f);

            BasisCameraSolveContext context = StackFixture.Context();
            for (int Frame = 0; Frame < 600; Frame++)
            {
                context.Time = Frame / 60f;
                BasisCameraModifierSolver.Solve(stack, state, context);
            }

            Assert.That(state.Position, Is.EqualTo(new Vector3(5f, 1f, 5f))
                .Using(BasisCameraFollowModifierTests.Vec(1e-4f)),
                "Ten seconds of shake moved the pose it is meant to be an offset from.");
        }

        [Test]
        public void ShakeMovesTheOutputItLeavesTheStateAlone()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.LockedOff);
            stack.shake = BasisCameraNoiseSettings.ForProfile(BasisCameraNoiseProfile.Shaky);
            stack.AddEffect(BasisCameraEffectModifier.Shake);

            BasisCameraModifierState state = StackFixture.State();
            state.Seed(new Vector3(5f, 1f, 5f), Quaternion.identity, 40f);

            BasisCameraSolveContext context = StackFixture.Context();
            context.Time = 3.7f;

            BasisCameraPose pose = BasisCameraModifierSolver.Solve(stack, state, context);

            Assert.That(Vector3.Distance(pose.Position, state.Position), Is.GreaterThan(0f),
                "A shaky profile should have moved the output.");
        }
    }

    public class BasisCameraLookAheadEffectTests
    {
        [Test]
        public void LookAheadLeadsAMovingSubject()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FollowSubject);
            StackFixture.Offset(stack, new Vector3(0f, 0f, 2f));
            stack.follow.lateralTracking = 0f;
            stack.lookAhead.time = 0.5f;
            stack.lookAhead.limit = 10f;
            stack.AddEffect(BasisCameraEffectModifier.LookAhead);

            BasisCameraSubject subject = StackFixture.Subject();
            subject.Velocity = new Vector3(4f, 0f, 0f);

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(), StackFixture.Context(subject));

            Assert.That(pose.Position.x, Is.GreaterThan(1f), "The camera should be led ahead of the subject.");
        }

        [Test]
        public void WithoutTheEffectTheSubjectsVelocityIsIgnored()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FollowSubject);
            StackFixture.Offset(stack, new Vector3(0f, 0f, 2f));
            stack.follow.lateralTracking = 0f;

            BasisCameraSubject subject = StackFixture.Subject();
            subject.Velocity = new Vector3(4f, 0f, 0f);

            BasisCameraPose pose = StackFixture.Settle(stack, StackFixture.State(), StackFixture.Context(subject));

            Assert.That(pose.Position.x, Is.EqualTo(0f).Within(1e-3f));
        }
    }

    public class BasisCameraModifierStateTests
    {
        [Test]
        public void Seed_ContinuesFromTheHandOffPoseRatherThanCuttingToTheSolve()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FollowSubject);
            StackFixture.Offset(stack, new Vector3(0f, 0f, 2f));
            StackFixture.Damping(stack, new Vector3(1f, 1f, 1f));
            stack.follow.lateralTracking = 0f;
            stack.follow.teleportDistance = 100f;

            BasisCameraModifierState state = StackFixture.State();
            state.Seed(new Vector3(0f, 0f, 20f), Quaternion.identity, 40f);

            BasisCameraPose first = BasisCameraModifierSolver.Solve(stack, state, StackFixture.Context());

            Assert.That(first.Position.z, Is.LessThan(20f), "It should have started easing in.");
            Assert.That(first.Position.z, Is.GreaterThan(2f), "It must not have arrived in one frame.");
        }

        [Test]
        public void Reseed_RederivesFromTheSubjectRatherThanEasingAcrossTheMap()
        {
            BasisCameraModifierStack stack = StackFixture.PositionOnly(BasisCameraPositionModifier.FollowSubject);
            StackFixture.Offset(stack, new Vector3(0f, 0f, 2f));
            StackFixture.Damping(stack, new Vector3(1f, 1f, 1f));
            stack.follow.lateralTracking = 0f;

            BasisCameraModifierState state = StackFixture.State();
            state.Seed(new Vector3(0f, 0f, 500f), Quaternion.identity, 40f);
            state.Reseed();

            BasisCameraSolveContext context = StackFixture.Context();
            context.OperatorPosition = Vector3.zero;

            BasisCameraPose first = BasisCameraModifierSolver.Solve(stack, state, context);

            Assert.That(first.Position.z, Is.LessThan(10f),
                "A reseed drops the old pose, so the camera must not sweep in from 500 metres away.");
        }

        [Test]
        public void Seed_ClearsTheStrafeHistory()
        {
            BasisCameraModifierState state = StackFixture.State();
            state.LastAnchor = new Vector3(100f, 0f, 0f);
            state.HasLastAnchor = true;
            state.SmoothedLateralSpeed = 42f;

            state.Seed(Vector3.zero, Quaternion.identity, 40f);

            Assert.That(state.HasLastAnchor, Is.False);
            Assert.That(state.SmoothedLateralSpeed, Is.EqualTo(0f));
        }

        [Test]
        public void ResetSubjectHistory_DropsTheStrafeHistoryWithoutMovingTheCamera()
        {
            BasisCameraModifierState state = StackFixture.State();
            state.Seed(new Vector3(1f, 2f, 3f), Quaternion.identity, 40f);
            state.SmoothedLateralSpeed = 9f;
            state.HasLastAnchor = true;

            state.ResetSubjectHistory();

            Assert.That(state.Position, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(state.HasLastAnchor, Is.False);
            Assert.That(state.SmoothedLateralSpeed, Is.EqualTo(0f));
        }
    }
}
