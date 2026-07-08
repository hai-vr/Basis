using System.Threading;
using NUnit.Framework;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Basis.ImagePickup.Tests
{
	public class BasisAnimatedImageTests
	{
		[Test]
		public void NativeDataAndPlaybackWork()
		{
			using BasisAnimatedImageData data = Create(
				new BasisAnimatedImageFrameSource(
					new RectInt(0, 0, 1, 1),
					1,
					BasisAnimationBlend.Source,
					BasisAnimationDisposal.None,
					new[] { Red }
				),
				new BasisAnimatedImageFrameSource(
					new RectInt(1, 0, 1, 1),
					100000,
					BasisAnimationBlend.Source,
					BasisAnimationDisposal.None,
					new[] { Green }
				)
			);

			Assert.That(data.FrameCount, Is.EqualTo(2));
			Assert.That(
				data.GetFrame(0).DurationMicroseconds,
				Is.EqualTo(
					BasisImagePickupSettings.MinAnimationFrameDurationMicroseconds
				)
			);
			Assert.That(data.DecodedFramePixels, Is.EqualTo(2));
			data.GetPlaybackState(
				data.TotalDurationMicroseconds + 1,
				out long play,
				out int frame,
				out bool completed
			);
			Assert.That(play, Is.EqualTo(1));
			Assert.That(frame, Is.EqualTo(0));
			Assert.That(completed, Is.False);
		}

		[Test]
		public void BurstCpuCanvasRestoresPrevious()
		{
			using BasisAnimatedImageData data = Create(
				new BasisAnimatedImageFrameSource(
					new RectInt(0, 0, 2, 1),
					50000,
					BasisAnimationBlend.Source,
					BasisAnimationDisposal.None,
					new[] { Red, Green }
				),
				new BasisAnimatedImageFrameSource(
					new RectInt(0, 0, 1, 1),
					50000,
					BasisAnimationBlend.Source,
					BasisAnimationDisposal.Previous,
					new[] { Blue }
				),
				new BasisAnimatedImageFrameSource(
					new RectInt(1, 0, 1, 1),
					50000,
					BasisAnimationBlend.Source,
					BasisAnimationDisposal.None,
					new[] { White }
				)
			);
			using var canvas = new BasisAnimatedImageCpuCanvas(data);

			canvas.UpdateToState(0, 2);
			Color32[] pixels = ((Texture2D)canvas.OutputTexture).GetPixels32();
			Assert.That(pixels[0], Is.EqualTo(Red));
			Assert.That(pixels[1], Is.EqualTo(White));
		}

		[Test]
		public void AnimatedPlayerCreatesResourcesLazilyAndSuspendsOffscreen()
		{
			var host = new GameObject("BasisAnimatedImagePlayerTest");
			var pickup = host.AddComponent<BasisImagePickupObject>();
			var player = host.AddComponent<BasisAnimatedImagePlayer>();
			BasisAnimatedImageData data = Create(
				new BasisAnimatedImageFrameSource(
					new RectInt(0, 0, 2, 1),
					50000,
					BasisAnimationBlend.Source,
					BasisAnimationDisposal.None,
					new[] { Red, Green }
				),
				new BasisAnimatedImageFrameSource(
					new RectInt(0, 0, 2, 1),
					50000,
					BasisAnimationBlend.Source,
					BasisAnimationDisposal.None,
					new[] { Blue, White }
				)
			);
			var commands = new CommandBuffer();
			bool initialized = false;
			try
			{
				initialized = player.Initialize(data, pickup, 1, true);
				Assert.That(initialized, Is.True);
				Assert.That(player.HasAllocatedCompositor, Is.False);

				int transitionsRemaining = 16;
				long pixelsRemaining = 1024;
				bool gpuCommandsAdded = false;
				player.Schedule(
					commands,
					1,
					ref transitionsRemaining,
					ref pixelsRemaining,
					true,
					ref gpuCommandsAdded
				);

				Assert.That(player.HasAllocatedCompositor, Is.True);
				float resumeTime =
					Time.unscaledTime
					+ BasisImagePickupSettings.AnimationOffscreenResourceReleaseSeconds
					+ 0.1f;
				player.UpdateVisibilityState(false, resumeTime);
				Assert.That(player.HasAllocatedCompositor, Is.False);

				player.UpdateVisibilityState(true, resumeTime);
				transitionsRemaining = 16;
				pixelsRemaining = 1024;
				player.Schedule(
					commands,
					500001,
					ref transitionsRemaining,
					ref pixelsRemaining,
					true,
					ref gpuCommandsAdded
				);
				Assert.That(player.HasAllocatedCompositor, Is.True);
				Assert.That(player.OutputTexture, Is.Not.Null);
				Color32[] resumedPixels = (
					(Texture2D)player.OutputTexture
				).GetPixels32();
				Assert.That(resumedPixels[0], Is.EqualTo(Blue));
				Assert.That(resumedPixels[1], Is.EqualTo(White));
			}
			finally
			{
				commands.Release();
				Object.DestroyImmediate(host);
				if (!initialized)
					data.Dispose();
				BasisAnimatedImageScheduler scheduler =
					BasisAnimatedImageScheduler.Instance;
				if (
					scheduler != null
					&& scheduler.gameObject.name == "BasisAnimatedImageScheduler"
				)
					Object.DestroyImmediate(scheduler.gameObject);
			}
		}

		[Test]
		public void ReloadableAnimatedPlayerReleasesAndRestoresDecodedFrames()
		{
			var host = new GameObject("BasisAnimatedImageReloadTest");
			var pickup = host.AddComponent<BasisImagePickupObject>();
			var player = host.AddComponent<BasisAnimatedImagePlayer>();
			BasisAnimatedImageData data = Create(
				new BasisAnimatedImageFrameSource(
					new RectInt(0, 0, 2, 1),
					50000,
					BasisAnimationBlend.Source,
					BasisAnimationDisposal.None,
					new[] { Red, Green }
				),
				new BasisAnimatedImageFrameSource(
					new RectInt(0, 0, 2, 1),
					50000,
					BasisAnimationBlend.Source,
					BasisAnimationDisposal.None,
					new[] { Blue, White }
				)
			);
			BasisNativeAnimationPayload payload = null;
			var commands = new CommandBuffer();
			bool initialized = false;
			try
			{
				using BasisBurstAnimationEncodeRequest encode =
					BasisBurstAnimationCodec.ScheduleEncode(data);
				BasisBurstAnimationEncodeResult encoded = encode.Complete();
				Assert.That(encoded.Ok, Is.True, encoded.Error);
				payload = encoded.Payload;
				encoded.Payload = null;
				Assert.That(payload.AllocatedBytes, Is.EqualTo(payload.Length));

				initialized = player.Initialize(data, pickup, 1, true, payload);
				Assert.That(initialized, Is.True);
				player.ReleaseDecodedDataForMemoryPressure();
				Assert.That(player.Data, Is.Null);
				Assert.That(payload.IsCreated, Is.True);

				for (int attempt = 0; attempt < 10000 && player.Data == null; attempt++)
				{
					int transitionsRemaining = 16;
					long pixelsRemaining = 1024;
					bool gpuCommandsAdded = false;
					player.Schedule(
						commands,
						500001,
						ref transitionsRemaining,
						ref pixelsRemaining,
						true,
						ref gpuCommandsAdded
					);
					JobHandle.ScheduleBatchedJobs();
					Thread.Yield();
				}

				Assert.That(player.Data, Is.Not.Null);
				Assert.That(player.HasAllocatedCompositor, Is.True);
				Assert.That(payload.IsCreated, Is.True);
			}
			finally
			{
				commands.Release();
				player.ClearReloadPayload();
				Object.DestroyImmediate(host);
				payload?.Dispose();
				if (!initialized)
					data.Dispose();
				BasisAnimatedImageScheduler scheduler =
					BasisAnimatedImageScheduler.Instance;
				if (
					scheduler != null
					&& scheduler.gameObject.name == "BasisAnimatedImageScheduler"
				)
					Object.DestroyImmediate(scheduler.gameObject);
			}
		}

		[Test]
		public void SceneViewAndPreviewCamerasDoNotDriveAnimationVisibility()
		{
			Assert.That(
				BasisAnimatedImageScheduler.IsSupportedVisibilityCameraType(
					CameraType.Game
				),
				Is.True
			);
			Assert.That(
				BasisAnimatedImageScheduler.IsSupportedVisibilityCameraType(
					CameraType.SceneView
				),
				Is.False
			);
			Assert.That(
				BasisAnimatedImageScheduler.IsSupportedVisibilityCameraType(
					CameraType.Preview
				),
				Is.False
			);
		}

		[Test]
		public void DepthVisibilityComputeShaderIsPackaged()
		{
			ComputeShader compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(
				"Packages/com.basis.imagepickup/Resources/BasisImageDepthVisibility.compute"
			);
			Assert.That(compute, Is.Not.Null);
		}

		[Test]
		public void PortablePlatformsUseFrontFaceAndDesktopUsesDepthBuffer()
		{
			Assert.That(
				BasisImagePickupSettings.ShouldUseDepthBufferAnimationVisibility(true),
				Is.False
			);
			Assert.That(
				BasisImagePickupSettings.ShouldUseDepthBufferAnimationVisibility(false),
				Is.True
			);
		}

		[Test]
		public void DepthVisibilityExpiresAndCanBeReset()
		{
			var host = new GameObject("DepthVisibilityResultTest");
			try
			{
				var player = host.AddComponent<BasisAnimatedImagePlayer>();
				player.SetDepthVisibility(false, 10f, false);
				Assert.That(player.DepthVisibilityFromGpu, Is.False);

				player.SetDepthVisibility(true, 10f);
				Assert.That(player.DepthVisibilityFromGpu, Is.True);
				Assert.That(
					player.TryGetDepthVisibility(10.1f, out bool visible),
					Is.True
				);
				Assert.That(visible, Is.True);
				Assert.That(
					player.TryGetDepthVisibility(
						10f
							+ BasisImagePickupSettings.AnimationDepthVisibilityResultMaxAgeSeconds
							+ 0.01f,
						out _
					),
					Is.False
				);

				player.ResetDepthVisibility();
				Assert.That(player.TryGetDepthVisibility(10f, out _), Is.False);
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
		}

		[Test]
		public void ResettingFaceVisibilityForcesImmediateReevaluation()
		{
			var host = new GameObject("VisibilityModeResetTest");
			try
			{
				var player = host.AddComponent<BasisAnimatedImagePlayer>();
				player.SetFaceVisibility(true, 10f);
				Assert.That(player.IsFaceVisible, Is.True);
				Assert.That(player.NeedsFaceOcclusionCheck(10f), Is.False);

				player.ResetFaceVisibility();
				Assert.That(player.IsFaceVisible, Is.False);
				Assert.That(player.NeedsFaceOcclusionCheck(0f), Is.True);
			}
			finally
			{
				Object.DestroyImmediate(host);
			}
		}

		[Test]
		public void CardFrontFacePointsTowardNegativeLocalZ()
		{
			Assert.That(
				BasisAnimatedImageScheduler.IsFrontFacingCamera(
					Vector3.back,
					Vector3.zero,
					new Vector3(0f, 0f, -2f),
					Vector3.forward,
					false
				),
				Is.True
			);
			Assert.That(
				BasisAnimatedImageScheduler.IsFrontFacingCamera(
					Vector3.back,
					Vector3.zero,
					new Vector3(0f, 0f, 2f),
					Vector3.back,
					false
				),
				Is.False
			);
		}

		[Test]
		public void FaceOcclusionIgnoresOwnAndUnrelatedTriggerColliders()
		{
			var targetObject = new GameObject("TargetImage");
			var target = targetObject.AddComponent<BasisImagePickupObject>();
			var ownCollider = targetObject.AddComponent<BoxCollider>();
			ownCollider.isTrigger = true;

			var triggerObject = new GameObject("UnrelatedTrigger");
			var unrelatedTrigger = triggerObject.AddComponent<BoxCollider>();
			unrelatedTrigger.isTrigger = true;

			var wallObject = new GameObject("Wall");
			var wallCollider = wallObject.AddComponent<BoxCollider>();

			var otherImageObject = new GameObject("OtherImage");
			var otherImage = otherImageObject.AddComponent<BasisImagePickupObject>();
			var otherImageCollider = otherImageObject.AddComponent<BoxCollider>();
			otherImageCollider.isTrigger = true;
			BasisImagePickupObject.RegisterCollider(otherImageCollider, otherImage);

			try
			{
				Assert.That(
					BasisAnimatedImageScheduler.IsBlockingOcclusionCollider(
						ownCollider,
						target
					),
					Is.False
				);
				Assert.That(
					BasisAnimatedImageScheduler.IsBlockingOcclusionCollider(
						unrelatedTrigger,
						target
					),
					Is.False
				);
				Assert.That(
					BasisAnimatedImageScheduler.IsBlockingOcclusionCollider(
						wallCollider,
						target
					),
					Is.True
				);
				Assert.That(
					BasisAnimatedImageScheduler.IsBlockingOcclusionCollider(
						otherImageCollider,
						target
					),
					Is.True
				);
			}
			finally
			{
				BasisImagePickupObject.UnregisterCollider(otherImageCollider);
				Object.DestroyImmediate(targetObject);
				Object.DestroyImmediate(triggerObject);
				Object.DestroyImmediate(wallObject);
				Object.DestroyImmediate(otherImageObject);
			}
		}

		[Test]
		public void LocalImageSlotsIncludeQueuedAndActiveGifImports()
		{
			Assert.That(
				BasisImagePickupManager.CalculateAvailableLocalImageSlots(60, 2, 1),
				Is.EqualTo(1)
			);
			Assert.That(
				BasisImagePickupManager.CalculateAvailableLocalImageSlots(64, 0, 0),
				Is.EqualTo(0)
			);
			Assert.That(
				BasisImagePickupManager.CalculateAvailableLocalImageSlots(63, 1, 1),
				Is.EqualTo(0)
			);
		}

		[Test]
		public void AcceptedInboundAnimationContinuesUntilItsPickupIsInvalid()
		{
			Assert.That(
				BasisImagePickupManager.ShouldContinueAcceptedInboundAnimation(
					true,
					true,
					true,
					false
				),
				Is.True
			);
			Assert.That(
				BasisImagePickupManager.ShouldContinueAcceptedInboundAnimation(
					false,
					true,
					true,
					false
				),
				Is.False
			);
			Assert.That(
				BasisImagePickupManager.ShouldContinueAcceptedInboundAnimation(
					true,
					false,
					true,
					false
				),
				Is.False
			);
			Assert.That(
				BasisImagePickupManager.ShouldContinueAcceptedInboundAnimation(
					true,
					true,
					false,
					false
				),
				Is.False
			);
			Assert.That(
				BasisImagePickupManager.ShouldContinueAcceptedInboundAnimation(
					true,
					true,
					true,
					true
				),
				Is.False
			);
		}

		[Test]
		public void InboundAnimationDecodeBudgetDefersTemporaryOverflow()
		{
			long decodedByteLimit =
				BasisImagePickupSettings.MaxPendingInboundAnimationDecodedBytesPerSender;
			int jobLimit =
				BasisImagePickupSettings.MaxPendingInboundAnimationDecodeJobsPerSender;

			Assert.That(
				BasisImagePickupManager.FitsInboundAnimationDecodeBudget(0, 0, 1),
				Is.True
			);
			Assert.That(
				BasisImagePickupManager.FitsInboundAnimationDecodeBudget(
					jobLimit,
					0,
					1
				),
				Is.False
			);
			Assert.That(
				BasisImagePickupManager.FitsInboundAnimationDecodeBudget(
					1,
					decodedByteLimit - 1024,
					1024
				),
				Is.True
			);
			Assert.That(
				BasisImagePickupManager.FitsInboundAnimationDecodeBudget(
					1,
					decodedByteLimit - 1023,
					1024
				),
				Is.False
			);
			Assert.That(
				BasisImagePickupManager.FitsInboundAnimationDecodeBudget(
					0,
					0,
					(int)decodedByteLimit + 1
				),
				Is.False
			);
		}

		[TestCase(-1, 1)]
		[TestCase(0, 1)]
		[TestCase(1, 1)]
		[TestCase(8, 8)]
		public void AnimationDecodeConcurrencyUsesAvailableLogicalProcessors(
			int availableProcessorCount,
			int expectedJobLimit
		)
		{
			Assert.That(
				BasisImagePickupSettings.CalculateAnimationDecodeJobLimit(
					availableProcessorCount
				),
				Is.EqualTo(expectedJobLimit)
			);
		}

		[Test]
		public void ImageManagerHandlesServerAndDirectNetworkRoutes()
		{
			System.Type managerType = typeof(BasisImagePickupManager);
			Assert.That(
				managerType
					.GetMethod(nameof(BasisImagePickupManager.OnNetworkMessage))
					?.DeclaringType,
				Is.EqualTo(managerType)
			);
			Assert.That(
				managerType
					.GetMethod(nameof(BasisImagePickupManager.OnDirectNetworkMessage))
					?.DeclaringType,
				Is.EqualTo(managerType)
			);
		}

		[Test]
		public void LargeBatchSpreadsHorizontallyAndStaysAboveMinimumHeight()
		{
			const int count = 64;
			const float batchCenterY = 1.6f;
			const float minimumCenterY = 0.3f;
			int columns = BasisImagePickupManager.CalculateBatchSpawnColumns(
				count,
				batchCenterY,
				minimumCenterY
			);

			Assert.That(
				columns,
				Is.GreaterThan(BasisImagePickupSettings.BatchSpawnColumns)
			);
			Assert.That(
				columns,
				Is.LessThanOrEqualTo(BasisImagePickupSettings.BatchSpawnMaximumColumns)
			);
			for (int index = 0; index < count; index++)
			{
				Vector3 offset = BasisImagePickupManager.CalculateBatchSpawnLocalOffset(
					index,
					count,
					columns,
					minimumCenterY - batchCenterY
				);
				Assert.That(
					batchCenterY + offset.y,
					Is.GreaterThanOrEqualTo(minimumCenterY - 0.0001f)
				);
			}
		}

		[Test]
		public void LowBatchCenterUsesMaximumWidthAndShiftsAboveGround()
		{
			const int count = 64;
			const float batchCenterY = 0.7f;
			const float minimumCenterY = 0.3f;
			int columns = BasisImagePickupManager.CalculateBatchSpawnColumns(
				count,
				batchCenterY,
				minimumCenterY
			);

			Assert.That(
				columns,
				Is.EqualTo(BasisImagePickupSettings.BatchSpawnMaximumColumns)
			);
			for (int index = 0; index < count; index++)
			{
				Vector3 offset = BasisImagePickupManager.CalculateBatchSpawnLocalOffset(
					index,
					count,
					columns,
					minimumCenterY - batchCenterY
				);
				Assert.That(
					batchCenterY + offset.y,
					Is.GreaterThanOrEqualTo(minimumCenterY - 0.0001f)
				);
			}
		}

		[Test]
		public void BatchOffsetsPlaceTwoImagesSideBySide()
		{
			Vector3 left = BasisImagePickupManager.CalculateBatchSpawnLocalOffset(0, 2);
			Vector3 right = BasisImagePickupManager.CalculateBatchSpawnLocalOffset(
				1,
				2
			);

			Assert.That(left.x, Is.EqualTo(-right.x).Within(0.0001f));
			Assert.That(left.y, Is.EqualTo(right.y).Within(0.0001f));
			Assert.That(left.x, Is.LessThan(0f));
			Assert.That(right.x, Is.GreaterThan(0f));
		}

		[Test]
		public void BatchOffsetsUseStableRowsForFiveImages()
		{
			Vector3 first = BasisImagePickupManager.CalculateBatchSpawnLocalOffset(
				0,
				5
			);
			Vector3 fourth = BasisImagePickupManager.CalculateBatchSpawnLocalOffset(
				3,
				5
			);
			Vector3 fifth = BasisImagePickupManager.CalculateBatchSpawnLocalOffset(
				4,
				5
			);

			Assert.That(first.y, Is.GreaterThan(fifth.y));
			Assert.That(first.x, Is.LessThan(fourth.x));
			Assert.That(fifth.x, Is.EqualTo(0f).Within(0.0001f));
		}

		[Test]
		public void NativeAdoptionRejectsInconsistentPreviousCanvasFlag()
		{
			var frames = new NativeArray<BasisAnimatedImageFrame>(
				1,
				Allocator.Temp,
				NativeArrayOptions.ClearMemory
			);
			var pixels = new NativeArray<Color32>(
				1,
				Allocator.Temp,
				NativeArrayOptions.ClearMemory
			);
			var frameEnds = new NativeArray<long>(
				1,
				Allocator.Temp,
				NativeArrayOptions.ClearMemory
			);
			BasisAnimatedImageData data = null;
			try
			{
				frames[0] = new BasisAnimatedImageFrame
				{
					Width = 1,
					Height = 1,
					PixelCount = 1,
					DurationMicroseconds = 50000,
					EndTimeMicroseconds = 50000,
					Blend = BasisAnimationBlend.Source,
					Disposal = BasisAnimationDisposal.Previous,
				};
				frameEnds[0] = 50000;

				Assert.That(
					BasisAnimatedImageData.TryAdoptNative(
						1,
						1,
						0,
						new Color32(0, 0, 0, 0),
						frames,
						pixels,
						frameEnds,
						50000,
						true,
						false,
						false,
						out data,
						out string error
					),
					Is.False
				);
				StringAssert.Contains("previous-canvas flag", error);
				Assert.That(data, Is.Null);
			}
			finally
			{
				data?.Dispose();
				if (frames.IsCreated)
					frames.Dispose();
				if (pixels.IsCreated)
					pixels.Dispose();
				if (frameEnds.IsCreated)
					frameEnds.Dispose();
			}
		}

		[Test]
		public void NativeAdoptionRejectsFrameOutsideCanvas()
		{
			using var frames = new NativeArray<BasisAnimatedImageFrame>(
				1,
				Allocator.Temp,
				NativeArrayOptions.ClearMemory
			);
			using var pixels = new NativeArray<Color32>(
				1,
				Allocator.Temp,
				NativeArrayOptions.ClearMemory
			);
			using var frameEnds = new NativeArray<long>(
				1,
				Allocator.Temp,
				NativeArrayOptions.ClearMemory
			);
			WriteFrame(
				frames,
				0,
				new BasisAnimatedImageFrame
				{
					X = 1,
					Width = 1,
					Height = 1,
					PixelCount = 1,
					DurationMicroseconds = 50000,
					EndTimeMicroseconds = 50000,
					Blend = BasisAnimationBlend.Source,
					Disposal = BasisAnimationDisposal.None,
				}
			);
			WriteFrameEnd(frameEnds, 0, 50000);

			Assert.That(
				BasisAnimatedImageData.TryAdoptNative(
					1,
					1,
					0,
					new Color32(0, 0, 0, 0),
					frames,
					pixels,
					frameEnds,
					50000,
					true,
					false,
					false,
					out BasisAnimatedImageData data,
					out string error
				),
				Is.False
			);
			Assert.That(data, Is.Null);
			StringAssert.Contains("bounds", error);
		}

		[Test]
		public void RemoteTransformSmoothingIsFrameRateIndependentAndBounded()
		{
			float oneSixtieth =
				BasisImagePickupObject.CalculateRemoteTransformLerpFactor(1f / 60f);
			float oneOneTwentieth =
				BasisImagePickupObject.CalculateRemoteTransformLerpFactor(1f / 120f);
			float twoOneTwentiethSteps =
				1f - (1f - oneOneTwentieth) * (1f - oneOneTwentieth);

			Assert.That(
				twoOneTwentiethSteps,
				Is.EqualTo(oneSixtieth).Within(0.000001f)
			);
			Assert.That(
				BasisImagePickupObject.CalculateRemoteTransformLerpFactor(10f),
				Is.InRange(0f, 1f)
			);
			Assert.That(
				BasisImagePickupObject.CalculateRemoteTransformLerpFactor(-1f),
				Is.EqualTo(0f)
			);
		}

		[TestCase(0, 1, 1000, 64)]
		[TestCase(64, 65, 1000, 128)]
		[TestCase(128, 129, 200, 200)]
		[TestCase(64, 65, 64, 64)]
		public void DepthVisibilityCapacityGrowsGeometrically(
			int currentCapacity,
			int minimumRequired,
			int maximumCapacity,
			int expected
		)
		{
			Assert.That(
				BasisAnimatedImageDepthVisibility.CalculateGrowthCapacity(
					currentCapacity,
					minimumRequired,
					maximumCapacity
				),
				Is.EqualTo(expected)
			);
		}

		[TestCase(79, 0)]
		[TestCase(80, 1)]
		[TestCase(1024, 12)]
		public void DepthVisibilityCapacityHonorsMaximumGraphicsBufferSize(
			long maximumBufferBytes,
			int expected
		)
		{
			Assert.That(
				BasisAnimatedImageDepthVisibility.CalculateMaximumCardCapacity(
					maximumBufferBytes
				),
				Is.EqualTo(expected)
			);
		}

		[Test]
		public void SchedulerStartIndexTracksPlayerRemoval()
		{
			Assert.That(
				BasisAnimatedImageScheduler.AdjustStartIndexAfterRemoval(3, 1, 4),
				Is.EqualTo(2)
			);
			Assert.That(
				BasisAnimatedImageScheduler.AdjustStartIndexAfterRemoval(3, 3, 4),
				Is.EqualTo(3)
			);
			Assert.That(
				BasisAnimatedImageScheduler.AdjustStartIndexAfterRemoval(4, 4, 4),
				Is.EqualTo(0)
			);
		}

		[Test]
		public void AtlasFramesWithoutPaddingReceiveDedicatedPages()
		{
			using var frames = new NativeArray<BasisAnimatedImageFrame>(
				2,
				Allocator.Temp,
				NativeArrayOptions.ClearMemory
			);
			using var locations = new NativeArray<BasisAnimationFrameAtlasLocation>(
				2,
				Allocator.Temp,
				NativeArrayOptions.ClearMemory
			);
			using var cursorX = new NativeArray<int>(2, Allocator.Temp);
			using var cursorY = new NativeArray<int>(2, Allocator.Temp);
			using var rowHeight = new NativeArray<int>(2, Allocator.Temp);
			using var usedWidth = new NativeArray<int>(2, Allocator.Temp);
			using var usedHeight = new NativeArray<int>(2, Allocator.Temp);
			using var result = new NativeArray<int>(3, Allocator.Temp);
			WriteFrame(
				frames,
				0,
				new BasisAnimatedImageFrame { Width = 4, Height = 1 }
			);
			WriteFrame(
				frames,
				1,
				new BasisAnimatedImageFrame { Width = 1, Height = 1 }
			);

			new BasisAnimationAtlasLayoutJob
			{
				PageLimit = 4,
				Frames = frames,
				Locations = locations,
				CursorX = cursorX,
				CursorY = cursorY,
				RowHeight = rowHeight,
				UsedWidth = usedWidth,
				UsedHeight = usedHeight,
				Result = result,
			}.Execute();

			Assert.That(result[2], Is.EqualTo(0));
			Assert.That(locations[0].Padding, Is.EqualTo(0));
			Assert.That(locations[0].PageIndex, Is.Not.EqualTo(locations[1].PageIndex));
		}

		[Test]
		public void AnimationNativeMemoryBudgetHonorsExactBoundary()
		{
			Assert.That(
				BasisAnimatedImageData.FitsMemoryBudget(100, 50, 25, 175),
				Is.True
			);
			Assert.That(
				BasisAnimatedImageData.FitsMemoryBudget(100, 50, 26, 175),
				Is.False
			);
			Assert.That(
				BasisAnimatedImageData.FitsMemoryBudget(-1, 0, 0, 175),
				Is.False
			);
			Assert.That(
				BasisAnimatedImageData.FitsMemoryBudget(0, 0, 176, 175),
				Is.False
			);
		}

		[Test]
		public void NativeDataTracksAndReleasesResidentBytes()
		{
			long before = BasisAnimatedImageData.TotalResidentNativeBytes;
			BasisAnimatedImageData data = Create(
				new BasisAnimatedImageFrameSource(
					new RectInt(0, 0, 1, 1),
					50000,
					BasisAnimationBlend.Source,
					BasisAnimationDisposal.None,
					new[] { Red }
				)
			);
			long nativeBytes = data.NativeByteCount;
			try
			{
				Assert.That(nativeBytes, Is.GreaterThan(0));
				Assert.That(
					BasisAnimatedImageData.TotalResidentNativeBytes,
					Is.EqualTo(before + nativeBytes)
				);
			}
			finally
			{
				data.Dispose();
			}
			Assert.That(
				BasisAnimatedImageData.TotalResidentNativeBytes,
				Is.EqualTo(before)
			);
		}

		[Test]
		public void NativeDataCopiesAuthoringPixels()
		{
			var pixels = new[] { Red };
			using BasisAnimatedImageData data = Create(
				new BasisAnimatedImageFrameSource(
					new RectInt(0, 0, 1, 1),
					50000,
					BasisAnimationBlend.Source,
					BasisAnimationDisposal.None,
					pixels
				)
			);
			pixels[0] = Blue;
			Assert.That(data.CopyFramePixelsToManaged(0)[0], Is.EqualTo(Red));
		}

		private static void WriteFrame(
			NativeArray<BasisAnimatedImageFrame> destination,
			int index,
			BasisAnimatedImageFrame value
		)
		{
			destination[index] = value;
		}

		private static void WriteFrameEnd(
			NativeArray<long> destination,
			int index,
			long value
		)
		{
			destination[index] = value;
		}

		private static BasisAnimatedImageData Create(
			params BasisAnimatedImageFrameSource[] frames
		)
		{
			Assert.That(
				BasisAnimatedImageData.TryCreate(
					2,
					1,
					0,
					new Color32(0, 0, 0, 0),
					frames,
					out BasisAnimatedImageData data,
					out string error
				),
				Is.True,
				error
			);
			return data;
		}

		private static readonly Color32 Red = new Color32(255, 0, 0, 255);
		private static readonly Color32 Green = new Color32(0, 255, 0, 255);
		private static readonly Color32 Blue = new Color32(0, 0, 255, 255);
		private static readonly Color32 White = new Color32(255, 255, 255, 255);
	}
}
