using System;
using Basis.Cinematics;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Camera
{
    /// <summary>
    /// The ease curves and the speed envelope built out of them.
    ///
    /// <para>The envelope is read from two places that must not be allowed to disagree: the solver
    /// advances the playhead by it, and the track paints itself by it. A colour that says the
    /// camera is crawling into the last waypoint while the solver runs it in flat out is worse than
    /// no colour at all, so the arithmetic lives in one place and is asserted on here.</para>
    /// </summary>
    public class BasisCameraDollyEaseTests
    {
        private static BasisCameraDollySettings Move(BasisCameraEase easeIn, float inPortion,
            BasisCameraEase easeOut, float outPortion, float speed = 2f)
        {
            BasisCameraDollySettings dolly = BasisCameraDollySettings.Default;
            dolly.mode = BasisCameraDollyMode.Play;
            dolly.speed = speed;
            dolly.easeIn = easeIn;
            dolly.easeInPortion = inPortion;
            dolly.easeOut = easeOut;
            dolly.easeOutPortion = outPortion;
            return dolly;
        }

        // ---- The curves -------------------------------------------------------------------

        [Test]
        public void EveryCurveRunsFromNothingToAllOfIt()
        {
            foreach (BasisCameraEase ease in Enum.GetValues(typeof(BasisCameraEase)))
            {
                Assert.That(BasisCameraEasing.In(ease, 0f), Is.EqualTo(0f).Within(0.001f), $"{ease} in at 0");
                Assert.That(BasisCameraEasing.In(ease, 1f), Is.EqualTo(1f).Within(0.001f), $"{ease} in at 1");
                Assert.That(BasisCameraEasing.Out(ease, 0f), Is.EqualTo(0f).Within(0.001f), $"{ease} out at 0");
                Assert.That(BasisCameraEasing.Out(ease, 1f), Is.EqualTo(1f).Within(0.001f), $"{ease} out at 1");
            }
        }

        [Test]
        public void AnEaseOutIsItsEaseInBackwards()
        {
            // The two dropdowns offer the same names, so the same name has to mean the same shape
            // coming and going or picking one from each would compose something nobody asked for.
            foreach (BasisCameraEase ease in Enum.GetValues(typeof(BasisCameraEase)))
            {
                for (float t = 0f; t <= 1f; t += 0.05f)
                {
                    Assert.That(BasisCameraEasing.Out(ease, t),
                        Is.EqualTo(1f - BasisCameraEasing.In(ease, 1f - t)).Within(0.0001f), $"{ease} at {t}");
                }
            }
        }

        [Test]
        public void TheSmoothCurvesOnlyEverClimb()
        {
            // Back, Elastic and Bounce are deliberately not in this list: leaving the range is the
            // whole of what they are for.
            BasisCameraEase[] smooth =
            {
                BasisCameraEase.Linear, BasisCameraEase.Sine, BasisCameraEase.Quad, BasisCameraEase.Cubic,
                BasisCameraEase.Quart, BasisCameraEase.Quint, BasisCameraEase.Expo, BasisCameraEase.Circ,
            };

            foreach (BasisCameraEase ease in smooth)
            {
                float previous = -1f;
                for (float t = 0f; t <= 1f; t += 0.02f)
                {
                    float value = BasisCameraEasing.In(ease, t);
                    Assert.That(value, Is.GreaterThanOrEqualTo(previous - 0.0001f), $"{ease} dipped at {t}");
                    Assert.That(value, Is.InRange(-0.0001f, 1.0001f), $"{ease} left the range at {t}");
                    previous = value;
                }
            }
        }

        [Test]
        public void LinearIsTheOneThatDoesNothing()
        {
            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                Assert.That(BasisCameraEasing.In(BasisCameraEase.Linear, t), Is.EqualTo(t).Within(0.0001f));
            }
        }

        [Test]
        public void AnEaseOffTheEndOfTheTableIsRefused()
        {
            // Settings arrive off disk and out of exported presets, so the enum is not a promise.
            Assert.That(BasisCameraEasing.IsDefined((BasisCameraEase)99), Is.False);
            Assert.That(BasisCameraEasing.IsDefined((BasisCameraEase)(-1)), Is.False);
            Assert.That(BasisCameraEasing.IsDefined(BasisCameraEase.Bounce), Is.True);
            Assert.That(BasisCameraEasing.Count, Is.EqualTo(Enum.GetValues(typeof(BasisCameraEase)).Length),
                "The dropdowns size themselves off Count, so a curve added without it is unreachable.");
        }

        // ---- The envelope -----------------------------------------------------------------

        [Test]
        public void WithNoEaseTheMoveIsAtSpeedThroughout()
        {
            BasisCameraDollySettings dolly = Move(BasisCameraEase.Cubic, 0f, BasisCameraEase.Cubic, 0f);

            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                Assert.That(BasisCameraDollySpeed.Weight(dolly, t, false), Is.EqualTo(1f).Within(0.0001f));
            }
        }

        [Test]
        public void TheMoveStartsSlowAndBuildsAcrossTheRunUp()
        {
            BasisCameraDollySettings dolly = Move(BasisCameraEase.Cubic, 0.4f, BasisCameraEase.Linear, 0f);

            float atStart = BasisCameraDollySpeed.Weight(dolly, 0f, false);
            float partWay = BasisCameraDollySpeed.Weight(dolly, 0.2f, false);
            float atSpeed = BasisCameraDollySpeed.Weight(dolly, 0.4f, false);

            Assert.That(atStart, Is.LessThan(partWay));
            Assert.That(partWay, Is.LessThan(atSpeed));
            Assert.That(atSpeed, Is.EqualTo(1f).Within(0.001f), "The run-up is over at the end of the run-up.");
        }

        [Test]
        public void TheMoveComesBackDownAcrossTheRunOut()
        {
            BasisCameraDollySettings dolly = Move(BasisCameraEase.Linear, 0f, BasisCameraEase.Cubic, 0.4f);

            Assert.That(BasisCameraDollySpeed.Weight(dolly, 0.6f, false), Is.EqualTo(1f).Within(0.001f));
            Assert.That(BasisCameraDollySpeed.Weight(dolly, 0.8f, false),
                Is.LessThan(BasisCameraDollySpeed.Weight(dolly, 0.6f, false)));
            Assert.That(BasisCameraDollySpeed.Weight(dolly, 1f, false),
                Is.LessThan(BasisCameraDollySpeed.Weight(dolly, 0.8f, false)));
        }

        [Test]
        public void TheMoveNeverQuiteStopsAndNeverReverses()
        {
            // A weight of zero parks the camera on the first waypoint with nothing to carry it off,
            // and Back's undershoot would reverse it into the far end and finish the move at once.
            foreach (BasisCameraEase ease in Enum.GetValues(typeof(BasisCameraEase)))
            {
                BasisCameraDollySettings dolly = Move(ease, 0.5f, ease, 0.5f);
                for (float t = 0f; t <= 1f; t += 0.01f)
                {
                    float weight = BasisCameraDollySpeed.Weight(dolly, t, false);
                    Assert.That(weight, Is.GreaterThanOrEqualTo(BasisCameraDollySpeed.MinimumWeight), $"{ease} at {t}");
                    Assert.That(weight, Is.LessThanOrEqualTo(BasisCameraDollySpeed.MaximumWeight), $"{ease} at {t}");
                }
            }
        }

        [Test]
        public void RunningTheTrackBackwardsPutsTheRunUpAtTheEndItStartsFrom()
        {
            BasisCameraDollySettings forwards = Move(BasisCameraEase.Cubic, 0.3f, BasisCameraEase.Linear, 0f, 2f);
            BasisCameraDollySettings backwards = Move(BasisCameraEase.Cubic, 0.3f, BasisCameraEase.Linear, 0f, -2f);

            Assert.That(BasisCameraDollySpeed.Weight(backwards, 1f, false),
                Is.EqualTo(BasisCameraDollySpeed.Weight(forwards, 0f, false)).Within(0.0001f),
                "A negative speed starts at the far end, so that is where it has to build from.");
            Assert.That(BasisCameraDollySpeed.Weight(backwards, 0f, false), Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void ALoopedTrackRunsFlat()
        {
            // A loop has no first or last waypoint, so an envelope on it is a slow spot at the seam
            // that comes round again on every lap.
            BasisCameraDollySettings dolly = Move(BasisCameraEase.Expo, 0.5f, BasisCameraEase.Expo, 0.5f);

            for (float t = 0f; t <= 1f; t += 0.1f)
            {
                Assert.That(BasisCameraDollySpeed.Weight(dolly, t, true), Is.EqualTo(1f).Within(0.0001f));
            }
        }

        [Test]
        public void RunUpsThatMeetInTheMiddleTakeWhicheverIsSlower()
        {
            // Both halves at their cap, so the two regions cover the whole track and overlap.
            BasisCameraDollySettings dolly = Move(BasisCameraEase.Linear, 0.5f, BasisCameraEase.Linear, 0.5f);

            Assert.That(BasisCameraDollySpeed.Weight(dolly, 0.5f, false), Is.EqualTo(1f).Within(0.01f));
            Assert.That(BasisCameraDollySpeed.Weight(dolly, 0.25f, false), Is.EqualTo(0.5f).Within(0.01f));
            Assert.That(BasisCameraDollySpeed.Weight(dolly, 0.75f, false), Is.EqualTo(0.5f).Within(0.01f));
        }

        [Test]
        public void APortionPastTheCapIsTreatedAsTheCap()
        {
            BasisCameraDollySettings sane = Move(BasisCameraEase.Linear, BasisCameraDollySpeed.MaximumEasePortion,
                BasisCameraEase.Linear, 0f);
            BasisCameraDollySettings absurd = Move(BasisCameraEase.Linear, 12f, BasisCameraEase.Linear, 0f);

            Assert.That(BasisCameraDollySpeed.Weight(absurd, 0.25f, false),
                Is.EqualTo(BasisCameraDollySpeed.Weight(sane, 0.25f, false)).Within(0.0001f));
        }

        [Test]
        public void ASanitizedStackCannotCarryACurveThatDoesNotExist()
        {
            var stack = new BasisCameraModifierStack();
            stack.dolly.easeIn = (BasisCameraEase)77;
            stack.dolly.easeOut = (BasisCameraEase)(-3);
            stack.dolly.easeInPortion = 9f;
            stack.dolly.easeOutPortion = -4f;

            stack.Sanitize();

            Assert.That(BasisCameraEasing.IsDefined(stack.dolly.easeIn), Is.True);
            Assert.That(BasisCameraEasing.IsDefined(stack.dolly.easeOut), Is.True);
            Assert.That(stack.dolly.easeInPortion, Is.EqualTo(BasisCameraDollySpeed.MaximumEasePortion));
            Assert.That(stack.dolly.easeOutPortion, Is.EqualTo(0f));
        }

        [Test]
        public void MovingAnEaseSettingIsNoticedByTheDriftCheck()
        {
            // Matches drives "you have left the saved mode". A field it does not read is a mode
            // that keeps claiming a move it no longer describes.
            var a = new BasisCameraModifierStack();
            var b = new BasisCameraModifierStack();
            Assert.That(BasisCameraModifierStack.Matches(a, b), Is.True);

            b.dolly.easeIn = BasisCameraEase.Bounce;
            Assert.That(BasisCameraModifierStack.Matches(a, b), Is.False, "easeIn");

            b = new BasisCameraModifierStack();
            b.dolly.easeOut = BasisCameraEase.Bounce;
            Assert.That(BasisCameraModifierStack.Matches(a, b), Is.False, "easeOut");

            b = new BasisCameraModifierStack();
            b.dolly.easeInPortion += 0.25f;
            Assert.That(BasisCameraModifierStack.Matches(a, b), Is.False, "easeInPortion");

            b = new BasisCameraModifierStack();
            b.dolly.easeOutPortion += 0.25f;
            Assert.That(BasisCameraModifierStack.Matches(a, b), Is.False, "easeOutPortion");
        }

        // ---- The colour -------------------------------------------------------------------

        [Test]
        public void TheRampRunsCoolToWarm()
        {
            Color slow = BasisCameraDollySpeed.Ramp(0f);
            Color fast = BasisCameraDollySpeed.Ramp(1f);

            Assert.That(slow.b, Is.GreaterThan(slow.r), "The slow end has to read as cold.");
            Assert.That(fast.r, Is.GreaterThan(fast.b), "The fast end has to read as hot.");
        }

        [Test]
        public void RedIsAlwaysGainedAndBlueIsAlwaysLostGoingUpTheRamp()
        {
            float previousRed = -1f;
            float previousBlue = 2f;
            for (float t = 0f; t <= 1f; t += 0.05f)
            {
                Color colour = BasisCameraDollySpeed.Ramp(t);
                Assert.That(colour.r, Is.GreaterThanOrEqualTo(previousRed - 0.0001f), $"red went back at {t}");
                Assert.That(colour.b, Is.LessThanOrEqualTo(previousBlue + 0.0001f), $"blue went up at {t}");
                previousRed = colour.r;
                previousBlue = colour.b;
            }
        }

        [Test]
        public void ASpeedOffTheEndOfTheScaleStillPaints()
        {
            Assert.That(BasisCameraDollySpeed.Sample(9999f, 1f), Is.EqualTo(BasisCameraDollySpeed.Ramp(1f)));
            Assert.That(BasisCameraDollySpeed.Sample(-5f, 1f), Is.EqualTo(BasisCameraDollySpeed.Ramp(0f)));
            Assert.That(BasisCameraDollySpeed.Sample(1f, 0f), Is.Not.EqualTo(default(Color)),
                "A zero scale must not divide the reference away.");
        }

        [Test]
        public void AFasterMoveIsPaintedWarmerAtTheSamePlaceOnTheTrack()
        {
            // The whole point of the colour: the speed setting has to be visible in it.
            Color slow = BasisCameraDollySpeed.Sample(0.5f, 1f);
            Color quick = BasisCameraDollySpeed.Sample(3f, 1f);

            Assert.That(quick.r, Is.GreaterThan(slow.r));
        }

        [Test]
        public void ALongStretchBetweenWaypointsIsPaintedAsTheFasterOne()
        {
            // The playhead advances in waypoints at one rate, so a long span is covered faster.
            BasisCameraDollySettings dolly = Move(BasisCameraEase.Linear, 0f, BasisCameraEase.Linear, 0f, 1.5f);

            float even = BasisCameraDollySpeed.MetresPerSecond(dolly, 0.5f, false, 1f);
            float stretched = BasisCameraDollySpeed.MetresPerSecond(dolly, 0.5f, false, 2f);

            Assert.That(even, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(stretched, Is.EqualTo(3f).Within(0.0001f));
        }

        [Test]
        public void TheSpeedShownIsNeverNegativeHoweverTheMoveRuns()
        {
            // The colour is about how fast, not which way; the arrows on the markers say which way.
            BasisCameraDollySettings dolly = Move(BasisCameraEase.Linear, 0f, BasisCameraEase.Linear, 0f, -4f);

            Assert.That(BasisCameraDollySpeed.MetresPerSecond(dolly, 0.5f, false, 1f), Is.EqualTo(4f).Within(0.0001f));
            Assert.That(BasisCameraDollySpeed.MetresPerSecond(dolly, 0.5f, false, -1f), Is.EqualTo(0f).Within(0.0001f));
        }
    }
}
