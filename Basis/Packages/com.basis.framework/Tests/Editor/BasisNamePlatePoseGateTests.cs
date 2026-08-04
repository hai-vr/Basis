using NUnit.Framework;
using Unity.Collections;

namespace Basis.Tests.Remote
{
    /// <summary>
    /// Pins the managed→native gate that stops MappedNameplateApplyJob posing nameplates nobody is
    /// displaying. Every remote player's plate transform used to get an unconditional
    /// SetPositionAndRotation each frame, including the plates deactivated by range, blocking,
    /// face-visibility, menu-only mode or the nameplate setting being off.
    ///
    /// The gate is keyed by player ID rather than SoA slot for the same reason as the face
    /// visibility mirror: RemoveRemotePlayer swap-compacts the SoA, and a slot-keyed flag would
    /// desync. What is left to protect is the direction it fails in. Unlike face visibility, this
    /// one fails CLOSED into a frozen transform rather than a missed cull, so the recovery path —
    /// a player being registered in the bone system re-asserts the plate's state — matters more
    /// than the default.
    /// </summary>
    public class BasisNamePlatePoseGateTests
    {
        // Well clear of any player ID a real session hands out, so a stray write from an editor
        // session that happens to have the bone system alive can't collide with these.
        const ushort KeyA = 40011;
        const ushort KeyB = 40012;

        [TearDown]
        public void TearDown()
        {
            RemoteBoneJobSystem.Dispose();
        }

        static bool GateSays(int key)
        {
            NativeArray<byte> map = RemoteBoneJobSystem.GetNamePlateActiveMap();
            return map[key] != 0;
        }

        [Test]
        public void GateDefaultsToNotPosed()
        {
            Assert.IsFalse(GateSays(KeyA),
                "a player with no live nameplate must never cost a transform write");
        }

        [Test]
        public void SetNamePlateActiveWritesAndClears()
        {
            RemoteBoneJobSystem.SetNamePlateActive(KeyA, true);
            Assert.IsTrue(GateSays(KeyA));

            RemoteBoneJobSystem.SetNamePlateActive(KeyA, false);
            Assert.IsFalse(GateSays(KeyA));
        }

        [Test]
        public void KeysAreIndependent()
        {
            RemoteBoneJobSystem.SetNamePlateActive(KeyA, true);
            Assert.IsFalse(GateSays(KeyB), "opening one player's gate must not touch another's slot");
        }

        [Test]
        public void OutOfRangeKeysAreIgnored()
        {
            Assert.DoesNotThrow(() => RemoteBoneJobSystem.SetNamePlateActive(-1, true));
            Assert.DoesNotThrow(() => RemoteBoneJobSystem.SetNamePlateActive(65536, true));
            Assert.DoesNotThrow(() => RemoteBoneJobSystem.SetNamePlateActive(int.MaxValue, true));
        }

        [Test]
        public void GateSurvivesADisposeAndReallocates()
        {
            // The map is lazily allocated and freed with the rest of the bone system, so a write
            // after a teardown has to bring it back rather than throw on an uncreated array.
            RemoteBoneJobSystem.SetNamePlateActive(KeyA, true);
            RemoteBoneJobSystem.Dispose();

            Assert.DoesNotThrow(() => RemoteBoneJobSystem.SetNamePlateActive(KeyA, true));
            Assert.IsTrue(GateSays(KeyA));
        }
    }
}
