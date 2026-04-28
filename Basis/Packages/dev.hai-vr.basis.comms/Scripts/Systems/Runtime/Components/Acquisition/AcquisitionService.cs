using UnityEngine;

namespace HVR.Basis.Comms
{
    [AddComponentMenu("HVR.Basis/Comms/Internal/Acquisition Service")]
    public class AcquisitionService : MonoBehaviour
    {
        public static AcquisitionService SceneInstance => HVRCommsUtil.GetOrCreateSceneInstance(ref _sceneInstance);
        private static AcquisitionService _sceneInstance;

        public readonly HVRDataProvider DataProvider = new();

        // Note: All the logic of this service has been moved to HVRDataProvider.
        // This is because we want components inside an avatar or prop to subscribe to the HVRDataProvider assigned to
        // that specific avatar or prop, which will be different if we're the wearer of the avatar.

        public void Submit(int address, float value) => DataProvider.Submit(address, value);
        public void SubmitOrDefineDefaultValue(int address, float value) => DataProvider.SubmitOrDefineDefaultValue(address, value);
        public void RegisterAddresses(int[] addressBase, HVRDataProvider.AddressUpdated onAddressUpdated) => DataProvider.RegisterAddresses(addressBase, onAddressUpdated);
        public void UnregisterAddresses(int[] addressBase, HVRDataProvider.AddressUpdated onAddressUpdated) => DataProvider.UnregisterAddresses(addressBase, onAddressUpdated);
        public float GetValue(int addressId) => DataProvider.GetValue(addressId);
    }
}
