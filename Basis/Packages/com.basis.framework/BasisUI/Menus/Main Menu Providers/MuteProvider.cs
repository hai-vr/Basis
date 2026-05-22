#if !BASIS_DISABLE_MICROPHONE
using Basis.Scripts.Device_Management;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.NetworkedAvatar;
using Basis.Scripts.Networking.Transmitters;
using UnityEngine;

namespace Basis.BasisUI
{
    public class MuteProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        [RuntimeInitializeOnLoadMethod]
        public static void AddToMenu()
        {
            BasisMenuBase<BasisMainMenu>.AddProvider(new MuteProvider());
        }

        public override string Title => BasisLocalization.Get(BasisLocalMicrophoneDriver.isPaused ? "menu.provider.unmute" : "menu.provider.mute");
        public override string IconAddress => BasisLocalMicrophoneDriver.isPaused
            ? AddressableAssets.Sprites.MicrophoneMute
            : AddressableAssets.Sprites.Microphone;
        public override int Order => 0; // first — in front of Settings
        public override bool Hidden => false;

        public override void RunAction()
        {
            BasisLocalMicrophoneDriver.ToggleIsPaused();
        }

        public override void OnButtonCreated(PanelButton button)
        {
            // Unsubscribe first to prevent duplicate subscriptions across menu open/close cycles
            BasisLocalMicrophoneDriver.OnPausedAction -= OnMuteChanged;
            BasisLocalMicrophoneDriver.OnPausedAction += OnMuteChanged;
            BasisNetworkModeration.OnShoutModeChanged -= OnShoutModeChanged;
            BasisNetworkModeration.OnShoutModeChanged += OnShoutModeChanged;
            BasisTalkModeManager.OnLocalTalkModeChanged -= OnTalkModeChanged;
            BasisTalkModeManager.OnLocalTalkModeChanged += OnTalkModeChanged;

            UpdateButtonVisuals(button, BasisLocalMicrophoneDriver.isPaused);

            // Half the normal hotbar button width — a compact mute button (like Exit).
            Vector2 size = button.rectTransform.sizeDelta;
            if (size.x > 0f) button.SetSize(new Vector2(size.x * 0.5f, size.y));
        }

        private void OnMuteChanged(bool isMuted)
        {
            if (BoundButton == null)
            {
                return;
            }

            UpdateButtonVisuals(BoundButton, isMuted);
        }

        private void OnShoutModeChanged(ushort playerId, bool enabled)
        {
            if (BoundButton == null)
                return;
            if (BasisNetworkPlayer.LocalPlayer == null || playerId != BasisNetworkPlayer.LocalPlayer.playerId)
                return;

            UpdateButtonVisuals(BoundButton, BasisLocalMicrophoneDriver.isPaused);
        }

        private void OnTalkModeChanged()
        {
            BasisDeviceManagement.EnqueueOnMainThread(() =>
            {
                if (BoundButton == null) return;
                UpdateButtonVisuals(BoundButton, BasisLocalMicrophoneDriver.isPaused);
            });
        }

        private static readonly Color MutedColor = new Color(1f, 0.3f, 0.3f, 1f);
        private static readonly Color ShoutColor = new Color(1f, 0.5490196f, 0f, 1f);
        private static readonly Color PrivateColor = new Color(0.6078432f, 0.1882353f, 1f, 1f);
        private static readonly Color DirectColor = new Color(0.12156863f, 0.7490196f, 0.3529412f, 1f);
        private static readonly Color ThisPersonColor = new Color(1f, 0.3098039f, 0.627451f, 1f);

        private static Color ModeColor(BasisTalkMode mode)
        {
            switch (mode)
            {
                case BasisTalkMode.Private: return PrivateColor;
                case BasisTalkMode.Direct: return DirectColor;
                case BasisTalkMode.ThisPerson: return ThisPersonColor;
                default: return Color.white;
            }
        }

        private void UpdateButtonVisuals(PanelButton button, bool isMuted)
        {
            string icon = isMuted
                ? AddressableAssets.Sprites.MicrophoneMute
                : AddressableAssets.Sprites.Microphone;

            button.SetIcon(icon);
            button.Descriptor.SetTitle(BasisLocalization.Get(isMuted ? "menu.provider.unmute" : "menu.provider.mute"));
            Color color = isMuted
                ? MutedColor
                : BasisAudioTransmission.IsInShoutMode
                    ? ShoutColor
                    : ModeColor(BasisTalkModeManager.CurrentMode);
            button.Descriptor.IconImage.color = color;
            button.Descriptor.TitleLabel.color = color;
        }
    }
}
#endif
