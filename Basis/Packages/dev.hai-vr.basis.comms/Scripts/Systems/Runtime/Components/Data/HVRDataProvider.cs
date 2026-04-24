using System;
using System.Collections.Generic;

namespace HVR.Basis.Comms
{
    public class HVRDataProvider
    {
        public delegate void AddressUpdated(int address, float value);

        internal readonly Dictionary<int, ListenerState> _addressIdToListenerState = new();

        public void Submit(int address, float value)
        {
            if (address == 0) throw new IndexOutOfRangeException("Address cannot be zero, this may indicate an initialization issue.");

            if (_addressIdToListenerState.TryGetValue(address, out var listenerState))
            {
                listenerState.value = value;
                listenerState.Invoke(address, value);
            }
        }

        public void SubmitOrDefineDefaultValue(int address, float value)
        {
            if (address == 0) throw new IndexOutOfRangeException("Address cannot be zero, this may indicate an initialization issue.");

            if (_addressIdToListenerState.TryGetValue(address, out var listenerState))
            {
                listenerState.value = value;
                listenerState.Invoke(address, value);
            }
            else
            {
                _addressIdToListenerState.Add(address, new ListenerState
                {
                    value = value
                });
            }
        }

        public void RegisterAddresses(int[] addressBase, AddressUpdated onAddressUpdated)
        {
            foreach (var address in addressBase)
            {
                _addressIdToListenerState.TryAdd(address, new ListenerState());

                var listenerState = _addressIdToListenerState[address];
                listenerState.OnAddressUpdated -= onAddressUpdated;
                listenerState.OnAddressUpdated += onAddressUpdated;
            }
        }

        public void UnregisterAddresses(int[] addressBase, AddressUpdated onAddressUpdated)
        {
            foreach (var address in addressBase)
            {
                if (_addressIdToListenerState.TryGetValue(address, out var listenerState))
                {
                    listenerState.OnAddressUpdated -= onAddressUpdated;
                }
            }
        }

        public float GetValue(int addressId)
        {
            if (_addressIdToListenerState.TryGetValue(addressId, out var listenerState))
            {
                return listenerState.value;
            }

            return 0f;
        }

        internal class ListenerState
        {
            internal event AddressUpdated OnAddressUpdated;
            internal float value;

            public void Invoke(int address, float value) => OnAddressUpdated?.Invoke(address, value);
            internal int GetListenersCount() => OnAddressUpdated?.GetInvocationList().Length ?? 0;
        }
    }
}
