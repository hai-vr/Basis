using System.Collections.Generic;
using Basis.EventDriver;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace Basis.ImagePickup
{
    /// <summary>
    /// Advances image-pickup animations only while their front face is visible to a gameplay
    /// camera. A per-frame CPU facing/frustum broad phase rejects cards before depth, raycast,
    /// decode, or composition work. Desktop builds then prefer camera-depth visibility, while
    /// mobile and portable platforms use front-face physics samples. Hidden players retain their
    /// synchronized epoch and later resume at the correct frame.
    /// </summary>
    public sealed class BasisAnimatedImageScheduler : MonoBehaviour
    {
        private const string CompositorShaderName =
            "Hidden/Basis/ImageAnimationComposite";
        private const BasisDebug.LogTag LogTag = BasisDebug.LogTag.Rendering;
        private const int FrontFaceSampleCount = 5;
        private const int RaycastHitBufferSize = 16;
        private const int MaximumCpuFacingCameraBits = 64;

        private static readonly ProfilerMarker ScheduleMarker = new("Basis.ImagePickup.AnimatedImage.Schedule");
        private static readonly ProfilerMarker GpuCommandsMarker = new("Basis.ImagePickup.AnimatedImage.GpuCommands");
        private static readonly ProfilerMarker CpuFrontFacingMarker = new(
            "Basis.ImagePickup.AnimatedImage.CpuFrontFacing"
        );

        public static BasisAnimatedImageScheduler Instance;

        private readonly List<BasisAnimatedImagePlayer> _players = new(64);
        private readonly List<BasisAnimatedImagePlayer> _pendingRemoval = new(8);
        private readonly List<BasisAnimatedImagePlayer> _pendingDecodedReleases = new(4);
        private readonly List<BasisAnimatedImagePlayer> _cpuFrontFacingPlayers = new(64);
        private readonly List<BasisAnimatedImagePlayer> _reloadDecodePlayers = new(2);
        private readonly List<Camera> _visibilityCameras = new(8);
        private readonly List<Vector3> _visibilityCameraPositions = new(8);
        private readonly List<Vector3> _visibilityCameraForwards = new(8);
        private readonly List<bool> _visibilityCameraOrthographic = new(8);
        private readonly List<Camera> _registeredCameraScratch = new(8);
        private readonly List<Plane[]> _visibilityFrustums = new(8);
        private readonly RaycastHit[] _raycastHits = new RaycastHit[
            RaycastHitBufferSize
        ];
        private CommandBuffer _commands;
        private Material _compositorMaterial;
        private BasisAnimatedImageDepthVisibility _depthVisibility;
        private int _localVisibilityCameraIndex = -1;
        private int _activeReloadDecodes;
        private int _visiblePassStartIndex;
        private bool _cameraMaskLimitWarningLogged;

        internal Material CompositorMaterial => _compositorMaterial;
        internal bool HasGpuCompositor => _compositorMaterial != null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            BasisEventDriver.OnLateUpdate += Simulate;
            BasisDebug.Log(
                BasisImagePickupSettings.UseDepthBufferAnimationVisibility
                    ? "Image pickup animation visibility uses the depth buffer."
                    : "Image pickup animation visibility uses front-face physics.",
                LogTag
            );
            _commands = new CommandBuffer { name = "Basis Animated Images" };
            for (int i = 0; i < _visibilityFrustums.Capacity; i++)
                _visibilityFrustums.Add(new Plane[6]);
            if (BasisImagePickupSettings.UseDepthBufferAnimationVisibility)
                _depthVisibility = BasisAnimatedImageDepthVisibility.EnsureInstance(gameObject);

            Shader shader = Shader.Find(CompositorShaderName);
            if (shader != null)
            {
                _compositorMaterial = new Material(shader)
                {
                    name = "Basis Animated Image Compositor",
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
            else
            {
                BasisDebug.LogWarning(
                    "Animated image GPU compositor shader was not found; using the CPU fallback.",
                    LogTag
                );
            }
        }

        public static BasisAnimatedImageScheduler EnsureInstance()
        {
            if (Instance != null)
                return Instance;

            GameObject host;
            if (BasisImagePickupManager.Instance != null)
            {
                host = BasisImagePickupManager.Instance.gameObject;
            }
            else
            {
                host = new GameObject("BasisAnimatedImageScheduler");
                if (Application.isPlaying)
                    DontDestroyOnLoad(host);
            }

            if (!host.TryGetComponent(out BasisAnimatedImageScheduler scheduler))
                scheduler = host.AddComponent<BasisAnimatedImageScheduler>();
            return scheduler;
        }

        internal void Register(BasisAnimatedImagePlayer player)
        {
            if (player == null || _players.Contains(player))
                return;
            _players.Add(player);
        }

        internal void Unregister(BasisAnimatedImagePlayer player)
        {
            if (player == null)
                return;
            int playerIndex = _players.IndexOf(player);
            if (playerIndex >= 0)
                RemovePlayerAt(playerIndex);
        }

        internal bool TryAcquireReloadDecodeSlot(BasisAnimatedImagePlayer player, long nativeBytes)
        {
            return TryAcquireReloadDecodeSlot(
                player,
                nativeBytes,
                BasisImagePickupSettings.MaxRemoteAnimationDecodedFramePixelsPerSender
            );
        }

        internal bool TryAcquireReloadDecodeSlot(
            BasisAnimatedImagePlayer player,
            long nativeBytes,
            long decodedPixelLimit
        )
        {
            if (
                player == null
                || _activeReloadDecodes >= BasisImagePickupSettings.MaxConcurrentAnimationDecodeJobs
                || _reloadDecodePlayers.Contains(player)
                || _pendingDecodedReleases.Count > 0
                || !TryMakeRoomForDecodedPixels(player, decodedPixelLimit)
                || !BasisAnimatedImageData.TryReserveRestoreBytes(nativeBytes)
            )
            {
                return false;
            }
            _activeReloadDecodes++;
            _reloadDecodePlayers.Add(player);
            return true;
        }

        internal void ReleaseReloadDecodeSlot(BasisAnimatedImagePlayer player, long nativeBytes)
        {
            int playerIndex = _reloadDecodePlayers.IndexOf(player);
            if (playerIndex >= 0)
            {
                _reloadDecodePlayers.RemoveAt(playerIndex);
                if (_activeReloadDecodes > 0)
                    _activeReloadDecodes--;
            }
            BasisAnimatedImageData.ReleaseRestoreBytes(nativeBytes);
        }

        internal void EnforceDecodedPixelBudget(BasisAnimatedImagePlayer candidate)
        {
            EnforceDecodedPixelBudget(
                candidate,
                BasisImagePickupSettings.MaxRemoteAnimationDecodedFramePixelsPerSender
            );
        }

        internal void EnforceDecodedPixelBudget(BasisAnimatedImagePlayer candidate, long decodedPixelLimit)
        {
            if (candidate == null || !candidate.HasDecodedData)
                return;
            TrimDecodedPixelBudget(candidate, false, false, decodedPixelLimit);
        }

        private bool TryMakeRoomForDecodedPixels(
            BasisAnimatedImagePlayer candidate,
            long decodedPixelLimit
        )
        {
            return TrimDecodedPixelBudget(candidate, true, true, decodedPixelLimit);
        }

        private bool TrimDecodedPixelBudget(
            BasisAnimatedImagePlayer candidate,
            bool candidateNeedsDecode,
            bool deferReleases,
            long decodedPixelLimit
        )
        {
            long limit = decodedPixelLimit;
            long candidatePixels = candidate.DecodedFramePixels;
            if (limit <= 0)
                return false;
            if (candidatePixels <= 0 || candidatePixels > limit)
                return false;

            ushort ownerId = candidate.OwnerId;
            long decodedPixels = candidateNeedsDecode ? candidatePixels : 0;
            int playerCount = _players.Count;
            for (int i = 0; i < playerCount; i++)
            {
                BasisAnimatedImagePlayer player = _players[i];
                if (player == null || player.OwnerId != ownerId || !player.HasDecodedData)
                    continue;
                decodedPixels += player.DecodedFramePixels;
            }

            int reloadDecodeCount = _reloadDecodePlayers.Count;
            for (int i = 0; i < reloadDecodeCount; i++)
            {
                BasisAnimatedImagePlayer player = _reloadDecodePlayers[i];
                if (
                    player == null
                    || ReferenceEquals(player, candidate)
                    || player.OwnerId != ownerId
                    || player.HasDecodedData
                )
                {
                    continue;
                }
                decodedPixels += player.DecodedFramePixels;
            }

            Camera localCamera = BasisLocalCameraDriver.CameraInstance;
            Vector3 cameraPosition = localCamera != null ? localCamera.transform.position : Vector3.zero;
            float candidateDistanceSquared =
                localCamera != null ? candidate.GetDistanceSquared(cameraPosition) : float.PositiveInfinity;

            if (candidateNeedsDecode && decodedPixels > limit)
            {
                long reclaimablePixels = 0;
                playerCount = _players.Count;
                for (int i = 0; i < playerCount; i++)
                {
                    BasisAnimatedImagePlayer player = _players[i];
                    if (
                        player == null
                        || player.OwnerId != ownerId
                        || !player.HasDecodedData
                        || !player.CanReleaseDecodedData
                    )
                    {
                        continue;
                    }

                    float distanceSquared =
                        localCamera != null
                            ? player.GetDistanceSquared(cameraPosition)
                            : 0f;
                    if (distanceSquared <= candidateDistanceSquared)
                        continue;
                    reclaimablePixels += player.DecodedFramePixels;
                }

                if (decodedPixels - reclaimablePixels > limit)
                    return false;
            }

            bool releaseDeferred = false;
            while (decodedPixels > limit)
            {
                BasisAnimatedImagePlayer farthest = null;
                float farthestDistanceSquared = -1f;
                playerCount = _players.Count;
                for (int i = 0; i < playerCount; i++)
                {
                    BasisAnimatedImagePlayer player = _players[i];
                    if (
                        player == null
                        || player.OwnerId != ownerId
                        || !player.HasDecodedData
                        || !player.CanReleaseDecodedData
                        || (deferReleases && _pendingDecodedReleases.Contains(player))
                    )
                    {
                        continue;
                    }

                    float distanceSquared;
                    if (localCamera != null)
                        distanceSquared = player.GetDistanceSquared(cameraPosition);
                    else
                        distanceSquared = ReferenceEquals(player, candidate) ? float.PositiveInfinity : 0f;
                    if (distanceSquared <= farthestDistanceSquared)
                        continue;
                    farthest = player;
                    farthestDistanceSquared = distanceSquared;
                }

                if (
                    farthest == null
                    || (
                        candidateNeedsDecode
                        && farthestDistanceSquared <= candidateDistanceSquared
                    )
                )
                {
                    return false;
                }

                decodedPixels -= farthest.DecodedFramePixels;
                if (deferReleases)
                {
                    _pendingDecodedReleases.Add(farthest);
                    releaseDeferred = true;
                }
                else
                {
                    farthest.ReleaseDecodedDataForMemoryPressure();
                }
            }

            return !releaseDeferred;
        }

        internal void ApplyPendingDecodedReleases()
        {
            int pendingReleaseCount = _pendingDecodedReleases.Count;
            for (int i = 0; i < pendingReleaseCount; i++)
            {
                BasisAnimatedImagePlayer player = _pendingDecodedReleases[i];
                if (player != null)
                    player.ReleaseDecodedDataForMemoryPressure();
            }
            _pendingDecodedReleases.Clear();
        }

        private void Simulate()
        {
            try
            {
                SimulateBody();
            }
            catch (System.Exception exception)
            {
                BasisDebug.LogErrorOnce($"Animated image scheduler simulation failed: {exception}", LogTag);
            }
        }

        private void SimulateBody()
        {
            if (_players.Count == 0)
            {
                _pendingDecodedReleases.Clear();
                return;
            }

            using (ScheduleMarker.Auto())
            {
                int registeredPlayerCount = _players.Count;
                for (int i = registeredPlayerCount - 1; i >= 0; i--)
                {
                    if (_players[i] == null)
                        RemovePlayerAt(i);
                }
                if (_players.Count == 0)
                    return;

                _commands.Clear();
                ApplyPendingDecodedReleases();
                EnforceResidentNativeBudget();
                _pendingRemoval.Clear();

                bool useDepthBufferOcclusion =
                    BasisImagePickupSettings.UseDepthBufferAnimationVisibility;
                CollectVisibilityCameras();
                float unscaledTime = Time.unscaledTime;
                PrepareCpuFrontFacingPlayers(Time.frameCount, unscaledTime);

                if (useDepthBufferOcclusion && _depthVisibility != null)
                {
                    _depthVisibility.PrepareFrame(
                        _cpuFrontFacingPlayers,
                        BasisLocalCameraDriver.CameraInstance,
                        unscaledTime
                    );
                }

                int transitionsRemaining =
                    BasisImagePickupSettings.MaxAnimationTransitionsPerFrame;
                long pixelsRemaining =
                    BasisImagePickupSettings.MaxAnimationCompositedPixelsPerFrame;
                int raycastsRemaining =
                    BasisImagePickupSettings.MaxAnimationFaceOcclusionRaycastsPerFrame;
                bool gpuCommandsAdded = false;
                long synchronizedTicks = BasisNetworkManagement.RemoteUtcTime().Ticks;

				ScheduleVisiblePlayers(
                    synchronizedTicks,
                    unscaledTime,
                    ref _visiblePassStartIndex,
                    useDepthBufferOcclusion,
                    ref transitionsRemaining,
                    ref pixelsRemaining,
                    ref raycastsRemaining,
                    ref gpuCommandsAdded
                );

                int pendingRemovalCount = _pendingRemoval.Count;
                for (int i = 0; i < pendingRemovalCount; i++)
                {
                    int playerIndex = _players.IndexOf(_pendingRemoval[i]);
                    if (playerIndex >= 0)
                        RemovePlayerAt(playerIndex);
                }

                if (gpuCommandsAdded)
                {
                    using (GpuCommandsMarker.Auto())
                    {
                        Graphics.ExecuteCommandBuffer(_commands);
                    }
                }
            }
        }

        private void EnforceResidentNativeBudget()
        {
            long limit = BasisImagePickupSettings.MaxResidentAnimationNativeBytes;
            if (BasisAnimatedImageData.TotalResidentNativeBytes <= limit)
                return;

            int playerCount = _players.Count;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = playerCount - 1; i >= 0; i--)
                {
                    BasisAnimatedImagePlayer player = _players[i];
                    if (player == null || !player.CanReleaseDecodedData || (pass == 0 && player.HasAllocatedCompositor))
                    {
                        continue;
                    }
                    player.ReleaseDecodedDataForMemoryPressure();
                    if (BasisAnimatedImageData.TotalResidentNativeBytes <= limit)
                        return;
                }
            }
        }

        private void RemovePlayerAt(int playerIndex)
        {
            _players.RemoveAt(playerIndex);
            _visiblePassStartIndex = AdjustStartIndexAfterRemoval(_visiblePassStartIndex, playerIndex, _players.Count);
        }

        internal static int AdjustStartIndexAfterRemoval(int startIndex, int removedIndex, int remainingCount)
        {
            if (remainingCount <= 0)
                return 0;
            if (removedIndex < startIndex)
                startIndex--;
            if (startIndex < 0)
                return 0;
            return startIndex % remainingCount;
        }

        private void PrepareCpuFrontFacingPlayers(int frame, float unscaledTime)
        {
            using var scope = CpuFrontFacingMarker.Auto();
            _cpuFrontFacingPlayers.Clear();
            int playerCount = _players.Count;
            for (int playerIndex = 0; playerIndex < playerCount; playerIndex++)
            {
                BasisAnimatedImagePlayer player = _players[playerIndex];
                if (player == null || !player.IsInitialized)
                    continue;

                ulong cameraMask = player.IsHidden
                    ? 0
                    : CalculateCpuFrontFacingCameraMask(player);
                player.SetCpuFrontFacingCameraMask(cameraMask, frame);
                if (cameraMask == 0)
                {
                    player.ResetFaceVisibility();
                    player.ResetDepthVisibility();
                    player.UpdateVisibilityState(false, unscaledTime);
                    continue;
                }
                _cpuFrontFacingPlayers.Add(player);
            }
        }

        private ulong CalculateCpuFrontFacingCameraMask(BasisAnimatedImagePlayer player)
        {
            BasisImagePickupObject pickup = player.Pickup;
            if (pickup == null || !pickup.HasFrontRenderer)
                return 0;

            Bounds bounds = pickup.FrontRendererBounds;
            pickup.GetFrontFacePose(out Vector3 faceCenter, out Vector3 frontNormal);
            int cameraCount = Mathf.Min(_visibilityCameras.Count, MaximumCpuFacingCameraBits);
            ulong cameraMask = 0;
            for (int cameraIndex = 0; cameraIndex < cameraCount; cameraIndex++)
            {
                Camera camera = _visibilityCameras[cameraIndex];
                if (
                    !IsCpuFrontFacingCandidate(
                        pickup.FrontRendererLayer,
                        bounds,
                        _visibilityFrustums[cameraIndex],
                        frontNormal,
                        faceCenter,
                        _visibilityCameraPositions[cameraIndex],
                        _visibilityCameraForwards[cameraIndex],
                        _visibilityCameraOrthographic[cameraIndex],
                        camera.cullingMask
                    )
                )
                {
                    continue;
                }
                cameraMask |= 1UL << cameraIndex;
            }
            return cameraMask;
        }

        internal static bool IsCpuFrontFacingCandidate(
            int rendererLayer,
            Bounds bounds,
            Plane[] frustumPlanes,
            Vector3 frontNormal,
            Vector3 faceCenter,
            Vector3 cameraPosition,
            Vector3 cameraForward,
            bool orthographic,
            int cameraCullingMask
        )
        {
            if (rendererLayer < 0 || rendererLayer > 31)
                return false;
            int layerMaskBit = 1 << rendererLayer;
            if ((cameraCullingMask & layerMaskBit) == 0)
                return false;
            if (!IsFrontFacingCamera(frontNormal, faceCenter, cameraPosition, cameraForward, orthographic))
            {
                return false;
            }
            return frustumPlanes != null
                && frustumPlanes.Length >= 6
                && GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
        }

		private void ScheduleVisiblePlayers(
            long synchronizedTicks,
            float unscaledTime,
            ref int startIndex,
            bool useDepthBufferOcclusion,
            ref int transitionsRemaining,
            ref long pixelsRemaining,
            ref int raycastsRemaining,
            ref bool gpuCommandsAdded
        )
        {
            int playerCount = _players.Count;
            if (playerCount == 0)
				return;

            int normalizedStartIndex = startIndex % playerCount;
            if (normalizedStartIndex < 0)
                normalizedStartIndex += playerCount;

            for (int offset = 0; offset < playerCount; offset++)
            {
                int playerIndex = (normalizedStartIndex + offset) % playerCount;
                BasisAnimatedImagePlayer player = _players[playerIndex];
                if (player == null)
                    continue;
                if (!player.IsInitialized)
                {
                    _pendingRemoval.Add(player);
                    continue;
                }

                bool hasCpuFacingMask = player.TryGetCpuFrontFacingCameraMask(
                    Time.frameCount,
                    out ulong cpuFacingCameraMask
                );
                if (player.IsHidden || !hasCpuFacingMask || cpuFacingCameraMask == 0)
                    continue;

                bool visible = useDepthBufferOcclusion
                    ? EvaluateDepthBufferVisibility(player, unscaledTime, cpuFacingCameraMask, ref raycastsRemaining)
                    : EvaluatePhysicsVisibility(
                        player,
                        unscaledTime,
                        cpuFacingCameraMask,
                        false,
                        ref raycastsRemaining
                    );
                player.UpdateVisibilityState(visible, unscaledTime);
                if (!visible)
                    continue;

                bool allowOversizedFirstJob =
                    transitionsRemaining
                        == BasisImagePickupSettings.MaxAnimationTransitionsPerFrame
                    && pixelsRemaining
                        == BasisImagePickupSettings.MaxAnimationCompositedPixelsPerFrame;

                player.Schedule(
                    _commands,
                    synchronizedTicks,
                    ref transitionsRemaining,
                    ref pixelsRemaining,
                    allowOversizedFirstJob,
                    ref gpuCommandsAdded
                );

                if (transitionsRemaining <= 0 || pixelsRemaining <= 0)
                {
                    startIndex = (playerIndex + 1) % playerCount;
					return;
                }
            }

            // Rotate even when the pass completes without exhausting its budget so the
            // same registration does not permanently receive first consideration.
            startIndex = (normalizedStartIndex + 1) % playerCount;
        }

        private bool EvaluatePhysicsVisibility(
            BasisAnimatedImagePlayer player,
            float unscaledTime,
            ulong cpuFacingCameraMask,
            bool skipLocalCamera,
            ref int raycastsRemaining
        )
        {
            if (
                player.NeedsFaceOcclusionCheck(unscaledTime)
                && TryEvaluateFaceVisibility(
                    player,
                    cpuFacingCameraMask,
                    skipLocalCamera,
                    ref raycastsRemaining,
                    out bool evaluatedVisible
                )
            )
            {
                player.SetFaceVisibility(evaluatedVisible, unscaledTime);
            }
            return player.IsFaceVisible;
        }

        private bool EvaluateDepthBufferVisibility(
            BasisAnimatedImagePlayer player,
            float unscaledTime,
            ulong cpuFacingCameraMask,
            ref int raycastsRemaining
        )
        {
            if (_depthVisibility != null && player.TryGetDepthVisibility(unscaledTime, out bool mainCameraVisible))
            {
                bool mainCameraCpuFacing =
                    _localVisibilityCameraIndex >= 0
                    && _localVisibilityCameraIndex < MaximumCpuFacingCameraBits
                    && (cpuFacingCameraMask & (1UL << _localVisibilityCameraIndex))
                        != 0;
                if (mainCameraVisible && mainCameraCpuFacing)
                    return true;

                // Registered secondary cameras do not share the main camera depth texture.
                return EvaluatePhysicsVisibility(
                    player,
                    unscaledTime,
                    cpuFacingCameraMask,
                    true,
                    ref raycastsRemaining
                );
            }

            // Until the first asynchronous readback arrives, retain the physics path so
            // depth mode never incorrectly freezes newly spawned or newly visible cards.
            return EvaluatePhysicsVisibility(player, unscaledTime, cpuFacingCameraMask, false, ref raycastsRemaining);
        }

        private bool TryEvaluateFaceVisibility(
            BasisAnimatedImagePlayer player,
            ulong cpuFacingCameraMask,
            bool skipLocalCamera,
            ref int raycastsRemaining,
            out bool visible
        )
        {
            visible = false;
            BasisImagePickupObject pickup = player.Pickup;
            if (pickup == null || !pickup.HasFrontRenderer)
                return true;

            pickup.GetFrontFacePose(out _, out Vector3 frontNormal);
            int cameraCount = Mathf.Min(_visibilityCameras.Count, MaximumCpuFacingCameraBits);
            for (int i = 0; i < cameraCount; i++)
            {
                if ((cpuFacingCameraMask & (1UL << i)) == 0)
                    continue;
                Camera camera = _visibilityCameras[i];
                if (skipLocalCamera && ReferenceEquals(camera, BasisLocalCameraDriver.CameraInstance))
                {
                    continue;
                }

                if (
                    !TryHasUnoccludedFaceSample(
                        pickup,
                        camera,
                        _visibilityCameraPositions[i],
                        _visibilityCameraForwards[i],
                        _visibilityCameraOrthographic[i],
                        frontNormal,
                        ref raycastsRemaining,
                        out bool cameraCanSeeFace
                    )
                )
                {
                    return false;
                }
                if (cameraCanSeeFace)
                {
                    visible = true;
                    return true;
                }
            }

            return true;
        }

        internal static bool IsFrontFacingCamera(
            Vector3 frontNormal,
            Vector3 faceCenter,
            Vector3 cameraPosition,
            Vector3 cameraForward,
            bool orthographic
        )
        {
            Vector3 towardCamera = orthographic
                ? -cameraForward
                : cameraPosition - faceCenter;
            return Vector3.Dot(frontNormal, towardCamera) > 0.0001f;
        }

        private bool TryHasUnoccludedFaceSample(
            BasisImagePickupObject pickup,
            Camera camera,
            Vector3 cameraPosition,
            Vector3 cameraForward,
            bool cameraOrthographic,
            Vector3 frontNormal,
            ref int raycastsRemaining,
            out bool visible
        )
        {
            visible = false;
            for (int sampleIndex = 0; sampleIndex < FrontFaceSampleCount; sampleIndex++)
            {
                if (raycastsRemaining <= 0)
                    return false;
                raycastsRemaining--;

                Vector3 sample = pickup.GetFrontFaceOcclusionSample(sampleIndex, frontNormal);
                if (IsFaceSampleUnoccluded(pickup, camera, cameraPosition, cameraForward, cameraOrthographic, sample))
                {
                    visible = true;
                    return true;
                }
            }
            return true;
        }

        private bool IsFaceSampleUnoccluded(
            BasisImagePickupObject pickup,
            Camera camera,
            Vector3 cameraPosition,
            Vector3 cameraForward,
            bool cameraOrthographic,
            Vector3 sample
        )
        {
            Vector3 origin;
            Vector3 direction;
            float distance;

            if (cameraOrthographic)
            {
                direction = cameraForward;
                distance = Vector3.Dot(sample - cameraPosition, direction);
                if (distance <= 0f)
                    return false;
                origin = sample - direction * distance;
            }
            else
            {
                origin = cameraPosition;
                Vector3 toSample = sample - origin;
                distance = toSample.magnitude;
                if (distance <= 0.0001f)
                    return true;
                direction = toSample / distance;
            }

            distance = Mathf.Max(
                0f,
                distance
                    - BasisImagePickupSettings.AnimationFaceOcclusionSurfaceOffsetMeters
                        * 0.5f
            );
            if (distance <= 0.0001f)
                return true;

            // Use the camera's own culling mask so geometry it cannot render does not occlude.
            int layerMask = camera.cullingMask & Physics.DefaultRaycastLayers;
            int hitCount = Physics.RaycastNonAlloc(
                origin,
                direction,
                _raycastHits,
                distance,
                layerMask,
                QueryTriggerInteraction.Collide
            );

            for (int i = 0; i < hitCount; i++)
            {
                Collider collider = _raycastHits[i].collider;
                if (!IsBlockingOcclusionCollider(collider, pickup))
                    continue;
                return false;
            }

            return hitCount < _raycastHits.Length;
        }

        internal static bool IsBlockingOcclusionCollider(Collider collider, BasisImagePickupObject target)
        {
            if (collider == null || target == null || target.OwnsCollider(collider))
                return false;

            if (!collider.isTrigger)
                return true;

            // Image-card colliders are registered by their pickup owner. Unrelated trigger
            // volumes stay invisible without component discovery in this hot path.
            return BasisImagePickupObject.TryGetPickup(collider, out BasisImagePickupObject otherImage)
                && otherImage != target;
        }

        private void CollectVisibilityCameras()
        {
            _visibilityCameras.Clear();
            Camera localCamera = BasisLocalCameraDriver.CameraInstance;
            AddVisibilityCamera(localCamera);
            _localVisibilityCameraIndex = _visibilityCameras.IndexOf(localCamera);

            _registeredCameraScratch.Clear();
            BasisCullingCameraRegistry.CollectInto(_registeredCameraScratch);
            int registeredCameraCount = _registeredCameraScratch.Count;
            for (int i = 0; i < registeredCameraCount; i++)
                AddVisibilityCamera(_registeredCameraScratch[i]);

            int cameraCount = _visibilityCameras.Count;
            while (_visibilityFrustums.Count < cameraCount)
                _visibilityFrustums.Add(new Plane[6]);

            _visibilityCameraPositions.Clear();
            _visibilityCameraForwards.Clear();
            _visibilityCameraOrthographic.Clear();
            for (int i = 0; i < cameraCount; i++)
            {
                Camera camera = _visibilityCameras[i];
                camera.transform.GetPositionAndRotation(out Vector3 cameraPosition, out Quaternion cameraRotation);
                _visibilityCameraPositions.Add(cameraPosition);
                _visibilityCameraForwards.Add(cameraRotation * Vector3.forward);
                _visibilityCameraOrthographic.Add(camera.orthographic);
                GeometryUtility.CalculateFrustumPlanes(camera, _visibilityFrustums[i]);
            }
        }

        private void AddVisibilityCamera(Camera camera)
        {
            if (!IsGameplayVisibilityCamera(camera) || _visibilityCameras.Contains(camera))
            {
                return;
            }
            if (_visibilityCameras.Count >= MaximumCpuFacingCameraBits)
            {
                if (!_cameraMaskLimitWarningLogged)
                {
                    _cameraMaskLimitWarningLogged = true;
                    BasisDebug.LogWarning(
                        $"Animated image visibility supports at most {MaximumCpuFacingCameraBits} gameplay cameras; additional cameras are ignored.",
                        LogTag
                    );
                }
                return;
            }
            _visibilityCameras.Add(camera);
        }

        internal static bool IsSupportedVisibilityCameraType(CameraType cameraType)
        {
            return cameraType != CameraType.SceneView
                && cameraType != CameraType.Preview;
        }

        private static bool IsGameplayVisibilityCamera(Camera camera)
        {
            return camera != null
                && camera.isActiveAndEnabled
                && IsSupportedVisibilityCameraType(camera.cameraType);
        }

        private void OnDestroy()
        {
            BasisEventDriver.OnLateUpdate -= Simulate;
            if (Instance == this)
                Instance = null;

            if (_commands != null)
            {
                _commands.Release();
                _commands = null;
            }
            if (_compositorMaterial != null)
            {
                Destroy(_compositorMaterial);
                _compositorMaterial = null;
            }
            _depthVisibility = null;
            _players.Clear();
            _pendingRemoval.Clear();
            _pendingDecodedReleases.Clear();
            _cpuFrontFacingPlayers.Clear();
            _reloadDecodePlayers.Clear();
            _visibilityCameras.Clear();
            _visibilityCameraPositions.Clear();
            _visibilityCameraForwards.Clear();
            _visibilityCameraOrthographic.Clear();
            _registeredCameraScratch.Clear();
            _visibilityFrustums.Clear();
        }
    }
}
