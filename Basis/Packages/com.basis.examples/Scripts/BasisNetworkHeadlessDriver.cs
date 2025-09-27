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
    public AvatarLoadInformation[] GeneratedRandomizedAvatars;

    [Header("Server-assigned index counter (wraps by transforms.Length)")]
    public ushort CurrentIndex;

    public AvatarLoadInformation[] BaseData;
    public class AvatarLoadInformation
    {
        public byte LoadMode;
        public BasisLoadableBundle BasisLoadableBundle;
    }
    public void Awake()
    {
        if (transforms == null || transforms.Length == 0)
        {
            BasisDebug.LogWarning("[HeadlessDriver] No transforms configured; cannot generate avatars.");
            return;
        }

        if (BaseData == null || BaseData.Length == 0)
        {
            BasisDebug.LogWarning("[HeadlessDriver] No base avatar data provided; cannot generate avatars.");
            return;
        }

        GeneratedRandomizedAvatars = new AvatarLoadInformation[transforms.Length];

        System.Random rng = new System.Random();
        for (int Index = 0; Index < GeneratedRandomizedAvatars.Length; Index++)
        {
            // Pick a random base entry
            int randomIndex = rng.Next(BaseData.Length);
            AvatarLoadInformation baseInfo = BaseData[randomIndex];

            // Create a new randomized AvatarLoadInformation
            GeneratedRandomizedAvatars[Index] = new AvatarLoadInformation
            {
                LoadMode = baseInfo.LoadMode,
                BasisLoadableBundle = baseInfo.BasisLoadableBundle
            };
        }

        BasisDebug.Log($"[HeadlessDriver] Generated {GeneratedRandomizedAvatars.Length} randomized avatars.");
    }
    public override void OnPlayerJoined(BasisNetworkPlayer player)
    {
        #if !UNITY_SERVER
        if (transforms == null || transforms.Length == 0)
        {
            BasisDebug.LogWarning("[HeadlessDriver] No transforms configured; cannot assign indices.");
            return;
        }

        // Assign an index for this player (wrap to stay within bounds)
        ushort assigned = (ushort)(CurrentIndex % transforms.Length);
        CurrentIndex++;

        // Tell only this player their assigned index
        byte[] bytes = BitConverter.GetBytes(assigned);
        SendCustomNetworkEvent(bytes, DeliveryMethod.ReliableUnordered, new ushort[] { player.playerId });

        BasisDebug.Log($"[HeadlessDriver] Player {player.playerId} joined; assigned index {assigned}.");
        #endif
    }

    public override async void OnNetworkMessage(ushort playerID, byte[] buffer, DeliveryMethod deliveryMethod)
    {
        // Expecting a 2-byte payload with a ushort 'index'
        if (buffer == null || buffer.Length < sizeof(ushort))
        {
            BasisDebug.LogWarning($"[HeadlessDriver] Bad message from {playerID}: payload too small.");
            return;
        }

        ushort index = BitConverter.ToUInt16(buffer, 0);

        if (transforms == null || transforms.Length == 0)
        {
            BasisDebug.LogWarning("[HeadlessDriver] No transforms configured; cannot teleport.");
            return;
        }

        if (index >= transforms.Length)
        {
            BasisDebug.LogWarning($"[HeadlessDriver] Player {playerID} requested out-of-range index {index}.");
            return;
        }

        Transform target = transforms[index];
        var data = GeneratedRandomizedAvatars[index];
        if (target == null)
        {
            BasisDebug.LogWarning($"[HeadlessDriver] Transform at index {index} is null.");
            return;
        }
        target.GetPositionAndRotation(out Vector3 Position, out Quaternion Rotation);

        BasisLocalPlayer.Instance.Teleport(Position, Rotation);

        await BasisLocalPlayer.Instance.CreateAvatar(data.LoadMode, data.BasisLoadableBundle);

        BasisDebug.Log($"[HeadlessDriver] Teleported player {playerID} to transform[{index}] at {Position}.");
    }
}
