using Basis.BasisUI;
using UnityEngine;

namespace Basis.Scripts.Device_Management.EyeTracking
{
    public static class BasisEyeTrackingSettings
    {
        [RuntimeInitializeOnLoadMethod]
        private static void Initialize()
        {
            Apply(BasisSettingsDefaults.EyeTrackingPreferOsc.RawValue);
            BasisSettingsDefaults.EyeTrackingPreferOsc.OnChanged -= Apply;
            BasisSettingsDefaults.EyeTrackingPreferOsc.OnChanged += Apply;
        }

        private static void Apply(bool preferOsc)
        {
            BasisEyeTrackingManager.PreferredGazeSource = preferOsc ? BasisEyeSource.Osc : BasisEyeSource.Hmd;
        }
    }
}
