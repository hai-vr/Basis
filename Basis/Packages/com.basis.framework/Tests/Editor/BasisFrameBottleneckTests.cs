using Basis.Scripts.Drivers;
using NUnit.Framework;

namespace Basis.Tests.Rendering
{
    /// <summary>
    /// Covers the CPU-versus-GPU verdict shown on the Graphics settings page. Every case is built
    /// from milliseconds the way Unity's FrameTimingManager reports them: main-thread time includes
    /// the stall waiting on present, so the classifier has to take that back off before it compares
    /// the two sides.
    /// </summary>
    public class BasisFrameBottleneckTests
    {
        const double Sixty = 1000.0 / 60.0;
        const double Ninety = 1000.0 / 90.0;

        static BasisFrameBottleneckReading Reading(
            double frameMs,
            double mainMs,
            double presentWaitMs,
            double gpuMs,
            double targetMs = 0.0,
            bool hasGpuTiming = true)
        {
            BasisFrameBottleneckReading reading = new BasisFrameBottleneckReading
            {
                FrameMs = frameMs,
                MainThreadMs = mainMs,
                RenderThreadMs = 0.0,
                PresentWaitMs = presentWaitMs,
                GpuMs = gpuMs,
                TargetMs = targetMs,
                HasGpuTiming = hasGpuTiming
            };
            reading.CpuBusyMs = BasisFrameBottleneck.ResolveCpuBusyMs(reading);
            return reading;
        }

        static BasisFrameBottleneckKind Classify(BasisFrameBottleneckReading reading) =>
            BasisFrameBottleneck.Classify(reading, true);

        static BasisFrameBottleneckKind Classify(
            BasisFrameBottleneckReading reading, BasisFrameBottleneckKind shown) =>
            BasisFrameBottleneck.Classify(reading, true, shown);

        [Test]
        public void PresentWaitIsNotCountedAsCpuWork()
        {
            BasisFrameBottleneckReading reading = Reading(frameMs: 20.0, mainMs: 20.0, presentWaitMs: 14.0, gpuMs: 19.0);
            Assert.That(reading.CpuBusyMs, Is.EqualTo(6.0).Within(1e-9),
                "time the main thread spends blocked in present is the GPU holding it up, not CPU cost.");
        }

        [Test]
        public void GpuOutrunningCpu_ReadsGpuLimited()
        {
            Assert.That(Classify(Reading(frameMs: 20.0, mainMs: 20.0, presentWaitMs: 14.0, gpuMs: 19.5)),
                Is.EqualTo(BasisFrameBottleneckKind.Gpu));
        }

        [Test]
        public void CpuOutrunningGpu_ReadsCpuLimited()
        {
            Assert.That(Classify(Reading(frameMs: 20.0, mainMs: 19.5, presentWaitMs: 0.5, gpuMs: 8.0)),
                Is.EqualTo(BasisFrameBottleneckKind.Cpu));
        }

        [Test]
        public void SidesWithinTheDominanceRatio_ReadBalanced()
        {
            Assert.That(Classify(Reading(frameMs: 20.0, mainMs: 20.0, presentWaitMs: 1.0, gpuMs: 19.5)),
                Is.EqualTo(BasisFrameBottleneckKind.Balanced),
                "neither side is far enough ahead to be named the bottleneck.");
        }

        [Test]
        public void BothIdleAtTheTarget_ReadsFrameCap()
        {
            Assert.That(Classify(Reading(frameMs: Sixty, mainMs: Sixty, presentWaitMs: 11.0, gpuMs: 6.0, targetMs: Sixty)),
                Is.EqualTo(BasisFrameBottleneckKind.FrameCap));
        }

        [Test]
        public void FrameCapWinsOverTheDominantSide()
        {
            BasisFrameBottleneckReading reading =
                Reading(frameMs: Sixty, mainMs: Sixty, presentWaitMs: 12.0, gpuMs: 10.0, targetMs: Sixty);
            Assert.That(reading.GpuMs, Is.GreaterThan(reading.CpuBusyMs * BasisFrameBottleneck.DominanceRatio),
                "the GPU is the busier side here, so this only passes if the cap check runs first.");
            Assert.That(Classify(reading), Is.EqualTo(BasisFrameBottleneckKind.FrameCap));
        }

        [Test]
        public void MissingTheTargetWhileGpuIsBusy_IsNotBlamedOnTheCap()
        {
            Assert.That(Classify(Reading(frameMs: 20.0, mainMs: 20.0, presentWaitMs: 12.0, gpuMs: 19.0, targetMs: Ninety)),
                Is.EqualTo(BasisFrameBottleneckKind.Gpu));
        }

        [Test]
        public void NoGpuTimer_ReportsUnavailableRatherThanGuessing()
        {
            Assert.That(Classify(Reading(frameMs: 20.0, mainMs: 20.0, presentWaitMs: 1.0, gpuMs: 0.0, hasGpuTiming: false)),
                Is.EqualTo(BasisFrameBottleneckKind.NoGpuTimer));
        }

        [Test]
        public void BeforeWarmup_ReadsMeasuring()
        {
            Assert.That(BasisFrameBottleneck.Classify(Reading(frameMs: 20.0, mainMs: 20.0, presentWaitMs: 1.0, gpuMs: 8.0), false),
                Is.EqualTo(BasisFrameBottleneckKind.Measuring));
        }

        [Test]
        public void WithoutAnyFrameTime_ReadsMeasuring()
        {
            Assert.That(Classify(Reading(frameMs: 0.0, mainMs: 0.0, presentWaitMs: 0.0, gpuMs: 0.0)),
                Is.EqualTo(BasisFrameBottleneckKind.Measuring));
        }

        [Test]
        public void MissingMainThreadTime_FallsBackToFrameTime()
        {
            BasisFrameBottleneckReading reading = Reading(frameMs: 12.0, mainMs: 0.0, presentWaitMs: 2.0, gpuMs: 4.0);
            Assert.That(reading.CpuBusyMs, Is.EqualTo(10.0).Within(1e-9));
            Assert.That(Classify(reading), Is.EqualTo(BasisFrameBottleneckKind.Cpu));
        }

        [Test]
        public void SittingJustOverTheCapHeadroom_HoldsTheCapVerdictItAlreadyShows()
        {
            BasisFrameBottleneckReading reading =
                Reading(frameMs: 11.4, mainMs: 11.4, presentWaitMs: 1.6, gpuMs: 9.6, targetMs: Ninety);

            Assert.That(Classify(reading), Is.EqualTo(BasisFrameBottleneckKind.Balanced),
                "on its own this frame reads a hair over the cap's headroom allowance.");
            Assert.That(Classify(reading, BasisFrameBottleneckKind.FrameCap),
                Is.EqualTo(BasisFrameBottleneckKind.FrameCap),
                "a frame that close to the cap must not flip the panel's verdict back and forth.");
        }

        [Test]
        public void FallingWellShortOfTheCap_StillReleasesTheCapVerdict()
        {
            Assert.That(
                Classify(Reading(frameMs: 13.5, mainMs: 13.5, presentWaitMs: 1.0, gpuMs: 6.0, targetMs: Ninety),
                    BasisFrameBottleneckKind.FrameCap),
                Is.EqualTo(BasisFrameBottleneckKind.Cpu),
                "hysteresis holds a verdict through noise, it does not hide a real change.");
        }

        [Test]
        public void CpuSlippingBackUnderTheDominanceRatio_KeepsTheCpuVerdict()
        {
            BasisFrameBottleneckReading reading =
                Reading(frameMs: 20.0, mainMs: 20.0, presentWaitMs: 0.0, gpuMs: 18.0);

            Assert.That(Classify(reading), Is.EqualTo(BasisFrameBottleneckKind.Balanced));
            Assert.That(Classify(reading, BasisFrameBottleneckKind.Cpu),
                Is.EqualTo(BasisFrameBottleneckKind.Cpu));
        }

        [Test]
        public void GpuSlippingBackUnderTheDominanceRatio_KeepsTheGpuVerdict()
        {
            BasisFrameBottleneckReading reading =
                Reading(frameMs: 20.0, mainMs: 20.0, presentWaitMs: 2.0, gpuMs: 20.0);

            Assert.That(Classify(reading), Is.EqualTo(BasisFrameBottleneckKind.Balanced));
            Assert.That(Classify(reading, BasisFrameBottleneckKind.Gpu),
                Is.EqualTo(BasisFrameBottleneckKind.Gpu));
        }

        [Test]
        public void SidesEveningOutCompletely_ReleasesTheHeldVerdict()
        {
            Assert.That(
                Classify(Reading(frameMs: 20.0, mainMs: 20.0, presentWaitMs: 0.0, gpuMs: 19.8),
                    BasisFrameBottleneckKind.Cpu),
                Is.EqualTo(BasisFrameBottleneckKind.Balanced));
        }

        [Test]
        public void TheOtherSideTakingOver_IsNotHeldBackByTheOldVerdict()
        {
            Assert.That(
                Classify(Reading(frameMs: 20.0, mainMs: 20.0, presentWaitMs: 12.0, gpuMs: 19.0),
                    BasisFrameBottleneckKind.Cpu),
                Is.EqualTo(BasisFrameBottleneckKind.Gpu));
        }
    }
}
