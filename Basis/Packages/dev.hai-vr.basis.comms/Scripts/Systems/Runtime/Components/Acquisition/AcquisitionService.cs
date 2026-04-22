using System.Collections.Generic;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/Internal/Acquisition Service")]
    public class AcquisitionService : MonoBehaviour
    {
        public static AcquisitionService SceneInstance => HVRCommsUtil.GetOrCreateSceneInstance(ref _sceneInstance);
        private static AcquisitionService _sceneInstance;

        public delegate void AddressUpdated(int address, float value);

        internal readonly Dictionary<int, AcquisitionForAddress> _addressUpdated = new();

        public void Submit(int address, float value)
        {
            if (_addressUpdated.TryGetValue(address, out var acquisitor))
            {
                acquisitor.lastValueForDebugPurposesOnly = value;
                acquisitor.Invoke(address, value);
            }
        }

        public void RegisterAddresses(int[] addressBase, AddressUpdated onAddressUpdated)
        {
            foreach (var address in addressBase)
            {
                _addressUpdated.TryAdd(address, new AcquisitionForAddress());

                var acquisitor = _addressUpdated[address];
                acquisitor.OnAddressUpdated -= onAddressUpdated;
                acquisitor.OnAddressUpdated += onAddressUpdated;
            }
        }

        public void UnregisterAddresses(int[] addressBase, AddressUpdated onAddressUpdated)
        {
            foreach (var address in addressBase)
            {
                if (_addressUpdated.TryGetValue(address, out var acquisitor))
                {
                    acquisitor.OnAddressUpdated -= onAddressUpdated;
                }
            }
        }
    }

    internal class AcquisitionForAddress
    {
        internal event AcquisitionService.AddressUpdated OnAddressUpdated;
        internal float lastValueForDebugPurposesOnly = 0f;

        public void Invoke(int address, float value) => OnAddressUpdated?.Invoke(address, value);
        internal int GetListenersCount() => OnAddressUpdated?.GetInvocationList().Length ?? 0;
    }
}
