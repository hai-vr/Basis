using System.Collections.Generic;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.Common;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking.Sync;
using Basis.Scripts.TransformBinders.BoneControl;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// A sweep of the space a welded hold can go wrong in, rather than a list of the bugs already found.
    ///
    /// The invariant under test is always the same one: the prop must end up in the same place relative to
    /// the observer's own hand frame as it is relative to the holder's. Because the reconstruction is a
    /// change of coordinates, that invariant holds for any pair of frames the two ends agree on and fails
    /// for any pair they do not — so sweeping hands, rigs, hand sizes, grips, ids and lifetime events is a
    /// direct search for disagreement, and a case that passes here cannot misplace a prop in a session.
    ///
    /// Cases the reconstruction legitimately cannot serve (an observer that cannot build the sender's frame)
    /// assert the honest outcome instead: the prop holds its last pose rather than moving somewhere wrong.
    /// </summary>
    public class BasisPickupWeldMatrixTests : BasisPickupWeldFixture
    {
        // ── the hold, across hands and rigs ──

        [Test]
        public void EitherHand_ReconstructsExactly([Values(true, false)] bool left)
        {
            BuildProductionPair();
            HoldWith(_sender.BasisPickupInteractable, left);
            _ownerWrist.SetPositionAndRotation(new Vector3(1f, 1.4f, -2f), Quaternion.Euler(12f, 40f, -8f));
            _observerWrist.SetPositionAndRotation(new Vector3(-5f, 1.1f, 6f), Quaternion.Euler(-30f, 130f, 20f));

            Vector3 grip = new Vector3(0.03f, -0.015f, 0.11f);
            Quaternion gripRot = Quaternion.Euler(0f, 25f, 90f);
            HoldWelded(grip, gripRot, left);

            AssertObserverMatchesOwner(grip, gripRot, $"left={left} must reconstruct", left);
        }

        /// <summary>
        /// The case the canonical frame exists for: the observer is drawing a copy of the holder rigged with
        /// a different bind orientation. A frame built from joint positions has to survive it; the wrist bone
        /// encoding cannot.
        /// </summary>
        [Test]
        public void ObserverBindRotation_DoesNotMoveTheGrip(
            [Values(0f, 25f, 90f, 179f)] float yaw,
            [Values(0f, 40f, -70f)] float pitch)
        {
            BuildProductionPair(observerBind: Quaternion.Euler(pitch, yaw, 0f));
            _ownerWrist.position = new Vector3(1f, 1.4f, -2f);
            _observerWrist.position = new Vector3(-5f, 1.1f, 6f);

            Vector3 grip = new Vector3(0.02f, -0.01f, 0.1f);
            Quaternion gripRot = Quaternion.Euler(10f, 20f, 30f);
            HoldWelded(grip, gripRot);

            AssertObserverMatchesOwner(grip, gripRot, $"bind ({pitch},{yaw}) must not move the grip");
        }

        /// <summary>
        /// Offsets travel as multiples of hand length, so a differently sized hand holds the prop
        /// proportionally rather than at the holder's absolute reach. That is deliberate, and it is the one
        /// place the two ends are allowed to produce different world positions.
        /// </summary>
        [Test]
        public void ObserverHandSize_ScalesTheHoldProportionally([Values(0.6f, 1f, 1.5f, 2.2f)] float handScale)
        {
            BuildProductionPair(observerHandScale: handScale);
            _ownerWrist.position = new Vector3(0f, 1.3f, 0f);
            _observerWrist.position = new Vector3(-5f, 1.3f, 6f);

            Vector3 grip = new Vector3(0.02f, 0f, 0.06f);
            HoldWelded(grip, Quaternion.identity);

            AssertObserverMatchesOwner(grip, Quaternion.identity,
                $"hand scale {handScale} must hold proportionally", left: true, handRatio: handScale);
        }

        /// <summary>
        /// A deterministic sweep of grips rather than the handful anyone thinks to write down: near the palm,
        /// out at arm's length, behind the hand, and through orientations that cross the quaternion sign flip
        /// and the look-rotation poles.
        /// </summary>
        [Test]
        public void GripSweep_EveryOffsetAndOrientationSurvivesTheRoundTrip()
        {
            BuildProductionPair(observerBind: Quaternion.Euler(15f, -120f, 40f), observerHandScale: 1.3f);
            _ownerWrist.SetPositionAndRotation(new Vector3(2f, 1.4f, -3f), Quaternion.Euler(5f, 70f, -15f));
            _observerWrist.SetPositionAndRotation(new Vector3(-7f, 0.9f, 4f), Quaternion.Euler(-20f, 200f, 35f));

            var grips = new List<Vector3>();
            foreach (float x in new[] { -0.4f, -0.02f, 0f, 0.02f, 0.4f })
            {
                foreach (float y in new[] { -0.25f, 0f, 0.25f })
                {
                    foreach (float z in new[] { -0.3f, 0f, 0.12f, 0.9f })
                    {
                        grips.Add(new Vector3(x, y, z));
                    }
                }
            }
            var rotations = new List<Quaternion>
            {
                Quaternion.identity,
                Quaternion.Euler(0f, 0f, 179.5f),
                Quaternion.Euler(90f, 0f, 0f),
                Quaternion.Euler(-90f, 0f, 0f),
                Quaternion.Euler(37f, 214f, -88f),
                Quaternion.Euler(180f, 180f, 180f),
            };

            for (int g = 0; g < grips.Count; g++)
            {
                Quaternion gripRot = rotations[g % rotations.Count];
                HoldWelded(grips[g], gripRot);
                // Consecutive sweep entries are far apart, and the receiver interpolates between the pose it
                // was showing and the new one — so settle before asserting, or the assertion is about the
                // jitter buffer rather than about the weld.
                Settle();
                AssertObserverMatchesOwner(grips[g], gripRot,
                    $"grip {grips[g]} / {gripRot.eulerAngles} must survive", left: true, handRatio: 1.3f);
            }
        }

        /// <summary>
        /// The single case the sweep first tripped on, held on its own from a settled start: a grip well
        /// outside the palm at an orientation on the look-rotation pole. Isolated so a failure here means the
        /// weld, and a failure only in the sweep means interpolation.
        /// </summary>
        [Test]
        public void ExtremeGrip_SurvivesOnItsOwn()
        {
            BuildProductionPair(observerBind: Quaternion.Euler(15f, -120f, 40f), observerHandScale: 1.3f);
            _ownerWrist.SetPositionAndRotation(new Vector3(2f, 1.4f, -3f), Quaternion.Euler(5f, 70f, -15f));
            _observerWrist.SetPositionAndRotation(new Vector3(-7f, 0.9f, 4f), Quaternion.Euler(-20f, 200f, 35f));

            Vector3 grip = new Vector3(-0.4f, -0.25f, 0.12f);
            Quaternion gripRot = Quaternion.Euler(90f, 0f, 0f);
            HoldWelded(grip, gripRot);
            Settle();

            AssertObserverMatchesOwner(grip, gripRot,
                "a grip far outside the palm must reconstruct like any other", left: true, handRatio: 1.3f);
        }

        /// <summary>
        /// The holder walks while holding. The offset is rigid, so the prop must stay put in the hand for
        /// every frame of it — no accumulation, no lag that grows with distance travelled.
        /// </summary>
        [Test]
        public void MovingHolder_HoldsWithoutDrift()
        {
            BuildProductionPair();
            Vector3 grip = new Vector3(0.02f, 0.01f, 0.09f);
            Quaternion gripRot = Quaternion.Euler(0f, 45f, 0f);

            for (int frame = 0; frame < 30; frame++)
            {
                float t = frame * 0.1f;
                _ownerWrist.SetPositionAndRotation(new Vector3(t, 1.3f + Mathf.Sin(t) * 0.2f, -t * 0.5f),
                    Quaternion.Euler(t * 7f, t * 23f, t * 3f));
                _observerWrist.SetPositionAndRotation(new Vector3(-5f + t, 1.3f + Mathf.Sin(t) * 0.2f, 6f - t * 0.5f),
                    Quaternion.Euler(t * 7f, t * 23f, t * 3f));
                HoldWelded(grip, gripRot);
                AssertObserverMatchesOwner(grip, gripRot, $"frame {frame} must not drift");
            }
        }

        // ── ids and lifetime ──

        /// <summary>
        /// Neither end can build the canonical basis, so both fall back to the raw wrist bone in metres —
        /// the one frame any receiver can always reproduce — and the id says so. Still exact.
        /// </summary>
        [Test]
        public void NeitherEndHasFingers_LegacyWristFrameStillReconstructs()
        {
            BuildProductionPair(observerFingers: false, ownerFingers: false);
            SeedLegacyHandCache();
            _ownerWrist.SetPositionAndRotation(new Vector3(0.5f, 1.2f, 0.3f), Quaternion.Euler(10f, 30f, 0f));
            _observerWrist.SetPositionAndRotation(new Vector3(-3f, 1.2f, 2f), Quaternion.Euler(10f, 30f, 0f));

            Vector3 grip = new Vector3(0.05f, -0.02f, 0.1f);
            Quaternion gripRot = Quaternion.Euler(15f, 0f, 45f);
            _ownerWrist.GetPositionAndRotation(out Vector3 hp, out Quaternion hr);
            _sender.Target.SetPositionAndRotation(hp + hr * grip, hr * gripRot);
            Transmit();

            _observerWrist.GetPositionAndRotation(out Vector3 ohp, out Quaternion ohr);
            Assert.LessOrEqual(Vector3.Distance(_remote.Target.position, ohp + ohr * grip), 0.005f,
                "a fingerless pair must still hold the prop, in metres against the wrist");
        }

        /// <summary>
        /// The holder's avatar has no fingers so it streams the legacy wrist id, while the observer's copy
        /// does. The legacy id names a frame both can reproduce, so the richer observer must decode against
        /// the wrist too rather than "upgrading" to its own palm frame.
        /// </summary>
        [Test]
        public void OwnerWithoutFingers_ObserverWithThem_BothUseTheWristFrame()
        {
            BuildProductionPair(observerFingers: true, ownerFingers: false);
            SeedLegacyHandCache();
            _ownerWrist.SetPositionAndRotation(new Vector3(0f, 1.3f, 0f), Quaternion.identity);
            _observerWrist.SetPositionAndRotation(new Vector3(-5f, 1.3f, 6f), Quaternion.identity);

            Vector3 grip = new Vector3(0.03f, 0f, 0.07f);
            _ownerWrist.GetPositionAndRotation(out Vector3 hp, out Quaternion hr);
            _sender.Target.SetPositionAndRotation(hp + hr * grip, hr);
            Transmit();

            _observerWrist.GetPositionAndRotation(out Vector3 ohp, out Quaternion ohr);
            Assert.LessOrEqual(Vector3.Distance(_remote.Target.position, ohp + ohr * grip), 0.005f,
                "the id names the space; a receiver with a richer rig must still decode in it");
        }

        /// <summary>
        /// The holder swaps avatar mid-hold: the observer's driver rebuilds its references onto a new rig.
        /// The prop must move to the new hand, not stay welded to the old one.
        /// </summary>
        [Test]
        public void ObserverAvatarSwapMidHold_ReweldsOntoTheNewRig()
        {
            BuildProductionPair();
            _ownerWrist.position = new Vector3(0f, 1.3f, 0f);
            _observerWrist.position = new Vector3(-5f, 1.3f, 6f);

            Vector3 grip = new Vector3(0.02f, 0f, 0.08f);
            HoldWelded(grip, Quaternion.identity);
            AssertObserverMatchesOwner(grip, Quaternion.identity, "baseline before the swap");

            _observerWrist = MakeRig("holder-on-observer-v2", true, Quaternion.Euler(0f, 90f, 0f), 1.6f,
                out _observerMapping, out BasisAvatar swapped);
            _observerWrist.position = new Vector3(3f, 0.9f, -4f);
            _remote.currentOwnedPlayer = MakeRemoteView(swapped, _observerMapping);

            HoldWelded(grip, Quaternion.identity);
            AssertObserverMatchesOwner(grip, Quaternion.identity,
                "after an avatar swap the prop belongs to the new hand", left: true, handRatio: 1.6f);
        }

        /// <summary>
        /// Release: the id goes to none and the channels go back to meaning a world pose. The prop must land
        /// on the world pose, not keep reading it as a hand-relative offset.
        /// </summary>
        [Test]
        public void ReleaseEdge_ReturnsToWorldPose()
        {
            BuildProductionPair();
            _ownerWrist.position = new Vector3(0f, 1.3f, 0f);
            _observerWrist.position = new Vector3(-5f, 1.3f, 6f);
            HoldWelded(new Vector3(0.02f, 0f, 0.08f), Quaternion.identity);

            // Let go: no input is interacting any more, so the transmit falls through to world streaming.
            _sender.BasisPickupInteractable.Inputs.leftHand = default;
            _sender.BasisPickupInteractable.Inputs.rightHand = default;
            Vector3 dropped = new Vector3(4f, 0.2f, -1f);
            _sender.Target.SetPositionAndRotation(dropped, Quaternion.Euler(0f, 200f, 0f));

            // The receive side holds the last in-hand pose for the single frame the snap lands on, so the
            // world pose arrives on the frame after the edge.
            Transmit();
            Tick();

            Assert.LessOrEqual(Vector3.Distance(_remote.Target.position, dropped), 0.005f,
                "a released prop must decode as a world pose again");
        }

        // ── the pose the grip is measured against ──

        /// <summary>
        /// The holder welds the prop to the POST-IK hand (BasisLocalBoneControl.IKWorldData, published at the
        /// end of FinishSimulate) but the transmit used to measure it against the LIVE bone transform. Those
        /// are not the same pose during LateUpdate: with the animator graph in GameTime mode
        /// (BasisLocalRigDriver.EngineDrivenAnimatorEvaluate) the engine rewrites the avatar's bones in
        /// PreLateUpdate, so when TransmitOwned runs the live bone carries the ANIMATED pose while the prop is
        /// still sitting on the SOLVED one from the previous frame.
        ///
        /// Measuring a grip against a hand the prop is not on ships an offset no observer can undo: wrong
        /// position AND wrong rotation, on every remote, for every prop, while the holder's own screen is
        /// perfect. This stages that split directly.
        /// </summary>
        [Test]
        public void TransmitMeasuresAgainstTheSolvedHand_NotTheAnimatedOne()
        {
            BuildProductionPair();

            // Where the solver actually put the hand, and therefore where the prop is welded.
            Vector3 solvedPos = new Vector3(0.4f, 1.35f, 0.35f);
            Quaternion solvedRot = Quaternion.Euler(-20f, 55f, 10f);
            PublishPostIKHandPose(solvedPos, solvedRot);

            // What the engine's animation stage left in the live bone: an idle arm, nowhere near the solve.
            _ownerWrist.SetPositionAndRotation(new Vector3(0.2f, 0.95f, -0.05f), Quaternion.Euler(70f, -30f, 0f));
            _observerWrist.SetPositionAndRotation(new Vector3(-5f, 1.35f, 6f), Quaternion.Euler(-20f, 55f, 10f));

            Assert.IsTrue(BasisHandGrip.TryGetFrame(BasisLocalAvatarDriver.Mapping, true, solvedPos, solvedRot,
                out BasisHandFrame weldFrame), "solved-hand frame");
            Vector3 grip = new Vector3(0.02f, -0.01f, 0.09f);
            Quaternion gripRot = Quaternion.Euler(0f, 30f, 0f);

            // The prop is welded to the solved hand, which is what the holder sees.
            _sender.Target.SetPositionAndRotation(weldFrame.Position + weldFrame.Rotation * grip, weldFrame.Rotation * gripRot);
            Transmit();
            Settle();

            // The observer's copy of that same solved hand must therefore hold it at the same grip.
            Assert.IsTrue(BasisHandGrip.TryGetFrame(_observerMapping, true,
                _observerWrist.position, _observerWrist.rotation, out BasisHandFrame observerFrame));
            Assert.LessOrEqual(Vector3.Distance(_remote.Target.position, observerFrame.Position + observerFrame.Rotation * grip), 0.005f,
                "the grip must be measured against the hand the prop is welded to, not the animated bone (position)");
            Assert.LessOrEqual(Quaternion.Angle(_remote.Target.rotation, observerFrame.Rotation * gripRot), 1.5f,
                "the grip must be measured against the hand the prop is welded to, not the animated bone (rotation)");
        }

        /// <summary>
        /// The local weld's staleness guard tested "rotation is not all zero", which catches a store that has
        /// never published but NOT a torn-down one: that answers BasisCalibratedCoords.Identity, whose
        /// (0,0,0,1) rotation passes. The frame then builds at the world origin and the held prop is welded
        /// there. Nothing may be reported as a usable local frame without a live store behind it.
        /// </summary>
        [Test]
        public void NoBoneStore_ReportsNoLocalFrameRatherThanTheWorldOrigin()
        {
            BuildProductionPair();
            BasisLocalBoneDriver.LeftHandControl = new BasisLocalBoneControl();

            Assert.IsFalse(BasisHandGrip.TryGetLocalFrame(BasisLocalBoneDriver.LeftHandControl, true, out BasisHandFrame frame),
                "a bone control with no store has no post-IK pose to offer");
            Assert.AreEqual(Vector3.zero, frame.Position, "and must not hand back an origin frame");
        }

        // ── the diagnostics that answer "which end disagreed" in a live session ──

        /// <summary>
        /// Both ends publish the frame they used, so a session can be inspected instead of guessed at. When
        /// the hold is healthy the two reports must agree on everything that has to match.
        /// </summary>
        [Test]
        public void Diagnostics_BothEndsReportTheSameHold()
        {
            BasisPickupWeldDiagnostics.Enabled = true;
            BuildProductionPair();
            _ownerWrist.position = new Vector3(0f, 1.3f, 0f);
            _observerWrist.position = new Vector3(-5f, 1.3f, 6f);

            BasisPickupWeldDiagnostics.Clear();
            HoldWelded(new Vector3(0.02f, 0f, 0.08f), Quaternion.identity);

            Assert.IsTrue(TryFindReport(owner: true, out BasisPickupWeldReport ownerReport), "owner report");
            Assert.IsTrue(TryFindReport(owner: false, out BasisPickupWeldReport observerReport), "observer report");

            Assert.AreEqual(ownerReport.HandId, observerReport.HandId, "both ends must agree on the attach id");
            Assert.IsTrue(ownerReport.Canonical && observerReport.Canonical, "both frames must be canonical");
            Assert.IsTrue(observerReport.FrameResolved, "the observer must have resolved a frame");
            Assert.AreEqual(ownerReport.HandLength, observerReport.HandLength, 1e-4f,
                "identical rigs must measure the same hand length");
            Assert.LessOrEqual(ownerReport.SelfCheckError, 1e-4f, "the owner's own encode must round-trip");
        }

        /// <summary>
        /// The authoring mistake nothing else catches: a prefab with a position axis left unsynced silently
        /// drops that component of the grip offset for every observer while looking perfect to the holder.
        /// The owner-side round trip has to surface it, because it is the only check the holder can make alone.
        /// </summary>
        [Test]
        public void Diagnostics_OwnerSelfCheckCatchesAnUnsyncedAxis()
        {
            BasisSyncDriver.UnregisterRemote(_remote);
            _sender = CreatePickup("lossy-prop", syncPositionY: false);
            _remote = CreatePickup("lossy-observer", syncPositionY: false);
            BasisSyncDriver.RegisterRemote(_remote);

            BasisPickupWeldDiagnostics.Enabled = true;
            BuildProductionPair();
            _ownerWrist.position = new Vector3(0f, 1.3f, 0f);
            _observerWrist.position = new Vector3(-5f, 1.3f, 6f);

            BasisPickupWeldDiagnostics.Clear();
            // A grip with real extent along the dropped axis, so the loss is measurable.
            HoldWelded(new Vector3(0.02f, 0.15f, 0.08f), Quaternion.identity);

            Assert.IsTrue(TryFindReport(owner: true, out BasisPickupWeldReport ownerReport), "owner report");
            Assert.Greater(ownerReport.SelfCheckError, 0.01f,
                "an unsynced position axis must show up as an encode error on the holder's own client");
        }

        /// <summary>An observer that cannot build the sender's frame must say so rather than report a pose.</summary>
        [Test]
        public void Diagnostics_UnresolvableObserverFrameIsReportedAsUnresolved()
        {
            BasisPickupWeldDiagnostics.Enabled = true;
            BuildProductionPair(observerFingers: false);
            _ownerWrist.position = new Vector3(0f, 1.3f, 0f);
            _observerWrist.position = new Vector3(-5f, 1.3f, 6f);

            BasisPickupWeldDiagnostics.Clear();
            HoldWelded(new Vector3(0.02f, 0f, 0.08f), Quaternion.identity);

            Assert.IsTrue(TryFindReport(owner: false, out BasisPickupWeldReport observerReport), "observer report");
            Assert.IsFalse(observerReport.FrameResolved,
                "the observer must report that it could not rebuild the sender's frame");
        }

        // ── helpers ──

        static bool TryFindReport(bool owner, out BasisPickupWeldReport report)
        {
            IReadOnlyList<BasisPickupWeldReport> reports = BasisPickupWeldDiagnostics.Reports;
            for (int i = 0; i < reports.Count; i++)
            {
                if (reports[i].Owner == owner)
                {
                    report = reports[i];
                    return true;
                }
            }
            report = default;
            return false;
        }

        /// <summary>
        /// The legacy frame resolves through Animator.GetBoneTransform, which needs a humanoid Avatar the
        /// test rigs do not have; seed the bone cache the way the other pickup suites do.
        /// </summary>
        void SeedLegacyHandCache()
        {
            Seed(_sender, _ownerWrist);
            Seed(_remote, _observerWrist);

            static void Seed(BasisPickupSyncNetworking p, Transform wrist)
            {
                p.currentOwnedPlayer.TryGetPlayer(out Basis.Scripts.BasisSdk.Players.IBasisPlayer player);
                typeof(BasisPickupSyncNetworking).GetField("_cachedHandAnimator", NP).SetValue(p, player.BasisAvatar.Animator);
                typeof(BasisPickupSyncNetworking).GetField("_cachedHandId", NP).SetValue(p, 1);
                typeof(BasisPickupSyncNetworking).GetField("_cachedHand", NP).SetValue(p, wrist);
            }
        }
    }
}
