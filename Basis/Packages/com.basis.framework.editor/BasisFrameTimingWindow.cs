using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Rolling per-frame capture of every Basis profiler marker (BasisDriver.*, BasisEerie.*, the Basis
/// Burst jobs, plus the engine's JobHandle.Complete / WaitForJobGroupID waits), recorded through
/// ProfilerRecorder so it works in play mode without the Profiler window and sums worker-thread
/// samples the same way the profiler's accumulated view does. Copy Summary produces the
/// paste-friendly table (avg / last / peak / calls over the recorded window); Copy CSV produces the
/// full per-frame history for offline diffing.
/// </summary>
public class BasisFrameTimingWindow : EditorWindow
{
    const int k_History = 600;                    // ~10s at 60fps editor pumping
    const double k_NsToMs = 1.0 / 1_000_000.0;

    class Row
    {
        public string Name;
        public ProfilerRecorder Recorder;
        public double[] Ms = new double[k_History];
        public long[] Calls = new long[k_History];
        public double LastMs;
        public long LastCalls;
        public double PeakMs;
    }

    // Names outside the filter that are still worth capturing: the engine-side fence costs the
    // whole session has been chasing.
    static readonly string[] k_ExtraMarkers =
    {
        "JobHandle.Complete",
        "WaitForJobGroupID",
        "Interactable System",
    };

    readonly List<Row> _rows = new List<Row>();
    readonly HashSet<string> _known = new HashSet<string>();
    readonly int[] _frameStamps = new int[k_History];
    ProfilerRecorder _mainThreadRecorder;
    int _head;                                    // next write slot
    int _captured;                                // total frames captured (ring saturates at k_History)
    int _lastSampledFrame = -1;
    int _rescanCountdown;
    bool _record = true;
    string _filter = "Basis";
    Vector2 _scroll;

    [MenuItem("Basis/Debug/Frame Timing")]
    static void Open()
    {
        GetWindow<BasisFrameTimingWindow>("Frame Timing");
    }

    void OnEnable()
    {
        EditorApplication.update += Pump;
        Rescan();
    }

    void OnDisable()
    {
        EditorApplication.update -= Pump;
        DisposeRecorders();
    }

    void DisposeRecorders()
    {
        foreach (Row row in _rows)
        {
            if (row.Recorder.Valid) row.Recorder.Dispose();
        }
        _rows.Clear();
        _known.Clear();
        if (_mainThreadRecorder.Valid) _mainThreadRecorder.Dispose();
        _mainThreadRecorder = default;
    }

    /// <summary>
    /// Marker stats only become enumerable after their first Begin, so this is re-run periodically
    /// while recording; existing rows keep their recorders and ring history.
    /// </summary>
    void Rescan()
    {
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        foreach (ProfilerRecorderHandle handle in handles)
        {
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
            _rows.Add(new Row { Name = name, Recorder = new ProfilerRecorder(handle, 1) });
        }

        if (!_mainThreadRecorder.Valid)
        {
            _mainThreadRecorder = new ProfilerRecorder(ProfilerCategory.Internal, "Main Thread", 1);
        }
    }

    void Pump()
    {
        if (!_record || !EditorApplication.isPlaying || EditorApplication.isPaused) return;

        int frame = Time.frameCount;
        if (frame == _lastSampledFrame) return;
        _lastSampledFrame = frame;

        if (--_rescanCountdown <= 0)
        {
            _rescanCountdown = 120;
            Rescan();
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
            row.Ms[_head] = ms;
            row.Calls[_head] = calls;
            row.LastMs = ms;
            row.LastCalls = calls;
            if (ms > row.PeakMs) row.PeakMs = ms;
        }
        double frameMs = _mainThreadRecorder.Valid ? _mainThreadRecorder.LastValueAsDouble * k_NsToMs : 0;
        _frameMs[_head] = frameMs;

        _head = (_head + 1) % k_History;
        if (_captured < k_History) _captured++;
    }

    readonly double[] _frameMs = new double[k_History];

    double Average(double[] ring)
    {
        int n = _captured;
        if (n == 0) return 0;
        double sum = 0;
        for (int i = 0; i < n; i++) sum += ring[i];
        return sum / n;
    }

    double AverageCalls(long[] ring)
    {
        int n = _captured;
        if (n == 0) return 0;
        double sum = 0;
        for (int i = 0; i < n; i++) sum += ring[i];
        return sum / n;
    }

    List<Row> SortedRows()
    {
        var sorted = new List<Row>(_rows);
        sorted.Sort((a, b) => Average(b.Ms).CompareTo(Average(a.Ms)));
        return sorted;
    }

    void OnInspectorUpdate() => Repaint();

    void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            _record = GUILayout.Toggle(_record, "Record", EditorStyles.toolbarButton, GUILayout.Width(60));
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                _captured = 0; _head = 0;
                foreach (Row row in _rows) row.PeakMs = 0;
            }
            if (GUILayout.Button("Rescan", EditorStyles.toolbarButton, GUILayout.Width(55))) Rescan();
            string newFilter = GUILayout.TextField(_filter, EditorStyles.toolbarTextField, GUILayout.Width(120));
            if (newFilter != _filter)
            {
                _filter = newFilter;
                DisposeRecorders();
                Rescan();
                _captured = 0; _head = 0;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy Summary", EditorStyles.toolbarButton, GUILayout.Width(95)))
            {
                EditorGUIUtility.systemCopyBuffer = BuildSummary();
                ShowNotification(new GUIContent("Summary copied"));
            }
            if (GUILayout.Button("Copy Frames CSV", EditorStyles.toolbarButton, GUILayout.Width(110)))
            {
                EditorGUIUtility.systemCopyBuffer = BuildCsv();
                ShowNotification(new GUIContent("CSV copied"));
            }
        }

        double avgFrame = Average(_frameMs);
        GUILayout.Label(
            $"{_rows.Count} markers | {_captured} frames | avg frame {avgFrame:F2} ms" +
            (avgFrame > 0 ? $" ({1000.0 / avgFrame:F0} fps)" : "") +
            (EditorApplication.isPlaying ? "" : "  —  enter play mode to record"),
            EditorStyles.miniLabel);

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label("Marker", EditorStyles.boldLabel, GUILayout.MinWidth(260));
            GUILayout.Label("Avg", EditorStyles.boldLabel, GUILayout.Width(70));
            GUILayout.Label("Last", EditorStyles.boldLabel, GUILayout.Width(70));
            GUILayout.Label("Peak", EditorStyles.boldLabel, GUILayout.Width(70));
            GUILayout.Label("Calls", EditorStyles.boldLabel, GUILayout.Width(50));
        }
        foreach (Row row in SortedRows())
        {
            double avg = Average(row.Ms);
            if (avg <= 0 && row.PeakMs <= 0) continue;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(row.Name, GUILayout.MinWidth(260));
                GUILayout.Label(avg.ToString("F3", CultureInfo.InvariantCulture), GUILayout.Width(70));
                GUILayout.Label(row.LastMs.ToString("F3", CultureInfo.InvariantCulture), GUILayout.Width(70));
                GUILayout.Label(row.PeakMs.ToString("F3", CultureInfo.InvariantCulture), GUILayout.Width(70));
                GUILayout.Label(row.LastCalls.ToString(), GUILayout.Width(50));
            }
        }
        EditorGUILayout.EndScrollView();
    }

    string BuildSummary()
    {
        var sb = new StringBuilder(4096);
        double avgFrame = Average(_frameMs);
        sb.AppendLine($"Basis frame timings — {_captured} frames, avg frame {avgFrame.ToString("F3", CultureInfo.InvariantCulture)} ms"
            + (avgFrame > 0 ? $" ({(1000.0 / avgFrame).ToString("F0", CultureInfo.InvariantCulture)} fps)" : "")
            + $", {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"{"marker",-56} {"avg ms",9} {"last ms",9} {"peak ms",9} {"calls",6}");
        foreach (Row row in SortedRows())
        {
            double avg = Average(row.Ms);
            if (avg <= 0 && row.PeakMs <= 0) continue;
            sb.AppendLine($"{row.Name,-56} {avg.ToString("F4", CultureInfo.InvariantCulture),9} "
                + $"{row.LastMs.ToString("F4", CultureInfo.InvariantCulture),9} "
                + $"{row.PeakMs.ToString("F4", CultureInfo.InvariantCulture),9} "
                + $"{AverageCalls(row.Calls).ToString("F1", CultureInfo.InvariantCulture),6}");
        }
        return sb.ToString();
    }

    string BuildCsv()
    {
        var sb = new StringBuilder(64 * 1024);
        var sorted = SortedRows();
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
