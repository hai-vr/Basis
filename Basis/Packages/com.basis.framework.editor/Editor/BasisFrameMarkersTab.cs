using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;
using UnityProfiler = UnityEngine.Profiling.Profiler;

/// <summary>
/// Rolling per-frame capture of every Basis profiler marker (BasisDriver.*, BasisEerie.*, the Basis
/// Burst jobs, plus the engine's JobHandle.Complete / WaitForJobGroupID waits), recorded through
/// ProfilerRecorder so it works in play mode without the Profiler window. Copy Summary produces the
/// paste-friendly table (avg / last / peak / calls over the recorded window); Copy CSV produces the
/// full per-frame history for offline diffing.
/// </summary>
public sealed class BasisFrameMarkersTab : BasisEditorTabPage
{
    const int k_History = 600;                    // ~10s at 60fps editor pumping
    const double k_NsToMs = 1.0 / 1_000_000.0;

    // Every drawn row is five IMGUI text elements, measured and drawn again on both the Layout and
    // the Repaint pass. A fully instrumented frame matches ~190 markers, and drawing all of them
    // cost more main-thread time than everything the table was measuring. Rows are sorted by average
    // descending, so the ones past the top slice are the ones nobody reads; Copy Summary and Copy
    // Frames CSV still emit every matched marker.
    const int k_DefaultRowsDrawn = 40;
    const int k_MinRowsDrawn = 5;

    /// <summary>
    /// SumAllSamplesInFrame is what makes a recorder produce one data point per frame holding that
    /// frame's total for the marker. Without it the recorder stores individual samples, so a marker
    /// hit once per job batch reports one batch instead of the frame and the worker-thread markers
    /// never add up to the profiler's accumulated view.
    /// </summary>
    const ProfilerRecorderOptions k_MarkerOptions =
        ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
        ProfilerRecorderOptions.StartImmediately |
        ProfilerRecorderOptions.SumAllSamplesInFrame;

    const ProfilerRecorderOptions k_CounterOptions =
        ProfilerRecorderOptions.WrapAroundWhenCapacityReached |
        ProfilerRecorderOptions.StartImmediately;

    // AvgMs/AvgCalls are maintained as running sums over the ring rather than recomputed, because
    // the table sorts by average on every repaint and a 600-frame walk per comparison is not free.
    class Row
    {
        public string Name;
        public ProfilerRecorder Recorder;
        public double[] Ms = new double[k_History];
        public long[] Calls = new long[k_History];
        public double SumMs;
        public double SumCalls;
        public double AvgMs;
        public double AvgCalls;
        public double LastMs;
        public long LastCalls;
        public double PeakMs;

        // Cells are re-formatted once per sampled frame, for the rows actually on screen, instead of
        // once per IMGUI pass for every matched marker.
        public readonly GUIContent NameContent = new GUIContent();
        public readonly GUIContent AvgContent = new GUIContent();
        public readonly GUIContent LastContent = new GUIContent();
        public readonly GUIContent PeakContent = new GUIContent();
        public readonly GUIContent CallsContent = new GUIContent();
        public int TextStamp = int.MinValue;
    }

    // Names outside the filter that are still worth capturing: the engine-side fence costs the
    // whole session has been chasing.
    static readonly string[] k_ExtraMarkers =
    {
        "JobHandle.Complete",
        "WaitForJobGroupID",
        "Interactable System",
    };

    // GUILayout.Width allocates an option object plus the params array on every call, so the column
    // constraints are built once instead of five times per row per IMGUI pass.
    static readonly GUILayoutOption[] k_NameCol = { GUILayout.MinWidth(240f) };
    static readonly GUILayoutOption[] k_NumCol = { GUILayout.Width(70f) };
    static readonly GUILayoutOption[] k_CallsCol = { GUILayout.Width(50f) };

    static readonly GUIContent k_HeadMarker = new GUIContent("Marker");
    static readonly GUIContent k_HeadAvg = new GUIContent("Avg");
    static readonly GUIContent k_HeadLast = new GUIContent("Last");
    static readonly GUIContent k_HeadPeak = new GUIContent("Peak");
    static readonly GUIContent k_HeadCalls = new GUIContent("Calls");

    static readonly Comparison<Row> k_ByAvgDescending = (a, b) => b.AvgMs.CompareTo(a.AvgMs);

    readonly List<Row> _rows = new List<Row>();
    readonly List<Row> _sorted = new List<Row>();
    readonly HashSet<string> _known = new HashSet<string>();
    readonly List<ProfilerRecorderHandle> _handleScratch = new List<ProfilerRecorderHandle>(1024);
    readonly int[] _frameStamps = new int[k_History];
    readonly double[] _frameMs = new double[k_History];
    double _frameSumMs;
    double _frameAvgMs;
    ProfilerRecorder _mainThreadRecorder;
    int _head;                                    // next write slot
    int _captured;                                // total frames captured (ring saturates at k_History)
    int _lastSampledFrame = -1;
    int _rescanCountdown;
    int _rescanInterval = 120;
    int _handlesSeen;                             // size of the last GetAvailable enumeration
    bool _record = true;
    bool _hideIdle = true;
    bool _sortDirty = true;
    bool _frameFromDelta;
    int _rowsDrawn = k_DefaultRowsDrawn;
    int _rowsHidden;
    string _filter = "Basis";

    // Markers feed ProfilerRecorder only while the profiler backend is collecting, and in the editor
    // that is off until something turns it on — which is why the table read empty. Recording forces
    // it on and hands the user's setting back when it stops.
    bool _forcedProfiler;
    bool _profilerWasEnabled;

    public override string Title => "Markers";

    public override string Subtitle =>
        "Every instrumented marker, sampled live — avg / last / peak per frame.";

    public override void OnEnable()
    {
        EditorApplication.update += Pump;
        Rescan();
    }

    public override void OnDisable()
    {
        EditorApplication.update -= Pump;
        ReleaseProfiler();
        DisposeRecorders();
    }

    // ── recording ───────────────────────────────────────────────────────────

    void ForceProfiler()
    {
        if (_forcedProfiler) return;
        _profilerWasEnabled = UnityProfiler.enabled;
        UnityProfiler.enabled = true;
        _forcedProfiler = true;
    }

    void ReleaseProfiler()
    {
        if (!_forcedProfiler) return;
        UnityProfiler.enabled = _profilerWasEnabled;
        _forcedProfiler = false;
    }

    void DisposeRecorders()
    {
        foreach (Row row in _rows)
        {
            if (row.Recorder.Valid) row.Recorder.Dispose();
        }
        _rows.Clear();
        _sorted.Clear();
        _known.Clear();
        _sortDirty = true;
        if (_mainThreadRecorder.Valid) _mainThreadRecorder.Dispose();
        _mainThreadRecorder = default;
    }

    void ClearHistory()
    {
        _captured = 0;
        _head = 0;
        _frameSumMs = 0;
        _frameAvgMs = 0;
        Array.Clear(_frameMs, 0, k_History);
        Array.Clear(_frameStamps, 0, k_History);
        foreach (Row row in _rows)
        {
            Array.Clear(row.Ms, 0, k_History);
            Array.Clear(row.Calls, 0, k_History);
            row.SumMs = row.SumCalls = row.AvgMs = row.AvgCalls = 0;
            row.LastMs = 0;
            row.LastCalls = 0;
            row.PeakMs = 0;
            row.TextStamp = int.MinValue;
        }
        _sortDirty = true;
    }

    /// <summary>
    /// Counters reject SumAllSamplesInFrame and markers reject its absence, so both shapes are tried
    /// and an unsupported stat yields an invalid recorder rather than throwing out of the rescan.
    /// </summary>
    static ProfilerRecorder CreateRecorder(ProfilerRecorderHandle handle)
    {
        try { return new ProfilerRecorder(handle, 1, k_MarkerOptions); }
        catch { }
        try { return new ProfilerRecorder(handle, 1, k_CounterOptions); }
        catch { return default; }
    }

    static ProfilerRecorder CreateRecorder(ProfilerCategory category, string name)
    {
        try { return new ProfilerRecorder(category, name, 1, k_MarkerOptions); }
        catch { }
        try { return new ProfilerRecorder(category, name, 1, k_CounterOptions); }
        catch { return default; }
    }

    /// <summary>
    /// Marker stats only become enumerable after their first Begin, so this is re-run periodically
    /// while recording; existing rows keep their recorders and ring history. Enumerating every stat
    /// in the process and describing each one is not cheap, so the interval backs off while the
    /// matched set is stable and snaps back the moment a new marker appears.
    /// </summary>
    void Rescan()
    {
        _handleScratch.Clear();
        ProfilerRecorderHandle.GetAvailable(_handleScratch);
        _handlesSeen = _handleScratch.Count;

        for (int h = 0; h < _handleScratch.Count; h++)
        {
            ProfilerRecorderHandle handle = _handleScratch[h];
            ProfilerRecorderDescription desc = ProfilerRecorderHandle.GetDescription(handle);
            if (desc.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
            string name = desc.Name;
            if (_known.Contains(name)) continue;

            bool wanted = !string.IsNullOrEmpty(_filter) && name.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0;
            if (!wanted)
            {
                for (int i = 0; i < k_ExtraMarkers.Length; i++)
                {
                    if (name == k_ExtraMarkers[i]) { wanted = true; break; }
                }
            }
            if (!wanted) continue;

            _known.Add(name);
            var row = new Row { Name = name, Recorder = CreateRecorder(handle) };
            row.NameContent.text = name;
            _rows.Add(row);
            _sortDirty = true;
        }

        if (!_mainThreadRecorder.Valid)
        {
            _mainThreadRecorder = CreateRecorder(ProfilerCategory.Internal, "Main Thread");
        }
    }

    void Pump()
    {
        if (!_record) return;
        if (!EditorApplication.isPlaying || EditorApplication.isPaused)
        {
            ReleaseProfiler();
            return;
        }
        ForceProfiler();

        int frame = Time.frameCount;
        if (frame == _lastSampledFrame) return;
        _lastSampledFrame = frame;

        if (--_rescanCountdown <= 0)
        {
            int before = _rows.Count;
            Rescan();
            _rescanInterval = _rows.Count == before ? Math.Min(_rescanInterval * 2, 1800) : 120;
            _rescanCountdown = _rescanInterval;
        }

        _frameStamps[_head] = frame - 1;   // LastValue describes the most recently completed frame
        for (int i = 0; i < _rows.Count; i++)
        {
            Row row = _rows[i];
            double ms = 0;
            long calls = 0;
            if (row.Recorder.Valid && row.Recorder.Count > 0)
            {
                ProfilerRecorderSample sample = row.Recorder.GetSample(row.Recorder.Count - 1);
                ms = sample.Value * k_NsToMs;
                calls = sample.Count;
            }
            row.SumMs += ms - row.Ms[_head];             // the slot being overwritten leaves the window
            row.SumCalls += calls - row.Calls[_head];
            row.Ms[_head] = ms;
            row.Calls[_head] = calls;
            row.LastMs = ms;
            row.LastCalls = calls;
            if (ms > row.PeakMs) row.PeakMs = ms;
        }
        // Backstop for the header reading "avg frame 0.000 ms" while every marker under it had data:
        // whenever the Main Thread stat yields nothing, fall back to the player loop's own delta and
        // say so in the Capture card, because that number includes vsync wait and is not the same
        // quantity.
        double frameMs = _mainThreadRecorder.Valid ? _mainThreadRecorder.LastValueAsDouble * k_NsToMs : 0;
        _frameFromDelta = frameMs <= 0;
        if (_frameFromDelta) frameMs = Time.unscaledDeltaTime * 1000.0;
        _frameSumMs += frameMs - _frameMs[_head];
        _frameMs[_head] = frameMs;

        _head = (_head + 1) % k_History;
        if (_captured < k_History) _captured++;

        double inv = 1.0 / _captured;
        _frameAvgMs = _frameSumMs * inv;
        for (int i = 0; i < _rows.Count; i++)
        {
            Row row = _rows[i];
            row.AvgMs = row.SumMs * inv;
            row.AvgCalls = row.SumCalls * inv;
        }
        _sortDirty = true;
    }

    // ── aggregation ─────────────────────────────────────────────────────────

    List<Row> SortedRows()
    {
        if (!_sortDirty) return _sorted;
        _sorted.Clear();
        _sorted.AddRange(_rows);
        _sorted.Sort(k_ByAvgDescending);
        _sortDirty = false;
        return _sorted;
    }

    static bool HasData(Row row) => row.AvgMs > 0 || row.PeakMs > 0;

    static void FormatRow(Row row, int stamp)
    {
        row.AvgContent.text = row.AvgMs.ToString("F3", CultureInfo.InvariantCulture);
        row.LastContent.text = row.LastMs.ToString("F3", CultureInfo.InvariantCulture);
        row.PeakContent.text = row.PeakMs.ToString("F3", CultureInfo.InvariantCulture);
        row.CallsContent.text = row.LastCalls.ToString(CultureInfo.InvariantCulture);
        row.TextStamp = stamp;
    }

    int RowsWithData()
    {
        int n = 0;
        for (int i = 0; i < _rows.Count; i++)
        {
            if (HasData(_rows[i])) n++;
        }
        return n;
    }

    // ── drawing ─────────────────────────────────────────────────────────────

    public override void Draw()
    {
        using (BasisEditorUI.Card())
        {
            DrawControls();
        }
        DrawStatus();
        using (BasisEditorUI.Card("Markers"))
        {
            DrawTable();
        }
    }

    void DrawControls()
    {
        EditorGUILayout.BeginHorizontal();

        bool record = GUILayout.Toggle(_record, "Record", EditorStyles.miniButton, GUILayout.Width(64f));
        if (record != _record)
        {
            _record = record;
            if (!_record) ReleaseProfiler();
        }

        if (BasisEditorUI.SecondaryButton("Clear", 20f, GUILayout.Width(56f))) ClearHistory();
        if (BasisEditorUI.SecondaryButton("Rescan", 20f, GUILayout.Width(64f))) Rescan();

        GUILayout.Space(6f);
        GUILayout.Label("Filter", EditorStyles.miniLabel, GUILayout.Width(34f));
        string newFilter = EditorGUILayout.TextField(_filter, GUILayout.Width(130f));
        if (newFilter != _filter)
        {
            _filter = newFilter;
            DisposeRecorders();
            Rescan();
            ClearHistory();
        }

        GUILayout.FlexibleSpace();
        GUILayout.Label("Top", EditorStyles.miniLabel, GUILayout.Width(26f));
        _rowsDrawn = Mathf.Clamp(EditorGUILayout.IntField(_rowsDrawn, GUILayout.Width(44f)), k_MinRowsDrawn, k_History);
        _hideIdle = GUILayout.Toggle(_hideIdle, "Hide idle", EditorStyles.miniButton, GUILayout.Width(70f));

        if (BasisEditorUI.SecondaryButton("Copy Summary", 20f, GUILayout.Width(104f)))
        {
            EditorGUIUtility.systemCopyBuffer = BuildSummary();
            Host?.ShowNotification(new GUIContent("Summary copied"));
        }
        if (BasisEditorUI.SecondaryButton("Copy Frames CSV", 20f, GUILayout.Width(120f)))
        {
            EditorGUIUtility.systemCopyBuffer = BuildCsv();
            Host?.ShowNotification(new GUIContent("CSV copied"));
        }

        EditorGUILayout.EndHorizontal();
    }

    void DrawStatus()
    {
        double avgFrame = _frameAvgMs;
        int withData = RowsWithData();

        using (BasisEditorUI.Card("Capture"))
        {
            BasisEditorUI.Row("Markers tracked",
                $"{withData} live / {_rows.Count} matched  ({_handlesSeen} handles enumerated)");
            BasisEditorUI.Row("Frames captured", $"{_captured} of {k_History}");
            BasisEditorUI.Row("Avg frame", avgFrame > 0
                ? $"{avgFrame.ToString("F2", CultureInfo.InvariantCulture)} ms ({(1000.0 / avgFrame).ToString("F0", CultureInfo.InvariantCulture)} fps)"
                    + (_frameFromDelta ? "  player-loop delta, includes vsync wait" : "  main thread")
                : "—");
            BasisEditorUI.PillRow("Profiler backend",
                _forcedProfiler ? "FORCED ON" : (UnityProfiler.enabled ? "ON" : "OFF"),
                UnityProfiler.enabled ? BasisEditorUI.State.Good : BasisEditorUI.State.Warn);

            if (!EditorApplication.isPlaying)
            {
                BasisEditorUI.Note("Enter play mode to record — markers only exist once the code that emits them runs.");
            }
            else if (!_record)
            {
                BasisEditorUI.Note("Recording is off; the profiler backend has been handed back to whatever had it.");
            }
            else if (_rows.Count == 0)
            {
                BasisEditorUI.Help(
                    $"Nothing matched \"{_filter}\". Marker names only become enumerable after the marker is first hit, "
                    + "so give it a few frames or press Rescan.", MessageType.Info);
            }
            else if (_captured > 0 && withData == 0)
            {
                BasisEditorUI.Help(
                    "Every marker read zero. The recorders are running and the profiler backend is on, so the "
                    + "matched markers were not hit in the captured frames — widen the filter or check the "
                    + "subsystem is actually ticking.", MessageType.Warning);
            }
        }
    }

    void DrawTable()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(k_HeadMarker, EditorStyles.boldLabel, k_NameCol);
        GUILayout.Label(k_HeadAvg, EditorStyles.boldLabel, k_NumCol);
        GUILayout.Label(k_HeadLast, EditorStyles.boldLabel, k_NumCol);
        GUILayout.Label(k_HeadPeak, EditorStyles.boldLabel, k_NumCol);
        GUILayout.Label(k_HeadCalls, EditorStyles.boldLabel, k_CallsCol);
        GUILayout.EndHorizontal();
        BasisEditorUI.Divider();

        List<Row> sorted = SortedRows();
        int stamp = _lastSampledFrame;
        int drawn = 0;
        _rowsHidden = 0;

        for (int i = 0; i < sorted.Count; i++)
        {
            Row row = sorted[i];
            bool live = HasData(row);
            if (_hideIdle && !live) continue;
            if (drawn >= _rowsDrawn)
            {
                _rowsHidden++;
                continue;
            }
            drawn++;
            if (row.TextStamp != stamp) FormatRow(row, stamp);

            GUILayout.BeginHorizontal();
            Color prev = GUI.color;
            if (!live) GUI.color = BasisEditorUI.Muted;
            GUILayout.Label(row.NameContent, k_NameCol);
            GUILayout.Label(row.AvgContent, k_NumCol);
            GUILayout.Label(row.LastContent, k_NumCol);
            GUILayout.Label(row.PeakContent, k_NumCol);
            GUILayout.Label(row.CallsContent, k_CallsCol);
            GUI.color = prev;
            GUILayout.EndHorizontal();
        }

        if (drawn == 0)
        {
            BasisEditorUI.Note(_rows.Count == 0
                ? "No markers matched the filter yet."
                : "Every matched marker is idle — untick Hide idle to list them anyway.");
        }
        else if (_rowsHidden > 0)
        {
            BasisEditorUI.Note($"{_rowsHidden} cheaper markers not drawn — raise Top, or Copy Summary for all of them.");
        }
    }

    // ── clipboard ───────────────────────────────────────────────────────────

    string BuildSummary()
    {
        var sb = new StringBuilder(4096);
        double avgFrame = _frameAvgMs;
        sb.AppendLine($"Basis frame timings — {_captured} frames, avg frame {avgFrame.ToString("F3", CultureInfo.InvariantCulture)} ms"
            + (avgFrame > 0 ? $" ({(1000.0 / avgFrame).ToString("F0", CultureInfo.InvariantCulture)} fps)" : "")
            + $", {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"{"marker",-56} {"avg ms",9} {"last ms",9} {"peak ms",9} {"calls",6}");
        foreach (Row row in SortedRows())
        {
            if (!HasData(row)) continue;
            sb.AppendLine($"{row.Name,-56} {row.AvgMs.ToString("F4", CultureInfo.InvariantCulture),9} "
                + $"{row.LastMs.ToString("F4", CultureInfo.InvariantCulture),9} "
                + $"{row.PeakMs.ToString("F4", CultureInfo.InvariantCulture),9} "
                + $"{row.AvgCalls.ToString("F1", CultureInfo.InvariantCulture),6}");
        }
        return sb.ToString();
    }

    string BuildCsv()
    {
        var sb = new StringBuilder(64 * 1024);
        List<Row> sorted = SortedRows();
        sb.Append("frame,frameMs");
        foreach (Row row in sorted) sb.Append(',').Append(row.Name.Replace(',', ';'));
        sb.AppendLine();

        int n = _captured;
        int start = (_head - n + k_History) % k_History;
        for (int i = 0; i < n; i++)
        {
            int slot = (start + i) % k_History;
            sb.Append(_frameStamps[slot]).Append(',')
              .Append(_frameMs[slot].ToString("F4", CultureInfo.InvariantCulture));
            foreach (Row row in sorted)
            {
                sb.Append(',').Append(row.Ms[slot].ToString("F4", CultureInfo.InvariantCulture));
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
