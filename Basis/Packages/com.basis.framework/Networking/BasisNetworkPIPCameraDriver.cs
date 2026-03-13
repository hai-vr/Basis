using Basis.Network.Core;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking;
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
/// Fully static - driven by BasisLocalPlayer.AfterSimulateOnLate via Simulate().
/// Uses Unity Jobs (Burst-compiled IJobParallelFor) for smoothing remote camera positions.
/// </summary>
public static class BasisNetworkPIPCameraDriver
{
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
    private static readonly Dictionary<ushort, int> playerIdToIndex = new();
    private static readonly Dictionary<int, ushort> indexToPlayerId = new();
    private static readonly HashSet<ushort> activeRemotePIPs = new();

    // Job data
    private static NativeArray<float3> currentPositions;
    private static NativeArray<float3> targetPositions;
    private static NativeArray<byte> activeFlags;
    private static JobHandle smoothHandle;

    // Transform references for active PIP models
    private static readonly Dictionary<ushort, Transform> pipTransforms = new();

    private const int MaxPIPCameras = 256;
    private const float LerpSpeed = 12f;

    /// <summary>
    /// Priority for AfterSimulateOnLate. Runs after the hand-held camera interactable (202).
    /// </summary>
    private const int SimulatePriority = 203;

    private static bool initialized;
    private static int nextFreeIndex;
    private static readonly Queue<int> recycledIndices = new();

    /// <summary>
    /// Initialize native arrays and subscribe to the simulation loop.
    /// Called from BasisNetworkLifeCycle.Initalize().
    /// </summary>
    public static void Create()
    {
        if (initialized) return;

        currentPositions = new NativeArray<float3>(MaxPIPCameras, Allocator.Persistent);
        targetPositions = new NativeArray<float3>(MaxPIPCameras, Allocator.Persistent);
        activeFlags = new NativeArray<byte>(MaxPIPCameras, Allocator.Persistent);
        nextFreeIndex = 0;

        BasisLocalPlayer.AfterSimulateOnLate.AddAction(SimulatePriority, Simulate);
        initialized = true;
    }

    /// <summary>
    /// Dispose native arrays and unsubscribe from the simulation loop.
    /// Called from BasisNetworkLifeCycle shutdown path.
    /// </summary>
    public static void Shutdown()
    {
        if (!initialized) return;

        BasisLocalPlayer.AfterSimulateOnLate.RemoveAction(SimulatePriority, Simulate);

        smoothHandle.Complete();

        if (currentPositions.IsCreated) currentPositions.Dispose();
        if (targetPositions.IsCreated) targetPositions.Dispose();
        if (activeFlags.IsCreated) activeFlags.Dispose();

        playerIdToIndex.Clear();
        indexToPlayerId.Clear();
        activeRemotePIPs.Clear();
        pipTransforms.Clear();
        recycledIndices.Clear();
        nextFreeIndex = 0;

        initialized = false;
    }

    /// <summary>
    /// Per-frame simulation driven by BasisLocalPlayer.AfterSimulateOnLate.
    /// Completes the previous frame's job, applies positions, schedules next job.
    /// </summary>
    private static void Simulate()
    {
        if (!initialized || activeRemotePIPs.Count == 0) return;

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
    public static void OnRemotePIPState(CameraPIPStateMessage msg)
    {
        if (!initialized) return;

        smoothHandle.Complete();

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
    public static void OnRemotePIPPosition(CameraPIPPositionMessage msg)
    {
        if (!initialized) return;

        if (playerIdToIndex.TryGetValue(msg.PlayerID, out int index))
        {
            smoothHandle.Complete();
            targetPositions[index] = new float3(msg.PositionX, msg.PositionY, msg.PositionZ);
        }
    }

    /// <summary>
    /// Register a transform to be driven by the smoothed PIP position.
    /// </summary>
    public static void RegisterPIPTransform(ushort playerId, Transform t)
    {
        pipTransforms[playerId] = t;
    }

    /// <summary>
    /// Unregister a PIP transform (called when the model is destroyed).
    /// </summary>
    public static void UnregisterPIPTransform(ushort playerId)
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
    public static void HandlePlayerDisconnect(ushort playerId)
    {
        if (!initialized) return;

        if (activeRemotePIPs.Contains(playerId))
        {
            smoothHandle.Complete();
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

    private static int GetOrAllocateIndex(ushort playerId)
    {
        if (playerIdToIndex.TryGetValue(playerId, out int existing))
            return existing;

        int index = recycledIndices.Count > 0 ? recycledIndices.Dequeue() : nextFreeIndex++;
        playerIdToIndex[playerId] = index;
        indexToPlayerId[index] = playerId;
        return index;
    }

    private static void FreeIndex(ushort playerId, int index)
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
