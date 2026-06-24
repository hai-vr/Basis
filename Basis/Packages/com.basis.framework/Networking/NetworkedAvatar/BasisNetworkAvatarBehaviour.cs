using Basis.Network.Core;
using Basis.Scripts.Behaviour;
using Basis.Scripts.Networking.NetworkedAvatar;

namespace Basis.Scripts.Networking.Behaviour
{
    public abstract class BasisNetworkAvatarBehaviour : BasisAvatarMonoBehaviour
    {
        //  [HideInInspector]
        public BasisNetworkPlayer NetworkedPlayer;

        public void OnNetworkAssign(byte messageIndex, BasisNetworkPlayer Player)
        {
            MessageIndex = messageIndex;
            NetworkedPlayer = Player;
            IsInitialized = true;
            OnNetworkReady(Player.IsLocal);
        }

        public void OnNetworkUnassign()
        {
            if (!IsInitialized)
            {
                return;
            }
            IsInitialized = false;
            bool wasLocallyOwned = NetworkedPlayer != null && NetworkedPlayer.Player != null && NetworkedPlayer.Player.IsLocal;
            OnNetworkTerminated(wasLocallyOwned);
        }

        public virtual void OnNetworkTerminated(bool WasLocallyOwned)
        {

        }

        public virtual void OnNetworkMessageReceived(ushort RemoteUser, byte[] buffer, DeliveryMethod DeliveryMethod)
        {
         //   BasisDebug.LogError("Data was Received but nothing interpreted it! OnNetworkMessageReceived", this.gameObject, BasisDebug.LogTag.Avatar);
        }

        public virtual void OnDirectNetworkMessageReceived(ushort RemoteUser, byte[] buffer, DeliveryMethod DeliveryMethod)
        {

        }

        /// <summary>
        /// this is used for sending Network Messages
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="DeliveryMethod"></param>
        /// <param name="Recipients">if null everyone but self, you can include yourself to make it loop back over the network</param>
        public void NetworkMessageSend(byte[] buffer = null, DeliveryMethod DeliveryMethod = DeliveryMethod.Unreliable, ushort[] Recipients = null)
        {
            if (IsInitialized)
            {
                NetworkedPlayer.OnAvatarNetworkMessageSend(MessageIndex, buffer, DeliveryMethod, Recipients);
            }
            else
            {
                BasisDebug.LogError("Network Is Not Ready!", this.gameObject, BasisDebug.LogTag.Avatar);
            }
        }

        /// <summary>
        /// this is used for sending Network Messages
        /// </summary>
        /// <param name="DeliveryMethod"></param>
        public void NetworkMessageSend(DeliveryMethod DeliveryMethod = DeliveryMethod.Unreliable)
        {
            if (IsInitialized)
            {
                NetworkedPlayer.OnAvatarNetworkMessageSend(MessageIndex, null, DeliveryMethod);
            }
            else
            {
                BasisDebug.LogError("Network Is Not Ready!", this.gameObject, BasisDebug.LogTag.Avatar);
            }
        }

        /// <summary>
        /// Sends a Network Message over direct peer-to-peer links, falling back to the server
        /// relay for recipients with no direct connection. Received via <see cref="OnDirectNetworkMessageReceived"/>.
        /// </summary>
        /// <param name="buffer"></param>
        /// <param name="DeliveryMethod"></param>
        /// <param name="Recipients">if null everyone but self</param>
        /// <param name="allowServerFallback">if false, only delivers to directly-connected recipients (best-effort, no server relay)</param>
        public void NetworkMessageSendDirect(byte[] buffer = null, DeliveryMethod DeliveryMethod = DeliveryMethod.Unreliable, ushort[] Recipients = null, bool allowServerFallback = true)
        {
            if (IsInitialized)
            {
                NetworkedPlayer.OnAvatarNetworkMessageSendDirect(MessageIndex, buffer, DeliveryMethod, Recipients, allowServerFallback);
            }
            else
            {
                BasisDebug.LogError("Network Is Not Ready!", this.gameObject, BasisDebug.LogTag.Avatar);
            }
        }

        public void ServerReductionSystemMessageSend(byte[] buffer = null)
        {
            if (IsInitialized)
            {
                NetworkedPlayer.OnAvatarServerReductionSystemMessageSend(MessageIndex, buffer);
            }
            else
            {
                BasisDebug.LogError("Network Is Not Ready!", this.gameObject, BasisDebug.LogTag.Avatar);
            }
        }
    }
}
