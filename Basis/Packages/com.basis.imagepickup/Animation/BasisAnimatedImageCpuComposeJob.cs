using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.ImagePickup
{
	[BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Standard)]
	internal struct BasisAnimatedImageCpuComposeJob : IJob
	{
		public int CanvasWidth;
		public byte Reset;
		public int StartFrame;
		public int TargetFrame;
		public byte Linear;
		public Color32 Background;

		[ReadOnly]
		public NativeArray<BasisAnimatedImageFrame> Frames;

		[ReadOnly]
		public NativeArray<Color32> FramePixels;
		public NativeArray<Color32> Canvas;
		public NativeArray<Color32> Previous;

		public void Execute()
		{
			if (Reset != 0)
			{
				Color32 color = Premultiply(Background);
				for (int i = 0; i < Canvas.Length; i++)
					Canvas[i] = color;
			}

			for (
				int frameIndex = StartFrame + 1;
				frameIndex <= TargetFrame;
				frameIndex++
			)
			{
				if (frameIndex > 0)
					DisposeFrame(Frames[frameIndex - 1]);
				BasisAnimatedImageFrame frame = Frames[frameIndex];
				if (frame.Disposal == BasisAnimationDisposal.Previous)
					CopyRect(Canvas, Previous, frame);
				Draw(frame);
			}
		}

		private void DisposeFrame(BasisAnimatedImageFrame frame)
		{
			if (frame.Disposal == BasisAnimationDisposal.None)
				return;
			if (frame.Disposal == BasisAnimationDisposal.Previous)
			{
				CopyRect(Previous, Canvas, frame);
				return;
			}

			Color32 background = Premultiply(Background);
			for (int y = 0; y < frame.Height; y++)
			{
				int index = (frame.Y + y) * CanvasWidth + frame.X;
				int end = index + frame.Width;
				while (index < end)
					Canvas[index++] = background;
			}
		}

		private void Draw(BasisAnimatedImageFrame frame)
		{
			int source = frame.PixelOffset;
			for (int y = 0; y < frame.Height; y++)
			{
				int destination = (frame.Y + y) * CanvasWidth + frame.X;
				for (int x = 0; x < frame.Width; x++, source++, destination++)
				{
					Color32 pixel = FramePixels[source];
					Canvas[destination] =
						frame.Blend == BasisAnimationBlend.Source
							? Premultiply(pixel)
							: Blend(pixel, Canvas[destination]);
				}
			}
		}

		private void CopyRect(
			NativeArray<Color32> source,
			NativeArray<Color32> destination,
			BasisAnimatedImageFrame frame
		)
		{
			for (int y = 0; y < frame.Height; y++)
			{
				int index = (frame.Y + y) * CanvasWidth + frame.X;
				int end = index + frame.Width;
				while (index < end)
				{
					destination[index] = source[index];
					index++;
				}
			}
		}

		private Color32 Premultiply(Color32 color)
		{
			float4 value = Straight(color);
			value.xyz *= value.w;
			return Encode(value);
		}

		private Color32 Blend(Color32 sourceColor, Color32 destinationColor)
		{
			float4 source = Straight(sourceColor);
			float4 destination = Stored(destinationColor);
			float inverse = 1f - source.w;
			return Encode(
				new float4(
					source.xyz * source.w + destination.xyz * inverse,
					source.w + destination.w * inverse
				)
			);
		}

		private float4 Straight(Color32 color)
		{
			float4 value = new float4(color.r, color.g, color.b, color.a) / 255f;
			if (Linear != 0)
				value.xyz = ToLinear(value.xyz);
			return value;
		}

		private float4 Stored(Color32 color)
		{
			float4 value = new float4(color.r, color.g, color.b, color.a) / 255f;
			if (Linear != 0)
				value.xyz = ToLinear(value.xyz);
			return value;
		}

		private Color32 Encode(float4 value)
		{
			value = math.saturate(value);
			if (Linear != 0)
				value.xyz = ToSrgb(value.xyz);
			value = math.round(math.saturate(value) * 255f);
			return new Color32(
				(byte)value.x,
				(byte)value.y,
				(byte)value.z,
				(byte)value.w
			);
		}

		private static float3 ToLinear(float3 value)
		{
			return math.select(
				value / 12.92f,
				math.pow((value + 0.055f) / 1.055f, 2.4f),
				value > 0.04045f
			);
		}

		private static float3 ToSrgb(float3 value)
		{
			return math.select(
				value * 12.92f,
				1.055f * math.pow(value, 1f / 2.4f) - 0.055f,
				value > 0.0031308f
			);
		}
	}
}
