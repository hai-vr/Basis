using Basis;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking.NetworkedAvatar;
using LiteNetLib;
using System;
using UnityEngine;

public class BasisNetworkHeadlessDriver : BasisNetworkBehaviour
{
    [Header("Spawn / Teleport Targets")]
    public Transform[] transforms;
    [HideInInspector]
    public BasisLoadableBundle[] GeneratedRandomizedAvatars;

    [Header("Server-assigned index counter (wraps by transforms.Length)")]
    public ushort CurrentIndex;

    public BasisLoadableBundle[] BaseData;
    public void Awake()
    {
        if (transforms == null || transforms.Length == 0)
        {
            BasisDebug.LogWarning("[HeadlessDriver] No transforms configured; cannot generate avatars.", BasisDebug.LogTag.Remote);
            return;
        }

        if (BaseData == null || BaseData.Length == 0)
        {
            BasisDebug.LogWarning("[HeadlessDriver] No base avatar data provided; cannot generate avatars.", BasisDebug.LogTag.Remote);
            return;
        }

        GeneratedRandomizedAvatars = new BasisLoadableBundle[transforms.Length];

        System.Random rng = new System.Random();
        for (int Index = 0; Index < GeneratedRandomizedAvatars.Length; Index++)
        {
            // Pick a random base entry
            int randomIndex = rng.Next(BaseData.Length);
            BasisLoadableBundle baseInfo = BaseData[randomIndex];

            // Create a new randomized AvatarLoadInformation
            GeneratedRandomizedAvatars[Index] = baseInfo;
        }

        BasisDebug.Log($"[HeadlessDriver] Generated {GeneratedRandomizedAvatars.Length} randomized avatars.", BasisDebug.LogTag.Remote);
    }
    public override void OnPlayerJoined(BasisNetworkPlayer player)
    {
#if !UNITY_SERVER
        if (transforms == null || transforms.Length == 0)
        {
            BasisDebug.LogWarning("[HeadlessDriver] No transforms configured; cannot assign indices.", BasisDebug.LogTag.Remote);
            return;
        }

        // Assign an index for this player (wrap to stay within bounds)
        ushort assigned = (ushort)(CurrentIndex % transforms.Length);
        CurrentIndex++;

        // Tell only this player their assigned index
        byte[] bytes = BitConverter.GetBytes(assigned);
        SendCustomNetworkEvent(bytes, DeliveryMethod.ReliableOrdered, new ushort[] { player.playerId });

        BasisDebug.Log($"[HeadlessDriver] Player {player.playerId} joined; assigned index {assigned}.", BasisDebug.LogTag.Remote);
#endif
    }

    public override async void OnNetworkMessage(ushort playerID, byte[] buffer, DeliveryMethod deliveryMethod)
    {
        // Expecting a 2-byte payload with a ushort 'index'
        if (buffer == null || buffer.Length < sizeof(ushort))
        {
            BasisDebug.LogWarning($"[HeadlessDriver] Bad message from {playerID}: payload too small.", BasisDebug.LogTag.Remote);
            return;
        }

        ushort index = BitConverter.ToUInt16(buffer, 0);

        if (transforms == null || transforms.Length == 0)
        {
            BasisDebug.LogWarning("[HeadlessDriver] No transforms configured; cannot teleport.", BasisDebug.LogTag.Remote);
            return;
        }

        if (index >= transforms.Length)
        {
            BasisDebug.LogWarning($"[HeadlessDriver] Player {playerID} requested out-of-range index {index}.", BasisDebug.LogTag.Remote);
            return;
        }

        Transform target = transforms[index];
        var data = GeneratedRandomizedAvatars[index];
        if (target == null)
        {
            BasisDebug.LogWarning($"[HeadlessDriver] Transform at index {index} is null.", BasisDebug.LogTag.Remote);
            return;
        }
        target.GetPositionAndRotation(out Vector3 Position, out Quaternion Rotation);

        BasisLocalPlayer.Instance.Teleport(Position, Rotation);

        await BasisLocalPlayer.Instance.CreateAvatar(0, data);

        BasisDebug.Log($"[HeadlessDriver] Teleported player {playerID} to transform[{index}] at {Position}.", BasisDebug.LogTag.Remote);
    }
}
