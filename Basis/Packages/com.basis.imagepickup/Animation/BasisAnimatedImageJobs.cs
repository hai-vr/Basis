using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Basis.Scripts.Common;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Basis.ImagePickup
{
	internal sealed class BasisGifDecodeJobResult : IDisposable
	{
		public bool Ok;
		public string Error;
		public BasisAnimatedImageData Animation;
		public Color32[] PosterPixels;
		public byte[] CleanPng;
		public bool HasAlpha;
		public BasisNativeAnimationPayload AnimationPayload;
		public string AnimationNetworkError;
		public long WorkerElapsedTicks;

		public void Dispose()
		{
			Animation?.Dispose();
			Animation = null;
			AnimationPayload?.Dispose();
			AnimationPayload = null;
			PosterPixels = null;
			CleanPng = null;
		}
	}

	internal sealed class BasisAnimationDecodeJobResult
	{
		public bool Ok;
		public string Error;
		public BasisAnimatedImageData Animation;
		public long WorkerElapsedTicks;
	}

	internal sealed class BasisAnimationPacketBatch : IDisposable
	{
		private NativeArray<byte> _header;
		private NativeArray<byte> _packets;
		private NativeArray<int> _packetOffsets;
		private NativeArray<int> _packetLengths;
		private bool _disposed;

		public bool Ok;
		public string Error;
		public int StartChunkIndex;
		public int TotalChunks;
		public int PacketCount => _packetLengths.IsCreated ? _packetLengths.Length : 0;
		public bool HasHeader => _header.IsCreated && _header.Length > 0;
		public int HeaderLength => HasHeader ? _header.Length : 0;

		// Time from scheduling until the main thread observed completion. This is not pure Burst CPU time.
		public long ReadyElapsedTicks;

		internal BasisAnimationPacketBatch(
			NativeArray<byte> header,
			NativeArray<byte> packets,
			NativeArray<int> packetOffsets,
			NativeArray<int> packetLengths,
			int startChunkIndex,
			int totalChunks,
			long readyElapsedTicks
		)
		{
			_header = header;
			_packets = packets;
			_packetOffsets = packetOffsets;
			_packetLengths = packetLengths;
			StartChunkIndex = startChunkIndex;
			TotalChunks = totalChunks;
			ReadyElapsedTicks = readyElapsedTicks;
			Ok = true;
		}

		public int GetPacketLength(int batchIndex)
		{
			if (_disposed)
				throw new ObjectDisposedException(nameof(BasisAnimationPacketBatch));
			if ((uint)batchIndex >= (uint)_packetLengths.Length)
				throw new ArgumentOutOfRangeException(nameof(batchIndex));
			return _packetLengths[batchIndex];
		}

		public void CopyHeaderTo(byte[] destination)
		{
			if (!HasHeader)
				throw new InvalidOperationException("Packet batch has no header.");
			if (destination == null || destination.Length != _header.Length)
				throw new ArgumentException(
					"Header destination has the wrong length.",
					nameof(destination)
				);
			for (int i = 0; i < destination.Length; i++)
				destination[i] = _header[i];
		}

		public void CopyPacketTo(int batchIndex, byte[] destination)
		{
			int length = GetPacketLength(batchIndex);
			if (destination == null || destination.Length != length)
				throw new ArgumentException(
					"Packet destination has the wrong length.",
					nameof(destination)
				);
			int sourceOffset = _packetOffsets[batchIndex];
			for (int i = 0; i < length; i++)
				destination[i] = _packets[sourceOffset + i];
		}

		public void Dispose()
		{
			if (_disposed)
				return;
			_disposed = true;
			if (_header.IsCreated)
				_header.Dispose();
			if (_packets.IsCreated)
				_packets.Dispose();
			if (_packetOffsets.IsCreated)
				_packetOffsets.Dispose();
			if (_packetLengths.IsCreated)
				_packetLengths.Dispose();
		}
	}

	internal sealed class BasisGifDecodeJobRequest : IDisposable
	{
		private enum State
		{
			Reading,
			Decoding,
			Encoding,
			Complete,
		}

		private readonly string _path;
		private Task<byte[]> _readTask;
		private BasisBurstGifDecodeRequest _decodeRequest;
		private BasisBurstGifDecodeResult _decodeResult;
		private BasisBurstAnimationEncodeRequest _encodeRequest;
		private Task<byte[]> _posterEncodeTask;
		private Color32[] _posterPixels;
		private BasisNativeAnimationPayload _animationPayload;
		private string _animationNetworkError;
		private bool _animationEncodingComplete;
		private BasisGifDecodeJobResult _result;
		private State _state;
		private bool _disposed;
		private readonly long _startTimestamp;

		internal BasisGifDecodeJobRequest(string path)
		{
			_path = path;
			_startTimestamp = Stopwatch.GetTimestamp();
			StartManagedRead();
		}

		public bool IsCompleted =>
			_state switch
			{
				State.Complete => true,
				State.Reading => _readTask?.IsCompleted == true,
				State.Decoding => _decodeRequest?.IsCompleted == true,
				State.Encoding => (
					_animationEncodingComplete || _encodeRequest?.IsCompleted == true
				)
					&& _posterEncodeTask?.IsCompleted == true,
				_ => false,
			};

		public bool TryComplete(out BasisGifDecodeJobResult result)
		{
			Advance(false);
			result = _state == State.Complete ? _result : null;
			return result != null;
		}

		public BasisGifDecodeJobResult Complete()
		{
			while (_state != State.Complete)
				Advance(true);
			return _result;
		}

		private void Advance(bool block)
		{
			if (_state == State.Complete)
				return;

			if (_state == State.Reading)
			{
				if (!AdvanceManagedRead(block))
					return;
			}

			if (_state == State.Decoding)
			{
				if (block)
					_decodeResult = _decodeRequest.Complete();
				else if (!_decodeRequest.TryComplete(out _decodeResult))
					return;

				if (_decodeResult == null || !_decodeResult.Ok)
				{
					FinishFailure(
						_decodeResult?.Error ?? "GIF Burst decoder returned no result."
					);
					return;
				}

				try
				{
					StartPosterEncoding();
				}
				catch (Exception exception)
				{
					FinishFailure(
						"GIF poster PNG encoding could not start: "
							+ exception.GetBaseException().Message
					);
					return;
				}

				if (_decodeResult.Animation.FrameCount <= 1)
				{
					_animationEncodingComplete = true;
				}
				else
				{
					try
					{
						_encodeRequest = BasisBurstAnimationCodec.ScheduleEncode(
							_decodeResult.Animation
						);
					}
					catch (BasisAnimationMemoryBudgetException exception)
					{
						_animationNetworkError = exception.Message;
						_animationEncodingComplete = true;
					}
					catch (Exception exception)
					{
						_animationNetworkError =
							"Animation Burst encoding could not start: "
							+ exception.Message;
						_animationEncodingComplete = true;
					}
				}
				_state = State.Encoding;
			}

			if (_state == State.Encoding)
			{
				if (!_animationEncodingComplete)
				{
					BasisBurstAnimationEncodeResult encoded;
					if (block)
						encoded = _encodeRequest.Complete();
					else if (!_encodeRequest.TryComplete(out encoded))
						return;

					if (encoded == null || !encoded.Ok)
					{
						_animationNetworkError =
							encoded?.Error
							?? "Animation Burst encoder returned no result.";
					}
					else
					{
						_animationPayload = encoded.Payload;
						encoded.Payload = null;
					}
					_animationEncodingComplete = true;
					_encodeRequest.Dispose();
					_encodeRequest = null;
				}

				if (!block && !_posterEncodeTask.IsCompleted)
					return;

				byte[] cleanPng;
				try
				{
					cleanPng = _posterEncodeTask.GetAwaiter().GetResult();
				}
				catch (Exception exception)
				{
					FinishFailure(
						"GIF poster PNG encoding failed: "
							+ exception.GetBaseException().Message
					);
					return;
				}
				finally
				{
					_posterEncodeTask = null;
				}

				FinishSuccess(cleanPng);
			}
		}

		private bool AdvanceManagedRead(bool block)
		{
			if (_readTask == null)
			{
				FinishFailure("GIF managed file read was not scheduled.");
				return false;
			}
			if (!block && !_readTask.IsCompleted)
				return false;

			byte[] bytes;
			try
			{
				bytes = _readTask.GetAwaiter().GetResult();
			}
			catch (Exception exception)
			{
				FinishFailure(
					"GIF file read failed (managed background reader): "
						+ exception.GetBaseException().Message
				);
				return false;
			}
			finally
			{
				_readTask = null;
			}

			try
			{
				_decodeRequest = BasisBurstGifDecoder.Schedule(bytes);
				_state = State.Decoding;
				return true;
			}
			catch (Exception exception)
			{
				FinishFailure("GIF Burst decode could not start: " + exception.Message);
				return false;
			}
		}

		private void StartManagedRead()
		{
			// IMPORTANT: Do not replace this with AsyncReadManager without first validating
			// burst-dragging more than two GIF files in a built Windows IL2CPP player.
			// Unity 6000.5 routes AsyncReadManager through DirectStorage in that configuration,
			// and concurrent transient GIF reads have repeatedly crashed inside DStorageCore.
			// The native crash occurs before managed code can observe a failed ReadHandle, so
			// adding a managed fallback after AsyncReadManager does not make the path safe.
			// File.ReadAllBytes is intentionally run on a worker task for every platform here.
			try
			{
				_readTask = Task.Run(() => ReadGifBytes(_path));
				_state = State.Reading;
			}
			catch (Exception exception)
			{
				FinishFailure(
					"GIF managed file read could not be scheduled: "
						+ exception.GetBaseException().Message
				);
			}
		}

		private static byte[] ReadGifBytes(string path)
		{
			FileInfo info = new FileInfo(path);
			if (!info.Exists)
				throw new FileNotFoundException("File not found.", path);
			if (info.Length <= 0)
				throw new InvalidDataException("GIF file is empty.");
			if (info.Length > BasisImagePickupSettings.MaxAnimationSourceBytes)
				throw new InvalidDataException(
					"GIF file exceeds the configured source-byte limit."
				);

			byte[] bytes = File.ReadAllBytes(path);
			if (bytes.Length <= 0)
				throw new InvalidDataException("GIF file is empty.");
			if (bytes.Length > BasisImagePickupSettings.MaxAnimationSourceBytes)
				throw new InvalidDataException(
					"GIF file grew beyond the configured source-byte limit while reading."
				);
			return bytes;
		}

		private void StartPosterEncoding()
		{
			if (
				_decodeResult.Animation == null
				|| !_decodeResult.PosterPixels.IsCreated
				|| _decodeResult.PosterPixels.Length == 0
			)
			{
				throw new InvalidDataException("GIF poster pixels are unavailable.");
			}

			int width = _decodeResult.Animation.CanvasWidth;
			int height = _decodeResult.Animation.CanvasHeight;
			Color32[] posterPixels = _decodeResult.PosterPixels.ToArray();
			_decodeResult.PosterPixels.Dispose();
			_decodeResult.PosterPixels = default;
			_posterPixels = posterPixels;
			_posterEncodeTask = Task.Run(() =>
				EncodePosterPng(posterPixels, width, height)
			);
		}

		private static byte[] EncodePosterPng(Color32[] pixels, int width, int height)
		{
			if (
				pixels == null
				|| width <= 0
				|| height <= 0
				|| (long)width * height != pixels.Length
			)
			{
				throw new InvalidDataException(
					"GIF poster pixel dimensions are invalid."
				);
			}

			var rgba = new byte[checked(pixels.Length * 4)];
			for (int i = 0; i < pixels.Length; i++)
			{
				Color32 pixel = pixels[i];
				int offset = i * 4;
				rgba[offset] = pixel.r;
				rgba[offset + 1] = pixel.g;
				rgba[offset + 2] = pixel.b;
				rgba[offset + 3] = pixel.a;
			}

			byte[] cleanPng = ImageConversion.EncodeArrayToPNG(
				rgba,
				GraphicsFormat.R8G8B8A8_UNorm,
				(uint)width,
				(uint)height,
				0
			);
			if (cleanPng == null || cleanPng.Length == 0)
				throw new InvalidDataException("GIF poster PNG encoding failed.");
			if (cleanPng.Length > BasisImagePickupSettings.MaxImageBytes)
				throw new InvalidDataException(
					"GIF poster PNG exceeds the configured image-byte limit."
				);
			return cleanPng;
		}

		private void FinishSuccess(byte[] cleanPng)
		{
			BasisAnimatedImageData animation = _decodeResult.Animation;
			_decodeResult.Animation = null;
			_result = new BasisGifDecodeJobResult
			{
				Ok = true,
				Animation = animation,
				PosterPixels = _posterPixels,
				CleanPng = cleanPng,
				HasAlpha = animation.HasAnyAlpha,
				AnimationPayload = _animationPayload,
				AnimationNetworkError = _animationNetworkError,
				WorkerElapsedTicks = Stopwatch.GetTimestamp() - _startTimestamp,
			};
			_posterPixels = null;
			_animationPayload = null;
			DisposeRequests();
			_state = State.Complete;
		}

		private void FinishFailure(string error)
		{
			_result = new BasisGifDecodeJobResult
			{
				Ok = false,
				Error = error,
				WorkerElapsedTicks = Stopwatch.GetTimestamp() - _startTimestamp,
			};
			DisposeRequests();
			_state = State.Complete;
		}

		private void DisposeRequests()
		{
			_readTask = null;
			_posterEncodeTask = null;
			_posterPixels = null;
			_animationPayload?.Dispose();
			_animationPayload = null;

			_encodeRequest?.Dispose();
			_encodeRequest = null;
			_decodeResult?.Dispose();
			_decodeResult = null;
			_decodeRequest?.Dispose();
			_decodeRequest = null;
		}

		public void Dispose()
		{
			if (_disposed)
				return;
			_disposed = true;
			_result?.Dispose();
			_result = null;
			DisposeRequests();
		}
	}

	internal sealed class BasisAnimationDecodeJobRequest : IDisposable
	{
		private BasisBurstAnimationDecodeRequest _request;
		private BasisAnimationDecodeJobResult _result;

		internal BasisAnimationDecodeJobRequest(byte[] payload)
		{
			_request = BasisBurstAnimationCodec.ScheduleDecode(payload);
		}

		internal BasisAnimationDecodeJobRequest(
			NativeArray<byte> payload,
			int length,
			bool takeOwnership
		)
		{
			_request = BasisBurstAnimationCodec.ScheduleDecode(
				payload,
				length,
				takeOwnership
			);
		}

		public bool IsCompleted => _result != null || _request.IsCompleted;

		public bool TryComplete(out BasisAnimationDecodeJobResult result)
		{
			result = null;
			if (_result == null)
			{
				if (!_request.TryComplete(out BasisBurstAnimationDecodeResult decoded))
					return false;
				_result = Convert(decoded);
			}
			result = _result;
			return true;
		}

		public BasisAnimationDecodeJobResult Complete()
		{
			if (_result != null)
				return _result;
			_result = Convert(_request.Complete());
			return _result;
		}

		private static BasisAnimationDecodeJobResult Convert(
			BasisBurstAnimationDecodeResult decoded
		)
		{
			if (decoded == null)
			{
				return new BasisAnimationDecodeJobResult
				{
					Ok = false,
					Error = "Animation Burst decoder returned no result.",
				};
			}
			BasisAnimatedImageData animation = decoded.Animation;
			decoded.Animation = null;
			return new BasisAnimationDecodeJobResult
			{
				Ok = decoded.Ok,
				Error = decoded.Error,
				Animation = animation,
				WorkerElapsedTicks = decoded.WorkerElapsedTicks,
			};
		}

		public void Dispose()
		{
			_result?.Animation?.Dispose();
			if (_result != null)
				_result.Animation = null;
			_request?.Dispose();
			_request = null;
		}
	}

	internal sealed class BasisAnimationPacketJobRequest : IDisposable
	{
		private readonly BasisNativeAnimationPayload _payload;
		private readonly int _startChunkIndex;
		private readonly int _totalChunks;
		private readonly int _chunksInBatch;
		private NativeArray<byte> _header;
		private NativeArray<byte> _packets;
		private NativeArray<int> _packetOffsets;
		private NativeArray<int> _packetLengths;
		private JobHandle _handle;
		private BasisAnimationPacketBatch _result;
		private bool _disposed;
		private readonly long _scheduledTimestamp;

		internal BasisAnimationPacketJobRequest(
			Guid id,
			BasisNativeAnimationPayload payload,
			long playbackEpochUtcTicks,
			byte format,
			byte spawnOpcode,
			byte chunkOpcode,
			int chunkPayloadBytes,
			int startChunkIndex,
			int maximumChunks
		)
		{
			if (payload == null || !payload.IsCreated || payload.Length <= 0)
				throw new ArgumentException(
					"Animation payload is empty.",
					nameof(payload)
				);
			if (playbackEpochUtcTicks <= 0)
				throw new ArgumentOutOfRangeException(nameof(playbackEpochUtcTicks));
			if (chunkPayloadBytes <= 0 || maximumChunks <= 0)
				throw new ArgumentOutOfRangeException(nameof(chunkPayloadBytes));

			_scheduledTimestamp = Stopwatch.GetTimestamp();
			BasisGuid128 networkId = BasisGuid128.FromGuid(id);
			_payload = payload;
			_startChunkIndex = startChunkIndex;
			_totalChunks = (payload.Length + chunkPayloadBytes - 1) / chunkPayloadBytes;
			if (startChunkIndex < 0 || startChunkIndex >= _totalChunks)
				throw new ArgumentOutOfRangeException(nameof(startChunkIndex));
			_chunksInBatch = math.min(maximumChunks, _totalChunks - startChunkIndex);

			JobHandle cleanupHandle = default;
			try
			{
				_header =
					startChunkIndex == 0
						? new NativeArray<byte>(
							BasisAnimatedImageNetworkCodec.AnimationHeaderSize,
							Allocator.Persistent,
							NativeArrayOptions.UninitializedMemory
						)
						: default;
				_packets = new NativeArray<byte>(
					checked(
						_chunksInBatch
						* (
							BasisAnimatedImageNetworkCodec.AnimationChunkHeaderSize
							+ chunkPayloadBytes
						)
					),
					Allocator.Persistent,
					NativeArrayOptions.UninitializedMemory
				);
				_packetOffsets = new NativeArray<int>(
					_chunksInBatch,
					Allocator.Persistent,
					NativeArrayOptions.UninitializedMemory
				);
				_packetLengths = new NativeArray<int>(
					_chunksInBatch,
					Allocator.Persistent,
					NativeArrayOptions.UninitializedMemory
				);

				JobHandle headerHandle = default;
				if (_header.IsCreated)
				{
					headerHandle = new BasisBuildAnimationHeaderJob
					{
						Id = networkId,
						Format = format,
						SpawnOpcode = spawnOpcode,
						TotalBytes = payload.Length,
						TotalChunks = _totalChunks,
						PlaybackEpochUtcTicks = playbackEpochUtcTicks,
						Header = _header,
					}.Schedule();
					cleanupHandle = headerHandle;
				}
				var buildPacketsJob = new BasisBuildAnimationPacketsJob
				{
					Id = networkId,
					ChunkOpcode = chunkOpcode,
					ChunkPayloadBytes = chunkPayloadBytes,
					StartChunkIndex = startChunkIndex,
					PayloadLength = payload.Length,
					Payload = payload.Bytes,
					Packets = _packets,
					PacketOffsets = _packetOffsets,
					PacketLengths = _packetLengths,
				};
				JobHandle packetHandle = buildPacketsJob.ScheduleParallelByRef(
					_chunksInBatch,
					1,
					default
				);
				cleanupHandle = _header.IsCreated
					? JobHandle.CombineDependencies(headerHandle, packetHandle)
					: packetHandle;
				_handle = cleanupHandle;
				JobHandle.ScheduleBatchedJobs();
			}
			catch
			{
				cleanupHandle.Complete();
				if (_header.IsCreated)
					_header.Dispose();
				if (_packets.IsCreated)
					_packets.Dispose();
				if (_packetOffsets.IsCreated)
					_packetOffsets.Dispose();
				if (_packetLengths.IsCreated)
					_packetLengths.Dispose();
				throw;
			}
		}

		public bool IsCompleted => _result != null || _handle.IsCompleted;

		public bool TryComplete(out BasisAnimationPacketBatch result)
		{
			result = null;
			if (_result == null && !_handle.IsCompleted)
				return false;
			result = Complete();
			return true;
		}

		public BasisAnimationPacketBatch Complete()
		{
			if (_result != null)
				return _result;
			_handle.Complete();
			_result = new BasisAnimationPacketBatch(
				_header,
				_packets,
				_packetOffsets,
				_packetLengths,
				_startChunkIndex,
				_totalChunks,
				Stopwatch.GetTimestamp() - _scheduledTimestamp
			);
			_header = default;
			_packets = default;
			_packetOffsets = default;
			_packetLengths = default;
			return _result;
		}

		public void Dispose()
		{
			if (_disposed)
				return;
			_disposed = true;
			_handle.Complete();
			if (_header.IsCreated)
				_header.Dispose();
			if (_packets.IsCreated)
				_packets.Dispose();
			if (_packetOffsets.IsCreated)
				_packetOffsets.Dispose();
			if (_packetLengths.IsCreated)
				_packetLengths.Dispose();
		}
	}

	internal static class BasisAnimatedImageJobs
	{
		public static bool IsGifPath(string path)
		{
			return string.Equals(
				Path.GetExtension(path),
				".gif",
				StringComparison.OrdinalIgnoreCase
			);
		}

		public static BasisGifDecodeJobRequest ScheduleGifDecode(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
				throw new ArgumentException("GIF path is empty.", nameof(path));
			return new BasisGifDecodeJobRequest(path);
		}

		public static BasisAnimationDecodeJobRequest ScheduleNetworkDecode(
			byte[] payload
		)
		{
			return new BasisAnimationDecodeJobRequest(payload);
		}

		public static BasisAnimationDecodeJobRequest ScheduleNetworkDecode(
			NativeArray<byte> payload,
			int length,
			bool takeOwnership
		)
		{
			return new BasisAnimationDecodeJobRequest(payload, length, takeOwnership);
		}

		public static BasisAnimationPacketJobRequest SchedulePacketBuild(
			Guid id,
			BasisNativeAnimationPayload payload,
			long playbackEpochUtcTicks,
			byte format,
			byte spawnOpcode,
			byte chunkOpcode,
			int chunkPayloadBytes,
			int startChunkIndex,
			int maximumChunks
		)
		{
			return new BasisAnimationPacketJobRequest(
				id,
				payload,
				playbackEpochUtcTicks,
				format,
				spawnOpcode,
				chunkOpcode,
				chunkPayloadBytes,
				startChunkIndex,
				maximumChunks
			);
		}

		public static BasisImageValidationResult FinalizeGifDecode(
			BasisGifDecodeJobResult workerResult
		)
		{
			var result = new BasisImageValidationResult();
			if (workerResult == null || !workerResult.Ok)
			{
				result.Error =
					workerResult?.Error
					?? "Animated image pipeline returned no result.";
				return result;
			}
			if (workerResult.Animation == null || !workerResult.Animation.IsCreated)
			{
				result.Error = "Animated image pipeline returned no native animation.";
				return result;
			}
			if (
				workerResult.PosterPixels == null
				|| workerResult.PosterPixels.Length
					!= workerResult.Animation.CanvasWidth
						* workerResult.Animation.CanvasHeight
				|| workerResult.CleanPng == null
				|| workerResult.CleanPng.Length == 0
			)
			{
				result.Error = "Animated image pipeline returned invalid poster data.";
				return result;
			}

			Texture2D poster = null;
			try
			{
				poster = new Texture2D(
					workerResult.Animation.CanvasWidth,
					workerResult.Animation.CanvasHeight,
					TextureFormat.RGBA32,
					false,
					false
				)
				{
					name = "Basis GIF Poster",
					wrapMode = TextureWrapMode.Clamp,
					filterMode = FilterMode.Bilinear,
					anisoLevel = 0,
				};
				poster.SetPixels32(workerResult.PosterPixels);
				poster.Apply(false, true);
				result.Ok = true;
				result.Texture = poster;
				result.CleanPng = workerResult.CleanPng;
				result.Width = workerResult.Animation.CanvasWidth;
				result.Height = workerResult.Animation.CanvasHeight;
				result.HasAlpha = workerResult.HasAlpha;
				result.Animation = workerResult.Animation;
				result.AnimationPayload = workerResult.AnimationPayload;
				result.AnimationNetworkError = workerResult.AnimationNetworkError;
				workerResult.Animation = null;
				workerResult.AnimationPayload = null;
				workerResult.CleanPng = null;
				return result;
			}
			catch (Exception exception)
			{
				if (poster != null)
					BasisImagePickupRuntimeUtility.DestroyObject(poster);
				result.Error = "GIF poster creation failed: " + exception.Message;
				return result;
			}
			finally
			{
				workerResult.PosterPixels = null;
				workerResult.CleanPng = null;
			}
		}
	}

	[BurstCompile]
	internal struct BasisBuildAnimationHeaderJob : IJob
	{
		private const int IdOffset = 1;
		private const int FormatOffset = IdOffset + BasisGuid128.SerializedSize;
		private const int TotalBytesOffset = FormatOffset + 1;
		private const int TotalChunksOffset = TotalBytesOffset + sizeof(int);
		private const int PlaybackEpochOffset = TotalChunksOffset + sizeof(int);

		public BasisGuid128 Id;
		public byte Format;
		public byte SpawnOpcode;
		public int TotalBytes;
		public int TotalChunks;
		public long PlaybackEpochUtcTicks;
		public NativeArray<byte> Header;

		public void Execute()
		{
			Header[0] = SpawnOpcode;
			BasisAnimatedImageNetworkCodec.WriteGuid(Header, IdOffset, Id);
			Header[FormatOffset] = Format;
			WriteInt32(Header, TotalBytesOffset, TotalBytes);
			WriteInt32(Header, TotalChunksOffset, TotalChunks);
			WriteInt64(Header, PlaybackEpochOffset, PlaybackEpochUtcTicks);
		}

		private static void WriteInt32(NativeArray<byte> output, int offset, int value)
		{
			output[offset] = (byte)value;
			output[offset + 1] = (byte)(value >> 8);
			output[offset + 2] = (byte)(value >> 16);
			output[offset + 3] = (byte)(value >> 24);
		}

		private static void WriteInt64(NativeArray<byte> output, int offset, long value)
		{
			ulong unsigned = (ulong)value;
			for (int i = 0; i < 8; i++)
				output[offset + i] = (byte)(unsigned >> (i * 8));
		}
	}

	[BurstCompile]
	internal struct BasisBuildAnimationPacketsJob : IJobFor
	{
		private const int IdOffset = 1;
		private const int ChunkIndexOffset = IdOffset + BasisGuid128.SerializedSize;
		private const int PayloadLengthOffset = ChunkIndexOffset + sizeof(int);
		private const int PacketHeaderSize =
			BasisAnimatedImageNetworkCodec.AnimationChunkHeaderSize;

		public BasisGuid128 Id;
		public byte ChunkOpcode;
		public int ChunkPayloadBytes;
		public int StartChunkIndex;
		public int PayloadLength;

		[ReadOnly]
		public NativeArray<byte> Payload;

		[NativeDisableParallelForRestriction]
		public NativeArray<byte> Packets;

		[NativeDisableParallelForRestriction]
		public NativeArray<int> PacketOffsets;

		[NativeDisableParallelForRestriction]
		public NativeArray<int> PacketLengths;

		public void Execute(int batchIndex)
		{
			int chunkIndex = StartChunkIndex + batchIndex;
			int sourceOffset = chunkIndex * ChunkPayloadBytes;
			int payloadLength = math.min(
				ChunkPayloadBytes,
				PayloadLength - sourceOffset
			);
			int packetOffset = batchIndex * (PacketHeaderSize + ChunkPayloadBytes);
			PacketOffsets[batchIndex] = packetOffset;
			PacketLengths[batchIndex] = PacketHeaderSize + payloadLength;
			Packets[packetOffset] = ChunkOpcode;
			BasisAnimatedImageNetworkCodec.WriteGuid(
				Packets,
				packetOffset + IdOffset,
				Id
			);
			WriteInt32(Packets, packetOffset + ChunkIndexOffset, chunkIndex);
			WriteInt32(Packets, packetOffset + PayloadLengthOffset, payloadLength);
			for (int i = 0; i < payloadLength; i++)
				Packets[packetOffset + PacketHeaderSize + i] = Payload[
					sourceOffset + i
				];
		}

		private static void WriteInt32(NativeArray<byte> output, int offset, int value)
		{
			output[offset] = (byte)value;
			output[offset + 1] = (byte)(value >> 8);
			output[offset + 2] = (byte)(value >> 16);
			output[offset + 3] = (byte)(value >> 24);
		}
	}
}
