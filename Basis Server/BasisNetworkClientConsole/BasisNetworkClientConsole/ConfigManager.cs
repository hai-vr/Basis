using System.Xml.Linq;

namespace Basis.Config
{
    public static class ConfigManager
    {
        public static string Password = "default_password";
        public static string Ip = "localhost";
        public static int Port = 4296;
        public static int ClientCount = 250;

        public static string AvatarPassword = "default_avatar_password";
        public static string AvatarUrl = "http://localhost/avatar";
        public static int AvatarLoadMode = 1;

        private static readonly object _lock = new();
        static XElement? Child(XElement parent, string name) =>
            parent.Elements().FirstOrDefault(e => e.Name.LocalName == name);

        static string ReadString(XElement root, string name, string fallback)
        {
            var el = Child(root, name);
            if (el == null)
            {
                BNL.Log($"Missing <{name}>, using fallback.");
                return fallback;
            }

            var value = el.Value.Trim();
            BNL.Log($"Loaded {name}: [{value}]");
            return value;
        }

        static int ReadInt(XElement root, string name, int fallback)
        {
            var el = Child(root, name);
            if (el == null)
            {
                BNL.Log($"Missing <{name}>, using fallback {fallback}.");
                return fallback;
            }

            if (!int.TryParse(el.Value, out var value))
            {
                BNL.Log($"Invalid <{name}> value '{el.Value}', using fallback {fallback}.");
                return fallback;
            }

            BNL.Log($"Loaded {name}: {value}");
            return value;
        }

        // ---------------- MAIN ENTRY ----------------

        public static void LoadOrCreateConfigXml(string filePath)
        {
            lock (_lock)
            {
                filePath = Path.GetFullPath(filePath);
                BNL.Log($"Config path: {filePath}");

                if (!File.Exists(filePath))
                {
                    BNL.Log("Config file not found. Creating default.");

                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                        var doc = new XDocument(
                            new XComment(" BasisNetworkClientConsole load-tester configuration. Spawns ClientCount fake clients that connect to a server for stress testing. "),
                            new XElement("Configuration",
                                new XComment(" Server connection password; must match the server's <Password>. string. "),
                                new XElement("Password", Password),
                                new XComment(" Server host to connect to: hostname or IP (e.g. localhost / 127.0.0.1). string. "),
                                new XElement("Ip", Ip),
                                new XComment(" Server UDP port; must match the server's <SetPort>. int, range 1-65535. "),
                                new XElement("Port", Port),
                                new XComment(" Number of simulated clients to spawn for load testing. int (>= 1); higher counts need more CPU, memory and sockets. "),
                                new XElement("ClientCount", ClientCount),
                                new XComment(" Avatar unlock password/key sent with the avatar; used to decrypt the (encrypted .BEE) bundle at <AvatarUrl>. string. "),
                                new XElement("AvatarPassword", AvatarPassword),
                                new XComment(" Avatar source each fake client advertises. For AvatarLoadMode 0 this is the (encrypted .BEE) bundle download URL. string. "),
                                new XElement("AvatarUrl", AvatarUrl),
                                new XComment(" How receiving clients load the avatar: 0 = AssetBundle (download from AvatarUrl), 1 = Addressables, 2 = In-scene. Allowed: 0, 1 or 2. "),
                                new XElement("AvatarLoadMode", AvatarLoadMode)
                            )
                        );

                        // atomic write
                        var temp = filePath + ".tmp";
                        doc.Save(temp);
                        File.Move(temp, filePath);

                        BNL.Log("Default config created successfully.");
                    }
                    catch (Exception ex)
                    {
                        BNL.LogError("Failed to create config file." + ex.Message);
                    }

                    return;
                }

                XDocument docLoaded;
                try
                {
                    docLoaded = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);
                }
                catch (Exception ex)
                {
                    BNL.LogError("Failed to load config XML (corrupt or in use)." + ex.Message);
                    return;
                }

                var root = docLoaded.Root;
                if (root == null)
                {
                    BNL.Log("Config XML has no root element.");
                    return;
                }

                BNL.Log($"Root element: {root.Name} | Namespace: '{root.Name.NamespaceName}'");

                try
                {
                    Password = ReadString(root, "Password", Password);
                    Ip = ReadString(root, "Ip", Ip);
                    Port = ReadInt(root, "Port", Port);
                    ClientCount = ReadInt(root, "ClientCount", ClientCount);

                    AvatarPassword = ReadString(root, "AvatarPassword", AvatarPassword);
                    AvatarUrl = ReadString(root, "AvatarUrl", AvatarUrl);
                    AvatarLoadMode = ReadInt(root, "AvatarLoadMode", AvatarLoadMode);
                }
                catch (Exception ex)
                {
                    BNL.LogError("Unexpected error while parsing config." + ex.Message);
                }
            }
        }
    }
}
