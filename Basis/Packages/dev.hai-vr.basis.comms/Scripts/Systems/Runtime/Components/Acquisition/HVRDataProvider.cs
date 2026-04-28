using System;
using System.Collections.Generic;

namespace HVR.Basis.Comms
{
    public class HVRDataProvider
    {
        public delegate void AddressUpdated(int address, float value);

        internal readonly Dictionary<int, AcquisitionForAddress> _addressUpdated = new();

        public void Submit(int address, float value)
        {
            if (address == 0) throw new IndexOutOfRangeException("Address cannot be zero, this may indicate an initialization issue.");

            if (_addressUpdated.TryGetValue(address, out var acquisitor))
            {
                acquisitor.value = value;
                acquisitor.Invoke(address, value);
            }
        }

        public void SubmitOrDefineDefaultValue(int address, float value)
        {
            if (address == 0) throw new IndexOutOfRangeException("Address cannot be zero, this may indicate an initialization issue.");

            if (_addressUpdated.TryGetValue(address, out var acquisitor))
            {
                acquisitor.value = value;
                acquisitor.Invoke(address, value);
            }
            else
            {
                _addressUpdated.Add(address, new AcquisitionForAddress
                {
                    value = value
                });
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

        public float GetValue(int addressId)
        {
            if (_addressUpdated.TryGetValue(addressId, out var acquisitor))
            {
                return acquisitor.value;
            }

            return 0f;
        }
    }
}
