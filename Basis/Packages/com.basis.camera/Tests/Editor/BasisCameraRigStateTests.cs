using Basis.Cinematics;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// Hand-off and teleport behaviour. These two look similar and want opposite things: a hand-off
    /// must continue from wherever the camera physically is, while a teleport must throw that away
    /// and re-derive from the subject.
    /// </summary>

    /// <summary>Frame maths the damping helpers rely on, and the native calls they stand in for.</summary>
    public class BasisCameraFrameMathTests
    {
        [Test]
        public void ConjugateUndoesTheRotationItWasBuiltFrom()
        {
            Quaternion yaw = BasisCameraDamping.Yaw(37f);
            Quaternion product = yaw * BasisCameraDamping.Conjugate(yaw);

            Assert.That(Mathf.Abs(product.w), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(product.x, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(product.y, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(product.z, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void ConjugateRotatesAVectorBackToWhereItStarted()
        {
            Quaternion yaw = BasisCameraDamping.Yaw(90f);
            Vector3 original = new Vector3(1f, 2f, 3f);

            Vector3 roundTrip = BasisCameraDamping.Conjugate(yaw) * (yaw * original);

            Assert.That(Vector3.Distance(roundTrip, original), Is.LessThan(1e-4f));
        }

        [Test]
        public void YawTurnsForwardTowardTheExpectedAxis()
        {
            Vector3 turned = BasisCameraDamping.Yaw(90f) * Vector3.forward;

            Assert.That(turned.x, Is.EqualTo(1f).Within(1e-4f), "90 degrees of yaw points forward at +X.");
            Assert.That(turned.z, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void YawIsAUnitRotation()
        {
            Quaternion yaw = BasisCameraDamping.Yaw(213f);
            float length = Mathf.Sqrt(yaw.x * yaw.x + yaw.y * yaw.y + yaw.z * yaw.z + yaw.w * yaw.w);

            Assert.That(length, Is.EqualTo(1f).Within(1e-5f), "A non-unit rotation would make the conjugate wrong.");
        }

        [Test]
        public void YawOfZeroIsIdentity()
        {
            Vector3 unchanged = BasisCameraDamping.Yaw(0f) * new Vector3(1f, 2f, 3f);
            Assert.That(Vector3.Distance(unchanged, new Vector3(1f, 2f, 3f)), Is.LessThan(1e-5f));
        }

        [Test]
        public void ApproachInFrame_DampsAlongTheFramesOwnAxesNotTheWorlds()
        {
            // The frame is turned 90 degrees, so the frame's forward is world +X. A shot that lags
            // hard on approach and not at all sideways must therefore lag along world X.
            Quaternion frame = BasisCameraDamping.Yaw(90f);
            Vector3 dampTime = new Vector3(0f, 0f, 100f);

            Vector3 result = BasisCameraDamping.ApproachInFrame(
                Vector3.zero, new Vector3(10f, 0f, 10f), frame, dampTime, 1f / 60f);

            Assert.That(result.x, Is.LessThan(0.1f), "World X is the frame's damped approach axis.");
            Assert.That(result.z, Is.EqualTo(10f).Within(1e-3f), "World Z is the frame's undamped side axis.");
        }

        [Test]
        public void ApproachInFrame_WithAnIdentityFrameMatchesPlainPerAxisDamping()
        {
            Vector3 dampTime = new Vector3(0f, 0.5f, 100f);
            Vector3 target = new Vector3(4f, 4f, 4f);

            Vector3 framed = BasisCameraDamping.ApproachInFrame(Vector3.zero, target, Quaternion.identity, dampTime, 1f / 60f);
            Vector3 plain = BasisCameraDamping.Damp(target, dampTime, 1f / 60f);

            Assert.That(Vector3.Distance(framed, plain), Is.LessThan(1e-4f));
        }
    }

    public class BasisCameraConfinerTests
    {
        [Test]
        public void APositionInsideTheBoundsIsLeftAlone()
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(10f, 10f, 10f));
            Vector3 inside = new Vector3(1f, 2f, 3f);

            Assert.That(BasisCameraFraming.Confine(inside, bounds), Is.EqualTo(inside));
        }

        [Test]
        public void APositionOutsideIsPulledToTheNearestFace()
        {
            var bounds = new Bounds(Vector3.zero, new Vector3(10f, 10f, 10f));

            Vector3 confined = BasisCameraFraming.Confine(new Vector3(50f, 0f, 0f), bounds);

            Assert.That(confined.x, Is.EqualTo(5f).Within(1e-4f));
        }

        [Test]
        public void EmptyBoundsConfineNothing()
        {
            var empty = new Bounds(Vector3.zero, Vector3.zero);
            Vector3 far = new Vector3(999f, 999f, 999f);

            Assert.That(BasisCameraFraming.Confine(far, empty), Is.EqualTo(far),
                "An unconfigured confiner must never move the shot.");
        }
    }


    public class BasisCameraLoopedSplineTests
    {
        private static readonly Vector3[] Square =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(4f, 0f, 0f),
            new Vector3(4f, 0f, 4f),
            new Vector3(0f, 0f, 4f),
        };

        [Test]
        public void ALoopedPathStillVisitsEveryWaypoint()
        {
            for (int Index = 0; Index < Square.Length; Index++)
            {
                Vector3 onPath = BasisCameraSpline.Evaluate(Square, Index, true);
                Assert.That(Vector3.Distance(onPath, Square[Index]), Is.LessThan(1e-4f));
            }
        }

        [Test]
        public void ALoopedPathClosesBackOnItself()
        {
            Vector3 start = BasisCameraSpline.Evaluate(Square, 0f, true);
            Vector3 wrapped = BasisCameraSpline.Evaluate(Square, Square.Length, true);

            Assert.That(Vector3.Distance(start, wrapped), Is.LessThan(1e-3f));
        }

        [Test]
        public void ALoopedPathIsContinuousAcrossTheSeam()
        {
            Vector3 before = BasisCameraSpline.Evaluate(Square, Square.Length - 0.001f, true);
            Vector3 after = BasisCameraSpline.Evaluate(Square, 0.001f, true);

            Assert.That(Vector3.Distance(before, after), Is.LessThan(0.05f),
                "A discontinuity at the seam would snap the camera once per lap.");
        }

        [Test]
        public void ALoopedPathIsLongerThanTheSameOpenPath()
        {
            float open = BasisCameraSpline.ApproximateLength(Square, false);
            float looped = BasisCameraSpline.ApproximateLength(Square, true);

            Assert.That(looped, Is.GreaterThan(open), "The loop adds the closing segment.");
        }

        [Test]
        public void ClosestPositionOnALoopStaysInRange()
        {
            float position = BasisCameraSpline.FindClosestPosition(Square, new Vector3(-5f, 0f, 2f), true);

            Assert.That(position, Is.GreaterThanOrEqualTo(0f));
            Assert.That(position, Is.LessThan(BasisCameraSpline.MaxPosition(Square.Length, true)));
        }

        [Test]
        public void DampingAcrossTheSeamTakesTheShortWay()
        {
            // 0.1 to 3.9 on a four-segment loop is 0.2 backwards, not 3.8 forwards.
            float stepped = BasisCameraModifierSolver.DampDollyPosition(0.1f, 3.9f, 4, true, 0.05f, 1f / 60f);

            Assert.That(stepped, Is.GreaterThan(3.5f).Or.LessThan(0.1f));
            Assert.That(stepped, Is.GreaterThanOrEqualTo(0f));
            Assert.That(stepped, Is.LessThan(4f), "The result must stay a legal path position.");
        }

        [Test]
        public void DampingOnAnOpenPathDoesNotWrap()
        {
            float stepped = BasisCameraModifierSolver.DampDollyPosition(0.1f, 2.9f, 4, false, 0f, 1f / 60f);

            Assert.That(stepped, Is.EqualTo(2.9f).Within(1e-3f),
                "An open track has no seam, so the move is the long way by definition.");
        }

        [Test]
        public void DampingAnEmptyTrackStaysAtZero()
        {
            Assert.That(BasisCameraModifierSolver.DampDollyPosition(0f, 5f, 0, false, 0f, 1f / 60f), Is.EqualTo(0f));
            Assert.That(BasisCameraModifierSolver.DampDollyPosition(0f, 5f, 1, false, 0f, 1f / 60f), Is.EqualTo(0f));
        }
    }

    public class BasisCameraNoiseProfileTests
    {
        [Test]
        public void EveryProfileExceptOffActuallyMoves()
        {
            foreach (BasisCameraNoiseProfile profile in System.Enum.GetValues(typeof(BasisCameraNoiseProfile)))
            {
                BasisCameraNoiseSettings settings = BasisCameraNoiseSettings.ForProfile(profile);
                bool silent = settings.positionAmplitude == Vector3.zero && settings.rotationAmplitude == Vector3.zero;

                if (profile == BasisCameraNoiseProfile.Off)
                {
                    Assert.That(silent, Is.True, "Off must be completely still.");
                }
                else
                {
                    Assert.That(silent, Is.False, $"{profile} would be an inert entry in the dropdown.");
                    Assert.That(settings.amplitudeGain, Is.EqualTo(1f).Within(1e-5f));
                    Assert.That(settings.frequencyGain, Is.EqualTo(1f).Within(1e-5f));
                }
            }
        }

        [Test]
        public void EveryProfileRemembersWhichOneItIs()
        {
            foreach (BasisCameraNoiseProfile profile in System.Enum.GetValues(typeof(BasisCameraNoiseProfile)))
            {
                Assert.That(BasisCameraNoiseSettings.ForProfile(profile).profile, Is.EqualTo(profile),
                    "The panel reads this back to show the selection.");
            }
        }

        [Test]
        public void NoChannelIsSeededOnThePerlinLattice()
        {
            // Perlin noise is exactly zero on integer lattice lines, so a channel seeded at 0 comes
            // out measurably quieter than the amplitude it was given.
            BasisCameraNoiseSettings handheld = BasisCameraNoiseSettings.ForProfile(BasisCameraNoiseProfile.Handheld);

            for (int Channel = 0; Channel < 3; Channel++)
            {
                float peak = 0f;
                for (float time = 0f; time < 60f; time += 0.05f)
                {
                    peak = Mathf.Max(peak, Mathf.Abs(BasisCameraNoise.SamplePosition(time, handheld)[Channel]));
                }

                float amplitude = handheld.positionAmplitude[Channel];
                Assert.That(peak, Is.GreaterThan(amplitude * 0.4f),
                    $"Position channel {Channel} barely moves; it is probably sampling a lattice line.");
            }
        }

        [Test]
        public void TheOffProfileNeverReachesTheNoiseField()
        {
            BasisCameraNoiseSettings off = BasisCameraNoiseSettings.ForProfile(BasisCameraNoiseProfile.Off);

            Assert.That(BasisCameraNoise.SamplePosition(12.5f, off), Is.EqualTo(Vector3.zero));
            Assert.That(BasisCameraNoise.SampleRotation(12.5f, off), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void ZeroFrequencyIsAsStillAsZeroAmplitude()
        {
            BasisCameraNoiseSettings settings = BasisCameraNoiseSettings.ForProfile(BasisCameraNoiseProfile.Handheld);
            settings.frequencyGain = 0f;

            Assert.That(BasisCameraNoise.SamplePosition(3f, settings), Is.EqualTo(Vector3.zero));
        }
    }
}
