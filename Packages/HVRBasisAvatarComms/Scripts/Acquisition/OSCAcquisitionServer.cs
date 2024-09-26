using HVR.Basis.Comms.OSC;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/Internal/OSC Acquisition Server")]
    internal class OSCAcquisitionServer : MonoBehaviour
    {
        private HVROsc _client;
        private const int OurFakeServerPort = 9000;
        private const int ExternalProgramReceiverPort = 9001;
        
        public event AddressUpdated OnAddressUpdated;
        public delegate void AddressUpdated(string address, float value);

        private void OnEnable()
        {
            _client = new HVROsc(OurFakeServerPort);
            _client.Start();
            _client.SetReceiverOscPort(ExternalProgramReceiverPort);
        }

        private void Update()
        {
            var messages = _client.PullMessages();
            foreach (var message in messages)
            {
                if (message.arguments.Length > 0)
                {
                    var arg = message.arguments[0];
                    if (arg is float floatValue)
                    {
                        OnAddressUpdated?.Invoke(message.path, floatValue);
                    }
                }
            }
        }

        private void OnDisable()
        {
            _client.Finish();
            _client = null;
        }

        public void SendWakeUpMessage(string wakeUp)
        {
            _client.SendOsc("/avatar/change", wakeUp);
        }
    }
}