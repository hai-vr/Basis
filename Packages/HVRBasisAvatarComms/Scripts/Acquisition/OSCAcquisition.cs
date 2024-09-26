using Basis.Scripts.BasisSdk;
using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/OSC Acquisition")]
    public class OSCAcquisition : MonoBehaviour
    {
        private const string FakeWakeUpMessage = "avtr_00000000-bc83-4caa-b77f-000000000000";
        
        [SerializeField] private BasisAvatar avatar;
        [SerializeField] private AcquisitionService acquisitionService;
        
        private OSCAcquisitionServer _acquisitionServer;

        private void Awake()
        {
            avatar.OnAvatarNetworkReady -= OnAvatarNetworkReady;
            avatar.OnAvatarNetworkReady += OnAvatarNetworkReady;
        }

        private void OnAvatarNetworkReady()
        {
            if (!avatar.IsOwnedLocally) return;
            
            _acquisitionServer = FindFirstObjectByType<OSCAcquisitionServer>();
            if (_acquisitionServer == null)
            {
                var go = new GameObject("OSCAcquisitionServer");
                _acquisitionServer = go.AddComponent<OSCAcquisitionServer>();
            }
            _acquisitionServer.SendWakeUpMessage(FakeWakeUpMessage);

            _acquisitionServer.OnAddressUpdated -= OnAddressUpdated;
            _acquisitionServer.OnAddressUpdated += OnAddressUpdated;
        }

        private void OnDestroy()
        {
            _acquisitionServer.OnAddressUpdated -= OnAddressUpdated;
            avatar.OnAvatarNetworkReady -= OnAvatarNetworkReady;
        }

        private void OnAddressUpdated(string address, float value)
        {
            if (!isActiveAndEnabled) return;
            
            acquisitionService.Submit(address, value);
        }
    }
}