using UnityEngine;

namespace Basis.ImagePickup
{
	/// <summary>
	/// Tunable limits and persistent runtime options for the image pickup feature.
	/// Caps are enforced on both the sending and receiving side.
	/// </summary>
	public static class BasisImagePickupSettings
	{
		public const string ReceiveEnabledKey = "Basis.ImagePickup.ReceiveEnabled";

		public const int MaxImageBytes = 8 * 1024 * 1024;
		public const int MaxSourceBytes = 32 * 1024 * 1024;
		public const int MaxDimension = 2048;
		public const long MaxTotalPixels = 2048L * 2048L;
		public const int MaxSourceDimension = 4096;
		public const long MaxSourceTotalPixels = 4096L * 4096L;
		public const int ChunkPayloadBytes = 16 * 1024;

		// Large drag batches are allowed by count, then bounded by aggregate decoded pixels and poster bytes.
		public const int MaxConcurrentImagesPerSender = 64;
		public const long MaxRemoteImagePixelsPerSender = 64L * 1024L * 1024L;
		public const long MaxRemoteImageBytesPerSender = 128L * 1024L * 1024L;
		public const int MaxInboundTransfersPerSender = 4;
		public const int SpawnRateBurstAllowance = MaxConcurrentImagesPerSender;
		public const float MinSecondsBetweenSpawnsPerSender = 0.5f;
		public const float InboundTransferTimeoutSeconds = 30f;

		public const float SpawnDistance = 1.5f;
		public const float BaseHeightMeters = 0.5f;
		public const int BatchSpawnColumns = 4;
		public const int BatchSpawnMaximumColumns = 16;
		public const float BatchSpawnHorizontalSpacingMeters = 1.0f;
		public const float BatchSpawnVerticalSpacingMeters = 0.65f;
		public const float BatchSpawnGroundClearanceMeters = 0.05f;

		public const float TransmitTransformHz = 15f;
		public const float MovedPositionEpsilon = 0.001f;
		public const float MovedRotationEpsilonDegrees = 0.5f;
		public const float MovedScaleEpsilon = 0.01f;

		// Animated images use a separate, larger budget than static images. Frame patches remain
		// bounded to 64M decoded pixels (roughly 256 MiB of Color32 data) to avoid unbounded RAM use.
		public const int MaxAnimationSourceBytes = 64 * 1024 * 1024;
		public const int MaxAnimationNetworkBytes = MaxAnimationSourceBytes;
		public const int MaxAnimationDimension = 2048;
		public const long MaxAnimationCanvasPixels = 2048L * 2048L;
		public const int MaxAnimationFrames = 512;
		public const long MaxAnimationDecodedFramePixels = 64L * 1024L * 1024L;
		public const long MaxAnimationNetworkDecodedBytes =
			MaxAnimationDecodedFramePixels * 4L + MaxAnimationFrames * 64L + 1024L;
		public const long MinAnimationFrameDurationMicroseconds = 33334L;
		public const long MaxAnimationDurationMicroseconds = 5L * 60L * 1000L * 1000L;
		public const int MaxAnimationTransitionsPerFrame = 256;
		public const long MaxAnimationCompositedPixelsPerFrame = 32L * 1024L * 1024L;
		public const float AnimationOffscreenResourceReleaseSeconds = 10f;
		public const long MaxResidentAnimationNativeBytes = 2L * 1024L * 1024L * 1024L;
		public const long MaxResidentAnimationPayloadBytes = 1L * 1024L * 1024L * 1024L;
		public const long MaxAnimationNativeWorkingSetBytes =
			3L * 1024L * 1024L * 1024L;

		// Physics-backed front-face visibility is sampled at 10 Hz and globally budgeted per frame.
		public const float AnimationFaceOcclusionCheckIntervalSeconds = 0.1f;
		public const int MaxAnimationFaceOcclusionRaycastsPerFrame = 96;
		public const float AnimationFaceOcclusionSampleHalfExtent = 0.34f;
		public const float AnimationFaceOcclusionSurfaceOffsetMeters = 0.005f;
		public const float AnimationDepthOcclusionBiasMeters = 0.025f;
		public const float AnimationDepthVisibilityResultMaxAgeSeconds = 0.5f;
		public const int AnimationBatchWarningThreshold = 4;
		public const int MaxAnimationNetworkChunksPerFrame = 4;
		public const int AnimationPacketBuildChunksPerJob = 32;

		// Schedule one Burst decode pipeline per logical processor. Unity's job system still
		// controls the worker threads, while the native-memory budgets remain the hard backstop.
		public static int MaxConcurrentAnimationDecodeJobs =>
			CalculateAnimationDecodeJobLimit(SystemInfo.processorCount);

		// Completed network payloads wait compressed; these limits apply only to active native decodes.
		public static int MaxPendingInboundAnimationDecodeJobsPerSender =>
			MaxConcurrentAnimationDecodeJobs;
		public const long MaxPendingInboundAnimationDecodedBytesPerSender =
			320L * 1024L * 1024L;
		public const long MaxInboundAnimationNetworkBytesPerSender =
			128L * 1024L * 1024L;
		public const long MaxRemoteAnimationDecodedFramePixelsPerSender =
			128L * 1024L * 1024L;
		public const long MaxRemoteAnimationCanvasPixelsPerSender = 16L * 1024L * 1024L;

		private static bool _loaded;
		private static bool _receiveEnabled = true;

		public static bool UseDepthBufferAnimationVisibility =>
			ShouldUseDepthBufferAnimationVisibility(Application.isMobilePlatform);

		internal static bool ShouldUseDepthBufferAnimationVisibility(
			bool mobileOrPortablePlatform
		)
		{
			return !mobileOrPortablePlatform;
		}

		internal static int CalculateAnimationDecodeJobLimit(
			int availableProcessorCount
		)
		{
			return Mathf.Max(1, availableProcessorCount);
		}

		/// <summary>
		/// When false, inbound images from other players are dropped (the feature still lets you spawn your own).
		/// Persisted across sessions.
		/// </summary>
		public static bool ReceiveEnabled
		{
			get
			{
				if (!_loaded)
				{
					_receiveEnabled = PlayerPrefs.GetInt(ReceiveEnabledKey, 1) != 0;
					_loaded = true;
				}
				return _receiveEnabled;
			}
			set
			{
				_receiveEnabled = value;
				_loaded = true;
				PlayerPrefs.SetInt(ReceiveEnabledKey, value ? 1 : 0);
				PlayerPrefs.Save();
			}
		}
	}
}
