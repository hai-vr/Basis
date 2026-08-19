#if BASIS_FRAMEWORK_EXISTS
using System.Collections.Generic;
using System.Text;
using Basis.BasisUI;
using Basis.Scripts.Device_Management;
using UnityEngine;

namespace Basis.Integration.MetaBodyTracking
{
    /// <summary>
    /// Adds the headset body tracking controls (source mode, which body parts, fidelity, height)
    /// as a section inside the framework's Tracker Settings tab via
    /// SettingsProvider.TrackerSettingsExtraBuilder.
    /// </summary>
    public static class SettingsProviderMetaBodyTracking
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
            sectionToggle.SetTitle(BasisLocalization.Get("settings.metabody.title"));
            int sectionStart = parent.childCount;

            PanelElementDescriptor group = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, parent);
            group.SetDescription(BasisLocalization.Get("settings.metabody.title.description"));
            var content = group.ContentParent;

            PanelDropdown sourceDropdown = PanelDropdown.CreateNewEntry(content);
            sourceDropdown.Descriptor.SetTitle(BasisLocalization.Get("settings.metabody.trackerSource"));
            sourceDropdown.Descriptor.SetDescription(BasisLocalization.Get("settings.metabody.trackerSource.description"));
            sourceDropdown.AssignEntries(
                new List<string>
                {
                    BasisMetaBodyTrackingSettings.TrackerSourceOff,
                    BasisMetaBodyTrackingSettings.TrackerSourceAuto,
                    BasisMetaBodyTrackingSettings.TrackerSourceForce,
                },
                new List<string>
                {
                    BasisLocalization.Get("settings.metabody.trackerSource.off"),
                    BasisLocalization.Get("settings.metabody.trackerSource.auto"),
                    BasisLocalization.Get("settings.metabody.trackerSource.force"),
                });
            sourceDropdown.SetValueWithoutNotify(BasisMetaBodyTrackingSettings.TrackerSource.RawValue);
            sourceDropdown.OnValueChanged += value => BasisMetaBodyTrackingSettings.TrackerSource.SetValue(value);

            PanelToggle upperBodyToggle = PanelToggle.CreateNewEntry(content);
            upperBodyToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.metabody.upperBody"));
            upperBodyToggle.Descriptor.SetDescription(BasisLocalization.Get("settings.metabody.upperBody.description"));
            upperBodyToggle.SetValueWithoutNotify(BasisMetaBodyTrackingSettings.TrackUpperBody.RawValue);
            upperBodyToggle.OnValueChanged += value => BasisMetaBodyTrackingSettings.TrackUpperBody.SetValue(value);

            PanelToggle legsToggle = PanelToggle.CreateNewEntry(content);
            legsToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.metabody.legs"));
            legsToggle.Descriptor.SetDescription(BasisLocalization.Get("settings.metabody.legs.description"));
            legsToggle.SetValueWithoutNotify(BasisMetaBodyTrackingSettings.TrackLegs.RawValue);
            legsToggle.OnValueChanged += value => BasisMetaBodyTrackingSettings.TrackLegs.SetValue(value);

            PanelToggle autoBindToggle = PanelToggle.CreateNewEntry(content);
            autoBindToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.metabody.autoBind"));
            autoBindToggle.Descriptor.SetDescription(BasisLocalization.Get("settings.metabody.autoBind.description"));
            autoBindToggle.SetValueWithoutNotify(BasisMetaBodyTrackingSettings.AutoBindTrackers.RawValue);
            autoBindToggle.OnValueChanged += value => BasisMetaBodyTrackingSettings.AutoBindTrackers.SetValue(value);

            PanelToggle fidelityToggle = PanelToggle.CreateNewEntry(content);
            fidelityToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.metabody.highFidelity"));
            fidelityToggle.Descriptor.SetDescription(BasisLocalization.Get("settings.metabody.highFidelity.description"));
            fidelityToggle.SetValueWithoutNotify(BasisMetaBodyTrackingSettings.HighFidelity.RawValue);
            fidelityToggle.OnValueChanged += value =>
            {
                BasisMetaBodyTrackingSettings.HighFidelity.SetValue(value);
                BasisMetaBodyTrackingFeature.ApplyFidelity();
            };

            PanelToggle heightToggle = PanelToggle.CreateNewEntry(content);
            heightToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.metabody.applyHeight"));
            heightToggle.Descriptor.SetDescription(BasisLocalization.Get("settings.metabody.applyHeight.description"));
            heightToggle.SetValueWithoutNotify(BasisMetaBodyTrackingSettings.ApplyPlayerHeight.RawValue);
            heightToggle.OnValueChanged += value =>
            {
                BasisMetaBodyTrackingSettings.ApplyPlayerHeight.SetValue(value);
                BasisMetaBodyTrackingFeature.ApplyHeightOverride();
            };

            PanelElementDescriptor statusField = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, content);
            statusField.SetTitle(BasisLocalization.Get("settings.metabody.status"));
            statusField.SetDescription(DescribeStatus());

            PanelButton refresh = PanelButton.CreateNew(content);
            refresh.Descriptor.SetTitle(BasisLocalization.Get("settings.metabody.refresh"));
            refresh.Descriptor.SetDescription(BasisLocalization.Get("settings.metabody.refresh.description"));
            refresh.OnClicked += () =>
            {
                BasisMetaBodyTrackingFeature.ApplyFidelity();
                BasisMetaBodyTrackingFeature.ApplyHeightOverride();
                statusField.SetDescription(DescribeStatus());
            };

            PanelSectionToggleHelpers.FinalizeFlatSectionFromIndex(sectionToggle, parent, sectionStart, false, _ =>
            {
                tabDescriptor?.ForceRebuild();
            });
        }

        private static string DescribeStatus()
        {
            if (!BasisMetaBodyTrackingFeature.IsSupported)
            {
                return "This runtime does not offer OpenXR body tracking.";
            }

            var text = new StringBuilder();
            switch (BasisMetaBodyTrackingFeature.ActiveJointSet)
            {
                case BasisMetaBodyJointSet.FullBody:
                    text.Append("Tracking upper body and legs.");
                    break;
                case BasisMetaBodyJointSet.UpperBody:
                    text.Append("Tracking upper body only (this headset solves no legs).");
                    break;
                default:
                    text.Append("Body tracker not running.");
                    break;
            }

            if (!BasisMetaBodyTrackerSource.WantsPoseFeed())
            {
                text.Append(" Turned off in settings.");
                return text.ToString();
            }

            if (BasisMetaBodyTrackingFeature.LastLocateResult != 0)
            {
                text.Append($" Locating joints failed with {BasisMetaBodyTrackingFeature.LastLocateResult}.");
            }
            else
            {
                text.Append(BasisMetaBodyTrackingFeature.IsBodyActive
                    ? $" Body visible, confidence {BasisMetaBodyTrackingFeature.BodyConfidence:P0}."
                    : " No body pose right now.");
            }

            if (BasisMetaBodyTrackerSource.IsSourcing)
            {
                text.Append($" {BasisMetaBodyTrackerSource.SourcedCount} trackers driven from the headset.");
            }

            if (!BasisMetaBodyTrackingFeature.SupportsFidelity)
            {
                text.Append(" No fidelity control on this runtime.");
            }
            if (!BasisMetaBodyTrackingFeature.SupportsCalibration)
            {
                text.Append(" No height override on this runtime.");
            }
            return text.ToString();
        }
    }
}
#endif
