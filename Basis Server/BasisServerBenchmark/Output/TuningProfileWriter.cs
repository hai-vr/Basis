using System.Globalization;
using System.Reflection;
using Basis.Benchmark.Tuning;

namespace Basis.Benchmark.Output;

/// <summary>
/// Turns the run's conclusions into the file the server reads at boot.
///
/// <para>Only findings that earned it get in. A recommendation measured on a topology that cannot
/// judge it is reported in the text report and deliberately left out of here — the report is for a
/// person, who can weigh a caveat, and this file is for a machine, which cannot.</para>
/// </summary>
public static class TuningProfileWriter
{
    /// <summary>Where the server looks: beside config.xml.</summary>
    public static string DestinationFor(string serverDirectory) =>
        BasisTuningProfile.ResolvePath(Path.Combine(serverDirectory, "config"));

    public static BasisTuningProfile Build(BenchmarkSession session)
    {
        var profile = new BasisTuningProfile
        {
            ProfileVersion = BasisTuningProfile.CurrentVersion,
            GeneratedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            GeneratedBy = "BasisServerBenchmark " +
                          (Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "dev"),
            Machine = BasisTuningProfile.Fingerprint(),
            MachineDetail =
                $"{session.Machine.LogicalCores} cores, {session.Machine.TotalMemoryGb:F1} GB, " +
                $"{session.Machine.Os}" +
                (session.Machine.IsContainerLimited ? " (container-limited)" : ""),
            DesignPlayers = session.DesignPlayers,
        };

        foreach (Recommendation r in session.Recommendations)
        {
            if (!r.Writable || !r.IsChange) continue;
            profile.Settings.Add(new BasisTunedSetting
            {
                Name = r.Setting,
                Value = r.ProposedValue,
                // The server has to know which of the two files declares a setting, and it cannot
                // infer it - the same name could exist on both.
                Stack = r.File == SettingFile.Transport ? "litenetlib" : string.Empty,
                Evidence = r.Evidence.ToString(),
                Rationale = r.Rationale,
            });
        }

        return profile;
    }

    /// <summary>
    /// Writes the profile where the server will find it, and describes what was and was not
    /// included.
    /// </summary>
    public static string Write(BenchmarkSession session, string? destination = null)
    {
        BasisTuningProfile profile = Build(session);
        string path = destination ?? DestinationFor(session.ServerDirectory);

        if (profile.Settings.Count == 0)
        {
            return "  Nothing to write: no setting was measured better than what is already configured.\n" +
                   "  That is a real result - this machine is already tuned for the population it serves.";
        }

        profile.Save(path);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"\n  Wrote {profile.Settings.Count} setting(s) to {path}");
        foreach (BasisTunedSetting s in profile.Settings)
            sb.AppendLine($"    {s.Name} = {s.Value}   [{(string.IsNullOrEmpty(s.Stack) ? "config.xml" : s.Stack + ".xml")}, {s.Evidence}]");

        int withheld = session.Recommendations.Count(r => r.IsChange && !r.Writable);
        if (withheld > 0)
            sb.AppendLine($"\n  {withheld} further change(s) were measured but NOT written - loopback cannot judge them " +
                          "honestly. See the text report; re-run with the load clients on another machine to settle them.");

        sb.AppendLine("\n  The server applies this on its next boot, folds the values into config.xml, and stamps");
        sb.AppendLine($"  the file so a restart does not re-apply it. It is tied to '{profile.Machine}' and will");
        sb.AppendLine("  refuse to apply on different hardware.");
        return sb.ToString();
    }
}
