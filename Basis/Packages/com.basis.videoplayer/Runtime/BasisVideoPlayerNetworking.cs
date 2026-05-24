using System;
using System.Text;
using System.Threading.Tasks;
using Basis;
using Basis.Network.Core;
using Basis.Scripts.Networking.NetworkedAvatar;
using UnityEngine;

// Network sync layer for BasisVideoPlayer. Mirrors URL, playback state
// (stopped/playing/paused) and position across all clients in the room. Late
// joiners receive the current state from whoever owns this object via the
// OnPlayerJoined hook on BasisNetworkBehaviour.
//
// Permission model: Basis has no role system, only network ownership. The
// AllowAnyoneToTakeControl flag mirrors BasisObjectSyncNetworking.CanNetworkSteal
// (default true). When true, any client that calls a public mutator (SetUrl,
// Play, Stop, Pause, Resume, Seek) first transfers ownership to itself, then
// broadcasts the new state. When false, non-owners are rejected with a warning.
[DisallowMultipleComponent]
[RequireComponent(typeof(BasisVideoPlayer))]
public sealed class BasisVideoPlayerNetworking : BasisNetworkBehaviour
{
    public enum SyncedPlaybackState : byte
    {
        Stopped = 0,
        Playing = 1,
        Paused = 2,
    }

    [Header("Permissions")]
    [Tooltip("If true, any client may take ownership and control playback. If false, only the current owner can call SetUrl/Play/Stop/Pause/Resume/Seek.")]
    public bool AllowAnyoneToTakeControl = true;

    [Header("Sync")]
    [Tooltip("Remote clients seek to catch up when their position drifts more than this many seconds from the owner's last-broadcast position. Set to 0 to disable drift correction.")]
    [Min(0f)] public float DriftSeekThresholdSeconds = 2f;

    [Tooltip("Verbose log lines for join/leave sync, drift corrections, rejected control attempts.")]
    public bool VerboseLogging = false;

    private static readonly Encoding UrlEncoding = new UTF8Encoding(false, false);
    // [state:1][positionTicks:8][urlLen:2] = 11 bytes before URL payload.
    private const int HeaderSize = 11;

    private BasisVideoPlayer videoPlayer;
    private string currentSyncedUrl = string.Empty;
    private bool sendOnNetworkReady;

    public BasisVideoPlayer VideoPlayer => videoPlayer;

    // True when the local client may invoke mutators without being rejected. In
    // non-networked sessions this is always true; in networked sessions it
    // depends on ownership and AllowAnyoneToTakeControl.
    public bool CanLocallyControl =>
        !HasNetworkID || IsOwnedLocallyOnClient || AllowAnyoneToTakeControl;

    public void Awake()
    {
        videoPlayer = GetComponent<BasisVideoPlayer>();
    }

    public override void OnNetworkReady()
    {
        if (sendOnNetworkReady)
        {
            sendOnNetworkReady = false;
            BroadcastFullState();
        }
    }

    public override void OnPlayerJoined(BasisNetworkPlayer player)
    {
        if (player == null) return;
        if (!IsLocalOwner()) return;
        var local = BasisNetworkPlayer.LocalPlayer;
        if (local != null && player.playerId == local.playerId) return;

        SendFullStateTo(new ushort[] { player.playerId });
        if (VerboseLogging) BasisDebug.Log($"{nameof(BasisVideoPlayerNetworking)} sent late-join state to player {player.playerId}.", BasisDebug.LogTag.Video);
    }

    public override void OnOwnershipTransfer(BasisNetworkPlayer newOwner)
    {
        // When ownership lands on us we re-broadcast so everyone sees the
        // authoritative state from the new owner.
        if (IsOwnedLocallyOnClient) BroadcastFullState();
    }

    public async void SetUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return;
        if (!await AcquireControlAsync()) return;
        currentSyncedUrl = url;
        videoPlayer.LoadUrl(url);
        BroadcastFullState();
    }

    public async void Play()
    {
        if (!await AcquireControlAsync()) return;
        videoPlayer.Play();
        BroadcastFullState();
    }

    public async void Stop()
    {
        if (!await AcquireControlAsync()) return;
        videoPlayer.Stop();
        BroadcastFullState();
    }

    public async void Pause()
    {
        if (!await AcquireControlAsync()) return;
        videoPlayer.Pause();
        BroadcastFullState();
    }

    public async void Resume()
    {
        if (!await AcquireControlAsync()) return;
        videoPlayer.Resume();
        BroadcastFullState();
    }

    public async void Seek(TimeSpan position)
    {
        if (!await AcquireControlAsync()) return;
        videoPlayer.Seek(position);
        BroadcastFullState();
    }

    private async Task<bool> AcquireControlAsync()
    {
        if (!HasNetworkID) return true;
        if (IsOwnedLocallyOnClient) return true;
        if (!AllowAnyoneToTakeControl)
        {
            if (VerboseLogging) BasisDebug.LogWarning($"{nameof(BasisVideoPlayerNetworking)} control rejected: AllowAnyoneToTakeControl is false and this client is not the owner.", BasisDebug.LogTag.Video);
            return false;
        }
        var result = await TakeOwnershipAsync();
        if (!result.Success && VerboseLogging) BasisDebug.LogWarning($"{nameof(BasisVideoPlayerNetworking)} ownership request was denied by the server.", BasisDebug.LogTag.Video);
        return result.Success;
    }

    public override void OnNetworkMessage(ushort senderId, byte[] buffer, DeliveryMethod deliveryMethod)
    {
        // The owner ignores remote state — its local truth is authoritative.
        if (IsOwnedLocallyOnClient) return;
        if (!TryDeserialize(buffer, out string url, out SyncedPlaybackState state, out long positionTicks)) return;
        ApplyRemoteState(url, state, positionTicks);
    }

    private void ApplyRemoteState(string url, SyncedPlaybackState state, long positionTicks)
    {
        bool urlChanged = !string.IsNullOrEmpty(url) && url != currentSyncedUrl;
        if (urlChanged)
        {
            currentSyncedUrl = url;
            var media = BasisMediaSource.FromUrl(url);
            media.StartPosition = positionTicks > 0 ? TimeSpan.FromTicks(positionTicks) : TimeSpan.Zero;

            bool savedAutoPlay = videoPlayer.AutoPlayOnSourceAssigned;
            videoPlayer.AutoPlayOnSourceAssigned = state == SyncedPlaybackState.Playing || state == SyncedPlaybackState.Paused;
            videoPlayer.LoadSource(media);
            videoPlayer.AutoPlayOnSourceAssigned = savedAutoPlay;

            if (state == SyncedPlaybackState.Paused) videoPlayer.Pause();
            else if (state == SyncedPlaybackState.Stopped) videoPlayer.Stop();
            return;
        }

        switch (state)
        {
            case SyncedPlaybackState.Stopped:
                if (videoPlayer.IsPlaying) videoPlayer.Stop();
                break;

            case SyncedPlaybackState.Playing:
                if (!videoPlayer.IsPlaying) videoPlayer.Play();
                else if (videoPlayer.IsPaused) videoPlayer.Resume();
                MaybeCorrectDrift(positionTicks);
                break;

            case SyncedPlaybackState.Paused:
                if (!videoPlayer.IsPlaying) videoPlayer.Play();
                if (!videoPlayer.IsPaused) videoPlayer.Pause();
                MaybeCorrectDrift(positionTicks);
                break;
        }
    }

    private void MaybeCorrectDrift(long positionTicks)
    {
        if (DriftSeekThresholdSeconds <= 0f) return;
        if (positionTicks <= 0) return;
        var target = TimeSpan.FromTicks(positionTicks);
        var current = videoPlayer.Position;
        double diff = Math.Abs((target - current).TotalSeconds);
        if (diff <= DriftSeekThresholdSeconds) return;
        try
        {
            videoPlayer.Seek(target);
            if (VerboseLogging) BasisDebug.Log($"{nameof(BasisVideoPlayerNetworking)} drift-corrected by {diff:F2}s.", BasisDebug.LogTag.Video);
        }
        catch (NotSupportedException)
        {
            // Live / non-seekable sources can't drift-correct; that's fine.
        }
    }

    private void BroadcastFullState()
    {
        // Defer until the framework has assigned us a NetworkID — otherwise
        // SendCustomNetworkEvent logs an error and drops the message.
        if (!HasNetworkID)
        {
            sendOnNetworkReady = true;
            return;
        }
        SendCustomNetworkEvent(Serialize(), DeliveryMethod.ReliableOrdered);
    }

    private void SendFullStateTo(ushort[] recipients)
    {
        if (!HasNetworkID) return;
        SendCustomNetworkEvent(Serialize(), DeliveryMethod.ReliableOrdered, recipients);
    }

    private SyncedPlaybackState GetLocalState()
    {
        if (!videoPlayer.IsPlaying) return SyncedPlaybackState.Stopped;
        return videoPlayer.IsPaused ? SyncedPlaybackState.Paused : SyncedPlaybackState.Playing;
    }

    private string GetActiveUrl()
    {
        var media = videoPlayer.ActiveMediaSource;
        if (media != null && !string.IsNullOrEmpty(media.Uri)) return media.Uri;
        return currentSyncedUrl ?? string.Empty;
    }

    private byte[] Serialize()
    {
        string url = GetActiveUrl();
        byte[] urlBytes = string.IsNullOrEmpty(url) ? Array.Empty<byte>() : UrlEncoding.GetBytes(url);
        if (urlBytes.Length > ushort.MaxValue)
        {
            BasisDebug.LogError($"{nameof(BasisVideoPlayerNetworking)} URL exceeds {ushort.MaxValue} bytes; truncating.", BasisDebug.LogTag.Video);
            Array.Resize(ref urlBytes, ushort.MaxValue);
        }
        var buf = new byte[HeaderSize + urlBytes.Length];
        buf[0] = (byte)GetLocalState();
        WriteLong(buf, 1, videoPlayer.Position.Ticks);
        WriteUShort(buf, 9, (ushort)urlBytes.Length);
        if (urlBytes.Length > 0) Buffer.BlockCopy(urlBytes, 0, buf, HeaderSize, urlBytes.Length);
        return buf;
    }

    private static bool TryDeserialize(byte[] buffer, out string url, out SyncedPlaybackState state, out long positionTicks)
    {
        url = string.Empty;
        state = SyncedPlaybackState.Stopped;
        positionTicks = 0;
        if (buffer == null || buffer.Length < HeaderSize) return false;
        byte stateByte = buffer[0];
        if (stateByte > (byte)SyncedPlaybackState.Paused) return false;
        state = (SyncedPlaybackState)stateByte;
        positionTicks = ReadLong(buffer, 1);
        ushort urlLen = ReadUShort(buffer, 9);
        if (buffer.Length < HeaderSize + urlLen) return false;
        if (urlLen > 0) url = UrlEncoding.GetString(buffer, HeaderSize, urlLen);
        return true;
    }

    private static void WriteLong(byte[] buf, int offset, long value)
    {
        for (int i = 0; i < 8; i++) buf[offset + i] = (byte)(value >> (i * 8));
    }

    private static long ReadLong(byte[] buf, int offset)
    {
        long v = 0;
        for (int i = 0; i < 8; i++) v |= (long)buf[offset + i] << (i * 8);
        return v;
    }

    private static void WriteUShort(byte[] buf, int offset, ushort value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
    }

    private static ushort ReadUShort(byte[] buf, int offset)
    {
        return (ushort)(buf[offset] | (buf[offset + 1] << 8));
    }
}
