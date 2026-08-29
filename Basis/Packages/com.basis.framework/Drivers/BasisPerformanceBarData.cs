using System;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;
using UnityProfiler = UnityEngine.Profiling.Profiler;
#if BASIS_HAS_RTAO && !UNITY_ANDROID
using Basis.Rendering.RTAO;
#endif

namespace Basis.Scripts.Drivers
{
    public enum BasisPerformanceGpuSegment { Shadows, Opaque, GlobalIllumination, Reflections, Rtao, Transparent, Other }
    public enum BasisPerformanceCpuSegment { EventDriver, Ik, Movement, AvatarLoad, Networking, Jiggle, Voice, RenderDispatch, Other }

    public static class BasisPerformanceBarData
    {
        public const int GpuSegmentCount = 7;
        public const int CpuSegmentCount = 9;

        private const double Smoothing = 0.15;
        private const double NsToMs = 1.0 / 1_000_000.0;
        private const int RescanInterval = 120;
        private const int MaxRescanInterval = 1800;
        private const ProfilerRecorderOptions MarkerOptions =
            ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
            ProfilerRecorderOptions.StartImmediately |
            ProfilerRecorderOptions.SumAllSamplesInFrame;

        private sealed class MarkerRow
        {
            public ProfilerRecorder Recorder;
            public BasisPerformanceCpuSegment Segment;
        }

        private static readonly float[] gpuMs = new float[GpuSegmentCount];
        private static readonly float[] cpuMs = new float[CpuSegmentCount];
        private static readonly List<MarkerRow> cpuRows = new List<MarkerRow>();
        private static readonly HashSet<string> knownMarkers = new HashSet<string>();
        private static readonly List<ProfilerRecorderHandle> handleScratch = new List<ProfilerRecorderHandle>(1024);

        private static int subscribers;
        private static bool forcedProfiler, profilerWasEnabled;
        private static int rescanCountdown, rescanInterval = RescanInterval;
        private static int lastSampledFrame = -1;

        public static float[] GpuMs => gpuMs;
        public static float[] CpuMs => cpuMs;
        public static float TargetMs { get; private set; }
        public static long Published { get; private set; }

        public static void AddSubscriber()
        {
            subscribers++;
            if (subscribers == 1) Activate();
        }

        public static void RemoveSubscriber()
        {
            if (subscribers <= 0) return;
            subscribers--;
            if (subscribers == 0) Deactivate();
        }

        private static void Activate()
        {
            profilerWasEnabled = UnityProfiler.enabled;
            UnityProfiler.enabled = true;
            forcedProfiler = true;
            SetGpuProfilingEnabled(true);
            rescanCountdown = 0;
            rescanInterval = RescanInterval;
            lastSampledFrame = -1;
        }

        private static void Deactivate()
        {
            if (forcedProfiler)
            {
                UnityProfiler.enabled = profilerWasEnabled;
                forcedProfiler = false;
            }
            SetGpuProfilingEnabled(false);
            DisposeCpuRecorders();
            Array.Clear(gpuMs, 0, gpuMs.Length);
            Array.Clear(cpuMs, 0, cpuMs.Length);
            Published++;
        }

        private static void SetGpuProfilingEnabled(bool enabled)
        {
            MainLightShadowCasterPass.SetProfilingEnabled(enabled);
            AdditionalLightsShadowCasterPass.SetProfilingEnabled(enabled);
#if BASIS_HAS_GI && !UNITY_ANDROID
            BasisGlobalIlluminationPass.SetProfilingEnabled(enabled);
            BasisGlobalIlluminationPass.SpecularPass.SetProfilingEnabled(enabled);
#endif
#if BASIS_HAS_RTAO && !UNITY_ANDROID
            BasisRTAOPass.SetProfilingEnabled(enabled);
#endif
            SetSamplerRecording(URPProfileId.DrawOpaqueObjects, enabled);
            SetSamplerRecording(URPProfileId.DrawTransparentObjects, enabled);
            SetSamplerRecording(URPProfileId.RecordRenderGraph, enabled);
        }

        private static void SetSamplerRecording(URPProfileId id, bool enabled)
        {
            ProfilingSampler sampler = ProfilingSampler.Get(id);
            if (sampler != null) sampler.enableRecording = enabled;
        }

        public static void Sample()
        {
            if (subscribers <= 0) return;
            int frame = Time.frameCount;
            if (frame == lastSampledFrame) return;
            lastSampledFrame = frame;

            if (--rescanCountdown <= 0)
            {
                int before = cpuRows.Count;
                RescanCpuMarkers();
                rescanInterval = cpuRows.Count == before ? Math.Min(rescanInterval * 2, MaxRescanInterval) : RescanInterval;
                rescanCountdown = rescanInterval;
            }

            BasisFrameBottleneck.Sample();
            BasisFrameBottleneckReading reading = BasisFrameBottleneck.Read();
            TargetMs = (float)reading.TargetMs;

            SampleGpu(reading);
            SampleCpu(reading);
            Published++;
        }

        private static void SampleGpu(BasisFrameBottleneckReading reading)
        {
            float shadows = MainLightShadowCasterPass.GpuMs + AdditionalLightsShadowCasterPass.GpuMs;
            float opaque = SamplerGpuMs(URPProfileId.DrawOpaqueObjects);
            float transparent = SamplerGpuMs(URPProfileId.DrawTransparentObjects);
            float gi = 0f, reflections = 0f, rtao = 0f;
#if BASIS_HAS_GI && !UNITY_ANDROID
            gi = BasisGlobalIlluminationPass.GpuMs;
            reflections = BasisGlobalIlluminationPass.SpecularPass.GpuMs;
#endif
#if BASIS_HAS_RTAO && !UNITY_ANDROID
            rtao = BasisRTAOPass.GpuMs;
#endif
            float named = shadows + opaque + gi + reflections + rtao + transparent;
            float other = Mathf.Max(0f, (float)reading.GpuMs - named);

            Accumulate(gpuMs, (int)BasisPerformanceGpuSegment.Shadows, shadows);
            Accumulate(gpuMs, (int)BasisPerformanceGpuSegment.Opaque, opaque);
            Accumulate(gpuMs, (int)BasisPerformanceGpuSegment.GlobalIllumination, gi);
            Accumulate(gpuMs, (int)BasisPerformanceGpuSegment.Reflections, reflections);
            Accumulate(gpuMs, (int)BasisPerformanceGpuSegment.Rtao, rtao);
            Accumulate(gpuMs, (int)BasisPerformanceGpuSegment.Transparent, transparent);
            Accumulate(gpuMs, (int)BasisPerformanceGpuSegment.Other, other);
        }

        private static void SampleCpu(BasisFrameBottleneckReading reading)
        {
            float eventDriver = 0f, ik = 0f, movement = 0f, avatarLoad = 0f, networking = 0f, jiggle = 0f, voice = 0f;
            for (int i = 0; i < cpuRows.Count; i++)
            {
                MarkerRow row = cpuRows[i];
                if (!row.Recorder.Valid || row.Recorder.Count == 0) continue;
                float ms = (float)(row.Recorder.GetSample(row.Recorder.Count - 1).Value * NsToMs);
                switch (row.Segment)
                {
                    case BasisPerformanceCpuSegment.EventDriver: eventDriver += ms; break;
                    case BasisPerformanceCpuSegment.Ik: ik += ms; break;
                    case BasisPerformanceCpuSegment.Movement: movement += ms; break;
                    case BasisPerformanceCpuSegment.AvatarLoad: avatarLoad += ms; break;
                    case BasisPerformanceCpuSegment.Networking: networking += ms; break;
                    case BasisPerformanceCpuSegment.Jiggle: jiggle += ms; break;
                    case BasisPerformanceCpuSegment.Voice: voice += ms; break;
                }
            }
            float renderDispatch = SamplerCpuMs(URPProfileId.RecordRenderGraph);
            float named = eventDriver + ik + movement + avatarLoad + networking + jiggle + voice + renderDispatch;
            float other = Mathf.Max(0f, (float)reading.CpuBusyMs - named);

            Accumulate(cpuMs, (int)BasisPerformanceCpuSegment.EventDriver, eventDriver);
            Accumulate(cpuMs, (int)BasisPerformanceCpuSegment.Ik, ik);
            Accumulate(cpuMs, (int)BasisPerformanceCpuSegment.Movement, movement);
            Accumulate(cpuMs, (int)BasisPerformanceCpuSegment.AvatarLoad, avatarLoad);
            Accumulate(cpuMs, (int)BasisPerformanceCpuSegment.Networking, networking);
            Accumulate(cpuMs, (int)BasisPerformanceCpuSegment.Jiggle, jiggle);
            Accumulate(cpuMs, (int)BasisPerformanceCpuSegment.Voice, voice);
            Accumulate(cpuMs, (int)BasisPerformanceCpuSegment.RenderDispatch, renderDispatch);
            Accumulate(cpuMs, (int)BasisPerformanceCpuSegment.Other, other);
        }

        private static float SamplerGpuMs(URPProfileId id)
        {
            ProfilingSampler sampler = ProfilingSampler.Get(id);
            return sampler != null ? sampler.gpuElapsedTime : 0f;
        }

        private static float SamplerCpuMs(URPProfileId id)
        {
            ProfilingSampler sampler = ProfilingSampler.Get(id);
            return sampler != null ? sampler.cpuElapsedTime : 0f;
        }

        // Unlike BasisFrameBottleneck.Accumulate, a zero reading here is real (a disabled segment must
        // decay to zero) rather than "no data yet", so it is smoothed toward like any other value.
        private static void Accumulate(float[] target, int index, float value)
        {
            float smoothed = target[index];
            target[index] = smoothed <= 0f && value <= 0f ? 0f : smoothed + (value - smoothed) * (float)Smoothing;
        }

        private static void RescanCpuMarkers()
        {
            handleScratch.Clear();
            ProfilerRecorderHandle.GetAvailable(handleScratch);
            for (int h = 0; h < handleScratch.Count; h++)
            {
                ProfilerRecorderHandle handle = handleScratch[h];
                ProfilerRecorderDescription desc = ProfilerRecorderHandle.GetDescription(handle);
                if (desc.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                string name = desc.Name;
                if (knownMarkers.Contains(name)) continue;

                BasisPerformanceCpuSegment? segment = ClassifyCpuMarker(name);
                if (segment == null) continue;

                knownMarkers.Add(name);
                cpuRows.Add(new MarkerRow { Recorder = CreateCpuRecorder(handle), Segment = segment.Value });
            }
        }

        // Order matters: every subsystem-specific prefix is matched before the bare "BasisDriver."
        // catch-all, so the driver's own dispatch markers (Update/LateUpdate/DeviceManagement/NamePlate/...)
        // land in EventDriver without double counting a subsystem that gets its own segment. Sync rides
        // with Networking (same job, different marker group); LocalPlayer and LocoPose both describe the
        // per-frame avatar pose/movement/bone/animator dispatch, so they share Movement.
        private static BasisPerformanceCpuSegment? ClassifyCpuMarker(string name)
        {
            if (name.StartsWith("BasisDriver.Network.", StringComparison.Ordinal)) return BasisPerformanceCpuSegment.Networking;
            if (name.StartsWith("BasisDriver.Sync.", StringComparison.Ordinal)) return BasisPerformanceCpuSegment.Networking;
            if (name.StartsWith("BasisDriver.Jiggle.", StringComparison.Ordinal)) return BasisPerformanceCpuSegment.Jiggle;
            if (name.StartsWith("BasisDriver.HVRComms.", StringComparison.Ordinal)) return BasisPerformanceCpuSegment.Voice;
            if (name.StartsWith("BasisDriver.LocalPlayer.", StringComparison.Ordinal)) return BasisPerformanceCpuSegment.Movement;
            if (name.StartsWith("BasisDriver.LocoPose.", StringComparison.Ordinal)) return BasisPerformanceCpuSegment.Movement;
            if (name.StartsWith("BasisDriver.Avatar.", StringComparison.Ordinal)) return BasisPerformanceCpuSegment.AvatarLoad;
            if (name.StartsWith("BasisEerie.", StringComparison.Ordinal)) return BasisPerformanceCpuSegment.Ik;
            if (name.StartsWith("BasisDriver.", StringComparison.Ordinal)) return BasisPerformanceCpuSegment.EventDriver;
            return null;
        }

        private static ProfilerRecorder CreateCpuRecorder(ProfilerRecorderHandle handle)
        {
            try { return new ProfilerRecorder(handle, 1, MarkerOptions); }
            catch { return default; }
        }

        private static void DisposeCpuRecorders()
        {
            for (int i = 0; i < cpuRows.Count; i++)
            {
                if (cpuRows[i].Recorder.Valid) cpuRows[i].Recorder.Dispose();
            }
            cpuRows.Clear();
            knownMarkers.Clear();
        }
    }
}
