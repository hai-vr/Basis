using System.Globalization;
using System.Text;
using Basis.Benchmark.Machine;
using Basis.Benchmark.Tuning;

namespace Basis.Benchmark.Output;

/// <summary>
/// What to expect from this machine, in plain language.
///
/// <para>Separate from the tuning report on purpose, because it answers a different question for a
/// different reader. The tuning report is for whoever wants to know why a setting was changed; this
/// is for whoever has to decide how many players to advertise, whether the box needs more memory,
/// and what happens on a busy night. It should be readable by somebody who has never opened
/// config.xml.</para>
///
/// <para>The discipline it has to keep is separating what was measured from what was calculated.
/// The ladder measures a few populations; every ceiling above the highest of them is arithmetic on
/// a fitted curve, and a number like "runs out of memory at 2,400 players" carries real weight with
/// somebody choosing hardware. So extrapolated figures say so, every time, next to the number
/// rather than in a footnote.</para>
/// </summary>
public static class CapabilitySummary
{
    public const string FileName = "what-to-expect.txt";

    public static string Render(BenchmarkSession session, CapabilityModel model)
    {
        var sb = new StringBuilder();
        MachineProfile machine = session.Machine;

        Line(sb, '=');
        sb.AppendLine(" WHAT TO EXPECT FROM THIS MACHINE");
        sb.AppendLine($" {machine.LogicalCores} cores, {machine.TotalMemoryGb:F1} GB, {ShortOs(machine.Os)}" +
                      (machine.IsContainerLimited ? ", container-limited" : ""));
        sb.AppendLine($" Measured {session.StartedUtc:yyyy-MM-dd}" +
                      (model.HasData ? $", populations {model.Rungs.Min(r => r.Players):N0} to {model.MeasuredTo:N0}" : ""));
        Line(sb, '=');
        sb.AppendLine();

        if (!model.HasData)
        {
            sb.AppendLine("  No load run has completed, so there is nothing to say about capacity yet.");
            sb.AppendLine("  Run /auto to measure this machine.");
            return sb.ToString();
        }

        AppendPlayerCounts(sb, model);
        AppendOperatingPoint(sb, session, model);
        AppendScaling(sb, model);
        AppendLink(sb, machine, model);
        AppendAdvice(sb, machine, model);
        AppendCaveats(sb, session, model);

        return sb.ToString();
    }

    // ── how many players ────────────────────────────────────────────────────────────────

    private static void AppendPlayerCounts(StringBuilder sb, CapabilityModel model)
    {
        Ceiling quality = model.QualityCeiling();
        Ceiling binding = model.Binding();
        var physical = model.AllCeilings().Where(c => c.Constraint != BindingConstraint.Quality).ToList();
        Ceiling? tightestPhysical = physical.FirstOrDefault();

        sb.AppendLine("HOW MANY PLAYERS");
        sb.AppendLine();

        // One recommended number, then the evidence behind it. An earlier version listed every
        // ceiling in descending order and produced "comfortably 500 / hard limit 386" - two true
        // statements about different things, arranged so that they contradicted each other.
        int recommended = binding.Players > 0 ? binding.Players : quality.Players;
        bool capIsFloor = quality.IsLowerBound && recommended == quality.Players;
        sb.AppendLine($"  Recommended cap    {recommended,8:N0}   " +
                      (capIsFloor
                          ? "at least this - the ladder was not pushed further"
                          : "what this machine can actually serve"));
        sb.AppendLine();

        sb.AppendLine("  It is the lower of two separate limits:");
        sb.AppendLine();
        sb.AppendLine($"    software/CPU     {(quality.IsLowerBound ? quality.Players.ToString("N0") + "+" : quality.Players.ToString("N0")),8}   " +
                      (quality.IsLowerBound
                          ? "measured - this held, and the ladder stopped rather than finding a limit"
                          : "measured - this held and the next rung did not"));

        if (tightestPhysical != null)
            sb.AppendLine($"    {Describe(tightestPhysical.Constraint),-16} {tightestPhysical.PlayersText,8}   " +
                          $"fitted - {tightestPhysical.Explanation}" +
                          (tightestPhysical.Extrapolated ? " (extrapolated)" : ""));

        sb.AppendLine();

        if (quality.IsLowerBound)
        {
            sb.AppendLine($"  NOTE: the ladder never found a population this machine could NOT serve. {model.MeasuredTo:N0} was");
            sb.AppendLine("  simply the highest it tried, so the software limit above is a floor, not a ceiling - raise");
            sb.AppendLine("  max-players and run again to find where it actually stops.");
            sb.AppendLine();
        }

        // The case that reads as nonsense unless it is spelled out: a fitted physical limit below a
        // population the machine was observed serving perfectly well.
        if (tightestPhysical != null && tightestPhysical.Players < quality.Players && tightestPhysical.Players > 0)
        {
            sb.AppendLine($"  These disagree, and that is not a contradiction. The server really did serve {quality.Players:N0}");
            sb.AppendLine($"  players well - but the load clients shared this machine, so that traffic never crossed the");
            sb.AppendLine($"  {Describe(tightestPhysical.Constraint)} the fitted limit is measured against. The bytes are real; the path they took");
            sb.AppendLine($"  was not. Over a real deployment this box is held to about {tightestPhysical.PlayersText}, which is why that is");
            sb.AppendLine("  the recommended cap.");
            sb.AppendLine();
        }
        else if (binding.Constraint == BindingConstraint.Quality)
        {
            sb.AppendLine("  Nothing physical runs out first, which is the healthy answer. The server degrades by design,");
            sb.AppendLine("  so on a machine with headroom it gives up delivering at full rate long before it exhausts a");
            sb.AppendLine("  core, a byte or a bit. Nothing is broken and nothing needs buying.");
            sb.AppendLine();
        }

        var others = physical.Skip(1).ToList();
        if (others.Count > 0)
        {
            sb.AppendLine("  The remaining ceilings, none of which bind here:");
            foreach (Ceiling ceiling in others)
                sb.AppendLine($"    {Describe(ceiling.Constraint),-16} {ceiling.PlayersText,8}   {ceiling.Explanation}" +
                              (ceiling.BeyondUsefulRange ? "  (not a limit within anything measured)"
                                  : ceiling.Extrapolated ? "  (extrapolated)" : ""));
            sb.AppendLine();
        }
    }

    // ── the operating point ─────────────────────────────────────────────────────────────

    private static void AppendOperatingPoint(StringBuilder sb, BenchmarkSession session, CapabilityModel model)
    {
        int at = model.FullQualityPlayers;
        LadderRung? rung = model.Rungs.FirstOrDefault(r => r.Players == at) ?? model.Rungs.LastOrDefault();
        if (rung == null || at <= 0) return;

        sb.AppendLine($"AT {at:N0} PLAYERS, EXPECT");
        sb.AppendLine();
        sb.AppendLine(rung.HasCores
            ? $"  CPU        {rung.Cores,8:F1} cores        {rung.Cores / session.Machine.LogicalCores:P0} of the machine"
            : "  CPU             n/a               the server process would not report its CPU during this run");
        sb.AppendLine($"  Memory     {rung.CommittedMb / 1024.0,8:F1} GB           {model.MemoryMbPerPlayer(at):F1} MB per player");
        sb.AppendLine($"  Egress     {rung.MegabytesPerSecond * 8 / 1000.0,8:F2} Gbit/s       {model.EgressKbpsPerPlayer(at) / 1000.0:F2} Mbit/s per player");
        sb.AppendLine($"  Quality    {rung.DeliveredPairHz,8:F1} updates/s    per pair of players");
        sb.AppendLine($"  Slicing    {rung.SliceCount,8:F1}              1.0 means the roster is served whole every tick");

        if (rung.Result.VoiceDeliveredFraction >= 0)
            sb.AppendLine($"  Voice      {rung.Result.VoiceDeliveredFraction,8:P1}            of frames actually heard by a receiver");

        double kernelDrops = rung.KernelDropsPerSecond;
        if (kernelDrops > 0)
            sb.AppendLine($"  Kernel     {kernelDrops,8:N0} drops/s      inbound datagrams discarded - the receive path is limiting");

        sb.AppendLine();
    }

    // ── the shape of the curve ──────────────────────────────────────────────────────────

    private static void AppendScaling(StringBuilder sb, CapabilityModel model)
    {
        sb.AppendLine("HOW IT SCALES");
        sb.AppendLine();
        sb.AppendLine("   players      cores     memory      egress    quality   delivery");
        foreach (LadderRung r in model.Rungs)
            sb.AppendLine($"   {r.Players,7:N0}   {(r.HasCores ? r.Cores.ToString("F2") : "?"),8}   {r.CommittedMb / 1024.0,6:F2} GB   " +
                          $"{r.MegabytesPerSecond * 8 / 1000.0,6:F2} Gb/s   {r.DeliveredPairHz,6:F1} Hz   {r.DeliveryRatio,8:P0}");
        sb.AppendLine();

        // The superlinear step is the single most useful thing to say about scaling here, and it is
        // the thing operators most reliably get wrong: capacity is not something you can multiply.
        if (model.Rungs.Count >= 2)
        {
            LadderRung low = model.Rungs[^2], high = model.Rungs[^1];
            if (low.HasCores && high.HasCores && low.Cores > 0 && high.Players > low.Players)
            {
                double populationFactor = (double)high.Players / low.Players;
                double coreFactor = high.Cores / low.Cores;
                sb.AppendLine($"  Doubling the crowd does not double the cost. Going from {low.Players:N0} to {high.Players:N0} " +
                              $"players ({populationFactor:F1}x) took {coreFactor:F1}x the CPU.");
                sb.AppendLine("  That is expected and structural: every player is tracked against every other, so the work");
                sb.AppendLine("  grows with the square of the population. Capacity cannot be estimated by multiplying up");
                sb.AppendLine("  from a small test.");
                sb.AppendLine();
            }
        }
    }

    // ── the link ────────────────────────────────────────────────────────────────────────

    private static void AppendLink(StringBuilder sb, MachineProfile machine, CapabilityModel model)
    {
        if (machine.Link == null) return;

        sb.AppendLine("THE NETWORK LINK");
        sb.AppendLine();
        sb.AppendLine($"  {machine.Link.Name}" +
                      (machine.Link.SpeedMbps > 0 ? $", {NetworkLink.FormatSpeed(machine.Link.SpeedMbps)}" : ", speed not reported") +
                      (machine.Link.Mtu > 0 ? $", MTU {machine.Link.Mtu}" : ""));
        sb.AppendLine();

        int at = model.FullQualityPlayers;
        if (at > 0 && machine.Link.SpeedMbps > 0)
        {
            double needed = model.EgressMbpsAt(at);
            double share = needed / machine.Link.SpeedMbps;
            sb.AppendLine($"  At {at:N0} players this server sends about {NetworkLink.FormatSpeed((long)needed)}, " +
                          $"which is {share:P0} of the link.");
            if (share > 1.0)
                sb.AppendLine("  THAT IS MORE THAN THE LINK CAN CARRY. The population above will not be reachable on this " +
                              "interface however the server is tuned.");
            else if (share > 0.7)
                sb.AppendLine("  That is close enough to the link's capacity that bursts will queue, which shows up as " +
                              "latency before it shows up as loss.");
            sb.AppendLine();
        }

        if (machine.Link.MtuIsReduced)
        {
            sb.AppendLine($"  MTU is {machine.Link.Mtu}, below the standard {NetworkLink.StandardMtu}. This is normal on cloud");
            sb.AppendLine("  overlay networks, VPNs and tunnels, and it means full-size datagrams get fragmented: the");
            sb.AppendLine("  packet rate goes up, and losing any one fragment destroys the whole datagram. If loss is");
            sb.AppendLine("  unexplained under load, this is a strong suspect.");
            sb.AppendLine();
        }
    }

    // ── what to do about it ─────────────────────────────────────────────────────────────

    private static void AppendAdvice(StringBuilder sb, MachineProfile machine, CapabilityModel model)
    {
        sb.AppendLine("IF YOU WANT MORE PLAYERS");
        sb.AppendLine();

        switch (model.Binding().Constraint)
        {
            case BindingConstraint.Quality:
                sb.AppendLine("  This machine gives up on quality before it runs out of anything, so more hardware will");
                sb.AppendLine("  not raise the number above. What would: accepting a lower update rate per pair (the");
                sb.AppendLine("  server already does this automatically as it fills), or splitting the crowd across more");
                sb.AppendLine("  instances. Player count per instance is a quality decision here, not a hardware one.");
                break;

            case BindingConstraint.Cpu:
                sb.AppendLine("  CPU is the limit. More cores help, but not proportionally - the reduction system's parallel");
                sb.AppendLine("  passes stop converting cores into throughput past a point this benchmark measures, so");
                sb.AppendLine("  faster cores are worth more than more of them. Check the tuning report's core-scaling");
                sb.AppendLine("  table before buying width.");
                break;

            case BindingConstraint.Memory:
                sb.AppendLine("  Memory is the limit, and it is the one that ends the process rather than degrading it.");
                sb.AppendLine("  Per-player state is genuinely live, so the collector has nothing to give back under");
                sb.AppendLine("  pressure. More RAM raises the ceiling directly and is usually the cheapest fix here.");
                break;

            case BindingConstraint.Bandwidth:
                sb.AppendLine("  The link is the limit. No amount of CPU or memory will move it, and the server cannot");
                sb.AppendLine("  compress its way out - the bundle codec is already measured in the tuning report and its");
                sb.AppendLine("  remaining headroom is small. A faster interface is the only real fix.");
                break;
        }

        sb.AppendLine();

        if (machine.Kernel?.AnyClamped == true)
        {
            sb.AppendLine("  Before any of that: the kernel is clamping the socket buffers this server asks for, so the");
            sb.AppendLine("  numbers above understate what this box can do. Fix that first and re-measure -");
            sb.AppendLine("  see the tuning report for the exact sysctl lines.");
            sb.AppendLine();
        }

        if (!machine.SupportsReusePort)
        {
            sb.AppendLine("  This OS has no SO_REUSEPORT, so the server is limited to a single socket - one receive");
            sb.AppendLine("  thread's worth of syscall throughput, whatever the core count. Linux lifts that ceiling and");
            sb.AppendLine("  is where a large instance belongs.");
            sb.AppendLine();
        }
    }

    // ── what these numbers are not ──────────────────────────────────────────────────────

    private static void AppendCaveats(StringBuilder sb, BenchmarkSession session, CapabilityModel model)
    {
        Line(sb, '-');
        sb.AppendLine("HOW MUCH TO TRUST THIS");
        sb.AppendLine();
        sb.AppendLine($"  Measured directly:  populations up to {model.MeasuredTo:N0}, and every figure quoted at them.");

        var extrapolated = model.AllCeilings().Where(c => c.Extrapolated).ToList();
        sb.AppendLine(extrapolated.Count == 0
            ? "  Extrapolated:       nothing - every ceiling above was inside the measured range."
            : $"  Extrapolated:       the {string.Join(" and ", extrapolated.Select(c => Describe(c.Constraint)))} " +
              "ceiling, fitted from the rungs below it.");
        sb.AppendLine();

        sb.AppendLine("  The load clients shared this machine with the server. Their CPU is excluded from the server");
        sb.AppendLine("  figures - both are sampled per process - but sharing a box still moves the number, and NOT");
        sb.AppendLine("  reliably in one direction:");
        sb.AppendLine();
        sb.AppendLine("    inflates it   contention for cores, shared cache and memory bandwidth; lower boost clocks");
        sb.AppendLine("                  with more cores busy; and loopback performs the receive-side work inside the");
        sb.AppendLine("                  sender, so the server pays for delivery a NIC would have handled elsewhere");
        sb.AppendLine("    deflates it   no driver, no checksums, no interrupts, no wire - per-packet costs are");
        sb.AppendLine("                  understated, which is why packet-rate settings cannot be judged here at all");
        sb.AppendLine();
        sb.AppendLine("  Which dominates is not known, so treat the CPU figures as indicative rather than as a bound");
        sb.AppendLine("  in either direction. Byte counts, delivery and egress are real. To remove the question");
        sb.AppendLine("  entirely, run the load clients on another machine.");
        sb.AppendLine();
        sb.AppendLine("  A crowd that behaves differently will measure differently. The simulated one spreads across a");
        sb.AppendLine("  40 m radius and talks in bursts with occasional choruses; a crowd packed into one room, all");
        sb.AppendLine("  talking, is a harder workload than this.");
        sb.AppendLine();
    }

    private static string Describe(BindingConstraint constraint) => constraint switch
    {
        BindingConstraint.Quality => "quality",
        BindingConstraint.Cpu => "CPU",
        BindingConstraint.Memory => "memory",
        BindingConstraint.Bandwidth => "bandwidth",
        _ => "nothing measurable",
    };

    private static string ShortOs(string os)
    {
        if (os.Contains("Windows", StringComparison.OrdinalIgnoreCase)) return "Windows";
        if (os.Contains("Linux", StringComparison.OrdinalIgnoreCase)) return os.Split(' ').FirstOrDefault() ?? "Linux";
        if (os.Contains("Darwin", StringComparison.OrdinalIgnoreCase)) return "macOS";
        return os;
    }

    private static void Line(StringBuilder sb, char c) => sb.AppendLine(new string(c, 80));

    public static string WriteTo(string directory, BenchmarkSession session, CapabilityModel model)
    {
        Directory.CreateDirectory(directory);
        string stamp = session.StartedUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string path = Path.Combine(directory, $"what-to-expect-{stamp}.txt");
        File.WriteAllText(path, Render(session, model));

        // Also written under a stable name, so an operator has one file to look at and automation
        // has one path to fetch rather than a timestamped guess.
        File.WriteAllText(Path.Combine(directory, FileName), Render(session, model));
        return path;
    }
}
