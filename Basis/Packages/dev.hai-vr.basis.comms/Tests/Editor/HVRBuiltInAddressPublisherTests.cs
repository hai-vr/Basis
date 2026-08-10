using System.Collections.Generic;
using Basis.Scripts.Drivers;
using NUnit.Framework;

namespace HVR.Basis.Comms.Tests
{
    /// Voice Gain and the viseme addresses froze on remote avatars while working for the wearer.
    /// The bridge cached the OpenLipSync context and its LastApplied array forever, but a remote's
    /// context is pooled: the viseme driver disposes it after a few seconds of silence or once the
    /// player leaves viseme range, then allocates a fresh instance on their next utterance. Since
    /// the release zeroes the array on its way out, the addresses stuck at 0 for the rest of the
    /// session. The wearer's context is never released, which is why it hid there.
    ///
    /// BasisVisemeContextLifecycleTests locks the driver side of the same contract.
    public class HVRBuiltInAddressPublisherTests
    {
        private const int VisemeCount = BasisOpenLipSyncContext.VisemeCount;
        private const int SilIndex = 0;
        private const int AaIndex = 10;
        private const int OhIndex = 13;
        private const int GainAddress = 9001;

        private const HVRBasisBuiltInAddressesVisemeFlags Aa = (HVRBasisBuiltInAddressesVisemeFlags)(1 << AaIndex);
        private const HVRBasisBuiltInAddressesVisemeFlags Gain = HVRBasisBuiltInAddressesVisemeFlags.Gain;

        private int[] _addressIds;
        private HVRVariableStore _store;
        private Dictionary<int, int> _submits;

        [SetUp]
        public void SetUp()
        {
            _addressIds = new int[VisemeCount];
            for (var index = 0; index < VisemeCount; index++)
            {
                // The store rejects address 0, so the ids have to start at 1.
                _addressIds[index] = index + 1;
            }

            _store = new HVRVariableStore();
            _submits = new Dictionary<int, int>();

            var everything = new int[VisemeCount + 1];
            _addressIds.CopyTo(everything, 0);
            everything[VisemeCount] = GainAddress;
            _store.RegisterAddresses(everything, (addressId, value) =>
            {
                _submits.TryGetValue(addressId, out var count);
                _submits[addressId] = count + 1;
            });
        }

        private HVRBuiltInAddressPublisher NewPublisher()
        {
            return new HVRBuiltInAddressPublisher(_addressIds, GainAddress);
        }

        private static BasisOpenLipSyncContext NewContext(int visemeIndex, float weight)
        {
            var context = new BasisOpenLipSyncContext();
            context.LastApplied[visemeIndex] = weight;
            return context;
        }

        private int SubmitsTo(int addressId)
        {
            _submits.TryGetValue(addressId, out var count);
            return count;
        }

        private int TotalSubmits()
        {
            var total = 0;
            foreach (var pair in _submits)
            {
                total += pair.Value;
            }
            return total;
        }

        [Test]
        public void It_should_publish_the_gain_of_the_loudest_non_sil_viseme()
        {
            // Given
            var publisher = NewPublisher();
            var context = NewContext(AaIndex, 60f);
            context.LastApplied[OhIndex] = 20f;

            // When
            publisher.Publish(_store, context, Gain);

            // Then
            Assert.AreEqual(0.6f, _store.GetValue(GainAddress), 1e-5f);
        }

        [Test]
        public void It_should_not_let_sil_drive_the_gain()
        {
            // Given
            var publisher = NewPublisher();
            var context = NewContext(SilIndex, 100f);

            // When
            publisher.Publish(_store, context, Gain);

            // Then
            Assert.AreEqual(0f, _store.GetValue(GainAddress), 1e-5f, "sil is silence, so it must not read as a full-volume voice");
        }

        [Test]
        public void It_should_publish_viseme_weights_normalized_to_zero_one()
        {
            // Given
            var publisher = NewPublisher();
            var context = NewContext(AaIndex, 50f);

            // When
            publisher.Publish(_store, context, Aa);

            // Then
            Assert.AreEqual(0.5f, _store.GetValue(_addressIds[AaIndex]), 1e-5f);
        }

        [Test]
        public void It_should_not_publish_addresses_that_were_not_declared()
        {
            // Given
            var publisher = NewPublisher();
            var context = NewContext(AaIndex, 50f);

            // When
            publisher.Publish(_store, context, Gain);

            // Then
            Assert.AreEqual(1, SubmitsTo(GainAddress));
            Assert.AreEqual(0, SubmitsTo(_addressIds[AaIndex]), "an avatar that only asked for gain must not pay for viseme submits");
        }

        [Test]
        public void It_should_follow_a_swapped_context_rather_than_the_disposed_one()
        {
            // Given
            var publisher = NewPublisher();
            var released = NewContext(AaIndex, 60f);
            publisher.Publish(_store, released, Gain | Aa);
            Assert.AreEqual(0.6f, _store.GetValue(GainAddress), 1e-5f);

            // When — ReleaseOpenLipSyncContext zeroes the outgoing context, then the next utterance
            // acquires a brand new instance with a brand new LastApplied array.
            released.LastApplied[AaIndex] = 0f;
            var acquired = NewContext(AaIndex, 80f);
            publisher.Publish(_store, acquired, Gain | Aa);

            // Then
            Assert.AreEqual(0.8f, _store.GetValue(GainAddress), 1e-5f, "the bridge is still reading the disposed context");
            Assert.AreEqual(0.8f, _store.GetValue(_addressIds[AaIndex]), 1e-5f);
            Assert.AreSame(acquired, publisher.TrackedContext);
        }

        [Test]
        public void It_should_ignore_a_disposed_context_that_keeps_mutating()
        {
            // Given
            var publisher = NewPublisher();
            var released = NewContext(AaIndex, 60f);
            publisher.Publish(_store, released, Gain);

            var acquired = NewContext(AaIndex, 80f);
            publisher.Publish(_store, acquired, Gain);

            // When
            released.LastApplied[AaIndex] = 100f;
            publisher.Publish(_store, acquired, Gain);

            // Then
            Assert.AreEqual(0.8f, _store.GetValue(GainAddress), 1e-5f, "the old context's array must not alias the published value");
        }

        [Test]
        public void It_should_rest_every_declared_address_when_the_context_goes_away()
        {
            // Given
            var publisher = NewPublisher();
            var context = NewContext(AaIndex, 60f);
            publisher.Publish(_store, context, Gain | Aa);

            // When
            publisher.Publish(_store, null, Gain | Aa);

            // Then
            Assert.AreEqual(0f, _store.GetValue(GainAddress), 1e-5f, "a silent player's glow must not stay lit");
            Assert.AreEqual(0f, _store.GetValue(_addressIds[AaIndex]), 1e-5f, "a silent player's mouth must not stay stuck mid-word");
            Assert.IsNull(publisher.TrackedContext);
        }

        [Test]
        public void It_should_rest_only_the_declared_addresses()
        {
            // Given
            var publisher = NewPublisher();
            var context = NewContext(AaIndex, 60f);
            publisher.Publish(_store, context, Gain);

            // When
            publisher.Publish(_store, null, Gain);

            // Then
            Assert.AreEqual(0, SubmitsTo(_addressIds[AaIndex]));
        }

        [Test]
        public void It_should_rest_only_once_while_the_context_stays_away()
        {
            // Given
            var publisher = NewPublisher();
            publisher.Publish(_store, NewContext(AaIndex, 60f), Gain | Aa);
            publisher.Publish(_store, null, Gain | Aa);
            var afterRest = TotalSubmits();

            // When
            publisher.Publish(_store, null, Gain | Aa);
            publisher.Publish(_store, null, Gain | Aa);

            // Then
            Assert.AreEqual(afterRest, TotalSubmits(), "an out-of-range player must not cost a submit every frame");
        }

        [Test]
        public void It_should_do_nothing_before_the_first_context_arrives()
        {
            // Given
            var publisher = NewPublisher();

            // When
            publisher.Publish(_store, null, Gain | Aa);

            // Then
            Assert.AreEqual(0, TotalSubmits());
        }

        [Test]
        public void It_should_not_republish_unchanged_weights()
        {
            // Given
            var publisher = NewPublisher();
            var context = NewContext(AaIndex, 60f);
            publisher.Publish(_store, context, Gain | Aa);
            var afterFirst = TotalSubmits();

            // When
            publisher.Publish(_store, context, Gain | Aa);
            publisher.Publish(_store, context, Gain | Aa);

            // Then
            Assert.AreEqual(afterFirst, TotalSubmits(), "the dedup against the last published value has to survive the re-read");
        }

        [Test]
        public void It_should_recover_the_gain_after_an_idle_gap()
        {
            // Given — the shape of the reported bug: talk, go quiet long enough for the context to
            // be recycled, talk again.
            var publisher = NewPublisher();
            publisher.Publish(_store, NewContext(AaIndex, 60f), Gain);
            Assert.AreEqual(0.6f, _store.GetValue(GainAddress), 1e-5f);

            // When
            publisher.Publish(_store, null, Gain);
            Assert.AreEqual(0f, _store.GetValue(GainAddress), 1e-5f);

            publisher.Publish(_store, NewContext(AaIndex, 80f), Gain);

            // Then
            Assert.AreEqual(0.8f, _store.GetValue(GainAddress), 1e-5f, "voice gain never came back after the first pause");
        }
    }
}
