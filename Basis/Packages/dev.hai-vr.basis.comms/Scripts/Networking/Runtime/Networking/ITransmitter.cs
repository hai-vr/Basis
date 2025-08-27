using LiteNetLib;

namespace HVR.Basis.Comms
{
    public interface ITransmitter
    {
        public void NetworkMessageSend(byte[] buffer = null, DeliveryMethod deliveryMethod = DeliveryMethod.Unreliable, ushort[] recipients = null);
        public void ServerReductionSystemMessageSend(byte[] buffer = null);
    }
}
