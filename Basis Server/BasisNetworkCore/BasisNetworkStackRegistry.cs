using System;
using System.Collections.Generic;

namespace Basis.Network.Core
{
    public static class BasisNetworkStackRegistry
    {
        public const string LiteNetLibId = "litenetlib";
        public const string DefaultId = LiteNetLibId;

        public readonly struct StackInfo
        {
            public readonly string Id;
            public readonly string DisplayName;
            public StackInfo(string id, string displayName)
            {
                Id = id;
                DisplayName = displayName;
            }
        }

        public delegate NetManager NetManagerFactory(EventBasedNetListener listener, Configuration configuration);

        private static readonly Dictionary<string, NetManagerFactory> _factories
            = new Dictionary<string, NetManagerFactory>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<StackInfo> _stacks = new List<StackInfo>();
        private static readonly object _lock = new object();

        static BasisNetworkStackRegistry()
        {
            Register(LiteNetLibId, "LiteNetLib", (listener, config) => new LNLNetManager(listener, config));
        }

        public static void Register(string id, string displayName, NetManagerFactory factory)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Stack id is required", nameof(id));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            lock (_lock)
            {
                if (_factories.ContainsKey(id)) return;
                _factories[id] = factory;
                _stacks.Add(new StackInfo(id, string.IsNullOrEmpty(displayName) ? id : displayName));
            }
        }

        public static NetManager Create(string id, EventBasedNetListener listener, Configuration configuration)
        {
            string effective = string.IsNullOrEmpty(id) ? DefaultId : id;
            NetManagerFactory factory;
            lock (_lock)
            {
                if (!_factories.TryGetValue(effective, out factory))
                {
                    BNL.LogWarning($"Network stack '{effective}' is not registered, falling back to '{DefaultId}'");
                    factory = _factories[DefaultId];
                }
            }
            return factory(listener, configuration);
        }

        public static bool IsRegistered(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (_lock) return _factories.ContainsKey(id);
        }

        public static IReadOnlyList<StackInfo> Stacks
        {
            get { lock (_lock) return _stacks.ToArray(); }
        }

        public static string GetDisplayName(string id)
        {
            if (string.IsNullOrEmpty(id)) id = DefaultId;
            lock (_lock)
            {
                foreach (StackInfo s in _stacks)
                {
                    if (string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase))
                        return s.DisplayName;
                }
            }
            return id;
        }
    }
}
