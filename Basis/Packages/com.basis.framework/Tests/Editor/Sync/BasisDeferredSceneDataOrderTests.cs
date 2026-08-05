using System.Collections.Generic;
using Basis.Network.Core;
using NUnit.Framework;

namespace Basis.Framework.Sync.Tests
{
    /// <summary>
    /// Scene data that arrives before its handler is registered is held and replayed. The replay must keep
    /// the arrival order: a chunked transfer whose body is delivered ahead of its header loses the whole
    /// transfer, which is how images vanished on a client that had just joined.
    /// </summary>
    public sealed class BasisDeferredSceneDataOrderTests
    {
        private const ushort MessageIndex = 41000;
        private const ushort Sender = 7;

        private readonly List<byte> _seen = new();

        [SetUp]
        public void SetUp()
        {
            _seen.Clear();
            BasisNetworkGenericMessages.UnregisterDirectHandler(MessageIndex);
            BasisNetworkGenericMessages.UnregisterHandler(MessageIndex);
        }

        [TearDown]
        public void TearDown()
        {
            BasisNetworkGenericMessages.UnregisterDirectHandler(MessageIndex);
            BasisNetworkGenericMessages.UnregisterHandler(MessageIndex);
        }

        private void Record(ushort sender, byte[] payload, DeliveryMethod deliveryMethod)
        {
            _seen.Add(payload[0]);
        }

        private static void SendDirect(byte marker)
        {
            BasisNetworkGenericMessages.HandleDirectP2PSceneMessage(
                Sender,
                MessageIndex,
                new[] { marker },
                DeliveryMethod.ReliableOrdered
            );
        }

        [Test]
        public void DeferredDirectMessagesReplayInArrivalOrder()
        {
            SendDirect(1);
            SendDirect(2);
            SendDirect(3);
            Assert.That(_seen, Is.Empty, "nothing should have been delivered before the handler existed");

            BasisNetworkGenericMessages.RegisterDirectHandler(MessageIndex, Record);

            Assert.That(_seen, Is.EqualTo(new List<byte> { 1, 2, 3 }));
        }

        [Test]
        public void AHeaderFollowedByItsBodyStillArrivesHeaderFirst()
        {
            // The exact shape of the image-pickup failure: one opening message and then the sequence that
            // depends on it, all arriving before the handler was ready.
            SendDirect(0);
            for (byte chunk = 1; chunk <= 8; chunk++)
                SendDirect(chunk);

            BasisNetworkGenericMessages.RegisterDirectHandler(MessageIndex, Record);

            Assert.That(_seen.Count, Is.EqualTo(9));
            Assert.That(_seen[0], Is.EqualTo(0), "the header has to be replayed before anything that needs it");
            for (int index = 1; index < _seen.Count; index++)
                Assert.That(_seen[index], Is.EqualTo((byte)index));
        }

        [Test]
        public void MessagesDeliverOnceAndAreNotReplayedAgainOnALaterRegistration()
        {
            SendDirect(1);
            BasisNetworkGenericMessages.RegisterDirectHandler(MessageIndex, Record);
            Assert.That(_seen, Is.EqualTo(new List<byte> { 1 }));

            BasisNetworkGenericMessages.UnregisterDirectHandler(MessageIndex);
            BasisNetworkGenericMessages.RegisterDirectHandler(MessageIndex, Record);

            Assert.That(_seen, Is.EqualTo(new List<byte> { 1 }));
        }

        [Test]
        public void MessagesForOtherIndexesKeepWaitingAndKeepTheirOrder()
        {
            const ushort otherIndex = 41001;
            try
            {
                SendDirect(1);
                BasisNetworkGenericMessages.HandleDirectP2PSceneMessage(
                    Sender,
                    otherIndex,
                    new byte[] { 9 },
                    DeliveryMethod.ReliableOrdered
                );
                SendDirect(2);

                BasisNetworkGenericMessages.RegisterDirectHandler(MessageIndex, Record);
                Assert.That(_seen, Is.EqualTo(new List<byte> { 1, 2 }));

                BasisNetworkGenericMessages.RegisterDirectHandler(otherIndex, Record);
                Assert.That(_seen, Is.EqualTo(new List<byte> { 1, 2, 9 }));
            }
            finally
            {
                BasisNetworkGenericMessages.UnregisterDirectHandler(otherIndex);
            }
        }

        [Test]
        public void RelayedSceneDataReplaysInArrivalOrderToo()
        {
            var seen = new List<byte>();
            BasisNetworkGenericMessages.RegisterHandler(
                MessageIndex,
                (sender, payload, method) => seen.Add(payload[0])
            );
            BasisNetworkGenericMessages.UnregisterHandler(MessageIndex);

            for (byte marker = 1; marker <= 4; marker++)
            {
                BasisNetworkGenericMessages.HandleDirectP2PSceneMessage(
                    Sender,
                    MessageIndex,
                    new[] { marker },
                    DeliveryMethod.ReliableOrdered
                );
            }

            BasisNetworkGenericMessages.RegisterDirectHandler(MessageIndex, Record);

            Assert.That(_seen, Is.EqualTo(new List<byte> { 1, 2, 3, 4 }));
        }
    }
}
