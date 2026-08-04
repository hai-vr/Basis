using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking.Receivers;
using NUnit.Framework;
using Unity.Collections;

namespace Basis.Tests.Remote
{
    /// <summary>
    /// Pins the managed→native face-visibility mirror that lets the gaze selector run without a
    /// per-player main-thread gather.
    ///
    /// BasisLocalEyeDriver used to walk BasisNetworkPlayers.ReceiversSnapshot every rescore purely
    /// to marshal one managed bool (BasisRemotePlayer.FaceIsVisible) per player into a NativeArray
    /// the Burst scorer could read. That loop is gone: the job now walks RemoteBoneJobSystem's dense
    /// SoA slots itself and reads visibility out of a native mirror keyed by player ID.
    ///
    /// That trades a main-thread loop for a sync invariant, and this file guards it. The mirror is
    /// keyed by player ID rather than SoA slot precisely so it cannot desync when RemoveRemotePlayer
    /// swap-compacts the SoA — nothing moves. What is left to protect is the write path itself, and
    /// its one ordering hazard: BasisRemotePlayer's setter can fire before NetworkReceiver is linked
    /// and has no key to write through. That case must fail CLOSED (leave the mirror hidden, so the
    /// player is skipped) rather than open (stale "visible", so the local player stares at a face
    /// that isn't rendered).
    /// </summary>
    public class BasisFaceVisibilityMirrorTests
    {
        // Well clear of any player ID a real session hands out, so a stray write from an editor
        // session that happens to have the bone system alive can't collide with these.
        const ushort KeyA = 40001;
        const ushort KeyB = 40002;

        [TearDown]
        public void TearDown()
        {
            // Releases the lazily-allocated persistent mirror so the leak detector stays quiet.
            // Safe on an uninitialized system — every container is IsCreated-guarded.
            RemoteBoneJobSystem.Dispose();
        }

        static bool MirrorSays(int key)
        {
            NativeArray<byte> map = RemoteBoneJobSystem.GetFaceVisibleMap();
            return map[key] != 0;
        }

        [Test]
        public void MirrorDefaultsToHidden()
        {
            Assert.IsFalse(MirrorSays(KeyA),
                "an unwritten key must read hidden — the job skips players it can't confirm are visible");
        }

        [Test]
        public void SetFaceVisibleWritesAndClears()
        {
            RemoteBoneJobSystem.SetFaceVisible(KeyA, true);
            Assert.IsTrue(MirrorSays(KeyA));

            RemoteBoneJobSystem.SetFaceVisible(KeyA, false);
            Assert.IsFalse(MirrorSays(KeyA));
        }

        [Test]
        public void KeysAreIndependent()
        {
            RemoteBoneJobSystem.SetFaceVisible(KeyA, true);
            Assert.IsFalse(MirrorSays(KeyB), "writing one player must not touch another's slot");
        }

        [Test]
        public void OutOfRangeKeysAreIgnored()
        {
            // The setter is called blind from the property, so a key outside the ushort ID space
            // has to be dropped rather than throwing or corrupting neighbouring memory.
            Assert.DoesNotThrow(() => RemoteBoneJobSystem.SetFaceVisible(-1, true));
            Assert.DoesNotThrow(() => RemoteBoneJobSystem.SetFaceVisible(65536, true));
            Assert.DoesNotThrow(() => RemoteBoneJobSystem.SetFaceVisible(int.MaxValue, true));
        }

        [Test]
        public void PlayerSetterMirrorsThroughTheLinkedReceiver()
        {
            BasisRemotePlayer player = new BasisRemotePlayer
            {
                NetworkReceiver = new BasisNetworkReceiver(KeyA),
            };

            player.FaceIsVisible = true;
            Assert.IsTrue(player.FaceIsVisible, "the managed flag still reads back");
            Assert.IsTrue(MirrorSays(KeyA), "and the native mirror tracks it");

            player.FaceIsVisible = false;
            Assert.IsFalse(MirrorSays(KeyA), "clearing propagates too");
        }

        [Test]
        public void PlayerSetterFailsClosedBeforeTheReceiverLinks()
        {
            // BasisRemoteAvatarDriver writes FaceIsVisible during avatar setup; if that ever ran
            // before the receiver was attached there is no key to resolve. The managed value must
            // still hold, the write must not throw, and the mirror must stay hidden.
            BasisRemotePlayer player = new BasisRemotePlayer();

            Assert.DoesNotThrow(() => player.FaceIsVisible = true);
            Assert.IsTrue(player.FaceIsVisible);
            Assert.IsFalse(MirrorSays(KeyA), "no key means no native write — never a stale 'visible'");
        }

        [Test]
        public void PlayerSetterRecoversOnceTheReceiverIsLinked()
        {
            // The dropped-write case above is recoverable rather than permanent: avatar setup
            // rewrites visibility once the receiver exists, and that write resolves the key.
            BasisRemotePlayer player = new BasisRemotePlayer();
            player.FaceIsVisible = true;

            player.NetworkReceiver = new BasisNetworkReceiver(KeyB);
            player.FaceIsVisible = true;

            Assert.IsTrue(MirrorSays(KeyB), "the next write after linking re-seeds the mirror");
        }

        [Test]
        public void KeyIsCachedNotReReadEveryWrite()
        {
            // The key is captured once and reused, so swapping the receiver underneath a live
            // player keeps writing to the original slot. Documented rather than desirable: player
            // IDs are stable for a player's lifetime, so this can't happen in a real session.
            BasisRemotePlayer player = new BasisRemotePlayer
            {
                NetworkReceiver = new BasisNetworkReceiver(KeyA),
            };
            player.FaceIsVisible = true;

            player.NetworkReceiver = new BasisNetworkReceiver(KeyB);
            player.FaceIsVisible = true;

            Assert.IsTrue(MirrorSays(KeyA));
            Assert.IsFalse(MirrorSays(KeyB));
        }
    }
}
