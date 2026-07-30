using Basis;
using Basis.Network.Core;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.Receivers;
using System;
using Unity.Mathematics;
using UnityEngine;
public class BasisSeatSync : BasisNetworkBehaviour
{
    private BasisNetworkReceiver _currentRemoteRec;
    private ushort _currentUserId = ushort.MaxValue;
    private Vector3 _lastPinPosition;
    private Quaternion _lastPinRotation = Quaternion.identity;
    private bool _hasLastPinPosition;
    private uint _seatGen;
    private bool _applyingNetworkState;

    [Header("Seat")]
    public BasisSeat Seat;

    [Header("Runtime")]
    [System.NonSerialized] public PlayerID LinkedPlayer = new PlayerID();
    public class PlayerID
    {
        public bool hasPlayerId = false;
        public ushort ThePlayerID;
    }
    public void Awake()
    {
        LinkedPlayer = new PlayerID();
        LinkedPlayer.hasPlayerId = false;
        LinkedPlayer.ThePlayerID = 0;
        BasisLocalPlayer.AfterRemoteSyncInterpolated.AddAction(20, ProvideRemotePlayerTarget);
    }
    public Action<IBasisPlayer> OnNetworkPlayerEnterSeat;
    public Action<IBasisPlayer> OnNetworkPlayerExitSeat;
    /// <summary>Returns true if the local player is currently the recorded occupant.</summary>
    public bool IsLocallyEntered()
    {
        return LinkedPlayer.hasPlayerId && GetLocalPlayerIdSafe(out ushort id) && LinkedPlayer.ThePlayerID == id;
    }

    /// <summary>Returns whether a user occupies the seat and outputs their ID (0 if none).</summary>
    public bool HasUser(out ushort id)
    {
        if (LinkedPlayer.hasPlayerId)
        {
            id = LinkedPlayer.ThePlayerID;
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
            Seat.OnInteractStartEvent.AddListener(OnInteractStartEvent);
            Seat.OnInteractEndEvent.AddListener(OnInteractEndEvent);
            Seat.OnOccupantYawChanged += OnOccupantYawChanged;
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

    public const float YawSendIntervalSeconds = 0.05f;

    private bool _yawDirty;
    private float _lastYawSendTime = float.NegativeInfinity;

    private void OnOccupantYawChanged(float yaw)
    {
        if (IsLocallyEntered())
        {
            _yawDirty = true;
        }
    }

    public bool FlushOccupantYaw(float now)
    {
        if (_yawDirty == false || Seat == null || IsLocallyEntered() == false)
        {
            return false;
        }
        if (now - _lastYawSendTime < YawSendIntervalSeconds)
        {
            return false;
        }
        _yawDirty = false;
        _lastYawSendTime = now;
        SendCustomNetworkEvent(CreateSeatPacket(true), DeliveryMethod.ReliableOrdered);
        return true;
    }

    public void ProvideRemotePlayerTarget()
    {
        FlushOccupantYaw(Time.unscaledTime);

        // If there is no seat, just clear any previous override.
        if (Seat == null)
        {
            ClearCurrentRemote();
            return;
        }

        // If there is no user in this seat, clear override.
        if (!HasUser(out ushort storedId))
        {
            ClearCurrentRemote();
            return;
        }

        // If we can't get local id, or something is wrong, clear override.
        if (!GetLocalPlayerIdSafe(out ushort localId))
        {
            ClearCurrentRemote();
            return;
        }

        // If it's us sitting in that seat, we don't want to override any remote.
        if (localId == storedId)
        {
            ClearCurrentRemote();
            return;
        }

        // Try to get the remote player by id.
        if (!BasisNetworkPlayers.RemotePlayerReceivers.TryGetValue(storedId, out BasisNetworkReceiver rec))
        {
            // ID no longer exists in dictionary (disconnected / removed).
            ClearCurrentRemote();
            return;
        }

        // If the player in the seat changed, clear the old one and store the new one.
        if (_currentUserId != storedId || _currentRemoteRec != rec)
        {
            ClearCurrentRemote(); // turn off override on the previous receiver
            _currentUserId = storedId;
            _currentRemoteRec = rec;
        }

        // Now drive the current remote receiver.
        Seat.CalculateSeatPositionRotation(rec.RemotePlayer, out Quaternion seatQuat, out Vector3 hips);
        rec.OverriddenDestinationOfRoot(true);
        rec.ProvidedDestinationOfRoot(hips, seatQuat);

        if (_hasLastPinPosition)
        {
            Vector3 pinDelta = hips - _lastPinPosition;
            bool rotated = Mathf.Abs(Quaternion.Dot(seatQuat, _lastPinRotation)) < 0.9999999f;
            if (pinDelta.sqrMagnitude > 0f || rotated)
            {
                Quaternion rotationDelta = seatQuat * Quaternion.Inverse(_lastPinRotation);
                TeleportOccupantJiggleRigs(rec, rotationDelta, _lastPinPosition, pinDelta);
            }
        }
        _lastPinPosition = hips;
        _lastPinRotation = seatQuat;
        _hasLastPinPosition = true;
    }

    private static void TeleportOccupantJiggleRigs(BasisNetworkReceiver rec, Quaternion rotationDelta, Vector3 pivot, Vector3 positionDelta)
    {
        var jiggleRigs = rec.RemotePlayer?.RemoteAvatarDriver?.JiggleRigs;
        if (jiggleRigs == null)
        {
            return;
        }
        for (int Index = 0; Index < jiggleRigs.Length; Index++)
        {
            var rig = jiggleRigs[Index];
            if (rig != null)
            {
                rig.Teleport(rotationDelta, pivot, positionDelta);
            }
        }
    }

    private void ClearCurrentRemote()
    {
        if (_currentRemoteRec != null)
        {
            if (_hasLastPinPosition)
            {
                _currentRemoteRec.GetLatestNetworkPose(out float3 networkHips, out _, out _);
                Vector3 exitDelta = (Vector3)networkHips - _lastPinPosition;
                if (exitDelta.sqrMagnitude > 0f)
                {
                    TeleportOccupantJiggleRigs(_currentRemoteRec, Quaternion.identity, Vector3.zero, exitDelta);
                }
            }
            // Assuming false turns off the override.
            _currentRemoteRec.OverriddenDestinationOfRoot(false);
            if (_currentRemoteRec.Player != null)
            {
                OnNetworkPlayerExitSeat?.Invoke(_currentRemoteRec.Player);
            }
            _currentRemoteRec = null;
        }

        _currentUserId = ushort.MaxValue;
        _hasLastPinPosition = false;
    }

    public override void OnDestroy()
    {
        // Force-eject local player if they're still in this seat
        if (IsLocallyEntered())
        {
            Stand();
        }
        // Clear any remote player position overrides
        ClearCurrentRemote();
        BasisLocalPlayer.AfterRemoteSyncInterpolated.RemoveAction(20, ProvideRemotePlayerTarget);
        if (Seat != null)
        {
            Seat.OnInteractStartEvent.RemoveListener(OnInteractStartEvent);
            Seat.OnInteractEndEvent.RemoveListener(OnInteractEndEvent);
            Seat.OnOccupantYawChanged -= OnOccupantYawChanged;
        }
        base.OnDestroy();
    }

    /// <summary>
    /// Local interaction start: attempt to enter seat if free or already ours.
    /// </summary>
    private void OnInteractStartEvent(BasisInput input)
    {
        if (_applyingNetworkState)
        {
            return;
        }
        if (Seat.LocallyInSeat)//guards against the oninteract from exit click
        {
            if (!GetLocalPlayerIdSafe(out ushort id))
            {
                BasisDebug.LogError("Missing LocalPlayer", BasisDebug.LogTag.Networking);
                return;
            }

            if (HasUser(out ushort current) && current != id)
            {
                _applyingNetworkState = true;
                try
                {
                    StandIfPhysicallySeated();
                }
                finally
                {
                    _applyingNetworkState = false;
                }
                return;
            }

            if (IsLocallyEntered())
            {
                _seatGen++;
                SendCustomNetworkEvent(CreateSeatPacket(true), DeliveryMethod.ReliableOrdered);
                return;
            }

            SetSeatState(true, id);
        }
    }

    /// <summary>
    /// Local interaction end: only the current local occupant may exit.
    /// </summary>
    private void OnInteractEndEvent(BasisInput input)
    {
        if (_applyingNetworkState)
        {
            return;
        }
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
        if (LinkedPlayer.hasPlayerId == state && LinkedPlayer.ThePlayerID == id)
        {
            return;
        }

        _seatGen++;
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
        if (!DeserializeSeatPacket(buffer, out bool occupied, out uint gen, out bool hasGen, out float yaw, out bool hasYaw))
        {
            return;
        }
        if (GetLocalPlayerIdSafe(out ushort localId) && occupantId == localId)
        {
            return;
        }
        if (!hasGen)
        {
            gen = _seatGen + 1;
        }

        if (occupied)
        {
            bool accept;
            if (gen > _seatGen)
            {
                accept = true;
            }
            else if (gen == _seatGen)
            {
                if (!HasUser(out ushort current) || current == occupantId)
                {
                    accept = true;
                }
                else
                {
                    accept = occupantId < current;
                }
            }
            else
            {
                accept = false;
            }
            if (!accept)
            {
                return;
            }

            _applyingNetworkState = true;
            try
            {
                _seatGen = gen;
                StandIfPhysicallySeated();
                SetSeatStateLocal(true, occupantId);
                if (hasYaw && Seat != null)
                {
                    Seat.ApplyNetworkedOccupantYaw(yaw);
                }
            }
            finally
            {
                _applyingNetworkState = false;
            }
        }
        else
        {
            if (gen <= _seatGen)
            {
                return;
            }
            if (!HasUser(out ushort current) || current != occupantId)
            {
                return;
            }

            _applyingNetworkState = true;
            try
            {
                _seatGen = gen;
                StandIfPhysicallySeated();
                SetSeatStateLocal(false, occupantId);
            }
            finally
            {
                _applyingNetworkState = false;
            }
        }
    }
    private void Stand()
    {
        BasisLocalPlayer.Instance?.LocalSeatDriver?.Stand();
    }

    private void StandIfPhysicallySeated()
    {
        if (Seat != null && Seat.LocallyInSeat)
        {
            BasisLocalPlayer.Instance?.LocalSeatDriver?.Stand();
        }
    }

    /// <summary>
    /// </summary>
    public byte[] CreateSeatPacket(bool isInSeat)
    {
        byte[] data = new byte[7];
        data[0] = isInSeat ? (byte)1 : (byte)0;
        uint gen = _seatGen;
        data[1] = (byte)gen;
        data[2] = (byte)(gen >> 8);
        data[3] = (byte)(gen >> 16);
        data[4] = (byte)(gen >> 24);
        short yaw = QuantizeYaw(Seat != null && isInSeat ? Seat.OccupantYawDegrees : 0f);
        data[5] = (byte)yaw;
        data[6] = (byte)(yaw >> 8);
        return data;
    }

    private const float YawQuantizeScale = 32767f / 180f;

    public static short QuantizeYaw(float degrees)
    {
        if (float.IsNaN(degrees) || float.IsInfinity(degrees))
        {
            return 0;
        }
        // Half-open wrap: DeltaAngle maps -180 to +180, which reads back as a 360 degree turn.
        float wrapped = BasisSeatFit.WrapDegrees(degrees);
        if (wrapped >= 180f)
        {
            wrapped -= 360f;
        }
        return (short)Mathf.Clamp(Mathf.RoundToInt(wrapped * YawQuantizeScale), short.MinValue, short.MaxValue);
    }

    public static float DequantizeYaw(short quantized) => quantized / YawQuantizeScale;

    /// <summary>
    /// </summary>
    private void SetSeatStateLocal(bool inSeat, ushort playerId)
    {
        bool changed = LinkedPlayer.hasPlayerId != inSeat || LinkedPlayer.ThePlayerID != playerId;

        LinkedPlayer.hasPlayerId = inSeat;
        LinkedPlayer.ThePlayerID = playerId;

        if (Seat != null)
        {
            Seat.SetSeatOccupied(inSeat);
            if (inSeat == false)
            {
                Seat.ResetOccupantYaw();
            }

            bool resolved = BasisNetworkPlayers.GetPlayerById(playerId, out BasisNetworkPlayer Player);
            Seat.SetOccupantRecord(inSeat && resolved ? Player.Player : null);

            if (changed && resolved)
            {
                if (inSeat)
                {
                    OnNetworkPlayerEnterSeat?.Invoke(Player.Player);
                }
                else
                {
                    OnNetworkPlayerExitSeat?.Invoke(Player.Player);
                }
            }
        }
        else
        {
            BasisDebug.LogError($"[{nameof(BasisSeatSync)}] Tried to set seat state, but Seat is null on {name}.", BasisDebug.LogTag.Networking);
        }
    }

    /// <summary>
    /// </summary>
    private static bool DeserializeSeatPacket(byte[] buffer, out bool occupied, out uint gen, out bool hasGen, out float yaw, out bool hasYaw)
    {
        occupied = false;
        gen = 0;
        hasGen = false;
        yaw = 0f;
        hasYaw = false;
        if (buffer == null || buffer.Length < 1)
        {
            return false;
        }
        occupied = buffer[0] != 0;
        if (buffer.Length >= 5)
        {
            gen = (uint)(buffer[1] | (buffer[2] << 8) | (buffer[3] << 16) | (buffer[4] << 24));
            hasGen = true;
        }
        if (buffer.Length >= 7)
        {
            yaw = DequantizeYaw((short)(buffer[5] | (buffer[6] << 8)));
            hasYaw = true;
        }
        return true;
    }
}
