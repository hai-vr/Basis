using Basis.Network.Core;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using static SerializableBasis;

/// <summary>
/// Client-side manager for networked PIP cameras.
/// Uses Unity Jobs (Burst-compiled) for smoothing remote camera positions.
/// </summary>
public class BasisNetworkPIPCameraManager : MonoBehaviour
{
    public static BasisNetworkPIPCameraManager Instance { get; private set; }

    /// <summary>
    /// Fired when a remote player's PIP camera is created.
    /// Subscribers should instantiate the 3D lens model.
    /// </summary>
    public static event Action<ushort, float3> OnRemotePIPCreated;

    /// <summary>
    /// Fired when a remote player's PIP camera is destroyed.
    /// Subscribers should remove the 3D lens model.
    /// </summary>
    public static event Action<ushort> OnRemotePIPDestroyed;

    // Mapping from playerID -> index in NativeArrays
    private readonly Dictionary<ushort, int> playerIdToIndex = new();
    private readonly Dictionary<int, ushort> indexToPlayerId = new();
    private readonly HashSet<ushort> activeRemotePIPs = new();

    // Job data
    private NativeArray<float3> currentPositions;
    private NativeArray<float3> targetPositions;
    private NativeArray<byte> activeFlags;
    private JobHandle smoothHandle;

    // Transform references for active PIP models
    private readonly Dictionary<ushort, Transform> pipTransforms = new();

    private const int MaxPIPCameras = 256;
    private const float LerpSpeed = 12f;

    private bool isInitialized;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        currentPositions = new NativeArray<float3>(MaxPIPCameras, Allocator.Persistent);
        targetPositions = new NativeArray<float3>(MaxPIPCameras, Allocator.Persistent);
        activeFlags = new NativeArray<byte>(MaxPIPCameras, Allocator.Persistent);
        isInitialized = true;
    }

    private void OnDestroy()
    {
        smoothHandle.Complete();

        if (currentPositions.IsCreated) currentPositions.Dispose();
        if (targetPositions.IsCreated) targetPositions.Dispose();
        if (activeFlags.IsCreated) activeFlags.Dispose();

        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!isInitialized) return;
        if (activeRemotePIPs.Count == 0) return;

        smoothHandle.Complete();

        // Apply smoothed positions to transforms
        foreach (var kvp in pipTransforms)
        {
            if (playerIdToIndex.TryGetValue(kvp.Key, out int index))
            {
                float3 pos = currentPositions[index];
                kvp.Value.position = new Vector3(pos.x, pos.y, pos.z);
            }
        }

        // Schedule next frame's smoothing job
        var job = new PIPPositionSmoothJob
        {
            DeltaTime = Time.deltaTime,
            LerpSpeed = LerpSpeed,
            CurrentPositions = currentPositions,
            TargetPositions = targetPositions,
            ActiveFlags = activeFlags,
        };
        smoothHandle = job.Schedule(MaxPIPCameras, 32);
    }

    /// <summary>
    /// Called from network receive when a remote PIP state message arrives.
    /// </summary>
    public void OnRemotePIPState(CameraPIPStateMessage msg)
    {
        if (msg.IsActive)
        {
            int index = GetOrAllocateIndex(msg.PlayerID);
            float3 pos = new float3(msg.PositionX, msg.PositionY, msg.PositionZ);
            currentPositions[index] = pos;
            targetPositions[index] = pos;
            activeFlags[index] = 1;
            activeRemotePIPs.Add(msg.PlayerID);

            OnRemotePIPCreated?.Invoke(msg.PlayerID, pos);
        }
        else
        {
            if (playerIdToIndex.TryGetValue(msg.PlayerID, out int index))
            {
                activeFlags[index] = 0;
                FreeIndex(msg.PlayerID, index);
            }
            activeRemotePIPs.Remove(msg.PlayerID);

            OnRemotePIPDestroyed?.Invoke(msg.PlayerID);
        }
    }

    /// <summary>
    /// Called from network receive when a remote PIP position update arrives.
    /// </summary>
    public void OnRemotePIPPosition(CameraPIPPositionMessage msg)
    {
        if (playerIdToIndex.TryGetValue(msg.PlayerID, out int index))
        {
            targetPositions[index] = new float3(msg.PositionX, msg.PositionY, msg.PositionZ);
        }
    }

    /// <summary>
    /// Register a transform to be driven by the smoothed PIP position.
    /// </summary>
    public void RegisterPIPTransform(ushort playerId, Transform t)
    {
        pipTransforms[playerId] = t;
    }

    /// <summary>
    /// Unregister a PIP transform (called when the model is destroyed).
    /// </summary>
    public void UnregisterPIPTransform(ushort playerId)
    {
        pipTransforms.Remove(playerId);
    }

    /// <summary>
    /// Send local PIP camera state to server.
    /// </summary>
    public static void SendPIPState(bool isActive, Vector3 position)
    {
        ClientCameraPIPStateMessage msg = new ClientCameraPIPStateMessage
        {
            IsActive = isActive,
            PositionX = position.x,
            PositionY = position.y,
            PositionZ = position.z,
        };

        NetDataWriter writer = new NetDataWriter();
        msg.Serialize(writer);
        BasisNetworkConnection.LocalPlayerPeer.Send(writer, BasisNetworkCommons.CameraPIPStateChannel, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// Send local PIP camera position to server.
    /// </summary>
    public static void SendPIPPosition(Vector3 position)
    {
        ClientCameraPIPPositionMessage msg = new ClientCameraPIPPositionMessage
        {
            PositionX = position.x,
            PositionY = position.y,
            PositionZ = position.z,
        };

        NetDataWriter writer = new NetDataWriter();
        msg.Serialize(writer);
        BasisNetworkConnection.LocalPlayerPeer.Send(writer, BasisNetworkCommons.CameraPIPPositionChannel, DeliveryMethod.Sequenced);
    }

    /// <summary>
    /// Clean up when a remote player disconnects.
    /// </summary>
    public void HandlePlayerDisconnect(ushort playerId)
    {
        if (activeRemotePIPs.Contains(playerId))
        {
            if (playerIdToIndex.TryGetValue(playerId, out int index))
            {
                activeFlags[index] = 0;
                FreeIndex(playerId, index);
            }
            activeRemotePIPs.Remove(playerId);
            pipTransforms.Remove(playerId);

            OnRemotePIPDestroyed?.Invoke(playerId);
        }
    }

    private int nextFreeIndex = 0;
    private readonly Queue<int> recycledIndices = new();

    private int GetOrAllocateIndex(ushort playerId)
    {
        if (playerIdToIndex.TryGetValue(playerId, out int existing))
            return existing;

        int index = recycledIndices.Count > 0 ? recycledIndices.Dequeue() : nextFreeIndex++;
        playerIdToIndex[playerId] = index;
        indexToPlayerId[index] = playerId;
        return index;
    }

    private void FreeIndex(ushort playerId, int index)
    {
        playerIdToIndex.Remove(playerId);
        indexToPlayerId.Remove(index);
        recycledIndices.Enqueue(index);
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    struct PIPPositionSmoothJob : IJobParallelFor
    {
        public float DeltaTime;
        public float LerpSpeed;

        public NativeArray<float3> CurrentPositions;
        [ReadOnly] public NativeArray<float3> TargetPositions;
        [ReadOnly] public NativeArray<byte> ActiveFlags;

        public void Execute(int index)
        {
            if (ActiveFlags[index] == 0)
                return;

            CurrentPositions[index] = math.lerp(
                CurrentPositions[index],
                TargetPositions[index],
                math.saturate(DeltaTime * LerpSpeed)
            );
        }
    }
}
