using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Basis.ImagePickup
{
    internal static class ZZGifBench
    {
        private const string CompositorShaderName = "Hidden/Basis/ImageAnimationComposite";

        [BurstCompile]
        private struct CompareJob : IJob
        {
            [ReadOnly]
            public NativeArray<Color32> A;

            [ReadOnly]
            public NativeArray<Color32> B;

            public NativeArray<long> Result;

            public void Execute()
            {
                long mismatches = 0;
                long first = -1;
                int maxDelta = 0;
                int length = math.min(A.Length, B.Length);
                for (int i = 0; i < length; i++)
                {
                    Color32 a = A[i];
                    Color32 b = B[i];
                    int delta = math.max(
                        math.max(math.abs(a.r - b.r), math.abs(a.g - b.g)),
                        math.max(math.abs(a.b - b.b), math.abs(a.a - b.a))
                    );
                    if (delta != 0)
                    {
                        if (first < 0)
                            first = i;
                        mismatches++;
                        maxDelta = math.max(maxDelta, delta);
                    }
                }
                Result[0] = mismatches + math.abs(A.Length - B.Length);
                Result[1] = first;
                Result[2] = maxDelta;
            }
        }

        private struct AtlasRun
        {
            public double Begin;
            public double Job;
            public double Flush;
            public int Pages;
        }

        private sealed class AtlasTiming
        {
            public double Begin = double.MaxValue;
            public double Job = double.MaxValue;
            public double Flush = double.MaxValue;
            public double MainThread = double.MaxValue;
            public int Pages;

            public void Add(AtlasRun run)
            {
                Pages = run.Pages;
                if (run.Begin + run.Flush < MainThread)
                {
                    MainThread = run.Begin + run.Flush;
                    Begin = run.Begin;
                    Flush = run.Flush;
                }
                Job = Math.Min(Job, run.Job);
            }
        }

        public static string Run(IEnumerable<string> paths, int runs)
        {
            var report = new StringBuilder();
            BurstCompiler.Options.EnableBurstCompileSynchronously = true;
            report.AppendLine(
                $"burst={BurstCompiler.IsEnabled} workers={JobsUtility.JobWorkerCount} cores={SystemInfo.processorCount} "
                    + $"cpu={SystemInfo.processorType} gfx={SystemInfo.graphicsDeviceType} colorSpace={QualitySettings.activeColorSpace} "
                    + $"compositorBudget={Mb(BasisImagePickupSettings.MaxResidentAnimationCompositorBytes):0} MiB "
                    + $"workingSetBudget={Mb(BasisImagePickupSettings.MaxAnimationNativeWorkingSetBytes):0} MiB"
            );
            foreach (string path in paths)
            {
                report.AppendLine();
                report.AppendLine($"== {Path.GetFileName(path)} ({Mb(new FileInfo(path).Length):0.0} MiB)");
                try
                {
                    RunFile(path, runs, report);
                }
                catch (Exception exception)
                {
                    report.AppendLine("   FAILED: " + exception);
                }
            }
            return report.ToString();
        }

        private static void RunFile(string path, int runs, StringBuilder report)
        {
            byte[] bytes = File.ReadAllBytes(path);

            for (int warm = 0; warm < 2; warm++)
            {
                DecodeHead(bytes, false, out _);
                DecodeNew(bytes, false, out _);
            }
            var headDecode = new double[runs];
            var newDecode = new double[runs];
            for (int run = 0; run < runs; run++)
            {
                if (run % 2 == 0)
                {
                    headDecode[run] = DecodeHead(bytes, false, out _);
                    newDecode[run] = DecodeNew(bytes, false, out _);
                }
                else
                {
                    newDecode[run] = DecodeNew(bytes, false, out _);
                    headDecode[run] = DecodeHead(bytes, false, out _);
                }
            }

            BasisAnimatedImageData data = null;
            DecodeHead(bytes, true, out BasisAnimatedImageData headData);
            try
            {
                DecodeNew(bytes, true, out data);
                Describe(data, report);
                report.AppendLine(
                    $"   decode wall time (best/median of {runs}): HEAD {Min(headDecode):0.0}/{Median(headDecode):0.0} ms, "
                        + $"NEW {Min(newDecode):0.0}/{Median(newDecode):0.0} ms ({Ratio(Min(headDecode), Min(newDecode))} best, {Ratio(Median(headDecode), Median(newDecode))} median)"
                );
                int frameMismatches = headData.FrameCount == data.FrameCount ? 0 : -1;
                if (frameMismatches == 0)
                {
                    for (int i = 0; i < data.FrameCount; i++)
                    {
                        if (!headData.GetFrame(i).Equals(data.GetFrame(i)))
                            frameMismatches++;
                    }
                }
                report.AppendLine(
                    $"   decode output vs HEAD: frame records {(frameMismatches == 0 ? "identical" : "MISMATCH " + frameMismatches)}, "
                        + $"pixels {Compare(headData.PixelsNative, data.PixelsNative)}, "
                        + $"duration {(headData.TotalDurationMicroseconds == data.TotalDurationMicroseconds ? "identical" : "MISMATCH")}"
                );
            }
            catch
            {
                data?.Dispose();
                throw;
            }
            finally
            {
                headData?.Dispose();
            }

            try
            {
                ComposeJobBench(data, report);
                CpuCanvasBench(data, report);
                AtlasBench(data, report);
                GpuReservationBench(data, report);
                ReleaseBench(data, report);
            }
            finally
            {
                data.Dispose();
            }
        }

        private static void Describe(BasisAnimatedImageData data, StringBuilder report)
        {
            int keyframes = 0;
            int over = 0;
            int previous = 0;
            int background = 0;
            long area = 0;
            for (int i = 0; i < data.FrameCount; i++)
            {
                BasisAnimatedImageFrame frame = data.GetFrame(i);
                if (BasisAnimatedImageWorkEstimator.IsKeyframe(data, frame))
                    keyframes++;
                if (frame.Blend != BasisAnimationBlend.Source)
                    over++;
                if (frame.Disposal == BasisAnimationDisposal.Previous)
                    previous++;
                else if (frame.Disposal != BasisAnimationDisposal.None)
                    background++;
                area += frame.PixelCount;
            }
            double canvasPixels = (double)data.CanvasWidth * data.CanvasHeight;
            report.AppendLine(
                $"   frames={data.FrameCount} canvas={data.CanvasWidth}x{data.CanvasHeight} decodedPool={Mb(data.DecodedFramePixels * 4L):0.0} MiB "
                    + $"keyframes={keyframes} blendOver={over} disposePrevious={previous} disposeBackground={background} "
                    + $"avgFrameArea={area / (double)data.FrameCount / canvasPixels:P0} anyAlpha={data.HasAnyAlpha} partialAlpha={data.HasPartialAlpha}"
            );
        }

        private static double DecodeNew(byte[] bytes, bool keep, out BasisAnimatedImageData data)
        {
            data = null;
            long start = Stopwatch.GetTimestamp();
            using BasisBurstGifDecodeRequest request = BasisBurstGifDecoder.Schedule(bytes);
            using BasisBurstGifDecodeResult result = request.Complete();
            long end = Stopwatch.GetTimestamp();
            if (!result.Ok)
                throw new InvalidOperationException("NEW decode failed: " + result.Error);
            if (keep)
                data = result.TakeAnimation();
            return Ms(end - start);
        }

        private static double DecodeHead(byte[] bytes, bool keep, out BasisAnimatedImageData data)
        {
            data = null;
            long start = Stopwatch.GetTimestamp();
            using ZZHeadBasisBurstGifDecodeRequest request = ZZHeadBasisBurstGifDecoder.Schedule(bytes);
            using ZZHeadBasisBurstGifDecodeResult result = request.Complete();
            long end = Stopwatch.GetTimestamp();
            if (!result.Ok)
                throw new InvalidOperationException("HEAD decode failed: " + result.Error);
            if (keep)
                data = result.TakeAnimation();
            return Ms(end - start);
        }

        private static void ComposeJobBench(BasisAnimatedImageData data, StringBuilder report)
        {
            int canvasPixels = data.CanvasWidth * data.CanvasHeight;
            int previousLength = data.RequiresPreviousCanvas ? canvasPixels : 1;
            byte linear = QualitySettings.activeColorSpace == ColorSpace.Linear ? (byte)1 : (byte)0;
            var headCanvas = new NativeArray<Color32>(canvasPixels, Allocator.Persistent);
            var headPrevious = new NativeArray<Color32>(previousLength, Allocator.Persistent);
            var newCanvas = new NativeArray<Color32>(canvasPixels, Allocator.Persistent);
            var newPrevious = new NativeArray<Color32>(previousLength, Allocator.Persistent);
            try
            {
                int mismatchedFrames = 0;
                string firstMismatch = null;
                for (int frame = 0; frame < data.FrameCount; frame++)
                {
                    ComposeHead(data, frame, linear, headCanvas, headPrevious);
                    ComposeNew(data, frame, linear, newCanvas, newPrevious);
                    string comparison = Compare(headCanvas, newCanvas);
                    if (comparison != "identical")
                    {
                        mismatchedFrames++;
                        firstMismatch ??= $"first at frame {frame}: {comparison}";
                    }
                }

                double headBest = double.MaxValue;
                double newBest = double.MaxValue;
                for (int pass = 0; pass < 4; pass++)
                {
                    for (int order = 0; order < 2; order++)
                    {
                        long start = Stopwatch.GetTimestamp();
                        bool head = (pass + order) % 2 == 0;
                        for (int frame = 0; frame < data.FrameCount; frame++)
                        {
                            if (head)
                                ComposeHead(data, frame, linear, headCanvas, headPrevious);
                            else
                                ComposeNew(data, frame, linear, newCanvas, newPrevious);
                        }
                        double elapsed = Ms(Stopwatch.GetTimestamp() - start);
                        if (head)
                            headBest = Math.Min(headBest, elapsed);
                        else
                            newBest = Math.Min(newBest, elapsed);
                    }
                }
                report.AppendLine(
                    $"   CPU compose job, every frame in order (best of 4): HEAD {headBest:0.0} ms, NEW {newBest:0.0} ms ({Ratio(headBest, newBest)}); "
                        + $"canvas vs HEAD after every frame: {(mismatchedFrames == 0 ? "identical" : mismatchedFrames + " frames differ, " + firstMismatch)}"
                );
            }
            finally
            {
                headCanvas.Dispose();
                headPrevious.Dispose();
                newCanvas.Dispose();
                newPrevious.Dispose();
            }
        }

        private static void ComposeHead(BasisAnimatedImageData data, int frame, byte linear, NativeArray<Color32> canvas, NativeArray<Color32> previous)
        {
            new ZZHeadBasisAnimatedImageCpuComposeJob
            {
                CanvasWidth = data.CanvasWidth,
                Reset = frame == 0 ? (byte)1 : (byte)0,
                StartFrame = frame - 1,
                TargetFrame = frame,
                Linear = linear,
                Background = data.BackgroundColor,
                Frames = data.FramesNative,
                FramePixels = data.PixelsNative,
                Canvas = canvas,
                Previous = previous,
            }.Run();
        }

        private static void ComposeNew(BasisAnimatedImageData data, int frame, byte linear, NativeArray<Color32> canvas, NativeArray<Color32> previous)
        {
            new BasisAnimatedImageCpuComposeJob
            {
                CanvasWidth = data.CanvasWidth,
                Reset = frame == 0 ? (byte)1 : (byte)0,
                StartFrame = frame - 1,
                TargetFrame = frame,
                Linear = linear,
                Background = data.BackgroundColor,
                Frames = data.FramesNative,
                FramePixels = data.PixelsNative,
                Canvas = canvas,
                Previous = previous,
            }.Run();
        }

        private static void CpuCanvasBench(BasisAnimatedImageData data, StringBuilder report)
        {
            double headSequential = double.MaxValue;
            double newSequential = double.MaxValue;
            for (int pass = 0; pass < 2; pass++)
            {
                using (var head = new ZZHeadBasisAnimatedImageCpuCanvas(data))
                {
                    long start = Stopwatch.GetTimestamp();
                    for (int frame = 0; frame < data.FrameCount; frame++)
                        head.UpdateToState(0, frame);
                    headSequential = Math.Min(headSequential, Ms(Stopwatch.GetTimestamp() - start));
                }
                using (var neu = new BasisAnimatedImageCpuCanvas(data))
                {
                    long start = Stopwatch.GetTimestamp();
                    for (int frame = 0; frame < data.FrameCount; frame++)
                        neu.UpdateToState(0, frame);
                    newSequential = Math.Min(newSequential, Ms(Stopwatch.GetTimestamp() - start));
                }
            }
            report.AppendLine(
                $"   CPU canvas playback incl. texture upload, every frame (best of 2): HEAD {headSequential:0.0} ms, NEW {newSequential:0.0} ms ({Ratio(headSequential, newSequential)})"
            );

            foreach (int target in new[] { data.FrameCount / 2, data.FrameCount - 1 })
            {
                double headSeek = double.MaxValue;
                double newSeek = double.MaxValue;
                string comparison = null;
                for (int pass = 0; pass < 3; pass++)
                {
                    using var head = new ZZHeadBasisAnimatedImageCpuCanvas(data);
                    using var neu = new BasisAnimatedImageCpuCanvas(data);
                    long t0 = Stopwatch.GetTimestamp();
                    head.UpdateToState(0, target);
                    long t1 = Stopwatch.GetTimestamp();
                    neu.UpdateToState(0, target);
                    long t2 = Stopwatch.GetTimestamp();
                    headSeek = Math.Min(headSeek, Ms(t1 - t0));
                    newSeek = Math.Min(newSeek, Ms(t2 - t1));
                    comparison ??= Compare(
                        ((Texture2D)head.OutputTexture).GetPixelData<Color32>(0),
                        ((Texture2D)neu.OutputTexture).GetPixelData<Color32>(0)
                    );
                }
                int first = BasisAnimatedImageWorkEstimator.FindFirstFrameToDraw(data, -1, target, out bool keyframe);
                report.AppendLine(
                    $"   CPU seek from a fresh canvas to frame {target}: HEAD draws {target + 1} frames in {headSeek:0.0} ms, "
                        + $"NEW draws {target - first + 1}{(keyframe ? $" (from keyframe {first})" : string.Empty)} in {newSeek:0.0} ms ({Ratio(headSeek, newSeek)}); output vs HEAD: {comparison}"
                );
            }
        }

        private static void AtlasBench(BasisAnimatedImageData data, StringBuilder report)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo headHandle = typeof(ZZHeadBasisAnimationFrameAtlas).GetField("_pageHandle", Flags);
            FieldInfo newHandle = typeof(BasisAnimationFrameAtlas).GetField("_pageHandle", Flags);
            var headTiming = new AtlasTiming();
            var newTiming = new AtlasTiming();
            for (int pass = 0; pass < 3; pass++)
            {
                for (int order = 0; order < 2; order++)
                {
                    if ((pass + order) % 2 == 0)
                    {
                        using var atlas = new ZZHeadBasisAnimationFrameAtlas(data);
                        headTiming.Add(TimeAtlas(atlas, () => atlas.IsReady, () => atlas.BeginNextPage(long.MaxValue, out _), atlas.FlushPendingPage, headHandle));
                    }
                    else
                    {
                        using var atlas = new BasisAnimationFrameAtlas(data);
                        newTiming.Add(TimeAtlas(atlas, () => atlas.IsReady, () => atlas.BeginNextPage(long.MaxValue, out _), atlas.FlushPendingPage, newHandle));
                    }
                }
            }
            report.AppendLine(
                $"   GPU atlas build, {newTiming.Pages} pages (best of 3): main thread HEAD {headTiming.MainThread:0.0} ms (schedule {headTiming.Begin:0.0} + upload {headTiming.Flush:0.0}), "
                    + $"NEW {newTiming.MainThread:0.0} ms (schedule {newTiming.Begin:0.0} + upload {newTiming.Flush:0.0}) ({Ratio(headTiming.MainThread, newTiming.MainThread)}); "
                    + $"worker populate HEAD {headTiming.Job:0.0} ms, NEW {newTiming.Job:0.0} ms"
            );
        }

        private static AtlasRun TimeAtlas(object atlas, Func<bool> isReady, Func<bool> begin, Action flush, FieldInfo handle)
        {
            var run = new AtlasRun();
            while (!isReady())
            {
                if (run.Pages > 4096)
                    throw new InvalidOperationException("Atlas never became ready.");
                long t0 = Stopwatch.GetTimestamp();
                begin();
                long t1 = Stopwatch.GetTimestamp();
                ((JobHandle)handle.GetValue(atlas)).Complete();
                long t2 = Stopwatch.GetTimestamp();
                flush();
                long t3 = Stopwatch.GetTimestamp();
                run.Begin += Ms(t1 - t0);
                run.Job += Ms(t2 - t1);
                run.Flush += Ms(t3 - t2);
                run.Pages++;
            }
            return run;
        }

        private static void GpuReservationBench(BasisAnimatedImageData data, StringBuilder report)
        {
            Shader shader = Shader.Find(CompositorShaderName);
            if (shader == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                report.AppendLine("   GPU compositor unavailable in this run");
                return;
            }
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                report.AppendLine(
                    $"   GPU compositor budget charged for this GIF: HEAD {Reserve(() => new ZZHeadBasisAnimatedImageGpuCanvas(data, material))}, "
                        + $"NEW {Reserve(() => new BasisAnimatedImageGpuCanvas(data, material))}"
                );
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static string Reserve(Func<IDisposable> create)
        {
            long before = BasisAnimatedImageData.TotalResidentCompositorBytes;
            try
            {
                using IDisposable canvas = create();
                return $"{Mb(BasisAnimatedImageData.TotalResidentCompositorBytes - before):0.0} MiB";
            }
            catch (Exception exception)
            {
                return $"refused ({exception.GetType().Name}: {exception.Message})";
            }
        }

        private static void ReleaseBench(BasisAnimatedImageData data, StringBuilder report)
        {
            long nativeBefore = data.NativeByteCount;
            long residentBefore = BasisAnimatedImageData.TotalResidentNativeBytes;
            data.ReleasePixels();
            report.AppendLine(
                $"   NEW releases the decoded frame pool after the atlas upload: GIF native memory {Mb(nativeBefore):0.0} -> {Mb(data.NativeByteCount):0.0} MiB "
                    + $"(resident total {Mb(residentBefore):0.0} -> {Mb(BasisAnimatedImageData.TotalResidentNativeBytes):0.0} MiB); HEAD kept the pool while the GIF was loaded"
            );
        }

        private static string Compare(NativeArray<Color32> a, NativeArray<Color32> b)
        {
            var result = new NativeArray<long>(3, Allocator.TempJob);
            try
            {
                new CompareJob { A = a, B = b, Result = result }.Run();
                return result[0] == 0
                    ? "identical"
                    : $"MISMATCH {result[0]} px (first {result[1]}, max channel delta {result[2]})";
            }
            finally
            {
                result.Dispose();
            }
        }

        private static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

        private static double Mb(long bytes) => bytes / (1024.0 * 1024.0);

        private static string Ratio(double head, double neu) => neu > 0 ? $"{head / neu:0.00}x faster" : "n/a";

        private static double Min(double[] values)
        {
            double min = double.MaxValue;
            foreach (double value in values)
                min = Math.Min(min, value);
            return min;
        }

        private static double Median(double[] values)
        {
            var copy = (double[])values.Clone();
            Array.Sort(copy);
            return copy[copy.Length / 2];
        }
    }
}
