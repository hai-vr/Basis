using Basis.Network.Core;

namespace Basis.Scripts.Networking
{
    public static class BasisNetworkHandlePubSub
    {
        public delegate void PubSubMessageDelegate(byte[] buffer, DeliveryMethod deliveryMethod);
        public static event PubSubMessageDelegate OnPubSubMessageReceived;

        public static void HandleMessage(NetPacketReader reader, DeliveryMethod deliveryMethod)
        {
            if (OnPubSubMessageReceived == null) return;
            
            try
            {
                byte[] data = reader.GetRemainingBytes();
                OnPubSubMessageReceived?.Invoke(data, deliveryMethod);
            }
            finally
            {
                reader.Recycle();
            }
        }
    }
}
