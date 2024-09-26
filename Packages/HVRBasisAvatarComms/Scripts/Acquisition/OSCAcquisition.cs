using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/OSC Acquisition")]
    public class OSCAcquisition : MonoBehaviour
    {
        private const string FakeWakeUpMessage = "avtr_00000000-bc83-4caa-b77f-000000000000";
        
        [SerializeField] private AcquisitionService acquisitionService;
        
        private OSCAcquisitionServer _acquisitionServer;

        private void Awake()
        {
            _acquisitionServer = FindFirstObjectByType<OSCAcquisitionServer>();
            if (_acquisitionServer == null)
            {
                var go = new GameObject("OSCAcquisitionServer");
                _acquisitionServer = go.AddComponent<OSCAcquisitionServer>();
            }

            _acquisitionServer.OnAddressUpdated -= OnAddressUpdated;
            _acquisitionServer.OnAddressUpdated += OnAddressUpdated;
        }

        private void OnEnable()
        {
            _acquisitionServer.SendWakeUpMessage(FakeWakeUpMessage);
        }

        private void OnDestroy()
        {
            _acquisitionServer.OnAddressUpdated -= OnAddressUpdated;
        }

        private void OnAddressUpdated(string address, float value)
        {
            if (!isActiveAndEnabled) return;
            
            acquisitionService.Submit(address, value);
        }
    }
}