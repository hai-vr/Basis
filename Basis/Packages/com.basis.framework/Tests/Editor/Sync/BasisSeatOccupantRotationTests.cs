using Basis.Scripts.BasisSdk.Interactions;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// Occupant rotation on seats (issue #538): a seat authors how far its occupant may turn themselves and
    /// in what steps, so a stool can be spun, a bench can allow a glance either way, and a chair can hold
    /// its occupant facing forward.
    ///
    /// Two properties matter beyond the arithmetic. The turn must be a pure spin about the occupant's own
    /// spine — turning on a stool cannot slide the pelvis around the seat's origin. And the yaw the occupant
    /// settles on is authoritative: remotes apply it verbatim and never re-resolve, because a seat whose
    /// limits changed mid-session (or whose step does not divide 360) would otherwise resolve to a different
    /// answer on each client and the occupant would face two directions at once.
    /// </summary>
    public sealed class BasisSeatOccupantRotationTests
    {
        private GameObject _go;
        private BasisSeat _seat;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject(nameof(BasisSeatOccupantRotationTests));
            _seat = _go.AddComponent<BasisSeat>();
            _seat.SetPoints(new Vector3(0f, 0f, -0.25f), new Vector3(0f, -0.5f, 0.25f), new Vector3(0f, 0f, 0.25f), 90.0);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        private static BasisSeatRotationLimits Limits(float range, float snap) => new BasisSeatRotationLimits(range, snap);

        // ── Defaults: existing content must not start rotating ──

        /// <summary>
        /// Seats did nothing when you tried to turn on them, and the maintainer asked for that to stay the
        /// default so other projects can choose their own behaviour. A fresh seat must therefore hold its
        /// occupant forward no matter what is thrown at it.
        /// </summary>
        [Test]
        public void AFreshSeat_HoldsItsOccupantFacingForward()
        {
            Assert.AreEqual(0f, _seat.OccupantRotationRangeDegrees,
                "a seat must default to no occupant rotation — anything else silently changes every "
                + "already-authored chair in every world.");

            foreach (float delta in new[] { 5f, -30f, 400f, -1000f })
            {
                Assert.IsFalse(_seat.TurnOccupant(delta),
                    $"turning a held seat by {delta} reported a change");
                Assert.AreEqual(0f, _seat.OccupantYawDegrees, 1e-5f,
                    $"a held seat let its occupant turn to {_seat.OccupantYawDegrees} degrees");
            }

            Assert.IsFalse(_seat.SetOccupantYaw(90f), "a held seat accepted an absolute yaw");
            Assert.AreEqual(0f, _seat.OccupantYawDegrees, 1e-5f);
        }

        /// <summary>A held seat composes exactly the pose it composed before occupant rotation existed.</summary>
        [Test]
        public void AHeldSeat_ComposesTheSamePoseAsBeforeRotationExisted()
        {
            var legs = new BasisSeatFitLegs { UpperLegLength = 0.42f, LowerLegLength = 0.40f, FootThickness = 0.08f };
            _go.transform.SetPositionAndRotation(new Vector3(2f, 0.5f, -3f), Quaternion.Euler(0f, 37f, 0f));

            _seat.CalculateSeatPositionRotation(legs, out Quaternion rot, out Vector3 pos);

            BasisSeatFitResult fit = BasisSeatFit.Solve(_seat.GetFitFrame(), legs);
            BasisSeatFit.ComposeHipsWorld(_go.transform.localToWorldMatrix, _go.transform.rotation,
                _seat.SpineRotation, fit.Back, out Vector3 expectedPos, out Quaternion expectedRot);

            Assert.Less((pos - expectedPos).magnitude, 1e-5f,
                $"a held seat moved its occupant {(pos - expectedPos).magnitude * 1000f:F3} mm");
            Assert.Less(Quaternion.Angle(rot, expectedRot), 1e-3f,
                $"a held seat turned its occupant {Quaternion.Angle(rot, expectedRot):F4} degrees");
        }

        // ── Range ──

        /// <summary>
        /// The issue asks for "allow rotation degrees from center, example 180, 360, 90, 45" — a total sweep
        /// centred on the seat's forward, so 90 means 45 degrees either way.
        /// </summary>
        [Test]
        public void Range_BoundsTheTurnEitherWayFromTheSeatsForward()
        {
            foreach (float range in new[] { 45f, 90f, 180f })
            {
                _seat.OccupantRotationSnapDegrees = 0f;
                _seat.OccupantRotationRangeDegrees = range;
                float limit = range * 0.5f;

                _seat.SetOccupantYaw(1000f);
                Assert.AreEqual(limit, _seat.OccupantYawDegrees, 1e-3f,
                    $"a {range} degree seat let its occupant reach {_seat.OccupantYawDegrees} degrees, past "
                    + $"the {limit} it allows either way.");

                _seat.SetOccupantYaw(-1000f);
                Assert.AreEqual(-limit, _seat.OccupantYawDegrees, 1e-3f,
                    $"a {range} degree seat let its occupant reach {_seat.OccupantYawDegrees} degrees.");

                _seat.SetOccupantYaw(limit * 0.5f);
                Assert.AreEqual(limit * 0.5f, _seat.OccupantYawDegrees, 1e-3f,
                    "a yaw inside the range must pass through untouched");
            }
        }

        /// <summary>360 is a free spin — a stool. The yaw stays in (-180, 180] so it survives the wire.</summary>
        [Test]
        public void FullCircle_SpinsFreely_AndStaysWrapped()
        {
            _seat.OccupantRotationRangeDegrees = 360f;

            foreach (float request in new[] { 0f, 90f, 179f, 181f, 359f, 720f, -450f })
            {
                _seat.SetOccupantYaw(request);
                float yaw = _seat.OccupantYawDegrees;

                Assert.LessOrEqual(yaw, 180f + 1e-3f, $"yaw {yaw} escaped the wrapped range");
                Assert.Greater(yaw, -180f - 1e-3f, $"yaw {yaw} escaped the wrapped range");
                Assert.AreEqual(0f, Mathf.DeltaAngle(request, yaw), 1e-2f,
                    $"a free-spin seat changed the requested facing from {request} to {yaw} — it is only "
                    + "supposed to wrap it.");
            }
        }

        // ── Snap ──

        /// <summary>
        /// The issue asks for "snap degrees 25, 30, 45, 90". Every reachable facing must be a multiple of
        /// the step.
        /// </summary>
        [Test]
        public void Snap_QuantisesEveryReachableFacing()
        {
            foreach (float snap in new[] { 25f, 30f, 45f, 90f })
            {
                _seat.OccupantRotationRangeDegrees = 360f;
                _seat.OccupantRotationSnapDegrees = snap;

                for (float request = -180f; request <= 180f; request += 7f)
                {
                    _seat.SetOccupantYaw(request);
                    float yaw = _seat.OccupantYawDegrees;
                    float offStep = Mathf.Abs(Mathf.DeltaAngle(yaw, Mathf.Round(yaw / snap) * snap));

                    Assert.Less(offStep, 1e-2f,
                        $"a {snap} degree snap seat settled on {yaw}, which is {offStep:F3} degrees off a "
                        + "step boundary.");
                    Assert.LessOrEqual(Mathf.Abs(Mathf.DeltaAngle(request, yaw)), snap * 0.5f + 1e-2f,
                        $"a {snap} degree snap moved a request of {request} all the way to {yaw} — snapping "
                        + "should never travel more than half a step.");
                }
            }
        }

        /// <summary>
        /// Range and snap together must not fight: with a 90 degree range (45 either way) and a 30 degree
        /// step, 60 is a legal step but outside the range and 45 is inside the range but not a step. Only
        /// -30, 0 and 30 are reachable, and the solver has to pick the outermost legal step rather than
        /// hand back an illegal value from either rule.
        /// </summary>
        [Test]
        public void SnapAndRangeTogether_OnlyOfferStepsThatAreAlsoInsideTheRange()
        {
            _seat.OccupantRotationRangeDegrees = 90f;
            _seat.OccupantRotationSnapDegrees = 30f;

            var reached = new System.Collections.Generic.HashSet<float>();
            for (float request = -180f; request <= 180f; request += 3f)
            {
                _seat.SetOccupantYaw(request);
                reached.Add(Mathf.Round(_seat.OccupantYawDegrees * 100f) / 100f);
            }

            CollectionAssert.AreEquivalent(new[] { -30f, 0f, 30f }, reached,
                "a 90 degree range with a 30 degree step should offer exactly -30, 0 and 30. Got: "
                + string.Join(", ", reached));
        }

        /// <summary>
        /// A step wider than the range leaves only the centre reachable — the seat is effectively held, and
        /// must not hand back a half-step or the range limit as a consolation.
        /// </summary>
        [Test]
        public void AStepWiderThanTheRange_LeavesOnlyTheCentre()
        {
            _seat.OccupantRotationRangeDegrees = 40f;
            _seat.OccupantRotationSnapDegrees = 90f;

            foreach (float request in new[] { -180f, -20f, -5f, 5f, 20f, 180f })
            {
                _seat.SetOccupantYaw(request);
                Assert.AreEqual(0f, _seat.OccupantYawDegrees, 1e-3f,
                    $"a 40 degree range with a 90 degree step settled on {_seat.OccupantYawDegrees} for a "
                    + $"request of {request}; no step other than 0 fits inside that range.");
            }
        }

        /// <summary>
        /// A smooth turn axis feeds deltas far smaller than a snap step. Resolving in place each frame would
        /// round every delta straight back to where it started and the occupant would never move, so the raw
        /// request accumulates separately and only the applied yaw snaps.
        /// </summary>
        [Test]
        public void SmoothTurnInput_AccumulatesUntilItCrossesASnapStep()
        {
            _seat.OccupantRotationRangeDegrees = 360f;
            _seat.OccupantRotationSnapDegrees = 45f;

            int changes = 0;
            for (int frame = 0; frame < 40; frame++)
            {
                if (_seat.TurnOccupant(3f))
                {
                    changes++;
                }
            }

            Assert.AreEqual(90f, _seat.OccupantYawDegrees, 1e-3f,
                $"forty 3 degree turns (120 raw) on a 45 degree snap seat should land on 90; landed on "
                + $"{_seat.OccupantYawDegrees}. If this is 0 the accumulation is being rounded away every "
                + "frame and smooth input can never move a snapped seat.");
            Assert.AreEqual(2, changes,
                $"the applied yaw should have moved exactly twice (at 45 and at 90), not {changes} times — "
                + "snapping is what keeps a spinning stool nearly free on the wire.");
        }

        /// <summary>Shrinking the range under an occupant pulls them back inside it rather than leaving them out.</summary>
        [Test]
        public void ShrinkingTheRange_PullsTheOccupantBackInside()
        {
            _seat.OccupantRotationRangeDegrees = 360f;
            _seat.SetOccupantYaw(170f);
            Assert.AreEqual(170f, _seat.OccupantYawDegrees, 1e-3f, "sanity: the occupant should be turned right round");

            _seat.OccupantRotationRangeDegrees = 60f;
            Assert.AreEqual(30f, _seat.OccupantYawDegrees, 1e-3f,
                $"after the range shrank to 60 the occupant is still at {_seat.OccupantYawDegrees} degrees, "
                + "outside what the seat now allows.");
        }

        // ── The turn is a spin about the occupant, not a slide around the seat ──

        /// <summary>
        /// Turning on a stool must not move the pelvis. The pelvis is the pivot, so the position it was
        /// solved onto has to come back byte-for-byte at every facing, while the rotation carries the whole
        /// turn. If the seat origin were the pivot instead, a 180 degree turn on this seat would drag the
        /// occupant half a metre off the cushion.
        /// </summary>
        [Test]
        public void TurningSpinsAboutThePelvis_WithoutMovingIt()
        {
            var legs = new BasisSeatFitLegs { UpperLegLength = 0.42f, LowerLegLength = 0.40f, FootThickness = 0.08f };
            Quaternion seatRot = Quaternion.Euler(0f, 41f, 0f);
            Matrix4x4 seatToWorld = Matrix4x4.TRS(new Vector3(1f, 0.45f, 2f), seatRot, Vector3.one);
            BasisSeatFitResult fit = BasisSeatFit.Solve(_seat.GetFitFrame(), legs);

            BasisSeatFit.ComposeHipsWorld(seatToWorld, seatRot, _seat.SpineRotation, fit.Back, 0f,
                out Vector3 basePos, out Quaternion baseRot, out Quaternion basePivot);
            Assert.AreEqual(Quaternion.identity.x, basePivot.x, 1e-6f, "an unturned occupant needs no pivot rotation");

            foreach (float yaw in new[] { 15f, -45f, 90f, 180f })
            {
                BasisSeatFit.ComposeHipsWorld(seatToWorld, seatRot, _seat.SpineRotation, fit.Back, yaw,
                    out Vector3 pos, out Quaternion rot, out Quaternion pivot);

                Assert.Less((pos - basePos).magnitude, 1e-5f,
                    $"turning {yaw} degrees slid the pelvis {(pos - basePos).magnitude * 1000f:F2} mm. The "
                    + "pelvis is the pivot — a stool spins its occupant in place.");
                Assert.AreEqual(Mathf.Abs(Mathf.DeltaAngle(0f, yaw)), Quaternion.Angle(rot, baseRot), 1e-2f,
                    $"a {yaw} degree turn rotated the occupant by {Quaternion.Angle(rot, baseRot):F3} degrees.");

                Vector3 spineAxis = baseRot * Vector3.up;
                Assert.Less(Vector3.Angle(rot * Vector3.up, spineAxis), 1e-2f,
                    $"a {yaw} degree turn tipped the spine axis; the turn must be a pure twist about it.");

                Vector3 aheadOfPelvis = basePos + baseRot * (Vector3.forward * 0.4f);
                Vector3 turned = BasisSeatFit.RotateAboutPivot(aheadOfPelvis, basePos, pivot);
                Assert.Less((turned - (basePos + rot * (Vector3.forward * 0.4f))).magnitude, 1e-4f,
                    $"the pivot rotation does not carry seat-space points (the foot targets) with the body "
                    + $"at {yaw} degrees, so the feet would stay pointing down the seat while the body swivels.");
            }
        }

        /// <summary>The occupant's facing must reach the remote pin, or only its owner sees the turn.</summary>
        [Test]
        public void TheRemotePinCarriesTheOccupantsFacing()
        {
            var legs = new BasisSeatFitLegs { UpperLegLength = 0.42f, LowerLegLength = 0.40f, FootThickness = 0.08f };
            _go.transform.SetPositionAndRotation(new Vector3(-4f, 0f, 1.5f), Quaternion.Euler(0f, 118f, 0f));
            _seat.OccupantRotationRangeDegrees = 360f;

            _seat.SetOccupantYaw(0f);
            _seat.CalculateSeatPositionRotation(legs, out Quaternion forwardRot, out Vector3 forwardPos);

            foreach (float yaw in new[] { 30f, -75f, 145f })
            {
                _seat.SetOccupantYaw(yaw);
                _seat.CalculateSeatPositionRotation(legs, out Quaternion rot, out Vector3 pos);

                Assert.Less((pos - forwardPos).magnitude, 1e-5f,
                    "the remote pin moved the pelvis when the occupant turned");
                Assert.AreEqual(Mathf.Abs(yaw), Quaternion.Angle(rot, forwardRot), 1e-2f,
                    $"the remote pin turned the occupant {Quaternion.Angle(rot, forwardRot):F3} degrees "
                    + $"instead of {Mathf.Abs(yaw)}, so everyone else sees them facing the wrong way.");
            }
        }

        // ── Over the network ──

        /// <summary>
        /// The yaw rides in the seat packet. It must survive quantisation far more finely than anyone can
        /// see, and the packet has to stay readable by the shorter forms that predate it.
        /// </summary>
        [Test]
        public void TheSeatPacketCarriesTheYaw_AndStaysBackwardReadable()
        {
            var sync = _go.AddComponent<BasisSeatSync>();
            sync.Seat = _seat;
            _seat.OccupantRotationRangeDegrees = 360f;
            _seat.SetOccupantYaw(123.456f);

            byte[] packet = sync.CreateSeatPacket(true);
            Assert.AreEqual(7, packet.Length, "the seat packet should be occupied + generation + yaw");
            Assert.AreEqual(1, packet[0], "the claim flag must stay in byte 0 where older readers look for it");

            float decoded = BasisSeatSync.DequantizeYaw((short)(packet[5] | (packet[6] << 8)));
            Assert.AreEqual(_seat.OccupantYawDegrees, decoded, 0.01f,
                $"the yaw came back as {decoded} instead of {_seat.OccupantYawDegrees} after a round trip "
                + "through the packet.");

            for (float yaw = -180f; yaw <= 180f; yaw += 3.7f)
            {
                float round = BasisSeatSync.DequantizeYaw(BasisSeatSync.QuantizeYaw(yaw));
                Assert.AreEqual(yaw, round, 0.01f, $"quantising {yaw} lost {Mathf.Abs(yaw - round):F4} degrees");
            }

            Assert.AreEqual(0, BasisSeatSync.QuantizeYaw(float.NaN), "a NaN yaw must not reach the wire");
        }

        /// <summary>
        /// Remotes apply the occupant's yaw verbatim and must never re-resolve it. A step that does not
        /// divide 360 is the case that proves why: 15 is a legal wrapped facing for a 25 degree step, but
        /// re-snapping it locally would move it to 25 and that client alone would show the occupant turned
        /// wrong. Same hazard if a world script widens or narrows the limits mid-session.
        /// </summary>
        [Test]
        public void ARemoteAppliesTheOccupantsYawVerbatim_NeverReResolvingIt()
        {
            _seat.OccupantRotationRangeDegrees = 360f;
            _seat.OccupantRotationSnapDegrees = 25f;

            _seat.ApplyNetworkedOccupantYaw(15f);
            Assert.AreEqual(15f, _seat.OccupantYawDegrees, 1e-4f,
                $"a remote re-snapped a received yaw of 15 to {_seat.OccupantYawDegrees}. Only the occupant "
                + "resolves; everyone else applies, or clients disagree about which way they are facing.");

            _seat.OccupantRotationRangeDegrees = 10f;
            _seat.ApplyNetworkedOccupantYaw(140f);
            Assert.AreEqual(140f, _seat.OccupantYawDegrees, 1e-4f,
                $"a remote clamped a received yaw of 140 to {_seat.OccupantYawDegrees} using its own copy of "
                + "the limits. The occupant's client is the authority on what it resolved against.");

            _seat.ApplyNetworkedOccupantYaw(float.NaN);
            Assert.AreEqual(140f, _seat.OccupantYawDegrees, 1e-4f, "a NaN from the wire must be ignored, not applied");
        }

        /// <summary>
        /// Turning marks the yaw for broadcast, and the flush is rate limited so a smoothly turning occupant
        /// cannot send every frame. The dirty flag has to survive a suppressed frame, otherwise the facing a
        /// turn settles on is the one that never gets sent.
        /// </summary>
        [Test]
        public void TheYawFlushIsRateLimited_ButNeverDropsTheFinalFacing()
        {
            var sync = _go.AddComponent<BasisSeatSync>();
            sync.Seat = _seat;
            _seat.OccupantRotationRangeDegrees = 360f;

            Assert.IsFalse(sync.FlushOccupantYaw(0f),
                "nothing to flush before anyone has turned, and nobody is seated locally anyway");

            _seat.SetOccupantYaw(20f);
            Assert.IsFalse(sync.FlushOccupantYaw(0f),
                "a turn by someone who is not the recorded local occupant must not broadcast");
        }

        /// <summary>Standing up returns the seat to its forward, so the next occupant does not inherit a facing.</summary>
        [Test]
        public void EmptyingTheSeat_ReturnsItToForward()
        {
            _seat.OccupantRotationRangeDegrees = 360f;
            _seat.SetOccupantYaw(120f);
            Assert.AreEqual(120f, _seat.OccupantYawDegrees, 1e-3f, "sanity: the occupant should be turned");

            _seat.ResetOccupantYaw();
            Assert.AreEqual(0f, _seat.OccupantYawDegrees, 1e-5f,
                "the next person to sit here would start out turned 120 degrees");
        }

        // ── Resolver edges ──

        /// <summary>Degenerate limits and requests must not produce a NaN facing.</summary>
        [Test]
        public void DegenerateLimitsAndRequests_StayFinite()
        {
            var limitSets = new[]
            {
                Limits(0f, 0f), Limits(-10f, 30f), Limits(360f, 0f), Limits(720f, 7f),
                Limits(1f, 0.0001f), Limits(90f, 1000f), Limits(float.NaN, 45f),
            };
            var requests = new[] { 0f, 45f, -45f, 1e6f, -1e6f, float.NaN, float.PositiveInfinity, float.NegativeInfinity };

            foreach (BasisSeatRotationLimits limits in limitSets)
            {
                foreach (float request in requests)
                {
                    float resolved = BasisSeatFit.ResolveOccupantYaw(request, limits);
                    Assert.IsFalse(float.IsNaN(resolved) || float.IsInfinity(resolved),
                        $"resolving {request} against range {limits.RangeDegrees}/snap {limits.SnapDegrees} "
                        + $"gave {resolved}");

                    float applied = BasisSeatFit.AddOccupantYaw(0f, request, limits, out float raw);
                    Assert.IsFalse(float.IsNaN(applied) || float.IsInfinity(applied),
                        $"adding {request} against range {limits.RangeDegrees} gave {applied}");
                    Assert.IsFalse(float.IsNaN(raw) || float.IsInfinity(raw),
                        $"adding {request} against range {limits.RangeDegrees} left raw {raw}");
                }
            }
        }
    }
}
