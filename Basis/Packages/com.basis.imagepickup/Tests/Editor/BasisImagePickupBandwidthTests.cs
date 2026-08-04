using NUnit.Framework;

namespace Basis.ImagePickup.Tests
{
    public sealed class BasisImagePickupBandwidthTests
    {
        private const int Chunk = BasisImagePickupSettings.ChunkPayloadBytes;

        [SetUp]
        public void SetUp()
        {
            BasisImagePickupLinkProbe.Reset();
            BasisImagePickupBandwidth.Reset();
        }

        [Test]
        public void EachBucketGetsHalfOfItsTransportBudget()
        {
            Assert.That(
                BasisImagePickupBandwidth.UplinkBytesPerSecond,
                Is.EqualTo(BasisImagePickupSettings.StartingUplinkBudgetBytesPerSecond * 0.5)
                    .Within(0.001)
            );
            Assert.That(
                BasisImagePickupBandwidth.RelayBytesPerSecond,
                Is.EqualTo(BasisImagePickupSettings.RelayEgressBudgetBytesPerSecond * 0.5)
                    .Within(0.001)
            );
        }

        [Test]
        public void RelayedSendCostsUplinkOnceAndRelayOncePerRecipient()
        {
            double uplinkBefore = BasisImagePickupBandwidth.UplinkTokens;
            double relayBefore = BasisImagePickupBandwidth.RelayTokens;

            Assert.That(BasisImagePickupBandwidth.TryConsume(1000, 0, 8), Is.True);

            Assert.That(
                uplinkBefore - BasisImagePickupBandwidth.UplinkTokens,
                Is.EqualTo(1000d).Within(0.001)
            );
            Assert.That(
                relayBefore - BasisImagePickupBandwidth.RelayTokens,
                Is.EqualTo(8000d).Within(0.001)
            );
        }

        [Test]
        public void DirectSendCostsUplinkPerPeerAndNoRelay()
        {
            double uplinkBefore = BasisImagePickupBandwidth.UplinkTokens;
            double relayBefore = BasisImagePickupBandwidth.RelayTokens;

            Assert.That(BasisImagePickupBandwidth.TryConsume(1000, 3, 0), Is.True);

            Assert.That(
                uplinkBefore - BasisImagePickupBandwidth.UplinkTokens,
                Is.EqualTo(3000d).Within(0.001)
            );
            Assert.That(BasisImagePickupBandwidth.RelayTokens, Is.EqualTo(relayBefore));
        }

        [Test]
        public void MixedSendChargesOneRelayCopyPlusEachDirectPeer()
        {
            double uplinkBefore = BasisImagePickupBandwidth.UplinkTokens;

            Assert.That(BasisImagePickupBandwidth.TryConsume(500, 2, 4), Is.True);

            Assert.That(
                uplinkBefore - BasisImagePickupBandwidth.UplinkTokens,
                Is.EqualTo(1500d).Within(0.001)
            );
            Assert.That(
                BasisImagePickupBandwidth.RelayCapacityBytes
                    - BasisImagePickupBandwidth.RelayTokens,
                Is.EqualTo(2000d).Within(0.001)
            );
        }

        [Test]
        public void AnExhaustedRelayBucketStillLetsDirectPeersTransfer()
        {
            // Wide but small sends, so the relay bucket empties while the uplink bucket — which a relayed
            // send only ever charges one copy against — still has room. That separation is the whole point:
            // being unable to afford the server's fan-out says nothing about a direct link.
            while (BasisImagePickupBandwidth.RelayTokens > 0d)
            {
                Assert.That(BasisImagePickupBandwidth.TryConsume(64, 0, 64), Is.True);
            }

            Assert.That(BasisImagePickupBandwidth.UplinkTokens, Is.GreaterThan(0d));
            Assert.That(BasisImagePickupBandwidth.TryConsume(64, 0, 1), Is.False);
            Assert.That(BasisImagePickupBandwidth.TryConsume(64, 1, 0), Is.True);
        }

        [Test]
        public void SendsStopWhenTheUplinkBucketIsSpentAndResumeAfterRefill()
        {
            while (BasisImagePickupBandwidth.UplinkTokens > 0d)
            {
                Assert.That(BasisImagePickupBandwidth.TryConsume(Chunk, 1, 0), Is.True);
            }

            Assert.That(BasisImagePickupBandwidth.TryConsume(Chunk, 1, 0), Is.False);

            BasisImagePickupBandwidth.Refill(1f);

            Assert.That(BasisImagePickupBandwidth.TryConsume(Chunk, 1, 0), Is.True);
        }

        [Test]
        public void RefillCreditsTheConfiguredRateAndClampsToTheBurstWindow()
        {
            BasisImagePickupBandwidth.TryConsume(Chunk, 1, 0);
            double spent = BasisImagePickupBandwidth.UplinkCapacityBytes
                - BasisImagePickupBandwidth.UplinkTokens;
            Assert.That(spent, Is.GreaterThan(0d));

            BasisImagePickupBandwidth.Refill(0.001f);
            Assert.That(
                BasisImagePickupBandwidth.UplinkTokens,
                Is.EqualTo(
                        BasisImagePickupBandwidth.UplinkCapacityBytes
                            - spent
                            + BasisImagePickupBandwidth.UplinkBytesPerSecond * 0.001d
                    )
                    .Within(0.01)
            );

            BasisImagePickupBandwidth.Refill(600f);
            Assert.That(
                BasisImagePickupBandwidth.UplinkTokens,
                Is.EqualTo(BasisImagePickupBandwidth.UplinkCapacityBytes).Within(0.001)
            );
            Assert.That(
                BasisImagePickupBandwidth.RelayTokens,
                Is.EqualTo(BasisImagePickupBandwidth.RelayCapacityBytes).Within(0.001)
            );
        }

        [Test]
        public void APacketLargerThanTheBucketStillSendsAndIsRepaidBeforeTheNextOne()
        {
            int oversized = (int)BasisImagePickupBandwidth.RelayCapacityBytes * 4;

            Assert.That(BasisImagePickupBandwidth.TryConsume(oversized, 0, 1), Is.True);
            Assert.That(BasisImagePickupBandwidth.RelayTokens, Is.LessThan(0d));
            Assert.That(BasisImagePickupBandwidth.TryConsume(Chunk, 0, 1), Is.False);

            BasisImagePickupBandwidth.Refill(3600f);

            Assert.That(BasisImagePickupBandwidth.TryConsume(Chunk, 0, 1), Is.True);
        }

        [Test]
        public void SendsWithNoRecipientsAreFree()
        {
            double uplinkBefore = BasisImagePickupBandwidth.UplinkTokens;

            Assert.That(BasisImagePickupBandwidth.TryConsume(Chunk, 0, 0), Is.True);

            Assert.That(BasisImagePickupBandwidth.UplinkTokens, Is.EqualTo(uplinkBefore));
        }
    }
}
