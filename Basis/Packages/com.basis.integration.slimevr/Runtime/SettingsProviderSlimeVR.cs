#if BASIS_FRAMEWORK_EXISTS
using System.Collections.Generic;
using System.Text;
using Basis.BasisUI;
using Basis.Scripts.Device_Management;
using UnityEngine;

namespace Basis.Integration.SlimeVR
{
    /// <summary>
    /// Adds the SlimeVR controls (enable, auto-apply body measurements, status, resets) as a
    /// section inside the framework's Tracker Settings tab via
    /// SettingsProvider.TrackerSettingsExtraBuilder.
    /// </summary>
    public static class SettingsProviderSlimeVR
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            // Deferred one frame: other packages assign TrackerSettingsExtraBuilder with `=`
            // during RuntimeInitializeOnLoadMethod, which would drop a section subscribed at the
            // same time. The main-thread queue drains after every load method has run, so a `+=`
            // from here always composes.
            BasisDeviceManagement.mainThreadActions.Enqueue(() =>
            {
                SettingsProvider.TrackerSettingsExtraBuilder += BuildSection;
            });
        }

        private static void BuildSection(RectTransform parent)
        {
            PanelElementDescriptor tabDescriptor = parent.GetComponentInParent<PanelElementDescriptor>(true);

            PanelSectionToggle sectionToggle = PanelSectionToggle.CreateNewEntry(parent);
            sectionToggle.SetTitle("SlimeVR");
            int sectionStart = parent.childCount;

            PanelElementDescriptor group = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, parent);
            group.SetDescription("Pulls your real body proportions from a running SlimeVR server, so your height and arm span are set automatically without calibrating.");
            var content = group.ContentParent;

            PanelToggle enableToggle = PanelToggle.CreateNewEntry(content);
            enableToggle.Descriptor.SetTitle("Connect To SlimeVR");
            enableToggle.Descriptor.SetDescription("Look for a local SlimeVR server and stay connected to it.");
            enableToggle.SetValueWithoutNotify(BasisSlimeVRSettings.Enable.RawValue);
            enableToggle.OnValueChanged += value => BasisSlimeVRSettings.Enable.SetValue(value);

            PanelDropdown transportDropdown = PanelDropdown.CreateNewEntry(content);
            transportDropdown.Descriptor.SetTitle("Connection Method");
            transportDropdown.Descriptor.SetDescription("WebSocket works with every SlimeVR server today. Pipe is SlimeVR's newer native connection and needs a server build whose pipe works.");
            transportDropdown.AssignEntries(
                new List<string> { BasisSlimeVRSettings.TransportWebSocket, BasisSlimeVRSettings.TransportPipe },
                new List<string> { "WebSocket", "Pipe" });
            transportDropdown.SetValueWithoutNotify(BasisSlimeVRSettings.Transport.RawValue);
            transportDropdown.OnValueChanged += value => BasisSlimeVRSettings.Transport.SetValue(value);

            PanelToggle applyToggle = PanelToggle.CreateNewEntry(content);
            applyToggle.Descriptor.SetTitle("Auto Apply Body Measurements");
            applyToggle.Descriptor.SetDescription("Use SlimeVR's measured eye height and arm span as your calibrated body size.");
            applyToggle.SetValueWithoutNotify(BasisSlimeVRSettings.ApplyBodyMeasurements.RawValue);
            applyToggle.OnValueChanged += value => BasisSlimeVRSettings.ApplyBodyMeasurements.SetValue(value);

            PanelToggle autoBindToggle = PanelToggle.CreateNewEntry(content);
            autoBindToggle.Descriptor.SetTitle("Auto Bind SlimeVR Trackers");
            autoBindToggle.Descriptor.SetDescription("SlimeVR trackers announce which body part they are and bind automatically. Turn off to calibrate them by hand instead.");
            autoBindToggle.SetValueWithoutNotify(BasisSlimeVRSettings.AutoBindSlimeVRTrackers.RawValue);
            autoBindToggle.OnValueChanged += value => BasisSlimeVRSettings.AutoBindSlimeVRTrackers.SetValue(value);

            PanelButton yawReset = PanelButton.CreateNew(content);
            yawReset.Descriptor.SetTitle("Yaw Reset");
            yawReset.Descriptor.SetDescription("Straighten the SlimeVR trackers (same as the SlimeVR yaw reset).");
            yawReset.OnClicked += BasisSlimeVRBridge.TriggerYawReset;

            PanelButton fullReset = PanelButton.CreateNew(content);
            fullReset.Descriptor.SetTitle("Full Reset");
            fullReset.Descriptor.SetDescription("Full SlimeVR tracker reset (stand straight while using it).");
            fullReset.OnClicked += BasisSlimeVRBridge.TriggerFullReset;

            PanelElementDescriptor statusField = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, content);
            statusField.SetTitle("Status");
            statusField.SetDescription(DescribeStatus());

            PanelButton refresh = PanelButton.CreateNew(content);
            refresh.Descriptor.SetTitle("Refresh");
            refresh.Descriptor.SetDescription("Re-read the body measurements and status from SlimeVR.");
            refresh.OnClicked += () =>
            {
                BasisSlimeVRBridge.RefreshBodyMeasurements();
                statusField.SetDescription(DescribeStatus());
            };

            void RefreshStatusText()
            {
                if (statusField == null)
                {
                    BasisSlimeVRBridge.OnConnectionChanged -= OnConnection;
                    BasisSlimeVRBridge.OnBodyMetricsChanged -= OnMetrics;
                    return;
                }
                statusField.SetDescription(DescribeStatus());
            }

            void OnConnection(bool _) => RefreshStatusText();
            void OnMetrics(BasisSlimeVRBodyMetrics _) => RefreshStatusText();

            BasisSlimeVRBridge.OnConnectionChanged += OnConnection;
            BasisSlimeVRBridge.OnBodyMetricsChanged += OnMetrics;

            PanelSectionToggleHelpers.FinalizeFlatSectionFromIndex(sectionToggle, parent, sectionStart, false, _ =>
            {
                tabDescriptor?.ForceRebuild();
            });
        }

        private static string DescribeStatus()
        {
            if (!BasisSlimeVRSettings.Enable.RawValue)
            {
                return "Disabled.";
            }
            if (!BasisSlimeVRBridge.IsConnected)
            {
                return "Looking for a SlimeVR server...";
            }

            var text = new StringBuilder("Connected.");
            if (BasisSlimeVRBridge.HasBodyMetrics)
            {
                var metrics = BasisSlimeVRBridge.LastBodyMetrics;
                text.Append($" Eye height {metrics.EyeHeightMeters:F2}m, full height {metrics.FullHeightMeters:F2}m, arm span {metrics.ControllerSpanMeters:F2}m.");
            }

            int physical = 0;
            float lowestBattery = float.MaxValue;
            foreach (var tracker in BasisSlimeVRBridge.Trackers)
            {
                if (tracker.IsSynthetic)
                {
                    continue;
                }
                physical++;
                if (tracker.HasBattery && tracker.BatteryPercent < lowestBattery)
                {
                    lowestBattery = tracker.BatteryPercent;
                }
            }
            if (physical > 0)
            {
                text.Append($" {physical} trackers");
                if (lowestBattery < float.MaxValue)
                {
                    text.Append($", lowest battery {lowestBattery:F0}%");
                }
                text.Append('.');
            }
            return text.ToString();
        }
    }
}
#endif
