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
    /// Voice Gain is no longer read off the context at all — it is the measured loudness of the
    /// voice, published on its own — so these also lock the seam between the two.
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

        private const float Silent = 0f;

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
        public void It_should_publish_the_voice_level_it_was_handed()
        {
            // Given
            var publisher = NewPublisher();

            // When
            publisher.Publish(_store, null, 0.42f, Gain);

            // Then
            Assert.AreEqual(0.42f, _store.GetValue(GainAddress), 1e-5f);
        }

        [Test]
        public void It_should_not_let_a_viseme_drive_the_gain()
        {
            // Given — a mouth mid-vowel on a voice that is not actually making any sound
            var publisher = NewPublisher();
            var context = NewContext(AaIndex, 60f);
            context.LastApplied[OhIndex] = 20f;
            context.LastApplied[SilIndex] = 100f;

            // When
            publisher.Publish(_store, context, Silent, Gain);

            // Then
            Assert.AreEqual(0f, _store.GetValue(GainAddress), 1e-5f, "gain is loudness, not lip-shape confidence");
        }

        [Test]
        public void It_should_publish_the_gain_without_a_context_at_all()
        {
            // Given — an avatar with no viseme mesh never gets one, and that has to still report voice
            var publisher = NewPublisher();

            // When
            publisher.Publish(_store, null, 0.75f, Gain);

            // Then
            Assert.AreEqual(0.75f, _store.GetValue(GainAddress), 1e-5f);
            Assert.IsNull(publisher.TrackedContext);
        }

        [Test]
        public void It_should_publish_viseme_weights_normalized_to_zero_one()
        {
            // Given
            var publisher = NewPublisher();
            var context = NewContext(AaIndex, 50f);

            // When
            publisher.Publish(_store, context, Silent, Aa);

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
            publisher.Publish(_store, context, 0.5f, Gain);

            // Then
            Assert.AreEqual(1, SubmitsTo(GainAddress));
            Assert.AreEqual(0, SubmitsTo(_addressIds[AaIndex]), "an avatar that only asked for gain must not pay for viseme submits");
        }

        [Test]
        public void It_should_not_publish_the_gain_when_it_was_not_declared()
        {
            // Given
            var publisher = NewPublisher();
            var context = NewContext(AaIndex, 50f);

            // When
            publisher.Publish(_store, context, 0.5f, Aa);

            // Then
            Assert.AreEqual(0, SubmitsTo(GainAddress), "an avatar that only asked for visemes must not pay for gain submits");
        }

        [Test]
        public void It_should_follow_a_swapped_context_rather_than_the_disposed_one()
        {
            // Given
            var publisher = NewPublisher();
            var released = NewContext(AaIndex, 60f);
            publisher.Publish(_store, released, Silent, Aa);
            Assert.AreEqual(0.6f, _store.GetValue(_addressIds[AaIndex]), 1e-5f);

            // When — ReleaseOpenLipSyncContext zeroes the outgoing context, then the next utterance
            // acquires a brand new instance with a brand new LastApplied array.
            released.LastApplied[AaIndex] = 0f;
            var acquired = NewContext(AaIndex, 80f);
            publisher.Publish(_store, acquired, Silent, Aa);

            // Then
            Assert.AreEqual(0.8f, _store.GetValue(_addressIds[AaIndex]), 1e-5f, "the bridge is still reading the disposed context");
            Assert.AreSame(acquired, publisher.TrackedContext);
        }

        [Test]
        public void It_should_ignore_a_disposed_context_that_keeps_mutating()
        {
            // Given
            var publisher = NewPublisher();
            var released = NewContext(AaIndex, 60f);
            publisher.Publish(_store, released, Silent, Aa);

            var acquired = NewContext(AaIndex, 80f);
            publisher.Publish(_store, acquired, Silent, Aa);

            // When
            released.LastApplied[AaIndex] = 100f;
            publisher.Publish(_store, acquired, Silent, Aa);

            // Then
            Assert.AreEqual(0.8f, _store.GetValue(_addressIds[AaIndex]), 1e-5f, "the old context's array must not alias the published value");
        }

        [Test]
        public void It_should_rest_the_visemes_when_the_context_goes_away()
        {
            // Given
            var publisher = NewPublisher();
            var context = NewContext(AaIndex, 60f);
            publisher.Publish(_store, context, 0.6f, Gain | Aa);

            // When
            publisher.Publish(_store, null, Silent, Gain | Aa);

            // Then
            Assert.AreEqual(0f, _store.GetValue(_addressIds[AaIndex]), 1e-5f, "a silent player's mouth must not stay stuck mid-word");
            Assert.AreEqual(0f, _store.GetValue(GainAddress), 1e-5f, "a silent player's glow must not stay lit");
            Assert.IsNull(publisher.TrackedContext);
        }

        /// A pooled context is recycled after a few seconds of silence, but a player can be mid-word
        /// when their avatar leaves viseme range. The mouth resting then is correct; the glow going
        /// out while they are still audibly talking is not.
        [Test]
        public void It_should_keep_the_gain_when_only_the_context_goes_away()
        {
            // Given
            var publisher = NewPublisher();
            publisher.Publish(_store, NewContext(AaIndex, 60f), 0.6f, Gain | Aa);

            // When
            publisher.Publish(_store, null, 0.6f, Gain | Aa);

            // Then
            Assert.AreEqual(0f, _store.GetValue(_addressIds[AaIndex]), 1e-5f);
            Assert.AreEqual(0.6f, _store.GetValue(GainAddress), 1e-5f);
        }

        [Test]
        public void It_should_rest_only_the_declared_addresses()
        {
            // Given
            var publisher = NewPublisher();
            var context = NewContext(AaIndex, 60f);
            publisher.Publish(_store, context, 0.6f, Gain);

            // When
            publisher.Publish(_store, null, Silent, Gain);

            // Then
            Assert.AreEqual(0, SubmitsTo(_addressIds[AaIndex]));
        }

        [Test]
        public void It_should_rest_only_once_while_the_context_stays_away()
        {
            // Given
            var publisher = NewPublisher();
            publisher.Publish(_store, NewContext(AaIndex, 60f), 0.6f, Gain | Aa);
            publisher.Publish(_store, null, Silent, Gain | Aa);
            var afterRest = TotalSubmits();

            // When
            publisher.Publish(_store, null, Silent, Gain | Aa);
            publisher.Publish(_store, null, Silent, Gain | Aa);

            // Then
            Assert.AreEqual(afterRest, TotalSubmits(), "an out-of-range player must not cost a submit every frame");
        }

        [Test]
        public void It_should_do_nothing_before_the_first_context_arrives()
        {
            // Given
            var publisher = NewPublisher();

            // When
            publisher.Publish(_store, null, Silent, Gain | Aa);

            // Then
            Assert.AreEqual(0, TotalSubmits());
        }

        [Test]
        public void It_should_not_republish_unchanged_weights()
        {
            // Given
            var publisher = NewPublisher();
            var context = NewContext(AaIndex, 60f);
            publisher.Publish(_store, context, 0.6f, Gain | Aa);
            var afterFirst = TotalSubmits();

            // When
            publisher.Publish(_store, context, 0.6f, Gain | Aa);
            publisher.Publish(_store, context, 0.6f, Gain | Aa);

            // Then
            Assert.AreEqual(afterFirst, TotalSubmits(), "the dedup against the last published value has to survive the re-read");
        }

        [Test]
        public void It_should_clamp_the_gain_to_zero_one()
        {
            // Given
            var publisher = NewPublisher();

            // When / Then
            publisher.Publish(_store, null, 1.5f, Gain);
            Assert.AreEqual(1f, _store.GetValue(GainAddress), 1e-5f);

            publisher.Publish(_store, null, -0.5f, Gain);
            Assert.AreEqual(0f, _store.GetValue(GainAddress), 1e-5f);

            publisher.Publish(_store, null, float.NaN, Gain);
            Assert.AreEqual(0f, _store.GetValue(GainAddress), 1e-5f, "a NaN must never reach an avatar's material");
        }

        [Test]
        public void It_should_recover_the_gain_after_an_idle_gap()
        {
            // Given — the shape of the reported bug: talk, go quiet long enough for the context to
            // be recycled, talk again.
            var publisher = NewPublisher();
            publisher.Publish(_store, NewContext(AaIndex, 60f), 0.6f, Gain);
            Assert.AreEqual(0.6f, _store.GetValue(GainAddress), 1e-5f);

            // When
            publisher.Publish(_store, null, Silent, Gain);
            Assert.AreEqual(0f, _store.GetValue(GainAddress), 1e-5f);

            publisher.Publish(_store, NewContext(AaIndex, 80f), 0.8f, Gain);

            // Then
            Assert.AreEqual(0.8f, _store.GetValue(GainAddress), 1e-5f, "voice gain never came back after the first pause");
        }
    }
}
