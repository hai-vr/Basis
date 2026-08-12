using UnityEngine;

namespace Basis.BasisUI.HandHeldCamera
{
    /// <summary>
    /// The GIF section on the Image tab: record a short clip of the camera feed straight to a
    /// shareable animated GIF. The controls write through the camera's clamped setters, and the
    /// tick keeps the button and status honest about a recording the panel did not start — the
    /// state lives on the camera, which keeps recording with the panel closed.
    /// </summary>
    public partial class BasisHandHeldCameraPanelProvider
    {
        private PanelSectionToggle _gifSection;
        private PanelElementDescriptor _gifGroup;
        private PanelButton _gifRecordButton;
        private PanelElementDescriptor _gifStatus;
        private PanelSlider _gifDurationSlider;
        private PanelSlider _gifFrameRateSlider;
        private PanelDropdown _gifSizeDropdown;
        private PanelToggle _gifLoopToggle;
        private PanelToggle _gifDitherToggle;

        private string _lastGifButtonLabel;
        private string _lastGifStatusText;
        private float _lastGifDuration = float.NaN;
        private float _lastGifFrameRate = float.NaN;
        private int _lastGifWidth = -1;
        private bool? _lastGifLoop;
        private bool? _lastGifDither;

        private void BuildGifGroup(RectTransform parent)
        {
            _gifSection = PanelSectionToggle.CreateNewEntry(parent);
            _gifGroup = PanelSectionToggleHelpers.CreateCollapsibleContentGroup(
                _gifSection, parent, BasisLocalization.Get("camera.gif"), false);
            RectTransform content = _gifGroup.ContentParent;

            RectTransform recordRow = PanelElementDescriptor.BuildActionRow(content, "CameraGifRecordRow");
            _gifRecordButton = PanelButton.CreateNew(recordRow);
            _gifRecordButton.Descriptor.SetTitle(BasisLocalization.Get("camera.gif.record"));
            _gifRecordButton.OnClicked += OnGifRecordClicked;

            _gifStatus = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, content);
            _gifStatus.SetTitle(BasisLocalization.Get("camera.gif.status"));
            _gifStatus.SetDescription(BasisLocalization.Get("camera.gif.status.idle"));
            if (_gifStatus.IconBackground != null) _gifStatus.IconBackground.SetActive(false);
            ReleaseControlSlot(_gifStatus);

            BasisHandHeldCameraUI.CameraSettings defaults = new BasisHandHeldCameraUI.CameraSettings();

            _gifDurationSlider = PanelSlider.CreateNew(content);
            _gifDurationSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.gif.length"),
                BasisHandHeldCamera.MinGifDurationSeconds, BasisHandHeldCamera.MaxGifDurationSeconds,
                true, 0, ValueDisplayMode.Raw));
            _gifDurationSlider.Descriptor.SetDescription(BasisLocalization.Get("camera.gif.length.description"));
            _gifDurationSlider.SetResetDefault(defaults.gifDurationSeconds);
            _gifDurationSlider.OnValueChanged = v => _activeCamera?.SetGifDuration(v);

            _gifFrameRateSlider = PanelSlider.CreateNew(content);
            _gifFrameRateSlider.SetSliderSettings(PanelSlider.SliderSettings.Advanced(
                BasisLocalization.Get("camera.gif.frameRate"),
                BasisHandHeldCamera.MinGifFrameRate, BasisHandHeldCamera.MaxGifFrameRate,
                true, 0, ValueDisplayMode.Hz));
            _gifFrameRateSlider.Descriptor.SetDescription(BasisLocalization.Get("camera.gif.frameRate.description"));
            _gifFrameRateSlider.SetResetDefault(defaults.gifFrameRate);
            _gifFrameRateSlider.OnValueChanged = v => _activeCamera?.SetGifFrameRate((int)v);

            _gifSizeDropdown = PanelDropdown.CreateNewEntry(content);
            _gifSizeDropdown.Descriptor.SetTitle(BasisLocalization.Get("camera.gif.size"));
            _gifSizeDropdown.Descriptor.SetDescription(BasisLocalization.Get("camera.gif.size.description"));
            _gifSizeDropdown.AssignEntries(BuildGifSizeLabels());
            _gifSizeDropdown.OnValueChanged = _ =>
            {
                if (_activeCamera == null || _gifSizeDropdown == null) return;
                int index = _gifSizeDropdown.Index;
                if (index >= 0 && index < BasisHandHeldCamera.GifWidthPresets.Length)
                {
                    _activeCamera.SetGifWidth(BasisHandHeldCamera.GifWidthPresets[index]);
                }
            };

            _gifLoopToggle = PanelToggle.CreateNewEntry(content);
            _gifLoopToggle.Descriptor.SetTitle(BasisLocalization.Get("camera.gif.loop"));
            _gifLoopToggle.Descriptor.SetDescription(BasisLocalization.Get("camera.gif.loop.description"));
            _gifLoopToggle.OnValueChanged = v =>
            {
                if (_activeCamera != null) _activeCamera.GifLoop = v;
            };

            _gifDitherToggle = PanelToggle.CreateNewEntry(content);
            _gifDitherToggle.Descriptor.SetTitle(BasisLocalization.Get("camera.gif.dither"));
            _gifDitherToggle.Descriptor.SetDescription(BasisLocalization.Get("camera.gif.dither.description"));
            _gifDitherToggle.OnValueChanged = v =>
            {
                if (_activeCamera != null) _activeCamera.GifDither = v;
            };

            if (BasisHandHeldCamera.CanOpenPhotosFolder)
            {
                RectTransform folderRow = PanelElementDescriptor.BuildActionRow(content, "CameraGifFolderRow");
                PanelButton openFolderButton = PanelButton.CreateNew(folderRow);
                openFolderButton.Descriptor.SetTitle(BasisLocalization.Get("camera.openPhotosFolder"));
                openFolderButton.OnClicked += () => BasisHandHeldCamera.OpenPhotosFolder();
            }
        }

        private static System.Collections.Generic.List<string> BuildGifSizeLabels()
        {
            var labels = new System.Collections.Generic.List<string>(BasisHandHeldCamera.GifWidthPresets.Length);
            for (int Index = 0; Index < BasisHandHeldCamera.GifWidthPresets.Length; Index++)
            {
                labels.Add($"{BasisHandHeldCamera.GifWidthPresets[Index]} px");
            }
            return labels;
        }

        private void OnGifRecordClicked()
        {
            if (_activeCamera == null) return;

            if (_activeCamera.GifState == BasisHandHeldCamera.BasisGifRecorderState.Recording)
            {
                _activeCamera.StopGifRecording();
            }
            else if (_activeCamera.GifState == BasisHandHeldCamera.BasisGifRecorderState.Idle)
            {
                _activeCamera.StartGifRecording();
            }

            // The click moved the state out from under the caches; repaint on the next tick.
            _lastGifButtonLabel = null;
            _lastGifStatusText = null;
            TickGifSection();
        }

        /// <summary>Seeds the GIF controls from a camera the panel just bound.</summary>
        private void SeedGifControls()
        {
            if (_activeCamera == null) return;

            _gifDurationSlider?.SetValueWithoutNotify(_activeCamera.GifDurationSeconds);
            _gifFrameRateSlider?.SetValueWithoutNotify(_activeCamera.GifFrameRate);
            _lastGifDuration = _activeCamera.GifDurationSeconds;
            _lastGifFrameRate = _activeCamera.GifFrameRate;
            SyncToggle(_gifLoopToggle, _activeCamera.GifLoop, ref _lastGifLoop);
            SyncToggle(_gifDitherToggle, _activeCamera.GifDither, ref _lastGifDither);
            _lastGifWidth = -1;
            SyncGifSizeDropdown();

            _lastGifButtonLabel = null;
            _lastGifStatusText = null;
            TickGifSection();
        }

        /// <summary>
        /// Per-tick sync. A recording carries on with the panel closed or on another tab, and a
        /// mode apply can move the sliders from underneath — so everything here follows the
        /// camera, edge-gated so an unchanged value never restarts a widget's tweens.
        /// </summary>
        private void TickGifSection()
        {
            if (_activeCamera == null || _gifRecordButton == null) return;

            SyncSlider(_gifDurationSlider, _activeCamera.GifDurationSeconds, ref _lastGifDuration);
            SyncSlider(_gifFrameRateSlider, _activeCamera.GifFrameRate, ref _lastGifFrameRate);
            SyncToggle(_gifLoopToggle, _activeCamera.GifLoop, ref _lastGifLoop);
            SyncToggle(_gifDitherToggle, _activeCamera.GifDither, ref _lastGifDither);
            SyncGifSizeDropdown();

            string buttonLabel;
            string statusText;
            bool interactable = true;

            switch (_activeCamera.GifState)
            {
                case BasisHandHeldCamera.BasisGifRecorderState.Recording:
                    int secondsLeft = Mathf.CeilToInt(_activeCamera.GifSecondsRemaining);
                    buttonLabel = BasisLocalization.Get("camera.gif.stop", secondsLeft);
                    statusText = BasisLocalization.Get("camera.gif.status.recording", _activeCamera.GifFramesCaptured);
                    break;

                case BasisHandHeldCamera.BasisGifRecorderState.Saving:
                    buttonLabel = BasisLocalization.Get("camera.gif.saving");
                    statusText = BasisLocalization.Get("camera.gif.status.saving",
                        _activeCamera.GifFramesEncoded, _activeCamera.GifFramesCaptured);
                    interactable = false;
                    break;

                default:
                    buttonLabel = BasisLocalization.Get("camera.gif.record");
                    if (BasisNetworkModeration.CameraCaptureBlockedLocally)
                    {
                        statusText = BasisLocalization.Get("camera.gif.status.blocked");
                        interactable = false;
                    }
                    else if (_activeCamera.LastGifFailure != null)
                    {
                        statusText = BasisLocalization.Get("camera.gif.status.failed", _activeCamera.LastGifFailure);
                    }
                    else if (_activeCamera.LastGifFileName != null)
                    {
                        statusText = BasisLocalization.Get("camera.gif.status.saved", _activeCamera.LastGifFileName);
                    }
                    else
                    {
                        statusText = BasisLocalization.Get("camera.gif.status.idle");
                    }
                    break;
            }

            if (buttonLabel != _lastGifButtonLabel)
            {
                _lastGifButtonLabel = buttonLabel;
                _gifRecordButton.Descriptor.SetTitle(buttonLabel);
                _gifRecordButton.SetInteractable(interactable);
            }

            if (statusText != _lastGifStatusText)
            {
                _lastGifStatusText = statusText;
                _gifStatus?.SetDescription(statusText);
            }
        }

        private void SyncGifSizeDropdown()
        {
            if (_gifSizeDropdown == null || _activeCamera == null) return;
            if (_activeCamera.GifWidth == _lastGifWidth) return;

            // Never move a list that is open under the user's pointer; the width is re-read the
            // moment it closes because the cache is only advanced on a successful write.
            if (_gifSizeDropdown.DropdownComponent != null && _gifSizeDropdown.DropdownComponent.IsExpanded) return;

            _lastGifWidth = _activeCamera.GifWidth;

            int[] presets = BasisHandHeldCamera.GifWidthPresets;
            int nearest = 0;
            for (int Index = 1; Index < presets.Length; Index++)
            {
                if (Mathf.Abs(presets[Index] - _lastGifWidth) < Mathf.Abs(presets[nearest] - _lastGifWidth))
                {
                    nearest = Index;
                }
            }
            _gifSizeDropdown.SetValueWithoutNotify($"{presets[nearest]} px");
        }

        private void ClearGifReferences()
        {
            _gifSection = null;
            _gifGroup = null;
            _gifRecordButton = null;
            _gifStatus = null;
            _gifDurationSlider = null;
            _gifFrameRateSlider = null;
            _gifSizeDropdown = null;
            _gifLoopToggle = null;
            _gifDitherToggle = null;
            _lastGifButtonLabel = null;
            _lastGifStatusText = null;
            _lastGifDuration = float.NaN;
            _lastGifFrameRate = float.NaN;
            _lastGifWidth = -1;
            _lastGifLoop = null;
            _lastGifDither = null;
        }
    }
}
