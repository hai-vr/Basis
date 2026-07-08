using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Basis.ImagePickup
{
	/// <summary>
	/// Maintains a persistent GPU canvas and appends GIF/APNG-style frame composition
	/// operations to a caller-owned command buffer.
	/// </summary>
	internal sealed class BasisAnimatedImageGpuCanvas : IDisposable
	{
		private const int SourcePass = 0;
		private const int OverPass = 1;
		private const int ClearPass = 2;
		private const int CopyPremultipliedPass = 3;

		private static readonly int SourceTextureId = Shader.PropertyToID(
			"_BasisImageAnimSourceTex"
		);
		private static readonly int SourceUvRectId = Shader.PropertyToID(
			"_BasisImageAnimSourceUvRect"
		);
		private static readonly int ClearColorId = Shader.PropertyToID(
			"_BasisImageAnimClearColor"
		);

		private readonly BasisAnimatedImageData _data;
		private readonly Material _compositorMaterial;
		private BasisAnimationFrameAtlas _frameAtlas;
		private readonly bool _canCopyRenderTextureRegions;

		private RenderTexture _canvas;
		private RenderTexture _previousCanvas;
		private long _currentPlayIndex = -1;
		private int _currentFrameIndex = -1;
		private bool _stateValid;
		private bool _disposed;

		public Texture OutputTexture => _canvas;
		public bool IsStateValid => _stateValid;

		public BasisAnimatedImageGpuCanvas(
			BasisAnimatedImageData data,
			Material compositorMaterial
		)
		{
			_data = data ?? throw new ArgumentNullException(nameof(data));
			_compositorMaterial =
				compositorMaterial != null
					? compositorMaterial
					: throw new ArgumentNullException(nameof(compositorMaterial));
			_canCopyRenderTextureRegions =
				(SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;

			BasisAnimationFrameAtlas frameAtlas = null;
			try
			{
				frameAtlas = new BasisAnimationFrameAtlas(data);
				_frameAtlas = frameAtlas;
				if (!EnsureCreated())
					throw new InvalidOperationException(
						"Animated image GPU canvas could not be created."
					);
			}
			catch
			{
				frameAtlas?.Dispose();
				ReleaseRenderTexture(ref _previousCanvas);
				ReleaseRenderTexture(ref _canvas);
				throw;
			}
		}

		public bool EnsureCreated()
		{
			ThrowIfDisposed();

			bool recreated = false;
			bool recoverFrameAtlas = false;
			if (_canvas == null)
			{
				_canvas = CreateCanvas("Basis Animated Image Canvas");
				recreated = true;
			}
			else if (!_canvas.IsCreated())
			{
				recreated = _canvas.Create();
				recoverFrameAtlas = recreated;
			}

			if (_data.RequiresPreviousCanvas)
			{
				if (_previousCanvas == null)
				{
					_previousCanvas = CreateCanvas(
						"Basis Animated Image Previous Canvas"
					);
					recreated = true;
				}
				else if (!_previousCanvas.IsCreated())
				{
					bool previousCanvasRecreated = _previousCanvas.Create();
					recreated |= previousCanvasRecreated;
					recoverFrameAtlas |= previousCanvasRecreated;
				}
			}

			if (recoverFrameAtlas && !TryRebuildFrameAtlas())
			{
				Invalidate();
				return false;
			}

			bool canvasCreated = _canvas != null && _canvas.IsCreated();
			bool previousCanvasCreated =
				!_data.RequiresPreviousCanvas
				|| (_previousCanvas != null && _previousCanvas.IsCreated());
			bool allRequiredCanvasesCreated = canvasCreated && previousCanvasCreated;

			if (recreated || !allRequiredCanvasesCreated)
				Invalidate();
			return allRequiredCanvasesCreated;
		}

		private bool TryRebuildFrameAtlas()
		{
			BasisAnimationFrameAtlas replacement = null;
			try
			{
				replacement = new BasisAnimationFrameAtlas(_data);
				_frameAtlas?.Dispose();
				_frameAtlas = replacement;
				return true;
			}
			catch
			{
				replacement?.Dispose();
				return false;
			}
		}

		public void Invalidate()
		{
			_stateValid = false;
			_currentPlayIndex = -1;
			_currentFrameIndex = -1;
		}

		public void EstimateWork(
			long targetPlayIndex,
			int targetFrameIndex,
			out int transitions,
			out long pixels
		)
		{
			ValidateTarget(targetPlayIndex, targetFrameIndex);

			if (
				!_stateValid
				|| targetPlayIndex != _currentPlayIndex
				|| targetFrameIndex < _currentFrameIndex
			)
			{
				transitions = targetFrameIndex + 1;
				pixels = (long)_data.CanvasWidth * _data.CanvasHeight;
				int previous = -1;
				for (int i = 0; i <= targetFrameIndex; i++)
				{
					if (previous >= 0)
						pixels += DisposalPixelCost(_data.GetFrame(previous));
					BasisAnimatedImageFrame frame = _data.GetFrame(i);
					pixels += frame.PixelCount;
					if (frame.Disposal == BasisAnimationDisposal.Previous)
						pixels += frame.PixelCount;
					previous = i;
				}
				return;
			}

			transitions = Math.Max(0, targetFrameIndex - _currentFrameIndex);
			pixels = 0;
			for (int i = _currentFrameIndex + 1; i <= targetFrameIndex; i++)
			{
				pixels += DisposalPixelCost(_data.GetFrame(i - 1));
				BasisAnimatedImageFrame frame = _data.GetFrame(i);
				pixels += frame.PixelCount;
				if (frame.Disposal == BasisAnimationDisposal.Previous)
					pixels += frame.PixelCount;
			}
		}

		public int AppendToState(
			CommandBuffer commands,
			long targetPlayIndex,
			int targetFrameIndex
		)
		{
			if (commands == null)
				throw new ArgumentNullException(nameof(commands));
			ThrowIfDisposed();
			ValidateTarget(targetPlayIndex, targetFrameIndex);

			if (!EnsureCreated())
				return 0;

			int transitions = 0;
			if (
				!_stateValid
				|| targetPlayIndex != _currentPlayIndex
				|| targetFrameIndex < _currentFrameIndex
			)
			{
				AppendReset(commands);
				_currentPlayIndex = targetPlayIndex;
				_currentFrameIndex = -1;
				_stateValid = true;
			}

			while (_currentFrameIndex < targetFrameIndex)
			{
				int nextFrameIndex = _currentFrameIndex + 1;
				if (_currentFrameIndex >= 0)
					AppendDisposal(commands, _data.GetFrame(_currentFrameIndex));

				BasisAnimatedImageFrame nextFrame = _data.GetFrame(nextFrameIndex);
				if (nextFrame.Disposal == BasisAnimationDisposal.Previous)
					AppendSavePrevious(commands, nextFrame.Destination);

				AppendFrame(commands, nextFrameIndex, nextFrame);
				_currentFrameIndex = nextFrameIndex;
				transitions++;
			}

			return transitions;
		}

		private void AppendReset(CommandBuffer commands)
		{
			commands.SetRenderTarget(
				_canvas,
				RenderBufferLoadAction.DontCare,
				RenderBufferStoreAction.Store
			);
			commands.ClearRenderTarget(
				false,
				true,
				BasisImagePickupRuntimeUtility.ToWorkingPremultipliedColor(
					_data.BackgroundColor
				)
			);
		}

		private void AppendDisposal(
			CommandBuffer commands,
			BasisAnimatedImageFrame frame
		)
		{
			switch (frame.Disposal)
			{
				case BasisAnimationDisposal.None:
					return;
				case BasisAnimationDisposal.Background:
					AppendClearRect(commands, frame.Destination, _data.BackgroundColor);
					return;
				case BasisAnimationDisposal.Previous:
					AppendRestorePrevious(commands, frame.Destination);
					return;
				default:
					throw new ArgumentOutOfRangeException(
						nameof(frame.Disposal),
						frame.Disposal,
						null
					);
			}
		}

		private void AppendFrame(
			CommandBuffer commands,
			int frameIndex,
			BasisAnimatedImageFrame frame
		)
		{
			int pass =
				frame.Blend == BasisAnimationBlend.Source ? SourcePass : OverPass;
			BasisAnimationFrameAtlasLocation location = _frameAtlas.GetLocation(
				frameIndex
			);
			Texture2D source = _frameAtlas.Pages[location.PageIndex];
			AppendTexturedRect(
				commands,
				source,
				location.SourceRectangle,
				_canvas,
				frame.Destination,
				pass
			);
		}

		private void AppendSavePrevious(CommandBuffer commands, RectInt rectangle)
		{
			if (_previousCanvas == null)
				return;

			if (_canCopyRenderTextureRegions)
			{
				commands.CopyTexture(
					_canvas,
					0,
					0,
					rectangle.x,
					rectangle.y,
					rectangle.width,
					rectangle.height,
					_previousCanvas,
					0,
					0,
					rectangle.x,
					rectangle.y
				);
				return;
			}

			AppendTexturedRect(
				commands,
				_canvas,
				rectangle,
				_previousCanvas,
				rectangle,
				CopyPremultipliedPass
			);
		}

		private void AppendRestorePrevious(CommandBuffer commands, RectInt rectangle)
		{
			if (_previousCanvas == null)
				return;

			if (_canCopyRenderTextureRegions)
			{
				commands.CopyTexture(
					_previousCanvas,
					0,
					0,
					rectangle.x,
					rectangle.y,
					rectangle.width,
					rectangle.height,
					_canvas,
					0,
					0,
					rectangle.x,
					rectangle.y
				);
				return;
			}

			AppendTexturedRect(
				commands,
				_previousCanvas,
				rectangle,
				_canvas,
				rectangle,
				CopyPremultipliedPass
			);
		}

		private void AppendClearRect(
			CommandBuffer commands,
			RectInt destination,
			Color32 color
		)
		{
			Rect viewport = ToRect(destination);
			commands.SetRenderTarget(
				_canvas,
				RenderBufferLoadAction.Load,
				RenderBufferStoreAction.Store
			);
			commands.SetViewport(viewport);
			commands.EnableScissorRect(viewport);
			commands.SetGlobalColor(
				ClearColorId,
				BasisImagePickupRuntimeUtility.ToWorkingStraightColor(color)
			);
			commands.DrawProcedural(
				Matrix4x4.identity,
				_compositorMaterial,
				ClearPass,
				MeshTopology.Triangles,
				3,
				1
			);
			commands.DisableScissorRect();
		}

		private void AppendTexturedRect(
			CommandBuffer commands,
			Texture source,
			RectInt sourceRectangle,
			RenderTexture destination,
			RectInt destinationRectangle,
			int pass
		)
		{
			Rect viewport = ToRect(destinationRectangle);
			Vector4 uvRect = new Vector4(
				sourceRectangle.x / (float)source.width,
				sourceRectangle.y / (float)source.height,
				sourceRectangle.width / (float)source.width,
				sourceRectangle.height / (float)source.height
			);

			commands.SetRenderTarget(
				destination,
				RenderBufferLoadAction.Load,
				RenderBufferStoreAction.Store
			);
			commands.SetViewport(viewport);
			commands.EnableScissorRect(viewport);
			commands.SetGlobalTexture(SourceTextureId, source);
			commands.SetGlobalVector(SourceUvRectId, uvRect);
			commands.DrawProcedural(
				Matrix4x4.identity,
				_compositorMaterial,
				pass,
				MeshTopology.Triangles,
				3,
				1
			);
			commands.DisableScissorRect();
		}

		private RenderTexture CreateCanvas(string name)
		{
			var descriptor = new RenderTextureDescriptor(
				_data.CanvasWidth,
				_data.CanvasHeight,
				RenderTextureFormat.ARGB32,
				0
			)
			{
				msaaSamples = 1,
				volumeDepth = 1,
				useMipMap = false,
				autoGenerateMips = false,
				enableRandomWrite = false,
				useDynamicScale = false,
				sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear,
				dimension = TextureDimension.Tex2D,
			};

			var texture = new RenderTexture(descriptor)
			{
				name = name,
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
				anisoLevel = 0,
				hideFlags = HideFlags.HideAndDontSave,
			};
			try
			{
				if (!texture.Create())
					throw new InvalidOperationException($"Could not create {name}.");
				return texture;
			}
			catch
			{
				ReleaseRenderTexture(ref texture);
				throw;
			}
		}

		private static long FramePixelCost(BasisAnimatedImageFrame frame)
		{
			return frame.PixelCount;
		}

		private static long DisposalPixelCost(BasisAnimatedImageFrame frame)
		{
			return frame.Disposal == BasisAnimationDisposal.None
				? 0
				: FramePixelCost(frame);
		}

		private static Rect ToRect(RectInt rectangle)
		{
			return new Rect(
				rectangle.x,
				rectangle.y,
				rectangle.width,
				rectangle.height
			);
		}

		private void ValidateTarget(long targetPlayIndex, int targetFrameIndex)
		{
			if (targetPlayIndex < 0)
				throw new ArgumentOutOfRangeException(nameof(targetPlayIndex));
			if (targetFrameIndex < 0 || targetFrameIndex >= _data.FrameCount)
				throw new ArgumentOutOfRangeException(nameof(targetFrameIndex));
		}

		private void ThrowIfDisposed()
		{
			if (_disposed)
				throw new ObjectDisposedException(nameof(BasisAnimatedImageGpuCanvas));
		}

		public void Dispose()
		{
			if (_disposed)
				return;
			_disposed = true;

			_frameAtlas?.Dispose();
			_frameAtlas = null;

			ReleaseRenderTexture(ref _previousCanvas);
			ReleaseRenderTexture(ref _canvas);
		}

		private static void ReleaseRenderTexture(ref RenderTexture texture)
		{
			if (texture == null)
				return;
			if (texture.IsCreated())
				texture.Release();
			BasisImagePickupRuntimeUtility.DestroyObject(texture);
			texture = null;
		}
	}
}
