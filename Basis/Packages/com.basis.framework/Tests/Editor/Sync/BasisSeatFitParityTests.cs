using System.Collections.Generic;
using Basis.Scripts.BasisSdk.Interactions;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Sync
{
    public sealed class BasisSeatFitParityTests
    {
        private static BasisSeatFitFrame Chair(float spineAngle)
        {
            return Seat(new Vector3(0f, 0f, -0.25f), new Vector3(0f, 0f, 0.25f), new Vector3(0f, -0.5f, 0.25f), spineAngle);
        }

        private static BasisSeatFitFrame Seat(Vector3 back, Vector3 knee, Vector3 foot, float spineAngle)
        {
            Assert.IsTrue(BasisSeatFit.BuildFrame(back, foot, knee, spineAngle, out BasisSeatFitFrame frame),
                "test seat is degenerate");
            return frame;
        }

        private static BasisSeatFitLegs Body(float upperLegLength, float lowerLegLength, float footThickness)
        {
            return new BasisSeatFitLegs
            {
                UpperLegLength = upperLegLength,
                LowerLegLength = lowerLegLength,
                FootThickness = footThickness,
            };
        }

        private static IEnumerable<(string name, BasisSeatFitLegs legs)> Bodies()
        {
            yield return ("average", Body(0.42f, 0.40f, 0.08f));
            yield return ("short legs", Body(0.30f, 0.35f, 0.07f));
            yield return ("long legs", Body(0.50f, 0.46f, 0.09f));
            yield return ("half-scale", Body(0.21f, 0.20f, 0.04f));
            yield return ("giant", Body(0.95f, 0.90f, 0.16f));
        }

        private static void RemotePin(in BasisSeatFitFrame frame, in BasisSeatFitLegs legs, Matrix4x4 seatToWorld, Quaternion seatRotation,
            out Vector3 hipsPos, out Quaternion hipsRot)
        {
            BasisSeatFitResult fit = BasisSeatFit.Solve(frame, legs);
            BasisSeatFit.ComposeHipsWorld(seatToWorld, seatRotation, frame.SpineRotation, fit.Back, out hipsPos, out hipsRot);
        }

        private static Vector3 LegacyLocalPelvis(in BasisSeatFitFrame seat, in BasisSeatFitLegs legs)
        {
            return LegacySolve(seat, legs,
                backAngle: Mathf.Clamp(seat.SpineAngleDegrees, 10f, 170f),
                limbYSign: -1f,
                guardRatios: true,
                clampShifts: true);
        }

        private static Vector3 LegacyRemotePelvis(in BasisSeatFitFrame seat, in BasisSeatFitLegs legs)
        {
            return LegacySolve(seat, legs,
                backAngle: 180f - seat.SpineAngleDegrees,
                limbYSign: 1f,
                guardRatios: false,
                clampShifts: false);
        }

        private static Vector3 LegacySolve(in BasisSeatFitFrame seat, in BasisSeatFitLegs legs, float backAngle, float limbYSign, bool guardRatios, bool clampShifts)
        {
            float upperLegLength = legs.UpperLegLength;
            float lowerLegLength = legs.LowerLegLength;
            float totalLegLength = upperLegLength + lowerLegLength;

            float spineBackThickness = totalLegLength * 0.14f;
            float upperLegBackRadius = totalLegLength * 0.14f;
            float upperLegKneeRadius = totalLegLength * 0.08f;
            float lowerLegKneeRadius = totalLegLength * 0.10f;
            float lowerLegFootRadius = totalLegLength * 0.06f;

            float upperLegAngleVsSeatRadians = Mathf.Asin(Mathf.Clamp((upperLegBackRadius - upperLegKneeRadius) / upperLegLength, -0.9999f, 0.9999f));
            float lowerLegAngleVsSeatRadians = Mathf.Asin(Mathf.Clamp((lowerLegKneeRadius - lowerLegFootRadius) / lowerLegLength, -0.9999f, 0.9999f));

            float spineAngle = seat.SpineAngleDegrees;

            Vector3 targetFoot = seat.Foot + seat.LowerLegPerp * lowerLegFootRadius - seat.LowerLegDir * legs.FootThickness;
            Vector3 targetKnee = seat.Knee + seat.UpperLegPerp * upperLegKneeRadius
                + seat.UpperLegDir * BasisSeat.GetAdjustmentScalar(seat.LegAngleDegrees, lowerLegKneeRadius, upperLegKneeRadius, upperLegLength);
            Vector3 targetBack = seat.Back + seat.UpperLegPerp * upperLegBackRadius
                + seat.UpperLegDir * BasisSeat.GetAdjustmentScalar(backAngle, spineBackThickness, upperLegBackRadius, upperLegLength);
            Vector3 preferredBack = targetBack;

            float thighAngle = upperLegAngleVsSeatRadians + Mathf.Deg2Rad * spineAngle;
            Vector3 thigh = new Vector3(0f, limbYSign * Mathf.Cos(thighAngle), Mathf.Sin(thighAngle));
            float upperRatio = Vector3.Dot(seat.UpperLegDir, seat.SpineRotation * thigh);
            if (guardRatios) upperRatio = Mathf.Max(0.05f, Mathf.Abs(upperRatio));

            float availableUpper = Vector3.Distance(
                targetKnee - seat.UpperLegPerp * upperLegKneeRadius,
                targetBack - seat.UpperLegPerp * upperLegBackRadius);
            float characterUpper = upperLegLength * upperRatio;

            if (characterUpper < availableUpper)
            {
                float delta = availableUpper - characterUpper;
                if (clampShifts) delta = Mathf.Min(delta, 0.25f);
                targetBack += seat.UpperLegDir * delta;
            }
            else
            {
                targetKnee += seat.UpperLegDir * (characterUpper - availableUpper);
            }

            if (clampShifts)
            {
                targetBack = preferredBack + Vector3.ClampMagnitude(targetBack - preferredBack, 0.25f);
            }

            float shinAngle = lowerLegAngleVsSeatRadians - Mathf.Deg2Rad * (spineAngle + seat.LegAngleDegrees);
            Vector3 shin = new Vector3(0f, limbYSign * Mathf.Cos(shinAngle), -Mathf.Sin(shinAngle));
            float lowerRatio = Vector3.Dot(seat.LowerLegDir, seat.SpineRotation * shin);
            if (guardRatios) lowerRatio = Mathf.Max(0.05f, Mathf.Abs(lowerRatio));

            float availableLower = Vector3.Distance(
                targetFoot + seat.LowerLegDir * lowerLegFootRadius,
                targetKnee + seat.LowerLegDir * lowerLegKneeRadius);
            float characterLower = lowerLegLength * lowerRatio;

            if (characterLower < availableLower)
            {
                if (clampShifts)
                {
                    targetFoot += seat.LowerLegDir * (characterLower - availableLower);
                }
            }
            else
            {
                targetKnee += seat.LowerLegDir * (availableLower - characterLower);

                if (characterUpper > availableUpper)
                {
                    if (!clampShifts || Mathf.Abs(Vector3.Distance(targetKnee, targetFoot) - lowerLegLength) > 0.005f)
                    {
                        targetKnee = BasisSeat.ClosestPointOnSphere(targetKnee, targetFoot, lowerLegLength);
                    }
                }

                if (!clampShifts)
                {
                    targetBack = BasisSeat.ClosestPointOnSphere(targetBack, targetKnee, upperLegLength);
                }
                else if (Mathf.Abs(Vector3.Distance(targetBack, targetKnee) - upperLegLength) > 0.005f)
                {
                    Vector3 snapped = BasisSeat.ClosestPointOnSphere(targetBack, targetKnee, upperLegLength);
                    targetBack = preferredBack + Vector3.ClampMagnitude(snapped - preferredBack, 0.25f);
                    if (Mathf.Abs(Vector3.Distance(targetBack, targetKnee) - upperLegLength) > 0.02f)
                    {
                        targetBack = snapped;
                    }
                }
            }

            return targetBack;
        }

        [Test]
        public void RemotePin_MatchesLocalHipsPlacement_AcrossSeatsAndBodies()
        {
            var placements = new[]
            {
                (pos: Vector3.zero, rot: Quaternion.identity, scale: 1f),
                (pos: new Vector3(3.2f, 1.25f, -7.4f), rot: Quaternion.Euler(0f, 47f, 0f), scale: 1f),
                (pos: new Vector3(-11f, 0.4f, 2f), rot: Quaternion.Euler(12f, 200f, -8f), scale: 1f),
                (pos: new Vector3(0.5f, 2f, 0.5f), rot: Quaternion.Euler(0f, 90f, 0f), scale: 1.35f),
            };

            foreach (float spineAngle in new[] { 78f, 90f, 104f, 118f, 140f })
            {
                BasisSeatFitFrame frame = Chair(spineAngle);

                foreach (var placement in placements)
                {
                    Matrix4x4 seatToWorld = Matrix4x4.TRS(placement.pos, placement.rot, Vector3.one * placement.scale);

                    foreach ((string name, BasisSeatFitLegs legs) in Bodies())
                    {
                        RemotePin(frame, legs, seatToWorld, placement.rot, out Vector3 remotePos, out Quaternion remoteRot);

                        BasisSeatFitResult fit = BasisSeatFit.Solve(frame, legs);
                        BasisSeatFit.ComposeHipsWorld(seatToWorld, placement.rot, frame.SpineRotation, fit.Back,
                            out Vector3 localPos, out Quaternion localRot);

                        Assert.Less((remotePos - localPos).magnitude, 1e-4f,
                            $"remote pin and local placement disagree by "
                            + $"{(remotePos - localPos).magnitude * 1000f:F2} mm for the {name} body on a "
                            + $"{spineAngle:F0} degree seat. Both are supposed to be the same "
                            + $"{nameof(BasisSeatFit)}.{nameof(BasisSeatFit.Solve)} result.");
                        Assert.Less(Quaternion.Angle(remoteRot, localRot), 1e-3f,
                            $"remote and local hips rotation disagree by {Quaternion.Angle(remoteRot, localRot):F3} "
                            + $"degrees for the {name} body on a {spineAngle:F0} degree seat.");
                    }
                }
            }
        }

        [Test]
        public void SeatedRoot_RoundTripsBackToThePinnedHips()
        {
            BasisSeatFitFrame frame = Chair(96f);
            Matrix4x4 seatToWorld = Matrix4x4.TRS(new Vector3(1f, 0.2f, 4f), Quaternion.Euler(0f, 133f, 0f), Vector3.one);
            Quaternion seatRot = Quaternion.Euler(0f, 133f, 0f);

            var hipsBases = new[] { Quaternion.identity, Quaternion.Euler(0f, 180f, 0f), Quaternion.Euler(7f, -25f, 3f) };
            var hipsTposeLocals = new[] { new Vector3(0f, 0.92f, 0f), new Vector3(0.02f, 0.61f, -0.04f) };

            foreach ((string name, BasisSeatFitLegs legs) in Bodies())
            {
                RemotePin(frame, legs, seatToWorld, seatRot, out Vector3 hipsPos, out Quaternion hipsRot);

                foreach (Quaternion basis in hipsBases)
                {
                    foreach (Vector3 hipsTpose in hipsTposeLocals)
                    {
                        BasisSeatFit.ComposeSeatedRoot(hipsPos, hipsRot, basis, hipsTpose,
                            out Vector3 rootPos, out Quaternion rootRot);

                        Vector3 overriddenHips = rootPos + rootRot * hipsTpose;

                        Assert.Less((overriddenHips - hipsPos).magnitude, 1e-4f,
                            $"the {name} body's rig override lands {(overriddenHips - hipsPos).magnitude * 1000f:F2} mm "
                            + "off the remote pin; root placement and the hips pin have come apart.");
                        Assert.Less(Quaternion.Angle(rootRot * basis, hipsRot), 1e-2f,
                            $"the {name} body's root rotation does not carry its hips basis onto the seat's "
                            + "spine rotation, so the seated avatar faces the wrong way.");
                    }
                }
            }
        }

        [Test]
        public void LegacyLocalAndRemote_AgreedAt90_AndDivergedOffIt()
        {
            BasisSeatFitLegs legs = Body(0.42f, 0.40f, 0.08f);

            BasisSeatFitFrame upright = Chair(90f);
            Assert.Less((LegacyLocalPelvis(upright, legs) - LegacyRemotePelvis(upright, legs)).magnitude, 1e-4f,
                "sanity: the two legacy transcriptions are supposed to coincide at 90 degrees — if they no "
                + "longer do, this test is not reproducing the shipped pair and the divergence numbers below "
                + "mean nothing.");

            foreach (float spineAngle in new[] { 70f, 105f, 118f, 135f })
            {
                BasisSeatFitFrame seat = Chair(spineAngle);
                float divergence = (LegacyLocalPelvis(seat, legs) - LegacyRemotePelvis(seat, legs)).magnitude;

                Assert.Greater(divergence, 0.01f,
                    $"the legacy local and remote pelvis are only {divergence * 1000f:F1} mm apart on a "
                    + $"{spineAngle:F0} degree seat. They differed in the GetAdjustmentScalar argument and in "
                    + "the analytic limb-direction sign, so a seat off 90 degrees must show a visible split — "
                    + "otherwise this test has stopped reproducing the bug it guards.");
            }
        }

        [Test]
        public void SquareSeats_KeepTheirExistingLocalPose_Exactly()
        {
            BasisSeatFitFrame seat = Chair(90f);
            Assert.AreEqual(90f, seat.LegAngleDegrees, 1e-2f,
                "sanity: this fixture is supposed to be square — the exactness claim below only holds "
                + "when the shin is perpendicular to the pan.");

            foreach ((string name, BasisSeatFitLegs legs) in Bodies())
            {
                float shift = (LegacyLocalPelvis(seat, legs) - BasisSeatFit.Solve(seat, legs).Back).magnitude;

                Assert.Less(shift, 1e-5f,
                    $"the {name} body moved {shift * 1000f:F3} mm on a square chair. Unifying the solve was "
                    + "supposed to be a bit-exact no-op there — a change here means every already-authored "
                    + "chair just shifted under its occupants.");
            }
        }

        [Test]
        public void RakedShinSeats_ShiftMillimetres_OntoTheCorrectShinSpan()
        {
            BasisSeatFitFrame seat = Seat(new Vector3(0f, 0f, -0.42f), new Vector3(0f, 0f, 0.28f), new Vector3(0f, -0.44f, 0.31f), 90f);
            Assert.Less(seat.LegAngleDegrees, 89f,
                "sanity: this fixture is supposed to rake its shins forward, otherwise it is just another "
                + "square chair and proves nothing the exactness test above does not.");

            foreach ((string name, BasisSeatFitLegs legs) in Bodies())
            {
                float total = legs.UpperLegLength + legs.LowerLegLength;
                float shinAngle = Mathf.Asin(
                    (total * BasisSeatFit.LowerLegKneeRadiusRatio - total * BasisSeatFit.LowerLegFootRadiusRatio) / legs.LowerLegLength)
                    - Mathf.Deg2Rad * (seat.SpineAngleDegrees + seat.LegAngleDegrees);

                float unifiedSpan = Mathf.Abs(Vector3.Dot(seat.LowerLegDir,
                    seat.SpineRotation * new Vector3(0f, Mathf.Cos(shinAngle), -Mathf.Sin(shinAngle))));
                float legacySpan = Mathf.Abs(Vector3.Dot(seat.LowerLegDir,
                    seat.SpineRotation * new Vector3(0f, -Mathf.Cos(shinAngle), -Mathf.Sin(shinAngle))));

                Assert.Greater(unifiedSpan, 0.99f,
                    $"the solved shin only covers {unifiedSpan:F4} of the seat's shin line for the {name} "
                    + "body. The shin lies along that line by construction, so this has to stay near 1.");
                Assert.Less(legacySpan, unifiedSpan,
                    $"the legacy shin direction is supposed to under-read the span ({legacySpan:F4} vs "
                    + $"{unifiedSpan:F4}) — that under-read is the whole reason a raked seat moves at all.");

                float shift = (LegacyLocalPelvis(seat, legs) - BasisSeatFit.Solve(seat, legs).Back).magnitude;
                Assert.Less(shift, 0.01f,
                    $"the {name} body moved {shift * 1000f:F2} mm on a raked-shin seat. A sub-centimetre "
                    + "correction is the intended cost of fixing the shin direction; anything larger is a "
                    + "visible reseat of existing content and needs to be looked at.");
            }
        }

        [Test]
        public void PelvisBackOffset_ShrinksAsTheSeatReclines()
        {
            BasisSeatFitLegs legs = Body(0.42f, 0.40f, 0.08f);
            float total = legs.UpperLegLength + legs.LowerLegLength;
            float spineBackThickness = total * BasisSeatFit.SpineBackThicknessRatio;
            float upperLegBackRadius = total * BasisSeatFit.UpperLegBackRadiusRatio;

            float previousUnified = float.MaxValue;
            float previousLegacy = float.MaxValue;
            bool legacyEverGrew = false;

            for (float spineAngle = 60f; spineAngle <= 150f; spineAngle += 10f)
            {
                float unified = BasisSeat.GetAdjustmentScalar(Mathf.Clamp(180f - spineAngle, 10f, 170f),
                    spineBackThickness, upperLegBackRadius, legs.UpperLegLength);
                float legacy = BasisSeat.GetAdjustmentScalar(Mathf.Clamp(spineAngle, 10f, 170f),
                    spineBackThickness, upperLegBackRadius, legs.UpperLegLength);

                Assert.LessOrEqual(unified, previousUnified + 1e-5f,
                    $"the pelvis clearance grew to {unified:F4} m at a {spineAngle:F0} degree spine angle "
                    + $"(was {previousUnified:F4} m). A more open backrest corner cannot need MORE forward "
                    + "clearance — that is the inversion the local path had.");

                if (legacy > previousLegacy + 1e-5f) legacyEverGrew = true;
                previousUnified = unified;
                previousLegacy = legacy;
            }

            Assert.IsTrue(legacyEverGrew,
                "the legacy local argument is supposed to grow the clearance as the seat reclines — that is "
                + "the defect this convention replaced. It no longer does, so the monotonicity check above "
                + "is not actually discriminating between the two.");
        }

        [Test]
        public void ThighSpansThePan_AtEverySpineAngle()
        {
            BasisSeatFitLegs legs = Body(0.42f, 0.40f, 0.08f);
            float total = legs.UpperLegLength + legs.LowerLegLength;
            float thighAngleVsSeat = Mathf.Asin(
                (total * BasisSeatFit.UpperLegBackRadiusRatio - total * BasisSeatFit.UpperLegKneeRadiusRatio) / legs.UpperLegLength);

            for (float spineAngle = 70f; spineAngle <= 140f; spineAngle += 10f)
            {
                BasisSeatFitFrame seat = Chair(spineAngle);

                float angle = thighAngleVsSeat + Mathf.Deg2Rad * spineAngle;
                Vector3 unified = seat.SpineRotation * new Vector3(0f, Mathf.Cos(angle), Mathf.Sin(angle));
                Vector3 legacyLocal = seat.SpineRotation * new Vector3(0f, -Mathf.Cos(angle), Mathf.Sin(angle));

                float unifiedSpan = Mathf.Abs(Vector3.Dot(seat.UpperLegDir, unified));
                float legacySpan = Mathf.Abs(Vector3.Dot(seat.UpperLegDir, legacyLocal));

                Assert.Greater(unifiedSpan, 0.95f,
                    $"at a {spineAngle:F0} degree spine angle the solved thigh only covers {unifiedSpan:F3} of "
                    + "the pan. The pan is flat and the thigh lies on it, so this has to stay near 1 "
                    + "regardless of how the backrest is angled.");

                if (spineAngle >= 110f)
                {
                    Assert.Less(legacySpan, 0.9f,
                        $"the legacy local direction is supposed to under-read the thigh span on a reclined "
                        + $"({spineAngle:F0} degree) seat — that under-read is what shifted the pelvis. It now "
                        + $"reads {legacySpan:F3}, so this test is no longer exercising the defect.");
                }
            }
        }

        [Test]
        public void PelvisStaysNearTheAuthoredPoint_ForEveryBody()
        {
            foreach (float spineAngle in new[] { 75f, 90f, 120f })
            {
                BasisSeatFitFrame frame = Chair(spineAngle);

                foreach ((string name, BasisSeatFitLegs legs) in Bodies())
                {
                    float drift = (BasisSeatFit.Solve(frame, legs).Back - AuthoredPelvis(frame, legs)).magnitude;

                    Assert.LessOrEqual(drift, BasisSeatFit.MaxBackShift + 1e-4f,
                        $"the {name} body's pelvis slid {drift:F3} m off the authored seat point on a "
                        + $"{spineAngle:F0} degree seat, past the {BasisSeatFit.MaxBackShift:F2} m bound. An "
                        + "avatar that fits the seat badly should end up seated badly, not seated in mid-air.");
                }
            }
        }

        [Test]
        public void LegacyRemotePin_CouldSlideOffTheSeat()
        {
            BasisSeatFitLegs tiny = Body(0.21f, 0.20f, 0.04f);

            foreach (float spineAngle in new[] { 90f, 118f, 135f })
            {
                BasisSeatFitFrame frame = Chair(spineAngle);
                Vector3 preferred = AuthoredPelvis(frame, tiny);

                float legacyDrift = (LegacyRemotePelvis(frame, tiny) - preferred).magnitude;
                float unifiedDrift = (BasisSeatFit.Solve(frame, tiny).Back - preferred).magnitude;

                Assert.Greater(legacyDrift, BasisSeatFit.MaxBackShift,
                    $"the legacy remote pin only drifted {legacyDrift:F3} m on a {spineAngle:F0} degree seat "
                    + "for an avatar whose legs are far too short for the pan. Its missing clamp is what let "
                    + "an occupant appear off the cushion to everyone else, so this negative must keep "
                    + "reproducing it.");
                Assert.LessOrEqual(unifiedDrift, BasisSeatFit.MaxBackShift + 1e-4f,
                    $"the unified solve drifted {unifiedDrift:F3} m on a {spineAngle:F0} degree seat, past the "
                    + $"{BasisSeatFit.MaxBackShift:F2} m bound it inherited from the local path.");
            }
        }

        private static Vector3 AuthoredPelvis(in BasisSeatFitFrame frame, in BasisSeatFitLegs legs)
        {
            float total = legs.UpperLegLength + legs.LowerLegLength;
            return frame.Back
                + frame.UpperLegPerp * (total * BasisSeatFit.UpperLegBackRadiusRatio)
                + frame.UpperLegDir * BasisSeat.GetAdjustmentScalar(
                    Mathf.Clamp(180f - frame.SpineAngleDegrees, 10f, 170f),
                    total * BasisSeatFit.SpineBackThicknessRatio,
                    total * BasisSeatFit.UpperLegBackRadiusRatio,
                    legs.UpperLegLength);
        }

        [Test]
        public void DegenerateBodiesAndSeats_StayFinite()
        {
            var seats = new[]
            {
                Chair(0.5f), Chair(179.5f), Chair(90f),
                Seat(Vector3.zero, new Vector3(0f, 0f, 0.01f), new Vector3(0f, -0.01f, 0.01f), 90f),
            };
            var bodies = new[] { Body(0f, 0f, 0f), Body(1e-5f, 1e-5f, 0f), Body(4f, 0.01f, 0f), Body(0.01f, 4f, 2f) };

            foreach (BasisSeatFitFrame seat in seats)
            {
                foreach (BasisSeatFitLegs legs in bodies)
                {
                    BasisSeatFitResult fit = BasisSeatFit.Solve(seat, legs);
                    RemotePin(seat, legs, Matrix4x4.identity, Quaternion.identity, out Vector3 pos, out Quaternion rot);

                    Assert.IsTrue(IsFinite(fit.Back) && IsFinite(fit.Knee) && IsFinite(fit.Foot),
                        $"the solve produced a non-finite target ({fit.Back}, {fit.Knee}, {fit.Foot}) for a "
                        + $"{seat.SpineAngleDegrees:F1} degree seat and a "
                        + $"{legs.UpperLegLength}/{legs.LowerLegLength} body.");
                    Assert.IsTrue(IsFinite(pos), $"the remote pin produced a non-finite position {pos}");
                    Assert.IsTrue(IsFinite(new Vector3(rot.x, rot.y, rot.z)) && !float.IsNaN(rot.w),
                        $"the remote pin produced a non-finite rotation {rot}");
                }
            }
        }

        [Test]
        public void BuildFrame_MatchesTheSeatsPublishedGeometry()
        {
            Assert.IsTrue(BasisSeatFit.BuildFrame(new Vector3(0f, 0f, -0.25f), new Vector3(0f, -0.5f, 0.25f),
                new Vector3(0f, 0f, 0.25f), 90f, out BasisSeatFitFrame frame));

            Assert.Less((frame.UpperLegDir - Vector3.forward).magnitude, 1e-5f, "pan runs seat-forward");
            Assert.Less((frame.LowerLegDir - Vector3.down).magnitude, 1e-5f, "shins hang seat-down");
            Assert.Less((frame.UpperLegPerp - Vector3.up).magnitude, 1e-5f, "the pan's normal is seat-up");
            Assert.Less((frame.LowerLegPerp - Vector3.forward).magnitude, 1e-5f, "the front face's normal is seat-forward");
            Assert.AreEqual(90f, frame.LegAngleDegrees, 1e-3f, "a square chair bends 90 degrees at the knee");
            Assert.Less(Quaternion.Angle(frame.SpineRotation, Quaternion.identity), 1e-3f,
                "a square chair's hips frame is the seat's own frame");

            Assert.IsFalse(BasisSeatFit.BuildFrame(Vector3.zero, new Vector3(0f, 0f, 2f), new Vector3(0f, 0f, 1f), 90f, out _),
                "collinear control points leave no seat plane and must be reported, not silently solved");
        }

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
                && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }
    }
}
