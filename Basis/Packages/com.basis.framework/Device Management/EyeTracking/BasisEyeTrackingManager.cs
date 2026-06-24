using System.Collections.Generic;
using Basis.Scripts.Drivers;
using Unity.Mathematics;

namespace Basis.Scripts.Device_Management.EyeTracking
{
    /// <summary>
    /// Single arbitration point for eye tracking. Registered providers (HMD gaze,
    /// OSC face tracking) are merged each frame into <see cref="Current"/> per the
    /// Developer source-order preference, and the legacy BasisLocalCameraDriver gaze
    /// statics are kept in sync for VRS. Pumped from BasisEventDriver's central tick.
    /// </summary>
    public static class BasisEyeTrackingManager
    {
        private const float GazeConvergenceDistance = 10f;

        private static readonly List<IBasisEyeTrackingProvider> _providers = new List<IBasisEyeTrackingProvider>();

        public static BasisEyeTrackingData Current;

        public static BasisEyeSource PreferredGazeSource = BasisEyeSource.Osc;

        public static void Register(IBasisEyeTrackingProvider provider)
        {
            if (provider == null || _providers.Contains(provider))
            {
                return;
            }
            _providers.Add(provider);
        }

        public static void Unregister(IBasisEyeTrackingProvider provider)
        {
            _providers.Remove(provider);
        }

        public static void Simulate()
        {
            BasisEyeTrackingData hmdData = default;
            BasisEyeTrackingData oscData = default;
            bool hmdHas = false;
            bool oscHas = false;

            for (int i = 0; i < _providers.Count; i++)
            {
                IBasisEyeTrackingProvider provider = _providers[i];
                if (provider == null || !provider.IsActive)
                {
                    continue;
                }

                if (provider.Source == BasisEyeSource.Hmd)
                {
                    if (!hmdHas)
                    {
                        BasisEyeTrackingData d = default;
                        hmdHas = provider.TryGetEyeData(ref d);
                        hmdData = d;
                    }
                }
                else if (!oscHas)
                {
                    BasisEyeTrackingData d = default;
                    oscHas = provider.TryGetEyeData(ref d);
                    oscData = d;
                }
            }

            bool hmdGaze = hmdHas && (hmdData.HasWorldRay || hmdData.HasPerEyeAngles);
            bool oscGaze = oscHas && (oscData.HasWorldRay || oscData.HasPerEyeAngles);

            BasisEyeTrackingData merged = default;

            bool preferOsc = PreferredGazeSource == BasisEyeSource.Osc;
            bool useOscGaze = oscGaze && (preferOsc || !hmdGaze);
            bool useHmdGaze = !useOscGaze && hmdGaze;

            if (useOscGaze)
            {
                CopyGaze(ref merged, oscData);
            }
            else if (useHmdGaze)
            {
                CopyGaze(ref merged, hmdData);
            }

            if (oscHas && oscData.HasOpenness)
            {
                merged.LeftOpenness = oscData.LeftOpenness;
                merged.RightOpenness = oscData.RightOpenness;
                merged.HasOpenness = true;
            }

            DeriveMissingGaze(ref merged);

            Current = merged;
            PublishLegacyGaze(merged);
        }

        private static void CopyGaze(ref BasisEyeTrackingData merged, in BasisEyeTrackingData src)
        {
            merged.GazeOrigin = src.GazeOrigin;
            merged.GazeDirection = src.GazeDirection;
            merged.HasWorldRay = src.HasWorldRay;
            merged.LeftAngles = src.LeftAngles;
            merged.RightAngles = src.RightAngles;
            merged.HasPerEyeAngles = src.HasPerEyeAngles;
            merged.LeftEyePosition = src.LeftEyePosition;
            merged.RightEyePosition = src.RightEyePosition;
            merged.HasEyePositions = src.HasEyePositions;
        }

        private static void DeriveMissingGaze(ref BasisEyeTrackingData data)
        {
            bool hasAnyGaze = data.HasWorldRay || data.HasPerEyeAngles;
            if (!hasAnyGaze)
            {
                return;
            }

            float3 leftCenter = BasisLocalCameraDriver.LeftEyePosition();
            float3 rightCenter = BasisLocalCameraDriver.RightEyePosition();
            quaternion headRot = BasisLocalCameraDriver.HeadRotation;

            if (data.HasWorldRay && !data.HasPerEyeAngles)
            {
                BasisEyeMath.WorldRayToPerEyeAngles(
                    data.GazeOrigin, data.GazeDirection, GazeConvergenceDistance,
                    leftCenter, rightCenter, headRot,
                    out data.LeftAngles, out data.RightAngles);
                data.HasPerEyeAngles = true;
            }
            else if (data.HasPerEyeAngles && !data.HasWorldRay)
            {
                BasisEyeMath.PerEyeAnglesToWorldRay(
                    data.LeftAngles, data.RightAngles,
                    leftCenter, rightCenter, headRot,
                    out float3 origin, out float3 dir);
                data.GazeOrigin = origin;
                data.GazeDirection = dir;
                data.HasWorldRay = true;
            }
        }

        private static void PublishLegacyGaze(in BasisEyeTrackingData data)
        {
            if (data.HasWorldRay)
            {
                BasisLocalCameraDriver.GazeOrigin = data.GazeOrigin;
                BasisLocalCameraDriver.GazeDirection = data.GazeDirection;
                BasisLocalCameraDriver.HasEyeGaze = true;
            }
            else
            {
                BasisLocalCameraDriver.HasEyeGaze = false;
            }
        }
    }
}
