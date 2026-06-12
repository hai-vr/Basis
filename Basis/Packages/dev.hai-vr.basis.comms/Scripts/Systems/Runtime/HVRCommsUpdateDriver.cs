using System;
using System.Collections.Generic;
using HVR.Vixxy;
using Unity.Profiling;

namespace HVR.Basis.Comms
{
    /// <summary>
    /// Batches the per-frame work of the comms components into a single update pumped by
    /// BasisEventDriver, replacing one MonoBehaviour.Update() invocation per instance.
    /// Components register on enable and unregister on disable.
    /// </summary>
    public static class HVRCommsUpdateDriver
    {
        private static readonly List<HVRVixxyOrchestrator> Orchestrators = new();
        private static readonly List<FaceTrackingActivityRelay> ActivityRelays = new();
        private static readonly List<HVRVariableNetworking> VariableNetworkers = new();
        private static readonly List<EyeTrackingBoneActuation> EyeActuations = new();

        private static readonly ProfilerMarker VixxyMarker = new ProfilerMarker("HVRComms.VixxyOrchestrator");
        private static readonly ProfilerMarker ActivityMarker = new ProfilerMarker("HVRComms.FaceTrackingActivity");
        private static readonly ProfilerMarker VariableNetworkingMarker = new ProfilerMarker("HVRComms.VariableNetworking");
        private static readonly ProfilerMarker EyeActuationMarker = new ProfilerMarker("HVRComms.EyeTrackingBoneActuation");

        internal static void Register(HVRVixxyOrchestrator instance) => Orchestrators.Add(instance);
        internal static void Unregister(HVRVixxyOrchestrator instance) => Orchestrators.Remove(instance);

        internal static void Register(EyeTrackingBoneActuation instance) => EyeActuations.Add(instance);
        internal static void Unregister(EyeTrackingBoneActuation instance) => EyeActuations.Remove(instance);

        internal static void Register(FaceTrackingActivityRelay instance) => ActivityRelays.Add(instance);
        internal static void Unregister(FaceTrackingActivityRelay instance) => ActivityRelays.Remove(instance);

        internal static void Register(HVRVariableNetworking instance) => VariableNetworkers.Add(instance);
        internal static void Unregister(HVRVariableNetworking instance) => VariableNetworkers.Remove(instance);

        public static void SimulateAll()
        {
            using (EyeActuationMarker.Auto())
            {
                for (var i = 0; i < EyeActuations.Count; i++)
                {
                    try { EyeActuations[i].SimulateTick(); }
                    catch (Exception ex) { BasisDebug.LogError($"EyeTrackingBoneActuation update failed: {ex}"); }
                }
            }

            using (VixxyMarker.Auto())
            {
                for (var i = 0; i < Orchestrators.Count; i++)
                {
                    try { Orchestrators[i].SimulateTick(); }
                    catch (Exception ex) { BasisDebug.LogError($"HVRVixxyOrchestrator update failed: {ex}"); }
                }
            }

            using (ActivityMarker.Auto())
            {
                for (var i = 0; i < ActivityRelays.Count; i++)
                {
                    try { ActivityRelays[i].SimulateTick(); }
                    catch (Exception ex) { BasisDebug.LogError($"FaceTrackingActivityRelay update failed: {ex}"); }
                }
            }

            using (VariableNetworkingMarker.Auto())
            {
                for (var i = 0; i < VariableNetworkers.Count; i++)
                {
                    try { VariableNetworkers[i].SimulateTick(); }
                    catch (Exception ex) { BasisDebug.LogError($"HVRVariableNetworking update failed: {ex}"); }
                }
            }
        }
    }
}
