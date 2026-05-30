using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Basis.Scripts.Networking;
using UnityEngine;

/// <summary>
/// Captures local error/exception reports to disk during a session and, if the previous
/// session ended without a clean shutdown (i.e. it crashed), re-sends what it had captured
/// once the client reconnects and the server has reporting enabled. This is what lets a
/// hard crash — which can never report live, because the app is gone — still reach the
/// server's per-player crash store on the next launch.
///
/// Layout under <c>Application.persistentDataPath/CrashReports</c>:
///   session.active : present while a session is running; deleted on a clean quit.
///   pending.jsonl  : one base64-delimited record per captured report this session.
/// If <c>session.active</c> still exists at startup, the previous run did not exit cleanly,
/// so <c>pending.jsonl</c> from that run is loaded for replay before being rotated out.
///
/// Records are stored one-per-line as <c>severity \t base64(system) \t base64(message) \t
/// base64(stack)</c> — base64 keeps newlines and arbitrary characters from breaking the
/// line format without pulling in a JSON dependency.
/// </summary>
public static class BasisCrashReportStore
{
    private const int MaxReplayEntries = 50;
    private const long MaxPendingBytes = 2L * 1024 * 1024;
    private const int MaxMessageChars = 2000;
    private const int MaxStackChars = 12000;

    private static readonly object FileLock = new object();
    private static readonly List<Entry> _previous = new List<Entry>();

    private static string _markerPath;
    private static string _pendingPath;
    private static bool _initialized;
    private static bool _replayed;

    private struct Entry
    {
        public string System;
        public string Message;
        public string Stack;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            string dir = Path.Combine(Application.persistentDataPath, "CrashReports");
            _markerPath = Path.Combine(dir, "session.active");
            _pendingPath = Path.Combine(dir, "pending.jsonl");
            Directory.CreateDirectory(dir);

            // Marker survived from last time → the previous session crashed. Grab its reports.
            if (File.Exists(_markerPath) && File.Exists(_pendingPath))
            {
                LoadPrevious();
            }

            // Rotate: start a fresh pending log and (re)arm the session marker.
            try { if (File.Exists(_pendingPath)) File.Delete(_pendingPath); } catch { }
            try { File.WriteAllText(_markerPath, DateTime.UtcNow.ToString("o")); } catch { }

            Application.quitting += OnQuit;
            BasisNetworkModeration.OnCrashReportingStateChanged += OnCrashReportingStateChanged;
        }
        catch (Exception e)
        {
            BasisDebug.LogWarning($"BasisCrashReportStore init failed: {e.Message}");
        }
    }

    private static void OnQuit()
    {
        // Clean shutdown → nothing to report next time.
        lock (FileLock)
        {
            try { if (_pendingPath != null && File.Exists(_pendingPath)) File.Delete(_pendingPath); } catch { }
            try { if (_markerPath != null && File.Exists(_markerPath)) File.Delete(_markerPath); } catch { }
        }
    }

    private static void OnCrashReportingStateChanged(bool enabled)
    {
        if (enabled) TryReplay();
    }

    /// <summary>
    /// Append one captured report to the pending log. Thread-safe — called from Unity's
    /// threaded log callback. Best-effort; IO failures are swallowed.
    /// </summary>
    public static void Persist(byte severity, string system, string message, string stackTrace)
    {
        if (!_initialized || string.IsNullOrEmpty(_pendingPath)) return;
        try
        {
            string line = severity
                + "\t" + Encode(system)
                + "\t" + Encode(Truncate(message, MaxMessageChars))
                + "\t" + Encode(Truncate(stackTrace, MaxStackChars));
            lock (FileLock)
            {
                // Don't let an exception storm fill the disk.
                try
                {
                    FileInfo info = new FileInfo(_pendingPath);
                    if (info.Exists && info.Length > MaxPendingBytes) return;
                }
                catch { }
                File.AppendAllText(_pendingPath, line + "\n");
            }
        }
        catch { }
    }

    /// <summary>
    /// Send any reports captured from a previous crashed session, once connected and the
    /// server allows reporting. Runs at most once per launch.
    /// </summary>
    public static void TryReplay()
    {
        if (_replayed) return;
        if (!BasisNetworkModeration.CrashReportingEnabled) return;
        if (BasisNetworkConnection.LocalPlayerPeer == null) return;

        List<Entry> toSend;
        lock (FileLock)
        {
            if (_previous.Count == 0) { _replayed = true; return; }
            toSend = new List<Entry>(_previous);
            _previous.Clear();
        }
        _replayed = true;

        foreach (Entry e in toSend)
        {
            BasisErrorReportSender.SendPrevious(e.System, e.Message, e.Stack);
        }
        BasisDebug.Log($"Replayed {toSend.Count} crash report(s) from the previous session.", BasisDebug.LogTag.Networking);
    }

    private static void LoadPrevious()
    {
        try
        {
            string[] lines = File.ReadAllLines(_pendingPath);
            int start = Math.Max(0, lines.Length - MaxReplayEntries);
            for (int i = start; i < lines.Length; i++)
            {
                if (TryParse(lines[i], out Entry entry)) _previous.Add(entry);
            }
        }
        catch (Exception e)
        {
            BasisDebug.LogWarning($"Failed to load previous crash reports: {e.Message}");
        }
    }

    private static bool TryParse(string line, out Entry entry)
    {
        entry = default;
        if (string.IsNullOrEmpty(line)) return false;
        string[] parts = line.Split('\t');
        if (parts.Length < 4) return false;
        try
        {
            entry.System = Decode(parts[1]);
            entry.Message = Decode(parts[2]);
            entry.Stack = Decode(parts[3]);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string Encode(string value)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));

    private static string Decode(string value)
        => string.IsNullOrEmpty(value) ? string.Empty : Encoding.UTF8.GetString(Convert.FromBase64String(value));

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= max ? value : value.Substring(0, max);
    }
}
