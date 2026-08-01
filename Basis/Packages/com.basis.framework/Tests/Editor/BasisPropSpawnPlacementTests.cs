using Basis.BasisUI;
using Basis.Scripts.UI.UI_Panels;
using NUnit.Framework;

namespace Basis.Tests.UI
{
    /// <summary>
    /// Covers how a prop's spawn placement is decided, for every value of
    /// <see cref="BasisPropSpawnPlacement"/>.
    ///
    /// <para>Only the resolution half is exercised here. <c>PropSpawnPlacement.ComputePose</c> reads
    /// <c>BasisLocalCameraDriver</c>/<c>BasisLocalPlayer.Instance</c>, casts a physics ray and calls
    /// native <c>Quaternion</c> methods, none of which exist in an EditMode run without a live player
    /// rig — so the pose maths is verified in-app, not here.</para>
    /// </summary>
    public class BasisPropSpawnPlacementTests
    {
        private static BasisDataStoreItemKeys.ItemKey Item(
            BasisPropSpawnPlacement over = BasisPropSpawnPlacement.Unspecified,
            BundledContentHolder.PlacementType legacy = BundledContentHolder.PlacementType.SpawnAtRaycast)
        {
            return new BasisDataStoreItemKeys.ItemKey
            {
                Url = "https://example.com/prop.bee",
                Pass = string.Empty,
                Mode = BundledContentHolder.Mode.Prop,
                PlacementOverride = over,
                PlacementType = legacy,
            };
        }

        private static BasisBundleConnector Connector(BasisPropSpawnMetaData authored)
        {
            return new BasisBundleConnector
            {
                MetaData = new BasisBundleConnector.BasisMetaData { PropSpawn = authored },
            };
        }

        private static BasisPropSpawnMetaData Authored(BasisPropSpawnPlacement placement)
        {
            BasisPropSpawnMetaData meta = BasisPropSpawnMetaData.Authoring;
            meta.Placement = placement;
            return meta;
        }

        // ---- every placement survives a round trip through Resolve ----

        [TestCase(BasisPropSpawnPlacement.Raycast)]
        [TestCase(BasisPropSpawnPlacement.InFrontOfPlayer)]
        [TestCase(BasisPropSpawnPlacement.AtPlayerOrigin)]
        [TestCase(BasisPropSpawnPlacement.InAirAtDistance)]
        [TestCase(BasisPropSpawnPlacement.OnGround)]
        [TestCase(BasisPropSpawnPlacement.InHand)]
        public void Resolve_HonorsAuthoredPlacement(BasisPropSpawnPlacement placement)
        {
            BasisPropSpawnMetaData resolved = PropSpawnPlacement.Resolve(Item(), Connector(Authored(placement)));
            Assert.That(resolved.Placement, Is.EqualTo(placement));
        }

        [TestCase(BasisPropSpawnPlacement.Raycast)]
        [TestCase(BasisPropSpawnPlacement.InFrontOfPlayer)]
        [TestCase(BasisPropSpawnPlacement.AtPlayerOrigin)]
        [TestCase(BasisPropSpawnPlacement.InAirAtDistance)]
        [TestCase(BasisPropSpawnPlacement.OnGround)]
        [TestCase(BasisPropSpawnPlacement.InHand)]
        public void Resolve_PlayerOverrideBeatsAuthoredPlacement(BasisPropSpawnPlacement chosen)
        {
            // The prop asks for OnGround; the player picked something else in the library entry.
            BasisPropSpawnMetaData resolved = PropSpawnPlacement.Resolve(
                Item(over: chosen),
                Connector(Authored(BasisPropSpawnPlacement.OnGround)));

            Assert.That(resolved.Placement, Is.EqualTo(chosen), "the player's own pick outranks the creator's request.");
        }

        // ---- precedence chain: override > authored > legacy type ----

        [Test]
        public void Resolve_FallsBackToLegacyPlacementTypeWhenNothingAuthored()
        {
            BasisPropSpawnMetaData resolved = PropSpawnPlacement.Resolve(
                Item(legacy: BundledContentHolder.PlacementType.SpawnAtPlayerOrigin),
                Connector(BasisPropSpawnMetaData.Authoring));

            Assert.That(resolved.Placement, Is.EqualTo(BasisPropSpawnPlacement.AtPlayerOrigin),
                "entries saved before spawn metadata existed only carry the legacy PlacementType.");
        }

        [Test]
        public void Resolve_UnspecifiedAuthoredDoesNotCountAsARequest()
        {
            BasisPropSpawnMetaData resolved = PropSpawnPlacement.Resolve(
                Item(legacy: BundledContentHolder.PlacementType.SpawnInFrontOfPlayer),
                Connector(Authored(BasisPropSpawnPlacement.Unspecified)));

            Assert.That(resolved.Placement, Is.EqualTo(BasisPropSpawnPlacement.InFrontOfPlayer));
        }

        [Test]
        public void Resolve_NullConnectorStillResolvesFromLegacyType()
        {
            // Connector-less spawn (embedded item): the entry's own placement type is all there is.
            BasisPropSpawnMetaData resolved = PropSpawnPlacement.Resolve(
                Item(legacy: BundledContentHolder.PlacementType.SpawnAtPlayerOrigin), null);

            Assert.That(resolved.Placement, Is.EqualTo(BasisPropSpawnPlacement.AtPlayerOrigin));
        }

        [Test]
        public void Resolve_NullItemFallsBackToRaycast()
        {
            BasisPropSpawnMetaData resolved = PropSpawnPlacement.Resolve(null, Connector(Authored(BasisPropSpawnPlacement.OnGround)));
            Assert.That(resolved.Placement, Is.EqualTo(BasisPropSpawnPlacement.Raycast),
                "with no entry to consult, interactive aiming is the safe default.");
        }

        // ---- non-placement metadata rides through whichever branch wins ----

        [Test]
        public void Resolve_CarriesAuthoredHandAndScaleThroughAPlayerOverride()
        {
            BasisPropSpawnMetaData authored = Authored(BasisPropSpawnPlacement.InHand);
            authored.Hand = BasisPropSpawnHand.Left;
            authored.HasCustomScale = true;
            authored.UniformScale = 0.25f;
            authored.HasCustomDistance = true;
            authored.Distance = 3f;

            BasisPropSpawnMetaData resolved = PropSpawnPlacement.Resolve(
                Item(over: BasisPropSpawnPlacement.OnGround), Connector(authored));

            Assert.That(resolved.Placement, Is.EqualTo(BasisPropSpawnPlacement.OnGround));
            Assert.That(resolved.Hand, Is.EqualTo(BasisPropSpawnHand.Left), "overriding WHERE it lands must not discard the rest of the request.");
            Assert.That(resolved.ResolvedUniformScale, Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(resolved.ResolvedDistance, Is.EqualTo(3f).Within(1e-6f));
        }

        [Test]
        public void Resolve_DefaultsAlignToSurfaceWhenNothingWasAuthored()
        {
            BasisPropSpawnMetaData resolved = PropSpawnPlacement.Resolve(Item(), Connector(default));
            Assert.That(resolved.AlignToSurface, Is.True,
                "a prop that never asked still seats itself on the surface it lands on.");
        }

        // ---- legacy mapping ----

        [TestCase(BundledContentHolder.PlacementType.SpawnInFrontOfPlayer, BasisPropSpawnPlacement.InFrontOfPlayer)]
        [TestCase(BundledContentHolder.PlacementType.SpawnAtPlayerOrigin, BasisPropSpawnPlacement.AtPlayerOrigin)]
        [TestCase(BundledContentHolder.PlacementType.SpawnAtRaycast, BasisPropSpawnPlacement.Raycast)]
        public void FromPlacementType_MapsLegacyTypes(BundledContentHolder.PlacementType legacy, BasisPropSpawnPlacement expected)
        {
            Assert.That(PropSpawnPlacement.FromPlacementType(legacy), Is.EqualTo(expected));
        }

        // ---- resolved numeric fallbacks ----

        [Test]
        public void ResolvedDistance_UsesFallbackUnlessOverridden()
        {
            BasisPropSpawnMetaData meta = BasisPropSpawnMetaData.Authoring;
            Assert.That(meta.ResolvedDistance, Is.EqualTo(BasisPropSpawnMetaData.FallbackDistance).Within(1e-6f));

            meta.HasCustomDistance = true;
            meta.Distance = 0f;
            Assert.That(meta.ResolvedDistance, Is.EqualTo(BasisPropSpawnMetaData.FallbackDistance).Within(1e-6f),
                "a zero distance would spawn the prop inside the player's head.");
        }

        [Test]
        public void ResolvedUniformScale_UsesOneUnlessOverridden()
        {
            BasisPropSpawnMetaData meta = BasisPropSpawnMetaData.Authoring;
            Assert.That(meta.ResolvedUniformScale, Is.EqualTo(1f).Within(1e-6f));

            meta.HasCustomScale = true;
            meta.UniformScale = 0f;
            Assert.That(meta.ResolvedUniformScale, Is.EqualTo(1f).Within(1e-6f),
                "a zero scale would collapse the prop to nothing.");
        }
    }
}
