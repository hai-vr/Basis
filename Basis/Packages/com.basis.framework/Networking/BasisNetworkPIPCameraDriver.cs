using Basis.Network.Core;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.UI.NamePlate;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Jobs;
using UnityEngine.ResourceManagement.AsyncOperations;
using static SerializableBasis;

/// <summary>
/// Client-side manager for networked PIP cameras.
/// Fully static - driven by BasisLocalPlayer.AfterSimulateOnLate via Simulate().
/// Smoothing (IJobParallelFor) plus camera and owner-nameplate transform writes
/// (IJobParallelForTransform) all run Burst-compiled off the main thread. Each spawned
/// camera and nameplate is an independent scene root so the transform writes spread
/// across worker threads instead of serializing on a shared hierarchy.
/// </summary>
public static class BasisNetworkPIPCameraDriver
{
    /// <summary>
    /// Fired when a remote player's PIP camera is created.
    /// </summary>
    public static event Action<ushort, float3, quaternion> OnRemotePIPCreated;

    /// <summary>
    /// Fired when a remote player's PIP camera is destroyed.
    /// </summary>
    public static event Action<ushort> OnRemotePIPDestroyed;

    /// <summary>
    /// Fired when a remote player takes a photo. Passes (playerID, worldPosition).
    /// </summary>
    public static event Action<ushort, Vector3> OnRemoteShutterSoundReceived;


    // Addressable path for the remote PIP prefab
    private const string RemotePIPPrefabPath = "Packages/com.basis.sdk/Prefabs/UI/Camera Prefab/BasisCameraRemotePip.prefab";

    // Cached prefab loaded via Addressables
    private static AsyncOperationHandle<GameObject> prefabHandle;
    private static GameObject loadedPrefab;

    // Dense per-camera SoA. Index i is shared across all of these plus the two
    // TransformAccessArrays and denseToPlayerId; swap-back removal keeps them aligned.
    private static NativeList<float3> currentPositions;
    private static NativeList<float3> targetPositions;
    private static NativeList<quaternion> currentRotations;
    private static NativeList<quaternion> targetRotations;
    private static NativeList<float> nameplateHeights;
    private static TransformAccessArray cameraTransforms;
    private static TransformAccessArray namePlateTransforms;
    private static readonly List<ushort> denseToPlayerId = new();
    private static readonly Dictionary<ushort, int> playerIdToIndex = new();
    private static JobHandle pipHandle;

    // Spawned objects and owner-name tag state, keyed by playerID for managed cleanup/baking.
    private static readonly Dictionary<ushort, GameObject> pipInstances = new();
    private static readonly Dictionary<ushort, RemotePipNamePlate> pipNamePlates = new();
    private static readonly List<ushort> pendingNameBakes = new();

    private const float NamePlateScale = 0.015f;
    private const float NamePlateMargin = 0.04f;
    private const float NamePlateHalfHeight = 4.5f * NamePlateScale;

    private const int InitialCapacity = 16;
    private const float LerpSpeed = 12f;

    /// <summary>
    /// Priority for AfterSimulateOnLate. Runs after the hand-held camera interactable (202).
    /// </summary>
    private const int SimulatePriority = 203;

    private static bool initialized;

    /// <summary>
    /// Initialize native containers and subscribe to the simulation loop.
    /// Called from BasisNetworkLifeCycle.Initialize().
    /// </summary>
    public static void Create()
    {
        if (initialized) return;

        currentPositions = new NativeList<float3>(InitialCapacity, Allocator.Persistent);
        targetPositions = new NativeList<float3>(InitialCapacity, Allocator.Persistent);
        currentRotations = new NativeList<quaternion>(InitialCapacity, Allocator.Persistent);
        targetRotations = new NativeList<quaternion>(InitialCapacity, Allocator.Persistent);
        nameplateHeights = new NativeList<float>(InitialCapacity, Allocator.Persistent);
        cameraTransforms = new TransformAccessArray(InitialCapacity);
        namePlateTransforms = new TransformAccessArray(InitialCapacity);

        // Preload the remote PIP prefab
        prefabHandle = Addressables.LoadAssetAsync<GameObject>(RemotePIPPrefabPath);
        loadedPrefab = prefabHandle.WaitForCompletion();

        BasisLocalPlayer.AfterSimulateOnLate.AddAction(SimulatePriority, Simulate);
        initialized = true;
    }

    /// <summary>
    /// Dispose native containers and unsubscribe from the simulation loop.
    /// Called from BasisNetworkLifeCycle shutdown path.
    /// </summary>
    public static void Shutdown()
    {
        if (!initialized) return;

        BasisLocalPlayer.AfterSimulateOnLate.RemoveAction(SimulatePriority, Simulate);

        pipHandle.Complete();

        // Destroy owner-name tags (baked mesh + GameObject)
        foreach (var kvp in pipNamePlates)
        {
            RemotePipNamePlate plate = kvp.Value;
            if (plate == null) continue;
            if (plate.Filter != null && plate.Filter.sharedMesh != null)
            {
                UnityEngine.Object.Destroy(plate.Filter.sharedMesh);
            }
            if (plate.Root != null)
            {
                UnityEngine.Object.Destroy(plate.Root);
            }
        }
        pipNamePlates.Clear();

        // Destroy all spawned PIP instances
        foreach (var kvp in pipInstances)
        {
            if (kvp.Value != null)
            {
                UnityEngine.Object.Destroy(kvp.Value);
            }
        }
        pipInstances.Clear();

        // Release the loaded prefab
        if (prefabHandle.IsValid())
        {
            Addressables.Release(prefabHandle);
        }
        loadedPrefab = null;

        if (currentPositions.IsCreated) currentPositions.Dispose();
        if (targetPositions.IsCreated) targetPositions.Dispose();
        if (currentRotations.IsCreated) currentRotations.Dispose();
        if (targetRotations.IsCreated) targetRotations.Dispose();
        if (nameplateHeights.IsCreated) nameplateHeights.Dispose();
        if (cameraTransforms.isCreated) cameraTransforms.Dispose();
        if (namePlateTransforms.isCreated) namePlateTransforms.Dispose();

        playerIdToIndex.Clear();
        denseToPlayerId.Clear();
        pendingNameBakes.Clear();

        initialized = false;
    }

    /// <summary>
    /// Per-frame simulation driven by BasisLocalPlayer.AfterSimulateOnLate.
    /// Smooths toward the latest network targets and writes the camera + nameplate
    /// transforms via parallel jobs, completing them before the frame renders.
    /// </summary>
    private static void Simulate()
    {
        if (!initialized) return;

        pipHandle.Complete();

        int count = currentPositions.Length;
        if (count == 0) return;

        ProcessPendingNameBakes();

        Vector3 viewer = BasisLocalCameraDriver.Position;

        // Capture the array views once so we don't re-check-out the lists while jobs are pending.
        NativeArray<float3> curPos = currentPositions.AsArray();
        NativeArray<float3> tgtPos = targetPositions.AsArray();
        NativeArray<quaternion> curRot = currentRotations.AsArray();
        NativeArray<quaternion> tgtRot = targetRotations.AsArray();
        NativeArray<float> heights = nameplateHeights.AsArray();

        JobHandle smoothHandle = new PIPSmoothJob
        {
            DeltaTime = Time.deltaTime,
            LerpSpeed = LerpSpeed,
            CurrentPositions = curPos,
            TargetPositions = tgtPos,
            CurrentRotations = curRot,
            TargetRotations = tgtRot,
        }.Schedule(count, 32);

        // Camera and nameplate are independent scene roots, so these two transform
        // writes touch disjoint hierarchies and run fully in parallel after smoothing.
        JobHandle cameraHandle = new PIPCameraApplyJob
        {
            Positions = curPos,
            Rotations = curRot,
        }.Schedule(cameraTransforms, smoothHandle);

        JobHandle namePlateHandle = new PIPNamePlateApplyJob
        {
            ViewerPosition = new float3(viewer.x, viewer.y, viewer.z),
            CameraPositions = curPos,
            Heights = heights,
        }.Schedule(namePlateTransforms, smoothHandle);

        pipHandle = JobHandle.CombineDependencies(cameraHandle, namePlateHandle);
        pipHandle.Complete();
    }

    /// <summary>
    /// Called from network receive when a remote PIP state message arrives.
    /// </summary>
    public static void OnRemotePIPState(CameraPIPStateMessage msg)
    {
        if (!initialized) return;

        pipHandle.Complete();

        if (msg.IsActive)
        {
            float3 pos = new float3(msg.PositionX, msg.PositionY, msg.PositionZ);
            quaternion rot = new quaternion(msg.RotationX, msg.RotationY, msg.RotationZ, msg.RotationW);

            AddRemotePIP(msg.PlayerID, pos, rot);

            OnRemotePIPCreated?.Invoke(msg.PlayerID, pos, rot);
        }
        else
        {
            RemoveRemotePIP(msg.PlayerID);

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
            pipHandle.Complete();
            targetPositions[index] = new float3(msg.PositionX, msg.PositionY, msg.PositionZ);
            targetRotations[index] = new quaternion(msg.RotationX, msg.RotationY, msg.RotationZ, msg.RotationW);
        }
    }

    /// <summary>
    /// Send local PIP camera state to server.
    /// </summary>
    public static void SendPIPState(bool isActive, Vector3 position, Quaternion rotation)
    {
        ClientCameraPIPStateMessage msg = new ClientCameraPIPStateMessage
        {
            IsActive = isActive,
            PositionX = position.x,
            PositionY = position.y,
            PositionZ = position.z,
            RotationX = rotation.x,
            RotationY = rotation.y,
            RotationZ = rotation.z,
            RotationW = rotation.w,
        };

        NetDataWriter writer = new NetDataWriter();
        msg.Serialize(writer);
        BasisNetworkConnection.LocalPlayerPeer.Send(writer, BasisNetworkCommons.CameraPIPStateChannel, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// Send local PIP camera position and rotation to server.
    /// </summary>
    public static void SendPIPPosition(Vector3 position, Quaternion rotation)
    {
        ClientCameraPIPPositionMessage msg = new ClientCameraPIPPositionMessage
        {
            PositionX = position.x,
            PositionY = position.y,
            PositionZ = position.z,
            RotationX = rotation.x,
            RotationY = rotation.y,
            RotationZ = rotation.z,
            RotationW = rotation.w,
        };

        NetDataWriter writer = new NetDataWriter();
        msg.Serialize(writer);
        BasisNetworkConnection.LocalPlayerPeer.Send(writer, BasisNetworkCommons.CameraPIPPositionChannel, DeliveryMethod.Sequenced);
    }

    /// <summary>
    /// Try to get the world position of a remote PIP camera by player ID.
    /// </summary>
    public static bool TryGetPIPPosition(ushort playerId, out Vector3 position)
    {
        if (pipInstances.TryGetValue(playerId, out GameObject instance) && instance != null)
        {
            position = instance.transform.position;
            return true;
        }
        position = Vector3.zero;
        return false;
    }

    /// <summary>
    /// Send a camera shutter sound event to the server for broadcast to other clients.
    /// </summary>
    public static void SendShutterSound()
    {
        NetDataWriter writer = new NetDataWriter();
        writer.Put(BasisNetworkCommons.EventType_CameraShutterSound);
        BasisNetworkConnection.LocalPlayerPeer.Send(writer, BasisNetworkCommons.EventsChannel, DeliveryMethod.Sequenced);
    }

    /// <summary>
    /// Send a camera countdown event to the server for broadcast to other clients.
    /// </summary>
    public static void SendCountdown(byte seconds)
    {
        ClientCameraCountdownMessage msg = new ClientCameraCountdownMessage
        {
            Seconds = seconds,
        };

        NetDataWriter writer = new NetDataWriter();
        writer.Put(BasisNetworkCommons.EventType_CameraCountdown);
        msg.Serialize(writer);
        BasisNetworkConnection.LocalPlayerPeer.Send(writer, BasisNetworkCommons.EventsChannel, DeliveryMethod.Sequenced);
    }

    /// <summary>
    /// Called from BasisNetworkEvents when a remote player's shutter sound message arrives.
    /// Plays the shutter sound at the remote PIP camera position.
    /// </summary>
    public static void OnRemoteShutterSound(CameraShutterSoundMessage msg)
    {
        if (BasisDeviceManagement.Instance.CameraShutterSound != null && TryGetPIPPosition(msg.PlayerID, out Vector3 position))
        {
            AudioSource.PlayClipAtPoint(BasisDeviceManagement.Instance.CameraShutterSound, position, SMModuleAudio.ActivePropVolume);
        }

        OnRemoteShutterSoundReceived?.Invoke(msg.PlayerID, TryGetPIPPosition(msg.PlayerID, out Vector3 pos) ? pos : Vector3.zero);
    }

    /// <summary>
    /// Called from BasisNetworkEvents when a remote player starts a countdown.
    /// Replays the same tick/shutter timing at the remote PIP camera position.
    /// </summary>
    public static void OnRemoteCountdown(CameraCountdownMessage msg)
    {
        if (!initialized) return;
        BasisDeviceManagement.Instance.StartCoroutine(RemoteCountdownCoroutine(msg.PlayerID, msg.Seconds));
    }

    private static IEnumerator RemoteCountdownCoroutine(ushort playerId, int seconds)
    {
        for (int i = seconds; i > 0; i--)
        {
            if (BasisDeviceManagement.Instance.CameraCountdownTickSound != null && TryGetPIPPosition(playerId, out Vector3 tickPos))
            {
                AudioSource.PlayClipAtPoint(BasisDeviceManagement.Instance.CameraCountdownTickSound, tickPos, SMModuleAudio.ActivePropVolume);
            }
            yield return new WaitForSeconds(1f);
        }

        // Match the 0.5s pause before capture
        yield return new WaitForSeconds(0.5f);

        if (BasisDeviceManagement.Instance.CameraShutterSound != null && TryGetPIPPosition(playerId, out Vector3 shutterPos))
        {
            AudioSource.PlayClipAtPoint(BasisDeviceManagement.Instance.CameraShutterSound, shutterPos, SMModuleAudio.ActivePropVolume);
        }
    }

    /// <summary>
    /// Clean up when a remote player disconnects.
    /// </summary>
    public static void HandlePlayerDisconnect(ushort playerId)
    {
        if (!initialized) return;

        if (pipInstances.ContainsKey(playerId))
        {
            pipHandle.Complete();
            RemoveRemotePIP(playerId);

            OnRemotePIPDestroyed?.Invoke(playerId);
        }
    }

    /// <summary>
    /// Spawn a remote PIP camera and its owner-name tag, appending them to the dense SoA
    /// and transform arrays. Both objects are independent scene roots (DontDestroyOnLoad)
    /// so their per-frame transform writes spread across worker threads.
    /// </summary>
    private static void AddRemotePIP(ushort playerId, float3 position, quaternion rotation)
    {
        if (loadedPrefab == null)
        {
            BasisDebug.LogError("Remote PIP prefab not loaded");
            return;
        }

        // Replace any existing camera for this player
        if (playerIdToIndex.ContainsKey(playerId))
        {
            RemoveRemotePIP(playerId);
        }

        Vector3 pos = new Vector3(position.x, position.y, position.z);
        Quaternion rot = new Quaternion(rotation.value.x, rotation.value.y, rotation.value.z, rotation.value.w);

        GameObject instance = UnityEngine.Object.Instantiate(loadedPrefab, pos, rot);
        UnityEngine.Object.DontDestroyOnLoad(instance);

        if (instance.TryGetComponent<BasisCameraRemotePip>(out BasisCameraRemotePip pipMeta))
        {
            pipMeta.PlayerID = playerId;
        }

        float height = ComputeNamePlateHeight(instance, pos);

        GameObject plateGo = new GameObject("OwnerNamePlate");
        UnityEngine.Object.DontDestroyOnLoad(plateGo);
        plateGo.transform.localScale = new Vector3(NamePlateScale, NamePlateScale, NamePlateScale);
        plateGo.transform.position = new Vector3(pos.x, pos.y + height, pos.z);

        MeshFilter filter = plateGo.AddComponent<MeshFilter>();
        MeshRenderer renderer = plateGo.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        int index = currentPositions.Length;
        currentPositions.Add(position);
        targetPositions.Add(position);
        currentRotations.Add(rotation);
        targetRotations.Add(rotation);
        nameplateHeights.Add(height);
        cameraTransforms.Add(instance.transform);
        namePlateTransforms.Add(plateGo.transform);
        denseToPlayerId.Add(playerId);
        playerIdToIndex[playerId] = index;

        pipInstances[playerId] = instance;

        RemotePipNamePlate plate = new RemotePipNamePlate
        {
            Root = plateGo,
            Filter = filter,
            Renderer = renderer,
        };
        pipNamePlates[playerId] = plate;

        if (TryResolveDisplayName(playerId, out string displayName) &&
            BasisRemoteNamePlateDriver.BakeNameMesh(displayName, filter, renderer))
        {
            plate.Baked = true;
        }
        else
        {
            pendingNameBakes.Add(playerId);
        }
    }

    /// <summary>
    /// Remove a remote PIP camera and its owner-name tag, swap-back compacting the dense
    /// SoA and transform arrays. Caller must have completed in-flight jobs first.
    /// </summary>
    private static void RemoveRemotePIP(ushort playerId)
    {
        if (playerIdToIndex.TryGetValue(playerId, out int idx))
        {
            int last = currentPositions.Length - 1;

            currentPositions.RemoveAtSwapBack(idx);
            targetPositions.RemoveAtSwapBack(idx);
            currentRotations.RemoveAtSwapBack(idx);
            targetRotations.RemoveAtSwapBack(idx);
            nameplateHeights.RemoveAtSwapBack(idx);
            cameraTransforms.RemoveAtSwapBack(idx);
            namePlateTransforms.RemoveAtSwapBack(idx);

            if (idx != last)
            {
                ushort movedPlayer = denseToPlayerId[last];
                denseToPlayerId[idx] = movedPlayer;
                playerIdToIndex[movedPlayer] = idx;
            }
            denseToPlayerId.RemoveAt(last);
            playerIdToIndex.Remove(playerId);
        }

        pendingNameBakes.Remove(playerId);

        if (pipNamePlates.TryGetValue(playerId, out RemotePipNamePlate plate))
        {
            pipNamePlates.Remove(playerId);
            if (plate.Filter != null && plate.Filter.sharedMesh != null)
            {
                UnityEngine.Object.Destroy(plate.Filter.sharedMesh);
            }
            if (plate.Root != null)
            {
                UnityEngine.Object.Destroy(plate.Root);
            }
        }

        if (pipInstances.TryGetValue(playerId, out GameObject instance))
        {
            pipInstances.Remove(playerId);
            if (instance != null)
            {
                UnityEngine.Object.Destroy(instance);
            }
        }
    }

    /// <summary>
    /// Bakes any owner-name tags whose display name wasn't known at spawn, once it resolves.
    /// Drained on the main thread (TMP baking) before scheduling the transform jobs.
    /// </summary>
    private static void ProcessPendingNameBakes()
    {
        for (int i = pendingNameBakes.Count - 1; i >= 0; i--)
        {
            ushort playerId = pendingNameBakes[i];
            if (!pipNamePlates.TryGetValue(playerId, out RemotePipNamePlate plate) || plate.Baked)
            {
                pendingNameBakes.RemoveAt(i);
                continue;
            }

            if (TryResolveDisplayName(playerId, out string displayName) &&
                BasisRemoteNamePlateDriver.BakeNameMesh(displayName, plate.Filter, plate.Renderer))
            {
                plate.Baked = true;
                pendingNameBakes.RemoveAt(i);
            }
        }
    }

    private static float ComputeNamePlateHeight(GameObject cameraInstance, Vector3 cameraPosition)
    {
        if (TryGetModelWorldBounds(cameraInstance, out Bounds bounds))
        {
            float clearance = Vector3.Distance(bounds.center, cameraPosition) + bounds.extents.magnitude;
            return clearance + NamePlateMargin + NamePlateHalfHeight;
        }
        return 0.25f;
    }

    private static bool TryGetModelWorldBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>();
        bool any = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer r = renderers[i];
            if (r == null) continue;
            if (!any)
            {
                bounds = r.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }
        return any;
    }

    private static bool TryResolveDisplayName(ushort playerId, out string displayName)
    {
        if (BasisNetworkPlayer.GetPlayerById(playerId, out BasisNetworkPlayer networkPlayer) &&
            networkPlayer != null && networkPlayer.Player != null)
        {
            displayName = networkPlayer.Player.DisplayName;
            return !string.IsNullOrEmpty(displayName);
        }
        displayName = string.Empty;
        return false;
    }

    private sealed class RemotePipNamePlate
    {
        public GameObject Root;
        public MeshFilter Filter;
        public MeshRenderer Renderer;
        public bool Baked;
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    struct PIPSmoothJob : IJobParallelFor
    {
        public float DeltaTime;
        public float LerpSpeed;

        public NativeArray<float3> CurrentPositions;
        [ReadOnly] public NativeArray<float3> TargetPositions;
        public NativeArray<quaternion> CurrentRotations;
        [ReadOnly] public NativeArray<quaternion> TargetRotations;

        public void Execute(int index)
        {
            float t = math.saturate(DeltaTime * LerpSpeed);

            CurrentPositions[index] = math.lerp(
                CurrentPositions[index],
                TargetPositions[index],
                t
            );

            CurrentRotations[index] = math.nlerp(
                CurrentRotations[index],
                TargetRotations[index],
                t
            );
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast)]
    struct PIPCameraApplyJob : IJobParallelForTransform
    {
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<quaternion> Rotations;

        public void Execute(int index, TransformAccess transform)
        {
            transform.SetPositionAndRotation(Positions[index], Rotations[index]);
        }
    }

    [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
    struct PIPNamePlateApplyJob : IJobParallelForTransform
    {
        /// <summary>Local viewer (camera) world position used to bill-board the tag (yaw-only).</summary>
        public float3 ViewerPosition;

        [ReadOnly] public NativeArray<float3> CameraPositions;
        [ReadOnly] public NativeArray<float> Heights;

        public void Execute(int index, TransformAccess transform)
        {
            float3 cam = CameraPositions[index];
            float3 platePos = new float3(cam.x, cam.y + Heights[index], cam.z);

            float3 toViewer = ViewerPosition - platePos;
            float2 xz = new float2(toViewer.x, toViewer.z);
            float yaw = math.lengthsq(xz) > 1e-12f ? math.atan2(xz.x, xz.y) : 0f;
            quaternion rot = quaternion.RotateY(yaw);

            transform.SetPositionAndRotation(platePos, rot);
        }
    }
}
