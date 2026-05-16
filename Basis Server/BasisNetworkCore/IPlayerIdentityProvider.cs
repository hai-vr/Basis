using System;
using System.Collections.Generic;

namespace Basis.Network.Core
{
    public sealed class PlayerIdentity
    {
        public string Uuid;
        public string Provider;
        public Dictionary<string, string> Properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public interface IPlayerIdentityProvider
    {
        string ProviderId { get; }
        PlayerIdentity GetOrCreate();
    }

    public static class BasisPlayerIdentityRegistry
    {
        public const string DefaultProviderId = "did";

        private static readonly Dictionary<string, IPlayerIdentityProvider> _providers
            = new Dictionary<string, IPlayerIdentityProvider>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();
        private static string _activeProviderId = DefaultProviderId;

        public static void Register(IPlayerIdentityProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (string.IsNullOrEmpty(provider.ProviderId)) throw new ArgumentException("ProviderId is required", nameof(provider));
            lock (_lock) _providers[provider.ProviderId] = provider;
        }

        public static string ActiveProviderId
        {
            get { lock (_lock) return _activeProviderId; }
            set { lock (_lock) _activeProviderId = string.IsNullOrEmpty(value) ? DefaultProviderId : value; }
        }

        public static PlayerIdentity ResolveActive()
        {
            IPlayerIdentityProvider provider = null;
            lock (_lock)
            {
                if (!_providers.TryGetValue(_activeProviderId, out provider))
                    _providers.TryGetValue(DefaultProviderId, out provider);
            }
            return provider?.GetOrCreate();
        }

        public static PlayerIdentity Resolve(string providerId)
        {
            IPlayerIdentityProvider provider = null;
            lock (_lock)
            {
                if (string.IsNullOrEmpty(providerId)) providerId = _activeProviderId;
                _providers.TryGetValue(providerId, out provider);
            }
            return provider?.GetOrCreate();
        }

        public static bool IsRegistered(string providerId)
        {
            if (string.IsNullOrEmpty(providerId)) return false;
            lock (_lock) return _providers.ContainsKey(providerId);
        }
    }
}
