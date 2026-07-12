using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.ImagePickup
{
    internal struct BasisAnimationFrameAtlasLocation
    {
        public int PageIndex;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int Padding;
        public RectInt SourceRectangle => new RectInt(X, Y, Width, Height);
    }

    internal sealed class BasisAnimationFrameAtlas : IDisposable
    {
        private const int PreferredMaximumPageSize = 2048;

        private Texture2D[] _pages;
        private NativeArray<BasisAnimationFrameAtlasLocation> _locations;
        private bool _disposed;

        public Texture2D GetPage(int index)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BasisAnimationFrameAtlas));
            if (_pages == null || (uint)index >= (uint)_pages.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _pages[index];
        }

        public BasisAnimationFrameAtlasLocation GetLocation(int index) =>
            _locations[index];

        public BasisAnimationFrameAtlas(BasisAnimatedImageData data)
        {
			if (data == null || !data.IsCreated)
                throw new ArgumentNullException(nameof(data));
            int frameCount = data.FrameCount;
            int pageLimit = math.min(PreferredMaximumPageSize, math.max(1, SystemInfo.maxTextureSize));

            NativeArray<int> cursorX = default;
            NativeArray<int> cursorY = default;
            NativeArray<int> rowHeight = default;
            NativeArray<int> usedWidth = default;
            NativeArray<int> usedHeight = default;
            NativeArray<int> result = default;

            try
            {
                _locations = new NativeArray<BasisAnimationFrameAtlasLocation>(
                    frameCount,
                    Allocator.Persistent,
                    NativeArrayOptions.UninitializedMemory
                );
                cursorX = new NativeArray<int>(frameCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                cursorY = new NativeArray<int>(frameCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                rowHeight = new NativeArray<int>(frameCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                usedWidth = new NativeArray<int>(frameCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                usedHeight = new NativeArray<int>(frameCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                result = new NativeArray<int>(3, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                new BasisAnimationAtlasLayoutJob
                {
                    PageLimit = pageLimit,
                    Frames = data.FramesNative,
                    Locations = _locations,
                    CursorX = cursorX,
                    CursorY = cursorY,
                    RowHeight = rowHeight,
                    UsedWidth = usedWidth,
                    UsedHeight = usedHeight,
                    Result = result,
                }
                    .Schedule()
                    .Complete();

                if (result[2] != 0)
                    throw new NotSupportedException("Animation frame does not fit in the device atlas limit.");
                int pageCount = result[1];
                _pages = new Texture2D[pageCount];
                for (int page = 0; page < pageCount; page++)
                {
                    int width = math.max(1, usedWidth[page]);
                    int height = math.max(1, usedHeight[page]);
                    var pagePixels = new NativeArray<Color32>(
                        checked(width * height),
                        Allocator.TempJob,
                        NativeArrayOptions.ClearMemory
                    );
                    try
                    {
                        var populateAtlasJob = new BasisAnimationAtlasPopulateJob
                        {
                            PageIndex = page,
                            PageWidth = width,
                            Frames = data.FramesNative,
                            SourcePixels = data.PixelsNative,
                            Locations = _locations,
                            PagePixels = pagePixels,
                        };
                        populateAtlasJob
                            .ScheduleParallelByRef(frameCount, 1, default)
                            .Complete();

                        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
                        {
                            name = $"Basis Animated Image Burst Atlas {page}",
                            wrapMode = TextureWrapMode.Clamp,
                            filterMode = FilterMode.Bilinear,
                            anisoLevel = 0,
                            hideFlags = HideFlags.HideAndDontSave,
                        };
                        _pages[page] = texture;
                        texture.SetPixelData(pagePixels, 0);
                        texture.Apply(false, true);
                    }
                    finally
                    {
                        pagePixels.Dispose();
                    }
                }
            }
            catch
            {
                Dispose();
                throw;
            }
            finally
            {
                if (cursorX.IsCreated)
                    cursorX.Dispose();
                if (cursorY.IsCreated)
                    cursorY.Dispose();
                if (rowHeight.IsCreated)
                    rowHeight.Dispose();
                if (usedWidth.IsCreated)
                    usedWidth.Dispose();
                if (usedHeight.IsCreated)
                    usedHeight.Dispose();
                if (result.IsCreated)
                    result.Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            if (_locations.IsCreated)
                _locations.Dispose();
            if (_pages == null)
                return;
            int pageCount = _pages.Length;
            for (int i = 0; i < pageCount; i++)
            {
                if (_pages[i] == null)
                    continue;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    UnityEngine.Object.DestroyImmediate(_pages[i]);
                else
#endif
                    UnityEngine.Object.Destroy(_pages[i]);
            }
            _pages = null;
        }
    }

    [BurstCompile]
    internal struct BasisAnimationAtlasLayoutJob : IJob
    {
        public int PageLimit;

        [ReadOnly]
        public NativeArray<BasisAnimatedImageFrame> Frames;

        [WriteOnly]
        public NativeArray<BasisAnimationFrameAtlasLocation> Locations;
        public NativeArray<int> CursorX;
        public NativeArray<int> CursorY;
        public NativeArray<int> RowHeight;
        public NativeArray<int> UsedWidth;
        public NativeArray<int> UsedHeight;
        public NativeArray<int> Result;

        public void Execute()
        {
            long totalArea = 0;
            int largest = 1;
            int frameCount = Frames.Length;
            for (int i = 0; i < frameCount; i++)
            {
                BasisAnimatedImageFrame frame = Frames[i];
                int padding =
                    frame.Width + 2 <= PageLimit && frame.Height + 2 <= PageLimit
                        ? 1
                        : 0;
                int width = frame.Width + padding * 2;
                int height = frame.Height + padding * 2;
                if (width > PageLimit || height > PageLimit)
                {
                    Result[2] = 1;
                    return;
                }
                totalArea += (long)width * height;
                largest = math.max(largest, math.max(width, height));
            }

            int areaExtent = (int)math.ceil(math.sqrt((double)totalArea));
            int pageSize = math.clamp(math.ceilpow2(math.max(largest, areaExtent)), largest, PageLimit);
            int pageCount = 0;
            for (int frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                BasisAnimatedImageFrame frame = Frames[frameIndex];
                int padding =
                    frame.Width + 2 <= PageLimit && frame.Height + 2 <= PageLimit
                        ? 1
                        : 0;
                int outerWidth = frame.Width + padding * 2;
                int outerHeight = frame.Height + padding * 2;
                bool requiresDedicatedPage = padding == 0;
                int selected = -1;
                int placeX = 0;
                int placeY = 0;

                if (!requiresDedicatedPage)
                {
                    for (int page = 0; page < pageCount; page++)
                    {
                        int x = CursorX[page];
                        int y = CursorY[page];
                        int row = RowHeight[page];
                        if (x + outerWidth > pageSize)
                        {
                            x = 0;
                            y += row;
                            row = 0;
                        }
                        if (y + outerHeight > pageSize)
                            continue;
                        selected = page;
                        placeX = x;
                        placeY = y;
                        CursorX[page] = x + outerWidth;
                        CursorY[page] = y;
                        RowHeight[page] = math.max(row, outerHeight);
                        break;
                    }
                }

                if (selected < 0)
                {
                    selected = pageCount++;
                    placeX = 0;
                    placeY = 0;
                    CursorX[selected] = requiresDedicatedPage ? pageSize : outerWidth;
                    CursorY[selected] = requiresDedicatedPage ? pageSize : 0;
                    RowHeight[selected] = requiresDedicatedPage
                        ? pageSize
                        : outerHeight;
                }

                int sourceX = placeX + padding;
                int sourceY = placeY + padding;
                UsedWidth[selected] = math.max(UsedWidth[selected], placeX + outerWidth);
                UsedHeight[selected] = math.max(UsedHeight[selected], placeY + outerHeight);
                Locations[frameIndex] = new BasisAnimationFrameAtlasLocation
                {
                    PageIndex = selected,
                    X = sourceX,
                    Y = sourceY,
                    Width = frame.Width,
                    Height = frame.Height,
                    Padding = padding,
                };
            }

            Result[1] = pageCount;
        }
    }

    [BurstCompile]
    internal struct BasisAnimationAtlasPopulateJob : IJobFor
    {
        public int PageIndex;
        public int PageWidth;

        [ReadOnly]
        public NativeArray<BasisAnimatedImageFrame> Frames;

        [ReadOnly]
        public NativeArray<Color32> SourcePixels;

        [ReadOnly]
        public NativeArray<BasisAnimationFrameAtlasLocation> Locations;

        [NativeDisableParallelForRestriction]
        public NativeArray<Color32> PagePixels;

        public void Execute(int frameIndex)
        {
            BasisAnimationFrameAtlasLocation location = Locations[frameIndex];
            if (location.PageIndex != PageIndex)
                return;
            BasisAnimatedImageFrame frame = Frames[frameIndex];
            for (int y = 0; y < frame.Height; y++)
            {
                int source = frame.PixelOffset + y * frame.Width;
                int destination = (location.Y + y) * PageWidth + location.X;
                for (int x = 0; x < frame.Width; x++)
                    PagePixels[destination + x] = SourcePixels[source + x];
            }

            if (location.Padding == 0)
                return;
            int left = location.X - 1;
            int right = location.X + location.Width;
            for (int y = 0; y < location.Height; y++)
            {
                int row = (location.Y + y) * PageWidth;
                PagePixels[row + left] = PagePixels[row + location.X];
                PagePixels[row + right] = PagePixels[
                    row + location.X + location.Width - 1
                ];
            }
            int bottom = (location.Y - 1) * PageWidth;
            int first = location.Y * PageWidth;
            int top = (location.Y + location.Height) * PageWidth;
            int last = (location.Y + location.Height - 1) * PageWidth;
            for (int x = left; x <= right; x++)
            {
                PagePixels[bottom + x] = PagePixels[first + x];
                PagePixels[top + x] = PagePixels[last + x];
            }
        }
    }
}
