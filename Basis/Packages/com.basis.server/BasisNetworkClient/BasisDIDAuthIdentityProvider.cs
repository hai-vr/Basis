using Basis.Network.Core;
#if UNITY_2017_1_OR_NEWER
using UnityEngine;
#endif

namespace BasisNetworkClient
{
    public sealed class BasisDIDAuthIdentityProvider : IPlayerIdentityProvider
    {
        public const string Id = "did";

        public string ProviderId => Id;

        public PlayerIdentity GetOrCreate()
        {
            string uuid = BasisDIDAuthIdentityClient.GetOrSaveDID();
            return new PlayerIdentity
            {
                Uuid = uuid,
                Provider = Id,
            };
        }

#if UNITY_2017_1_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoRegister()
        {
            BasisPlayerIdentityRegistry.Register(new BasisDIDAuthIdentityProvider());
            BasisPlayerIdentityRegistry.ActiveProviderId = Id;
        }
#endif
    }
}
