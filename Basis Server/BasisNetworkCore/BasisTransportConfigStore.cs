using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace Basis.Network.Core
{
    /// <summary>
    /// Implemented by a transport config that needs to change values it has already written to
    /// disc, not merely gain new ones.
    ///
    /// The normal upgrade path deliberately preserves every value already in the file — an operator
    /// who set something meant it. That is the wrong behaviour exactly once: when a shipped default
    /// turns out to be harmful, every existing install is carrying it explicitly and would never
    /// receive the fix. A migration may then replace that specific value, and must leave anything
    /// else alone.
    /// </summary>
    public interface IBasisTransportConfigMigration
    {
        /// <summary>
        /// Called immediately after deserialisation with the version found in the file, before the
        /// missing-settings upgrade runs. Implementations must be idempotent and must only touch
        /// values they can recognise as the old default.
        /// </summary>
        void MigrateFrom(int loadedVersion);
    }

    public static class BasisTransportConfigStore
    {
        public const string TransportsFolderName = "transports";

        private static readonly Dictionary<string, object> _configs = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Type> _types = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        public static void RegisterType(string stackId, Type configType)
        {
            if (string.IsNullOrEmpty(stackId)) throw new ArgumentException("Stack id is required", nameof(stackId));
            if (configType == null) throw new ArgumentNullException(nameof(configType));
            lock (_lock)
            {
                _types[stackId] = configType;
                if (!_configs.ContainsKey(stackId))
                {
                    _configs[stackId] = Activator.CreateInstance(configType);
                }
            }
        }

        public static T Get<T>(string stackId) where T : class, new()
        {
            if (string.IsNullOrEmpty(stackId)) stackId = BasisNetworkStackRegistry.DefaultId;
            lock (_lock)
            {
                if (_configs.TryGetValue(stackId, out object c))
                {
                    if (c is T typed) return typed;
                }
                T fresh = new T();
                _configs[stackId] = fresh;
                return fresh;
            }
        }

        public static object Get(string stackId)
        {
            if (string.IsNullOrEmpty(stackId)) return null;
            lock (_lock)
            {
                _configs.TryGetValue(stackId, out object c);
                return c;
            }
        }

        public static void Set<T>(string stackId, T config) where T : class
        {
            if (string.IsNullOrEmpty(stackId)) throw new ArgumentException("Stack id is required", nameof(stackId));
            if (config == null) throw new ArgumentNullException(nameof(config));
            lock (_lock) _configs[stackId] = config;
        }

        public static IReadOnlyDictionary<string, Type> RegisteredTypes
        {
            get { lock (_lock) return new Dictionary<string, Type>(_types); }
        }

        public static void LoadAll(string configBaseDir)
        {
            if (string.IsNullOrEmpty(configBaseDir)) return;
            string transportsDir = Path.Combine(configBaseDir, TransportsFolderName);
            try { Directory.CreateDirectory(transportsDir); }
            catch (Exception ex) { BNL.LogWarning($"Could not create transports dir '{transportsDir}': {ex.Message}"); }

            Dictionary<string, Type> types;
            lock (_lock) types = new Dictionary<string, Type>(_types);

            foreach (KeyValuePair<string, Type> kv in types)
            {
                string path = Path.Combine(transportsDir, kv.Key + ".xml");
                object loaded = LoadOrCreate(kv.Value, path);
                lock (_lock) _configs[kv.Key] = loaded;
            }
        }

        public static void SaveAll(string configBaseDir)
        {
            if (string.IsNullOrEmpty(configBaseDir)) return;
            string transportsDir = Path.Combine(configBaseDir, TransportsFolderName);
            try { Directory.CreateDirectory(transportsDir); }
            catch (Exception ex) { BNL.LogWarning($"Could not create transports dir '{transportsDir}': {ex.Message}"); return; }

            Dictionary<string, Type> types;
            Dictionary<string, object> configs;
            lock (_lock)
            {
                types = new Dictionary<string, Type>(_types);
                configs = new Dictionary<string, object>(_configs);
            }

            foreach (KeyValuePair<string, Type> kv in types)
            {
                if (!configs.TryGetValue(kv.Key, out object config) || config == null) continue;
                string path = Path.Combine(transportsDir, kv.Key + ".xml");
                SaveAtomic(kv.Value, config, path);
            }
        }

        private static object LoadOrCreate(Type type, string path)
        {
            XmlSerializer serializer = new XmlSerializer(type);
            if (File.Exists(path))
            {
                try
                {
                    object loaded;
                    using (StreamReader reader = new StreamReader(path))
                    {
                        loaded = serializer.Deserialize(reader);
                    }

                    // Retire values a newer build knows are harmful, before the upgrade below
                    // re-saves the file. Adding settings is not enough here: a bad default already
                    // written to disc is indistinguishable from a deliberate choice unless the
                    // config itself says which it was.
                    if (loaded is IBasisTransportConfigMigration migratable)
                    {
                        migratable.MigrateFrom(BasisConfigXmlDocs.ReadVersion(loaded));
                    }

                    // Heal an older sidecar: re-save when it predates the current schema version
                    // or is missing any setting we now write, so new settings get added.
                    if (BasisConfigXmlDocs.NeedsUpgrade(path, type, loaded))
                    {
                        BasisConfigXmlDocs.StampVersion(loaded);
                        SaveAtomic(type, loaded, path);
                        BNL.Log($"Transport config '{path}' is from an older version; adding missing settings.");
                    }
                    return loaded;
                }
                catch (Exception ex)
                {
                    BNL.LogWarning($"Failed to load transport config '{path}': {ex.Message}. Recreating.");
                }
            }

            object created = Activator.CreateInstance(type);
            BasisConfigXmlDocs.StampVersion(created);
            try
            {
                using StreamWriter writer = new StreamWriter(path);
                BasisConfigXmlDocs.Serialize(serializer, type, created, writer);
            }
            catch (Exception ex)
            {
                BNL.LogWarning($"Failed to write transport config '{path}': {ex.Message}");
            }
            return created;
        }

        private static void SaveAtomic(Type type, object config, string path)
        {
            try
            {
                BasisConfigXmlDocs.StampVersion(config);
                XmlSerializer serializer = new XmlSerializer(type);
                string tempPath = path + ".tmp";
                using (StreamWriter writer = new StreamWriter(tempPath))
                {
                    BasisConfigXmlDocs.Serialize(serializer, type, config, writer);
                }
                if (File.Exists(path)) File.Replace(tempPath, path, null);
                else File.Move(tempPath, path);
            }
            catch (Exception ex)
            {
                BNL.LogWarning($"Failed to save transport config '{path}': {ex.Message}");
            }
        }
    }
}
