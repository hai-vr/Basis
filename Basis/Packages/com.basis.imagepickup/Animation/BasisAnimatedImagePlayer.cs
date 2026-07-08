using System;
using Basis.Scripts.Networking;
using UnityEngine;
using UnityEngine.Rendering;

namespace Basis.ImagePickup
{
	/// <summary>
	/// Per-pickup animation state. Playback position is derived from an absolute synchronized
	/// epoch; no frame-by-frame network messages or accumulated delta time are required.
	/// Animation compositor resources are created lazily. Reloadable local animations also release
	/// decoded frame data after remaining outside or occluded from gameplay cameras for a grace period.
	/// </summary>
	public sealed class BasisAnimatedImagePlayer : MonoBehaviour
	{
		private const BasisDebug.LogTag LogTag = BasisDebug.LogTag.Rendering;

		private BasisAnimatedImageData _data;
		private BasisNativeAnimationPayload _reloadPayload;
		private BasisAnimationDecodeJobRequest _reloadRequest;
		private BasisImagePickupObject _pickup;
		private BasisAnimatedImageScheduler _scheduler;
		private BasisAnimatedImageGpuCanvas _gpuCanvas;
		private BasisAnimatedImageCpuCanvas _cpuCanvas;
		private long _playbackEpochUtcTicks;
		private long _reloadNativeByteEstimate;
		private long _reservedReloadNativeBytes;
		private float _lastVisibleTime;
		private float _nextFaceOcclusionCheckTime;
		private float _depthVisibilityTimestamp;
		private bool _faceVisible;
		private bool _depthVisible;
		private bool _depthVisibilityValid;
		private bool _depthVisibilityFromGpu;
		private bool _preferCpuFallback;
		private bool _reloadDecodeSlotHeld;
		private bool _reloadFailed;
		private bool _hasAnyAlpha;
		private bool _hasPartialAlpha;
		private bool _initialized;
		private bool _displayTextureBound;
		private bool _destroying;

		public bool IsInitialized => _initialized;
		public bool IsHidden => _pickup == null || _pickup.IsHidden;
		internal BasisImagePickupObject Pickup => _pickup;
		internal bool IsFaceVisible => _faceVisible;
		public long PlaybackEpochUtcTicks => _playbackEpochUtcTicks;
		public BasisAnimatedImageData Data => _data;
		internal bool CanReleaseDecodedData =>
			_data != null && _reloadPayload != null && _reloadPayload.IsCreated;
		public Texture OutputTexture =>
			_gpuCanvas != null ? _gpuCanvas.OutputTexture : _cpuCanvas?.OutputTexture;
		internal bool HasAllocatedCompositor =>
			_gpuCanvas != null || _cpuCanvas != null;

		internal bool Initialize(
			BasisAnimatedImageData data,
			BasisImagePickupObject pickup,
			long playbackEpochUtcTicks,
			bool forceCpuFallback = false,
			BasisNativeAnimationPayload reloadPayload = null
		)
		{
			if (_initialized || data == null || pickup == null)
				return false;

			_data = data;
			_reloadPayload = reloadPayload;
			_reloadNativeByteEstimate = data.NativeByteCount;
			_hasAnyAlpha = data.HasAnyAlpha;
			_hasPartialAlpha = data.HasPartialAlpha;
			_pickup = pickup;
			_playbackEpochUtcTicks =
				playbackEpochUtcTicks > 0
					? playbackEpochUtcTicks
					: BasisNetworkManagement.RemoteUtcTime().Ticks;
			_preferCpuFallback = forceCpuFallback;
			_lastVisibleTime = Time.unscaledTime;
			_nextFaceOcclusionCheckTime = 0f;
			_faceVisible = false;
			_depthVisibilityTimestamp = 0f;
			_depthVisible = false;
			_depthVisibilityValid = false;
			_depthVisibilityFromGpu = false;
			_scheduler = BasisAnimatedImageScheduler.EnsureInstance();

			// Keep the static poster bound until a rendering camera can actually see this pickup.
			// This avoids allocating a full animation atlas and one or two full-size canvases for
			// images that spawn behind the player or outside every rendering camera frustum.
			_initialized = true;
			_scheduler.Register(this);
			return true;
		}

		internal bool NeedsFaceOcclusionCheck(float unscaledTime)
		{
			return unscaledTime >= _nextFaceOcclusionCheckTime;
		}

		internal void SetFaceVisibility(bool visible, float unscaledTime)
		{
			_faceVisible = visible;
			_nextFaceOcclusionCheckTime =
				unscaledTime
				+ BasisImagePickupSettings.AnimationFaceOcclusionCheckIntervalSeconds;
		}

		internal void ResetFaceVisibility()
		{
			_faceVisible = false;
			_nextFaceOcclusionCheckTime = 0f;
		}

		internal bool DepthVisibilityFromGpu =>
			_depthVisibilityValid && _depthVisibilityFromGpu;

		internal void SetDepthVisibility(
			bool visible,
			float unscaledTime,
			bool fromGpu = true
		)
		{
			_depthVisible = visible;
			_depthVisibilityTimestamp = unscaledTime;
			_depthVisibilityValid = true;
			_depthVisibilityFromGpu = fromGpu;
		}

		internal bool TryGetDepthVisibility(float unscaledTime, out bool visible)
		{
			visible = _depthVisible;
			return _depthVisibilityValid
				&& unscaledTime - _depthVisibilityTimestamp
					<= BasisImagePickupSettings.AnimationDepthVisibilityResultMaxAgeSeconds;
		}

		internal void ResetDepthVisibility()
		{
			_depthVisible = false;
			_depthVisibilityTimestamp = 0f;
			_depthVisibilityValid = false;
			_depthVisibilityFromGpu = false;
		}

		internal void UpdateVisibilityState(bool visible, float unscaledTime)
		{
			if (!_initialized)
				return;

			if (_reloadRequest != null && _reloadRequest.IsCompleted)
				CompleteReload(visible);

			if (visible)
			{
				_lastVisibleTime = unscaledTime;
				return;
			}

			if (
				unscaledTime - _lastVisibleTime
				< BasisImagePickupSettings.AnimationOffscreenResourceReleaseSeconds
			)
			{
				return;
			}

			if (HasAllocatedCompositor || CanReleaseDecodedData)
				SuspendToPoster(true);
		}

		internal void ReleaseDecodedDataForMemoryPressure()
		{
			if (!_initialized || !CanReleaseDecodedData)
				return;
			SuspendToPoster(true);
		}

		internal void Schedule(
			CommandBuffer commands,
			long synchronizedUtcTicks,
			ref int transitionsRemaining,
			ref long pixelsRemaining,
			bool allowOversizedJob,
			ref bool gpuCommandsAdded
		)
		{
			if (!_initialized || !EnsureAnimationData() || !EnsureCompositor())
				return;

			long elapsedTicks = synchronizedUtcTicks - _playbackEpochUtcTicks;
			long elapsedMicroseconds = elapsedTicks <= 0 ? 0 : elapsedTicks / 10L;
			_data.GetPlaybackState(
				elapsedMicroseconds,
				out long targetPlayIndex,
				out int targetFrameIndex,
				out _
			);

			int transitions;
			long pixels;
			if (_gpuCanvas != null)
			{
				if (!_gpuCanvas.EnsureCreated())
				{
					SwitchToCpuFallback();
					if (_cpuCanvas == null)
						return;
				}
			}

			if (_gpuCanvas != null)
				_gpuCanvas.EstimateWork(
					targetPlayIndex,
					targetFrameIndex,
					out transitions,
					out pixels
				);
			else
				_cpuCanvas.EstimateWork(
					targetPlayIndex,
					targetFrameIndex,
					out transitions,
					out pixels
				);

			if (transitions == 0)
			{
				BindDisplayTexture();
				return;
			}
			if (
				!allowOversizedJob
				&& (transitions > transitionsRemaining || pixels > pixelsRemaining)
			)
				return;

			if (_gpuCanvas != null)
			{
				int appended = _gpuCanvas.AppendToState(
					commands,
					targetPlayIndex,
					targetFrameIndex
				);
				if (appended <= 0)
					return;
				gpuCommandsAdded = true;
			}
			else
			{
				_cpuCanvas.UpdateToState(targetPlayIndex, targetFrameIndex);
			}

			BindDisplayTexture();
			transitionsRemaining = Math.Max(0, transitionsRemaining - transitions);
			pixelsRemaining = Math.Max(0, pixelsRemaining - pixels);
		}

		private bool EnsureAnimationData()
		{
			if (_data != null)
				return true;
			if (
				_reloadFailed
				|| _reloadPayload == null
				|| !_reloadPayload.IsCreated
				|| _scheduler == null
			)
			{
				return false;
			}

			if (_reloadRequest != null)
				return CompleteReload(true);
			if (!_scheduler.TryAcquireReloadDecodeSlot(_reloadNativeByteEstimate))
				return false;

			_reloadDecodeSlotHeld = true;
			_reservedReloadNativeBytes = _reloadNativeByteEstimate;
			try
			{
				_reloadRequest = BasisAnimatedImageJobs.ScheduleNetworkDecode(
					_reloadPayload.Bytes,
					_reloadPayload.Length,
					false
				);
				return false;
			}
			catch (BasisAnimationMemoryBudgetException)
			{
				ReleaseReloadDecodeSlot();
				return false;
			}
			catch (Exception exception)
			{
				ReleaseReloadDecodeSlot();
				_reloadFailed = true;
				BasisDebug.LogWarning(
					$"Animated image could not restore its decoded frame data: {exception.Message}",
					LogTag
				);
				return false;
			}
		}

		private bool CompleteReload(bool keepResult)
		{
			if (_reloadRequest == null)
				return _data != null;
			if (!_reloadRequest.TryComplete(out BasisAnimationDecodeJobResult result))
			{
				return false;
			}

			try
			{
				if (
					keepResult
					&& result != null
					&& result.Ok
					&& result.Animation != null
				)
				{
					_data = result.Animation;
					result.Animation = null;
					_reloadNativeByteEstimate = _data.NativeByteCount;
					_hasAnyAlpha = _data.HasAnyAlpha;
					_hasPartialAlpha = _data.HasPartialAlpha;
					_reloadFailed = false;
					return true;
				}

				if (keepResult)
				{
					_reloadFailed = true;
					BasisDebug.LogWarning(
						$"Animated image could not restore its decoded frame data: "
							+ $"{result?.Error ?? "no result"}.",
						LogTag
					);
				}
				return false;
			}
			finally
			{
				_reloadRequest.Dispose();
				_reloadRequest = null;
				ReleaseReloadDecodeSlot();
			}
		}

		private void ReleaseReloadDecodeSlot()
		{
			if (!_reloadDecodeSlotHeld)
				return;
			_reloadDecodeSlotHeld = false;
			long reservedBytes = _reservedReloadNativeBytes;
			_reservedReloadNativeBytes = 0;
			if (_scheduler != null)
				_scheduler.ReleaseReloadDecodeSlot(reservedBytes);
			else
				BasisAnimatedImageData.ReleaseRestoreBytes(reservedBytes);
		}

		internal void ClearReloadPayload()
		{
			_reloadPayload = null;
		}

		private bool EnsureCompositor()
		{
			if (_gpuCanvas != null || _cpuCanvas != null)
				return OutputTexture != null;

			if (
				!_preferCpuFallback
				&& _scheduler != null
				&& _scheduler.HasGpuCompositor
				&& SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null
			)
			{
				try
				{
					_gpuCanvas = new BasisAnimatedImageGpuCanvas(
						_data,
						_scheduler.CompositorMaterial
					);
				}
				catch (Exception exception)
				{
					_gpuCanvas?.Dispose();
					_gpuCanvas = null;
					_preferCpuFallback = true;
					BasisDebug.LogWarning(
						$"Animated image GPU canvas failed; using CPU fallback: {exception.Message}",
						LogTag
					);
				}
			}

			if (_gpuCanvas == null)
			{
				try
				{
					_cpuCanvas = new BasisAnimatedImageCpuCanvas(_data);
				}
				catch (Exception exception)
				{
					BasisDebug.LogError(
						$"Animated image CPU canvas failed: {exception.Message}",
						LogTag
					);
					_initialized = false;
					return false;
				}
			}

			if (OutputTexture != null)
				return true;
			DisposeCanvases();
			return false;
		}

		private void SwitchToCpuFallback()
		{
			if (_cpuCanvas != null)
				return;

			_preferCpuFallback = true;
			_gpuCanvas?.Dispose();
			_gpuCanvas = null;
			try
			{
				_cpuCanvas = new BasisAnimatedImageCpuCanvas(_data);
				_displayTextureBound = false;
				BasisDebug.LogWarning(
					"Animated image switched to the CPU compositor fallback.",
					LogTag
				);
			}
			catch (Exception exception)
			{
				BasisDebug.LogError(
					$"Animated image fallback failed: {exception.Message}",
					LogTag
				);
				_cpuCanvas?.Dispose();
				_cpuCanvas = null;
				_initialized = false;
			}
		}

		private void SuspendToPoster(bool releaseDecodedData)
		{
			_pickup?.SetPosterDisplayTexture();
			_displayTextureBound = false;
			DisposeCanvases();
			if (releaseDecodedData && CanReleaseDecodedData)
			{
				_data.Dispose();
				_data = null;
			}
		}

		private void BindDisplayTexture()
		{
			if (_displayTextureBound || _pickup == null || OutputTexture == null)
				return;
			_pickup.SetAnimatedDisplayTexture(
				OutputTexture,
				_hasAnyAlpha,
				_hasPartialAlpha
			);
			_displayTextureBound = true;
		}

		private void OnDestroy()
		{
			if (_destroying)
				return;
			_destroying = true;
			_reloadRequest?.Dispose();
			_reloadRequest = null;
			ReleaseReloadDecodeSlot();
			if (_scheduler != null)
				_scheduler.Unregister(this);
			DisposeCanvases();
			_data?.Dispose();
			_data = null;
			_reloadPayload = null;
			_initialized = false;
		}

		private void DisposeCanvases()
		{
			_gpuCanvas?.Dispose();
			_gpuCanvas = null;
			_cpuCanvas?.Dispose();
			_cpuCanvas = null;
		}
	}
}
