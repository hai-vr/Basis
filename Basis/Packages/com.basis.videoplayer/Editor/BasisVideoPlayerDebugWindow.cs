using System;
using UnityEditor;
using UnityEngine;

public class BasisVideoPlayerDebugWindow : EditorWindow
{
    private Vector2 _scrollPos;
    private bool _showPlayer = true;
    private bool _showSource = true;
    private bool _showVideoDecode = true;
    private bool _showVideoQueue = true;
    private bool _showClock = true;
    private bool _showAudioDecode = true;
    private bool _showAudioQueue = true;
    private bool _showAudioOutput = true;

    private BasisVideoPlayer _target;

    // Native fps sampling (counters are monotonic; we differentiate over wall time).
    private double _fpsLastTime;
    private ulong _fpsLastDecoded;
    private long _fpsLastPresented;
    private long _fpsLastRendered;
    private float _decodedFps;
    private float _displayedFps;
    private float _renderFps;

    private static readonly Color BarColor = new Color(0.2f, 0.7f, 1f, 0.8f);
    private static readonly Color BarBgColor = new Color(0.15f, 0.15f, 0.15f, 1f);
    private static readonly Color GoodColor = new Color(0.3f, 0.9f, 0.3f);
    private static readonly Color WarnColor = new Color(1f, 0.8f, 0.2f);
    private static readonly Color ErrorColor = new Color(1f, 0.3f, 0.3f);
    private static readonly Color VideoBarColor = new Color(0.2f, 0.7f, 1f, 0.85f);
    private static readonly Color AudioBarColor = new Color(0.9f, 0.5f, 0.2f, 0.85f);

    [MenuItem("Basis/Debug/Video Player Debug")]
    public static void ShowWindow()
    {
        var w = GetWindow<BasisVideoPlayerDebugWindow>("Video Player Debug");
        w.minSize = new Vector2(440, 600);
    }

    private void OnEnable() { EditorApplication.update += Repaint; }
    private void OnDisable() { EditorApplication.update -= Repaint; }

    private void OnGUI()
    {
        DrawHeader();

        if (_target == null && Application.isPlaying)
        {
            _target = FindFirstObjectByTypeSafe();
        }

        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        if (_target == null)
        {
            EditorGUILayout.HelpBox(
                "No BasisVideoPlayer assigned and none found in the scene.\nAssign one above or enter Play Mode with a scene that has one.",
                MessageType.Info);
        }
        else
        {
            _showPlayer = DrawSection("1. Player", _showPlayer, DrawPlayerSection);
            _showSource = DrawSection("2. Source / Transport", _showSource, DrawSourceSection);
            _showVideoDecode = DrawSection("3. Video Decode", _showVideoDecode, DrawVideoDecodeSection);
            _showVideoQueue = DrawSection("4. Video Queue", _showVideoQueue, DrawVideoQueueSection);
            _showClock = DrawSection("5. A/V Sync Clock", _showClock, DrawClockSection);
            _showAudioDecode = DrawSection("6. Audio Decode", _showAudioDecode, DrawAudioDecodeSection);
            _showAudioQueue = DrawSection("7. Audio Queue", _showAudioQueue, DrawAudioQueueSection);
            _showAudioOutput = DrawSection("8. Audio Output", _showAudioOutput, DrawAudioOutputSection);
        }

        EditorGUILayout.EndScrollView();
    }

    private void DrawHeader()
    {
        EditorGUILayout.LabelField("Video Player Pipeline", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Source -> Packet -> Decode -> Queue -> Render / Audio Out",
            EditorStyles.miniLabel);
        EditorGUILayout.Space(2);
        _target = (BasisVideoPlayer)EditorGUILayout.ObjectField(
            "Player", _target, typeof(BasisVideoPlayer), allowSceneObjects: true);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode for live counters.", MessageType.Info);
        }

        DrawDiagnosticsControls();
        EditorGUILayout.Space(4);
    }

    private void DrawDiagnosticsControls()
    {
        if (_target == null) return;

        var diag = _target.GetComponent<BasisVideoPlayerDiagnostics>();

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Diagnostics CSV", GUILayout.Width(EditorGUIUtility.labelWidth));

        if (diag == null)
        {
            if (GUILayout.Button("Attach Logger"))
            {
                Undo.AddComponent<BasisVideoPlayerDiagnostics>(_target.gameObject);
            }
        }
        else
        {
            if (Application.isPlaying)
            {
                if (diag.IsLogging)
                {
                    if (GUILayout.Button("Stop")) diag.StopLogging();
                    if (GUILayout.Button("Flush")) diag.Flush();
                }
                else
                {
                    if (GUILayout.Button("Start")) diag.StartLogging();
                }
            }
            if (GUILayout.Button("Reveal"))
            {
                string p = diag.ResolvedLogPath;
                if (!string.IsNullOrEmpty(p) && System.IO.File.Exists(p))
                {
                    EditorUtility.RevealInFinder(p);
                }
                else
                {
                    EditorUtility.RevealInFinder(System.IO.Path.GetDirectoryName(p) ?? Application.persistentDataPath);
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        if (diag != null)
        {
            EditorGUILayout.LabelField("  Path", string.IsNullOrEmpty(diag.ResolvedLogPath) ? "(unset)" : diag.ResolvedLogPath, EditorStyles.miniLabel);
            if (Application.isPlaying)
            {
                EditorGUILayout.LabelField("  Snapshots", diag.SnapshotsWritten.ToString(), EditorStyles.miniLabel);
            }
        }
    }

    private BasisVideoPlayer FindFirstObjectByTypeSafe()
    {
#if UNITY_2023_1_OR_NEWER
        return FindFirstObjectByType<BasisVideoPlayer>(FindObjectsInactive.Include);
#else
        var all = Resources.FindObjectsOfTypeAll<BasisVideoPlayer>();
        for (int i = 0; i < all.Length; i++)
        {
            var p = all[i];
            if (p != null && p.gameObject.scene.IsValid()) return p;
        }
        return null;
#endif
    }

    private void DrawPlayerSection()
    {
        StatusLabel("Playing", _target.IsPlaying);
        StatusLabel("Paused", _target.IsPaused);

        EditorGUILayout.LabelField("Frames Presented", _target.PresentedFrameCount.ToString());
        EditorGUILayout.LabelField("Queue Length Max", _target.MaxQueueLength.ToString());
        EditorGUILayout.LabelField("Late-Frame Skip", $"{_target.LateFrameSkipUs} us");
        EditorGUILayout.LabelField("Presentation Offset", $"{_target.PresentationOffsetUs} us");
        EditorGUILayout.LabelField("Overflow Policy", _target.OverflowPolicy.ToString());

        if (_target.Source == null)
        {
            EditorGUILayout.HelpBox(
                "Player has no Source assigned. Add a BasisVideoPlayerStreaming or BasisVideoPlayerSynthetic on the same GameObject, or assign player.Source from code.",
                MessageType.Warning);
        }
        else if (Application.isPlaying && _target.IsPlaying && _target.PresentedFrameCount == 0 && _target.QueuedFrameCount == 0)
        {
            EditorGUILayout.HelpBox(
                "Playing but no frames have arrived or been presented. Check the source/transport status below.",
                MessageType.Warning);
        }
    }

    private void DrawSourceSection()
    {
        var eng = _target.NativeEngine;
        if (eng != null)
        {
            EditorGUILayout.LabelField("Backend", "OS-codec engine (zero-copy)");
            EditorGUILayout.LabelField("URL", eng.Url);
            StatusLabel("Running", eng.IsRunning);
            EditorGUILayout.LabelField("State", eng.State.ToString());
            var sz = eng.VideoSize;
            EditorGUILayout.LabelField("Video Size", sz.x > 0 ? $"{sz.x} x {sz.y}" : "(unknown)");
            EditorGUILayout.LabelField("Position", FormatUs(eng.PositionUs < 0 ? 0 : eng.PositionUs));
            StatusLabel("Output Texture", eng.OutputTexture != null);

            if (Application.isPlaying && eng.State == BasisMediaEngineState.Error)
            {
                EditorGUILayout.HelpBox(
                    "Engine reported an error — see the Console for the message, and verify basis_media_native is built and present under Plugins/.",
                    MessageType.Error);
            }
            else if (Application.isPlaying && eng.IsRunning && eng.OutputTexture == null)
            {
                EditorGUILayout.HelpBox(
                    "Engine running but no frame published yet (connecting/buffering), or the native library isn't producing frames.",
                    MessageType.Info);
            }
            return;
        }

        var source = _target.Source;
        if (source == null)
        {
            EditorGUILayout.LabelField("(no source assigned)");
            return;
        }

        EditorGUILayout.LabelField("Type", source.GetType().Name);
        StatusLabel("Running", source.IsRunning);

        if (source is BasisSyntheticTestSource synth)
        {
            EditorGUILayout.LabelField("Resolution", $"{synth.Width} x {synth.Height}");
            EditorGUILayout.LabelField("FPS Target", synth.FramesPerSecond.ToString());
            EditorGUILayout.LabelField("Pattern", synth.PatternMode.ToString());
            EditorGUILayout.LabelField("Frames Emitted", synth.FramesEmitted.ToString());
        }
    }

    private void DrawVideoDecodeSection()
    {
        var eng = _target.NativeEngine;
        if (eng == null)
        {
            EditorGUILayout.LabelField("(CPU source — OS hardware decode not in use)");
            return;
        }

        EditorGUILayout.LabelField("Decoder", "OS hardware (Media Foundation / MediaCodec)");
        var sz = eng.VideoSize;
        EditorGUILayout.LabelField("Decoded Size", sz.x > 0 ? $"{sz.x} x {sz.y}" : "(none yet)");
        EditorGUILayout.LabelField("Last PTS", FormatUs(eng.PositionUs < 0 ? 0 : eng.PositionUs));
        var tex = eng.OutputTexture;
        EditorGUILayout.LabelField("Output Texture", tex != null ? $"{tex.width} x {tex.height}" : "(none)");

        if (Application.isPlaying && eng.IsRunning && tex == null)
        {
            EditorGUILayout.HelpBox(
                "No decoded frame yet. Check that basis_media_native is present for this platform/arch and that the stream URL is reachable.",
                MessageType.Warning);
        }
    }

    // Differentiates the engine's monotonic counters over wall time to get fps.
    // Decoded comes from the frame counter; displayed/render are parsed out of the
    // native debug string ("copy=" / "render="). Resets cleanly on a new stream.
    private void UpdateNativeFps(BasisNativeVideoSource eng, string dbg)
    {
        double now = EditorApplication.timeSinceStartup;
        ulong decoded = eng.DecodedFrameCount;
        long presented = ParseCounter(dbg, "copy=");
        long rendered = ParseCounter(dbg, "render=");

        if (_fpsLastTime <= 0 || now < _fpsLastTime || decoded < _fpsLastDecoded)
        {
            _fpsLastTime = now;
            _fpsLastDecoded = decoded;
            _fpsLastPresented = presented;
            _fpsLastRendered = rendered;
            return;
        }

        double dt = now - _fpsLastTime;
        if (dt < 0.5) return;

        _decodedFps = (float)((decoded - _fpsLastDecoded) / dt);
        _displayedFps = (float)(Math.Max(0L, presented - _fpsLastPresented) / dt);
        _renderFps = (float)(Math.Max(0L, rendered - _fpsLastRendered) / dt);
        _fpsLastTime = now;
        _fpsLastDecoded = decoded;
        _fpsLastPresented = presented;
        _fpsLastRendered = rendered;
    }

    private void DrawFpsLine(string label, float fps)
    {
        var prev = GUI.contentColor;
        GUI.contentColor = fps >= 24f ? GoodColor : (fps >= 12f ? WarnColor : ErrorColor);
        EditorGUILayout.LabelField(label, $"{fps:F1} fps");
        GUI.contentColor = prev;
    }

    private static long ParseCounter(string s, string key)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int i = s.IndexOf(key, StringComparison.Ordinal);
        if (i < 0) return 0;
        i += key.Length;
        int start = i;
        if (i < s.Length && (s[i] == '-' || s[i] == '+')) i++;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        return (i > start && long.TryParse(s.Substring(start, i - start), out long v)) ? v : 0;
    }

    private void DrawVideoQueueSection()
    {
        var eng = _target.NativeEngine;
        if (eng != null)
        {
            string dbg = eng.DebugInfo;
            UpdateNativeFps(eng, dbg);

            EditorGUILayout.LabelField("Backend", "OS-codec PTS-paced ring (no CPU queue)");
            EditorGUILayout.LabelField("State", eng.State.ToString());
            long ttffMs = ParseCounter(dbg, "ttff=");
            EditorGUILayout.LabelField("Time to first frame", ttffMs >= 0 ? $"{ttffMs} ms" : "— (connecting)");

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Framerate", EditorStyles.boldLabel);
            DrawFpsLine("Decoded", _decodedFps);
            DrawFpsLine("Displayed", _displayedFps);
            DrawFpsLine("Render calls", _renderFps);

            EditorGUILayout.Space(2);
            var esz = eng.VideoSize;
            EditorGUILayout.LabelField("Decoded Size", esz.x > 0 ? $"{esz.x} x {esz.y}" : "(unknown)");
            EditorGUILayout.LabelField("Position", FormatUs(eng.PositionUs < 0 ? 0 : eng.PositionUs));

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Playback buffer", EditorStyles.boldLabel);
            long bufMs = ParseCounter(dbg, "buf=");
            long lagMs = ParseCounter(dbg, "lag=");
            EditorGUILayout.LabelField("Buffered ahead", $"{lagMs} ms");
            EditorGUILayout.LabelField($"Target ({_target.BufferMode})", $"{bufMs} ms");
            Rect bufRect = GUILayoutUtility.GetRect(0, 18, GUILayout.ExpandWidth(true));
            float bufFill = bufMs > 0 ? (float)lagMs / bufMs : 0f;
            Color bufCol = bufFill >= 0.75f ? GoodColor : (bufFill >= 0.4f ? WarnColor : ErrorColor);
            DrawBar(bufRect, bufFill, $"{lagMs} / {bufMs} ms", bufCol);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Native counters", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(dbg ?? "(no data — is the plugin built?)", MessageType.None);
            EditorGUILayout.LabelField("  blit=decoded  copy=presented  lag=ms behind live", EditorStyles.miniLabel);
            return;
        }

        int depth = _target.QueuedFrameCount;
        int max = Mathf.Max(1, _target.MaxQueueLength);
        float fill = (float)depth / max;

        EditorGUILayout.LabelField("Queued / Max", $"{depth} / {max}");
        Rect r = GUILayoutUtility.GetRect(0, 16, GUILayout.ExpandWidth(true));
        DrawBar(r, fill, $"{depth} frames", VideoBarColor);

        EditorGUILayout.LabelField("Overflow Policy", _target.OverflowPolicy.ToString());

        long clockNow = _target.Clock.CurrentMediaTimeUs;
        long headPts = _target.HeadFramePtsUs;
        long tailPts = _target.TailFramePtsUs;

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Queue timing", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("  Clock now", FormatUs(clockNow));
        if (headPts >= 0)
        {
            long headDelta = headPts - clockNow;
            EditorGUILayout.LabelField("  Head PTS", FormatUs(headPts) + $"  ({DeltaLabel(headDelta)})");
        }
        else
        {
            EditorGUILayout.LabelField("  Head PTS", "(empty)");
        }
        if (tailPts > 0)
        {
            long tailDelta = tailPts - clockNow;
            EditorGUILayout.LabelField("  Tail PTS (last enqueued)", FormatUs(tailPts) + $"  ({DeltaLabel(tailDelta)})");
            EditorGUILayout.LabelField("  Queue span", FormatUs(tailPts - (headPts < 0 ? tailPts : headPts)));
        }

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Drop categories", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("  Overflow (queue full)", _target.OverflowDropCount.ToString());
        EditorGUILayout.LabelField("  Late-skip (>" + _target.LateFrameSkipUs + "us behind)", _target.LateSkipCount.ToString());
        EditorGUILayout.LabelField("  Format-error", _target.FormatErrorCount.ToString());
        EditorGUILayout.LabelField("  Catch-up coalesce (informational)", _target.CatchUpSkipCount.ToString());

        long overflow = _target.OverflowDropCount;
        long catchup = _target.CatchUpSkipCount;
        long late = _target.LateSkipCount;

        if (Application.isPlaying && depth >= max && overflow > 0)
        {
            bool headInFuture = headPts > clockNow + 50_000; // >50 ms ahead
            bool clockMaybeStalled = clockNow == 0 && _target.IsPlaying && depth > 0;

            if (clockMaybeStalled)
            {
                EditorGUILayout.HelpBox(
                    "Queue full + Clock stuck at 0. The A/V clock never anchored. Most common cause: audio is wired as the master clock but the AudioSource isn't actually playing (or no streaming clip was assigned), so HasMediaTime stays false AND the local wall-clock anchor isn't running either. Verify section 8 (Audio Output) shows 'Is Playing: YES' and 'Has Media Anchor: YES'.",
                    MessageType.Error);
            }
            else if (headInFuture)
            {
                EditorGUILayout.HelpBox(
                    $"Queue is full of FUTURE frames (head is {DeltaLabel(headPts - clockNow)}). The server is sending faster than real time — PTSs span more wall-time than has elapsed.\n" +
                    "Fixes, in order of preference:\n" +
                    "  1. Throttle the server to real-time pacing (sleep between packets to match frame duration).\n" +
                    "  2. If the server can't be changed: set OverflowPolicy = DropNewest so the FAR-future frames are shed instead of near-due ones (DropOldest discards the frames you're about to show).\n" +
                    "  3. Raise MaxQueueLength to absorb the burst (memory cost = MaxQueueLength × frame size).",
                    MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Queue full and overflow drops accumulating, but head PTS is near the clock — frames are arriving in tight bursts faster than Update() can drain them. Raise MaxQueueLength (each unit ≈ 1/fps seconds of buffer).",
                    MessageType.Warning);
            }
        }
        else if (Application.isPlaying && catchup > _target.PresentedFrameCount && catchup > 30)
        {
            EditorGUILayout.HelpBox(
                "Catch-up coalesce dominates: more frames were walked-past than presented. This is normal when frames arrive bursty over network, OR when the clock occasionally jumps forward (audio buffer refill). Not a data loss — earlier frames were decoded successfully, only the most recent is rendered.",
                MessageType.Info);
        }
        else if (Application.isPlaying && late > 0)
        {
            EditorGUILayout.HelpBox(
                "Late-skip is firing: frames arrived too far behind the clock (set by LateFrameSkipUs). Either the producer is delayed, or the clock has jumped ahead. Set LateFrameSkipUs=0 to disable, or raise it.",
                MessageType.Warning);
        }
    }

    private static string DeltaLabel(long deltaUs)
    {
        if (deltaUs > 0) return $"+{FormatUs(deltaUs)} future";
        if (deltaUs < 0) return $"-{FormatUs(-deltaUs)} past";
        return "now";
    }

    private void DrawClockSection()
    {
        var clock = _target.Clock;
        StatusLabel("Started", clock.IsStarted);
        EditorGUILayout.LabelField("Current Media Time", FormatUs(clock.CurrentMediaTimeUs));
        bool hasExternal = clock.ExternalSource != null;
        StatusLabel("External Source Wired", hasExternal);
        if (hasExternal)
        {
            StatusLabel("External Has Media Time", clock.ExternalSource.HasMediaTime);
            EditorGUILayout.LabelField("External Media Time", FormatUs(clock.ExternalSource.CurrentMediaTimeUs));
            EditorGUILayout.LabelField("External Type", clock.ExternalSource.GetType().Name);
        }
    }

    private BasisVideoPlayerAudio GetAudio() => _target.AudioComponent;

    private void DrawAudioDecodeSection()
    {
        var eng = _target.NativeEngine;
        if (eng == null)
        {
            EditorGUILayout.LabelField("(CPU source — no OS audio decode)");
            return;
        }

        EditorGUILayout.LabelField("Decoder", "OS hardware AAC");
        if (eng.TryGetPcmFormat(out int sr, out int ch))
            EditorGUILayout.LabelField("Format", $"{sr} Hz / {ch} ch");
        else
            EditorGUILayout.LabelField("Format", "(pending first audio frame)");
    }

    private void DrawAudioQueueSection()
    {
        var audio = GetAudio();
        var eng = _target.NativeEngine;
        if (eng != null)
        {
            EditorGUILayout.LabelField("Backend", "OS-codec PCM ring (pulled on audio thread)");
            if (eng.TryGetPcmFormat(out int sr, out int ch)) EditorGUILayout.LabelField("Format", $"{sr} Hz / {ch} ch");
            else EditorGUILayout.LabelField("Format", "(pending first audio frame)");
            if (audio != null)
            {
                var asrc = audio.ActiveAudioSource;
                StatusLabel("Source Playing", asrc != null && asrc.isPlaying);
                EditorGUILayout.LabelField("PCM peak / rms", $"{audio.LastPcmPeak:F3} / {audio.LastPcmRms:F3}");
            }
            else
            {
                EditorGUILayout.HelpBox("No BasisVideoPlayerAudio on the GameObject — add one (with an AudioSource) for sound.", MessageType.Info);
            }
            return;
        }

        if (audio == null)
        {
            EditorGUILayout.LabelField("(no BasisVideoPlayerAudio on player's GameObject)");
            return;
        }

        int depth = audio.QueuedFrameCount;
        int max = audio.MaxQueuedFrames > 0 ? audio.MaxQueuedFrames : Mathf.Max(1, depth + 1);
        float fill = audio.MaxQueuedFrames > 0 ? (float)depth / max : 0f;

        EditorGUILayout.LabelField("Queued / Max", audio.MaxQueuedFrames > 0
            ? $"{depth} / {max}"
            : $"{depth} / unbounded");
        Rect r = GUILayoutUtility.GetRect(0, 16, GUILayout.ExpandWidth(true));
        DrawBar(r, audio.MaxQueuedFrames > 0 ? fill : Mathf.Min(1f, depth / 64f), $"{depth} frames", AudioBarColor);

        EditorGUILayout.LabelField("Dropped (total)", audio.DroppedFrameCount.ToString());
        EditorGUILayout.LabelField("Drop Policy", audio.DropOldestOnOverflow ? "DropOldest" : "DropNewest");
    }

    private void DrawAudioOutputSection()
    {
        var audio = GetAudio();
        if (audio == null)
        {
            EditorGUILayout.LabelField("(no BasisVideoPlayerAudio component)");
            return;
        }

        var src = audio.ActiveAudioSource;
        if (src == null)
        {
            EditorGUILayout.HelpBox(
                "BasisVideoPlayerAudio has no AudioSource. Assign TargetAudioSource or add an AudioSource to the GameObject.",
                MessageType.Error);
            return;
        }

        EditorGUILayout.LabelField("AudioSource", src.name);
        StatusLabel("Is Playing", src.isPlaying);
        EditorGUILayout.LabelField("Volume", src.volume.ToString("F2"));
        EditorGUILayout.LabelField("Spatial Blend", src.spatialBlend.ToString("F2"));
        StatusLabel("Mute (sink)", audio.Mute);
        EditorGUILayout.LabelField("Gain (sink)", audio.VolumeGain.ToString("F2"));

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Clip", audio.StreamingClip != null ? audio.StreamingClip.name : "(none)");
        EditorGUILayout.LabelField("Format", $"{audio.SampleRate} Hz / {audio.ChannelCount} ch");
        EditorGUILayout.LabelField("Consumed Samples", audio.ConsumedSampleCount.ToString());

        StatusLabel("Has Media Anchor", audio.HasMediaTime);
        EditorGUILayout.LabelField("Audio Media Time", FormatUs(audio.CurrentMediaTimeUs));

        if (Application.isPlaying && src.isPlaying && audio.ConsumedSampleCount == 0 && audio.QueuedFrameCount > 0)
        {
            EditorGUILayout.HelpBox(
                "AudioSource reports playing and frames are queued, but the PCM callback hasn't been driven. The streaming AudioClip may not be assigned (AssignClipOnAwake?) or the AudioSource is muted at the mixer.",
                MessageType.Warning);
        }
    }

    private bool DrawSection(string title, bool expanded, Action drawContent)
    {
        expanded = EditorGUILayout.BeginFoldoutHeaderGroup(expanded, title);
        if (expanded)
        {
            EditorGUI.indentLevel++;
            drawContent();
            EditorGUI.indentLevel--;
        }
        EditorGUILayout.EndFoldoutHeaderGroup();
        EditorGUILayout.Space(2);
        return expanded;
    }

    private void StatusLabel(string label, bool value)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label, GUILayout.Width(EditorGUIUtility.labelWidth));
        var prev = GUI.color;
        GUI.color = value ? GoodColor : ErrorColor;
        EditorGUILayout.LabelField(value ? "YES" : "NO", EditorStyles.boldLabel);
        GUI.color = prev;
        EditorGUILayout.EndHorizontal();
    }

    private void DrawBar(Rect rect, float fill, string label, Color? color = null)
    {
        fill = Mathf.Clamp01(fill);
        EditorGUI.DrawRect(rect, BarBgColor);
        Rect filled = new Rect(rect.x, rect.y, rect.width * fill, rect.height);
        EditorGUI.DrawRect(filled, color ?? BarColor);
        EditorGUI.LabelField(rect, $"  {label}", EditorStyles.miniLabel);
    }

    private static string FormatBytes(long bytes)
    {
        const long KiB = 1024;
        const long MiB = 1024 * KiB;
        const long GiB = 1024 * MiB;
        if (bytes >= GiB) return $"{bytes / (double)GiB:F2} GiB";
        if (bytes >= MiB) return $"{bytes / (double)MiB:F2} MiB";
        if (bytes >= KiB) return $"{bytes / (double)KiB:F2} KiB";
        return $"{bytes} B";
    }

    private static string FormatUs(long us)
    {
        if (us <= 0) return "0 us";
        long ms = us / 1000;
        if (ms < 1000) return $"{us} us ({ms} ms)";
        double s = us / 1_000_000.0;
        return $"{us} us ({s:F3} s)";
    }
}
