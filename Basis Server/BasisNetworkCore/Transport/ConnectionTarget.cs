using System;
using System.Collections.Generic;

namespace Basis.Network.Core
{
    public sealed class ConnectionTarget
    {
        public string StackId;
        public string Raw;
        public Dictionary<string, string> Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ConnectionTarget() { }

        public ConnectionTarget(string stackId, string raw)
        {
            StackId = stackId ?? string.Empty;
            Raw = raw ?? string.Empty;
        }

        public string Get(string key, string fallback = null)
        {
            if (string.IsNullOrEmpty(key) || Properties == null) return fallback;
            return Properties.TryGetValue(key, out string v) ? v : fallback;
        }

        public bool TryGet(string key, out string value)
        {
            value = null;
            if (string.IsNullOrEmpty(key) || Properties == null) return false;
            return Properties.TryGetValue(key, out value);
        }

        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            Properties ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Properties[key] = value ?? string.Empty;
        }

        public static class Keys
        {
            public const string Address = "address";
            public const string Port = "port";
            public const string Password = "password";
            public const string LobbyId = "lobbyId";
        }
    }

    public interface IConnectionTargetParser
    {
        void Parse(ConnectionTarget target);
        string Format(ConnectionTarget target);
    }
}
