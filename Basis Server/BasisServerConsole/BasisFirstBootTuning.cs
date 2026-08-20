using System.Diagnostics;

namespace BasisNetworkConsole
{
    /// <summary>
    /// Offers to fit the server's settings to this machine the first time it boots, by running the
    /// benchmark that ships beside it.
    ///
    /// <para><b>A child process, never an in-process call, and for two independent reasons.</b></para>
    ///
    /// <para>The first is that it could not work any other way. The benchmark measures the server by
    /// starting and stopping it — repeatedly, with different settings — because the values worth
    /// fitting include ones read once at socket bind, and because the runtime's own adaptive state
    /// (measured core ceilings, the slicing controller's position, the packet pool's high-water
    /// mark) carries across a reconfiguration and would make the second arm inherit the first.
    /// Something that has to restart the server cannot be running inside it.</para>
    ///
    /// <para>The second is memory, and it is why this class deliberately mentions no type from the
    /// benchmark assembly. The CLR loads an assembly when a method referencing its types is first
    /// JITted, not when that code runs — so a single direct call, however well guarded by an
    /// <c>if</c>, would map the benchmark and its two compression dependencies into every server
    /// process for the entire life of the instance, to be used approximately never. Going through
    /// <see cref="Process"/> means the only thing this costs a normal boot is one
    /// <see cref="File.Exists"/>.</para>
    /// </summary>
    public static class BasisFirstBootTuning
    {
        /// <summary>
        /// Older layout: the tooling used to be published into a benchmark/ subfolder with the load
        /// client under benchmark/loadclient/. It now sits flat beside the server, but an install
        /// unpacked before that keeps working.
        /// </summary>
        private const string BenchmarkFolder = "benchmark";

        private const string BenchmarkName = "BasisServerBenchmark";
        private const string LoadClientName = "BasisNetworkClientConsole";

        /// <summary>
        /// Set to 1/true to tune without prompting, or 0/false to skip it. Provisioning scripts have
        /// nobody to answer the question and must not be left blocked on it.
        /// </summary>
        private const string EnvironmentSwitch = "BASIS_AUTOTUNE";

        /// <summary>
        /// Runs the benchmark if this is a first boot, the tool is present, and the operator wants
        /// it. Returns true when a tuning profile was produced and is waiting to be applied.
        /// </summary>
        public static bool Run(string baseDirectory, string configDirectory)
        {
            string profilePath = Path.Combine(configDirectory, "tuning-profile.xml");
            if (File.Exists(profilePath))
            {
                // Already tuned — a profile is sitting here waiting to be applied, so there is
                // nothing to measure and the caller will pick it up.
                return true;
            }

            // Both binaries ship flat beside this one, sharing a single copy of every dependency
            // they have in common. The benchmark/ subfolder is the older layout and is still
            // accepted so an install unpacked before the change keeps tuning.
            string benchmarkDirectory = Path.Combine(baseDirectory, BenchmarkFolder);
            string benchmark = FindExecutable(baseDirectory, BenchmarkName)
                            ?? FindExecutable(benchmarkDirectory, BenchmarkName);
            if (benchmark == null)
            {
                BNL.Log("[Tuning] No benchmark beside the server, so first-boot tuning is unavailable. The " +
                        "server is starting on its shipped defaults, which is a supported way to run it.");
                return false;
            }

            // The benchmark needs a crowd to measure, and the crowd is a separate binary. Without it
            // it could still do the offline half, but that is not what was offered here.
            string loadClient = FindExecutable(baseDirectory, LoadClientName)
                             ?? FindExecutable(Path.Combine(benchmarkDirectory, "loadclient"), LoadClientName)
                             ?? FindExecutable(benchmarkDirectory, LoadClientName);
            if (loadClient == null)
            {
                BNL.Log("[Tuning] The benchmark is present but the load client it needs is not, so there is nothing " +
                        "to generate load with. Starting on the shipped defaults.");
                return false;
            }

            string mode = ChooseMode();
            if (mode == null) return false;

            AnnounceStart(mode);

            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = benchmark,
                    WorkingDirectory = Path.GetDirectoryName(benchmark),
                    // Inherited rather than captured: this runs for hours and the operator is sitting
                    // in front of it. Redirecting would hide the ladder and the sweep behind a
                    // silent prompt, and a full pipe would eventually block the child mid-run.
                    UseShellExecute = false,
                };
                start.ArgumentList.Add("--auto");
                start.ArgumentList.Add(mode);
                start.ArgumentList.Add("--server");
                start.ArgumentList.Add(baseDirectory);
                start.ArgumentList.Add("--client");
                start.ArgumentList.Add(Path.GetDirectoryName(loadClient));

                using Process process = Process.Start(start);
                if (process == null)
                {
                    BNL.LogWarning("[Tuning] The benchmark could not be started. Starting on the shipped defaults.");
                    return false;
                }

                process.WaitForExit();

                if (process.ExitCode != 0)
                    BNL.LogWarning($"[Tuning] The benchmark exited with code {process.ExitCode}.");
            }
            catch (Exception ex)
            {
                BNL.LogWarning($"[Tuning] The benchmark failed to run ({ex.Message}). Starting on the shipped defaults.");
                return false;
            }

            if (!File.Exists(profilePath))
            {
                BNL.Log("[Tuning] The benchmark produced no profile - it found nothing worth changing on this " +
                        "machine, which is a real result. Starting on the shipped defaults.");
                return false;
            }

            Console.WriteLine();
            Console.WriteLine("  ----------------------------------------------------------------------------");
            Console.WriteLine("   Tuning finished. Applying what it measured, then starting the server.");
            Console.WriteLine("   The full report is under 'benchmark-results', beside the server.");
            Console.WriteLine("  ----------------------------------------------------------------------------");
            Console.WriteLine();
            return true;
        }

        /// <summary>
        /// Which depth of tuning to run, or null to skip.
        ///
        /// <para>Three choices rather than yes/no, because the honest answer to "how long does this
        /// take" ranges from five minutes to a couple of hours depending on how much is measured,
        /// and offering only the long one gets it declined. The middle option is the default: it is
        /// the cheapest run that can say what actually limits this machine, because three
        /// populations are the fewest a curve can be fitted through.</para>
        ///
        /// <para>The default when nobody answers is <b>skip</b>. A server started by systemd or a
        /// container runtime that silently disappeared on its first boot would look like a failed
        /// deploy, and the operator would have no way to tell what it was doing.</para>
        /// </summary>
        private static string ChooseMode()
        {
            string configured = Environment.GetEnvironmentVariable(EnvironmentSwitch);
            if (!string.IsNullOrEmpty(configured))
            {
                string chosen = NormaliseMode(configured);
                BNL.Log(chosen == null
                    ? $"[Tuning] {EnvironmentSwitch}={configured}, so tuning is skipped."
                    : $"[Tuning] {EnvironmentSwitch}={configured}, so a '{chosen}' run is starting.");
                return chosen;
            }

            if (Console.IsInputRedirected)
            {
                BNL.Log($"[Tuning] This machine has never been tuned, and there is no terminal to ask. Set " +
                        $"{EnvironmentSwitch} to quick, medium or long to tune on first boot, or run {BenchmarkName} " +
                        "beside the server yourself later. Starting on the shipped defaults.");
                return null;
            }

            Console.WriteLine();
            Console.WriteLine("  This machine has not been tuned yet.");
            Console.WriteLine();
            Console.WriteLine("  The benchmark measures what this host actually does under load and fits the settings");
            Console.WriteLine("  to it. The server is not reachable while it runs.");
            Console.WriteLine();
            Console.WriteLine("    1  quick    ~5 minutes    codec settings, parallel pass width, auth window");
            Console.WriteLine("    2  medium   ~15 minutes   adds the player cap and what limits this box  (recommended)");
            Console.WriteLine("    3  long     ~2 hours      adds the A/B setting sweep");
            Console.WriteLine("    s  skip                   start now on the shipped defaults");
            Console.WriteLine();
            Console.WriteLine($"  Skipping is fine, and you can run {BenchmarkName} beside the server whenever it");
            Console.WriteLine("  suits - it is the same tool, and it will offer the same choices.");
            Console.WriteLine();
            Console.Write("  Which? [2] ");

            string answer = (Console.ReadLine() ?? string.Empty).Trim();
            if (answer.Length == 0) return "medium";

            // Menu digits are positional and mean something different from the environment
            // variable's "1", which predates these modes and meant "yes". Translated here rather
            // than in the shared mapper so the two cannot be confused for each other.
            switch (answer.ToLowerInvariant())
            {
                case "1": return "quick";
                case "2": return "medium";
                case "3": return "long";
            }

            string mode = NormaliseMode(answer);
            if (mode == null) BNL.Log("[Tuning] Skipped. Starting on the shipped defaults.");
            return mode;
        }

        /// <summary>Rough wall time per mode, so the wait is a stated expectation rather than a surprise.</summary>
        private static string ExpectedDuration(string mode)
        {
            switch (mode)
            {
                case "quick": return "about 5 minutes";
                case "long": return "a couple of hours";
                default: return "about 15 minutes";
            }
        }

        /// <summary>
        /// Says clearly that the benchmark is running and roughly how long it will be.
        ///
        /// <para>Without this the operator sees the setup wizard finish and then, instead of a
        /// server, an unexplained stream of another program's output for anywhere up to two hours.
        /// The child inherits stdout so its progress is visible, which is useful once you know what
        /// you are looking at and alarming when you do not — the boundary has to be drawn here,
        /// before it starts, because nothing downstream knows this is a first boot.</para>
        /// </summary>
        private static void AnnounceStart(string mode)
        {
            Console.WriteLine();
            Console.WriteLine("  ============================================================================");
            Console.WriteLine($"   TUNING THIS MACHINE - {mode}, {ExpectedDuration(mode)}");
            Console.WriteLine("  ============================================================================");
            Console.WriteLine();
            Console.WriteLine("   The benchmark is running now. It starts and stops copies of this server under");
            Console.WriteLine("   load to find out what this hardware actually does, so THE SERVER IS NOT UP YET");
            Console.WriteLine("   and nobody can connect until it finishes.");
            Console.WriteLine();
            Console.WriteLine("   Progress appears below as it works through each population. When it is done the");
            Console.WriteLine("   settings it measured are applied and the server starts on its own - there is");
            Console.WriteLine("   nothing else for you to do.");
            Console.WriteLine();
            Console.WriteLine("   Ctrl-C stops the benchmark; the server then starts on the shipped defaults.");
            Console.WriteLine();
            Console.WriteLine("  ----------------------------------------------------------------------------");
            Console.WriteLine();

            BNL.Log($"[Tuning] Benchmark started ({mode}, {ExpectedDuration(mode)}). Server start is deferred until it finishes.");
        }

        /// <summary>Maps an environment value to a mode word, or null to skip.</summary>
        private static string NormaliseMode(string value)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "quick":
                    return "quick";
                // "1"/"true" predate the three modes and meant "tune". Kept working, and pointed at
                // the recommended depth rather than the longest one - a config that used to mean
                // "yes please" should not silently become a two-hour outage.
                case "1":
                case "true":
                case "medium":
                    return "medium";
                case "long":
                case "full":
                    return "long";
                default:
                    return null;
            }
        }

        /// <summary>Finds a published executable by base name, whatever the platform calls it.</summary>
        private static string FindExecutable(string directory, string baseName)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return null;

            string windows = Path.Combine(directory, baseName + ".exe");
            if (File.Exists(windows)) return windows;

            string unix = Path.Combine(directory, baseName);
            return File.Exists(unix) ? unix : null;
        }
    }
}
