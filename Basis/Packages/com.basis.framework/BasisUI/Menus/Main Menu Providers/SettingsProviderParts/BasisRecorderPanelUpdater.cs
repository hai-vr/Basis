using UnityEngine;

namespace Basis.BasisUI
{
    public class BasisRecorderPanelUpdater : MonoBehaviour
    {
        public PanelElementDescriptor StatusField;
        public PanelButton RecordButton;

        private string _lastStatus;
        private string _lastButton;
        private bool _subscribed;
        private bool _richTextDisabled;

        public void Initialize(PanelElementDescriptor statusField, PanelButton recordButton)
        {
            StatusField = statusField;
            RecordButton = recordButton;

            if (!_subscribed)
            {
                BasisAvatarRecorderDriver.OnChanged += Refresh;
                _subscribed = true;
            }

            Refresh();
        }

        private void OnDestroy()
        {
            if (_subscribed)
            {
                BasisAvatarRecorderDriver.OnChanged -= Refresh;
                _subscribed = false;
            }
        }

        private void Refresh()
        {
            if (StatusField != null)
            {
                if (!_richTextDisabled)
                {
                    StatusField.DisableRichText();
                    _richTextDisabled = true;
                }

                string status = BuildStatus();
                if (status != _lastStatus)
                {
                    StatusField.SetDescription(status);
                    _lastStatus = status;
                }
            }

            if (RecordButton != null)
            {
                string title = BuildButtonTitle();
                if (title != _lastButton)
                {
                    RecordButton.Descriptor.SetTitle(title);
                    _lastButton = title;
                }
            }
        }

        private static string BuildStatus()
        {
            switch (BasisAvatarRecorderDriver.State)
            {
                case BasisAvatarRecorderDriver.RecorderState.CountingDown:
                    return BasisLocalization.Get("settings.developer.recorder.status.countdown",
                        Mathf.CeilToInt(BasisAvatarRecorderDriver.CountdownRemaining).ToString());

                case BasisAvatarRecorderDriver.RecorderState.Recording:
                    return BasisLocalization.Get("settings.developer.recorder.status.recording",
                        FormatTime(BasisAvatarRecorderDriver.ElapsedSeconds),
                        BasisAvatarRecorderDriver.FrameCount.ToString());

                default:
                    return BasisLocalization.Get("settings.developer.recorder.status.idle");
            }
        }

        private static string BuildButtonTitle()
        {
            switch (BasisAvatarRecorderDriver.State)
            {
                case BasisAvatarRecorderDriver.RecorderState.CountingDown:
                    return BasisLocalization.Get("settings.developer.recorder.cancel");
                case BasisAvatarRecorderDriver.RecorderState.Recording:
                    return BasisLocalization.Get("settings.developer.recorder.stop");
                default:
                    return BasisLocalization.Get("settings.developer.recorder.start");
            }
        }

        private static string FormatTime(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int minutes = (int)(seconds / 60f);
            float remainder = seconds - minutes * 60f;
            return $"{minutes:00}:{remainder:00.0}";
        }
    }
}
