using Basis.Cinematics;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// The cinematic rig's maths. Every solver here is a pure static precisely so it can be
    /// asserted on without a scene, a camera or a player — the same reason
    /// <c>ComputeDesktopFitScale</c> was extracted. Framing errors are invisible in review and
    /// obvious the moment someone films with it.
    /// </summary>
    public class BasisCameraDampingTests
    {
        [Test]
        public void Damping_RemovesNearlyAllOfTheResidualAfterOneDampTime()
        {
            float remaining = 1f;
            const float dampTime = 0.5f;
            const float step = 1f / 240f;

            for (float elapsed = 0f; elapsed < dampTime; elapsed += step)
            {
                remaining -= BasisCameraDamping.Damp(remaining, dampTime, step);
            }

            Assert.That(remaining, Is.EqualTo(0.01f).Within(0.004f),
                "A damp time is defined as the point where 99% of the residual is gone.");
        }

        [Test]
        public void Damping_LandsInTheSamePlaceAtAnyFramerate()
        {
            const float dampTime = 0.4f;
            const float duration = 1f;

            float slow = Integrate(dampTime, duration, 1f / 30f);
            float fast = Integrate(dampTime, duration, 1f / 240f);

            Assert.That(slow, Is.EqualTo(fast).Within(0.01f),
                "Frame-rate dependent damping would make the same shot read differently on different machines.");
        }

        private static float Integrate(float dampTime, float duration, float step)
        {
            float current = 0f;
            for (float elapsed = 0f; elapsed < duration; elapsed += step)
            {
                current = BasisCameraDamping.Approach(current, 1f, dampTime, step);
            }
            return current;
        }

        [Test]
        public void ZeroDampTime_Snaps()
        {
            Assert.That(BasisCameraDamping.Approach(0f, 5f, 0f, 1f / 60f), Is.EqualTo(5f).Within(1e-5f));
        }

        [Test]
        public void ZeroDeltaTime_DoesNotMove()
        {
            Assert.That(BasisCameraDamping.Approach(2f, 9f, 0.5f, 0f), Is.EqualTo(2f).Within(1e-6f),
                "A paused frame must not advance a solve, or a hitch reads as a jump.");
        }

        [Test]
        public void PerAxisDamping_MovesEachAxisAtItsOwnRate()
        {
            Vector3 result = BasisCameraDamping.Damp(Vector3.one, new Vector3(0f, 0.5f, 100f), 1f / 60f);

            Assert.That(result.x, Is.EqualTo(1f).Within(1e-5f), "Zero damp time snaps.");
            Assert.That(result.y, Is.GreaterThan(result.z), "A shorter damp time must catch up faster.");
            Assert.That(result.z, Is.GreaterThan(0f).And.LessThan(0.01f), "A long damp time barely moves in one frame.");
        }

        [Test]
        public void NormalizeAngle_TakesTheShortWayRound()
        {
            Assert.That(BasisCameraDamping.NormalizeAngle(350f), Is.EqualTo(-10f).Within(1e-4f));
            Assert.That(BasisCameraDamping.NormalizeAngle(-350f), Is.EqualTo(10f).Within(1e-4f));
            Assert.That(BasisCameraDamping.NormalizeAngle(180f), Is.EqualTo(180f).Within(1e-4f));
        }
    }

    public class BasisCameraComposerTests
    {
        private const float Fov = 50f;
        private const float Aspect = 16f / 9f;

        [Test]
        public void ScreenPoint_IsCentredWhenTheTargetIsDeadAhead()
        {
            bool ok = BasisCameraComposer.TryGetScreenPoint(Vector3.zero, Quaternion.identity,
                Vector3.forward * 5f, Fov, Aspect, out Vector2 point);

            Assert.That(ok, Is.True);
            Assert.That(point.x, Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(point.y, Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void ScreenPoint_ReportsFailureForATargetBehindTheCamera()
        {
            bool ok = BasisCameraComposer.TryGetScreenPoint(Vector3.zero, Quaternion.identity,
                Vector3.back * 5f, Fov, Aspect, out _);

            Assert.That(ok, Is.False, "A target behind the lens has no screen position; composing on one would flip the shot.");
        }

        [Test]
        public void RotationForScreenPoint_PutsTheTargetExactlyWhereItWasAsked()
        {
            Vector3 cameraPos = new Vector3(1f, 2f, -3f);
            Vector3 target = new Vector3(4f, 1.5f, 6f);
            Vector2 wanted = new Vector2(0.3f, 0.7f);

            Quaternion rotation = BasisCameraComposer.RotationForScreenPoint(cameraPos, target, wanted, Fov, Aspect, Vector3.up);
            bool ok = BasisCameraComposer.TryGetScreenPoint(cameraPos, rotation, target, Fov, Aspect, out Vector2 actual);

            Assert.That(ok, Is.True);
            Assert.That(actual.x, Is.EqualTo(wanted.x).Within(1e-3f));
            Assert.That(actual.y, Is.EqualTo(wanted.y).Within(1e-3f));
        }

        [Test]
        public void DeadZone_HoldsStillWhileTheSubjectIsInsideIt()
        {
            // Subject sits 0.05 off centre; the dead zone is 0.3 wide, so it is comfortably inside.
            float result = BasisCameraComposer.SolveAxis(0.55f, 0.5f, 0.15f, 0.5f, 0.4f, 0.5f, 1f / 60f);

            Assert.That(result, Is.EqualTo(0.55f).Within(1e-5f),
                "Inside the dead zone the camera must not react at all, or it micro-jitters on every breath.");
        }

        [Test]
        public void OutsideTheDeadZone_TheSubjectIsEasedBackTowardTheEdge()
        {
            float result = BasisCameraComposer.SolveAxis(0.9f, 0.5f, 0.1f, 0.5f, 0.5f, 0.4f, 1f / 60f);

            Assert.That(result, Is.LessThan(0.9f), "It must move back toward frame.");
            Assert.That(result, Is.GreaterThan(0.6f), "But ease, not snap to the dead zone edge in one frame.");
        }

        [Test]
        public void SoftZoneEdge_IsAHardLimitTheSubjectCannotCross()
        {
            // Way outside, and damping so slow that easing alone would leave it far out of frame.
            float result = BasisCameraComposer.SolveAxis(5f, 0.5f, 0.1f, 0.5f, 0.3f, 100f, 1f / 60f);

            Assert.That(result, Is.EqualTo(0.8f).Within(1e-4f),
                "At the soft zone edge the camera keeps up exactly, whatever the damping says.");
        }

        [Test]
        public void SoftZoneNeverShrinksInsideTheDeadZone()
        {
            // Soft zone authored smaller than the dead zone. Without the invariant the hard limit
            // would clamp to 0.55 - back inside the region the camera is deliberately not reacting
            // in, so the subject would be dragged by a zone that is supposed to leave it alone.
            float result = BasisCameraComposer.SolveAxis(0.9f, 0.5f, 0.2f, 0.5f, 0.05f, 0f, 1f / 60f);

            Assert.That(result, Is.EqualTo(0.7f).Within(1e-4f));
        }

        [Test]
        public void ABiasedSoftZoneStillCannotCutIntoTheDeadZone()
        {
            // Soft zone pushed well to the right of the dead zone. Its left edge lands at 0.45,
            // inside the free region [0.4, 0.6] - clamping there would drag a subject the camera
            // had deliberately decided not to react to.
            float result = BasisCameraComposer.SolveAxis(0.42f, 0.5f, 0.1f, 0.8f, 0.35f, 0f, 1f / 60f);

            Assert.That(result, Is.EqualTo(0.42f).Within(1e-4f));
        }

        [Test]
        public void ABiasedSoftZoneStillLimitsOnItsOwnFarEdge()
        {
            float result = BasisCameraComposer.SolveAxis(5f, 0.5f, 0.1f, 0.8f, 0.35f, 100f, 1f / 60f);

            Assert.That(result, Is.EqualTo(1.15f).Within(1e-4f),
                "Widening the limit to cover the dead zone must not stop it limiting the other way.");
        }

        [Test]
        public void TheDrawnLimitIsExactlyTheLimitTheSolveEnforces()
        {
            // The on-screen guide draws GetEffectiveLimits; the solve clamps to it. If they ever
            // disagree the guide is showing framing the camera will not honour.
            float[] deadCentres = { 0.5f, 0.3f, 0.75f };
            float[] deadHalves = { 0f, 0.1f, 0.25f };
            float[] biases = { 0f, 0.2f, -0.3f };
            float[] softHalves = { 0f, 0.05f, 0.35f, 0.6f };

            foreach (float deadCentre in deadCentres)
            {
                foreach (float deadHalf in deadHalves)
                {
                    foreach (float bias in biases)
                    {
                        foreach (float softHalf in softHalves)
                        {
                            BasisCameraComposer.GetEffectiveLimits(
                                deadCentre, deadHalf, deadCentre + bias, softHalf,
                                out float low, out float high);

                            // Damping of 0 makes the solve land on the dead-zone edge, so anything
                            // further out is the clamp speaking and must stop at the drawn limit.
                            float pushedHigh = BasisCameraComposer.SolveAxis(
                                deadCentre + 50f, deadCentre, deadHalf, deadCentre + bias, softHalf, 1000f, 1f / 60f);
                            float pushedLow = BasisCameraComposer.SolveAxis(
                                deadCentre - 50f, deadCentre, deadHalf, deadCentre + bias, softHalf, 1000f, 1f / 60f);

                            string context = $"dead {deadCentre}+-{deadHalf}, bias {bias}, soft half {softHalf}";
                            Assert.That(pushedHigh, Is.EqualTo(high).Within(1e-4f), context);
                            Assert.That(pushedLow, Is.EqualTo(low).Within(1e-4f), context);
                        }
                    }
                }
            }
        }

        [Test]
        public void TheDrawnLimitAlwaysContainsTheDeadZone()
        {
            BasisCameraComposer.GetEffectiveLimits(0.5f, 0.2f, 0.9f, 0.05f, out float low, out float high);

            Assert.That(low, Is.LessThanOrEqualTo(0.3f + 1e-4f));
            Assert.That(high, Is.GreaterThanOrEqualTo(0.7f - 1e-4f));
        }

        [Test]
        public void ANegativeSoftZoneIsTreatedAsNone()
        {
            float result = BasisCameraComposer.SolveAxis(0.9f, 0.5f, 0.1f, 0.5f, -3f, 0f, 1f / 60f);

            Assert.That(result, Is.EqualTo(0.6f).Within(1e-4f), "It collapses onto the dead zone edge, not past it.");
        }

        [Test]
        public void Solve_CentresTheSubjectWhenConfiguredAsAHardLookAt()
        {
            Vector3 cameraPos = Vector3.zero;
            Vector3 target = new Vector3(3f, 1f, 4f);

            Quaternion rotation = BasisCameraComposer.Solve(cameraPos, Quaternion.identity, target,
                Fov, Aspect, BasisComposerSettings.HardLookAt, Vector3.up, 1f / 60f);

            BasisCameraComposer.TryGetScreenPoint(cameraPos, rotation, target, Fov, Aspect, out Vector2 point);

            Assert.That(point.x, Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(point.y, Is.EqualTo(0.5f).Within(1e-3f));
        }

        [Test]
        public void LookAhead_LeadsAMovingSubjectAndRespectsItsLimit()
        {
            Vector3 led = BasisCameraComposer.ApplyLookAhead(Vector3.zero, Vector3.right * 4f, 0.5f, 10f);
            Assert.That(led.x, Is.EqualTo(2f).Within(1e-4f));

            Vector3 clamped = BasisCameraComposer.ApplyLookAhead(Vector3.zero, Vector3.right * 100f, 0.5f, 3f);
            Assert.That(clamped.magnitude, Is.EqualTo(3f).Within(1e-4f),
                "Without a limit a sprint or a teleport would throw the aim point across the map.");
        }

        [Test]
        public void LookAhead_IsOffAtZeroTime()
        {
            Vector3 result = BasisCameraComposer.ApplyLookAhead(Vector3.one, Vector3.right * 9f, 0f, 5f);
            Assert.That(result, Is.EqualTo(Vector3.one));
        }
    }

    public class BasisCameraFramingTests
    {
        [Test]
        public void DistanceToFit_PutsTheSubjectAtTheRequestedShareOfTheFrame()
        {
            const float radius = 0.5f;
            const float fov = 40f;
            const float fraction = 0.25f;

            float distance = BasisCameraFraming.DistanceToFit(radius, fov, 16f / 9f, fraction);
            float halfHeight = distance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);

            Assert.That(radius / halfHeight, Is.EqualTo(fraction).Within(1e-3f));
        }

        [Test]
        public void DistanceToFit_MovesBackForABiggerSubject()
        {
            float near = BasisCameraFraming.DistanceToFit(0.4f, 40f, 1.78f, 0.3f);
            float far = BasisCameraFraming.DistanceToFit(2f, 40f, 1.78f, 0.3f);

            Assert.That(far, Is.GreaterThan(near));
        }

        [Test]
        public void FovToFit_IsTheInverseOfDistanceToFit()
        {
            const float radius = 0.6f;
            const float fraction = 0.3f;
            const float fov = 45f;

            float distance = BasisCameraFraming.DistanceToFit(radius, fov, 1f, fraction);
            float recovered = BasisCameraFraming.FovToFit(radius, distance, fraction);

            Assert.That(recovered, Is.EqualTo(fov).Within(0.5f));
        }

        [Test]
        public void DegenerateInput_AsksForNoDistance()
        {
            Assert.That(BasisCameraFraming.DistanceToFit(0f, 40f, 1.78f, 0.3f), Is.EqualTo(0f));
            Assert.That(BasisCameraFraming.DistanceToFit(1f, 0f, 1.78f, 0.3f), Is.EqualTo(0f));
            Assert.That(BasisCameraFraming.FovToFit(1f, 0f, 0.3f), Is.EqualTo(0f));
        }

        [Test]
        public void GroupBounds_EnclosesEveryMember()
        {
            Vector3[] positions = { new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(0f, 0f, 3f) };
            float[] radii = { 0.4f, 0.4f, 0.4f };

            bool ok = BasisCameraFraming.TryGetGroupBounds(positions, radii, null, out Vector3 centre, out float radius);

            Assert.That(ok, Is.True);
            for (int Index = 0; Index < positions.Length; Index++)
            {
                Assert.That(Vector3.Distance(centre, positions[Index]) + radii[Index], Is.LessThanOrEqualTo(radius + 1e-4f),
                    "A member outside the bounding sphere would be framed out of shot.");
            }
        }

        [Test]
        public void GroupBounds_LeansTowardTheHeavilyWeightedMember()
        {
            Vector3[] positions = { new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f) };
            float[] weights = { 9f, 1f };

            BasisCameraFraming.TryGetGroupBounds(positions, null, weights, out Vector3 centre, out _);

            Assert.That(centre.x, Is.LessThan(-5f));
        }

        [Test]
        public void GroupBounds_IgnoresZeroWeightMembersEntirely()
        {
            Vector3[] positions = { Vector3.zero, new Vector3(1000f, 0f, 0f) };
            float[] weights = { 1f, 0f };

            BasisCameraFraming.TryGetGroupBounds(positions, null, weights, out Vector3 centre, out float radius);

            Assert.That(centre, Is.EqualTo(Vector3.zero));
            Assert.That(radius, Is.LessThan(1f), "A dropped member must not stretch the bounds it was dropped from.");
        }

        [Test]
        public void GroupBounds_FailsWhenNothingIsLeftToFrame()
        {
            Assert.That(BasisCameraFraming.TryGetGroupBounds(new Vector3[0], null, null, out _, out _), Is.False);
            Assert.That(BasisCameraFraming.TryGetGroupBounds(null, null, null, out _, out _), Is.False);
        }

        [Test]
        public void PullIn_SitsJustInFrontOfWhateverBlockedTheShot()
        {
            Vector3 target = Vector3.zero;
            Vector3 desired = new Vector3(0f, 0f, 10f);

            Vector3 result = BasisCameraFraming.PullIn(target, desired, 4f, 0.5f, 0.4f);

            Assert.That(result.z, Is.EqualTo(3.5f).Within(1e-4f));
        }

        [Test]
        public void PullIn_NeverPushesPastTheShotItWasAskedFor()
        {
            Vector3 result = BasisCameraFraming.PullIn(Vector3.zero, new Vector3(0f, 0f, 2f), 50f, 0.5f, 0.4f);

            Assert.That(result.z, Is.EqualTo(2f).Within(1e-4f),
                "A clear line of sight must leave the framing exactly as the shot authored it.");
        }

        [Test]
        public void PullIn_StopsAtTheMinimumRatherThanEnteringTheSubject()
        {
            Vector3 result = BasisCameraFraming.PullIn(Vector3.zero, new Vector3(0f, 0f, 10f), 0.1f, 0.5f, 0.6f);

            Assert.That(result.z, Is.EqualTo(0.6f).Within(1e-4f));
        }
    }

    public class BasisCameraSplineTests
    {
        private static readonly Vector3[] Track =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(2f, 1f, 0f),
            new Vector3(4f, 0f, 2f),
            new Vector3(0f, 0f, 4f),
        };

        [Test]
        public void ThePathVisitsEveryWaypointItWasGiven()
        {
            for (int Index = 0; Index < Track.Length; Index++)
            {
                Vector3 onPath = BasisCameraSpline.Evaluate(Track, Index, false);
                Assert.That(Vector3.Distance(onPath, Track[Index]), Is.LessThan(1e-4f),
                    "A hand-placed point the camera does not actually reach makes the track unusable.");
            }
        }

        [Test]
        public void MaxPosition_CountsSegmentsNotPoints()
        {
            Assert.That(BasisCameraSpline.MaxPosition(4, false), Is.EqualTo(3f));
            Assert.That(BasisCameraSpline.MaxPosition(4, true), Is.EqualTo(4f), "Looping adds the closing segment.");
            Assert.That(BasisCameraSpline.MaxPosition(1, false), Is.EqualTo(0f));
        }

        [Test]
        public void LoopedPositions_WrapInsteadOfClamping()
        {
            float wrapped = BasisCameraSpline.NormalizePosition(4.25f, 4, true);
            Assert.That(wrapped, Is.EqualTo(0.25f).Within(1e-4f));

            float negative = BasisCameraSpline.NormalizePosition(-0.5f, 4, true);
            Assert.That(negative, Is.EqualTo(3.5f).Within(1e-4f));
        }

        [Test]
        public void OpenPositions_ClampToTheEnds()
        {
            Assert.That(BasisCameraSpline.NormalizePosition(99f, 4, false), Is.EqualTo(3f));
            Assert.That(BasisCameraSpline.NormalizePosition(-99f, 4, false), Is.EqualTo(0f));
        }

        [Test]
        public void ClosestPosition_FindsTheWaypointItIsSittingOn()
        {
            float position = BasisCameraSpline.FindClosestPosition(Track, Track[2], false);
            Assert.That(position, Is.EqualTo(2f).Within(0.05f));
        }

        [Test]
        public void ClosestPosition_LandsBetweenPointsForAQueryBetweenThem()
        {
            Vector3 midpoint = BasisCameraSpline.Evaluate(Track, 1.5f, false);
            float position = BasisCameraSpline.FindClosestPosition(Track, midpoint, false);

            Assert.That(position, Is.EqualTo(1.5f).Within(0.05f));
        }

        [Test]
        public void TwoPointsAreAStraightLine()
        {
            Vector3[] pair = { Vector3.zero, new Vector3(0f, 0f, 10f) };
            Vector3 half = BasisCameraSpline.Evaluate(pair, 0.5f, false);

            Assert.That(half.z, Is.EqualTo(5f).Within(1e-3f));
        }

        [Test]
        public void ASinglePointIsTheWholePath()
        {
            Vector3[] one = { new Vector3(1f, 2f, 3f) };
            Assert.That(BasisCameraSpline.Evaluate(one, 0.7f, false), Is.EqualTo(one[0]));
        }

        [Test]
        public void AnEmptyTrackEvaluatesToTheOriginRatherThanThrowing()
        {
            Assert.That(BasisCameraSpline.Evaluate(new Vector3[0], 1f, false), Is.EqualTo(Vector3.zero));
            Assert.That(BasisCameraSpline.Evaluate(null, 1f, false), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void ApproximateLength_IsAtLeastTheStraightLineThroughThePoints()
        {
            float straight = 0f;
            for (int Index = 1; Index < Track.Length; Index++)
            {
                straight += Vector3.Distance(Track[Index - 1], Track[Index]);
            }

            Assert.That(BasisCameraSpline.ApproximateLength(Track, false), Is.GreaterThanOrEqualTo(straight * 0.98f));
        }

        [Test]
        public void TangentPointsAlongTheTrack()
        {
            Vector3[] line = { Vector3.zero, new Vector3(0f, 0f, 5f), new Vector3(0f, 0f, 10f) };
            Vector3 tangent = BasisCameraSpline.EvaluateTangent(line, 1f, false);

            Assert.That(Vector3.Dot(tangent, Vector3.forward), Is.EqualTo(1f).Within(1e-3f));
        }
    }

    public class BasisCameraOrbitalTests
    {
        [Test]
        public void TheOrbitPassesThroughAllThreeAuthoredRings()
        {
            Vector2 bottom = new Vector2(-0.6f, 1.4f);
            Vector2 middle = new Vector2(0.1f, 1.8f);
            Vector2 top = new Vector2(1.6f, 1.2f);

            Assert.That(BasisCameraOrbital.EvaluateRig(0f, bottom, middle, top), Is.EqualTo(bottom));
            Assert.That(BasisCameraOrbital.EvaluateRig(1f, bottom, middle, top), Is.EqualTo(top));

            Vector2 half = BasisCameraOrbital.EvaluateRig(0.5f, bottom, middle, top);
            Assert.That(half.x, Is.EqualTo(middle.x).Within(1e-4f),
                "The waist ring is authored, not merely approached — the curve has to hit it at 0.5.");
            Assert.That(half.y, Is.EqualTo(middle.y).Within(1e-4f));
        }

        [Test]
        public void TheVerticalSweepIsClamped()
        {
            Vector2 bottom = new Vector2(0f, 1f);
            Vector2 middle = new Vector2(1f, 2f);
            Vector2 top = new Vector2(2f, 1f);

            Assert.That(BasisCameraOrbital.EvaluateRig(-5f, bottom, middle, top), Is.EqualTo(bottom));
            Assert.That(BasisCameraOrbital.EvaluateRig(5f, bottom, middle, top), Is.EqualTo(top));
        }

        [Test]
        public void HeadingZeroSitsInFrontOfTheSubject()
        {
            Vector3 position = BasisCameraOrbital.SolvePosition(Vector3.zero, Quaternion.identity, 0f, 0f, 2f, 1f);

            Assert.That(position.z, Is.EqualTo(2f).Within(1e-4f),
                "The rig has to agree with the camera's existing forward-facing follow offset.");
        }

        [Test]
        public void HeadingRoundTripsThroughThePositionItProduces()
        {
            Quaternion yaw = Quaternion.Euler(0f, 35f, 0f);
            const float heading = 110f;

            Vector3 position = BasisCameraOrbital.SolvePosition(Vector3.zero, yaw, heading, 1f, 2.5f, 1f);
            float recovered = BasisCameraOrbital.HeadingFromPosition(Vector3.zero, yaw, position);

            Assert.That(Mathf.DeltaAngle(recovered, heading), Is.EqualTo(0f).Within(1e-2f),
                "Switching a shot into orbit must not whip the camera round to heading zero.");
        }

        [Test]
        public void HeadingDamping_CrossesZeroTheShortWay()
        {
            float result = BasisCameraOrbital.DampHeading(350f, 10f, 0f, 1f / 60f);

            Assert.That(Mathf.DeltaAngle(result, 10f), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(result, Is.GreaterThan(340f), "It must step forward through 360, not sweep back through 180.");
        }

        [Test]
        public void OrbitScalesWithTheAvatar()
        {
            Vector3 small = BasisCameraOrbital.SolvePosition(Vector3.zero, Quaternion.identity, 0f, 1f, 2f, 0.5f);
            Vector3 large = BasisCameraOrbital.SolvePosition(Vector3.zero, Quaternion.identity, 0f, 1f, 2f, 2f);

            Assert.That(large.z, Is.EqualTo(small.z * 4f).Within(1e-4f));
        }
    }

    public class BasisCameraNoiseTests
    {
        [Test]
        public void NoiseStaysInsideTheAmplitudeItWasGiven()
        {
            BasisCameraNoiseSettings settings = BasisCameraNoiseSettings.ForProfile(BasisCameraNoiseProfile.Shaky);

            for (float time = 0f; time < 30f; time += 0.017f)
            {
                Vector3 sample = BasisCameraNoise.SamplePosition(time, settings);
                Assert.That(Mathf.Abs(sample.x), Is.LessThanOrEqualTo(settings.positionAmplitude.x + 1e-4f));
                Assert.That(Mathf.Abs(sample.y), Is.LessThanOrEqualTo(settings.positionAmplitude.y + 1e-4f));
                Assert.That(Mathf.Abs(sample.z), Is.LessThanOrEqualTo(settings.positionAmplitude.z + 1e-4f));
            }
        }

        [Test]
        public void TheOffProfileIsPerfectlyStill()
        {
            BasisCameraNoiseSettings off = BasisCameraNoiseSettings.ForProfile(BasisCameraNoiseProfile.Off);

            Assert.That(BasisCameraNoise.SamplePosition(4.2f, off), Is.EqualTo(Vector3.zero));
            Assert.That(BasisCameraNoise.SampleRotation(4.2f, off), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void ZeroGainSilencesAProfileWithoutClearingIt()
        {
            BasisCameraNoiseSettings settings = BasisCameraNoiseSettings.ForProfile(BasisCameraNoiseProfile.Handheld);
            settings.amplitudeGain = 0f;

            Assert.That(BasisCameraNoise.SamplePosition(1.5f, settings), Is.EqualTo(Vector3.zero));
            Assert.That(settings.positionAmplitude, Is.Not.EqualTo(Vector3.zero),
                "Turning the shake down must not destroy the profile the user picked.");
        }

        [Test]
        public void NoiseIsContinuousSoItCannotStep()
        {
            BasisCameraNoiseSettings settings = BasisCameraNoiseSettings.ForProfile(BasisCameraNoiseProfile.Documentary);

            Vector3 a = BasisCameraNoise.SamplePosition(3f, settings);
            Vector3 b = BasisCameraNoise.SamplePosition(3f + 1e-4f, settings);

            Assert.That(Vector3.Distance(a, b), Is.LessThan(1e-3f));
        }

        [Test]
        public void ImpulseEnvelope_RisesHoldsAndDies()
        {
            Assert.That(BasisCameraNoise.ImpulseEnvelope(0f, 0.1f, 0.2f, 0.5f), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(BasisCameraNoise.ImpulseEnvelope(0.1f, 0.1f, 0.2f, 0.5f), Is.EqualTo(1f).Within(1e-3f));
            Assert.That(BasisCameraNoise.ImpulseEnvelope(0.25f, 0.1f, 0.2f, 0.5f), Is.EqualTo(1f).Within(1e-3f));
            Assert.That(BasisCameraNoise.ImpulseEnvelope(0.8f, 0.1f, 0.2f, 0.5f), Is.EqualTo(0f).Within(1e-3f));
            Assert.That(BasisCameraNoise.ImpulseEnvelope(99f, 0.1f, 0.2f, 0.5f), Is.EqualTo(0f));
        }

        [Test]
        public void ImpulseFadesWithDistanceAndStopsEntirelyBeyondItsReach()
        {
            Assert.That(BasisCameraNoise.DistanceAttenuation(1f, 5f, 10f), Is.EqualTo(1f));
            Assert.That(BasisCameraNoise.DistanceAttenuation(10f, 5f, 10f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(BasisCameraNoise.DistanceAttenuation(50f, 5f, 10f), Is.EqualTo(0f));
        }
    }

    public class BasisCameraTargetGroupTests
    {
        private static BasisCameraSubject Member(Vector3 position, float yawDegrees = 0f)
            => new BasisCameraSubject
            {
                Valid = true,
                AnchorPos = position,
                LookPoint = position + Vector3.up * 0.2f,
                GroundPos = position - Vector3.up * 1.5f,
                Yaw = Quaternion.Euler(0f, yawDegrees, 0f),
                Scale = 1f,
                Radius = 0.4f,
            };

        [Test]
        public void ASingleMemberPassesStraightThrough()
        {
            BasisCameraSubject only = Member(new Vector3(5f, 1f, 2f));

            bool ok = BasisCameraTargetGroup.TryCombine(new[] { only }, null, out BasisCameraSubject combined);

            Assert.That(ok, Is.True);
            Assert.That(combined.AnchorPos, Is.EqualTo(only.AnchorPos));
        }

        [Test]
        public void TheGroupSitsBetweenItsMembersAndBoundsThemAll()
        {
            var members = new[] { Member(new Vector3(-4f, 0f, 0f)), Member(new Vector3(4f, 0f, 0f)) };

            bool ok = BasisCameraTargetGroup.TryCombine(members, null, out BasisCameraSubject combined);

            Assert.That(ok, Is.True);
            Assert.That(combined.AnchorPos.x, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(combined.Radius, Is.GreaterThanOrEqualTo(4.4f - 1e-3f),
                "The radius drives the framing dolly; too small and the outer members leave the shot.");
        }

        [Test]
        public void AnEmptyOrAllInvalidGroupFails()
        {
            Assert.That(BasisCameraTargetGroup.TryCombine(new BasisCameraSubject[0], null, out _), Is.False);
            Assert.That(BasisCameraTargetGroup.TryCombine(new[] { default(BasisCameraSubject) }, null, out _), Is.False);
        }

        [Test]
        public void AverageFacingCrossesZeroInsteadOfPointingBackwards()
        {
            var members = new[] { Member(Vector3.left, 350f), Member(Vector3.right, 10f) };

            BasisCameraTargetGroup.TryCombine(members, null, out BasisCameraSubject combined);
            float yaw = combined.Yaw.eulerAngles.y;

            Assert.That(Mathf.DeltaAngle(yaw, 0f), Is.EqualTo(0f).Within(1f),
                "Numerically averaging 350 and 10 gives 180 — the exact opposite of the answer.");
        }

        [Test]
        public void TheGroupStandsOnItsLowestMember()
        {
            var members = new[] { Member(new Vector3(0f, 0f, 0f)), Member(new Vector3(2f, 10f, 0f)) };

            BasisCameraTargetGroup.TryCombine(members, null, out BasisCameraSubject combined);

            Assert.That(combined.GroundPos.y, Is.EqualTo(-1.5f).Within(1e-4f));
        }
    }
}
