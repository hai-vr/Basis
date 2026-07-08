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

        private const byte OpSpawn = 1;
        private const byte OpChunk = 2;
        private const byte OpTransform = 3;
        private const byte OpDespawn = 4;
        private const byte OpClaim = 5;
		private const byte OpAnimationSpawn = 6;
		private const byte OpAnimationChunk = 7;
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
			public NativeArray<byte> Buffer;
			public int PayloadBytes;
			public int DecodedBytes;
			public long PlaybackEpochUtcTicks;
		}

		private sealed class PendingInboundAnimationDecode
		{
			public ushort Sender;
			public Guid Id;
			public int PayloadBytes;
			public int DecodedBytes;
			public long PlaybackEpochUtcTicks;
			public BasisAnimationDecodeJobRequest Job;
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
        private readonly Dictionary<Guid, InboundTransfer> _inbound = new();
		private readonly Dictionary<Guid, InboundAnimationTransfer> _inboundAnimations =
			new();
		private readonly Queue<OutboundAnimationTransfer> _outboundAnimations = new();
		private readonly Queue<PendingGifSpawn> _queuedGifSpawns = new();
		private readonly List<PendingGifSpawn> _pendingGifSpawns = new();
		private readonly List<QueuedInboundAnimationDecode> _queuedInboundAnimationDecodes =
			new();
		private readonly List<PendingInboundAnimationDecode> _pendingInboundAnimationDecodes =
			new();
		private readonly HashSet<Guid> _animationAttempted = new();
		private bool _gifDecodePausedForMemory;
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
            DontDestroyOnLoad(gameObject);
        }

		public override void OnDestroy()
		{
			BasisEventDriver.OnUpdate -= Simulate;
			if (Instance == this)
				Instance = null;

			for (int i = 0; i < _pendingGifSpawns.Count; i++)
				_pendingGifSpawns[i].Job?.Dispose();
			_pendingGifSpawns.Clear();
			_queuedGifSpawns.Clear();

			for (int i = 0; i < _queuedInboundAnimationDecodes.Count; i++)
			{
				QueuedInboundAnimationDecode queued = _queuedInboundAnimationDecodes[i];
				if (queued.Buffer.IsCreated)
					queued.Buffer.Dispose();
			}
			_queuedInboundAnimationDecodes.Clear();

			for (int i = 0; i < _pendingInboundAnimationDecodes.Count; i++)
				_pendingInboundAnimationDecodes[i].Job?.Dispose();
			_pendingInboundAnimationDecodes.Clear();

			foreach (InboundAnimationTransfer transfer in _inboundAnimations.Values)
			{
				if (transfer.Buffer.IsCreated)
					transfer.Buffer.Dispose();
				if (transfer.Received.IsCreated)
					transfer.Received.Dispose();
			}
			_inboundAnimations.Clear();

			while (_outboundAnimations.Count > 0)
				DisposeOutboundAnimationTransfer(_outboundAnimations.Dequeue());

			foreach (OwnedImage owned in _owned.Values)
			{
				if (owned.Object != null)
					owned.Object.AnimatedImagePlayer?.ClearReloadPayload();
				owned.AnimationPayload?.Dispose();
			}
			_owned.Clear();

			base.OnDestroy();
		}

        public override void Start()
        {
            AssignNetworkGUIDIdentifier(FixedNetworkIdentifier);
            base.Start();
        }

        public override void OnNetworkReady()
        {
			BasisDebug.Log(
				$"Image pickup manager ready (network id {NetworkID}).",
				LogTag
			);
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

			var supportedPaths = new List<string>(paths.Count);
			for (int i = 0; i < paths.Count; i++)
			{
				string path = paths[i];
				if (
					!string.IsNullOrWhiteSpace(path)
					&& BasisImageSecurity.HasSupportedImageExtension(path)
				)
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
				BasisImagePickupRejectionPopup.ShowImageLimit(
					currentCount,
					supportedPaths.Count
				);
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
			int columns = CalculateBatchSpawnColumns(
				attemptCount,
				batchCenter.y,
				minimumCenterY
			);
			float minimumLocalY = minimumCenterY - batchCenter.y;
			Vector3 horizontalRight = rotation * Vector3.right;

			int accepted = 0;
			int animatedAccepted = 0;
			for (
				int pathIndex = 0;
				pathIndex < supportedPaths.Count && accepted < attemptCount;
				pathIndex++
			)
			{
				Vector3 localOffset = CalculateBatchSpawnLocalOffset(
					accepted,
					attemptCount,
					columns,
					minimumLocalY
				);
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
				attemptCount,
				animatedAccepted
			);
			return accepted;
		}

		private int GetLocalReservedImageCount()
		{
			return _owned.Count + _queuedGifSpawns.Count + _pendingGifSpawns.Count;
		}

		internal static int CalculateAvailableLocalImageSlots(
			int ownedCount,
			int queuedGifCount,
			int activeGifCount
		)
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
			if (
				!BasisNetworkModeration.GlobalImagesLocked
				|| BasisNetworkModeration.LocalPlayerHasGlobalLockBypass()
			)
			{
				return true;
			}

			string reason = BasisLocalization.Get(
				"imagePickup.popup.reason.adminLocked"
			);
			BasisImagePickupRejectionPopup.Show(path, reason);
			BasisDebug.LogWarning($"Image pickup rejected: {reason}", LogTag);
			return false;
		}

		private bool SpawnFromFileAtPose(
			string path,
			Vector3 position,
			Quaternion rotation
		)
		{
			if (BasisAnimatedImageJobs.IsGifPath(path))
				return QueueGifSpawn(path, position, rotation);

			return SpawnValidatedFile(
				path,
				BasisImageSecurity.ValidateFile(path),
				position,
				rotation
			);
		}

		private bool QueueGifSpawn(string path, Vector3 position, Quaternion rotation)
		{
			_queuedGifSpawns.Enqueue(
				new PendingGifSpawn
				{
					Path = path,
					Position = position,
					Rotation = rotation,
				}
			);
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
				if (!_gifDecodePausedForMemory)
				{
					_gifDecodePausedForMemory = true;
					BasisDebug.LogWarning(
						$"Image pickup paused queued GIF decoding at "
							+ $"{BasisAnimatedImageData.TotalResidentNativeBytes / (1024L * 1024L):N0} MiB "
							+ "of resident animation data; reloadable offscreen animations will release decoded frames until memory is available.",
						LogTag
					);
				}
				return;
			}
			_gifDecodePausedForMemory = false;

			while (
				_pendingGifSpawns.Count
					< BasisImagePickupSettings.MaxConcurrentAnimationDecodeJobs
				&& _queuedGifSpawns.Count > 0
			)
			{
				PendingGifSpawn pending = _queuedGifSpawns.Dequeue();
				try
				{
					pending.Job = BasisAnimatedImageJobs.ScheduleGifDecode(
						pending.Path
					);
					_pendingGifSpawns.Add(pending);
					BasisDebug.Log(
						$"Image pickup: started GIF Burst pipeline for "
							+ $"'{Path.GetFileName(pending.Path)}' "
							+ $"({_pendingGifSpawns.Count:N0}/"
							+ $"{BasisImagePickupSettings.MaxConcurrentAnimationDecodeJobs:N0} active, "
							+ $"{_queuedGifSpawns.Count:N0} waiting).",
						LogTag
					);
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

		private void ProcessCompletedGifSpawns()
		{
			for (int index = _pendingGifSpawns.Count - 1; index >= 0; index--)
			{
				PendingGifSpawn pending = _pendingGifSpawns[index];
				if (!pending.Job.TryComplete(out BasisGifDecodeJobResult workerResult))
					continue;
				_pendingGifSpawns.RemoveAt(index);

				try
				{
					if (
						BasisNetworkModeration.GlobalImagesLocked
						&& !BasisNetworkModeration.LocalPlayerHasGlobalLockBypass()
					)
					{
						string reason = BasisLocalization.Get(
							"imagePickup.popup.reason.adminLockedDuringDecode"
						);
						BasisImagePickupRejectionPopup.Show(pending.Path, reason);
						BasisDebug.LogWarning(
							$"Image pickup rejected: {reason}",
							LogTag
						);
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
					SpawnValidatedFile(
						pending.Path,
						result,
						pending.Position,
						pending.Rotation
					);
				}
				finally
				{
					pending.Job.Dispose();
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

            Guid id = Guid.NewGuid();
			ushort ownerId =
				BasisNetworkPlayer.LocalPlayer != null
					? BasisNetworkPlayer.LocalPlayer.playerId
					: (ushort)0;
			string ownerName =
				BasisLocalPlayer.Instance != null
					? BasisLocalPlayer.Instance.SafeDisplayName
					: "Unknown";

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
				result.Animation.Dispose();
				result.Animation = null;
				result.AnimationPayload?.Dispose();
				result.AnimationPayload = null;
			}
			if (result.Animation != null)
			{
				playbackEpochUtcTicks = BasisNetworkManagement.RemoteUtcTime().Ticks;
				BasisNativeAnimationPayload candidatePayload = result.AnimationPayload;
				result.AnimationPayload = null;
				int frameCount = result.Animation.FrameCount;
				if (candidatePayload == null)
				{
					playbackEpochUtcTicks = 0;
					result.Animation.Dispose();
					result.Animation = null;
					BasisDebug.LogWarning(
						$"Image pickup: GIF animation encoding failed; showing the poster frame only so decoded frame memory remains reclaimable: {result.AnimationNetworkError ?? "unknown error"}",
						LogTag
					);
				}
				else if (
					pickup.TrySetAnimation(
						result.Animation,
						playbackEpochUtcTicks,
						candidatePayload
					)
				)
				{
					result.Animation = null;
					animationPayload = candidatePayload;
					BasisDebug.Log(
						$"Image pickup: GIF animation attached locally; Burst packet batches will use the compact persistent native payload ({frameCount} frames, {animationPayload.Length} LZ4 bytes).",
						LogTag
					);
				}
				else
				{
					playbackEpochUtcTicks = 0;
					result.Animation.Dispose();
					result.Animation = null;
					candidatePayload?.Dispose();
					BasisDebug.LogWarning(
						"Image pickup: GIF decoded, but animated playback could not be attached; showing and replicating the poster frame only.",
						LogTag
					);
				}
			}
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
		public bool TrySetAnimation(
			Guid id,
			BasisAnimatedImageData data,
			long playbackEpochUtcTicks = 0
		)
		{
			if (data == null)
				return false;
			if (
				!_images.TryGetValue(id, out BasisImagePickupObject pickup)
				|| pickup == null
			)
				return false;
			return pickup.TrySetAnimation(data, playbackEpochUtcTicks);
		}

        /// <summary>Removes an image for everyone. Any client may call this for any image.</summary>
        public void RequestDespawn(Guid id)
        {
            if (HasNetworkID)
            {
				SendCustomNetworkEventDirect(
					EncodeDespawn(id),
					DeliveryMethod.ReliableOrdered,
					null
				);
            }
            RemoveImage(id);
        }

        /// <summary>Takes movement authority when this client grabs an image, demoting other clients to followers.</summary>
        public void ClaimControl(Guid id)
        {
			if (
				!_images.TryGetValue(id, out BasisImagePickupObject pickup)
				|| pickup == null
			)
				return;
			if (pickup.IsController)
				return;
            pickup.SetController(true);
			if (HasNetworkID)
			{
				SendCustomNetworkEventDirect(
					EncodeClaim(id),
					DeliveryMethod.ReliableOrdered,
					null
				);
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
					$"Image pickup manager simulation failed: {exception}",
					LogTag
				);
			}
		}

		private void SimulateBody()
		{
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

				pickup.transform.GetPositionAndRotation(
					out Vector3 position,
					out Quaternion rotation
				);
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
				owned.Object.transform.GetPositionAndRotation(
					out Vector3 position,
					out Quaternion rotation
				);
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
			for (int i = 0; i < _scratchIds.Count; i++)
				RemoveImage(_scratchIds[i]);

            _scratchIds.Clear();
            foreach (KeyValuePair<Guid, InboundTransfer> entry in _inbound)
            {
				if (entry.Value.Sender == left)
					_scratchIds.Add(entry.Key);
            }
			for (int i = 0; i < _scratchIds.Count; i++)
				_inbound.Remove(_scratchIds[i]);

			_scratchIds.Clear();
			foreach (
				KeyValuePair<Guid, InboundAnimationTransfer> entry in _inboundAnimations
			)
        {
				if (entry.Value.Sender == left)
					_scratchIds.Add(entry.Key);
			}
			for (int i = 0; i < _scratchIds.Count; i++)
				RemoveInboundAnimationTransfer(_scratchIds[i]);

			for (int i = _queuedInboundAnimationDecodes.Count - 1; i >= 0; i--)
			{
				QueuedInboundAnimationDecode queued = _queuedInboundAnimationDecodes[i];
				if (queued.Sender != left)
					continue;
				if (queued.Buffer.IsCreated)
					queued.Buffer.Dispose();
				_animationAttempted.Remove(queued.Id);
				_queuedInboundAnimationDecodes.RemoveAt(i);
			}

			for (int i = _pendingInboundAnimationDecodes.Count - 1; i >= 0; i--)
			{
				PendingInboundAnimationDecode pending = _pendingInboundAnimationDecodes[
					i
				];
				if (pending.Sender != left)
					continue;
				pending.Job?.Dispose();
				_animationAttempted.Remove(pending.Id);
				_pendingInboundAnimationDecodes.RemoveAt(i);
			}

			RemoveOutboundAnimationTransfersForRecipient(left);
			_spawnRateBySender.Remove(left);
		}

		public override void OnDirectNetworkMessage(
			ushort senderId,
			byte[] buffer,
			DeliveryMethod deliveryMethod
		)
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
				BasisDebug.LogWarning(
					$"Image pickup: malformed message from {senderId} ({e.Message}).",
					LogTag
				);
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
			Vector3 position = new Vector3(
				reader.ReadSingle(),
				reader.ReadSingle(),
				reader.ReadSingle()
			);
			Quaternion rotation = new Quaternion(
				reader.ReadSingle(),
				reader.ReadSingle(),
				reader.ReadSingle(),
				reader.ReadSingle()
			);

			if (_images.ContainsKey(id) || _inbound.ContainsKey(id))
				return;

			if (
				!CanAcceptSpawn(
					senderId,
					totalBytes,
					width,
					height,
					totalChunks,
					out string reason
				)
			)
			{
				BasisDebug.LogWarning(
					$"Image pickup from {senderId} dropped: {reason}.",
					LogTag
				);
                return;
            }

            _inbound[id] = new InboundTransfer
            {
                Sender = senderId,
                Id = id,
                Buffer = new byte[totalBytes],
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

        private void HandleChunk(ushort senderId, BinaryReader reader)
        {
            Guid id = new Guid(reader.ReadBytes(16));
            int chunkIndex = reader.ReadInt32();
            int length = reader.ReadInt32();

            if (!_inbound.TryGetValue(id, out InboundTransfer transfer)) return;
            if (transfer.Sender != senderId) return;
            if (chunkIndex < 0 || chunkIndex >= transfer.TotalChunks) return;
            if (length <= 0 || length > BasisImagePickupSettings.ChunkPayloadBytes) return;

            int offset = chunkIndex * BasisImagePickupSettings.ChunkPayloadBytes;
            if (offset < 0 || offset >= transfer.Buffer.Length) return;

            int expectedLength = Mathf.Min(BasisImagePickupSettings.ChunkPayloadBytes, transfer.Buffer.Length - offset);
            if (length != expectedLength) return;

            long remainingBytes = reader.BaseStream.Length - reader.BaseStream.Position;
            if (remainingBytes < length) return;

            byte[] data = reader.ReadBytes(length);
            if (data.Length != length) return;

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

			BasisImageValidationResult result = BasisImageSecurity.ValidateBytes(
				transfer.Buffer
			);
            if (!result.Ok)
            {
				BasisDebug.LogWarning(
					$"Image pickup from {transfer.Sender} failed validation: {result.Error}.",
					LogTag
				);
                return;
            }
            if (_images.ContainsKey(transfer.Id))
            {
				if (result.Texture != null)
					Destroy(result.Texture);
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
				return;
			if (_inboundAnimations.ContainsKey(id) || _animationAttempted.Contains(id))
				return;
			if (
				!CanAcceptAnimation(
					senderId,
					id,
					totalBytes,
					totalChunks,
					playbackEpochUtcTicks,
					out string reason
				)
			)
			{
				BasisDebug.LogWarning(
					$"Image pickup animation from {senderId} dropped: {reason}.",
					LogTag
				);
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
				received = new NativeArray<byte>(
					totalChunks,
					Allocator.Persistent,
					NativeArrayOptions.ClearMemory
				);
				_inboundAnimations[id] = new InboundAnimationTransfer
				{
					Sender = senderId,
					Id = id,
					Buffer = buffer,
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

			if (
				!_inboundAnimations.TryGetValue(
					id,
					out InboundAnimationTransfer transfer
				)
			)
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
			int expectedLength = Mathf.Min(
				BasisImagePickupSettings.ChunkPayloadBytes,
				transfer.Buffer.Length - offset
			);
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
				_animationAttempted.Remove(transfer.Id);
				BasisDebug.LogWarning(
					$"Image pickup animation from {transfer.Sender} dropped: {headerError}",
					LogTag
				);
				return;
			}
			if (!FitsInboundAnimationDecodeBudget(0, 0, decodedBytes))
			{
				if (transfer.Buffer.IsCreated)
					transfer.Buffer.Dispose();
				_animationAttempted.Remove(transfer.Id);
				BasisDebug.LogWarning(
					$"Image pickup animation from {transfer.Sender} dropped: decoded "
						+ "payload exceeds the per-sender native decode limit.",
					LogTag
				);
				return;
			}

			int payloadBytes = transfer.Buffer.Length;
			var queued = new QueuedInboundAnimationDecode
			{
				Sender = transfer.Sender,
				Id = transfer.Id,
				Buffer = transfer.Buffer,
				PayloadBytes = payloadBytes,
				DecodedBytes = decodedBytes,
				PlaybackEpochUtcTicks = transfer.PlaybackEpochUtcTicks,
			};
			try
			{
				_queuedInboundAnimationDecodes.Add(queued);
				transfer.Buffer = default;
			}
			catch (Exception exception)
			{
				if (transfer.Buffer.IsCreated)
					transfer.Buffer.Dispose();
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
			for (int index = 0; index < _queuedInboundAnimationDecodes.Count; )
			{
				if (
					_pendingInboundAnimationDecodes.Count
					>= BasisImagePickupSettings.MaxConcurrentAnimationDecodeJobs
				)
				{
					return;
				}

				QueuedInboundAnimationDecode queued = _queuedInboundAnimationDecodes[
					index
				];
				if (
					!TryGetAcceptedInboundAnimationPickup(
						queued.Sender,
						queued.Id,
						out BasisImagePickupObject pickup
					)
				)
				{
					RemoveQueuedInboundAnimationDecode(index);
					continue;
				}

				if (!CanStartInboundAnimationDecode(queued.Sender, queued.DecodedBytes))
				{
					index++;
					continue;
				}

				_queuedInboundAnimationDecodes.RemoveAt(index);
				BasisAnimationDecodeJobRequest job = null;
				try
				{
					job = BasisAnimatedImageJobs.ScheduleNetworkDecode(
						queued.Buffer,
						queued.PayloadBytes,
						true
					);
					queued.Buffer = default;
					_pendingInboundAnimationDecodes.Add(
						new PendingInboundAnimationDecode
						{
							Sender = queued.Sender,
							Id = queued.Id,
							PayloadBytes = queued.PayloadBytes,
							DecodedBytes = queued.DecodedBytes,
							PlaybackEpochUtcTicks = queued.PlaybackEpochUtcTicks,
							Job = job,
						}
					);
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
					if (queued.Buffer.IsCreated)
						queued.Buffer.Dispose();
					_animationAttempted.Remove(queued.Id);
					BasisDebug.LogWarning(
						$"Image pickup animation from {queued.Sender} could not schedule "
							+ $"a Burst decode ({exception.Message}).",
						LogTag
					);
				}
			}
		}

		private bool TryGetAcceptedInboundAnimationPickup(
			ushort sender,
			Guid id,
			out BasisImagePickupObject pickup
		)
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
			for (int i = 0; i < _pendingInboundAnimationDecodes.Count; i++)
			{
				PendingInboundAnimationDecode pending = _pendingInboundAnimationDecodes[
					i
				];
				if (pending.Sender != sender)
					continue;
				pendingForSender++;
				pendingDecodedBytes += pending.DecodedBytes;
			}

			return FitsInboundAnimationDecodeBudget(
				pendingForSender,
				pendingDecodedBytes,
				decodedBytes
			);
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
			if (queued.Buffer.IsCreated)
				queued.Buffer.Dispose();
			_animationAttempted.Remove(queued.Id);
		}

		private void ProcessCompletedInboundAnimationDecodes()
		{
			for (
				int index = _pendingInboundAnimationDecodes.Count - 1;
				index >= 0;
				index--
			)
			{
				PendingInboundAnimationDecode pending = _pendingInboundAnimationDecodes[
					index
				];
				if (
					!pending.Job.TryComplete(
						out BasisAnimationDecodeJobResult workerResult
					)
				)
				{
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

					if (
						workerResult == null
						|| !workerResult.Ok
						|| workerResult.Animation == null
					)
					{
						BasisDebug.LogWarning(
							$"Image pickup animation from {pending.Sender} failed Burst validation: "
								+ $"{workerResult?.Error ?? "no result"}.",
							LogTag
						);
						continue;
					}

					BasisAnimatedImageData animation = workerResult.Animation;
					if (
						!CanAttachRemoteAnimation(
							pending.Sender,
							animation,
							out string budgetReason
						)
					)
					{
						BasisDebug.LogWarning(
							$"Image pickup animation from {pending.Sender} dropped: "
								+ $"{budgetReason}.",
							LogTag
						);
						continue;
					}

					if (
						!pickup.TrySetAnimation(
							animation,
							pending.PlaybackEpochUtcTicks
						)
					)
					{
						BasisDebug.LogWarning(
							$"Image pickup animation from {pending.Sender} could not be "
								+ "attached to its poster.",
							LogTag
						);
						continue;
					}

					workerResult.Animation = null;
					double workerMilliseconds =
						workerResult.WorkerElapsedTicks * 1000d / Stopwatch.Frequency;
					BasisDebug.Log(
						$"Image pickup animation replicated from {pending.Sender} "
							+ $"({animation.FrameCount} frames, {pending.PayloadBytes} bytes, "
							+ $"decoded by Burst in {workerMilliseconds:0.###} ms).",
						LogTag
					);
				}
				finally
				{
					pending.Job.Dispose();
				}
			}
        }

        private void HandleTransform(ushort senderId, BinaryReader reader)
        {
            Guid id = new Guid(reader.ReadBytes(16));
			Vector3 position = new Vector3(
				reader.ReadSingle(),
				reader.ReadSingle(),
				reader.ReadSingle()
			);
			Quaternion rotation = new Quaternion(
				reader.ReadSingle(),
				reader.ReadSingle(),
				reader.ReadSingle(),
				reader.ReadSingle()
			);
            float scale = reader.ReadSingle();

			if (
				_images.TryGetValue(id, out BasisImagePickupObject pickup)
				&& pickup != null
				&& !pickup.IsController
			)
            {
                pickup.SetRemoteTarget(position, rotation, scale);
            }
        }

        private void HandleClaim(ushort senderId, BinaryReader reader)
        {
            Guid id = new Guid(reader.ReadBytes(16));
			if (
				_images.TryGetValue(id, out BasisImagePickupObject pickup)
				&& pickup != null
			)
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
			if (
				BasisNetworkModeration.GlobalImagesLocked
				&& !BasisNetworkModeration.LocalPlayerHasGlobalLockBypass()
			)
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

			if (
				!IsWithinRemoteImageBudget(
					imageCount,
					aggregatePixels,
					aggregateBytes,
					out reason
				)
			)
			{
				return false;
			}
			if (
				activeTransfers >= BasisImagePickupSettings.MaxInboundTransfersPerSender
			)
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
			if (
				aggregatePixels > BasisImagePickupSettings.MaxRemoteImagePixelsPerSender
			)
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
			if (
				BasisNetworkModeration.GlobalImagesLocked
				&& !BasisNetworkModeration.LocalPlayerHasGlobalLockBypass()
			)
			{
				reason = "shared images locked by admin";
				return false;
			}
			if (!BasisImagePickupSettings.ReceiveEnabled)
			{
				reason = "receiving disabled";
				return false;
			}
			if (
				totalBytes <= 0
				|| totalBytes > BasisImagePickupSettings.MaxAnimationNetworkBytes
			)
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

			if (
				!_images.TryGetValue(id, out BasisImagePickupObject pickup)
				|| pickup == null
			)
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
			foreach (InboundAnimationTransfer transfer in _inboundAnimations.Values)
			{
				if (transfer.Sender != sender)
					continue;
				activeTransfers++;
				activeTransferBytes += transfer.Buffer.Length;
			}
			for (int i = 0; i < _queuedInboundAnimationDecodes.Count; i++)
			{
				QueuedInboundAnimationDecode queued = _queuedInboundAnimationDecodes[i];
				if (queued.Sender == sender)
					activeTransferBytes += queued.PayloadBytes;
			}
			for (int i = 0; i < _pendingInboundAnimationDecodes.Count; i++)
			{
				PendingInboundAnimationDecode pending = _pendingInboundAnimationDecodes[
					i
				];
				if (pending.Sender == sender)
					activeTransferBytes += pending.PayloadBytes;
			}
			if (
				activeTransfers >= BasisImagePickupSettings.MaxInboundTransfersPerSender
			)
			{
				reason = "too many animation transfers";
				return false;
			}
			if (
				activeTransferBytes
				> BasisImagePickupSettings.MaxInboundAnimationNetworkBytesPerSender
			)
			{
				reason = "aggregate animation transfer budget";
				return false;
			}

			return true;
		}

		private bool CanAttachRemoteAnimation(
			ushort sender,
			BasisAnimatedImageData candidate,
			out string reason
		)
		{
			if (
				BasisAnimatedImageData.TotalResidentNativeBytes
				> BasisImagePickupSettings.MaxResidentAnimationNativeBytes
			)
			{
				reason = "global resident animation memory budget exceeded";
				return false;
			}

			long decodedFramePixels = candidate.DecodedFramePixels;
			long canvasPixels = (long)candidate.CanvasWidth * candidate.CanvasHeight;

			foreach (BasisImagePickupObject pickup in _images.Values)
			{
				if (
					pickup == null
					|| pickup.OwnerId != sender
					|| pickup.AnimatedImagePlayer == null
					|| pickup.AnimatedImagePlayer.Data == null
				)
				{
					continue;
				}

				BasisAnimatedImageData existing = pickup.AnimatedImagePlayer.Data;
				decodedFramePixels += existing.DecodedFramePixels;
				canvasPixels += (long)existing.CanvasWidth * existing.CanvasHeight;
			}

			if (
				decodedFramePixels
				> BasisImagePickupSettings.MaxRemoteAnimationDecodedFramePixelsPerSender
			)
			{
				reason = "aggregate decoded animation pixel budget exceeded";
				return false;
			}
			if (
				canvasPixels
				> BasisImagePickupSettings.MaxRemoteAnimationCanvasPixelsPerSender
			)
			{
				reason = "aggregate animation canvas budget exceeded";
				return false;
			}

			reason = null;
            return true;
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
					_inbound.Remove(_scratchIds[i]);
			}

			if (_inboundAnimations.Count > 0)
			{
				_scratchIds.Clear();
				foreach (
					KeyValuePair<
						Guid,
						InboundAnimationTransfer
					> entry in _inboundAnimations
				)
				{
					if (now >= entry.Value.Deadline)
						_scratchIds.Add(entry.Key);
				}
				int expiredAnimationCount = _scratchIds.Count;
				for (int i = 0; i < expiredAnimationCount; i++)
					RemoveInboundAnimationTransfer(_scratchIds[i]);
			}
		}

		private void RemoveInboundAnimationTransfer(Guid id)
		{
			if (
				!_inboundAnimations.TryGetValue(
					id,
					out InboundAnimationTransfer transfer
				)
			)
				return;
			_inboundAnimations.Remove(id);
			_animationAttempted.Remove(id);
			if (transfer.Buffer.IsCreated)
				transfer.Buffer.Dispose();
			if (transfer.Received.IsCreated)
				transfer.Received.Dispose();
        }

        private void RemoveImage(Guid id)
        {
            if (_images.TryGetValue(id, out BasisImagePickupObject pickup))
            {
                if (pickup != null)
                {
					pickup.AnimatedImagePlayer?.ClearReloadPayload();
                    Destroy(pickup.gameObject);
                }
                _images.Remove(id);
            }
			RemoveOutboundAnimationTransfers(id);
			if (_owned.TryGetValue(id, out OwnedImage owned))
				owned.AnimationPayload?.Dispose();
            _owned.Remove(id);
			_inbound.Remove(id);
			RemoveInboundAnimationTransfer(id);
			for (int i = _queuedInboundAnimationDecodes.Count - 1; i >= 0; i--)
			{
				if (_queuedInboundAnimationDecodes[i].Id != id)
					continue;
				RemoveQueuedInboundAnimationDecode(i);
			}
			for (int i = _pendingInboundAnimationDecodes.Count - 1; i >= 0; i--)
			{
				if (_pendingInboundAnimationDecodes[i].Id != id)
					continue;
				_pendingInboundAnimationDecodes[i].Job?.Dispose();
				_pendingInboundAnimationDecodes.RemoveAt(i);
			}
			_animationAttempted.Remove(id);
            BasisShareableRegistry.Unregister(id.ToString());
        }

        private void IncrementSenderCount(ushort sender)
        {
            _imageCountBySender.TryGetValue(sender, out int count);
            _imageCountBySender[sender] = count + 1;
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

        private void DecrementSenderCount(ushort sender)
        {
            if (_imageCountBySender.TryGetValue(sender, out int count))
            {
                if (count <= 1) _imageCountBySender.Remove(sender);
                else _imageCountBySender[sender] = count - 1;
            }
        }

        private void SendSpawn(Guid id, ushort ownerId, string ownerName, int width, int height, byte[] png, Vector3 position, Quaternion rotation, ushort[] recipients)
        {
            int chunkSize = BasisImagePickupSettings.ChunkPayloadBytes;
            int totalChunks = (png.Length + chunkSize - 1) / chunkSize;

			SendCustomNetworkEventDirect(
				EncodeSpawn(
					id,
					ownerId,
					ownerName,
					width,
					height,
					png.Length,
					totalChunks,
					position,
					rotation
				),
				DeliveryMethod.ReliableOrdered,
				recipients
			);

            for (int i = 0; i < totalChunks; i++)
            {
                int offset = i * chunkSize;
                int length = Mathf.Min(chunkSize, png.Length - offset);
				SendCustomNetworkEventDirect(
					EncodeChunk(id, i, png, offset, length),
					DeliveryMethod.ReliableOrdered,
					recipients
				);
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

					if (
						!transfer.PacketJob.TryComplete(
							out BasisAnimationPacketBatch packetBatch
						)
					)
					{
						return;
					}
					transfer.PacketJob.Dispose();
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
						BasisDebug.LogWarning(
							"Image pickup: first native packet batch has no header.",
							LogTag
						);
						_outboundAnimations.Dequeue();
						DisposeOutboundAnimationTransfer(transfer);
						continue;
					}
					if (
						transfer.HeaderBuffer == null
						|| transfer.HeaderBuffer.Length != headerLength
					)
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
				while (
					chunksRemaining > 0
					&& batchIndex >= 0
					&& batchIndex < transfer.Packets.PacketCount
				)
				{
					int packetLength = transfer.Packets.GetPacketLength(batchIndex);
					int fullPacketLength =
						BasisAnimatedImageNetworkCodec.AnimationChunkHeaderSize
						+ BasisImagePickupSettings.ChunkPayloadBytes;
					byte[] packet;
					if (packetLength == fullPacketLength)
					{
						if (
							transfer.FullChunkBuffer == null
							|| transfer.FullChunkBuffer.Length != packetLength
						)
						{
							transfer.FullChunkBuffer = new byte[packetLength];
						}
						packet = transfer.FullChunkBuffer;
					}
					else
					{
						if (
							transfer.TailChunkBuffer == null
							|| transfer.TailChunkBuffer.Length != packetLength
						)
						{
							transfer.TailChunkBuffer = new byte[packetLength];
						}
						packet = transfer.TailChunkBuffer;
					}
					transfer.Packets.CopyPacketTo(batchIndex, packet);

					SendCustomNetworkEventDirect(
						packet,
						DeliveryMethod.ReliableOrdered,
						transfer.Recipients
					);
					transfer.NextChunkIndex++;
					batchIndex++;
					chunksRemaining--;
				}
				if (transfer == null)
					continue;

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
				if (
					transfer.Recipients == null
					|| Array.IndexOf(transfer.Recipients, recipient) < 0
				)
				{
					_outboundAnimations.Enqueue(transfer);
				}
				else
				{
					DisposeOutboundAnimationTransfer(transfer);
				}
			}
		}

		private static void DisposeOutboundAnimationTransfer(
			OutboundAnimationTransfer transfer
		)
		{
			transfer?.PacketJob?.Dispose();
			transfer?.Packets?.Dispose();
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
            writer.Write(ownerName ?? string.Empty);
            writer.Write(width);
            writer.Write(height);
            writer.Write(totalBytes);
            writer.Write(totalChunks);
            WritePose(writer, position, rotation);
            writer.Flush();
            return stream.ToArray();
        }

		private static byte[] EncodeChunk(
			Guid id,
			int chunkIndex,
			byte[] source,
			int offset,
			int length
		)
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

		private static byte[] EncodeTransform(
			Guid id,
			Vector3 position,
			Quaternion rotation,
			float scale
		)
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

		private static void WritePose(
			BinaryWriter writer,
			Vector3 position,
			Quaternion rotation
		)
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
			return CalculateBatchSpawnLocalOffset(
				index,
				count,
				columns,
				float.NegativeInfinity
			);
		}

		internal static Vector3 CalculateBatchSpawnLocalOffset(
			int index,
			int count,
			int columns,
			float minimumLocalY
		)
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

		internal static int CalculateBatchSpawnColumns(
			int count,
			float batchCenterY,
			float minimumCenterY
		)
		{
			if (count <= 1)
				return 1;

			float verticalSpacing = Mathf.Max(
				0.01f,
				BasisImagePickupSettings.BatchSpawnVerticalSpacingMeters
			);
			float availableDownward = Mathf.Max(0f, batchCenterY - minimumCenterY);
			int rowsWithoutCrossingMinimum = Mathf.Max(
				1,
				Mathf.FloorToInt(availableDownward * 2f / verticalSpacing) + 1
			);
			int requiredColumns = Mathf.CeilToInt(
				count / (float)rowsWithoutCrossingMinimum
			);
			int defaultColumns = Mathf.Min(
				BasisImagePickupSettings.BatchSpawnColumns,
				count
			);
			int maximumColumns = Mathf.Min(
				BasisImagePickupSettings.BatchSpawnMaximumColumns,
				count
			);
			return Mathf.Clamp(
				Mathf.Max(defaultColumns, requiredColumns),
				1,
				maximumColumns
			);
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
			if (
				BasisLocalCameraDriver.HasInstance
				&& BasisLocalCameraDriver.Instance != null
			)
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
