using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking;
using Basis.Scripts.Networking.Transmitters;
using Basis.Scripts.UI.UI_Panels;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BasisIndividualPlayerSettings : BasisUIBase
{
    public static string Path = "Packages/com.basis.sdk/Prefabs/UI/PlayerSelectionPanel.prefab";
    public static string CursorRequest = "PlayerSelectionPanel";

    [Header("Controls")]
    public Slider UserVolumeOverride;
    public Button ToggleAvatar;
    public Button ToggleAvatarInteraction;
    public Button RequestAvatarClone;

    [Header("Texts")]
    public TextMeshProUGUI AvatarVisibleText;
    public TextMeshProUGUI AvatarInteractionsText;
    public TextMeshProUGUI SliderVolumePercentage;
    public TextMeshProUGUI PlayerName;
    public TextMeshProUGUI PlayerUUID;
    public TextMeshProUGUI PlayerDebug;
    [Header("Context")]
    public BasisRemotePlayer RemotePlayer;
    public BasisUIVolumeSampler BasisUIVolumeSampler;

    [Header("Config")]
    public float step = 0.05f; // The interval between values
    public override void DestroyEvent()
    {
        CheckThenUnAssign();
        BasisCursorManagement.LockCursor(CursorRequest);
    }
    public void OnDisable()
    {
        CheckThenUnAssign();
    }
    public void OnDestroy()
    {
        CheckThenUnAssign();
    }
    public void CheckThenUnAssign()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public override void InitalizeEvent()
    {
        BasisCursorManagement.UnlockCursor(CursorRequest);
    }
    public static BasisIndividualPlayerSettings Instance;
    public static async void OpenPlayerSettings(BasisRemotePlayer RemotePlayer)
    {
        BasisUIManagement.CloseAllMenus();
        BasisUIBase Base = OpenMenuNow(Path);
        var PlayerSettings = (BasisIndividualPlayerSettings)Base;
        Instance = PlayerSettings;
        await PlayerSettings.Initalize(RemotePlayer);
    }
    public async Task Initalize(BasisRemotePlayer remotePlayer)
    {
        RemotePlayer = remotePlayer;
        BasisUIVolumeSampler.Initalize(remotePlayer);

        PlayerName.text = RemotePlayer.DisplayName;
        PlayerUUID.text = RemotePlayer.UUID;

        // Slider setup
        UserVolumeOverride.wholeNumbers = false;
        UserVolumeOverride.maxValue = 1.5f;
        UserVolumeOverride.minValue = 0f;

        // Load settings
        var settings = await BasisPlayerSettingsManager.RequestPlayerSettings(RemotePlayer.UUID);

        // Apply to UI (set values BEFORE wiring listeners so we don't trigger saves immediately)
        UserVolumeOverride.SetValueWithoutNotify(settings.VolumeLevel);
        SliderVolumePercentage.text = Mathf.RoundToInt(settings.VolumeLevel * 100) + "%";

        AvatarVisibleText.text = settings.AvatarVisible ? "Hide Avatar" : "Show Avatar";
        AvatarInteractionsText.text = settings.AvatarInteraction ? "Disable Interactions" : "Enable Interactions";

        // Wire listeners
        ToggleAvatar.onClick.RemoveAllListeners();
        ToggleAvatar.onClick.AddListener(() => ToggleAvatarPressed(RemotePlayer.UUID));

        ToggleAvatarInteraction.onClick.RemoveAllListeners();
        ToggleAvatarInteraction.onClick.AddListener(() => ToggleAvatarInteractions(RemotePlayer.UUID));

        // If this button should *clone* the avatar, point it to the correct action.
        // Kept as-is in case your original intent was to reuse the visibility toggle.
        RequestAvatarClone.onClick.RemoveAllListeners();
        RequestAvatarClone.onClick.AddListener(() => ToggleAvatarPressed(RemotePlayer.UUID));

        UserVolumeOverride.onValueChanged.RemoveAllListeners();
        UserVolumeOverride.onValueChanged.AddListener(value => ChangePlayersVolume(RemotePlayer.UUID, value));
    }

    float SnapValue(float value)
    {
        return Mathf.Round(value / step) * step;
    }

    public async void ToggleAvatarInteractions(string playerUUID)
    {
        var settings = await BasisPlayerSettingsManager.RequestPlayerSettings(playerUUID);
        settings.AvatarInteraction = !settings.AvatarInteraction;
        await BasisPlayerSettingsManager.SetPlayerSettings(settings);

        AvatarInteractionsText.text = settings.AvatarInteraction ? "Disable Interactions" : "Enable Interactions";

        if (RemotePlayer != null)
        {
            RemotePlayer.ReloadAvatar();
        }
    }

    public async void ToggleAvatarPressed(string playerUUID)
    {
        var settings = await BasisPlayerSettingsManager.RequestPlayerSettings(playerUUID);
        settings.AvatarVisible = !settings.AvatarVisible;
        await BasisPlayerSettingsManager.SetPlayerSettings(settings);

        AvatarVisibleText.text = settings.AvatarVisible ? "Hide Avatar" : "Show Avatar";

        if (RemotePlayer != null)
        {
            RemotePlayer.ReloadAvatar();
        }
    }

    public async void ChangePlayersVolume(string playerUUID, float volume)
    {
        volume = SnapValue(volume);
        UserVolumeOverride.SetValueWithoutNotify(volume);

        var settings = await BasisPlayerSettingsManager.RequestPlayerSettings(playerUUID);
        settings.VolumeLevel = volume;

        SliderVolumePercentage.text = Mathf.RoundToInt(volume * 100) + "%";
        await BasisPlayerSettingsManager.SetPlayerSettings(settings);

        if (RemotePlayer != null)
        {
            RemotePlayer.NetworkReceiver.AudioReceiverModule.ChangeRemotePlayersVolumeSettings(volume);
        }
        bool over = volume > 1.0f;
        SliderVolumePercentage.color = over ? Color.red : Color.white;
    }
    public void Update()
    {
        DisplayData();
    }
    /// <summary>
    /// lets add a debug for stats about audio playback
    /// 
    /// </summary>
    public void DisplayData()
    {
        // Make sure we actually have a player selected
        if (RemotePlayer == null)
        {
            BasisDebug.LogError("DisplayData: RemotePlayer is null.", BasisDebug.LogTag.Voice);
            return;
        }

        // Make sure networking is available
        var nm = BasisNetworkManagement.Instance;
        if (nm == null || nm.LocalAccessTransmitter == null)
        {
            BasisDebug.LogError("DisplayData: No LocalAccessTransmitter.", BasisDebug.LogTag.Voice);
            return;
        }

        var transmitter = nm.LocalAccessTransmitter;
        var results = transmitter.TransmissionResults;

        if (results == null)
        {
            BasisDebug.LogError("DisplayData: TransmissionResults is null.", BasisDebug.LogTag.Voice);
            return;
        }

        // Basic sanity on the managed mirrors
        if (results.HearingIndexToId == null ||
            results.MicrophoneRangeIndex == null ||
            results.HearingIndex == null ||
            results.AvatarIndex == null ||
            results.CalculatedDistances == null)
        {
            BasisDebug.LogError("DisplayData: TransmissionResults arrays not initialized.", BasisDebug.LogTag.Voice);
            return;
        }

        int length = results.IndexLength >= 0 ? results.IndexLength : results.HearingIndexToId.Length;
        if (BasisNetworkPlayers.PlayerToNetworkedPlayer(RemotePlayer, out var networkedplayer))
        {
            ushort targetId = networkedplayer.playerId;

            // Find this player’s index in the transmission arrays
            int index = -1;
            for (int i = 0; i < length && i < results.HearingIndexToId.Length; i++)
            {
                if (results.HearingIndexToId[i] == targetId)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                BasisDebug.LogError(
                    $"DisplayData: Could not find playerId {targetId} ({RemotePlayer.DisplayName}) in HearingIndexToId.",
                    BasisDebug.LogTag.Voice);
                return;
            }

            // Guard against any weird length mismatches
            if (index >= results.MicrophoneRangeIndex.Length ||
                index >= results.HearingIndex.Length ||
                index >= results.AvatarIndex.Length ||
                index >= results.CalculatedDistances.Length)
            {
                BasisDebug.LogError("DisplayData: Index out of range for TransmissionResults arrays.", BasisDebug.LogTag.Voice);
                return;
            }

            bool inMicRange = results.MicrophoneRangeIndex[index];
            bool inHearingRange = results.HearingIndex[index];
            bool inAvatarRange = results.AvatarIndex[index];

            // CalculatedDistances is squared distance (copied from Native distanceSq)
            float d2 = results.CalculatedDistances[index];
            float d = Mathf.Sqrt(Mathf.Max(0f, d2));

            string log =
                $"  Index: {targetId}, index: {index}\n" + $"  SQDis: {d2:F3}, dis: {d:F3} m\n" +
                $"  inMicRange: {inMicRange}\n" + $"  inHearingRange: {inHearingRange}\n" +
                $"  inAvatarRange: {inAvatarRange}\n" + $"  intervalSeconds: {results.intervalSeconds:F3}\n" +
                $"  defaultInterval: {results.DefaultInterval:F3}\n" + $"  unClampedInterval: {results.UnClampedInterval:F3}";

            // Optional: also show something in the UI if you have a text field for it
            if (PlayerDebug != null)
            {
                PlayerDebug.text = log;
            }
        }
    }
}
