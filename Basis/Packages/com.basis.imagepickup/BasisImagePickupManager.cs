using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Basis.BasisUI;
using Basis.EventDriver;
using Basis.Network.Core;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Unity.Collections;
using UnityEngine;

namespace Basis.ImagePickup
{
    /// <summary>
    /// Per-client singleton that replicates image pickups. It shares a deterministic network identity across
    /// all clients, so any client's manager can message the others. The server (or the P2P link) relays the
    /// bytes and never stores them: an image exists only while its spawner is connected, and a late joiner is
    /// served by the owner re-sending. Anyone may delete any image for everyone.
    /// </summary>
    public class BasisImagePickupManager : BasisNetworkBehaviour
    {
        public static BasisImagePickupManager Instance;

        private const string FixedNetworkIdentifier = "BasisImagePickupManager";
        private const int MaxIgnoredOwnerNameBytes = 1024;
        private const BasisDebug.LogTag LogTag = BasisDebug.LogTag.Pickups;
        internal const int MaxOwnerNameUtf8Bytes = 256;
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        private const byte OpSpawn = 1;
        private const byte OpChunk = 2;
        private const byte OpTransform = 3;
        private const byte OpDespawn = 4;
        private const byte OpClaim = 5;
        private const byte OpAnimationSpawn = 6;
        private const byte OpAnimationChunk = 7;

        // Values 0 and 1 were used by pre-release animation transport experiments and remain
        // reserved so stale clients cannot misinterpret the production V2 native-LZ4 payload.
        private const byte AnimationFormatNativeLz4 = 2;

        private sealed class SpawnRateLimitState
        {
            public float Tokens;
            public float LastRefillTime;
        }

        private sealed class OwnedImage
        {
            public BasisImagePickupObject Object;
            public byte[] CleanPng;
            public int Width;
            public int Height;
            public string OwnerName;
            public BasisNativeAnimationPayload AnimationPayload;
            public long PlaybackEpochUtcTicks;
        }

        private sealed class InboundTransfer
        {
            public ushort Sender;
            public Guid Id;
            public byte[] Buffer;
            public long ReservedBytes;
            public bool[] Received;
            public int ReceivedCount;
            public int TotalChunks;
            public int Width;
            public int Height;
            public ushort OwnerId;
            public string OwnerName;
            public float Deadline;
            public Vector3 Position;
            public Quaternion Rotation;
        }

        private sealed class InboundAnimationTransfer
        {
            public ushort Sender;
            public Guid Id;
            public NativeArray<byte> Buffer;
            public long ReservedBytes;
            public NativeArray<byte> Received;
            public int ReceivedCount;
            public int TotalChunks;
            public long PlaybackEpochUtcTicks;
            public float Deadline;
        }

        private sealed class PendingGifSpawn
        {
            public string Path;
            public Vector3 Position;
            public Quaternion Rotation;
            public BasisGifDecodeJobRequest Job;
        }

        private sealed class QueuedInboundAnimationDecode
        {
            public ushort Sender;
            public Guid Id;
            public BasisNativeAnimationPayload Payload;
            public long ReservedBytes;
            public int PayloadBytes;
            public int DecodedBytes;
            public long PlaybackEpochUtcTicks;
        }

        private sealed class PendingInboundAnimationDecode
        {
            public ushort Sender;
            public Guid Id;
            public BasisNativeAnimationPayload Payload;
            public long ReservedBytes;
            public int PayloadBytes;
            public int DecodedBytes;
            public long PlaybackEpochUtcTicks;
            public BasisBurstAnimationDecodeRequest Job;
        }

        private sealed class OutboundImageTransfer
        {
            public Guid Id;
            public ushort OwnerId;
            public string OwnerName;
            public int Width;
            public int Height;
            public byte[] Png;
            public int NextChunkIndex;
            public Vector3 Position;
            public Quaternion Rotation;
            public ushort[] Recipients;
            public bool HeaderSent;
        }

        private sealed class OutboundAnimationTransfer
        {
            public Guid Id;
            public BasisNativeAnimationPayload Payload;
            public int NextChunkIndex;
            public long PlaybackEpochUtcTicks;
            public ushort[] Recipients;
            public BasisAnimationPacketJobRequest PacketJob;
            public BasisAnimationPacketBatch Packets;
            public byte[] HeaderBuffer;
            public byte[] FullChunkBuffer;
            public byte[] TailChunkBuffer;
            public bool HeaderSent;
            public long EnqueuedTimestamp;
            public long FirstPacketQueueTicks;
        }

        private readonly Dictionary<Guid, BasisImagePickupObject> _images = new();
        private readonly Dictionary<Guid, OwnedImage> _owned = new();
        private readonly Dictionary<Guid, BasisNativeAnimationPayload> _remoteAnimationPayloads = new();
        private readonly Dictionary<Guid, InboundTransfer> _inbound = new();
        private readonly Dictionary<Guid, InboundAnimationTransfer> _inboundAnimations =
            new();
        private readonly Queue<OutboundImageTransfer> _outboundImages = new();
        private readonly Queue<OutboundAnimationTransfer> _outboundAnimations = new();
        private readonly Queue<PendingGifSpawn> _queuedGifSpawns = new();
        private readonly List<PendingGifSpawn> _pendingGifSpawns = new();
        private readonly List<QueuedInboundAnimationDecode> _queuedInboundAnimationDecodes =
            new();
        private readonly List<PendingInboundAnimationDecode> _pendingInboundAnimationDecodes =
            new();
        private readonly HashSet<Guid> _animationAttempted = new();
        private long _reservedInboundTransferBytes;
        private bool _gifDecodePausedForMemory;
        private bool _destroying;
        private readonly Dictionary<ushort, SpawnRateLimitState> _spawnRateBySender =
            new();
        private readonly List<Guid> _scratchIds = new();

#if UNITY_EDITOR
        [SerializeField]
        private string editorTestImagePath;
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            BasisEventDriver.OnUpdate += Simulate;
            if (Application.isPlaying)
                DontDestroyOnLoad(gameObject);
        }

        public override void OnDestroy()
        {
            _destroying = true;
            BasisEventDriver.OnUpdate -= Simulate;
            if (Instance == this)
                Instance = null;

            // Release player-owned native buffers before Unity's shutdown leak validation runs.
            _scratchIds.Clear();
            foreach (Guid id in _images.Keys)
                _scratchIds.Add(id);
            int trackedImageCount = _scratchIds.Count;
            for (int i = 0; i < trackedImageCount; i++)
                RemoveImage(_scratchIds[i]);
            _scratchIds.Clear();

            int pendingGifSpawnCount = _pendingGifSpawns.Count;
            for (int i = 0; i < pendingGifSpawnCount; i++)
                _pendingGifSpawns[i].Job?.Dispose();
            _pendingGifSpawns.Clear();
            _queuedGifSpawns.Clear();

            foreach (InboundTransfer transfer in _inbound.Values)
                ReleaseInboundTransferBytes(transfer.ReservedBytes);
            _inbound.Clear();

            int queuedInboundDecodeCount = _queuedInboundAnimationDecodes.Count;
            for (int i = 0; i < queuedInboundDecodeCount; i++)
            {
                QueuedInboundAnimationDecode queued = _queuedInboundAnimationDecodes[i];
                queued.Payload?.Dispose();
                ReleaseInboundTransferBytes(queued.ReservedBytes);
            }
            _queuedInboundAnimationDecodes.Clear();

            int pendingInboundDecodeCount = _pendingInboundAnimationDecodes.Count;
            for (int i = 0; i < pendingInboundDecodeCount; i++)
            {
                PendingInboundAnimationDecode pending = _pendingInboundAnimationDecodes[
                    i
                ];
                pending.Job?.Dispose();
                pending.Payload?.Dispose();
                ReleaseInboundTransferBytes(pending.ReservedBytes);
            }
            _pendingInboundAnimationDecodes.Clear();

            foreach (InboundAnimationTransfer transfer in _inboundAnimations.Values)
            {
                if (transfer.Buffer.IsCreated)
                    transfer.Buffer.Dispose();
                if (transfer.Received.IsCreated)
                    transfer.Received.Dispose();
                ReleaseInboundTransferBytes(transfer.ReservedBytes);
            }
            _inboundAnimations.Clear();

            _outboundImages.Clear();
            while (_outboundAnimations.Count > 0)
                DisposeOutboundAnimationTransfer(_outboundAnimations.Dequeue());

            foreach (OwnedImage owned in _owned.Values)
            {
                if (owned.Object != null)
                    owned.Object.AnimatedImagePlayer?.ClearReloadPayload();
                owned.AnimationPayload?.Dispose();
            }
            _owned.Clear();
            foreach (BasisNativeAnimationPayload payload in _remoteAnimationPayloads.Values)
                payload?.Dispose();
            _remoteAnimationPayloads.Clear();
            _reservedInboundTransferBytes = 0;

            base.OnDestroy();
        }

        public override void Start()
        {
            AssignNetworkGUIDIdentifier(FixedNetworkIdentifier);
            base.Start();
        }

        public override void OnNetworkReady()
        {
            BasisDebug.Log($"Image pickup manager ready (network id {NetworkID}).", LogTag);
        }

        /// <summary>
        /// Validates a PNG, JPEG, or GIF file, spawns it locally, and broadcasts a sanitized poster PNG.
        /// Multi-frame GIFs additionally replicate normalized animation data and a synchronized playback epoch.
        /// </summary>
        public bool SpawnFromFile(string path)
        {
            if (!CanStartLocalSpawn(path))
                return false;

            int currentCount = GetLocalReservedImageCount();
            if (currentCount >= BasisImagePickupSettings.MaxConcurrentImagesPerSender)
            {
                BasisImagePickupRejectionPopup.ShowImageLimit(currentCount, 1);
                BasisDebug.LogWarning(
                    $"Image pickup rejected: local image limit of "
                        + $"{BasisImagePickupSettings.MaxConcurrentImagesPerSender} reached.",
                    LogTag
                );
                return false;
            }

            GetSpawnPose(out Vector3 position, out Quaternion rotation);
            return SpawnFromFileAtPose(path, position, rotation);
        }

        /// <summary>
        /// Spawns one drag/drop batch in stable row-major slots. GIFs retain their assigned slots while
        /// waiting in the decode queue, so faster static images or shorter GIFs cannot scramble the layout.
        /// </summary>
        public int SpawnFromFiles(IReadOnlyList<string> paths)
        {
            if (paths == null || paths.Count == 0)
                return 0;

            int pathCount = paths.Count;
            var supportedPaths = new List<string>(pathCount);
            for (int i = 0; i < pathCount; i++)
            {
                string path = paths[i];
                if (!string.IsNullOrWhiteSpace(path) && BasisImageSecurity.HasSupportedImageExtension(path))
                {
                    supportedPaths.Add(path);
                }
            }
            if (supportedPaths.Count == 0)
                return 0;
            if (!CanStartLocalSpawn(supportedPaths[0]))
                return 0;

            int currentCount = GetLocalReservedImageCount();
            int availableSlots = CalculateAvailableLocalImageSlots(
                _owned.Count,
                _queuedGifSpawns.Count,
                _pendingGifSpawns.Count
            );
            if (availableSlots <= 0)
            {
                BasisImagePickupRejectionPopup.ShowImageLimit(currentCount, supportedPaths.Count);
                BasisDebug.LogWarning(
                    $"Image pickup batch rejected: local image limit of "
                        + $"{BasisImagePickupSettings.MaxConcurrentImagesPerSender} reached.",
                    LogTag
                );
                return 0;
            }

            int attemptCount = Mathf.Min(supportedPaths.Count, availableSlots);
            GetSpawnPose(out Vector3 batchCenter, out Quaternion rotation);
            float minimumCenterY = GetMinimumBatchImageCenterY(batchCenter.y);
            int columns = CalculateBatchSpawnColumns(attemptCount, batchCenter.y, minimumCenterY);
            float minimumLocalY = minimumCenterY - batchCenter.y;
            Vector3 horizontalRight = rotation * Vector3.right;

            int accepted = 0;
            int animatedAccepted = 0;
            int supportedPathCount = supportedPaths.Count;
            for (int pathIndex = 0; pathIndex < supportedPathCount && accepted < attemptCount; pathIndex++)
            {
                Vector3 localOffset = CalculateBatchSpawnLocalOffset(accepted, attemptCount, columns, minimumLocalY);
                Vector3 position =
                    batchCenter
                    + horizontalRight * localOffset.x
                    + Vector3.up * localOffset.y;
                string path = supportedPaths[pathIndex];
                if (!SpawnFromFileAtPose(path, position, rotation))
                    continue;

                accepted++;
                if (BasisAnimatedImageJobs.IsGifPath(path))
                    animatedAccepted++;
            }

            BasisImagePickupRejectionPopup.ShowBatchNotice(
                currentCount,
                supportedPaths.Count,
                accepted,
                animatedAccepted
            );
            return accepted;
        }

        private int GetLocalReservedImageCount()
        {
            return _owned.Count + _queuedGifSpawns.Count + _pendingGifSpawns.Count;
        }

        internal static int CalculateAvailableLocalImageSlots(int ownedCount, int queuedGifCount, int activeGifCount)
        {
            long reserved =
                Math.Max(0, ownedCount)
                + (long)Math.Max(0, queuedGifCount)
                + Math.Max(0, activeGifCount);
            long available =
                BasisImagePickupSettings.MaxConcurrentImagesPerSender - reserved;
            return available <= 0 ? 0 : (int)Math.Min(int.MaxValue, available);
        }

        private static bool CanStartLocalSpawn(string path)
        {
            if (!BasisNetworkModeration.GlobalImagesLocked || BasisNetworkModeration.LocalPlayerHasGlobalLockBypass())
            {
                return true;
            }

            string reason = BasisLocalization.Get("imagePickup.popup.reason.adminLocked");
            BasisImagePickupRejectionPopup.Show(path, reason);
            BasisDebug.LogWarning($"Image pickup rejected: {reason}", LogTag);
            return false;
        }

        private bool SpawnFromFileAtPose(string path, Vector3 position, Quaternion rotation)
        {
            if (BasisAnimatedImageJobs.IsGifPath(path))
                return QueueGifSpawn(path, position, rotation);

            return SpawnValidatedFile(path, BasisImageSecurity.ValidateFile(path), position, rotation);
        }

        private bool QueueGifSpawn(string path, Vector3 position, Quaternion rotation)
        {
            _queuedGifSpawns.Enqueue(new PendingGifSpawn { Path = path, Position = position, Rotation = rotation });
            BasisDebug.Log(
                $"Image pickup: queued GIF '{Path.GetFileName(path)}' "
                    + $"({_queuedGifSpawns.Count:N0} waiting, "
                    + $"{_pendingGifSpawns.Count:N0} active).",
                LogTag
            );
            StartQueuedGifSpawns();
            return true;
        }

        private void StartQueuedGifSpawns()
        {
            if (BasisAnimatedImageData.ShouldPauseNewDecode())
            {
                LogGifDecodePausedForMemory();
                return;
            }
            if (_queuedGifSpawns.Count == 0)
            {
            _gifDecodePausedForMemory = false;
                return;
            }

            while (
                _pendingGifSpawns.Count
                    < BasisImagePickupSettings.MaxConcurrentAnimationDecodeJobs
                && _queuedGifSpawns.Count > 0
            )
            {
                PendingGifSpawn pending = _queuedGifSpawns.Peek();
                if (ShouldDeferGifDecodeForMemory(pending.Path))
                {
                    LogGifDecodePausedForMemory();
                    return;
                }
                _queuedGifSpawns.Dequeue();
                try
                {
                    pending.Job = BasisAnimatedImageJobs.ScheduleGifDecode(pending.Path);
                    _pendingGifSpawns.Add(pending);
                    _gifDecodePausedForMemory = false;
                    BasisDebug.Log(
                        $"Image pickup: started GIF Burst pipeline for "
                            + $"'{Path.GetFileName(pending.Path)}' "
                            + $"({_pendingGifSpawns.Count:N0}/"
                            + $"{BasisImagePickupSettings.MaxConcurrentAnimationDecodeJobs:N0} active, "
                            + $"{_queuedGifSpawns.Count:N0} waiting).",
                        LogTag
                    );
                }
                catch (BasisAnimationMemoryBudgetException)
                {
                    _queuedGifSpawns.Enqueue(pending);
                    LogGifDecodePausedForMemory();
                    return;
                }
                catch (Exception exception)
                {
                    string reason = BasisLocalization.Get(
                        "imagePickup.popup.reason.animationStartFailed",
                        exception.Message
                    );
                    BasisImagePickupRejectionPopup.Show(pending.Path, reason);
                    BasisDebug.LogWarning($"Image pickup rejected: {reason}", LogTag);
                }
            }
        }

        private static bool ShouldDeferGifDecodeForMemory(string path)
        {
            try
            {
                long length = new FileInfo(path).Length;
                if (length <= 0 || length > BasisImagePickupSettings.MaxAnimationSourceBytes)
            {
                return false;
            }

                long estimate = BasisAnimatedImageData.EstimateGifDecodeWorkingBytes((int)length);
                return !BasisAnimatedImageData.CanReserveWorkingBytes(estimate);
            }
            catch
            {
                // File validation reports missing, inaccessible, or changing files with the
                // normal import error instead of converting them into a memory deferral.
                return false;
            }
        }

        private void LogGifDecodePausedForMemory()
        {
            if (_gifDecodePausedForMemory)
                return;
            _gifDecodePausedForMemory = true;
            long residentBytes =
                BasisAnimatedImageData.TotalResidentNativeBytes
                + BasisAnimatedImageData.TotalResidentCompositorBytes;
            BasisDebug.LogWarning(
                $"Image pickup paused queued GIF decoding at "
                    + $"{residentBytes / (1024L * 1024L):N0} MiB of resident "
                    + "animation and compositor data; reloadable offscreen animations "
                    + "will release resources until memory is available.",
                LogTag
            );
        }

        private void ProcessCompletedGifSpawns()
        {
            int pendingGifSpawnCount = _pendingGifSpawns.Count;
            for (int index = pendingGifSpawnCount - 1; index >= 0; index--)
            {
                PendingGifSpawn pending = _pendingGifSpawns[index];
                BasisGifDecodeJobResult workerResult;
                try
                {
                    if (!pending.Job.TryComplete(out workerResult))
                        continue;
                }
                catch (Exception exception)
                {
                    _pendingGifSpawns.RemoveAt(index);
                    DisposeRequest(pending.Job, "GIF decode request");
                    string failureReason = BasisLocalization.Get(
                        "imagePickup.popup.reason.animationStartFailed",
                        exception.GetBaseException().Message
                    );
                    BasisImagePickupRejectionPopup.Show(pending.Path, failureReason);
                    BasisDebug.LogWarning(
                        $"Image pickup rejected after GIF completion failed: {failureReason}",
                        LogTag
                    );
                    continue;
                }
                _pendingGifSpawns.RemoveAt(index);

                try
                {
                    if (
                        BasisNetworkModeration.GlobalImagesLocked
                        && !BasisNetworkModeration.LocalPlayerHasGlobalLockBypass()
                    )
                    {
                        string lockedReason = BasisLocalization.Get(
                            "imagePickup.popup.reason.adminLockedDuringDecode"
                        );
                        BasisImagePickupRejectionPopup.Show(pending.Path, lockedReason);
                        BasisDebug.LogWarning($"Image pickup rejected: {lockedReason}", LogTag);
                        continue;
                    }

                    BasisImageValidationResult result =
                        BasisAnimatedImageJobs.FinalizeGifDecode(workerResult);
                    if (result.Ok)
                    {
                        double workerMilliseconds =
                            workerResult.WorkerElapsedTicks
                            * 1000d
                            / Stopwatch.Frequency;
                        BasisDebug.Log(
                            $"Image pickup: Burst GIF pipeline finished in {workerMilliseconds:0.###} ms "
                                + $"({result.Animation?.FrameCount ?? 1} frames).",
                            LogTag
                        );
                    }
                    SpawnValidatedFile(pending.Path, result, pending.Position, pending.Rotation);
                }
                finally
                {
                    DisposeRequest(pending.Job, "GIF decode request");
                }
            }

            StartQueuedGifSpawns();
        }

        private bool SpawnValidatedFile(
            string path,
            BasisImageValidationResult result,
            Vector3 position,
            Quaternion rotation
        )
        {
            if (!result.Ok)
            {
                BasisImagePickupRejectionPopup.Show(path, result.Error);
                BasisDebug.LogWarning($"Image pickup rejected: {result.Error}", LogTag);
                return false;
            }
            if (
                !CanAcceptLocalImage(
                    result.CleanPng?.Length ?? 0,
                    result.Width,
                    result.Height,
                    out string imageBudgetReason
                )
            )
            {
                DisposeRejectedValidationResult(ref result);
                BasisImagePickupRejectionPopup.Show(path, imageBudgetReason);
                BasisDebug.LogWarning($"Image pickup rejected: {imageBudgetReason}", LogTag);
                return false;
            }

            Guid id = Guid.NewGuid();
            ushort ownerId =
                BasisNetworkPlayer.LocalPlayer != null
                    ? BasisNetworkPlayer.LocalPlayer.playerId
                    : (ushort)0;
            string ownerName = NormalizeOwnerNameForNetwork(
                BasisLocalPlayer.Instance != null
                    ? BasisLocalPlayer.Instance.SafeDisplayName
                    : "Unknown"
            );

            var pickup = BasisImagePickupObject.Build(
                this,
                id,
                ownerId,
                ownerName,
                true,
                result.Texture,
                result.CleanPng,
                result.HasAlpha,
                position,
                rotation
            );
            BasisNativeAnimationPayload animationPayload = null;
            long playbackEpochUtcTicks = 0;
            if (result.Animation != null && result.Animation.FrameCount <= 1)
            {
                result.TakeAnimation()?.Dispose();
                result.TakeAnimationPayload()?.Dispose();
            }

            BasisAnimatedImageData candidateAnimation = result.TakeAnimation();
            BasisNativeAnimationPayload candidatePayload =
                result.TakeAnimationPayload();
            if (candidateAnimation != null)
            {
                playbackEpochUtcTicks = BasisNetworkManagement.RemoteUtcTime().Ticks;
                int frameCount = candidateAnimation.FrameCount;
                if (candidatePayload == null)
                {
                    playbackEpochUtcTicks = 0;
                    BasisDebug.LogWarning(
                        $"Image pickup: GIF animation encoding failed; showing the poster frame only so decoded frame memory remains reclaimable: {result.AnimationNetworkError ?? "unknown error"}",
                        LogTag
                    );
                }
                else if (!CanAttachLocalAnimation(candidateAnimation, true, out string animationBudgetReason))
                {
                    playbackEpochUtcTicks = 0;
                    BasisDebug.LogWarning(
                        $"Image pickup animation omitted: {animationBudgetReason}; "
                            + "the sanitized poster remains available.",
                        LogTag
                    );
                }
                else if (pickup.TrySetAnimation(candidateAnimation, playbackEpochUtcTicks, candidatePayload))
                {
                    animationPayload = candidatePayload;
                    candidateAnimation = null;
                    candidatePayload = null;
                    bool decodedDataDeferred = pickup.AnimatedImagePlayer?.Data == null;
                    string attachmentMessage = decodedDataDeferred
                        ? "Image pickup: GIF animation retained as a compact payload and deferred to "
                            + $"the closest-animation decoded-data budget ({frameCount} frames, "
                            + $"{animationPayload.Length} LZ4 bytes)."
                        : "Image pickup: GIF animation attached locally; Burst packet batches will use "
                            + $"the compact persistent native payload ({frameCount} frames, "
                            + $"{animationPayload.Length} LZ4 bytes).";
                    BasisDebug.Log(attachmentMessage, LogTag);
                }
                else
                {
                    playbackEpochUtcTicks = 0;
                    BasisDebug.LogWarning(
                        "Image pickup: GIF decoded, but animated playback could not be attached; showing and replicating the poster frame only.",
                        LogTag
                    );
                }
            }
            candidateAnimation?.Dispose();
            candidatePayload?.Dispose();
            _images[id] = pickup;
            var owned = new OwnedImage
            {
                Object = pickup,
                CleanPng = result.CleanPng,
                Width = result.Width,
                Height = result.Height,
                OwnerName = ownerName,
                AnimationPayload = animationPayload,
                PlaybackEpochUtcTicks = playbackEpochUtcTicks,
            };
            _owned[id] = owned;

            BasisShareableRegistry.Register(
                new BasisShareableEntry
            {
                Id = id.ToString(),
                Kind = BasisShareableKind.Image,
                Title = $"{result.Width}x{result.Height}",
                SharerName = ownerName,
                Actions = new List<BasisShareableAction>
                {
                    new BasisShareableAction
                    {
                        Style = BasisShareableActionStyle.Destructive,
                        Invoke = () => { if (Instance != null) Instance.RequestDespawn(id); },
                    },
                },
            });

            if (HasNetworkID)
            {
                SendSpawn(
                    id,
                    ownerId,
                    ownerName,
                    result.Width,
                    result.Height,
                    result.CleanPng,
                    position,
                    rotation,
                    null
                );
                if (animationPayload != null && playbackEpochUtcTicks > 0)
                    SendAnimation(id, owned, null);
                BasisDebug.Log(
                    $"Image pickup spawned and replicated ({result.Width}x{result.Height}, {result.CleanPng.Length} poster bytes, {animationPayload?.Length ?? 0} animation bytes).",
                    LogTag
                );
            }
            else
            {
                BasisDebug.Log(
                    $"Image pickup spawned locally; not connected, so it will not replicate yet ({result.Width}x{result.Height}).",
                    LogTag
                );
            }
            return true;
        }

        /// <summary>
        /// Attaches validated decoded animation data to an existing image pickup.
        /// Decoder/network layers can call this after the static poster has spawned.
        /// </summary>
        public bool TrySetAnimation(Guid id, BasisAnimatedImageData data, long playbackEpochUtcTicks = 0)
        {
            if (data == null)
                return false;
            if (
                !_images.TryGetValue(id, out BasisImagePickupObject pickup)
                || pickup == null
                || pickup.AnimatedImagePlayer != null
            )
                return false;

            string reason;
            bool withinBudget = _owned.ContainsKey(id)
                ? CanAttachLocalAnimation(data, false, out reason)
                : CanAttachRemoteAnimation(pickup.OwnerId, data, false, out reason);
            if (!withinBudget)
            {
                BasisDebug.LogWarning($"Image pickup animation could not be attached: {reason}.", LogTag);
                return false;
            }
            return pickup.TrySetAnimation(data, playbackEpochUtcTicks);
        }

        /// <summary>Removes an image for everyone. Any client may call this for any image.</summary>
        public void RequestDespawn(Guid id)
        {
            if (HasNetworkID)
            {
                SendCustomNetworkEventDirect(EncodeDespawn(id), DeliveryMethod.ReliableOrdered, null);
            }
            RemoveImage(id);
        }

        /// <summary>Takes movement authority when this client grabs an image, demoting other clients to followers.</summary>
        public void ClaimControl(Guid id)
        {
            if (!_images.TryGetValue(id, out BasisImagePickupObject pickup) || pickup == null)
                return;
            if (pickup.IsController)
                return;
            pickup.SetController(true);
            if (HasNetworkID)
            {
                SendCustomNetworkEventDirect(EncodeClaim(id), DeliveryMethod.ReliableOrdered, null);
            }
        }

        private void Simulate()
        {
            try
            {
                SimulateBody();
            }
            catch (Exception exception)
            {
                BasisDebug.LogErrorOnce(
                    $"Image pickup manager simulation failed with {_images.Count:N0} images, "
                        + $"{_pendingGifSpawns.Count:N0} active GIF decodes, "
                        + $"{_pendingInboundAnimationDecodes.Count:N0} active inbound animation decodes, "
                        + $"and {_reservedInboundTransferBytes / (1024L * 1024L):N0} MiB reserved: "
                        + $"{exception}",
                    LogTag
                );
            }
        }

        private void SimulateBody()
        {
            CleanupDestroyedImages();
            ProcessCompletedGifSpawns();
            ProcessCompletedInboundAnimationDecodes();
            StartQueuedInboundAnimationDecodes();

            float deltaTime = Time.deltaTime;
            foreach (BasisImagePickupObject pickup in _images.Values)
                pickup?.SimulateRemoteTransform(deltaTime);

            if (!HasNetworkID)
                return;

            float now = Time.unscaledTime;
            float interval = 1f / BasisImagePickupSettings.TransmitTransformHz;

            foreach (KeyValuePair<Guid, BasisImagePickupObject> entry in _images)
            {
                BasisImagePickupObject pickup = entry.Value;
                if (pickup == null || !pickup.IsController)
                    continue;
                if (now - pickup.LastSendTime < interval)
                    continue;

                pickup.transform.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
                float scale = pickup.transform.localScale.x;
                bool moved =
                    (position - pickup.LastSentPosition).sqrMagnitude
                        > BasisImagePickupSettings.MovedPositionEpsilon
                            * BasisImagePickupSettings.MovedPositionEpsilon
                    || Quaternion.Angle(rotation, pickup.LastSentRotation)
                        > BasisImagePickupSettings.MovedRotationEpsilonDegrees
                    || Mathf.Abs(scale - pickup.LastSentScale)
                        > BasisImagePickupSettings.MovedScaleEpsilon;
                if (!moved)
                    continue;

                pickup.LastSendTime = now;
                pickup.LastSentPosition = position;
                pickup.LastSentRotation = rotation;
                pickup.LastSentScale = scale;
                SendCustomNetworkEventDirect(
                    EncodeTransform(entry.Key, position, rotation, scale),
                    DeliveryMethod.ReliableOrdered,
                    null
                );
            }

            ProcessOutboundImageTransfers();
            ProcessOutboundAnimationTransfers();
            CleanupExpiredTransfers(now);
        }

        public override void OnPlayerJoined(BasisNetworkPlayer player)
        {
            if (player == null || _owned.Count == 0)
                return;
            ushort[] recipients = { player.playerId };
            ushort ownerId =
                BasisNetworkPlayer.LocalPlayer != null
                    ? BasisNetworkPlayer.LocalPlayer.playerId
                    : (ushort)0;

            foreach (KeyValuePair<Guid, OwnedImage> entry in _owned)
            {
                OwnedImage owned = entry.Value;
                if (owned.Object == null)
                    continue;
                owned.Object.transform.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
                SendSpawn(
                    entry.Key,
                    ownerId,
                    owned.OwnerName,
                    owned.Width,
                    owned.Height,
                    owned.CleanPng,
                    position,
                    rotation,
                    recipients
                );
                if (owned.AnimationPayload != null && owned.PlaybackEpochUtcTicks > 0)
                    SendAnimation(entry.Key, owned, recipients);
            }
        }

        public override void OnPlayerLeft(BasisNetworkPlayer player)
        {
            if (player == null)
                return;
            ushort left = player.playerId;

            _scratchIds.Clear();
            foreach (KeyValuePair<Guid, BasisImagePickupObject> entry in _images)
            {
                if (entry.Value != null && entry.Value.OwnerId == left)
                    _scratchIds.Add(entry.Key);
            }
            int ownedImageCount = _scratchIds.Count;
            for (int i = 0; i < ownedImageCount; i++)
                RemoveImage(_scratchIds[i]);

            _scratchIds.Clear();
            foreach (KeyValuePair<Guid, InboundTransfer> entry in _inbound)
            {
                if (entry.Value.Sender == left)
                    _scratchIds.Add(entry.Key);
            }
            int inboundTransferCount = _scratchIds.Count;
            for (int i = 0; i < inboundTransferCount; i++)
                RemoveInboundTransfer(_scratchIds[i]);

            _scratchIds.Clear();
            foreach (KeyValuePair<Guid, InboundAnimationTransfer> entry in _inboundAnimations)
            {
                if (entry.Value.Sender == left)
                    _scratchIds.Add(entry.Key);
            }
            int inboundAnimationTransferCount = _scratchIds.Count;
            for (int i = 0; i < inboundAnimationTransferCount; i++)
                RemoveInboundAnimationTransfer(_scratchIds[i]);

            int queuedInboundDecodeCount = _queuedInboundAnimationDecodes.Count;
            for (int i = queuedInboundDecodeCount - 1; i >= 0; i--)
            {
                QueuedInboundAnimationDecode queued = _queuedInboundAnimationDecodes[i];
                if (queued.Sender != left)
                    continue;
                queued.Payload?.Dispose();
                ReleaseInboundTransferBytes(queued.ReservedBytes);
                queued.ReservedBytes = 0;
                _animationAttempted.Remove(queued.Id);
                _queuedInboundAnimationDecodes.RemoveAt(i);
            }

            int pendingInboundDecodeCount = _pendingInboundAnimationDecodes.Count;
            for (int i = pendingInboundDecodeCount - 1; i >= 0; i--)
            {
                PendingInboundAnimationDecode pending = _pendingInboundAnimationDecodes[
                    i
                ];
                if (pending.Sender != left)
                    continue;
                pending.Job?.Dispose();
                pending.Payload?.Dispose();
                ReleaseInboundTransferBytes(pending.ReservedBytes);
                pending.ReservedBytes = 0;
                _animationAttempted.Remove(pending.Id);
                _pendingInboundAnimationDecodes.RemoveAt(i);
            }

            RemoveOutboundImageTransfersForRecipient(left);
            RemoveOutboundAnimationTransfersForRecipient(left);
            _spawnRateBySender.Remove(left);
        }

        public override void OnDirectNetworkMessage(ushort senderId, byte[] buffer, DeliveryMethod deliveryMethod)
        {
            if (buffer == null || buffer.Length < 1)
                return;

            using var stream = new MemoryStream(buffer, false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            byte opcode = reader.ReadByte();
            if (opcode != OpChunk && opcode != OpAnimationChunk)
            {
                BasisDebug.Log(
                    $"Image pickup RX: opcode={opcode} from player {senderId} ({buffer.Length} bytes), my NetworkID={NetworkID}.",
                    LogTag
                );
            }
            try
            {
                switch (opcode)
                {
                    case OpSpawn:
                        HandleSpawn(senderId, reader);
                        break;
                    case OpChunk:
                        HandleChunk(senderId, reader);
                        break;
                    case OpTransform:
                        HandleTransform(senderId, reader);
                        break;
                    case OpClaim:
                        HandleClaim(senderId, reader);
                        break;
                    case OpDespawn:
                        HandleDespawn(reader);
                        break;
                    case OpAnimationSpawn:
                        HandleAnimationSpawn(senderId, reader);
                        break;
                    case OpAnimationChunk:
                        HandleAnimationChunk(senderId, reader);
                        break;
                }
            }
            catch (Exception e)
            {
                BasisDebug.LogWarning($"Image pickup: malformed message from {senderId} ({e.Message}).", LogTag);
            }
        }

        private void HandleSpawn(ushort senderId, BinaryReader reader)
        {
            Guid id = new Guid(reader.ReadBytes(16));
            reader.ReadUInt16();
            if (!TrySkipWireString(reader, MaxIgnoredOwnerNameBytes)) return;
            int width = reader.ReadInt32();
            int height = reader.ReadInt32();
            int totalBytes = reader.ReadInt32();
            int totalChunks = reader.ReadInt32();
            Vector3 position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            Quaternion rotation = new Quaternion(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle()
            );

            if (_images.ContainsKey(id) || _inbound.ContainsKey(id))
                return;

            if (!CanAcceptSpawn(senderId, totalBytes, width, height, totalChunks, out string reason))
            {
                BasisDebug.LogWarning($"Image pickup from {senderId} dropped: {reason}.", LogTag);
                return;
            }

            if (!TryReserveInboundTransferBytes(totalBytes, out reason))
            {
                BasisDebug.LogWarning($"Image pickup from {senderId} dropped: {reason}.", LogTag);
                return;
            }

            try
            {
            _inbound[id] = new InboundTransfer
            {
                Sender = senderId,
                Id = id,
                Buffer = new byte[totalBytes],
                    ReservedBytes = totalBytes,
                Received = new bool[totalChunks],
                ReceivedCount = 0,
                TotalChunks = totalChunks,
                Width = width,
                Height = height,
                OwnerId = senderId,
                OwnerName = ResolveOwnerName(senderId),
                Deadline =
                    Time.unscaledTime
                    + BasisImagePickupSettings.InboundTransferTimeoutSeconds,
                Position = position,
                Rotation = rotation,
            };
        }
            catch
            {
                ReleaseInboundTransferBytes(totalBytes);
                throw;
            }
        }

        private void HandleChunk(ushort senderId, BinaryReader reader)
        {
            Guid id = new Guid(reader.ReadBytes(16));
            int chunkIndex = reader.ReadInt32();
            int length = reader.ReadInt32();

            if (!_inbound.TryGetValue(id, out InboundTransfer transfer))
                return;
            if (transfer.Sender != senderId)
                return;
            if (chunkIndex < 0 || chunkIndex >= transfer.TotalChunks)
                return;
            if (length <= 0 || length > BasisImagePickupSettings.ChunkPayloadBytes)
                return;

            int offset = chunkIndex * BasisImagePickupSettings.ChunkPayloadBytes;
            if (offset < 0 || offset >= transfer.Buffer.Length)
                return;
            int expectedLength = Mathf.Min(BasisImagePickupSettings.ChunkPayloadBytes, transfer.Buffer.Length - offset);
            if (length != expectedLength)
                return;

            long remainingBytes = reader.BaseStream.Length - reader.BaseStream.Position;
            if (remainingBytes < length)
                return;

            byte[] data = reader.ReadBytes(length);
            if (data.Length != length)
                return;

            if (!transfer.Received[chunkIndex])
            {
                transfer.Deadline =
                    Time.unscaledTime
                    + BasisImagePickupSettings.InboundTransferTimeoutSeconds;
                Buffer.BlockCopy(data, 0, transfer.Buffer, offset, length);
                transfer.Received[chunkIndex] = true;
                transfer.ReceivedCount++;
            }

            if (transfer.ReceivedCount >= transfer.TotalChunks)
                FinalizeTransfer(transfer);
        }

        private void FinalizeTransfer(InboundTransfer transfer)
        {
            _inbound.Remove(transfer.Id);
            ReleaseInboundTransferBytes(transfer.ReservedBytes);
            transfer.ReservedBytes = 0;

            BasisImageValidationResult result = BasisImageSecurity.ValidateBytes(transfer.Buffer);
            if (!result.Ok)
            {
                BasisDebug.LogWarning(
                    $"Image pickup from {transfer.Sender} failed validation: {result.Error}.",
                    LogTag
                );
                return;
            }
            if (
                !MatchesClaimedDimensions(
                    transfer.Width,
                    transfer.Height,
                    result.Width,
                    result.Height
                )
            )
            {
                DisposeRejectedValidationResult(ref result);
                BasisDebug.LogWarning(
                    $"Image pickup from {transfer.Sender} failed validation: "
                        + $"claimed {transfer.Width}x{transfer.Height}, decoded "
                        + $"{result.Width}x{result.Height}.",
                    LogTag
                );
                return;
            }
            if (_images.ContainsKey(transfer.Id))
            {
                DisposeRejectedValidationResult(ref result);
                return;
            }

            var pickup = BasisImagePickupObject.Build(
                this,
                transfer.Id,
                transfer.OwnerId,
                transfer.OwnerName,
                false,
                result.Texture,
                result.CleanPng,
                result.HasAlpha,
                transfer.Position,
                transfer.Rotation
            );
            _images[transfer.Id] = pickup;

            Guid imageId = transfer.Id;
            BasisShareableRegistry.Register(
                new BasisShareableEntry
            {
                Id = imageId.ToString(),
                Kind = BasisShareableKind.Image,
                Title = $"{transfer.Width}x{transfer.Height}",
                SharerName = transfer.OwnerName,
                Actions = new List<BasisShareableAction>
                {
                    new BasisShareableAction
                    {
                        Style = BasisShareableActionStyle.Destructive,
                        Invoke = () => { if (Instance != null) Instance.RequestDespawn(imageId); },
                    },
                },
            });
        }

        private void HandleAnimationSpawn(ushort senderId, BinaryReader reader)
        {
            Guid id = new Guid(reader.ReadBytes(16));
            byte format = reader.ReadByte();
            int totalBytes = reader.ReadInt32();
            int totalChunks = reader.ReadInt32();
            long playbackEpochUtcTicks = reader.ReadInt64();

            if (format != AnimationFormatNativeLz4)
            {
                BasisDebug.LogWarningOnce(
                    "BasisImagePickup.UnsupportedAnimationFormat",
                    $"Image pickup ignored unsupported animation format {format} from "
                        + $"player {senderId}.",
                    LogTag
                );
                return;
            }
            if (_inboundAnimations.ContainsKey(id) || _animationAttempted.Contains(id))
                return;
            if (!CanAcceptAnimation(senderId, id, totalBytes, totalChunks, playbackEpochUtcTicks, out string reason))
            {
                BasisDebug.LogWarning($"Image pickup animation from {senderId} dropped: {reason}.", LogTag);
                return;
            }

            if (!TryReserveInboundTransferBytes(totalBytes, out reason))
            {
                BasisDebug.LogWarning($"Image pickup animation from {senderId} dropped: {reason}.", LogTag);
                return;
        }

            NativeArray<byte> buffer = default;
            NativeArray<byte> received = default;
            try
        {
                buffer = new NativeArray<byte>(
                    totalBytes,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory
                );
                received = new NativeArray<byte>(totalChunks, Allocator.Persistent, NativeArrayOptions.ClearMemory);
                _inboundAnimations[id] = new InboundAnimationTransfer
            {
                    Sender = senderId,
                    Id = id,
                    Buffer = buffer,
                    ReservedBytes = totalBytes,
                    Received = received,
                    ReceivedCount = 0,
                    TotalChunks = totalChunks,
                    PlaybackEpochUtcTicks = playbackEpochUtcTicks,
                    Deadline =
                        Time.unscaledTime
                        + BasisImagePickupSettings.InboundTransferTimeoutSeconds,
                };
                buffer = default;
                received = default;
                _animationAttempted.Add(id);
            }
            catch (Exception exception)
            {
                if (buffer.IsCreated)
                    buffer.Dispose();
                if (received.IsCreated)
                    received.Dispose();
                ReleaseInboundTransferBytes(totalBytes);
                BasisDebug.LogWarning(
                    $"Image pickup animation from {senderId} could not allocate its native transfer "
                        + $"buffers ({exception.Message}).",
                    LogTag
                );
            }
        }

        private void HandleAnimationChunk(ushort senderId, BinaryReader reader)
        {
            Guid id = new Guid(reader.ReadBytes(16));
            int chunkIndex = reader.ReadInt32();
            int length = reader.ReadInt32();

            if (!_inboundAnimations.TryGetValue(id, out InboundAnimationTransfer transfer))
                return;
            if (transfer.Sender != senderId)
                return;
            if (chunkIndex < 0 || chunkIndex >= transfer.TotalChunks)
                return;
            if (length <= 0 || length > BasisImagePickupSettings.ChunkPayloadBytes)
                return;

            int offset = chunkIndex * BasisImagePickupSettings.ChunkPayloadBytes;
            if (offset < 0 || offset >= transfer.Buffer.Length)
                return;
            int expectedLength = Mathf.Min(BasisImagePickupSettings.ChunkPayloadBytes, transfer.Buffer.Length - offset);
            if (length != expectedLength)
                return;

            byte[] data = reader.ReadBytes(length);
            if (data.Length != length)
                return;

            if (transfer.Received[chunkIndex] == 0)
            {
                transfer.Deadline =
                    Time.unscaledTime
                    + BasisImagePickupSettings.InboundTransferTimeoutSeconds;
                NativeArray<byte>.Copy(data, 0, transfer.Buffer, offset, length);
                transfer.Received[chunkIndex] = 1;
                transfer.ReceivedCount++;
            }

            if (transfer.ReceivedCount >= transfer.TotalChunks)
                FinalizeAnimationTransfer(transfer);
        }

        private void FinalizeAnimationTransfer(InboundAnimationTransfer transfer)
        {
            _inboundAnimations.Remove(transfer.Id);
            if (transfer.Received.IsCreated)
                transfer.Received.Dispose();

            if (
                !_images.TryGetValue(transfer.Id, out BasisImagePickupObject pickup)
                || pickup == null
                || pickup.OwnerId != transfer.Sender
                || pickup.AnimatedImagePlayer != null
            )
            {
                if (transfer.Buffer.IsCreated)
                    transfer.Buffer.Dispose();
                ReleaseInboundTransferBytes(transfer.ReservedBytes);
                transfer.ReservedBytes = 0;
                _animationAttempted.Remove(transfer.Id);
                return;
            }

            if (
                !BasisBurstAnimationCodec.TryReadOuterHeader(
                    transfer.Buffer,
                    transfer.Buffer.Length,
                    out int decodedBytes,
                    out string headerError
                )
            )
            {
                if (transfer.Buffer.IsCreated)
                    transfer.Buffer.Dispose();
                ReleaseInboundTransferBytes(transfer.ReservedBytes);
                transfer.ReservedBytes = 0;
                _animationAttempted.Remove(transfer.Id);
                BasisDebug.LogWarning($"Image pickup animation from {transfer.Sender} dropped: {headerError}", LogTag);
                return;
            }
            if (!FitsInboundAnimationDecodeBudget(0, 0, decodedBytes))
            {
                if (transfer.Buffer.IsCreated)
                    transfer.Buffer.Dispose();
                ReleaseInboundTransferBytes(transfer.ReservedBytes);
                transfer.ReservedBytes = 0;
                _animationAttempted.Remove(transfer.Id);
                BasisDebug.LogWarning(
                    $"Image pickup animation from {transfer.Sender} dropped: decoded "
                        + "payload exceeds the per-sender native decode limit.",
                    LogTag
                );
                return;
            }

            int payloadBytes = transfer.Buffer.Length;
            if (!BasisNativeAnimationPayload.TryReserveBytes(payloadBytes, out string payloadError))
            {
                transfer.Buffer.Dispose();
                ReleaseInboundTransferBytes(transfer.ReservedBytes);
                transfer.ReservedBytes = 0;
                _animationAttempted.Remove(transfer.Id);
                BasisDebug.LogWarning(
                    $"Image pickup animation from {transfer.Sender} dropped: {payloadError}",
                    LogTag
                );
                return;
            }

            BasisNativeAnimationPayload payload = null;
            bool payloadReservationHeld = true;
            try
            {
                payload = new BasisNativeAnimationPayload(transfer.Buffer, payloadBytes, true);
                payloadReservationHeld = false;
                transfer.Buffer = default;
                _queuedInboundAnimationDecodes.Add(
                    new QueuedInboundAnimationDecode
                    {
                        Sender = transfer.Sender,
                        Id = transfer.Id,
                        Payload = payload,
                        ReservedBytes = transfer.ReservedBytes,
                        PayloadBytes = payloadBytes,
                        DecodedBytes = decodedBytes,
                        PlaybackEpochUtcTicks = transfer.PlaybackEpochUtcTicks,
                    }
                );
                payload = null;
                transfer.ReservedBytes = 0;
            }
            catch (Exception exception)
            {
                payload?.Dispose();
                if (payloadReservationHeld)
                    BasisNativeAnimationPayload.ReleaseReservation(payloadBytes);
                if (transfer.Buffer.IsCreated)
                    transfer.Buffer.Dispose();
                ReleaseInboundTransferBytes(transfer.ReservedBytes);
                transfer.ReservedBytes = 0;
                _animationAttempted.Remove(transfer.Id);
                BasisDebug.LogWarning(
                    $"Image pickup animation from {transfer.Sender} could not enter "
                        + $"the decode queue ({exception.Message}).",
                    LogTag
                );
                return;
            }

            BasisDebug.Log(
                $"Image pickup animation from {transfer.Sender} queued for Burst decode "
                    + $"({_queuedInboundAnimationDecodes.Count:N0} waiting, "
                    + $"{_pendingInboundAnimationDecodes.Count:N0} active).",
                LogTag
            );
            StartQueuedInboundAnimationDecodes();
        }

        private void StartQueuedInboundAnimationDecodes()
        {
            int queuedDecodeCount = _queuedInboundAnimationDecodes.Count;
            for (int index = 0; index < queuedDecodeCount; )
            {
                if (_pendingInboundAnimationDecodes.Count >= BasisImagePickupSettings.MaxConcurrentAnimationDecodeJobs)
                {
                    return;
                }

                QueuedInboundAnimationDecode queued = _queuedInboundAnimationDecodes[
                    index
                ];
                if (!TryGetAcceptedInboundAnimationPickup(queued.Sender, queued.Id, out BasisImagePickupObject pickup))
                {
                    RemoveQueuedInboundAnimationDecode(index);
                    queuedDecodeCount--;
                    continue;
                }

                if (!CanStartInboundAnimationDecode(queued.Sender, queued.DecodedBytes))
                {
                    index++;
                    continue;
                }

                _queuedInboundAnimationDecodes.RemoveAt(index);
                queuedDecodeCount--;
                BasisBurstAnimationDecodeRequest job = null;
                try
                {
                    job = new BasisBurstAnimationDecodeRequest(
                        queued.Payload.Bytes,
                        queued.Payload.Length,
                        false
                    );
                    _pendingInboundAnimationDecodes.Add(
                        new PendingInboundAnimationDecode
                        {
                            Sender = queued.Sender,
                            Id = queued.Id,
                            Payload = queued.Payload,
                            ReservedBytes = queued.ReservedBytes,
                            PayloadBytes = queued.PayloadBytes,
                            DecodedBytes = queued.DecodedBytes,
                            PlaybackEpochUtcTicks = queued.PlaybackEpochUtcTicks,
                            Job = job,
                        }
                    );
                    queued.Payload = null;
                    job = null;
                }
                catch (BasisAnimationMemoryBudgetException)
                {
                    job?.Dispose();
                    _queuedInboundAnimationDecodes.Insert(index, queued);
                    return;
                }
                catch (Exception exception)
                {
                    job?.Dispose();
                    queued.Payload?.Dispose();
                    ReleaseInboundTransferBytes(queued.ReservedBytes);
                    queued.ReservedBytes = 0;
                    _animationAttempted.Remove(queued.Id);
                    BasisDebug.LogWarning(
                        $"Image pickup animation from {queued.Sender} could not schedule "
                            + $"a Burst decode ({exception.Message}).",
                        LogTag
                    );
                }
            }
        }

        private bool TryGetAcceptedInboundAnimationPickup(ushort sender, Guid id, out BasisImagePickupObject pickup)
        {
            // The administrator lock blocks new image/animation headers. It does not remove
            // existing pickups, so work accepted before the lock must be allowed to finish.
            bool imageExists = _images.TryGetValue(id, out pickup) && pickup != null;
            return ShouldContinueAcceptedInboundAnimation(
                BasisImagePickupSettings.ReceiveEnabled,
                imageExists,
                imageExists && pickup.OwnerId == sender,
                imageExists && pickup.AnimatedImagePlayer != null
            );
        }

        internal static bool ShouldContinueAcceptedInboundAnimation(
            bool receiveEnabled,
            bool imageExists,
            bool ownerMatches,
            bool animationAlreadyAttached
        )
        {
            return receiveEnabled
                && imageExists
                && ownerMatches
                && !animationAlreadyAttached;
        }

        private bool CanStartInboundAnimationDecode(ushort sender, int decodedBytes)
        {
            int pendingForSender = 0;
            long pendingDecodedBytes = 0;
            int pendingDecodeCount = _pendingInboundAnimationDecodes.Count;
            for (int i = 0; i < pendingDecodeCount; i++)
            {
                PendingInboundAnimationDecode pending = _pendingInboundAnimationDecodes[
                    i
                ];
                if (pending.Sender != sender)
                    continue;
                pendingForSender++;
                pendingDecodedBytes += pending.DecodedBytes;
            }

            return FitsInboundAnimationDecodeBudget(pendingForSender, pendingDecodedBytes, decodedBytes);
        }

        internal static bool FitsInboundAnimationDecodeBudget(
            int pendingJobs,
            long pendingDecodedBytes,
            int candidateDecodedBytes
        )
        {
            if (pendingJobs < 0 || pendingDecodedBytes < 0 || candidateDecodedBytes < 0)
            {
                return false;
            }

            long decodedByteLimit =
                BasisImagePickupSettings.MaxPendingInboundAnimationDecodedBytesPerSender;
            return pendingJobs
                    < BasisImagePickupSettings.MaxPendingInboundAnimationDecodeJobsPerSender
                && candidateDecodedBytes <= decodedByteLimit
                && pendingDecodedBytes <= decodedByteLimit - candidateDecodedBytes;
        }

        private void RemoveQueuedInboundAnimationDecode(int index)
        {
            QueuedInboundAnimationDecode queued = _queuedInboundAnimationDecodes[index];
            _queuedInboundAnimationDecodes.RemoveAt(index);
            queued.Payload?.Dispose();
            ReleaseInboundTransferBytes(queued.ReservedBytes);
            queued.ReservedBytes = 0;
            _animationAttempted.Remove(queued.Id);
        }

        private void ProcessCompletedInboundAnimationDecodes()
        {
            int pendingDecodeCount = _pendingInboundAnimationDecodes.Count;
            for (int index = pendingDecodeCount - 1; index >= 0; index--)
            {
                PendingInboundAnimationDecode pending = _pendingInboundAnimationDecodes[
                    index
                ];
                BasisBurstAnimationDecodeResult workerResult;
                try
                {
                    if (!pending.Job.TryComplete(out workerResult))
                        continue;
                }
                catch (Exception exception)
                {
                    _pendingInboundAnimationDecodes.RemoveAt(index);
                    _animationAttempted.Remove(pending.Id);
                    DisposeRequest(pending.Job, "inbound animation decode request");
                    pending.Job = null;
                    pending.Payload?.Dispose();
                    pending.Payload = null;
                    ReleaseInboundTransferBytes(pending.ReservedBytes);
                    pending.ReservedBytes = 0;
                    BasisDebug.LogWarning(
                        $"Image pickup animation from {pending.Sender} failed while completing "
                            + $"Burst validation: {exception.GetBaseException().Message}.",
                        LogTag
                    );
                    continue;
                }
                _pendingInboundAnimationDecodes.RemoveAt(index);
                _animationAttempted.Remove(pending.Id);

                try
                {
                    if (
                        !TryGetAcceptedInboundAnimationPickup(
                            pending.Sender,
                            pending.Id,
                            out BasisImagePickupObject pickup
                        )
                    )
                    {
                        continue;
                    }

                    if (workerResult == null || !workerResult.Ok || workerResult.Animation == null)
                    {
                        BasisDebug.LogWarning(
                            $"Image pickup animation from {pending.Sender} failed Burst validation: "
                                + $"{workerResult?.Error ?? "no result"}.",
                            LogTag
                        );
                        continue;
                    }

                    BasisAnimatedImageData animation = workerResult.Animation;
                    int frameCount = animation.FrameCount;
                    if (!CanAttachRemoteAnimation(pending.Sender, animation, true, out string budgetReason))
                    {
                        BasisDebug.LogWarning(
                            $"Image pickup animation from {pending.Sender} dropped: "
                                + $"{budgetReason}.",
                            LogTag
                        );
                        continue;
                    }

                    _remoteAnimationPayloads.Add(pending.Id, pending.Payload);
                    try
                    {
                        if (
                            !pickup.TrySetAnimation(
                                animation,
                                pending.PlaybackEpochUtcTicks,
                                pending.Payload
                            )
                        )
                        {
                            _remoteAnimationPayloads.Remove(pending.Id);
                            BasisDebug.LogWarning(
                                $"Image pickup animation from {pending.Sender} could not be "
                                    + "attached to its poster.",
                                LogTag
                            );
                            continue;
                        }
                    }
                    catch
                    {
                        _remoteAnimationPayloads.Remove(pending.Id);
                        throw;
                    }

                    pending.Payload = null;
                    workerResult.TakeAnimation();
                    double workerMilliseconds =
                        workerResult.WorkerElapsedTicks * 1000d / Stopwatch.Frequency;
                    bool decodedDataDeferred = pickup.AnimatedImagePlayer?.Data == null;
                    string replicationMessage = decodedDataDeferred
                        ? $"Image pickup animation from {pending.Sender} retained as a compact payload "
                            + "and deferred to the closest-animation decoded-data budget "
                            + $"({frameCount} frames, {pending.PayloadBytes} bytes, decoded by "
                            + $"Burst in {workerMilliseconds:0.###} ms)."
                        : $"Image pickup animation replicated from {pending.Sender} "
                            + $"({frameCount} frames, {pending.PayloadBytes} bytes, decoded by "
                            + $"Burst in {workerMilliseconds:0.###} ms).";
                    BasisDebug.Log(replicationMessage, LogTag);
                }
                finally
                {
                    workerResult?.TakeAnimation()?.Dispose();
                    DisposeRequest(pending.Job, "inbound animation decode request");
                    pending.Job = null;
                    pending.Payload?.Dispose();
                    pending.Payload = null;
                    ReleaseInboundTransferBytes(pending.ReservedBytes);
                    pending.ReservedBytes = 0;
                }
            }
        }

        private void HandleTransform(ushort senderId, BinaryReader reader)
        {
            Guid id = new Guid(reader.ReadBytes(16));
            Vector3 position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            Quaternion rotation = new Quaternion(
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle()
            );
            float scale = reader.ReadSingle();

            if (_images.TryGetValue(id, out BasisImagePickupObject pickup) && pickup != null && !pickup.IsController)
            {
                pickup.SetRemoteTarget(position, rotation, scale);
            }
        }

        private void HandleClaim(ushort senderId, BinaryReader reader)
        {
            Guid id = new Guid(reader.ReadBytes(16));
            if (_images.TryGetValue(id, out BasisImagePickupObject pickup) && pickup != null)
            {
                pickup.SetController(false);
            }
        }

        private void HandleDespawn(BinaryReader reader)
        {
            Guid id = new Guid(reader.ReadBytes(16));
            RemoveImage(id);
        }

        private bool CanAcceptSpawn(
            ushort sender,
            int totalBytes,
            int width,
            int height,
            int totalChunks,
            out string reason
        )
        {
            reason = null;
            if (BasisNetworkModeration.GlobalImagesLocked && !BasisNetworkModeration.LocalPlayerHasGlobalLockBypass())
            {
                reason = "shared images locked by admin";
                return false;
            }
            if (!BasisImagePickupSettings.ReceiveEnabled)
            {
                reason = "receiving disabled";
                return false;
            }
            if (totalBytes <= 0 || totalBytes > BasisImagePickupSettings.MaxImageBytes)
            {
                reason = "size";
                return false;
            }
            if (
                width <= 0
                || height <= 0
                || width > BasisImagePickupSettings.MaxDimension
                || height > BasisImagePickupSettings.MaxDimension
            )
            {
                reason = "dimensions";
                return false;
            }
            if ((long)width * height > BasisImagePickupSettings.MaxTotalPixels)
            {
                reason = "pixel budget";
                return false;
            }

            int expectedChunks =
                (totalBytes + BasisImagePickupSettings.ChunkPayloadBytes - 1)
                / BasisImagePickupSettings.ChunkPayloadBytes;
            if (totalChunks != expectedChunks)
            {
                reason = "chunk count";
                return false;
            }

            int imageCount = 1;
            int activeTransfers = 0;
            long aggregatePixels = (long)width * height;
            long aggregateBytes = totalBytes;

            foreach (BasisImagePickupObject pickup in _images.Values)
            {
                if (pickup == null || pickup.OwnerId != sender)
                    continue;
                imageCount++;
                aggregatePixels += pickup.PosterPixelCount;
                aggregateBytes += pickup.CleanPng?.Length ?? 0;
            }
            foreach (InboundTransfer transfer in _inbound.Values)
            {
                if (transfer.Sender != sender)
                    continue;
                imageCount++;
                activeTransfers++;
                aggregatePixels += (long)transfer.Width * transfer.Height;
                aggregateBytes += transfer.Buffer?.Length ?? 0;
            }
            foreach (InboundAnimationTransfer transfer in _inboundAnimations.Values)
            {
                if (transfer.Sender == sender)
                    activeTransfers++;
            }

            if (!IsWithinRemoteImageBudget(imageCount, aggregatePixels, aggregateBytes, out reason))
            {
                return false;
            }
            if (activeTransfers >= BasisImagePickupSettings.MaxInboundTransfersPerSender)
            {
                reason = "too many transfers";
                return false;
            }

            if (!TryConsumeSpawnRateToken(sender, Time.unscaledTime))
            {
                reason = "rate limit";
                return false;
            }
            return true;
        }

        private bool CanAcceptLocalImage(int totalBytes, int width, int height, out string reason)
        {
            int imageCount = 1;
            long aggregatePixels = (long)width * height;
            long aggregateBytes = totalBytes;
            foreach (OwnedImage owned in _owned.Values)
            {
                if (owned?.Object == null)
                    continue;
                imageCount++;
                aggregatePixels += owned.Object.PosterPixelCount;
                aggregateBytes += owned.CleanPng?.Length ?? 0;
            }

            return IsWithinRemoteImageBudget(imageCount, aggregatePixels, aggregateBytes, out reason);
        }

        internal static bool MatchesClaimedDimensions(
            int claimedWidth,
            int claimedHeight,
            int decodedWidth,
            int decodedHeight
        )
        {
            return claimedWidth == decodedWidth && claimedHeight == decodedHeight;
        }

        internal static bool IsWithinRemoteImageBudget(
            int imageCount,
            long aggregatePixels,
            long aggregateBytes,
            out string reason
        )
        {
            if (imageCount > BasisImagePickupSettings.MaxConcurrentImagesPerSender)
            {
                reason =
                    $"image count limit ({BasisImagePickupSettings.MaxConcurrentImagesPerSender})";
                return false;
            }
            if (aggregatePixels > BasisImagePickupSettings.MaxRemoteImagePixelsPerSender)
            {
                reason = "aggregate image pixel budget";
                return false;
            }
            if (aggregateBytes > BasisImagePickupSettings.MaxRemoteImageBytesPerSender)
            {
                reason = "aggregate image byte budget";
                return false;
            }

            reason = null;
            return true;
        }

        internal static bool FitsInboundTransferBudget(long reservedBytes, long candidateBytes)
        {
            return reservedBytes >= 0
                && candidateBytes > 0
                && reservedBytes <= BasisImagePickupSettings.MaxInboundTransferBytes
                && candidateBytes
                    <= BasisImagePickupSettings.MaxInboundTransferBytes - reservedBytes;
        }

        private bool TryReserveInboundTransferBytes(long bytes, out string reason)
        {
            if (!FitsInboundTransferBudget(_reservedInboundTransferBytes, bytes))
            {
                reason = "global inbound transfer memory budget";
                return false;
            }

            _reservedInboundTransferBytes = checked(_reservedInboundTransferBytes + bytes);
            reason = null;
            return true;
        }

        private void ReleaseInboundTransferBytes(long bytes)
        {
            if (bytes <= 0)
                return;
            _reservedInboundTransferBytes = Math.Max(0, _reservedInboundTransferBytes - bytes);
        }

        private bool TryConsumeSpawnRateToken(ushort sender, float now)
        {
            float interval = BasisImagePickupSettings.MinSecondsBetweenSpawnsPerSender;
            if (interval <= 0f)
                return true;

            if (!_spawnRateBySender.TryGetValue(sender, out SpawnRateLimitState state))
            {
                state = new SpawnRateLimitState
                {
                    Tokens = BasisImagePickupSettings.SpawnRateBurstAllowance,
                    LastRefillTime = now,
                };
                _spawnRateBySender[sender] = state;
            }
            else
            {
                float elapsed = Mathf.Max(0f, now - state.LastRefillTime);
                state.Tokens = Mathf.Min(
                    BasisImagePickupSettings.SpawnRateBurstAllowance,
                    state.Tokens + elapsed / interval
                );
                state.LastRefillTime = now;
            }

            if (state.Tokens < 1f)
                return false;
            state.Tokens -= 1f;
            return true;
        }

        private bool CanAcceptAnimation(
            ushort sender,
            Guid id,
            int totalBytes,
            int totalChunks,
            long playbackEpochUtcTicks,
            out string reason
        )
        {
            reason = null;
            if (BasisNetworkModeration.GlobalImagesLocked && !BasisNetworkModeration.LocalPlayerHasGlobalLockBypass())
            {
                reason = "shared images locked by admin";
                return false;
            }
            if (!BasisImagePickupSettings.ReceiveEnabled)
            {
                reason = "receiving disabled";
                return false;
            }
            if (totalBytes <= 0 || totalBytes > BasisImagePickupSettings.MaxAnimationNetworkBytes)
            {
                reason = "animation size";
                return false;
            }
            if (playbackEpochUtcTicks <= 0)
            {
                reason = "playback epoch";
                return false;
            }

            int expectedChunks =
                (totalBytes + BasisImagePickupSettings.ChunkPayloadBytes - 1)
                / BasisImagePickupSettings.ChunkPayloadBytes;
            if (totalChunks != expectedChunks)
            {
                reason = "animation chunk count";
                return false;
            }

            if (!_images.TryGetValue(id, out BasisImagePickupObject pickup) || pickup == null)
            {
                reason = "poster is unavailable";
                return false;
            }
            if (pickup.OwnerId != sender)
            {
                reason = "sender does not own the image";
                return false;
            }
            if (pickup.AnimatedImagePlayer != null)
            {
                reason = "animation is already attached";
                return false;
            }

            int activeTransfers = 0;
            long activeTransferBytes = totalBytes;
            foreach (KeyValuePair<Guid, BasisNativeAnimationPayload> entry in _remoteAnimationPayloads)
            {
                if (
                    entry.Value == null
                    || !_images.TryGetValue(entry.Key, out BasisImagePickupObject retainedPickup)
                    || retainedPickup == null
                    || retainedPickup.OwnerId != sender
                )
                {
                    continue;
                }
                activeTransferBytes += entry.Value.Length;
            }
            foreach (InboundAnimationTransfer transfer in _inboundAnimations.Values)
            {
                if (transfer.Sender != sender)
                    continue;
                activeTransfers++;
                activeTransferBytes += transfer.Buffer.Length;
            }
            foreach (InboundTransfer transfer in _inbound.Values)
            {
                if (transfer.Sender == sender)
                    activeTransfers++;
            }
            int queuedInboundDecodeCount = _queuedInboundAnimationDecodes.Count;
            for (int i = 0; i < queuedInboundDecodeCount; i++)
            {
                QueuedInboundAnimationDecode queued = _queuedInboundAnimationDecodes[i];
                if (queued.Sender == sender)
                    activeTransferBytes += queued.PayloadBytes;
            }
            int pendingInboundDecodeCount = _pendingInboundAnimationDecodes.Count;
            for (int i = 0; i < pendingInboundDecodeCount; i++)
            {
                PendingInboundAnimationDecode pending = _pendingInboundAnimationDecodes[
                    i
                ];
                if (pending.Sender == sender)
                    activeTransferBytes += pending.PayloadBytes;
            }
            if (activeTransfers >= BasisImagePickupSettings.MaxInboundTransfersPerSender)
            {
                reason = "too many animation transfers";
                return false;
            }
            if (activeTransferBytes > BasisImagePickupSettings.MaxInboundAnimationNetworkBytesPerSender)
            {
                reason = "aggregate animation payload budget";
                return false;
            }

            return true;
        }

        private bool CanAttachLocalAnimation(
            BasisAnimatedImageData candidate,
            bool reloadable,
            out string reason
        )
        {
            if (
                !reloadable
                && BasisAnimatedImageData.TotalResidentNativeBytes
                    > BasisImagePickupSettings.MaxResidentAnimationNativeBytes
            )
            {
                reason = "global resident animation memory budget exceeded";
                return false;
            }

            long decodedFramePixels = reloadable ? 0 : candidate.DecodedFramePixels;
            long canvasPixels = (long)candidate.CanvasWidth * candidate.CanvasHeight;
            foreach (OwnedImage owned in _owned.Values)
            {
                BasisAnimatedImagePlayer existing = owned?.Object?.AnimatedImagePlayer;
                if (existing == null)
                    continue;
                if (!reloadable)
                    decodedFramePixels += existing.DecodedFramePixels;
                canvasPixels += existing.CanvasPixels;
            }

            return reloadable
                ? IsWithinRemoteAnimationCanvasBudget(canvasPixels, out reason)
                : IsWithinRemoteAnimationBudget(decodedFramePixels, canvasPixels, out reason);
        }

        private bool CanAttachRemoteAnimation(
            ushort sender,
            BasisAnimatedImageData candidate,
            bool reloadable,
            out string reason
        )
        {
            if (
                !reloadable
                && BasisAnimatedImageData.TotalResidentNativeBytes
                    > BasisImagePickupSettings.MaxResidentAnimationNativeBytes
            )
            {
                reason = "global resident animation memory budget exceeded";
                return false;
            }

            long decodedFramePixels = reloadable ? 0 : candidate.DecodedFramePixels;
            long canvasPixels = (long)candidate.CanvasWidth * candidate.CanvasHeight;

            foreach (BasisImagePickupObject pickup in _images.Values)
            {
                if (pickup == null || pickup.OwnerId != sender || pickup.AnimatedImagePlayer == null)
                    continue;

                BasisAnimatedImagePlayer existing = pickup.AnimatedImagePlayer;
                if (!reloadable)
                    decodedFramePixels += existing.DecodedFramePixels;
                canvasPixels += existing.CanvasPixels;
            }

            return reloadable
                ? IsWithinRemoteAnimationCanvasBudget(canvasPixels, out reason)
                : IsWithinRemoteAnimationBudget(decodedFramePixels, canvasPixels, out reason);
        }

        internal static bool IsWithinRemoteAnimationBudget(
            long decodedFramePixels,
            long canvasPixels,
            out string reason
        )
        {
            if (decodedFramePixels > BasisImagePickupSettings.MaxRemoteAnimationDecodedFramePixelsPerSender)
            {
                reason = "aggregate decoded animation pixel budget exceeded";
                return false;
            }
            if (canvasPixels > BasisImagePickupSettings.MaxRemoteAnimationCanvasPixelsPerSender)
            {
                reason = "aggregate animation canvas budget exceeded";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool IsWithinRemoteAnimationCanvasBudget(long canvasPixels, out string reason)
        {
            if (canvasPixels > BasisImagePickupSettings.MaxRemoteAnimationCanvasPixelsPerSender)
            {
                reason = "aggregate animation canvas budget exceeded";
                return false;
            }

            reason = null;
            return true;
        }

        private void DisposeRejectedValidationResult(ref BasisImageValidationResult result)
        {
            if (result.Texture != null)
                Destroy(result.Texture);
            result.Texture = null;
            result.TakeAnimation()?.Dispose();
            result.TakeAnimationPayload()?.Dispose();
        }

        private void CleanupExpiredTransfers(float now)
        {
            if (_inbound.Count > 0)
            {
                _scratchIds.Clear();
                foreach (KeyValuePair<Guid, InboundTransfer> entry in _inbound)
                {
                    if (now >= entry.Value.Deadline)
                        _scratchIds.Add(entry.Key);
                }
                int expiredTransferCount = _scratchIds.Count;
                for (int i = 0; i < expiredTransferCount; i++)
                    RemoveInboundTransfer(_scratchIds[i]);
            }

            if (_inboundAnimations.Count > 0)
            {
                _scratchIds.Clear();
                foreach (KeyValuePair<Guid, InboundAnimationTransfer> entry in _inboundAnimations)
                {
                    if (now >= entry.Value.Deadline)
                        _scratchIds.Add(entry.Key);
                }
                int expiredAnimationCount = _scratchIds.Count;
                for (int i = 0; i < expiredAnimationCount; i++)
                    RemoveInboundAnimationTransfer(_scratchIds[i]);
            }
        }

        private void RemoveInboundTransfer(Guid id)
        {
            if (!_inbound.TryGetValue(id, out InboundTransfer transfer))
                return;
            _inbound.Remove(id);
            ReleaseInboundTransferBytes(transfer.ReservedBytes);
            transfer.ReservedBytes = 0;
        }

        private void RemoveInboundAnimationTransfer(Guid id)
        {
            if (!_inboundAnimations.TryGetValue(id, out InboundAnimationTransfer transfer))
                return;
            _inboundAnimations.Remove(id);
            _animationAttempted.Remove(id);
            if (transfer.Buffer.IsCreated)
                transfer.Buffer.Dispose();
            if (transfer.Received.IsCreated)
                transfer.Received.Dispose();
            ReleaseInboundTransferBytes(transfer.ReservedBytes);
            transfer.ReservedBytes = 0;
        }

        /// <summary>Cleans manager-owned state when a scene unload destroys a pickup directly.</summary>
        internal void OnPickupDestroyed(BasisImagePickupObject pickup)
        {
            if (_destroying || ReferenceEquals(pickup, null))
                return;
            if (
                !_images.TryGetValue(pickup.ImageId, out BasisImagePickupObject tracked)
                || !ReferenceEquals(tracked, pickup)
            )
                return;
            RemoveImage(pickup.ImageId, false);
        }

        private void CleanupDestroyedImages()
        {
            _scratchIds.Clear();
            foreach (KeyValuePair<Guid, BasisImagePickupObject> entry in _images)
            {
                if (entry.Value == null)
                    _scratchIds.Add(entry.Key);
            }
            int destroyedImageCount = _scratchIds.Count;
            for (int i = 0; i < destroyedImageCount; i++)
                RemoveImage(_scratchIds[i], false);
            _scratchIds.Clear();
        }

        private void RemoveImage(Guid id, bool destroyPickup = true)
        {
            _images.TryGetValue(id, out BasisImagePickupObject pickup);
            _images.Remove(id);

            if (pickup != null)
            {
                BasisAnimatedImagePlayer player = pickup.AnimatedImagePlayer;
                if (player != null)
                {
                    player.ClearReloadPayload();
                    player.DisposeOwnedResources();
                }
            }

            RemoveOutboundImageTransfers(id);
            RemoveOutboundAnimationTransfers(id);
            if (_owned.TryGetValue(id, out OwnedImage owned))
                owned.AnimationPayload?.Dispose();
            _owned.Remove(id);
            if (_remoteAnimationPayloads.TryGetValue(id, out BasisNativeAnimationPayload remotePayload))
                remotePayload?.Dispose();
            _remoteAnimationPayloads.Remove(id);
            RemoveInboundTransfer(id);
            RemoveInboundAnimationTransfer(id);
            int queuedInboundDecodeCount = _queuedInboundAnimationDecodes.Count;
            for (int i = queuedInboundDecodeCount - 1; i >= 0; i--)
            {
                if (_queuedInboundAnimationDecodes[i].Id != id)
                    continue;
                RemoveQueuedInboundAnimationDecode(i);
            }
            int pendingInboundDecodeCount = _pendingInboundAnimationDecodes.Count;
            for (int i = pendingInboundDecodeCount - 1; i >= 0; i--)
            {
                if (_pendingInboundAnimationDecodes[i].Id != id)
                    continue;
                PendingInboundAnimationDecode pending = _pendingInboundAnimationDecodes[i];
                pending.Job?.Dispose();
                pending.Payload?.Dispose();
                ReleaseInboundTransferBytes(pending.ReservedBytes);
                pending.ReservedBytes = 0;
                _pendingInboundAnimationDecodes.RemoveAt(i);
            }
            _animationAttempted.Remove(id);
            BasisShareableRegistry.Unregister(id.ToString());

            if (!destroyPickup || pickup == null)
                return;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                DestroyImmediate(pickup.gameObject);
            else
#endif
                Destroy(pickup.gameObject);
        }

        private void SendSpawn(
            Guid id,
            ushort ownerId,
            string ownerName,
            int width,
            int height,
            byte[] png,
            Vector3 position,
            Quaternion rotation,
            ushort[] recipients
        )
        {
            if (png == null || png.Length <= 0)
                return;
            _outboundImages.Enqueue(
                new OutboundImageTransfer
        {
                    Id = id,
                    OwnerId = ownerId,
                    OwnerName = ownerName,
                    Width = width,
                    Height = height,
                    Png = png,
                    NextChunkIndex = 0,
                    Position = position,
                    Rotation = rotation,
                    Recipients = recipients,
                    HeaderSent = false,
                }
            );
        }

        private void ProcessOutboundImageTransfers()
        {
            int chunksRemaining =
                BasisImagePickupSettings.MaxImageNetworkChunksPerFrame;
            while (chunksRemaining > 0 && _outboundImages.Count > 0)
            {
                OutboundImageTransfer transfer = _outboundImages.Peek();
                if (
                    !_owned.TryGetValue(transfer.Id, out OwnedImage owned)
                    || owned.Object == null
                    || !ReferenceEquals(owned.CleanPng, transfer.Png)
                )
                {
                    _outboundImages.Dequeue();
                    continue;
                }

                int chunkSize = BasisImagePickupSettings.ChunkPayloadBytes;
                int totalChunks = (transfer.Png.Length + chunkSize - 1) / chunkSize;
                if (!transfer.HeaderSent)
                {
                    owned.Object.transform.GetPositionAndRotation(out transfer.Position, out transfer.Rotation);
            SendCustomNetworkEventDirect(
                EncodeSpawn(
                            transfer.Id,
                            transfer.OwnerId,
                            transfer.OwnerName,
                            transfer.Width,
                            transfer.Height,
                            transfer.Png.Length,
                    totalChunks,
                            transfer.Position,
                            transfer.Rotation
                ),
                DeliveryMethod.ReliableOrdered,
                        transfer.Recipients
            );
                    transfer.HeaderSent = true;
                }

                while (chunksRemaining > 0 && transfer.NextChunkIndex < totalChunks)
            {
                    int offset = transfer.NextChunkIndex * chunkSize;
                    int length = Mathf.Min(chunkSize, transfer.Png.Length - offset);
                SendCustomNetworkEventDirect(
                        EncodeChunk(
                            transfer.Id,
                            transfer.NextChunkIndex,
                            transfer.Png,
                            offset,
                            length
                        ),
                    DeliveryMethod.ReliableOrdered,
                        transfer.Recipients
                );
                    transfer.NextChunkIndex++;
                    chunksRemaining--;
                }

                if (transfer.NextChunkIndex >= totalChunks)
                {
                    owned.Object.transform.GetPositionAndRotation(
                        out Vector3 finalPosition,
                        out Quaternion finalRotation
                    );
                    SendCustomNetworkEventDirect(
                        EncodeTransform(
                            transfer.Id,
                            finalPosition,
                            finalRotation,
                            owned.Object.transform.localScale.x
                        ),
                        DeliveryMethod.ReliableOrdered,
                        transfer.Recipients
                    );
                    _outboundImages.Dequeue();
                }
            }
        }

        private bool HasPendingOutboundImageTransfer(Guid id)
        {
            foreach (OutboundImageTransfer transfer in _outboundImages)
            {
                if (transfer.Id == id)
                    return true;
            }
            return false;
        }

        private void RemoveOutboundImageTransfers(Guid id)
        {
            int count = _outboundImages.Count;
            for (int i = 0; i < count; i++)
            {
                OutboundImageTransfer transfer = _outboundImages.Dequeue();
                if (transfer.Id != id)
                    _outboundImages.Enqueue(transfer);
            }
        }

        private void RemoveOutboundImageTransfersForRecipient(ushort recipient)
        {
            int count = _outboundImages.Count;
            for (int i = 0; i < count; i++)
            {
                OutboundImageTransfer transfer = _outboundImages.Dequeue();
                if (transfer.Recipients == null || Array.IndexOf(transfer.Recipients, recipient) < 0)
                {
                    _outboundImages.Enqueue(transfer);
                }
            }
        }

        private void SendAnimation(Guid id, OwnedImage owned, ushort[] recipients)
        {
            if (
                owned == null
                || owned.AnimationPayload == null
                || !owned.AnimationPayload.IsCreated
                || owned.AnimationPayload.Length <= 0
                || owned.AnimationPayload.Length
                    > BasisImagePickupSettings.MaxAnimationNetworkBytes
                || owned.PlaybackEpochUtcTicks <= 0
            )
            {
                return;
            }

            _outboundAnimations.Enqueue(
                new OutboundAnimationTransfer
                {
                    Id = id,
                    Payload = owned.AnimationPayload,
                    NextChunkIndex = 0,
                    PlaybackEpochUtcTicks = owned.PlaybackEpochUtcTicks,
                    Recipients = recipients,
                    PacketJob = null,
                    Packets = null,
                    HeaderSent = false,
                    EnqueuedTimestamp = Stopwatch.GetTimestamp(),
                    FirstPacketQueueTicks = 0,
                }
            );
        }

        private static BasisAnimationPacketJobRequest ScheduleAnimationPacketBatch(
            Guid id,
            BasisNativeAnimationPayload animationPayload,
            long playbackEpochUtcTicks,
            int startChunkIndex
        )
        {
            return BasisAnimatedImageJobs.SchedulePacketBuild(
                id,
                animationPayload,
                playbackEpochUtcTicks,
                AnimationFormatNativeLz4,
                OpAnimationSpawn,
                OpAnimationChunk,
                BasisImagePickupSettings.ChunkPayloadBytes,
                startChunkIndex,
                BasisImagePickupSettings.AnimationPacketBuildChunksPerJob
            );
        }

        private void ProcessOutboundAnimationTransfers()
        {
            int chunksRemaining =
                BasisImagePickupSettings.MaxAnimationNetworkChunksPerFrame;

            // Keep one animation transfer at the head until all of its chunks are sent. The receiver
            // intentionally limits concurrent native transfer buffers; round-robin headers opened many
            // transfers at once and caused later animations in a large drag batch to be dropped forever.
            while (chunksRemaining > 0 && _outboundAnimations.Count > 0)
            {
                OutboundAnimationTransfer transfer = _outboundAnimations.Peek();
                if (HasPendingOutboundImageTransfer(transfer.Id))
                    return;
                if (
                    !_owned.TryGetValue(transfer.Id, out OwnedImage owned)
                    || !ReferenceEquals(owned.AnimationPayload, transfer.Payload)
                    || owned.PlaybackEpochUtcTicks != transfer.PlaybackEpochUtcTicks
                )
                {
                    _outboundAnimations.Dequeue();
                    DisposeOutboundAnimationTransfer(transfer);
                    continue;
                }

                if (transfer.Packets == null)
                {
                    if (transfer.PacketJob == null)
                    {
                        if (!transfer.HeaderSent && transfer.NextChunkIndex == 0)
                        {
                            transfer.FirstPacketQueueTicks = Math.Max(
                                0L,
                                Stopwatch.GetTimestamp() - transfer.EnqueuedTimestamp
                            );
                        }

                        try
                        {
                            transfer.PacketJob = ScheduleAnimationPacketBatch(
                                transfer.Id,
                                transfer.Payload,
                                transfer.PlaybackEpochUtcTicks,
                                transfer.NextChunkIndex
                            );
                        }
                        catch (Exception exception)
                        {
                            BasisDebug.LogWarning(
                                $"Image pickup: could not schedule animation packet worker "
                                    + $"({exception.Message}).",
                                LogTag
                            );
                            _outboundAnimations.Dequeue();
                            DisposeOutboundAnimationTransfer(transfer);
                            continue;
                        }
                    }

                    BasisAnimationPacketBatch packetBatch;
                    try
                    {
                        if (!transfer.PacketJob.TryComplete(out packetBatch))
                            return;
                    }
                    catch (Exception exception)
                    {
                        BasisDebug.LogWarning(
                            $"Image pickup: animation packet worker failed while completing "
                                + $"({exception.GetBaseException().Message}).",
                            LogTag
                        );
                        _outboundAnimations.Dequeue();
                        DisposeOutboundAnimationTransfer(transfer);
                        continue;
                    }
                    DisposeRequest(transfer.PacketJob, "animation packet request");
                    transfer.PacketJob = null;
                    if (
                        packetBatch == null
                        || !packetBatch.Ok
                        || packetBatch.StartChunkIndex != transfer.NextChunkIndex
                        || packetBatch.PacketCount <= 0
                    )
                    {
                        BasisDebug.LogWarning(
                            $"Image pickup: Burst packet construction failed: "
                                + $"{packetBatch?.Error ?? "invalid batch"}.",
                            LogTag
                        );
                        packetBatch?.Dispose();
                        _outboundAnimations.Dequeue();
                        DisposeOutboundAnimationTransfer(transfer);
                        continue;
                    }

                    transfer.Packets = packetBatch;
                    if (!transfer.HeaderSent)
                    {
                        double readyMilliseconds =
                            packetBatch.ReadyElapsedTicks * 1000d / Stopwatch.Frequency;
                        double queueMilliseconds =
                            transfer.FirstPacketQueueTicks
                            * 1000d
                            / Stopwatch.Frequency;
                        BasisDebug.Log(
                            $"Image pickup: first native packet batch observed ready "
                                + $"{readyMilliseconds:0.###} ms after scheduling; transfer waited "
                                + $"{queueMilliseconds:0.###} ms in the serialized animation queue.",
                            LogTag
                        );
                    }
                }

                if (!transfer.HeaderSent)
                {
                    int headerLength = transfer.Packets.HeaderLength;
                    if (headerLength <= 0)
                    {
                        BasisDebug.LogWarning("Image pickup: first native packet batch has no header.", LogTag);
                        _outboundAnimations.Dequeue();
                        DisposeOutboundAnimationTransfer(transfer);
                        continue;
                    }
                    if (transfer.HeaderBuffer == null || transfer.HeaderBuffer.Length != headerLength)
                    {
                        transfer.HeaderBuffer = new byte[headerLength];
                    }
                    transfer.Packets.CopyHeaderTo(transfer.HeaderBuffer);
                    SendCustomNetworkEventDirect(
                        transfer.HeaderBuffer,
                        DeliveryMethod.ReliableOrdered,
                        transfer.Recipients
                    );
                    transfer.HeaderSent = true;
                }

                int batchIndex =
                    transfer.NextChunkIndex - transfer.Packets.StartChunkIndex;
                while (chunksRemaining > 0 && batchIndex >= 0 && batchIndex < transfer.Packets.PacketCount)
                {
                    int packetLength = transfer.Packets.GetPacketLength(batchIndex);
                    int fullPacketLength =
                        BasisAnimatedImageNetworkCodec.AnimationChunkHeaderSize
                        + BasisImagePickupSettings.ChunkPayloadBytes;
                    byte[] packet;
                    if (packetLength == fullPacketLength)
                    {
                        if (transfer.FullChunkBuffer == null || transfer.FullChunkBuffer.Length != packetLength)
                        {
                            transfer.FullChunkBuffer = new byte[packetLength];
                        }
                        packet = transfer.FullChunkBuffer;
                    }
                    else
                    {
                        if (transfer.TailChunkBuffer == null || transfer.TailChunkBuffer.Length != packetLength)
                        {
                            transfer.TailChunkBuffer = new byte[packetLength];
                        }
                        packet = transfer.TailChunkBuffer;
                    }
                    transfer.Packets.CopyPacketTo(batchIndex, packet);

                    SendCustomNetworkEventDirect(packet, DeliveryMethod.ReliableOrdered, transfer.Recipients);
                    transfer.NextChunkIndex++;
                    batchIndex++;
                    chunksRemaining--;
                }
                if (transfer.NextChunkIndex >= transfer.Packets.TotalChunks)
                {
                    _outboundAnimations.Dequeue();
                    DisposeOutboundAnimationTransfer(transfer);
                    continue;
                }

                if (batchIndex >= transfer.Packets.PacketCount)
            {
                    transfer.Packets.Dispose();
                    transfer.Packets = null;
                    try
                    {
                        transfer.PacketJob = ScheduleAnimationPacketBatch(
                            transfer.Id,
                            transfer.Payload,
                            transfer.PlaybackEpochUtcTicks,
                            transfer.NextChunkIndex
                        );
                    }
                    catch (Exception exception)
                    {
                        BasisDebug.LogWarning(
                            $"Image pickup: could not schedule the next Burst packet batch ({exception.Message}).",
                            LogTag
                        );
                        _outboundAnimations.Dequeue();
                        DisposeOutboundAnimationTransfer(transfer);
                        continue;
                    }
                }
            }
        }

        private void RemoveOutboundAnimationTransfers(Guid id)
        {
            int count = _outboundAnimations.Count;
            for (int i = 0; i < count; i++)
            {
                OutboundAnimationTransfer transfer = _outboundAnimations.Dequeue();
                if (transfer.Id != id)
                    _outboundAnimations.Enqueue(transfer);
                else
                    DisposeOutboundAnimationTransfer(transfer);
            }
        }

        private void RemoveOutboundAnimationTransfersForRecipient(ushort recipient)
        {
            int count = _outboundAnimations.Count;
            for (int i = 0; i < count; i++)
            {
                OutboundAnimationTransfer transfer = _outboundAnimations.Dequeue();
                if (transfer.Recipients == null || Array.IndexOf(transfer.Recipients, recipient) < 0)
                {
                    _outboundAnimations.Enqueue(transfer);
                }
                else
                {
                    DisposeOutboundAnimationTransfer(transfer);
                }
            }
        }

        private static void DisposeOutboundAnimationTransfer(OutboundAnimationTransfer transfer)
        {
            if (transfer == null)
                return;
            DisposeRequest(transfer.PacketJob, "animation packet request");
            transfer.PacketJob = null;
            DisposeRequest(transfer.Packets, "animation packet batch");
            transfer.Packets = null;
        }

        private static void DisposeRequest(IDisposable request, string description)
        {
            if (request == null)
                return;
            try
            {
                request.Dispose();
            }
            catch (Exception exception)
            {
                BasisDebug.LogWarning(
                    $"Image pickup could not fully dispose {description}: "
                        + $"{exception.GetBaseException().Message}",
                    LogTag
                );
            }
        }

        private static string ResolveOwnerName(ushort senderId)
        {
            if (BasisNetworkPlayer.GetPlayerById(senderId, out BasisNetworkPlayer player))
            {
                string name = player.SafeDisplayName;
                if (string.IsNullOrEmpty(name)) name = player.displayName;
                if (!string.IsNullOrEmpty(name)) return name;
            }
            return $"Player {senderId}";
        }

        private static bool TrySkipWireString(BinaryReader reader, int maxByteLength)
        {
            if (!TryRead7BitEncodedInt(reader, out int byteLength)) return false;
            if (byteLength < 0 || byteLength > maxByteLength) return false;

            long remainingBytes = reader.BaseStream.Length - reader.BaseStream.Position;
            if (remainingBytes < byteLength) return false;

            reader.BaseStream.Position += byteLength;
            return true;
        }

        private static bool TryRead7BitEncodedInt(BinaryReader reader, out int value)
        {
            value = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                if (reader.BaseStream.Length - reader.BaseStream.Position < 1) return false;

                byte b = reader.ReadByte();
                if (shift == 28 && (b & 0xF0) != 0) return false;

                value |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0) return true;
            }
            return false;
        }

        internal static string ReadBoundedOwnerName(BinaryReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            uint byteLength = 0;
            for (int byteIndex = 0; byteIndex < 5; byteIndex++)
            {
                byte value = reader.ReadByte();
                if (byteIndex == 4 && (value & 0xF8) != 0)
                    throw new InvalidDataException("Owner name length prefix is invalid.");
                byteLength |= (uint)(value & 0x7F) << (byteIndex * 7);
                if ((value & 0x80) != 0)
                    continue;

                if (byteLength > MaxOwnerNameUtf8Bytes)
                {
                    throw new InvalidDataException($"Owner name exceeds {MaxOwnerNameUtf8Bytes:N0} UTF-8 bytes.");
                }
                byte[] bytes = reader.ReadBytes((int)byteLength);
                if (bytes.Length != (int)byteLength)
                    throw new EndOfStreamException("Owner name is truncated.");
                return StrictUtf8.GetString(bytes);
            }

            throw new InvalidDataException("Owner name length prefix is invalid.");
        }

        internal static string NormalizeOwnerNameForNetwork(string ownerName)
        {
            string value = ownerName ?? string.Empty;
            if (Encoding.UTF8.GetByteCount(value) <= MaxOwnerNameUtf8Bytes)
                return value;

            int characterCount = Math.Min(value.Length, MaxOwnerNameUtf8Bytes);
            while (characterCount > 0)
            {
                if (char.IsHighSurrogate(value[characterCount - 1]))
                    characterCount--;
                string candidate = value.Substring(0, characterCount);
                if (Encoding.UTF8.GetByteCount(candidate) <= MaxOwnerNameUtf8Bytes)
                    return candidate;
                characterCount--;
            }
            return string.Empty;
        }

        private static byte[] EncodeSpawn(
            Guid id,
            ushort ownerId,
            string ownerName,
            int width,
            int height,
            int totalBytes,
            int totalChunks,
            Vector3 position,
            Quaternion rotation
        )
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write(OpSpawn);
            BasisAnimatedImageNetworkCodec.WriteGuid(writer, id);
            writer.Write(ownerId);
            writer.Write(NormalizeOwnerNameForNetwork(ownerName));
            writer.Write(width);
            writer.Write(height);
            writer.Write(totalBytes);
            writer.Write(totalChunks);
            WritePose(writer, position, rotation);
            writer.Flush();
            return stream.ToArray();
        }

        private static byte[] EncodeChunk(Guid id, int chunkIndex, byte[] source, int offset, int length)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write(OpChunk);
            BasisAnimatedImageNetworkCodec.WriteGuid(writer, id);
            writer.Write(chunkIndex);
            writer.Write(length);
            writer.Write(source, offset, length);
            writer.Flush();
            return stream.ToArray();
        }

        private static byte[] EncodeTransform(Guid id, Vector3 position, Quaternion rotation, float scale)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write(OpTransform);
            BasisAnimatedImageNetworkCodec.WriteGuid(writer, id);
            WritePose(writer, position, rotation);
            writer.Write(scale);
            writer.Flush();
            return stream.ToArray();
        }

        private static byte[] EncodeDespawn(Guid id)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write(OpDespawn);
            BasisAnimatedImageNetworkCodec.WriteGuid(writer, id);
            writer.Flush();
            return stream.ToArray();
        }

        private static byte[] EncodeClaim(Guid id)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8);
            writer.Write(OpClaim);
            BasisAnimatedImageNetworkCodec.WriteGuid(writer, id);
            writer.Flush();
            return stream.ToArray();
        }

        private static void WritePose(BinaryWriter writer, Vector3 position, Quaternion rotation)
        {
            writer.Write(position.x);
            writer.Write(position.y);
            writer.Write(position.z);
            writer.Write(rotation.x);
            writer.Write(rotation.y);
            writer.Write(rotation.z);
            writer.Write(rotation.w);
        }

        internal static Vector3 CalculateBatchSpawnLocalOffset(int index, int count)
        {
            int columns = Mathf.Min(BasisImagePickupSettings.BatchSpawnColumns, count);
            return CalculateBatchSpawnLocalOffset(index, count, columns, float.NegativeInfinity);
        }

        internal static Vector3 CalculateBatchSpawnLocalOffset(int index, int count, int columns, float minimumLocalY)
        {
            if (count <= 1)
            {
                if (index != 0)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return new Vector3(0f, Mathf.Max(0f, minimumLocalY), 0f);
            }
            if (index < 0 || index >= count)
                throw new ArgumentOutOfRangeException(nameof(index));

            columns = Mathf.Clamp(columns, 1, count);
            int rows = (count + columns - 1) / columns;
            int row = index / columns;
            int column = index % columns;
            int itemsInRow = Mathf.Min(columns, count - row * columns);

            float x =
                (column - (itemsInRow - 1) * 0.5f)
                * BasisImagePickupSettings.BatchSpawnHorizontalSpacingMeters;
            float centeredY =
                ((rows - 1) * 0.5f - row)
                * BasisImagePickupSettings.BatchSpawnVerticalSpacingMeters;
            float lowestCenteredY =
                -(rows - 1)
                * 0.5f
                * BasisImagePickupSettings.BatchSpawnVerticalSpacingMeters;
            float upwardShift = float.IsNegativeInfinity(minimumLocalY)
                ? 0f
                : Mathf.Max(0f, minimumLocalY - lowestCenteredY);
            return new Vector3(x, centeredY + upwardShift, 0f);
        }

        internal static int CalculateBatchSpawnColumns(int count, float batchCenterY, float minimumCenterY)
        {
            if (count <= 1)
                return 1;

            float verticalSpacing = Mathf.Max(0.01f, BasisImagePickupSettings.BatchSpawnVerticalSpacingMeters);
            float availableDownward = Mathf.Max(0f, batchCenterY - minimumCenterY);
            int rowsWithoutCrossingMinimum = Mathf.Max(
                1,
                Mathf.FloorToInt(availableDownward * 2f / verticalSpacing) + 1
            );
            int requiredColumns = Mathf.CeilToInt(count / (float)rowsWithoutCrossingMinimum);
            int defaultColumns = Mathf.Min(BasisImagePickupSettings.BatchSpawnColumns, count);
            int maximumColumns = Mathf.Min(BasisImagePickupSettings.BatchSpawnMaximumColumns, count);
            return Mathf.Clamp(Mathf.Max(defaultColumns, requiredColumns), 1, maximumColumns);
        }

        private static float GetMinimumBatchImageCenterY(float batchCenterY)
        {
            float playerGroundY =
                BasisLocalPlayer.Instance != null
                    ? BasisLocalPlayer.Instance.transform.position.y
                    : batchCenterY - 1.5f;
            return playerGroundY
                + BasisImagePickupSettings.BaseHeightMeters * 0.5f
                + BasisImagePickupSettings.BatchSpawnGroundClearanceMeters;
        }

        private void GetSpawnPose(out Vector3 position, out Quaternion rotation)
        {
            if (BasisLocalCameraDriver.HasInstance && BasisLocalCameraDriver.Instance != null)
            {
                BasisLocalCameraDriver.Instance.transform.GetPositionAndRotation(
                    out Vector3 cameraPosition,
                    out Quaternion cameraRotation
                );
                Vector3 forward = cameraRotation * Vector3.forward;
                position =
                    cameraPosition + forward * BasisImagePickupSettings.SpawnDistance;
                rotation = Quaternion.LookRotation(forward, Vector3.up);
            }
            else
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
            }
        }

#if UNITY_EDITOR
        [ContextMenu("Test Spawn From Path")]
        private void EditorTestSpawn()
        {
            if (!string.IsNullOrEmpty(editorTestImagePath))
                SpawnFromFile(editorTestImagePath);
        }
#endif
    }
}
