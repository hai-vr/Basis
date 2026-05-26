using System.Collections.Generic;
using Basis.BasisUI;
using Basis.Scripts.Drivers;
using Basis.Scripts.Networking;
using UnityEngine;
using UnityEngine.UI;

namespace Basis.BasisUI.VideoPlayer
{
    public class BasisVideoPlayerPanelProvider : BasisMenuActionProvider<BasisMainMenu>
    {
        public const string Perm_Control = "basis.videoplayer.control";
        public const string StaticTitle = "Video Players";

        private static BasisVideoPlayerPanelProvider _instance;

        public override string Title => StaticTitle;
        public override string IconAddress => AddressableAssets.Sprites.Camera;
        public override int Order => 8;
        public override bool Hidden => BasisVideoPlayerRegistry.Count == 0;

        private BasisMenuPanel _panel;
        private RectTransform _scrollContent;
        private PanelDropdown _selector;
        private PanelElementDescriptor _controlGroup;
        private PanelElementDescriptor _userGroup;
        private PanelElementDescriptor _emptyState;
        private PanelElementDescriptor _debugGroup;
        private PanelToggle _debugToggle;
        private PanelTextField _urlField;
        private PanelSlider _volumeSlider;
        private PanelToggle _muteToggle;
        private BasisVideoPlayer _activePlayer;
        private readonly List<BasisVideoPlayer> _entries = new List<BasisVideoPlayer>();
        private bool _debugTickSubscribed;
        private readonly System.Text.StringBuilder _debugBuilder = new System.Text.StringBuilder(256);

        [RuntimeInitializeOnLoadMethod]
        public static void AddToMenu()
        {
            _instance = new BasisVideoPlayerPanelProvider();
            BasisMenuBase<BasisMainMenu>.AddProvider(_instance);
            BasisVideoPlayerRegistry.OnChanged += RefreshMainMenu;
        }

        private static void RefreshMainMenu()
        {
            if (BasisMenuBase<BasisMainMenu>.Instance) BasisMenuBase<BasisMainMenu>.Instance.BindProvidersToButtons();
            if (BasisMainMenu.ActiveMenuTitle == StaticTitle && _instance != null) _instance.RebuildSelector();
        }

        public static bool HasControlPermission()
        {
            if (!BasisNetworkConnection.LocalPlayerIsConnected) return true;
            var perms = BasisNetworkManagement.LocalPermissions;
            return perms != null && (perms.Contains(Perm_Control) || perms.Contains("*"));
        }

        public override void RunAction()
        {
            if (BasisMainMenu.ActiveMenuTitle == Title)
            {
                BasisMainMenu.Instance.ActiveMenu.ReleaseInstance();
                return;
            }

            BasisMenuPanel panel = BasisMainMenu.CreateActiveMenu(
                BasisMenuPanel.PanelData.Standard(Title),
                BasisMenuPanel.PanelStyles.Page);
            BoundButton?.BindActiveStateToAddressablesInstance(panel);
            _panel = panel;

            panel.OnInstanceReleased += OnPanelClosed;

            RectTransform container = panel.Descriptor.ContentParent;
            PanelElementDescriptor scroll = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.ScrollViewVertical, container);
            _scrollContent = scroll.ContentParent;

            _selector = PanelDropdown.CreateNewEntry(_scrollContent);
            _selector.Descriptor.SetTitle("Player");
            _selector.OnValueChanged = _ => OnSelectionChanged();

            _emptyState = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, _scrollContent);
            _emptyState.SetTitle("No Video Players");
            _emptyState.SetDescription("Spawn or load a prop/scene that contains a Basis Video Player.");

            BuildControlGroup(_scrollContent);
            BuildUserGroup(_scrollContent);
            BuildDebugGroup(_scrollContent);

            RebuildSelector();
        }

        private void OnPanelClosed()
        {
            SetDebugTickSubscription(false);
            _panel = null;
            _scrollContent = null;
            _selector = null;
            _controlGroup = null;
            _userGroup = null;
            _emptyState = null;
            _debugGroup = null;
            _debugToggle = null;
            _urlField = null;
            _volumeSlider = null;
            _muteToggle = null;
            _activePlayer = null;
            _entries.Clear();
        }

        public override void OnReleaseEvent() => OnPanelClosed();

        private void BuildControlGroup(RectTransform parent)
        {
            _controlGroup = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, parent);
            _controlGroup.SetTitle("Playback");
            _controlGroup.SetDescription("Requires the basis.videoplayer.control permission.");
            RectTransform content = _controlGroup.ContentParent;

            _urlField = PanelTextField.CreateNewEntry(content);
            _urlField.Descriptor.SetTitle("URL");

            RectTransform actions = BuildActionRow(content);

            PanelButton loadBtn = PanelButton.CreateNew(actions);
            loadBtn.Descriptor.SetTitle("Load URL");
            loadBtn.OnClicked += () =>
            {
                if (_activePlayer == null || _urlField == null) return;
                string u = _urlField.Value;
                if (!string.IsNullOrEmpty(u)) _activePlayer.LoadUrl(u);
            };

            PanelButton playBtn = PanelButton.CreateNew(actions);
            playBtn.Descriptor.SetTitle("Play");
            playBtn.OnClicked += () => _activePlayer?.Play();

            PanelButton pauseBtn = PanelButton.CreateNew(actions);
            pauseBtn.Descriptor.SetTitle("Pause");
            pauseBtn.OnClicked += () => _activePlayer?.Pause();

            PanelButton stopBtn = PanelButton.CreateNew(actions);
            stopBtn.Descriptor.SetTitle("Stop");
            stopBtn.OnClicked += () => _activePlayer?.Stop();
        }

        private void BuildUserGroup(RectTransform parent)
        {
            _userGroup = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, parent);
            _userGroup.SetTitle("My Settings");
            _userGroup.SetDescription("Client-side controls — only affect your own playback.");
            RectTransform content = _userGroup.ContentParent;

            _volumeSlider = PanelSlider.CreateNew(content);
            _volumeSlider.SetSliderSettings(PanelSlider.SliderSettings.Percentage("Volume"));
            _volumeSlider.OnValueChanged = v =>
            {
                if (_activePlayer == null) return;
                _activePlayer.Volume = Mathf.Clamp01(v / 100f);
                if (_activePlayer.AudioComponent != null) _activePlayer.AudioComponent.VolumeGain = _activePlayer.Volume;
            };

            _muteToggle = PanelToggle.CreateNewEntry(content);
            _muteToggle.Descriptor.SetTitle("Mute");
            _muteToggle.OnValueChanged = v =>
            {
                if (_activePlayer == null) return;
                _activePlayer.Mute = v;
                if (_activePlayer.AudioComponent != null) _activePlayer.AudioComponent.Mute = v;
            };

            RectTransform actions = BuildActionRow(content);
            PanelButton resyncBtn = PanelButton.CreateNew(actions);
            resyncBtn.Descriptor.SetTitle("Resync");
            resyncBtn.OnClicked += () =>
            {
                if (_activePlayer == null) return;
                _activePlayer.AudioComponent?.ResetSyncAnchor();
                _activePlayer.Clock.Reset();
            };
        }

        private void RebuildSelector()
        {
            if (_selector == null) return;

            _entries.Clear();
            List<string> labels = new List<string>();
            for (int i = 0; i < BasisVideoPlayerRegistry.Players.Count; i++)
            {
                BasisVideoPlayer p = BasisVideoPlayerRegistry.Players[i];
                if (p == null) continue;
                _entries.Add(p);
                string name = !string.IsNullOrEmpty(p.DisplayName)
                    ? p.DisplayName
                    : (p.gameObject != null ? p.gameObject.name : "(destroyed)");
                labels.Add($"{i + 1}. {name}");
            }

            _selector.AssignEntries(labels);

            if (_entries.Count == 0)
            {
                _selector.gameObject.SetActive(false);
                _emptyState?.SetActive(true);
                SetGroupsActive(false);
                _activePlayer = null;
                return;
            }

            _selector.gameObject.SetActive(true);
            _emptyState?.SetActive(false);

            int idx = _activePlayer != null ? _entries.IndexOf(_activePlayer) : 0;
            if (idx < 0) idx = 0;
            _activePlayer = _entries[idx];
            _selector.SetValueWithoutNotify(labels[idx]);

            ApplyActivePlayerToControls();
        }

        private void OnSelectionChanged()
        {
            if (_selector == null) return;
            int idx = _selector.Index;
            if (idx < 0 || idx >= _entries.Count) return;
            _activePlayer = _entries[idx];
            ApplyActivePlayerToControls();
        }

        private void ApplyActivePlayerToControls()
        {
            bool canControl = HasControlPermission();
            _controlGroup?.SetActive(canControl);
            _userGroup?.SetActive(true);

            if (_activePlayer == null) return;

            if (canControl && _urlField != null)
            {
                string current = _activePlayer.ActiveMediaSource != null
                    ? _activePlayer.ActiveMediaSource.Uri
                    : string.Empty;
                _urlField.SetValueWithoutNotify(current ?? string.Empty);
            }

            _volumeSlider?.SetValueWithoutNotify(Mathf.Clamp01(_activePlayer.Volume) * 100f);
            _muteToggle?.SetValueWithoutNotify(_activePlayer.Mute);

            if (_debugTickSubscribed) RefreshDebugInfo();
        }

        private void SetGroupsActive(bool active)
        {
            _controlGroup?.SetActive(active && HasControlPermission());
            _userGroup?.SetActive(active);
        }

        private void BuildDebugGroup(RectTransform parent)
        {
            _debugGroup = PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, parent);
            _debugGroup.SetTitle("Debug");
            _debugGroup.SetDescription("Toggle to surface live pipeline counters.");
            RectTransform content = _debugGroup.ContentParent;

            _debugToggle = PanelToggle.CreateNewEntry(content);
            _debugToggle.Descriptor.SetTitle("Debug Mode");
            _debugToggle.OnValueChanged = v =>
            {
                SetDebugTickSubscription(v);
                if (v) RefreshDebugInfo();
                else _debugGroup?.SetDescription("Toggle to surface live pipeline counters.");
            };
        }

        private void SetDebugTickSubscription(bool subscribe)
        {
            if (subscribe == _debugTickSubscribed) return;
            if (subscribe)
            {
                BasisFrameClock.AddRequest();
                BasisFrameClock.OnTick += RefreshDebugInfo;
            }
            else
            {
                BasisFrameClock.OnTick -= RefreshDebugInfo;
                BasisFrameClock.RemoveRequest();
            }
            _debugTickSubscribed = subscribe;
        }

        private void RefreshDebugInfo()
        {
            if (_debugGroup == null) return;
            if (_activePlayer == null)
            {
                _debugGroup.SetDescription("No active player.");
                return;
            }

            _debugBuilder.Clear();
            var eng = _activePlayer.NativeEngine;
            string backend = eng != null ? "OS-codec engine" : (_activePlayer.Source != null ? _activePlayer.Source.GetType().Name : "(no source)");
            _debugBuilder.Append("Backend: ").Append(backend).Append('\n');

            string state = _activePlayer.IsPlaying ? (_activePlayer.IsPaused ? "Paused" : "Playing") : "Stopped";
            _debugBuilder.Append("State: ").Append(state).Append('\n');

            var sz = _activePlayer.VideoSize;
            _debugBuilder.Append("Size: ").Append(sz.x > 0 ? $"{sz.x} x {sz.y}" : "—").Append('\n');

            long mediaUs = _activePlayer.Clock != null ? _activePlayer.Clock.CurrentMediaTimeUs : 0;
            _debugBuilder.Append("Position: ").Append(mediaUs / 1000L).Append(" ms\n");

            _debugBuilder.Append("Queue: ").Append(_activePlayer.QueuedFrameCount).Append(" / ").Append(_activePlayer.MaxQueueLength).Append('\n');
            _debugBuilder.Append("Presented: ").Append(_activePlayer.PresentedFrameCount).Append('\n');
            _debugBuilder.Append("Dropped: ").Append(_activePlayer.DroppedFrameCount)
                .Append(" (overflow ").Append(_activePlayer.OverflowDropCount)
                .Append(", late ").Append(_activePlayer.LateSkipCount)
                .Append(", fmt ").Append(_activePlayer.FormatErrorCount).Append(")\n");

            if (eng != null)
            {
                _debugBuilder.Append("Engine: ").Append(eng.State).Append('\n');
                string dbg = eng.DebugInfo;
                if (!string.IsNullOrEmpty(dbg)) _debugBuilder.Append(dbg);
            }

            var audio = _activePlayer.AudioComponent;
            if (audio != null)
            {
                var src = audio.ActiveAudioSource;
                _debugBuilder.Append("\nAudio: ")
                    .Append(src != null && src.isPlaying ? "playing" : "idle")
                    .Append(" peak ").Append(audio.LastPcmPeak.ToString("F3"))
                    .Append(" rms ").Append(audio.LastPcmRms.ToString("F3"));
            }

            _debugGroup.SetDescription(_debugBuilder.ToString());
        }

        private static RectTransform BuildActionRow(RectTransform parent)
        {
            GameObject rowGO = new GameObject("VideoPlayerActions", typeof(RectTransform));
            RectTransform rowRect = (RectTransform)rowGO.transform;
            rowRect.SetParent(parent, false);

            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);

            HorizontalLayoutGroup hlg = rowGO.AddComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = false;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.spacing = 8f;
            hlg.padding = new RectOffset(8, 8, 4, 8);

            ContentSizeFitter fitter = rowGO.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            LayoutElement layout = rowGO.AddComponent<LayoutElement>();
            layout.flexibleWidth = 1f;

            return rowRect;
        }
    }
}
