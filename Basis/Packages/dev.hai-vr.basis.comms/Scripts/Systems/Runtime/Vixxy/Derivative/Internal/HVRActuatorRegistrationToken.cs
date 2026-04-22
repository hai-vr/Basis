using HVR.Basis.Comms;

namespace HVR.Vixxy.Runtime
{
    public class HVRActuatorRegistrationToken
    {
        public string registeredAddress;
        public int registeredIddress;
        public AcquisitionService.AddressUpdated registeredCallback;
        public IHVRVixxyActuator registeredActuator;
    }
}
