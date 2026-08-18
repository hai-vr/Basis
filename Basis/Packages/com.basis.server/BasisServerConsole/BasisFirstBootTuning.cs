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
        /// <summary>Subfolder of the server's directory the benchmark is published into.</summary>
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

            string benchmarkDirectory = Path.Combine(baseDirectory, BenchmarkFolder);
            string benchmark = FindExecutable(benchmarkDirectory, BenchmarkName);
            if (benchmark == null)
            {
                BNL.Log($"[Tuning] No benchmark under '{benchmarkDirectory}', so first-boot tuning is unavailable. " +
                        "The server is starting on its shipped defaults, which is a supported way to run it.");
                return false;
            }

            // The benchmark needs a crowd to measure, and the crowd is a separate binary. Without it
            // it could still do the offline half, but that is not what was offered here.
            string loadClient = FindExecutable(Path.Combine(benchmarkDirectory, "loadclient"), LoadClientName)
                             ?? FindExecutable(benchmarkDirectory, LoadClientName);
            if (loadClient == null)
            {
                BNL.Log("[Tuning] The benchmark is present but the load client it needs is not, so there is nothing " +
                        "to generate load with. Starting on the shipped defaults.");
                return false;
            }

            if (!ShouldRun()) return false;

            BNL.Log("[Tuning] Running the benchmark. It starts and stops this server many times over, so the " +
                    "instance will not be reachable until it finishes.");

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

            return true;
        }

        /// <summary>
        /// Whether to tune, asking only when there is somebody to ask.
        ///
        /// The default when nobody answers is <b>no</b>. A server started by systemd or a container
        /// runtime that silently disappeared for two hours on its first boot would look like a
        /// failed deploy, and the operator would have no way to tell what it was doing.
        /// </summary>
        private static bool ShouldRun()
        {
            string configured = Environment.GetEnvironmentVariable(EnvironmentSwitch);
            if (!string.IsNullOrEmpty(configured))
            {
                bool wanted = configured == "1" || configured.Equals("true", StringComparison.OrdinalIgnoreCase);
                BNL.Log($"[Tuning] {EnvironmentSwitch}={configured}, so tuning is {(wanted ? "running" : "skipped")}.");
                return wanted;
            }

            if (Console.IsInputRedirected)
            {
                BNL.Log($"[Tuning] This machine has never been tuned, and there is no terminal to ask. Set " +
                        $"{EnvironmentSwitch}=1 to tune on first boot, or run the benchmark under '{BenchmarkFolder}' " +
                        "yourself later. Starting on the shipped defaults.");
                return false;
            }

            Console.WriteLine();
            Console.WriteLine("  This machine has not been tuned yet.");
            Console.WriteLine();
            Console.WriteLine("  The benchmark can measure what this host actually does under load and fit the");
            Console.WriteLine("  settings to it - how wide the parallel pools should run, what the compression");
            Console.WriteLine("  budget is worth here, and how many players it serves before quality drops.");
            Console.WriteLine();
            Console.WriteLine("  It takes a couple of hours and the server will not be reachable while it runs.");
            Console.WriteLine("  Skipping is fine: the shipped defaults are a supported configuration, and you can");
            Console.WriteLine($"  run the benchmark under '{BenchmarkFolder}' whenever it suits.");
            Console.WriteLine();
            Console.Write("  Tune this machine now? [y/N] ");

            string answer = Console.ReadLine();
            bool yes = answer != null && answer.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase);
            if (!yes) BNL.Log("[Tuning] Skipped. Starting on the shipped defaults.");
            return yes;
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
