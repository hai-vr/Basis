using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Players;
using UnityEngine;

namespace HVR.Basis.Comms
{
    /// <summary>
    /// Registers the face- and eye-tracking diagnostic builders into the
    /// framework's Developer tab. The framework owns the toggles
    /// (DevDebugFaceTracking / DevDebugEyeTracking) and the collapsible group
    /// containers; this package fills them in with the live state of the HVR
    /// pipeline components.
    /// </summary>
    public static class SettingsProviderFaceTracking
    {
        [RuntimeInitializeOnLoadMethod]
        static void Register()
        {
            SettingsProvider.FaceTrackingDebugBuilder = BuildFaceTrackingSection;
            SettingsProvider.EyeTrackingDebugBuilder = BuildEyeTrackingSection;
        }

        static void BuildFaceTrackingSection(RectTransform parent)
        {
            PanelButton refreshButton = PanelButton.CreateNew(parent);
            refreshButton.Descriptor.SetTitle("Refresh");
            refreshButton.Descriptor.SetDescription("Poll the current face tracking state from all components.");

            PanelElementDescriptor fieldFTActive = CreateInfoField(parent, "Face Tracking Active", "...");
            PanelElementDescriptor fieldOSC = CreateInfoField(parent, "OSC Acquisition", "...");
            PanelElementDescriptor fieldBlendshapeActive = CreateInfoField(parent, "Blendshape Tracking", "...");
            PanelElementDescriptor fieldActuatedAddresses = CreateInfoField(parent, "Actuated Addresses", "...");

            void Refresh() => RefreshFaceState(fieldFTActive, fieldOSC, fieldBlendshapeActive, fieldActuatedAddresses);
            refreshButton.OnClicked += Refresh;
            Refresh();
        }

        static void BuildEyeTrackingSection(RectTransform parent)
        {
            PanelButton refreshButton = PanelButton.CreateNew(parent);
            refreshButton.Descriptor.SetTitle("Refresh");
            refreshButton.Descriptor.SetDescription("Poll the current eye tracking state from all components.");

            PanelElementDescriptor fieldEyeOverride = CreateInfoField(parent, "Eye Override", "...");
            PanelElementDescriptor fieldEyeDriverEnabled = CreateInfoField(parent, "Eye Driver Enabled", "...");
            PanelElementDescriptor fieldEyeParamsActive = CreateInfoField(parent, "Eye Params Active", "...");
            PanelElementDescriptor fieldEyeLeftX = CreateInfoField(parent, "Eye Left X", "...");
            PanelElementDescriptor fieldEyeRightX = CreateInfoField(parent, "Eye Right X", "...");
            PanelElementDescriptor fieldEyeY = CreateInfoField(parent, "Eye Y", "...");

            void Refresh() => RefreshEyeState(
                fieldEyeOverride, fieldEyeDriverEnabled, fieldEyeParamsActive,
                fieldEyeLeftX, fieldEyeRightX, fieldEyeY);
            refreshButton.OnClicked += Refresh;
            Refresh();
        }

        static void RefreshFaceState(
            PanelElementDescriptor ftActive,
            PanelElementDescriptor oscAcquisition,
            PanelElementDescriptor blendshapeActive,
            PanelElementDescriptor actuatedAddresses)
        {
            BasisLocalPlayer localPlayer = BasisLocalPlayer.Instance;
            var avatar = localPlayer != null ? localPlayer.BasisAvatar : null;

            FaceTrackingActivityRelay relay = avatar != null
                ? avatar.GetComponentInChildren<FaceTrackingActivityRelay>(true)
                : null;
            ftActive.SetDescription(relay != null ? relay.IsTrackingActive.ToString() : "No relay found");

            OSCAcquisition oscAcq = avatar != null
                ? avatar.GetComponentInChildren<OSCAcquisition>(true)
                : null;
            if (oscAcq != null)
                oscAcquisition.SetDescription(oscAcq.isActiveAndEnabled ? "Active" : "DISABLED");
            else
                oscAcquisition.SetDescription("No component");

            BlendshapeActuation blendshape = avatar != null
                ? avatar.GetComponentInChildren<BlendshapeActuation>(true)
                : null;
            if (blendshape != null)
            {
                blendshapeActive.SetDescription(blendshape.IsTrackingActive.ToString());
                int addressCount = blendshape.debugAddresses != null ? blendshape.debugAddresses.Length : 0;
                actuatedAddresses.SetDescription(addressCount.ToString());
            }
            else
            {
                blendshapeActive.SetDescription("No component");
                actuatedAddresses.SetDescription("--");
            }
        }

        static void RefreshEyeState(
            PanelElementDescriptor eyeOverride,
            PanelElementDescriptor eyeDriverEnabled,
            PanelElementDescriptor eyeParamsActive,
            PanelElementDescriptor eyeLeftX,
            PanelElementDescriptor eyeRightX,
            PanelElementDescriptor eyeY)
        {
            BasisLocalPlayer localPlayer = BasisLocalPlayer.Instance;
            var avatar = localPlayer != null ? localPlayer.BasisAvatar : null;

            eyeOverride.SetDescription(BasisLocalEyeDriver.Override.ToString());
            eyeDriverEnabled.SetDescription(BasisLocalEyeDriver.IsEnabled.ToString());

            EyeTrackingBoneActuation eyeActuation = avatar != null
                ? avatar.GetComponentInChildren<EyeTrackingBoneActuation>(true)
                : null;
            if (eyeActuation != null)
            {
                eyeParamsActive.SetDescription(eyeActuation.IsEyeTrackingParametersActive.ToString());
                eyeLeftX.SetDescription(eyeActuation._fEyeLeftX.ToString("F3"));
                eyeRightX.SetDescription(eyeActuation._fEyeRightX.ToString("F3"));
                eyeY.SetDescription(eyeActuation._fEyeY.ToString("F3"));
            }
            else
            {
                eyeParamsActive.SetDescription("No component");
                eyeLeftX.SetDescription("--");
                eyeRightX.SetDescription("--");
                eyeY.SetDescription("--");
            }
        }

        static PanelElementDescriptor CreateInfoField(RectTransform parent, string title, string initialValue)
        {
            PanelElementDescriptor field = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, parent);
            field.SetTitle(title);
            field.SetDescription(initialValue);
            return field;
        }
    }
}
