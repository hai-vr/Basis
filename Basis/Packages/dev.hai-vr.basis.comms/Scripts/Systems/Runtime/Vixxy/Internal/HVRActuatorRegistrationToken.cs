using HVR.Basis.Comms;

namespace HVR.Vixxy
{
    public class HVRActuatorRegistrationToken
    {
        public string registeredAddress;
        public int registeredIddress;
        public AcquisitionService.AddressUpdated registeredCallback;
        public IHVRVixxyActuator registeredActuator;
    }
}
