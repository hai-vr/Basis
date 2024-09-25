using Acquisition;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/OSC Acquisition")]
    public class OSCAcquisition : MonoBehaviour
    {
        [SerializeField] private OSCAcquisitionServer server;
        private bool _writtenThisFrame;
        public event AddressUpdated OnAddressUpdated;
        public delegate void AddressUpdated(string address, float value);
    }
}