using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/Internal/Acquisition Service")]
    public class AcquisitionService : MonoBehaviour
    {
        public static AcquisitionService SceneInstance => HVRCommsUtil.GetOrCreateSceneInstance(ref _sceneInstance);
        private static AcquisitionService _sceneInstance;

        public readonly HVRDataProvider DataProvider = new();

        public void Submit(int address, float value) => DataProvider.Submit(address, value);
        public void SubmitOrDefineDefaultValue(int address, float value) => DataProvider.SubmitOrDefineDefaultValue(address, value);
        public void RegisterAddresses(int[] addressBase, HVRDataProvider.AddressUpdated onAddressUpdated) => DataProvider.RegisterAddresses(addressBase, onAddressUpdated);
        public void UnregisterAddresses(int[] addressBase, HVRDataProvider.AddressUpdated onAddressUpdated) => DataProvider.UnregisterAddresses(addressBase, onAddressUpdated);
        public float GetValue(int addressId) => DataProvider.GetValue(addressId);
    }

    internal class AcquisitionForAddress
    {
        internal event HVRDataProvider.AddressUpdated OnAddressUpdated;
        internal float value;

        public void Invoke(int address, float value) => OnAddressUpdated?.Invoke(address, value);
        internal int GetListenersCount() => OnAddressUpdated?.GetInvocationList().Length ?? 0;
    }
}
