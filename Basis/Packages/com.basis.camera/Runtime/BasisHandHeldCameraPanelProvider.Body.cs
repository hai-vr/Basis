using UnityEngine;

namespace Basis.BasisUI.HandHeldCamera
{
    /// <summary>
    /// The camera you are holding, on the Mode page: what is left in it, whether the flash is armed,
    /// and the one button that puts more film in.
    ///
    /// <para>Everything else on this page is a setting. This section is not — the frame counter goes
    /// down when you take a photograph and comes back only by reloading, and nothing on the panel
    /// can talk it out of that. It sits with the mode picker because the picker is what hands you a
    /// body in the first place, and because a camera that has stopped taking pictures is a question
    /// about the mode, not about the lens.</para>
    ///
    /// <para>Polled on the panel tick rather than driven by an event from the camera, like every
    /// other control on this page. The state moves on a timer nobody presses — a wind-on finishing,
    /// a flash charging — so there is nothing to subscribe to that a quarter-second poll does not
    /// already catch, and the section reads no state a photograph has not already changed.</para>
    /// </summary>
    public partial class BasisHandHeldCameraPanelProvider
    {
        private PanelSectionToggle _bodySection;
        private PanelElementDescriptor _bodyGroup;
        private PanelElementDescriptor _bodyStatus;
        private PanelButton _bodyReloadButton;
        private PanelToggle _bodyFlashToggle;

        private bool? _lastBodyFlash;

        /// <summary>
        /// What the status card currently reads. The card is rewritten on the same quarter-second
        /// beat as the settings readout beside it, and a status that has not moved must not reflow
        /// the page — which for a three-line card is the whole cost of the section.
        /// </summary>
        private string _lastBodyStatus;

        private void BuildBodySection(RectTransform parent)
        {
            _bodySection = PanelSectionToggle.CreateNewEntry(parent);
            _bodyGroup = PanelSectionToggleHelpers.CreateCollapsibleContentGroup(
                _bodySection, parent, BasisLocalization.Get("camera.body"), false);

            BuildBodyControls(_bodyGroup.ContentParent);

            PanelSectionToggleHelpers.FinalizeCollapsibleGroup(
                _bodySection, _bodyGroup, true, OnSectionExpanded);
        }

        private void BuildBodyControls(RectTransform content)
        {
            // A card of text rather than rows of readouts: the three facts on it — which camera,
            // what is left, what the flash is doing — are one sentence about one object, and split
            // across three labelled rows they read as three unrelated numbers.
            _bodyStatus = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, content);
            ReleaseControlSlot(_bodyStatus);
            _bodyStatus.SetDescription(BasisLocalization.Get("camera.body.help"));

            _bodyFlashToggle = PanelToggle.CreateNewEntry(content);
            _bodyFlashToggle.Descriptor.SetTitle(BasisLocalization.Get("camera.body.flash"));
            _bodyFlashToggle.Descriptor.SetTooltip(BasisLocalization.Get("camera.body.flash.tooltip"));
            _bodyFlashToggle.OnValueChanged = v =>
            {
                _activeCamera?.SetFlashEnabled(v);
                RefreshBodyControls(force: true);
            };

            _bodyReloadButton = PanelButton.CreateNew(content);
            _bodyReloadButton.Descriptor.SetTitle(BasisLocalization.Get("camera.body.reload"));
            _bodyReloadButton.Descriptor.SetTooltip(BasisLocalization.Get("camera.body.reload.tooltip"));
            _bodyReloadButton.OnClicked += () =>
            {
                _activeCamera?.ReloadFilm();
                RefreshBodyControls(force: true);
            };

            RefreshBodyControls(force: true);
        }

        /// <summary>
        /// Brings the card, the toggle and the button in line with the camera in hand.
        ///
        /// <para>Both controls stay present on a body that has neither a flash nor film in it, and
        /// go dead instead of disappearing. A row that vanished would take the page's height with
        /// it every time somebody tried a different mode, and "this camera does not have one" is
        /// something the card is already saying in words.</para>
        /// </summary>
        private void RefreshBodyControls(bool force = false)
        {
            if (_bodyStatus == null) return;

            BasisCameraBodyTraits body = _activeCamera != null
                ? _activeCamera.BodyTraits
                : BasisCameraBodies.Get(BasisCameraBodyKind.Digital);

            if (_bodyFlashToggle != null)
            {
                SyncToggle(_bodyFlashToggle, _activeCamera != null && _activeCamera.FlashEnabled, ref _lastBodyFlash);
                if (_bodyFlashToggle.ToggleComponent != null)
                {
                    _bodyFlashToggle.ToggleComponent.interactable = _activeCamera != null && body.HasFlash;
                }
            }

            if (_bodyReloadButton?.ButtonComponent != null)
            {
                // Live only where there is something to reload and something to reload it with: a
                // full camera has nothing to gain from the button, and a digital one has no film.
                _bodyReloadButton.ButtonComponent.interactable =
                    _activeCamera != null && body.HasFilm && _activeCamera.ExposuresRemaining < body.Exposures;
            }

            string status = BuildBodyStatus(body);
            if (!force && string.Equals(status, _lastBodyStatus)) return;

            _lastBodyStatus = status;
            _bodyStatus.SetDescription(status);
            RebuildModeLayout(_bodyGroup);
        }

        /// <summary>
        /// The card's text: which camera, what is left in it, and what the shutter is doing — in
        /// that order, because that is the order the questions arrive in when a photograph does not
        /// happen.
        /// </summary>
        private string BuildBodyStatus(BasisCameraBodyTraits body)
        {
            if (_activeCamera == null) return BasisLocalization.Get("camera.body.help");

            string text = BasisLocalization.Get(BasisCameraBodies.TitleKey(body.Kind));

            if (body.HasFilm)
            {
                text += "\n" + BasisLocalization.Get(
                    "camera.body.status.frames",
                    _activeCamera.ExposuresRemaining.ToString(),
                    body.Exposures.ToString());
            }
            else
            {
                text += "\n" + BasisLocalization.Get("camera.body.status.unlimited");
            }

            switch (_activeCamera.EvaluateShutter())
            {
                case BasisCameraShutterState.OutOfFilm:
                    text += "\n" + BasisLocalization.Get("camera.body.status.empty");
                    break;
                case BasisCameraShutterState.Developing:
                    text += "\n" + BasisLocalization.Get("camera.body.status.developing");
                    break;
                case BasisCameraShutterState.WindingOn:
                    text += "\n" + BasisLocalization.Get("camera.body.status.winding");
                    break;
                default:
                    text += "\n" + BasisLocalization.Get("camera.body.status.ready");
                    break;
            }

            if (body.HasFlash)
            {
                string flash =
                    !_activeCamera.FlashEnabled ? "camera.body.status.flashOff" :
                    _activeCamera.FlashRecycleRemaining > 0f ? "camera.body.status.flashCharging" :
                    "camera.body.status.flashReady";

                text += "\n" + BasisLocalization.Get(flash);
            }

            return text;
        }

        private void ClearBodyReferences()
        {
            _bodySection = null;
            _bodyGroup = null;
            _bodyStatus = null;
            _bodyReloadButton = null;
            _bodyFlashToggle = null;

            // Forces the card to be written on the next open. It is rebuilt holding the help line,
            // so a remembered status would skip the one write that gives it its real height.
            _lastBodyStatus = null;
            _lastBodyFlash = null;
        }
    }
}
