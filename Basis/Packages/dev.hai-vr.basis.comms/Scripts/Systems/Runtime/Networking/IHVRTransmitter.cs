using System;
#if HVR_HAS_BASIS_SDK
using Basis.Network.Core;
#endif

namespace HVR.Basis.Comms
{
    public interface IHVRTransmitter
    {
        public void NetworkMessageSend(byte[] buffer = null, HVRTransmitterMethod deliveryMethod = HVRTransmitterMethod.Unreliable, ushort[] recipients = null);
        public void ServerReductionSystemMessageSend(byte[] buffer = null);

#if HVR_HAS_BASIS_SDK
        public static DeliveryMethod FromHVRTransmitterMethod(HVRTransmitterMethod method)
        {
            return method switch
            {
                HVRTransmitterMethod.ReliableSequenced => DeliveryMethod.ReliableSequenced,
                HVRTransmitterMethod.Unreliable => DeliveryMethod.Unreliable,
                HVRTransmitterMethod.Sequenced => DeliveryMethod.Sequenced,
                _ => throw new ArgumentOutOfRangeException(nameof(method), method, null)
            };
        }
#endif
    }

    public enum HVRTransmitterMethod
    {
        ReliableSequenced, Unreliable, Sequenced
    }
}
