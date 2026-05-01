using BasisNetworking.InitialData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace BasisNetworking.InitalData
{
    public static class BasisDefaultLibraryLoader
    {
        public static List<BasisDefaultLibraryConfiguration> LoadedItems = new List<BasisDefaultLibraryConfiguration>();

        public static void LoadXML(string FolderName)
        {
            try
            {
                string exeDirectory = AppDomain.CurrentDomain.BaseDirectory;
                string newFolderPath = Path.Combine(exeDirectory, FolderName);

                if (!Directory.Exists(newFolderPath))
                {
                    Directory.CreateDirectory(newFolderPath);
                    BNL.Log("Folder created successfully: " + newFolderPath);

                    string exampleFilePath = Path.Combine(newFolderPath, "ExampleAvatardisabled.xml[remove]");
                    File.WriteAllText(exampleFilePath, exampleXml);
                    BNL.Log("Example XML file created at: " + exampleFilePath);
                }

                LoadedItems.Clear();

                BasisDefaultLibraryConfiguration[] configurations = BasisDefaultLibraryConfiguration.LoadAllFromFolder(newFolderPath);
                foreach (BasisDefaultLibraryConfiguration config in configurations)
                {
                    if (string.IsNullOrEmpty(config.Url))
                    {
                        BNL.LogError("Skipping default library entry with empty Url");
                        continue;
                    }
                    LoadedItems.Add(config);
                    BNL.Log($"Default library entry loaded: Mode={config.Mode}, Url={config.Url}");
                }

                BNL.Log($"Default library loaded with {LoadedItems.Count} item(s).");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading default library: {ex.Message}");
            }
        }

        /// <summary>
        /// Persist a single entry as a new XML file under the configured folder
        /// and append it to the in-memory list. Returns the absolute path written
        /// or an empty string on failure.
        /// </summary>
        public static string SaveItem(string folderName, BasisDefaultLibraryConfiguration config)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.Url))
            {
                BNL.LogError("Refusing to save default library entry with empty Url.");
                return string.Empty;
            }

            try
            {
                string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, folderName);
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                string fileName = BuildUniqueFileName(folder, config);
                string fullPath = Path.Combine(folder, fileName);

                var serializer = new XmlSerializer(typeof(BasisDefaultLibraryConfiguration));
                using (var writer = new StreamWriter(fullPath))
                {
                    serializer.Serialize(writer, config);
                }

                LoadedItems.Add(config);
                BNL.Log($"Default library entry saved: {fullPath}");
                return fullPath;
            }
            catch (Exception ex)
            {
                BNL.LogError($"Failed to save default library entry: {ex.Message}");
                return string.Empty;
            }
        }

        private static string BuildUniqueFileName(string folder, BasisDefaultLibraryConfiguration config)
        {
            string modeName = config.Mode switch
            {
                0 => "avatar",
                1 => "world",
                2 => "prop",
                _ => "item",
            };
            string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            string baseName = $"{modeName}_{stamp}.xml";
            string candidate = Path.Combine(folder, baseName);

            // Defensive: collisions are virtually impossible with millisecond precision
            // but two clicks within the same millisecond would clobber. Append a counter.
            int counter = 1;
            while (File.Exists(candidate))
            {
                baseName = $"{modeName}_{stamp}_{counter}.xml";
                candidate = Path.Combine(folder, baseName);
                counter++;
            }
            return baseName;
        }

        public const string exampleXml = @"<BasisDefaultLibraryConfiguration>
    <!-- 0 = Avatar, 1 = World, 2 = Prop -->
    <Mode>0</Mode>
    <!-- Bee file URL -->
    <Url></Url>
    <!-- Unlock password -->
    <Password></Password>
</BasisDefaultLibraryConfiguration>";
    }
}
