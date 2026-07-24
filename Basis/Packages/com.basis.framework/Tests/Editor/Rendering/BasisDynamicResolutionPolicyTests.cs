using Basis.Scripts.Rendering;
using NUnit.Framework;

namespace Basis.Tests.Rendering
{
    public sealed class BasisDynamicResolutionPolicyTests
    {
        const float Target90Hz = 1000f / 90f;

        static BasisDynamicResolutionSettings Settings()
        {
            return BasisDynamicResolutionSettings.Default();
        }

        static BasisDynamicResolutionState StartedAtMaximum(in BasisDynamicResolutionSettings settings)
        {
            return new BasisDynamicResolutionState
            {
                Scale = settings.MaximumScale,
                SmoothedGpuMilliseconds = 0f,
                FramesSinceChange = settings.SettleFrames
            };
        }

        static float Settle(in BasisDynamicResolutionSettings settings, ref BasisDynamicResolutionState state, float gpuMilliseconds, float targetMilliseconds, int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                BasisDynamicResolutionPolicy.Evaluate(settings, ref state, gpuMilliseconds, targetMilliseconds);
            }
            return state.Scale;
        }

        [Test]
        public void OnBudget_HoldsScale()
        {
            BasisDynamicResolutionSettings settings = Settings();
            BasisDynamicResolutionState state = StartedAtMaximum(settings);
            state.SmoothedGpuMilliseconds = Target90Hz;

            float scale = Settle(settings, ref state, Target90Hz, Target90Hz, 120);

            Assert.AreEqual(settings.MaximumScale, scale, 0.0001f, "A frame exactly on budget must not move the resolution");
        }

        [Test]
        public void SustainedOverload_LowersScale()
        {
            BasisDynamicResolutionSettings settings = Settings();
            BasisDynamicResolutionState state = StartedAtMaximum(settings);
            state.SmoothedGpuMilliseconds = Target90Hz;

            float scale = Settle(settings, ref state, Target90Hz * 2f, Target90Hz, 300);

            Assert.Less(scale, settings.MaximumScale, "A GPU at twice the frame budget must reduce resolution");
            Assert.AreEqual(settings.MinimumScale, scale, 0.0001f, "A sustained 2x overload must drive scale to the configured floor");
        }

        [Test]
        public void SustainedHeadroom_RaisesScaleBackToMaximum()
        {
            BasisDynamicResolutionSettings settings = Settings();
            BasisDynamicResolutionState state = StartedAtMaximum(settings);
            state.Scale = settings.MinimumScale;
            state.SmoothedGpuMilliseconds = Target90Hz;

            float scale = Settle(settings, ref state, Target90Hz * 0.4f, Target90Hz, 600);

            Assert.AreEqual(settings.MaximumScale, scale, 0.0001f, "Plenty of headroom must return the resolution to the configured ceiling");
        }

        [Test]
        public void NeverExceedsConfiguredBounds()
        {
            BasisDynamicResolutionSettings settings = Settings();
            BasisDynamicResolutionState state = StartedAtMaximum(settings);
            state.SmoothedGpuMilliseconds = Target90Hz;

            float[] loads = { 0.1f, 8f, 0.2f, 5f, 1f, 0.05f, 12f };
            foreach (float load in loads)
            {
                for (int frame = 0; frame < 90; frame++)
                {
                    BasisDynamicResolutionPolicy.Evaluate(settings, ref state, Target90Hz * load, Target90Hz);
                    Assert.GreaterOrEqual(state.Scale, settings.MinimumScale, "Scale must never fall below the configured floor");
                    Assert.LessOrEqual(state.Scale, settings.MaximumScale, "Scale must never rise above the configured ceiling");
                }
            }
        }

        [Test]
        public void RecoveryIsSlowerThanReduction()
        {
            BasisDynamicResolutionSettings settings = Settings();

            BasisDynamicResolutionState dropping = StartedAtMaximum(settings);
            dropping.SmoothedGpuMilliseconds = Target90Hz;
            int framesToDrop = 0;
            while (dropping.Scale > 0.7f && framesToDrop < 2000)
            {
                BasisDynamicResolutionPolicy.Evaluate(settings, ref dropping, Target90Hz * 2f, Target90Hz);
                framesToDrop++;
            }

            BasisDynamicResolutionState raising = StartedAtMaximum(settings);
            raising.Scale = 0.7f;
            raising.SmoothedGpuMilliseconds = Target90Hz;
            int framesToRaise = 0;
            while (raising.Scale < settings.MaximumScale && framesToRaise < 2000)
            {
                BasisDynamicResolutionPolicy.Evaluate(settings, ref raising, Target90Hz * 0.4f, Target90Hz);
                framesToRaise++;
            }

            Assert.Greater(framesToRaise, framesToDrop, "Resolution must drop quickly under load but recover gradually to avoid visible pumping");
        }

        [Test]
        public void HysteresisBand_SuppressesMinorJitter()
        {
            BasisDynamicResolutionSettings settings = Settings();
            BasisDynamicResolutionState state = StartedAtMaximum(settings);
            state.SmoothedGpuMilliseconds = Target90Hz;

            int changes = 0;
            for (int frame = 0; frame < 600; frame++)
            {
                float jitter = (frame % 2 == 0) ? 1.01f : 0.99f;
                if (BasisDynamicResolutionPolicy.Evaluate(settings, ref state, Target90Hz * jitter, Target90Hz))
                {
                    changes++;
                }
            }

            Assert.AreEqual(0, changes, "Frame to frame jitter inside the hysteresis band must not retrigger scaling");
        }

        [Test]
        public void SettleWindow_RateLimitsConsecutiveChanges()
        {
            BasisDynamicResolutionSettings settings = Settings();
            BasisDynamicResolutionState state = StartedAtMaximum(settings);
            state.SmoothedGpuMilliseconds = Target90Hz;

            int changes = 0;
            const int frames = 120;
            for (int frame = 0; frame < frames; frame++)
            {
                if (BasisDynamicResolutionPolicy.Evaluate(settings, ref state, Target90Hz * 4f, Target90Hz))
                {
                    changes++;
                }
            }

            Assert.LessOrEqual(changes, frames / settings.SettleFrames, "Changes must be rate limited by the settle window");
            Assert.Greater(changes, 0, "A sustained overload must still produce changes");
        }

        [Test]
        public void ConvergesToStableScaleUnderConstantLoad()
        {
            BasisDynamicResolutionSettings settings = Settings();
            BasisDynamicResolutionState state = StartedAtMaximum(settings);
            state.SmoothedGpuMilliseconds = Target90Hz;

            for (int frame = 0; frame < 4000; frame++)
            {
                float cost = Target90Hz * 1.6f * (state.Scale * state.Scale);
                BasisDynamicResolutionPolicy.Evaluate(settings, ref state, cost, Target90Hz);
            }

            float settledCost = Target90Hz * 1.6f * (state.Scale * state.Scale);

            Assert.LessOrEqual(settledCost, Target90Hz * 1.02f, "A closed loop against a quadratic cost model must settle inside the frame budget");
            Assert.Greater(state.Scale, settings.MinimumScale, "The loop must find an interior equilibrium rather than collapsing to the floor");
        }

        [Test]
        public void ZeroFrameTime_IsIgnored()
        {
            BasisDynamicResolutionSettings settings = Settings();
            BasisDynamicResolutionState state = StartedAtMaximum(settings);
            state.SmoothedGpuMilliseconds = Target90Hz;

            bool changed = BasisDynamicResolutionPolicy.Evaluate(settings, ref state, 0f, Target90Hz);

            Assert.IsFalse(changed, "A missing GPU timing sample must not move the resolution");
            Assert.AreEqual(settings.MaximumScale, state.Scale, 0.0001f, "Scale must be left untouched when timings are unavailable");
        }

        [Test]
        public void ZeroTarget_IsIgnored()
        {
            BasisDynamicResolutionSettings settings = Settings();
            BasisDynamicResolutionState state = StartedAtMaximum(settings);

            bool changed = BasisDynamicResolutionPolicy.Evaluate(settings, ref state, Target90Hz, 0f);

            Assert.IsFalse(changed, "An unknown refresh rate must not move the resolution");
        }

        [Test]
        public void UninitialisedState_StartsAtMaximum()
        {
            BasisDynamicResolutionSettings settings = Settings();
            BasisDynamicResolutionState state = new BasisDynamicResolutionState();

            BasisDynamicResolutionPolicy.Evaluate(settings, ref state, Target90Hz, Target90Hz);

            Assert.AreEqual(settings.MaximumScale, state.Scale, 0.0001f, "A fresh state must begin at the ceiling so quality is never silently reduced");
        }

        [Test]
        public void SmoothSample_IgnoresMissingSamples()
        {
            float smoothed = BasisDynamicResolutionPolicy.SmoothSample(10f, 0f, 0.1f);

            Assert.AreEqual(10f, smoothed, 0.0001f, "A dropped timing sample must leave the smoothed value untouched");
        }

        [Test]
        public void SmoothSample_SeedsFromFirstSample()
        {
            float smoothed = BasisDynamicResolutionPolicy.SmoothSample(0f, 12f, 0.1f);

            Assert.AreEqual(12f, smoothed, 0.0001f, "The first sample must seed the average rather than ramp from zero");
        }

        [Test]
        public void EqualBounds_PinScale()
        {
            BasisDynamicResolutionSettings settings = Settings();
            settings.MinimumScale = 0.8f;
            settings.MaximumScale = 0.8f;
            BasisDynamicResolutionState state = StartedAtMaximum(settings);
            state.SmoothedGpuMilliseconds = Target90Hz;

            float scale = Settle(settings, ref state, Target90Hz * 4f, Target90Hz, 300);

            Assert.AreEqual(0.8f, scale, 0.0001f, "Matching bounds must pin the scale rather than oscillate");
        }
    }
}
