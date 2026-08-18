using BasisPermissions;
using static BasisPermissions.PermissionManager;

namespace BasisNetworkConsole
{
    /// <summary>
    /// First-boot setup wizard. Runs once, before the network server starts, when no
    /// config.xml existed at launch (a brand-new server). It walks the operator through
    /// the core server settings and asks them to designate at least one admin, so a
    /// fresh server is never left misconfigured or with nobody able to moderate it.
    /// The admin step can be skipped for a server that does not want one yet.
    /// Admins are stored in permissions.xml as members of the "admin" group (full access),
    /// exactly like the runtime "/perm user group add &lt;uuid&gt; admin" command; the other
    /// settings are written straight back to config.xml.
    /// </summary>
    public static class BasisSetupWizard
    {
        /// <summary>
        /// Environment variable used to seed admins on headless/automated first boots
        /// (Docker, CI) where no interactive console is attached. Accepts one UUID or a
        /// comma/space separated list.
        /// </summary>
        public const string AdminEnvVar = "BasisFirstAdmin";
        private const string AdminGroup = "admin";
        private const string DefaultPassword = "default_password";

        public static void Run(Configuration config, string configFilePath)
        {
            string configDir = Path.GetDirectoryName(configFilePath) ?? string.Empty;
            string permissionsPath = Path.Combine(configDir, "permissions.xml");

            // Bring the shared permission store up far enough that the "admin" group exists,
            // then write straight to permissions.xml. When the server boots a moment later,
            // PermissionIntegration.Init reloads this same file, so the admin we add sticks.
            PermissionManager pm = PermissionIntegration.Manager;
            pm.SetXmlPath(permissionsPath);
            pm.LoadFromXml();
            pm.EnsureDefaults();

            // Headless / automated deploys: seed admins from the env var instead of prompting.
            // Server settings on these deployments come from env vars / a mounted config.xml,
            // so we don't run the interactive settings walkthrough here.
            string? fromEnv = Environment.GetEnvironmentVariable(AdminEnvVar);
            if (!string.IsNullOrWhiteSpace(fromEnv))
            {
                int seeded = SeedFromList(pm, fromEnv);
                if (seeded > 0)
                {
                    pm.SaveToXml();
                    BNL.Log($"[Setup] First boot: added {seeded} admin(s) from ${AdminEnvVar}.");
                    return;
                }
                BNL.LogWarning($"[Setup] ${AdminEnvVar} was set but contained no usable UUIDs.");
            }

            if (!CanPrompt())
            {
                WarnNoAdmin();
                return;
            }

            RunInteractive(pm, config, configFilePath);
        }

        private static void RunInteractive(PermissionManager pm, Configuration config, string configFilePath)
        {
            PrintIntro();
            RunSettingsWalkthrough(config, configFilePath);
            int admins = PromptAdmins(pm);
            BNL.Log(admins == 0
                ? "[Setup] First-time setup complete - no admin configured. Starting server..."
                : $"[Setup] First-time setup complete - {admins} admin(s) configured. Starting server...");
        }

        // ----------------------------------------------------------------------------
        // Core server settings
        // ----------------------------------------------------------------------------

        private static void RunSettingsWalkthrough(Configuration config, string configFilePath)
        {
            BNL.Log("");
            BNL.Log("--- Server settings ---");
            BNL.Log("Press Enter to keep the [current] value, or type a new one.");
            BNL.Log("");

            string name = PromptString("Server name (shown in the server list)", config.ServerName);
            string motd = PromptString("Message of the day / MOTD", config.ServerMotd);
            ushort port = PromptUShort("Game port (UDP)", config.SetPort);
            string password = PromptPassword(config.Password);
            int peerLimit = PromptInt("Max players", config.PeerLimit, 1);

            BNL.Log("");
            BNL.Log("--- Review ---");
            BNL.Log($"  Server name : {name}");
            BNL.Log($"  MOTD        : {(motd.Length == 0 ? "<none>" : motd)}");
            BNL.Log($"  Game port   : {port}");
            BNL.Log($"  Password    : {(password == DefaultPassword ? DefaultPassword : "(set)")}");
            BNL.Log($"  Max players : {peerLimit}");
            BNL.Log("");

            if (!ConfirmDefaultYes("Save these settings and start the server?"))
            {
                BNL.Log("[Setup] Settings discarded - keeping config.xml defaults.");
                return;
            }

            config.ServerName = name;
            config.ServerMotd = motd;
            config.SetPort = port;
            config.Password = password;
            config.PeerLimit = peerLimit;
            config.SaveToXml(configFilePath);
            BNL.Log("[Setup] Settings saved to config.xml.");
        }

        // ----------------------------------------------------------------------------
        // Admin
        // ----------------------------------------------------------------------------

        private static int PromptAdmins(PermissionManager pm)
        {
            BNL.Log("");
            BNL.Log("--- Admin setup ---");
            PrintAdminHelp();

            int adminCount = 0;
            while (true)
            {
                Console.Write(adminCount == 0
                    ? "Admin player UUID ('skip' to start without one): "
                    : "Add another admin UUID (leave blank to finish): ");
                string input = (Console.ReadLine() ?? string.Empty).Trim();

                if (input.Equals("help", StringComparison.OrdinalIgnoreCase) || input == "?")
                {
                    PrintAdminHelp();
                    continue;
                }

                if (input.Length == 0 || input.Equals("skip", StringComparison.OrdinalIgnoreCase))
                {
                    if (adminCount > 0) break;
                    if (!Confirm("Start this server with no admin? Nobody will be able to moderate or configure it in game."))
                    {
                        continue;
                    }
                    WarnSkippedAdmin();
                    break;
                }

                if (!LooksLikeDid(input) && !Confirm($"'{input}' doesn't look like a did:key UUID. Add it anyway?"))
                {
                    continue;
                }

                pm.AddUserToGroup(input, AdminGroup);
                pm.SaveToXml();
                adminCount++;
                BNL.Log($"[Setup] Added admin: {input}");
            }

            return adminCount;
        }

        /// <summary>Add every UUID in a delimited list to the admin group. Returns how many were added.</summary>
        private static int SeedFromList(PermissionManager pm, string raw)
        {
            int added = 0;
            foreach (string part in raw.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string uuid = part.Trim();
                if (uuid.Length == 0) continue;
                pm.AddUserToGroup(uuid, AdminGroup);
                BNL.Log($"[Setup] Added admin: {uuid}");
                added++;
            }
            return added;
        }

        // ----------------------------------------------------------------------------
        // Prompt helpers
        // ----------------------------------------------------------------------------

        private static string PromptString(string label, string current)
        {
            string shown = current.Length == 0 ? "<none>" : current;
            Console.Write($"{label} [{shown}]: ");
            string input = (Console.ReadLine() ?? string.Empty).Trim();
            return input.Length == 0 ? current : input;
        }

        private static ushort PromptUShort(string label, ushort current)
        {
            while (true)
            {
                Console.Write($"{label} [{current}]: ");
                string input = (Console.ReadLine() ?? string.Empty).Trim();
                if (input.Length == 0) return current;
                if (ushort.TryParse(input, out ushort value)) return value;
                BNL.LogWarning($"Please enter a whole number between 0 and {ushort.MaxValue}.");
            }
        }

        private static int PromptInt(string label, int current, int min)
        {
            while (true)
            {
                Console.Write($"{label} [{current}]: ");
                string input = (Console.ReadLine() ?? string.Empty).Trim();
                if (input.Length == 0) return current;
                if (int.TryParse(input, out int value) && value >= min) return value;
                BNL.LogWarning($"Please enter a whole number of {min} or more.");
            }
        }

        private static string PromptPassword(string current)
        {
            Console.Write("Server password [keep current]: ");
            string input = (Console.ReadLine() ?? string.Empty).Trim();
            return input.Length == 0 ? current : input;
        }

        // ----------------------------------------------------------------------------
        // Text / confirmation
        // ----------------------------------------------------------------------------

        private static void PrintIntro()
        {
            BNL.Log("============================================================");
            BNL.Log(" Basis Server - First-Time Setup");
            BNL.Log("============================================================");
            BNL.Log("No config.xml was found, so this is a brand-new server.");
            BNL.Log("This quick wizard sets up the core server settings and has");
            BNL.Log("you designate an admin before the server starts.");
        }

        private static void PrintAdminHelp()
        {
            BNL.Log("An admin is identified by their Basis player UUID (a DID), which");
            BNL.Log("looks like:  did:key:z6Mk...");
            BNL.Log("Where to find it:");
            BNL.Log("  - in the Basis client: open Settings > Developer tab, find the");
            BNL.Log("    \"Identity Key\" section, and tap the eye icon on the \"UUID\"");
            BNL.Log("    field to reveal it; or");
            BNL.Log("  - in this server's log: every time a player connects it prints");
            BNL.Log("    their UUID as \"(UUID did:key:...)\".");
            BNL.Log("The UUID you enter is added to the \"admin\" group (full access).");
            BNL.Log("You can add more than one; leave the prompt blank once done.");
            BNL.Log("Don't have it to hand? Type 'skip' to start without an admin and");
            BNL.Log("add one later with:  /perm user group add <uuid> admin");
            BNL.Log("");
        }

        private static void WarnSkippedAdmin()
        {
            BNL.LogWarning("");
            BNL.LogWarning("============================================================");
            BNL.LogWarning(" Starting with NO ADMIN");
            BNL.LogWarning("------------------------------------------------------------");
            BNL.LogWarning(" Nobody can moderate or configure this server in game.");
            BNL.LogWarning(" Add one at any time from this console with:");
            BNL.LogWarning("   /perm user group add <uuid> admin");
            BNL.LogWarning("============================================================");
        }

        private static void WarnNoAdmin()
        {
            BNL.LogWarning("============================================================");
            BNL.LogWarning(" Basis Server first-time setup: NO ADMIN CONFIGURED");
            BNL.LogWarning("------------------------------------------------------------");
            BNL.LogWarning(" No interactive console is attached and the environment");
            BNL.LogWarning($" variable {AdminEnvVar} is not set, so the server is starting");
            BNL.LogWarning(" WITHOUT an admin - nobody can moderate or configure it.");
            BNL.LogWarning(" To set one up, provide the admin's player UUID, e.g.:");
            BNL.LogWarning($"   {AdminEnvVar}=did:key:z6Mk...   (comma-separate for several)");
            BNL.LogWarning(" then delete config/config.xml and restart, or run the server");
            BNL.LogWarning(" once in an interactive terminal to use the setup wizard.");
            BNL.LogWarning("============================================================");
        }

        private static bool LooksLikeDid(string value)
        {
            return value.StartsWith("did:", StringComparison.OrdinalIgnoreCase)
                && value.Length > "did:".Length
                && !value.Contains(' ');
        }

        /// <summary>Yes/no prompt that defaults to NO on a blank line.</summary>
        private static bool Confirm(string question)
        {
            Console.Write($"{question} (y/N): ");
            string answer = (Console.ReadLine() ?? string.Empty).Trim();
            return answer.Equals("y", StringComparison.OrdinalIgnoreCase)
                || answer.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Yes/no prompt that defaults to YES on a blank line.</summary>
        private static bool ConfirmDefaultYes(string question)
        {
            Console.Write($"{question} (Y/n): ");
            string answer = (Console.ReadLine() ?? string.Empty).Trim();
            return answer.Length == 0
                || answer.Equals("y", StringComparison.OrdinalIgnoreCase)
                || answer.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True only when a real interactive terminal is attached (so a prompt won't hang a daemon).</summary>
        private static bool CanPrompt()
        {
            try
            {
                return !Console.IsInputRedirected;
            }
            catch
            {
                return false;
            }
        }
    }
}
