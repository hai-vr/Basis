using System.Text.Json;

namespace Basis.Benchmark.Harness;

/// <summary>
/// One reading of the server's /health endpoint.
///
/// <para>Fields are split into two kinds and the distinction is load-bearing. <b>Cumulative</b>
/// counters (bytes, packets, drops) only mean something as a difference between two samples.
/// <b>Instantaneous</b> ones (tick time, slice count, shed tier) describe the moment they were
/// read. Mixing them is how a benchmark ends up dividing a lifetime total by a window length and
/// reporting a rate that is off by the age of the process.</para>
/// </summary>
public sealed record HealthSample
{
    public DateTime SampledUtc { get; init; }

    public bool Ready { get; init; }
    public int Visitors { get; init; }

    // ── Cumulative: differences only ──────────────────────────────────────────
    public long BytesSent { get; init; }
    public long BytesReceived { get; init; }
    public long PacketsSent { get; init; }
    public long PacketsReceived { get; init; }

    /// <summary>
    /// Avatar updates the transport threw away at the queue bound, cumulative.
    ///
    /// The most important number on the endpoint. It is what separates "busy" from "past
    /// capacity", and it is invisible in CPU terms — shedding is CHEAPER than sending, so an
    /// overloaded server reports lower CPU while delivering less. Any tuner that does not read
    /// this will reliably choose the broken configuration.
    /// </summary>
    public long DroppedUnreliable { get; init; }

    /// <summary>
    /// Voice packets dropped, cumulative. Counted apart from the line above because they are not
    /// the same event: bulk shedding is the designed response to load and a busy instance shows
    /// plenty of it, while anything here is audio nobody heard.
    /// </summary>
    public long DroppedVoice { get; init; }

    // ── Instantaneous ─────────────────────────────────────────────────────────
    public int QueuePerPeer { get; init; }
    public int VoiceQueuePerPeer { get; init; }
    public double TickMs { get; init; }
    public double OverrunRatio { get; init; }
    public long IntervalMs { get; init; }
    public int ShedTier { get; init; }
    public string ShedTierName { get; init; } = "";
    public int SliceCount { get; init; }

    /// <summary>Workers the reduction system's send pass is currently allowed.</summary>
    public int SendWorkers { get; init; }

    /// <summary>Workers the core allocator currently grants that pass. Its ceiling, not its width.</summary>
    public int SendWorkerCap { get; init; }

    /// <summary>Share of the tick period the send pass is sized against, as a percentage.</summary>
    public int SendBudgetPercent { get; init; }

    /// <summary>
    /// Send pass duration over the budget above. 1.0 means it exactly fills its share of the
    /// period; this can and does exceed 1.0, which is a pass overrunning the slice it was sized
    /// for rather than the tick overrunning outright.
    /// </summary>
    public double SendDuty { get; init; }

    /// <summary>Pairs one send worker gets through per busy millisecond, measured by the server.</summary>
    public double PairsPerWorkerMs { get; init; }

    public double HeapMb { get; init; }
    public double CommittedMb { get; init; }
    public double FragmentedMb { get; init; }
    public double GcPauseTimePercent { get; init; }
    public long AllocatedMbCumulative { get; init; }
    public int Gen2Collections { get; init; }

    // ── Last closed BSR profiler window ───────────────────────────────────────
    public bool HasWindow { get; init; }
    public long WindowTicks { get; init; }
    public long WindowSends { get; init; }
    public double BundleDeflateMsPerTick { get; init; }
    public double BundleRatio { get; init; }
    public long BundleRawBytes { get; init; }
    public long BundleCompressedBytes { get; init; }
    public double UpdateMsPerTick { get; init; }
    public double TotalMsPerTick { get; init; }

    /// <summary>
    /// Reduction-system sends attempted per second, derived from the profiler window.
    ///
    /// Derived from the window's own tick count and interval rather than from the wall-clock
    /// length of the window, because the tick rate adapts under load: a fixed divisor would read
    /// a re-slicing server as a change in send rate.
    /// </summary>
    public double SendsPerSecond
    {
        get
        {
            if (!HasWindow || WindowTicks <= 0 || IntervalMs <= 0) return 0;
            double windowSeconds = WindowTicks * IntervalMs / 1000.0;
            return windowSeconds <= 0 ? 0 : WindowSends / windowSeconds;
        }
    }

    /// <summary>
    /// How often per second the reduction system visits any given receiver, before losses.
    ///
    /// <para>The product of the two levers the server pulls when it is struggling: it lengthens
    /// the tick, and it slices the roster so each tick serves only part of it. Reading either
    /// alone is misleading — a server holding a fast tick while slicing 32 ways is not doing
    /// well.</para>
    ///
    /// <para><b>It is an upper bound on the per-pair update rate, not the rate itself.</b> Whether
    /// a given pair is actually sent on a given visit depends on their distance, since the send
    /// interval widens with it. That makes this the wrong number to quote as an absolute quality
    /// figure, and the right one to compare configurations with: at a fixed population and spawn
    /// radius the distance distribution is identical across arms, so it moves only when the server
    /// changes how well it is coping — which is exactly the signal an A/B needs.</para>
    /// </summary>
    public double PairHzBeforeLoss => IntervalMs <= 0 || SliceCount <= 0 ? 0 : 1000.0 / IntervalMs / SliceCount;

    /// <summary>
    /// What fraction of the tick period the send pass itself takes.
    ///
    /// The server reports the pass against its own budget rather than against the period, because
    /// the budget is what it sizes workers from. Multiplying back out gives the share of the whole
    /// tick, which is the only form comparable with <see cref="TickMs"/>.
    /// </summary>
    public double SendShareOfPeriod =>
        IntervalMs <= 0 || SendBudgetPercent <= 0 ? 0 : SendDuty * (SendBudgetPercent / 100.0);

    /// <summary>
    /// What the rest of the tick costs, as a fraction of the period — the drain, message
    /// processing, the distance slice and the transport kick together.
    ///
    /// <para><b>This is the quantity BSRSendPhaseBudgetPercent is the complement of,</b> and the
    /// reason it can be fitted at all. It is very nearly independent of the send pool's width:
    /// widening the pool makes the send pass shorter and leaves these phases alone, so a budget
    /// share derived from this does not move when the setting derived from it takes effect. A
    /// figure read the other way round — from how full the send pass's own budget looks — would,
    /// and would chase itself between runs.</para>
    ///
    /// <para>Negative or absurd values mean the fields were not both present; callers check.</para>
    /// </summary>
    public double NonSendShareOfPeriod =>
        IntervalMs <= 0 ? 0 : TickMs / IntervalMs - SendShareOfPeriod;

    public static HealthSample? Parse(string json, DateTime sampledUtc)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            JsonElement gc = Get(root, "gc");
            JsonElement bsr = Get(root, "bsr");
            JsonElement load = Get(bsr, "load");
            JsonElement window = Get(bsr, "window");
            bool hasWindow = window.ValueKind == JsonValueKind.Object;
            JsonElement bundles = hasWindow ? Get(window, "bundles") : default;
            JsonElement msPerTick = hasWindow ? Get(window, "msPerTick") : default;

            return new HealthSample
            {
                SampledUtc = sampledUtc,
                Ready = Bool(root, "ready"),
                Visitors = (int)Long(root, "visitors"),
                BytesSent = Long(root, "sent"),
                BytesReceived = Long(root, "recv"),
                PacketsSent = Long(root, "packetsSent"),
                PacketsReceived = Long(root, "packetsRecv"),
                DroppedUnreliable = Long(root, "droppedUnreliable"),
                DroppedVoice = Long(root, "droppedVoice"),
                QueuePerPeer = (int)Long(root, "queuePerPeer"),
                VoiceQueuePerPeer = (int)Long(root, "voiceQueuePerPeer"),

                TickMs = Double(load, "tickMs"),
                OverrunRatio = Double(load, "overrunRatio"),
                IntervalMs = Long(load, "intervalMs"),
                ShedTier = (int)Long(load, "shedTier"),
                ShedTierName = String(load, "shedTierName"),
                SliceCount = (int)Long(load, "sliceCount"),
                SendWorkers = (int)Long(load, "sendWorkers"),
                SendWorkerCap = (int)Long(load, "sendWorkerCap"),
                SendBudgetPercent = (int)Long(load, "sendBudgetPercent"),
                SendDuty = Double(load, "sendDuty"),
                PairsPerWorkerMs = Double(load, "pairsPerWorkerMs"),

                HeapMb = Double(gc, "heapMb"),
                CommittedMb = Double(gc, "committedMb"),
                FragmentedMb = Double(gc, "fragmentedMb"),
                GcPauseTimePercent = Double(gc, "pauseTimePercent"),
                AllocatedMbCumulative = (long)Double(gc, "allocatedMb"),
                Gen2Collections = (int)Long(gc, "gen2"),

                HasWindow = hasWindow,
                WindowTicks = hasWindow ? Long(window, "ticks") : 0,
                WindowSends = hasWindow ? Long(window, "sends") : 0,
                BundleDeflateMsPerTick = hasWindow ? Double(bundles, "deflateMsPerTick") : 0,
                BundleRatio = hasWindow ? Double(bundles, "ratio") : 0,
                BundleRawBytes = hasWindow ? Long(bundles, "rawBytes") : 0,
                BundleCompressedBytes = hasWindow ? Long(bundles, "compressedBytes") : 0,
                UpdateMsPerTick = hasWindow ? Double(msPerTick, "update") : 0,
                TotalMsPerTick = hasWindow ? Double(msPerTick, "total") : 0,
            };
        }
        catch
        {
            return null;
        }
    }

    private static JsonElement Get(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out JsonElement v) ? v : default;

    private static long Long(JsonElement parent, string name) =>
        Get(parent, name) is { ValueKind: JsonValueKind.Number } e && e.TryGetInt64(out long v) ? v : 0;

    private static double Double(JsonElement parent, string name) =>
        Get(parent, name) is { ValueKind: JsonValueKind.Number } e && e.TryGetDouble(out double v) ? v : 0;

    private static bool Bool(JsonElement parent, string name) =>
        Get(parent, name).ValueKind == JsonValueKind.True;

    private static string String(JsonElement parent, string name) =>
        Get(parent, name) is { ValueKind: JsonValueKind.String } e ? e.GetString() ?? "" : "";
}
