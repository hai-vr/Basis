using System;
using System.Collections.Generic;
using System.Threading;

namespace Basis.Network
{
    /// <summary>
    /// Measures what a receiver actually HEARS, which is the only honest way to judge the voice path.
    ///
    /// The server's own counters cannot answer this. They report what it chose to discard, not what
    /// arrived — a packet shed at the queue bound and a packet that never got sent look identical
    /// from the outside, and neither shows up as a gap until somebody tries to play the audio.
    ///
    /// Every simulated voice frame already carries a per-sender sequence byte
    /// (MovementSender.VoiceSender.SendEncoded writes it as the first payload byte, and the server
    /// relays the body untouched behind a player id). Tracking that per (receiver, sender) pair turns
    /// the stream into a loss measurement: a jump of more than one is exactly the hole a listener
    /// hears.
    ///
    /// Sequence is a single byte and voice runs at 50 frames/s, so it wraps every ~5.1 s. All
    /// arithmetic below is deliberately done in byte space for that reason.
    /// </summary>
    public static class VoiceDeliveryStats
    {
        /// <summary>
        /// A delta above this is read as reorder/duplicate rather than a very large gap. Server to
        /// client is unreliable, so late arrivals are expected and must not be counted as loss.
        /// Half the byte space is the only defensible split point without a wider sequence.
        /// </summary>
        private const int ReorderThreshold = 128;

        private static long _received;
        private static long _lost;
        private static long _reordered;
        private static long _streams;

        /// <summary>Last sequence seen per (receiver, sender). Guarded by <see cref="_gate"/>.</summary>
        private static readonly Dictionary<long, byte> _lastSeq = new Dictionary<long, byte>();
        private static readonly object _gate = new object();

        public static bool Enabled;

        public static long Received => Interlocked.Read(ref _received);
        public static long Lost => Interlocked.Read(ref _lost);
        public static long Reordered => Interlocked.Read(ref _reordered);
        public static long Streams => Interlocked.Read(ref _streams);

        public static void Reset()
        {
            lock (_gate)
            {
                _lastSeq.Clear();
                Interlocked.Exchange(ref _received, 0);
                Interlocked.Exchange(ref _lost, 0);
                Interlocked.Exchange(ref _reordered, 0);
                Interlocked.Exchange(ref _streams, 0);
            }
        }

        /// <summary>
        /// Records one received voice frame. <paramref name="senderId"/> and <paramref name="sequence"/>
        /// come straight off the wire; the caller has already stripped the player id.
        /// </summary>
        public static void Note(int receiverIndex, int senderId, byte sequence)
        {
            if (!Enabled) return;

            Interlocked.Increment(ref _received);
            long key = ((long)receiverIndex << 32) | (uint)senderId;

            lock (_gate)
            {
                if (!_lastSeq.TryGetValue(key, out byte last))
                {
                    // First frame of a stream establishes the baseline. Counting the distance from
                    // zero here would charge every talker's first packet as a burst of loss.
                    _lastSeq[key] = sequence;
                    Interlocked.Increment(ref _streams);
                    return;
                }

                int delta = (byte)(sequence - last);
                if (delta == 0 || delta > ReorderThreshold)
                {
                    Interlocked.Increment(ref _reordered);
                    return; // do not move the baseline backwards
                }

                if (delta > 1)
                    Interlocked.Add(ref _lost, delta - 1);

                _lastSeq[key] = sequence;
            }
        }

        /// <summary>
        /// Delivered share, 0..1. This is the number that answers "is voice breaking up" — at the
        /// bug it sat near 0.5 ("every second packet"), and a healthy path is ~1.
        /// </summary>
        public static double DeliveredFraction
        {
            get
            {
                long recv = Received, lost = Lost;
                long produced = recv + lost;
                return produced > 0 ? (double)recv / produced : 0;
            }
        }

        public static string Describe()
        {
            long recv = Received, lost = Lost;
            return $"[VOICE] delivered {DeliveredFraction * 100:F2}% | received={recv} lost={lost} " +
                   $"reordered={Reordered} streams={Streams}";
        }
    }
}
