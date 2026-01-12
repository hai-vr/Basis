#if HVR_HAS_BASIS_SDK
using Basis.Scripts.Behaviour;

namespace HVR.Basis.Comms
{
    internal class Transmitter : IHVRTransmitter
    {
        private readonly BasisAvatarMonoBehaviour _behaviour;

        public Transmitter(BasisAvatarMonoBehaviour behaviour)
        {
            _behaviour = behaviour;
        }

        public void NetworkMessageSend(byte[] buffer = null, HVRTransmitterMethod deliveryMethod = HVRTransmitterMethod.Unreliable, ushort[] recipients = null)
        {
            _behaviour.NetworkMessageSend(buffer, IHVRTransmitter.FromHVRTransmitterMethod(deliveryMethod), recipients);
        }

        public void ServerReductionSystemMessageSend(byte[] buffer = null)
        {
            _behaviour.ServerReductionSystemMessageSend(buffer);
        }
    }
}
#endif