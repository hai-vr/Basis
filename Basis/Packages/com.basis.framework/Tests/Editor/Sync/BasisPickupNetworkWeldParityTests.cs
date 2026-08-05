using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking.Sync;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// The three ways the two ends of a welded hold were able to disagree, each of which put the prop in a
    /// place the holder could not see. See <see cref="BasisPickupWeldFixture"/> for why the owner-local /
    /// observer-remote split is what makes these reachable at all.
    ///
    /// <see cref="BasisPickupWeldMatrixTests"/> sweeps the space these three came out of; this file pins the
    /// specific regressions with the reasoning attached.
    /// </summary>
    public class BasisPickupNetworkWeldParityTests : BasisPickupWeldFixture
    {
        /// <summary>
        /// The frame the local weld drives the prop with and the frame the transmit measures it against come
        /// from two different lookups — the avatar driver's static mapping and the rig driver's — and nothing
        /// in the type system makes them the same object. If they ever diverge the owner welds against one
        /// hand and ships an offset measured against another: wrong on every remote, right on its own screen.
        /// </summary>
        [Test]
        public void LocalWeldFrameAndTransmitFrameAreTheSameFrame()
        {
            BuildProductionPair();

            Assert.IsTrue(BasisHandGrip.TryGetFrame(BasisLocalAvatarDriver.Mapping, true,
                _ownerWrist.position, _ownerWrist.rotation, out BasisHandFrame weld), "local weld frame");
            _sender.currentOwnedPlayer.TryGetPlayer(out IBasisPlayer owner);
            Assert.IsTrue(BasisHandGrip.TryGetPlayerFrame(owner, true, out BasisHandFrame sent), "transmit frame");

            Assert.LessOrEqual(Vector3.Distance(weld.Position, sent.Position), 1e-4f,
                "the hand the prop is welded to and the hand the offset is measured against must be one hand");
            Assert.LessOrEqual(Quaternion.Angle(weld.Rotation, sent.Rotation), 0.05f, "and one orientation");
            Assert.AreEqual(weld.HandLength, sent.HandLength, 1e-5f, "and one hand length");
        }

        /// <summary>
        /// The whole point of the hold: what the holder sees is what every observer sees. Owner resolves
        /// through the local branch, observer through the remote branch, identical rigs in different places.
        /// </summary>
        [Test]
        public void LocalHolder_ObserverReconstructsTheOwnersWorldPose()
        {
            BuildProductionPair();
            _ownerWrist.SetPositionAndRotation(new Vector3(1f, 1.4f, -2f), Quaternion.Euler(12f, 40f, -8f));
            _observerWrist.SetPositionAndRotation(new Vector3(-5f, 1.4f, 6f), Quaternion.Euler(12f, 40f, -8f));

            Vector3 grip = new Vector3(0.03f, -0.015f, 0.11f);
            Quaternion gripRot = Quaternion.Euler(0f, 25f, 90f);
            HoldWelded(grip, gripRot);

            AssertObserverMatchesOwner(grip, gripRot,
                "a welded hold must reconstruct into the observer's copy of the same hand");
        }

        /// <summary>
        /// The observer could not build the canonical basis (no finger bones on its copy of the avatar — a
        /// fallback, a still-loading model, a stripped performance substitute) but the id says the offset is
        /// in canonical units. Nothing on the receive side used to check that, so it decoded against the raw
        /// wrist bone scaled by a stand-in hand length: off the palm, turned by the wrist bind, forever.
        /// </summary>
        [Test]
        public void ObserverWithoutFingerBones_DoesNotSilentlyDecodeAgainstTheWrist()
        {
            BuildProductionPair(observerFingers: false);
            _ownerWrist.SetPositionAndRotation(new Vector3(0f, 1.3f, 0f), Quaternion.identity);
            _observerWrist.SetPositionAndRotation(new Vector3(-5f, 1.3f, 6f), Quaternion.identity);

            Vector3 grip = new Vector3(0.02f, 0f, 0.08f);
            Vector3 parked = new Vector3(-99f, -99f, -99f);
            _remote.Target.SetPositionAndRotation(parked, Quaternion.identity);
            HoldWelded(grip, Quaternion.identity);

            _observerWrist.GetPositionAndRotation(out Vector3 wp, out Quaternion wr);
            Vector3 wristWeld = wp + wr * (grip * (BasisHandGrip.FallbackHandLength / OwnerFrame().HandLength));
            Assert.Greater(Vector3.Distance(_remote.Target.position, wristWeld), 0.02f,
                "an observer that cannot build the canonical frame must not pretend the wrist bone is it");
            Assert.LessOrEqual(Vector3.Distance(_remote.Target.position, parked), 1e-4f,
                "and must hold its last pose, the same as for an owner whose hand cannot be resolved");
        }

        /// <summary>
        /// The holder did not resolve as a remote player (mid-join, mid-avatar-swap, a stale owner entry).
        /// TryGetPlayerFrame's else-branch assumed "not remote therefore local" and handed back the VIEWER'S
        /// OWN hand frame, welding the prop into the hand of whoever was watching.
        /// </summary>
        [Test]
        public void HolderThatIsNotARemotePlayer_DoesNotWeldToTheViewersOwnHand()
        {
            BuildProductionPair();
            _ownerWrist.SetPositionAndRotation(new Vector3(0f, 1.3f, 0f), Quaternion.identity);
            _observerWrist.SetPositionAndRotation(new Vector3(-5f, 1.3f, 6f), Quaternion.identity);

            Vector3 grip = new Vector3(0.02f, 0f, 0.08f);
            HoldWelded(grip, Quaternion.identity);

            // The observer loses the typed remote player for the holder, while the viewer's own local rig
            // (the owner rig, in this fixture) stays resolvable.
            SetHolderPlayer(_remote, new StubPlayer());
            _remote.Target.SetPositionAndRotation(new Vector3(-99f, -99f, -99f), Quaternion.identity);
            Tick();

            BasisHandFrame viewerHand = OwnerFrame();
            Assert.Greater(Vector3.Distance(_remote.Target.position, viewerHand.Position + viewerHand.Rotation * grip), 0.05f,
                "an unresolvable holder must not collapse onto the viewer's own hand");
        }

        /// <summary>
        /// The id said "authored grip" purely because the prefab had a GripPoint, but the owner's hold was
        /// never aligned to it (weld off, a desktop grab, an in-hand nudge). The receiver throws the streamed
        /// offset away and re-solves the grip onto its own palm, so it held the prop by a handle the owner
        /// was not holding it by.
        /// </summary>
        [Test]
        public void AuthoredGripFlag_DoesNotOverrideAHoldTheOwnerNeverAlignedToIt()
        {
            BuildProductionPair();
            AddGripPoint(_sender, new Vector3(0f, 0.02f, -0.3f), Quaternion.Euler(0f, 0f, 30f), aligned: false);
            AddGripPoint(_remote, new Vector3(0f, 0.02f, -0.3f), Quaternion.Euler(0f, 0f, 30f), aligned: false);
            _sender.BasisPickupInteractable.WeldToHand = false;

            _ownerWrist.SetPositionAndRotation(new Vector3(0f, 1.3f, 0f), Quaternion.identity);
            _observerWrist.SetPositionAndRotation(new Vector3(-5f, 1.3f, 6f), Quaternion.identity);

            // Held the way an unwelded grab holds it: wherever it was, not by the authored handle.
            Vector3 grip = new Vector3(0.12f, 0.05f, 0.2f);
            Quaternion gripRot = Quaternion.Euler(70f, 0f, 0f);
            HoldWelded(grip, gripRot);

            AssertObserverMatchesOwner(grip, gripRot,
                "an authored grip must not replace a hold the owner never aligned to it");
        }

        /// <summary>
        /// The other half of that contract: when the owner IS holding by the authored point, the receiver
        /// still re-solves it from its own prefab rather than taking the streamed offset, so a differently
        /// sized hand grips the same handle instead of a scaled approximation of it.
        /// </summary>
        [Test]
        public void AuthoredGripAlignedHold_ObserverStillSolvesTheGripItself()
        {
            BuildProductionPair(observerBind: Quaternion.Euler(0f, 35f, 0f), observerHandScale: 1.4f);
            Transform senderGrip = AddGripPoint(_sender, new Vector3(0f, 0.02f, -0.3f), Quaternion.Euler(0f, 0f, 30f), aligned: true);
            Transform observerGrip = AddGripPoint(_remote, new Vector3(0f, 0.02f, -0.3f), Quaternion.Euler(0f, 0f, 30f), aligned: true);

            _ownerWrist.position = new Vector3(0f, 1.3f, 0f);
            _observerWrist.position = new Vector3(-5f, 1.3f, 6f);

            BasisHandFrame ownerFrame = OwnerFrame();
            _sender.Target.GetPositionAndRotation(out Vector3 op, out Quaternion orot);
            Assert.IsTrue(_sender.BasisPickupInteractable.TryGetGripOffsets(op, orot, out Vector3 offPos, out Quaternion offRot));
            _sender.Target.SetPositionAndRotation(ownerFrame.Position + ownerFrame.Rotation * offPos, ownerFrame.Rotation * offRot);
            Assert.LessOrEqual(Vector3.Distance(senderGrip.position, ownerFrame.Position), 0.001f, "owner-side sanity");

            Transmit();

            BasisHandFrame observerFrame = ObserverFrame();
            Assert.LessOrEqual(Vector3.Distance(observerGrip.position, observerFrame.Position), 0.005f,
                "an aligned authored grip must land on the observer's own palm");
            Assert.LessOrEqual(Quaternion.Angle(observerGrip.rotation, observerFrame.Rotation), 1.5f,
                "and take that hand's orientation");
        }

        /// <summary>An IBasisPlayer that is neither the local player nor a typed remote — what a mid-join or stale owner entry looks like.</summary>
        private sealed class StubPlayer : IBasisPlayer
        {
            public bool IsLocal { get; set; }
            public string PlayerPlatform { get; set; }
            public string DisplayName { get; set; }
            public string UUID { get; set; }
            public string SafeDisplayName { get; set; }
            public BasisAvatar BasisAvatar { get; set; }
            public Transform AvatarTransform { get; set; }
            public Transform AvatarAnimatorTransform { get; set; }
            public Transform PlayerSelf { get; set; }
            public BasisProgressReport ProgressReportAvatarLoad => null;
            public BasisProgressReport AvatarProgress => null;
            public System.Action AudioReceived { get; set; }
            public bool FaceIsVisible { get; set; }
            public BasisMeshRendererCheck FaceRenderer { get; set; }
            public bool IsConsideredFallBackAvatar { get; set; }
            public byte AvatarLoadMode { get; set; }
            public BasisLoadableBundle AvatarMetaData { get; set; }
            public GameObject GameObject => null;
            public Transform Transform => null;
            public Transform AvatarParent => null;
            public bool IsDestroyed => false;
            public event System.Action OnAvatarSwitched { add { } remove { } }
            public void SetSafeDisplayname() { }
            public void UpdateFaceVisibility(bool State) { }
            public void AvatarSwitched() { }
        }
    }
}
