using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/OSC Acquisition")]
    public class OSCAcquisition : MonoBehaviour
    {
        private const string FakeWakeUpMessage = "avtr_00000000-89b1-4313-aa2d-000000000000";

        [HideInInspector] [SerializeField] private AcquisitionService acquisitionService;

        private OSCAcquisitionServer _acquisitionServer;
        private bool _alreadyInitialized;
        
        private object _unhook;

        private void Awake()
        {
            if (acquisitionService == null) acquisitionService = AcquisitionService.SceneInstance;

            _unhook = HVRCommsUtil.HookAvatarReady(this, OnAvatarReady);
        }

        internal void OnAvatarReady(bool isWearer)
        {
            if (!isWearer) return;

            if (_alreadyInitialized) return;
            _alreadyInitialized = true;

            _acquisitionServer = OSCAcquisitionServer.SceneInstance;
            _acquisitionServer.SendWakeUpMessage(FakeWakeUpMessage);

            _acquisitionServer.OnAddressUpdated -= OnAddressUpdated;
            _acquisitionServer.OnAddressUpdated += OnAddressUpdated;
        }

        private void OnDestroy()
        {
            if (_acquisitionServer != null)
            {
                _acquisitionServer.OnAddressUpdated -= OnAddressUpdated;
            }
            
            if (_unhook != null) HVRCommsUtil.UnhookAvatarReady(this, _unhook);
        }

        private void OnAddressUpdated(string address, float value)
        {
            if (!isActiveAndEnabled) return;

            acquisitionService.Submit(HVRAddress.AddressToId(address), value);
        }
    }
}
