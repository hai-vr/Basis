using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Basis.Network.Core
{
    public sealed class ServerProbeResult
    {
        public bool Reachable;
        public string Error;
        public bool TimedOut;
        public ushort Online;
        public ushort Max;
        public ushort ProtocolVersion;
        public string Name;
        public string Motd;
        public int RoundTripMs;
        public Dictionary<string, string> Extras = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>
        /// The IP address that successfully responded to the probe. Null when unreachable.
        /// Callers can use this to bypass DNS re-resolution and connect directly to the
        /// confirmed-reachable address family (supporting IPv6→IPv4 fallback).
        /// </summary>
        public IPAddress ResolvedAddress;
    }

    public delegate Task<ServerProbeResult> StackProbeDelegate(ConnectionTarget target, int timeoutMs, CancellationToken ct);
    public delegate IPeerIntroducer PeerIntroducerFactory(NetManager activeManager);

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

        private sealed class Slot
        {
            public string Id;
            public string DisplayName;
            public NetManagerFactory Factory;
            public IConnectionTargetParser Parser;
            public StackProbeDelegate Probe;
            public Action Tick;
            public PeerIntroducerFactory IntroducerFactory;
        }

        private static readonly Dictionary<string, Slot> _slots
            = new Dictionary<string, Slot>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<StackInfo> _stacks = new List<StackInfo>();
        private static readonly object _lock = new object();
        private static string _activeStackId = string.Empty;

        public static event Action<string> ActiveStackChanged;

        static BasisNetworkStackRegistry()
        {
            Register(LiteNetLibId, "LiteNetLib", (listener, config) => new LNLNetManager(listener, config));
            RegisterParser(LiteNetLibId, new LNLConnectionTargetParser());
            BasisTransportConfigStore.RegisterType(LiteNetLibId, typeof(LNLTransportConfig));
        }

        public static void Register(string id, string displayName, NetManagerFactory factory)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Stack id is required", nameof(id));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            lock (_lock)
            {
                if (_slots.ContainsKey(id)) return;
                _slots[id] = new Slot { Id = id, DisplayName = string.IsNullOrEmpty(displayName) ? id : displayName, Factory = factory };
                _stacks.Add(new StackInfo(id, _slots[id].DisplayName));
            }
        }

        public static void RegisterParser(string stackId, IConnectionTargetParser parser)
        {
            if (string.IsNullOrEmpty(stackId)) throw new ArgumentException("Stack id is required", nameof(stackId));
            if (parser == null) throw new ArgumentNullException(nameof(parser));
            lock (_lock)
            {
                if (!_slots.TryGetValue(stackId, out Slot slot))
                {
                    BNL.LogWarning($"Cannot register parser for unknown stack '{stackId}'");
                    return;
                }
                slot.Parser = parser;
            }
        }

        public static void RegisterProbe(string stackId, StackProbeDelegate probe)
        {
            if (string.IsNullOrEmpty(stackId)) throw new ArgumentException("Stack id is required", nameof(stackId));
            if (probe == null) throw new ArgumentNullException(nameof(probe));
            lock (_lock)
            {
                if (!_slots.TryGetValue(stackId, out Slot slot))
                {
                    BNL.LogWarning($"Cannot register probe for unknown stack '{stackId}'");
                    return;
                }
                slot.Probe = probe;
            }
        }

        public static void RegisterTick(string stackId, Action tick)
        {
            if (string.IsNullOrEmpty(stackId)) throw new ArgumentException("Stack id is required", nameof(stackId));
            if (tick == null) throw new ArgumentNullException(nameof(tick));
            lock (_lock)
            {
                if (!_slots.TryGetValue(stackId, out Slot slot))
                {
                    BNL.LogWarning($"Cannot register tick for unknown stack '{stackId}'");
                    return;
                }
                slot.Tick = tick;
            }
        }

        public static void RegisterIntroducerFactory(string stackId, PeerIntroducerFactory factory)
        {
            if (string.IsNullOrEmpty(stackId)) throw new ArgumentException("Stack id is required", nameof(stackId));
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            lock (_lock)
            {
                if (!_slots.TryGetValue(stackId, out Slot slot))
                {
                    BNL.LogWarning($"Cannot register peer introducer for unknown stack '{stackId}'");
                    return;
                }
                slot.IntroducerFactory = factory;
            }
        }

        public static NetManager Create(string id, EventBasedNetListener listener, Configuration configuration)
        {
            string effective = string.IsNullOrEmpty(id) ? DefaultId : id;
            Slot slot;
            lock (_lock)
            {
                if (!_slots.TryGetValue(effective, out slot))
                {
                    BNL.LogWarning($"Network stack '{effective}' is not registered, falling back to '{DefaultId}'");
                    slot = _slots[DefaultId];
                    effective = DefaultId;
                }
            }
            NetManager mgr = slot.Factory(listener, configuration);
            SetActiveStackId(effective);
            return mgr;
        }

        public static string ActiveStackId
        {
            get { lock (_lock) return _activeStackId; }
        }

        public static void SetActiveStackId(string id)
        {
            string normalized = string.IsNullOrEmpty(id) ? string.Empty : id;
            bool changed = false;
            lock (_lock)
            {
                if (!string.Equals(_activeStackId, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    _activeStackId = normalized;
                    changed = true;
                }
            }
            if (changed)
            {
                try { ActiveStackChanged?.Invoke(normalized); }
                catch (Exception ex) { BNL.LogError($"ActiveStackChanged handler threw: {ex.Message}"); }
            }
        }

        public static IConnectionTargetParser GetParser(string stackId)
        {
            string effective = string.IsNullOrEmpty(stackId) ? DefaultId : stackId;
            lock (_lock)
            {
                if (_slots.TryGetValue(effective, out Slot slot) && slot.Parser != null) return slot.Parser;
                if (!string.Equals(effective, DefaultId, StringComparison.OrdinalIgnoreCase))
                {
                    BNL.LogWarning($"No connection-target parser registered for stack '{effective}', falling back to '{DefaultId}'");
                    if (_slots.TryGetValue(DefaultId, out Slot fallback)) return fallback.Parser;
                }
            }
            return null;
        }

        public static Task<ServerProbeResult> ProbeAsync(ConnectionTarget target, int timeoutMs, CancellationToken ct)
        {
            if (target == null) return Task.FromResult(new ServerProbeResult { Error = "Target is null" });
            string stackId = string.IsNullOrEmpty(target.StackId) ? DefaultId : target.StackId;
            StackProbeDelegate probe;
            lock (_lock)
            {
                if (!_slots.TryGetValue(stackId, out Slot slot) || slot.Probe == null)
                {
                    if (!string.Equals(stackId, DefaultId, StringComparison.OrdinalIgnoreCase))
                    {
                        BNL.LogWarning($"No probe registered for stack '{stackId}', falling back to '{DefaultId}'");
                    }
                    if (!_slots.TryGetValue(DefaultId, out Slot fallback) || fallback.Probe == null)
                    {
                        return Task.FromResult(new ServerProbeResult { Error = $"No probe registered for stack '{stackId}' (no fallback available)" });
                    }
                    probe = fallback.Probe;
                }
                else
                {
                    probe = slot.Probe;
                }
            }
            return probe(target, timeoutMs, ct);
        }

        public static void TickActive()
        {
            Action tick = null;
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_activeStackId)
                    && _slots.TryGetValue(_activeStackId, out Slot slot))
                {
                    tick = slot.Tick;
                }
            }
            if (tick == null) return;
            try { tick(); }
            catch (Exception ex) { BNL.LogError($"Stack tick threw: {ex.Message}"); }
        }

        public static IPeerIntroducer CreateIntroducer(string stackId, NetManager activeManager)
        {
            string effective = string.IsNullOrEmpty(stackId) ? DefaultId : stackId;
            PeerIntroducerFactory factory;
            lock (_lock)
            {
                if (!_slots.TryGetValue(effective, out Slot slot) || slot.IntroducerFactory == null)
                {
                    if (!string.Equals(effective, DefaultId, StringComparison.OrdinalIgnoreCase))
                    {
                        BNL.LogWarning($"No peer introducer registered for stack '{effective}', falling back to '{DefaultId}'");
                    }
                    if (!_slots.TryGetValue(DefaultId, out Slot fallback) || fallback.IntroducerFactory == null) return null;
                    factory = fallback.IntroducerFactory;
                }
                else
                {
                    factory = slot.IntroducerFactory;
                }
            }
            return factory(activeManager);
        }

        public static bool IsRegistered(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            lock (_lock) return _slots.ContainsKey(id);
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
                if (_slots.TryGetValue(id, out Slot slot)) return slot.DisplayName;
            }
            return id;
        }
    }
}
