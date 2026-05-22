using System.Collections.Generic;
using System.Linq;
using Basis.BasisUI;
using UnityEngine;

namespace Basis.MediaPipe
{
    /// <summary>
    /// Adds the webcam tracking controls (enable, camera selection, per-feature toggles, calibrate)
    /// as a section inside the framework's Tracker Settings tab via
    /// SettingsProvider.TrackerSettingsExtraBuilder.
    /// </summary>
    public static class SettingsProviderMediaPipe
    {
        [RuntimeInitializeOnLoadMethod]
        private static void Register()
        {
            SettingsProvider.TrackerSettingsExtraBuilder = BuildSection;
        }

        private static void BuildSection(RectTransform parent)
        {
            PanelElementDescriptor group = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, parent);
            group.SetTitle("Webcam Tracking");
            group.SetDescription("Drive your avatar's face, eyes, fingers and hands from a webcam (MediaPipe). Requires the MediaPipe Unity Plugin and its models (see package README).");
            var content = group.ContentParent;

            PanelToggle enableToggle = PanelToggle.CreateNewEntry(content);
            enableToggle.Descriptor.SetTitle("Enable Webcam Tracking");
            enableToggle.Descriptor.SetDescription("Turn webcam tracking on or off.");
            enableToggle.SetValueWithoutNotify(BasisMediaPipeSettings.Enable.RawValue);
            enableToggle.OnValueChanged += value =>
            {
                BasisMediaPipeSettings.Enable.SetValue(value);
                BasisMediaPipeManagement.GetOrCreate()?.SetEnabled(value);
            };

            PanelDropdown cameraDropdown = PanelDropdown.CreateNewEntry(content);
            cameraDropdown.Descriptor.SetTitle("Camera");
            cameraDropdown.Descriptor.SetDescription("Select which webcam to use.");
            List<string> deviceNames = BasisMediaPipeCamera.EnumerateDevices().Select(d => d.name).ToList();
            if (deviceNames.Count == 0) deviceNames.Add("(no cameras found)");
            cameraDropdown.AssignEntries(deviceNames);
            string currentCamera = BasisMediaPipeSettings.Camera.RawValue;
            if (!string.IsNullOrEmpty(currentCamera) && deviceNames.Contains(currentCamera))
            {
                cameraDropdown.SetValueWithoutNotify(currentCamera);
            }
            cameraDropdown.OnValueChanged += choice =>
            {
                BasisMediaPipeSettings.Camera.SetValue(choice);
                BasisMediaPipeManagement.GetOrCreate()?.SetCamera(choice);
            };

            void AddFeatureToggle(string title, string description, BasisSettingsBinding<bool> binding)
            {
                PanelToggle toggle = PanelToggle.CreateNewEntry(content);
                toggle.Descriptor.SetTitle(title);
                toggle.Descriptor.SetDescription(description);
                toggle.SetValueWithoutNotify(binding.RawValue);
                toggle.OnValueChanged += value =>
                {
                    binding.SetValue(value);
                    BasisMediaPipeManagement.GetOrCreate()?.ApplySettings();
                };
            }

            void AddTuningToggle(string title, string description, BasisSettingsBinding<bool> binding)
            {
                PanelToggle toggle = PanelToggle.CreateNewEntry(content);
                toggle.Descriptor.SetTitle(title);
                toggle.Descriptor.SetDescription(description);
                toggle.SetValueWithoutNotify(binding.RawValue);
                toggle.OnValueChanged += value =>
                {
                    binding.SetValue(value);
                    BasisMediaPipeManagement.GetOrCreate()?.ApplyTuning();
                };
            }

            AddFeatureToggle("Face & Eyes", "Track facial expressions, blink and gaze.", BasisMediaPipeSettings.EnableFace);
            AddFeatureToggle("Hands & Fingers", "Track finger curl and splay.", BasisMediaPipeSettings.EnableHands);
            AddFeatureToggle("Head Tracking", "Your avatar's head follows your real head. The camera stays on the mouse.", BasisMediaPipeSettings.EnableHead);
            AddFeatureToggle("Hand Position (experimental)", "Move your avatar's hands to match your real hands (in addition to finger curl).", BasisMediaPipeSettings.EnableHandTracking);
            AddTuningToggle("Hand Rotation", "Off keeps a neutral wrist (position only) to avoid noisy webcam wrist rotation.", BasisMediaPipeSettings.HandRotation);
            AddFeatureToggle("Mirror Camera", "Flip the camera horizontally (selfie view).", BasisMediaPipeSettings.Mirror);

            AddFeatureToggle("Swap Hands", "Fix left/right hands if they are reversed.", BasisMediaPipeSettings.SwapHands);
            AddTuningToggle("Invert Blink", "Fix blink if the eyes close when you open them.", BasisMediaPipeSettings.InvertBlink);
            AddTuningToggle("Invert Head Yaw", "Fix the head turn (left/right) direction.", BasisMediaPipeSettings.InvertHeadYaw);
            AddTuningToggle("Invert Head Pitch", "Fix the head nod (up/down) direction.", BasisMediaPipeSettings.InvertHeadPitch);

            void AddSmoothingSlider(string title, BasisSettingsBinding<float> binding)
            {
                PanelSlider slider = PanelSlider.CreateNew(content);
                slider.SetSliderSettings(new PanelSlider.SliderSettings { SliderMin = 0f, SliderMax = 1f, DecimalPlaces = 2, DisplayMode = ValueDisplayMode.Percentage });
                slider.Descriptor.SetTitle(title);
                slider.Descriptor.SetDescription("Higher = smoother but more latency.");
                slider.SetValueWithoutNotify(binding.RawValue);
                slider.OnValueChanged += value =>
                {
                    binding.SetValue(value);
                    BasisMediaPipeManagement.GetOrCreate()?.ApplyTuning();
                };
            }

            AddSmoothingSlider("Head Smoothing", BasisMediaPipeSettings.HeadSmoothing);
            AddSmoothingSlider("Face Smoothing", BasisMediaPipeSettings.FaceSmoothing);
            AddSmoothingSlider("Hand Smoothing", BasisMediaPipeSettings.HandSmoothing);
            AddSmoothingSlider("Finger Smoothing", BasisMediaPipeSettings.FingerSmoothing);

            PanelSlider headPosition = PanelSlider.CreateNew(content);
            headPosition.SetSliderSettings(new PanelSlider.SliderSettings { SliderMin = 0f, SliderMax = 3f, DecimalPlaces = 2, DisplayMode = ValueDisplayMode.Percentage });
            headPosition.Descriptor.SetTitle("Head Position Strength");
            headPosition.Descriptor.SetDescription("How much your head movement shifts the avatar's head position.");
            headPosition.SetValueWithoutNotify(BasisMediaPipeSettings.HeadPositionStrength.RawValue);
            headPosition.OnValueChanged += value =>
            {
                BasisMediaPipeSettings.HeadPositionStrength.SetValue(value);
                BasisMediaPipeManagement.GetOrCreate()?.ApplyTuning();
            };

            PanelButton calibrate = PanelButton.CreateNew(content);
            calibrate.Descriptor.SetTitle("Calibrate Head (look forward)");
            calibrate.Descriptor.SetDescription("Face the screen straight on, then click to set your neutral head pose.");
            calibrate.OnClicked += () => BasisMediaPipeManagement.GetOrCreate()?.CalibrateHead();
        }
    }
}
