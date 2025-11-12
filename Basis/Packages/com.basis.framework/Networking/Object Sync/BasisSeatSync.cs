using Basis;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Networking.NetworkedAvatar;
using LiteNetLib;
using UnityEngine;
public class BasisSeatSync : BasisNetworkBehaviour
{
    [Header("Seat")]
    public BasisSeat Seat;

    [Header("Runtime")]
    public PlayerID ActivePlayerID = new PlayerID();

    [System.Serializable]
    public class PlayerID
    {
        public bool hasPlayerId = false;
        public ushort ThePlayerID;
    }

    /// <summary>Returns true if the local player is currently the recorded occupant.</summary>
    public bool IsLocallyEntered()
    {
        return ActivePlayerID.hasPlayerId && GetLocalPlayerIdSafe(out ushort id) && ActivePlayerID.ThePlayerID == id;
    }

    /// <summary>Returns whether a user occupies the seat and outputs their ID (0 if none).</summary>
    public bool HasUser(out ushort id)
    {
        if (ActivePlayerID.hasPlayerId)
        {
            id = ActivePlayerID.ThePlayerID;
            return true;
        }
        id = 0;
        return false;
    }

    public static bool GetLocalPlayerIdSafe(out ushort ID)
    {
        var localPlayer = BasisNetworkPlayer.LocalPlayer;
        if (localPlayer != null)
        {
            ID = localPlayer.playerId;
            return true;
        }
        ID = 0;
        return false;
    }

    public override void OnNetworkReady()
    {
        if (Seat == null)
        {
            gameObject.TryGetComponent(out Seat);
        }
        if (Seat != null)
        {
            Seat.OnInteractStartEvent += OnInteractStartEvent;
            Seat.OnInteractEndEvent += OnInteractEndEvent;
        }
        else
        {
            BasisDebug.LogError($"[{nameof(BasisSeatSync)}] No BasisSeat found on {name}.", BasisDebug.LogTag.Networking);
        }
    }

    public override void OnPlayerJoined(BasisNetworkPlayer player)
    {
        if (IsLocallyEntered())
        {
            // Broadcast our current state only to the new player (includes our ID).
            byte[] data = CreateSeatPacket(true);
            SendCustomNetworkEvent(data, DeliveryMethod.ReliableOrdered, new ushort[] { player.playerId });
        }
    }

    public override void OnPlayerLeft(BasisNetworkPlayer player)
    {
        if (HasUser(out ushort id) && player.playerId == id)
        {
            // If the local player was the occupant, ensure we stand locally.
            if (GetLocalPlayerIdSafe(out ushort localId) && localId == id)
            {
                Stand();
            }
            SetSeatStateLocal(false, player.playerId);
        }
    }

    public override void OnDestroy()
    {
        if (Seat != null)
        {
            Seat.OnInteractStartEvent -= OnInteractStartEvent;
            Seat.OnInteractEndEvent -= OnInteractEndEvent;
        }
        base.OnDestroy();
    }

    /// <summary>
    /// Local interaction start: attempt to enter seat if free or already ours.
    /// </summary>
    private void OnInteractStartEvent(BasisInput input)
    {
        if (!GetLocalPlayerIdSafe(out ushort id))
        {
            BasisDebug.LogError("Missing LocalPlayer", BasisDebug.LogTag.Networking);
            return;
        }

        // If someone else is already in the seat, do nothing.
        if (HasUser(out ushort current) && current != id)
        {
            return;
        }

        // If we're already the occupant, do nothing.
        if (IsLocallyEntered())
        {
            return;
        }

        SetSeatState(true, id);
    }

    /// <summary>
    /// Local interaction end: only the current local occupant may exit.
    /// </summary>
    private void OnInteractEndEvent(BasisInput input)
    {
        if (GetLocalPlayerIdSafe(out ushort id))
        {
            if (IsLocallyEntered())
            {
                SetSeatState(false, id);
            }
            else
            {
                BasisDebug.LogWarning("we dont belong to this seat!", BasisDebug.LogTag.Networking);
                return;
            }
        }
        else
        {
            BasisDebug.LogError("Missing LocalPlayer", BasisDebug.LogTag.Networking);
            return;
        }
    }

    /// <summary>
    /// Set seat state locally and broadcast if it is actually changing.
    /// </summary>
    public void SetSeatState(bool state, ushort id)
    {
        // Idempotency: if nothing changes, don't spam the network.
        if (ActivePlayerID.hasPlayerId == state && ActivePlayerID.ThePlayerID == id)
        {
            return;
        }

        SetSeatStateLocal(state, id);

        // Broadcast new state including occupant ID.
        byte[] data = CreateSeatPacket(state);
        SendCustomNetworkEvent(data, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// Apply state received from the network. Packet occupantId is the senderid, this is to lock it into coming from the right person.
    /// </summary>
    public override void OnNetworkMessage(ushort occupantId, byte[] buffer, DeliveryMethod deliveryMethod)
    {
        if (!DeserializeSeatPacket(buffer, out bool occupied))
        {
            return;
        }

        // If remote says "occupied by X", and we think we're seated but X != local, stand locally.
        if (occupied)
        {
            if (IsLocallyEntered() && GetLocalPlayerIdSafe(out ushort localId) && occupantId != localId)
            {
                Stand();
            }
        }
        else
        {
            // Remote says "unoccupied"; if we think we're seated, stand.
            if (IsLocallyEntered())
            {
                Stand();
            }
        }

        // Apply without rebroadcasting.
        SetSeatStateLocal(occupied, occupantId);
    }
    private void Stand()
    {
        BasisLocalPlayer.Instance?.LocalSeatDriver?.Stand();
    }

    /// <summary>
    /// Create a seat packet: [occupied(byte)].
    /// </summary>
    public byte[] CreateSeatPacket(bool isInSeat)
    {
        byte[] data = new byte[1];
        data[0] = isInSeat ? (byte)1 : (byte)0;
        return data;
    }

    /// <summary>
    /// Set local state and update the Seat component (no networking here).
    /// </summary>
    private void SetSeatStateLocal(bool inSeat, ushort playerId)
    {
        ActivePlayerID.hasPlayerId = inSeat;
        ActivePlayerID.ThePlayerID = playerId;

        if (Seat != null)
        {
            Seat.SetSeatOccupied(inSeat);
        }
        else
        {
            BasisDebug.LogError($"[{nameof(BasisSeatSync)}] Tried to set seat state, but Seat is null on {name}.", BasisDebug.LogTag.Networking);
        }
    }

    /// <summary>
    /// Parse a seat packet. Returns false on malformed data.
    /// </summary>
    private static bool DeserializeSeatPacket(byte[] buffer, out bool occupied)
    {
        occupied = false;
        if (buffer == null || buffer.Length < 1)
        {
            return false;
        }
        occupied = buffer[0] != 0;
        return true;
    }
}
