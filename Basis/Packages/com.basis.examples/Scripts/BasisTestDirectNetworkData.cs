using Basis;
using Basis.Network.Core;
using UnityEngine.InputSystem;

public class BasisTestDirectNetworkData : BasisNetworkBehaviour
{
    public DeliveryMethod Method = DeliveryMethod.Unreliable;
    public bool AllowServerFallback = true;
    private byte _counter;

    public void Update()
    {
        if (HasNetworkID && Keyboard.current != null && Keyboard.current[Key.G].wasPressedThisFrame)
        {
            _counter++;
            SendCustomNetworkEventDirect(new byte[] { _counter }, Method, null, AllowServerFallback);
            BasisDebug.Log($"[DirectData] Sent {_counter} (server fallback: {AllowServerFallback}).");
        }
    }

    public override void OnDirectNetworkMessage(ushort PlayerID, byte[] buffer, DeliveryMethod DeliveryMethod)
    {
        byte value = (buffer != null && buffer.Length > 0) ? buffer[0] : (byte)0;
        BasisDebug.Log($"[DirectData] Received {value} from player {PlayerID}.");
    }
}
