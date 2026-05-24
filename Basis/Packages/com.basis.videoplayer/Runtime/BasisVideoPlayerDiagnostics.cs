using System;
using System.IO;
using System.Text;
using UnityEngine;

[RequireComponent(typeof(BasisVideoPlayer))]
public sealed class BasisVideoPlayerDiagnostics : MonoBehaviour
{
    [Header("Logging")]
    [Tooltip("If true, begin writing diagnostics CSV on Enable. Disable to gate logging behind a manual StartLogging() call.")]
    public bool AutoStart = true;

    [Tooltip("Snapshot rate in samples per second. 50 Hz (20 ms) is dense enough to see audio-callback chunking.")]
    [Min(1f)] public float SnapshotsPerSecond = 50f;

    [Tooltip("Maximum snapshot rows kept in the in-memory buffer before forcing a disk flush.")]
    [Min(64)] public int FlushEveryNSnapshots = 200;

    [Tooltip("If true, the CSV is appended to between Play sessions; if false, the file is truncated on each StartLogging() call.")]
    public bool AppendBetweenSessions = false;

    [Tooltip("Override for the output path. Sandboxed to Application.persistentDataPath; absolute paths outside that root are rejected. Leave empty to use Application.persistentDataPath/BasisVideoPlayerDiag.csv.")]
    public string LogPathOverride = "";

    public string ResolvedLogPath { get; private set; }
    public bool IsLogging { get; private set; }
    public long SnapshotsWritten { get; private set; }

    private BasisVideoPlayer player;
    private BasisVideoPlayerAudio audioComponent;
    private StreamWriter writer;
    private StringBuilder lineBuilder;
    private float nextSnapshotTime;
    private int rowsSinceFlush;

    private void Awake()
    {
        player = GetComponent<BasisVideoPlayer>();
        TryGetComponent(out audioComponent);
        lineBuilder = new StringBuilder(512);
        ResolvedLogPath = ResolvePath();
    }

    private void OnEnable()
    {
        if (AutoStart) StartLogging();
    }

    private void OnDisable()
    {
        StopLogging();
    }

    public void StartLogging()
    {
        if (IsLogging) return;
        ResolvedLogPath = ResolvePath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResolvedLogPath) ?? ".");
            bool fileExists = File.Exists(ResolvedLogPath);
            var fs = new FileStream(ResolvedLogPath,
                AppendBetweenSessions ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.Read);
            writer = new StreamWriter(fs, new UTF8Encoding(false));
            if (!fileExists || !AppendBetweenSessions)
            {
                writer.WriteLine(BuildHeader());
            }
            else
            {
                writer.WriteLine("# --- session " + DateTime.UtcNow.ToString("o") + " ---");
            }
            writer.Flush();
            IsLogging = true;
            SnapshotsWritten = 0;
            rowsSinceFlush = 0;
            nextSnapshotTime = Time.realtimeSinceStartup;
            BasisDebug.Log($"BasisVideoPlayerDiagnostics: logging to {ResolvedLogPath}", BasisDebug.LogTag.Video);
        }
        catch (Exception ex)
        {
            BasisDebug.LogError($"BasisVideoPlayerDiagnostics: failed to open log: {ex.Message}", BasisDebug.LogTag.Video);
            writer = null;
            IsLogging = false;
        }
    }

    public void StopLogging()
    {
        if (!IsLogging) return;
        IsLogging = false;
        try
        {
            writer?.Flush();
            writer?.Dispose();
        }
        catch { }
        writer = null;
    }

    public void Flush()
    {
        try { writer?.Flush(); rowsSinceFlush = 0; } catch { }
    }

    private void Update()
    {
        if (!IsLogging || writer == null) return;
        float now = Time.realtimeSinceStartup;
        if (now < nextSnapshotTime) return;
        float interval = 1f / Mathf.Max(1f, SnapshotsPerSecond);
        nextSnapshotTime = now + interval;

        WriteSnapshotRow(now);

        rowsSinceFlush++;
        if (rowsSinceFlush >= FlushEveryNSnapshots)
        {
            try { writer.Flush(); rowsSinceFlush = 0; } catch { }
        }
    }

    private string ResolvePath()
    {
        if (string.IsNullOrEmpty(LogPathOverride))
            return Path.Combine(Application.persistentDataPath, "BasisVideoPlayerDiag.csv");
        if (!BasisVideoPlayerSecurity.TrySandboxLogPath(LogPathOverride, out string sandboxed, out string reason))
        {
            BasisDebug.LogWarning($"BasisVideoPlayerDiagnostics: LogPathOverride rejected ({reason}); falling back to default.", BasisDebug.LogTag.Video);
            return Path.Combine(Application.persistentDataPath, "BasisVideoPlayerDiag.csv");
        }
        return sandboxed;
    }

    private string BuildHeader()
    {
        return string.Join(",",
            "unity_time_s",
            "is_playing",
            "is_paused",
            "prepared",
            "backend",
            "clock_now_us",
            "video_w",
            "video_h",
            "engine_state",
            "engine_pos_us",
            "engine_has_texture",
            "cpu_queue_depth",
            "cpu_presented",
            "cpu_overflow_drops",
            "cpu_catchup_skips",
            "cpu_late_skips",
            "cpu_format_errors",
            "audio_present",
            "audio_source_playing",
            "audio_consumed_samples",
            "audio_queue_depth",
            "audio_dropped",
            "audio_mute",
            "audio_gain",
            "audio_volume",
            "audio_spatial",
            "pcm_peak",
            "pcm_rms",
            "listener_paused"
        );
    }

    private void WriteSnapshotRow(float unityTime)
    {
        lineBuilder.Length = 0;

        var clock = player.Clock;
        var eng = player.NativeEngine;
        var src = player.Source;
        var aud = audioComponent != null ? audioComponent : player.AudioComponent;

        AppendF(unityTime);
        AppendB(player.IsPlaying);
        AppendB(player.IsPaused);
        AppendB(player.IsPrepared);
        AppendStr(eng != null ? "native" : (src != null ? src.GetType().Name : "none"));
        AppendL(clock.CurrentMediaTimeUs);
        AppendI(player.VideoSize.x);
        AppendI(player.VideoSize.y);
        AppendStr(eng != null ? eng.State.ToString() : "");
        AppendL(eng != null ? eng.PositionUs : 0);
        AppendB(eng != null && eng.OutputTexture != null);
        AppendI(player.QueuedFrameCount);
        AppendL(player.PresentedFrameCount);
        AppendL(player.OverflowDropCount);
        AppendL(player.CatchUpSkipCount);
        AppendL(player.LateSkipCount);
        AppendL(player.FormatErrorCount);
        AppendB(aud != null);
        AppendB(aud != null && aud.ActiveAudioSource != null && aud.ActiveAudioSource.isPlaying);
        AppendL(aud != null ? aud.ConsumedSampleCount : 0);
        AppendI(aud != null ? aud.QueuedFrameCount : 0);
        AppendL(aud != null ? aud.DroppedFrameCount : 0);
        AppendB(aud != null && aud.Mute);
        AppendF(aud != null ? aud.VolumeGain : 0f);
        var src2 = aud != null ? aud.ActiveAudioSource : null;
        AppendF(src2 != null ? src2.volume : 0f);
        AppendF(src2 != null ? src2.spatialBlend : 0f);
        AppendF(aud != null ? aud.LastPcmPeak : 0f);
        AppendF(aud != null ? aud.LastPcmRms : 0f);
        AppendB(AudioListener.pause, last: true);

        try
        {
            writer.WriteLine(lineBuilder.ToString());
            SnapshotsWritten++;
        }
        catch (Exception ex)
        {
            BasisDebug.LogError($"BasisVideoPlayerDiagnostics: write failed: {ex.Message}", BasisDebug.LogTag.Video);
            IsLogging = false;
        }
    }

    private void AppendF(float v, bool last = false) { lineBuilder.Append(v.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)); if (!last) lineBuilder.Append(','); }
    private void AppendI(int v, bool last = false) { lineBuilder.Append(v); if (!last) lineBuilder.Append(','); }
    private void AppendL(long v, bool last = false) { lineBuilder.Append(v); if (!last) lineBuilder.Append(','); }
    private void AppendB(bool v, bool last = false) { lineBuilder.Append(v ? "1" : "0"); if (!last) lineBuilder.Append(','); }
    private void AppendStr(string v, bool last = false) { if (v != null) lineBuilder.Append(v); if (!last) lineBuilder.Append(','); }
}
