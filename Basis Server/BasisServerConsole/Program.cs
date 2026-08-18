using Basis.Network;
using Basis.Network.Server;
using BasisNetworkConsole;
using BasisNetworking.InitialData;
using BasisNetworkServer.BasisNetworkingReductionSystem;
namespace Basis
{
    class Program
    {
        public static BasisNetworkHealthCheck Check;
#if !UNITY_2017_1_OR_NEWER
        public static BasisRestApiHandler Api;
#endif
        public static bool isRunning = true;
        private static ManualResetEventSlim shutdownEvent = new ManualResetEventSlim(false);
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            BasisConsoleCommands.WaitForPredecessorExit(args);

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string configDir = Path.Combine(baseDir, Configuration.ConfigFolderName);
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }
            string configFilePath = Path.Combine(configDir, "config.xml");
            // Capture this before LoadFromXml, which creates config.xml when it's missing.
            bool isFirstBoot = !File.Exists(configFilePath);
            Configuration config = Configuration.LoadFromXml(configFilePath);

            // Settings the benchmark fitted to this machine, if it left any. Applied once and
            // folded into config.xml, so it never shadows a later hand edit.
            //
            // ⚠️ Before the environment overrides, not after, and the order is load-bearing in both
            // directions. Applying this persists the config, and an override is a per-run pin — so
            // running it second would write whatever was in the environment permanently into
            // config.xml, turning a temporary override into a setting nobody remembers making.
            // Going first also leaves the overrides applied last, which is what makes them still
            // win for this run.
            BasisTuningProfile.ApplyIfPresent(configDir, config);

            config.ProcessEnvironmentalOverrides();

            string folderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Configuration.LogsFolderName);
            BasisServerSideLogging.Initialize(config, folderPath);

            // Brand-new server: walk the operator through core settings and force them to
            // designate an admin before anything boots.
            if (isFirstBoot)
            {
                BasisSetupWizard.Run(config, configFilePath);

                // Offer to fit the settings to this machine before it ever serves anyone. Runs the
                // benchmark as a separate process, so nothing from it is ever loaded here.
                if (BasisFirstBootTuning.Run(baseDir, configDir))
                {
                    // Re-read from disc rather than applying onto the object in hand. That object
                    // has already had this run's environment overrides folded into it, and applying
                    // a profile persists the config — which would write a per-run pin into
                    // config.xml permanently. Loading fresh also picks up the transport sidecars the
                    // benchmark's own server runs rewrote underneath us.
                    config = Configuration.LoadFromXml(configFilePath);
                    BasisTuningProfile.ApplyIfPresent(configDir, config);
                    config.ProcessEnvironmentalOverrides();
                }
            }

            BNL.Log("Server Booting");
            Check = new BasisNetworkHealthCheck(config);
#if !UNITY_2017_1_OR_NEWER
            if (config.ApiEnabled && !string.IsNullOrEmpty(config.ApiKey))
                Api = new BasisRestApiHandler(config);
#endif

            NetworkServer.StartServer(config);
            
            // Handle legacy resource directory name migrations and similar.
            // after a version bump or two this should be removed
            string[] legacyPaths = [
                "initalresources",    // dooly spelling
                "initialressources",  // if you're french
                "intialresources",   // another common typo
            ];
            
            string correctPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Configuration.InitialResourcesFolderName);

            foreach (string legacyName in legacyPaths)
            {
                string legacyFullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, legacyName);
                
                if (Directory.Exists(legacyFullPath) && !Directory.Exists(correctPath))
                {
                    try
                    {
                        BNL.Log($"Found legacy '{legacyName}' directory, migrating to '{Configuration.InitialResourcesFolderName}'...");
                        Directory.Move(legacyFullPath, correctPath);
                        BNL.Log("Directory migration completed successfully");
                        break; // Exit after first successful migration
                    }
                    catch (Exception ex)
                    {
                        BNL.LogError($"Failed to migrate legacy directory '{legacyName}': {ex.Message}");
                    }
                }
            }
            BasisLoadableLoader.LoadXML(Configuration.InitialResourcesFolderName);
            BasisDefaultLibraryLoader.LoadXML(Configuration.DefaultLibraryFolderName);

            AppDomain.CurrentDomain.ProcessExit += async (sender, eventArgs) =>
            {
                BNL.Log("Shutting down server...");
                isRunning = false;
                shutdownEvent.Set(); // Signal the main thread to exit
#if !UNITY_2017_1_OR_NEWER
                Api?.Dispose();
#endif
                BasisServerReductionSystemEvents.Shutdown();
                if (config.EnableStatistics) BasisStatistics.StopWorkerThread();
                await BasisServerSideLogging.ShutdownAsync();
                BNL.Log("Server shut down successfully.");
            };
            if (config.EnableConsole)
            {
                BasisConsoleCommands.RegisterCommand("/players", "Lists all connected players.", BasisConsoleCommands.HandleShowPlayers);
                BasisConsoleCommands.RegisterCommand("/status", "Shows the current server status.", BasisConsoleCommands.HandleStatus);
                BasisConsoleCommands.RegisterCommand("/shutdown", "Shuts down the server.", BasisConsoleCommands.HandleShutdown);
                BasisConsoleCommands.RegisterCommand("/restart", "Restarts the server, applying settings that need a restart.", BasisConsoleCommands.HandleRestart);
                BasisConsoleCommands.RegisterCommand("/help", "Displays all available commands.", BasisConsoleCommands.HandleHelp);
                BasisConsoleCommands.RegisterCommand("/clear", "Clears the console", BasisConsoleCommands.HandleClear);
                BasisConsoleCommands.RegisterPermissionCommands();
                BasisConsoleCommands.RegisterConfigurationCommands(config);
                BasisConsoleCommands.StartConsoleListener();
            }
            // Wait for shutdown signal
            shutdownEvent.Wait();
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            BNL.LogError($"Unhandled Exception: {e.ExceptionObject}");
        }

        private static void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            BNL.LogError($"Unobserved Task Exception: {e.Exception.Message}");
            e.SetObserved();
        }
    }
}
