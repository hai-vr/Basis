using System.Threading;

namespace Basis.Scripts.Networking.NetworkedAvatar
{
    /// <summary>
    /// Hop-by-hop counters for AdditionalAvatarData (face tracking, avatar behaviour params)
    /// across the avatar sync pipeline. Counting is always on (a few Interlocked adds per
    /// avatar frame); a summary line is emitted every ~10s only when <see cref="Enabled"/> is
    /// set, or automatically when a hop is starving — data enters the pipeline but never leaves
    /// it — so a broken stage names itself in the log without any configuration.
    ///
    /// Sender hops:   Submitted (behaviour → transmitter) → Attached (Compress put it on a frame).
    /// Receiver hops: Parsed (frame carried a section) → gate results → Dispatched (behaviour ran).
    /// </summary>
    public static class BasisAdditionalDataDiagnostics
    {
        /// <summary>Force the periodic summary on, even when nothing looks wrong.</summary>
        public static bool Enabled;

        // ── Sender ──
        public static long SenderSubmitted;          // OnAvatarServerReductionSystemMessageSend calls
        public static long SenderSubmitFailedNoTransmitter;
        public static long SenderFramesWithAdditional;   // Compress frames that carried a section
        public static long SenderFramesKeyframe;         // of those, sent as keyframes
        public static long SenderFramesDelta;            // of those, sent as uplink deltas

        // ── Receiver ──
        public static long ReceiverFramesWithAdditional; // frames whose section parsed
        public static long ReceiverDroppedLinkedIndex;   // LinkedAvatarIndex gate rejected
        public static long ReceiverDroppedNoBehaviours;  // NetworkBehaviours null (avatar not ready)
        public static long ReceiverMarshaledToMainThread;// arrived off-main, deferred (P2P socket path)
        public static long ReceiverDroppedStaleOnDrain;  // avatar swapped between enqueue and drain
        public static long ReceiverEntriesDispatched;    // behaviour callbacks actually invoked
        public static long ReceiverEntriesSkippedEmpty;  // size-0 entries skipped
        public static long ReceiverEntriesSkippedIndex;  // messageIndex out of range

        // Last-seen LinkedAvatarIndex pair from a gate rejection, to make a mismatch diagnosable.
        public static int LastGateMessageIndex = -1;
        public static int LastGateReceiverIndex = -1;

        static long sLastReportTicks;
        static long sLastSubmitted, sLastAttached, sLastParsed, sLastDispatched;

        /// <summary>
        /// Called from the pipeline once per avatar frame (either side, any thread). Emits at
        /// most one line per ~10s: always when <see cref="Enabled"/>, otherwise only when a hop
        /// is starving. Uses Stopwatch time so it is safe off the main thread.
        /// </summary>
        public static void MaybeReport()
        {
            long now = System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
            long last = Interlocked.Read(ref sLastReportTicks);
            if (now - last < 10) return;
            if (Interlocked.CompareExchange(ref sLastReportTicks, now, last) != last) return;

            long submitted = Interlocked.Read(ref SenderSubmitted);
            long attached = Interlocked.Read(ref SenderFramesWithAdditional);
            long parsed = Interlocked.Read(ref ReceiverFramesWithAdditional);
            long dispatched = Interlocked.Read(ref ReceiverEntriesDispatched);

            long dSubmitted = submitted - sLastSubmitted;
            long dAttached = attached - sLastAttached;
            long dParsed = parsed - sLastParsed;
            long dDispatched = dispatched - sLastDispatched;
            sLastSubmitted = submitted; sLastAttached = attached; sLastParsed = parsed; sLastDispatched = dispatched;

            // Starvation between hops in the last window = the pipeline is broken at that seam.
            bool senderStarved = dSubmitted > 0 && dAttached == 0;
            bool receiverStarved = dParsed > 0 && dDispatched == 0;

            if (!Enabled && !senderStarved && !receiverStarved) return;

            string gate = LastGateMessageIndex >= 0
                ? $" lastGatePair(msg={LastGateMessageIndex},recv={LastGateReceiverIndex})"
                : string.Empty;

            BasisDebug.Log(
                $"[AdditionalData] 10s window — sender: submitted={dSubmitted} attached={dAttached} " +
                $"(kf={Interlocked.Read(ref SenderFramesKeyframe)} delta={Interlocked.Read(ref SenderFramesDelta)} noTransmitter={Interlocked.Read(ref SenderSubmitFailedNoTransmitter)}) | " +
                $"receiver: parsed={dParsed} dispatched={dDispatched} " +
                $"droppedLinkedIndex={Interlocked.Read(ref ReceiverDroppedLinkedIndex)}{gate} " +
                $"noBehaviours={Interlocked.Read(ref ReceiverDroppedNoBehaviours)} marshaled={Interlocked.Read(ref ReceiverMarshaledToMainThread)} " +
                $"staleOnDrain={Interlocked.Read(ref ReceiverDroppedStaleOnDrain)} skippedEmpty={Interlocked.Read(ref ReceiverEntriesSkippedEmpty)} " +
                $"skippedIndex={Interlocked.Read(ref ReceiverEntriesSkippedIndex)}" +
                (senderStarved ? " ← SENDER STARVED: data submitted but never attached to frames" : string.Empty) +
                (receiverStarved ? " ← RECEIVER STARVED: sections parsed but no behaviour dispatched" : string.Empty),
                BasisDebug.LogTag.Networking);
        }
    }
}
