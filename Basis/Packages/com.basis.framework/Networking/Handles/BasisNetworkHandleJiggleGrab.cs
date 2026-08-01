using Basis.Network.Core;
using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.Networking;
using UnityEngine;

/// <summary>
/// Sends and receives jiggle grab lifecycle events on
/// <see cref="BasisNetworkCommons.EventsChannel"/> using
/// <see cref="BasisNetworkCommons.EventType_JiggleGrab"/>.
///
/// The grab itself never streams: every client derives the pull from the grabber's
/// already-synced hand pose. Start doubles as a periodic keepalive/late-join heal,
/// Stop ends a grab, and Deny is sent by the grab TARGET to kill a grab everywhere
/// (the both-ways block enforcement).
/// </summary>
public static class BasisNetworkHandleJiggleGrab
{
    public const byte OpStart = BasisNetworkCommons.JiggleGrabOp_Start;
    public const byte OpStop = BasisNetworkCommons.JiggleGrabOp_Stop;
    public const byte OpDeny = BasisNetworkCommons.JiggleGrabOp_Deny;

    // Only a garbage/NaN guard, deliberately loose: a pointed grab on a scaled-up avatar legitimately
    // starts metres from the bone. What actually bounds the pull is the per-rig max grab stretch,
    // enforced in the simulation itself.
    public const float MaxGrabOffsetMeters = 8f;

    public static void SendGrabStart(ushort targetId, byte rigIndex, ushort pointIndex, byte hand, uint boneNameHash, Vector3 grabOffset)
    {
        if (BasisNetworkConnection.LocalPlayerIsConnected == false) return;
        if (BasisNetworkConnection.LocalPlayerPeer == null) return;

        NetDataWriter writer = new NetDataWriter();
        writer.Put(BasisNetworkCommons.EventType_JiggleGrab);
        writer.Put(OpStart);
        writer.Put(targetId);
        writer.Put(rigIndex);
        writer.Put(pointIndex);
        writer.Put(hand);
        writer.Put(boneNameHash);
        writer.Put(Mathf.FloatToHalf(grabOffset.x));
        writer.Put(Mathf.FloatToHalf(grabOffset.y));
        writer.Put(Mathf.FloatToHalf(grabOffset.z));
        BasisNetworkConnection.LocalPlayerPeer.Send(writer, BasisNetworkCommons.EventsChannel, DeliveryMethod.ReliableOrdered);
    }

    public static void SendGrabStop(ushort targetId, byte rigIndex, ushort pointIndex)
    {
        if (BasisNetworkConnection.LocalPlayerIsConnected == false) return;
        if (BasisNetworkConnection.LocalPlayerPeer == null) return;

        NetDataWriter writer = new NetDataWriter();
        writer.Put(BasisNetworkCommons.EventType_JiggleGrab);
        writer.Put(OpStop);
        writer.Put(targetId);
        writer.Put(rigIndex);
        writer.Put(pointIndex);
        BasisNetworkConnection.LocalPlayerPeer.Send(writer, BasisNetworkCommons.EventsChannel, DeliveryMethod.ReliableOrdered);
    }

    public static void SendGrabDeny(ushort grabberId)
    {
        if (BasisNetworkConnection.LocalPlayerIsConnected == false) return;
        if (BasisNetworkConnection.LocalPlayerPeer == null) return;

        NetDataWriter writer = new NetDataWriter();
        writer.Put(BasisNetworkCommons.EventType_JiggleGrab);
        writer.Put(OpDeny);
        writer.Put(grabberId);
        BasisNetworkConnection.LocalPlayerPeer.Send(writer, BasisNetworkCommons.EventsChannel, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// Main-thread apply for a server-stamped jiggle grab event. senderId is authenticated by
    /// the server; for Deny the payload's ushort names the denied grabber and rig/point/hand
    /// are unused.
    /// </summary>
    public static void OnRemoteJiggleGrabReceived(byte op, ushort senderId, ushort payloadId, byte rigIndex, ushort pointIndex, byte hand, uint boneNameHash, Vector3 grabOffset)
    {
        if (op == OpStart)
        {
            if (!float.IsFinite(grabOffset.x) || !float.IsFinite(grabOffset.y) || !float.IsFinite(grabOffset.z))
            {
                return;
            }
            if (grabOffset.sqrMagnitude > MaxGrabOffsetMeters * MaxGrabOffsetMeters)
            {
                return;
            }
        }
        BasisJiggleGrabDriver.OnRemoteGrabEvent(op, senderId, payloadId, rigIndex, pointIndex, hand, boneNameHash, grabOffset);
    }
}
