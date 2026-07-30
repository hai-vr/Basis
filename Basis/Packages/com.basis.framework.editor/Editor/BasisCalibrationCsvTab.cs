#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Basis.Scripts.Drivers;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Browses the calibration-debug CSVs written by <see cref="BasisCalibrationDebugRecorder"/>.
/// Shows the on-disk location, lists the captured files (newest first), and renders the selected
/// file as a filterable table. For calibration files it also surfaces a "quick read" of the
/// values that matter for the flipped/inverted-head problem.
/// </summary>
public sealed class BasisCalibrationCsvTab : BasisEditorTabPage
{
        public override string Title => "Debug CSV";
        public override string Subtitle =>
            "Record a calibration run to CSV so a bad bind can be read back frame by frame.";

    private sealed class CsvFileInfo
    {
        public string Path;
        public string Name;
        public long Size;
        public DateTime Modified;
    }

    private readonly List<CsvFileInfo> _files = new List<CsvFileInfo>();
    private string[] _fileLabels = Array.Empty<string>();
    private int _selectedFile = -1;

    private string[] _headers = Array.Empty<string>();
    private readonly List<string[]> _rows = new List<string[]>();
    private string _loadError;

    private string[] _stageOptions = { "All" };
    private string[] _spaceOptions = { "All" };
    private int _stageFilter;
    private int _spaceFilter;
    private string _labelFilter = string.Empty;
    private bool _showQuaternions;

    private Vector2 _tableScroll;
    private GUIStyle _monoHeader;
    private GUIStyle _monoCell;

    public override void OnEnable()
    {
        RefreshFiles();
    }

    private void EnsureStyles()
    {
        if (_monoHeader == null)
        {
            _monoHeader = new GUIStyle(EditorStyles.boldLabel) { fontSize = 11 };
            _monoHeader.font = EditorStyles.miniLabel.font;
        }
        if (_monoCell == null)
        {
            _monoCell = new GUIStyle(EditorStyles.label) { fontSize = 11, clipping = TextClipping.Clip };
        }
    }

    public override void Draw()
    {
        EnsureStyles();
        DrawLocationBar();
        EditorGUILayout.Space(4);
        DrawFilePicker();

        if (!string.IsNullOrEmpty(_loadError))
        {
            BasisEditorUI.Help(_loadError, MessageType.Error);
            return;
        }
        if (_headers.Length == 0)
        {
            EditorGUILayout.HelpBox(
                "No file loaded. Enable Settings → Developer → \"Dump Calibration CSV\", calibrate an avatar in Play mode, then Refresh.",
                MessageType.Info);
            return;
        }

        DrawQuickRead();
        EditorGUILayout.Space(4);
        DrawFilters();
        EditorGUILayout.Space(2);
        DrawTable();
    }

    // ── Location ──────────────────────────────────────────────────────────────

    private void DrawLocationBar()
    {
        string dir = BasisCalibrationDebugRecorder.OutputDirectory;
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Folder", GUILayout.Width(44));
            EditorGUILayout.SelectableLabel(dir, EditorStyles.textField, GUILayout.Height(18));
            bool exists = Directory.Exists(dir);
            using (new EditorGUI.DisabledScope(!exists))
            {
                if (GUILayout.Button("Reveal", GUILayout.Width(64)))
                {
                    EditorUtility.RevealInFinder(dir);
                }
            }
            if (GUILayout.Button("Refresh", GUILayout.Width(64)))
            {
                RefreshFiles();
            }
        }
    }

    // ── File picker ───────────────────────────────────────────────────────────

    private void DrawFilePicker()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("File", GUILayout.Width(44));
            if (_files.Count == 0)
            {
                BasisEditorUI.Note("(no CSV files found)");
                return;
            }
            int newIndex = EditorGUILayout.Popup(_selectedFile, _fileLabels);
            if (newIndex != _selectedFile)
            {
                LoadFile(newIndex);
            }

            CsvFileInfo info = (_selectedFile >= 0 && _selectedFile < _files.Count) ? _files[_selectedFile] : null;
            using (new EditorGUI.DisabledScope(info == null))
            {
                if (GUILayout.Button("Open", GUILayout.Width(56)) && info != null)
                {
                    EditorUtility.OpenWithDefaultApp(info.Path);
                }
                if (GUILayout.Button("Reveal", GUILayout.Width(64)) && info != null)
                {
                    EditorUtility.RevealInFinder(info.Path);
                }
            }
        }

        if (_selectedFile >= 0 && _selectedFile < _files.Count)
        {
            CsvFileInfo info = _files[_selectedFile];
            string avatarName = FindMeta("avatarName");
            string namePart = string.IsNullOrEmpty(avatarName) ? string.Empty : $"avatar: {avatarName} · ";
            EditorGUILayout.LabelField(
                $"{namePart}{_rows.Count} rows · {FormatSize(info.Size)} · {info.Modified:yyyy-MM-dd HH:mm:ss}",
                EditorStyles.miniLabel);
        }
    }

    private string FindMeta(string label)
    {
        int sc = ColumnIndex("stage");
        int lc = ColumnIndex("label");
        int nc = ColumnIndex("note");
        if (sc < 0 || lc < 0 || nc < 0) return null;
        foreach (string[] row in _rows)
        {
            if (sc < row.Length && lc < row.Length && nc < row.Length && row[sc] == "Meta" && row[lc] == label)
            {
                return row[nc];
            }
        }
        return null;
    }

    private void RefreshFiles()
    {
        _files.Clear();
        string dir = BasisCalibrationDebugRecorder.OutputDirectory;
        if (Directory.Exists(dir))
        {
            foreach (string path in Directory.GetFiles(dir, "*.csv", SearchOption.TopDirectoryOnly))
            {
                FileInfo fi = new FileInfo(path);
                _files.Add(new CsvFileInfo { Path = path, Name = fi.Name, Size = fi.Length, Modified = fi.LastWriteTime });
            }
            _files.Sort((a, b) => b.Modified.CompareTo(a.Modified));
        }

        _fileLabels = new string[_files.Count];
        for (int i = 0; i < _files.Count; i++)
        {
            _fileLabels[i] = $"{_files[i].Name}   ({_files[i].Modified:MM-dd HH:mm})";
        }

        if (_files.Count == 0)
        {
            _selectedFile = -1;
            _headers = Array.Empty<string>();
            _rows.Clear();
        }
        else
        {
            LoadFile(Mathf.Clamp(_selectedFile < 0 ? 0 : _selectedFile, 0, _files.Count - 1));
        }
    }

    private void LoadFile(int index)
    {
        _selectedFile = index;
        _loadError = null;
        _headers = Array.Empty<string>();
        _rows.Clear();
        _stageFilter = 0;
        _spaceFilter = 0;
        _labelFilter = string.Empty;

        if (index < 0 || index >= _files.Count)
        {
            return;
        }

        try
        {
            string[] lines = File.ReadAllLines(_files[index].Path);
            if (lines.Length == 0)
            {
                _loadError = "File is empty.";
                return;
            }
            _headers = ParseLine(lines[0]);
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrEmpty(lines[i])) continue;
                _rows.Add(ParseLine(lines[i]));
            }
            BuildFilterOptions();
        }
        catch (Exception e)
        {
            _loadError = "Failed to read file: " + e.Message;
        }
    }

    private void BuildFilterOptions()
    {
        int stageCol = ColumnIndex("stage");
        int spaceCol = ColumnIndex("space");
        var stages = new List<string> { "All" };
        var spaces = new List<string> { "All" };
        foreach (string[] row in _rows)
        {
            if (stageCol >= 0 && stageCol < row.Length && !stages.Contains(row[stageCol])) stages.Add(row[stageCol]);
            if (spaceCol >= 0 && spaceCol < row.Length && !spaces.Contains(row[spaceCol])) spaces.Add(row[spaceCol]);
        }
        _stageOptions = stages.ToArray();
        _spaceOptions = spaces.ToArray();
    }

    // ── Quick read (head-flip diagnosis) ───────────────────────────────────────

    private void DrawQuickRead()
    {
        string rootEuler = FindEuler("Offsets", "AnimatorRoot", "world");
        string headOffset = FindEuler("Offsets", "CalibratedRotationHead", "offset");
        string headTpose = FindEuler("Offsets", "head.ctrl.TposeLocal", "local");
        if (rootEuler == null && headOffset == null)
        {
            return; // not a calibration file (e.g. a runtime_* file)
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            BasisEditorUI.SectionTitle("Quick read — head offset frame");
            if (rootEuler != null) EditorGUILayout.LabelField("AnimatorRoot (world euler):", rootEuler);
            if (headOffset != null) EditorGUILayout.LabelField("CalibratedRotationHead (offset):", headOffset);
            if (headTpose != null) EditorGUILayout.LabelField("head.ctrl.TposeLocal (root-relative):", headTpose);

            if (TryGetYaw(rootEuler, out float yaw))
            {
                float off = Mathf.Abs(Mathf.DeltaAngle(0f, yaw));
                if (off > 10f)
                {
                    EditorGUILayout.HelpBox(
                        $"AnimatorRoot is rotated {off:F1}° in yaw at capture time. This spawn orientation is baked into the absolute head offset and is the likely source of the flipped/inverted head.",
                        MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "AnimatorRoot is near-upright at capture — this run should not exhibit the spawn-orientation head flip.",
                        MessageType.Info);
                }
            }
        }
    }

    // ── Filters ────────────────────────────────────────────────────────────────

    private void DrawFilters()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Stage", GUILayout.Width(40));
            _stageFilter = EditorGUILayout.Popup(_stageFilter, _stageOptions, GUILayout.Width(120));
            EditorGUILayout.LabelField("Space", GUILayout.Width(44));
            _spaceFilter = EditorGUILayout.Popup(_spaceFilter, _spaceOptions, GUILayout.Width(100));
            EditorGUILayout.LabelField("Label", GUILayout.Width(40));
            _labelFilter = EditorGUILayout.TextField(_labelFilter);
            _showQuaternions = GUILayout.Toggle(_showQuaternions, "Quats", EditorStyles.miniButton, GUILayout.Width(50));
        }
    }

    private bool RowPasses(string[] row)
    {
        if (_stageFilter > 0)
        {
            int c = ColumnIndex("stage");
            if (c < 0 || c >= row.Length || row[c] != _stageOptions[_stageFilter]) return false;
        }
        if (_spaceFilter > 0)
        {
            int c = ColumnIndex("space");
            if (c < 0 || c >= row.Length || row[c] != _spaceOptions[_spaceFilter]) return false;
        }
        if (!string.IsNullOrEmpty(_labelFilter))
        {
            int c = ColumnIndex("label");
            if (c < 0 || c >= row.Length || row[c].IndexOf(_labelFilter, StringComparison.OrdinalIgnoreCase) < 0) return false;
        }
        return true;
    }

    // ── Table ────────────────────────────────────────────────────────────────

    private void DrawTable()
    {
        _tableScroll = EditorGUILayout.BeginScrollView(_tableScroll);

        using (new EditorGUILayout.HorizontalScope())
        {
            for (int c = 0; c < _headers.Length; c++)
            {
                if (!ColumnVisible(c)) continue;
                GUILayout.Label(_headers[c], _monoHeader, GUILayout.Width(ColumnWidth(c)));
            }
        }

        int shown = 0;
        for (int r = 0; r < _rows.Count; r++)
        {
            string[] row = _rows[r];
            if (!RowPasses(row)) continue;
            shown++;

            Color bg = GUI.backgroundColor;
            if ((shown & 1) == 0) GUI.backgroundColor = new Color(0f, 0f, 0f, 0.06f);
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int c = 0; c < _headers.Length; c++)
                {
                    if (!ColumnVisible(c)) continue;
                    string val = c < row.Length ? row[c] : string.Empty;
                    GUILayout.Label(FormatCell(c, val), _monoCell, GUILayout.Width(ColumnWidth(c)));
                }
            }
            GUI.backgroundColor = bg;
        }

        EditorGUILayout.EndScrollView();
        BasisEditorUI.Note($"{shown} / {_rows.Count} rows shown");
    }

    private bool ColumnVisible(int c)
    {
        if (_showQuaternions) return true;
        string h = _headers[c];
        return h != "quatX" && h != "quatY" && h != "quatZ" && h != "quatW";
    }

    private float ColumnWidth(int c)
    {
        switch (c < _headers.Length ? _headers[c] : string.Empty)
        {
            case "index": return 44f;
            case "stage": return 72f;
            case "label": return 180f;
            case "space": return 70f;
            case "note": return 130f;
            default: return 64f;
        }
    }

    private string FormatCell(int c, string val)
    {
        string h = c < _headers.Length ? _headers[c] : string.Empty;
        // Trim long floats for readability while keeping sign/magnitude.
        if (val.Length > 0 && (h.StartsWith("pos") || h.StartsWith("euler") || h.StartsWith("quat") || h.StartsWith("scale")))
        {
            if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
            {
                return f.ToString("0.###", CultureInfo.InvariantCulture);
            }
        }
        return val;
    }

    // ── CSV / lookup helpers ───────────────────────────────────────────────────

    private int ColumnIndex(string header)
    {
        for (int i = 0; i < _headers.Length; i++)
        {
            if (_headers[i] == header) return i;
        }
        return -1;
    }

    private string FindEuler(string stage, string label, string space)
    {
        int sc = ColumnIndex("stage");
        int lc = ColumnIndex("label");
        int spc = ColumnIndex("space");
        int ex = ColumnIndex("eulerX");
        int ey = ColumnIndex("eulerY");
        int ez = ColumnIndex("eulerZ");
        if (sc < 0 || lc < 0 || spc < 0 || ex < 0) return null;

        foreach (string[] row in _rows)
        {
            if (sc < row.Length && lc < row.Length && spc < row.Length
                && row[sc] == stage && row[lc] == label && row[spc] == space)
            {
                return $"({Trim(row, ex)}, {Trim(row, ey)}, {Trim(row, ez)})";
            }
        }
        return null;
    }

    private static string Trim(string[] row, int col)
    {
        if (col < 0 || col >= row.Length) return "?";
        return float.TryParse(row[col], NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
            ? f.ToString("0.##", CultureInfo.InvariantCulture)
            : row[col];
    }

    private static bool TryGetYaw(string eulerTuple, out float yaw)
    {
        yaw = 0f;
        if (string.IsNullOrEmpty(eulerTuple)) return false;
        string inner = eulerTuple.Trim('(', ')');
        string[] parts = inner.Split(',');
        return parts.Length == 3 && float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out yaw);
    }

    private static string[] ParseLine(string line)
    {
        var fields = new List<string>(20);
        var sb = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(ch);
            }
            else
            {
                if (ch == '"') inQuotes = true;
                else if (ch == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(ch);
            }
        }
        fields.Add(sb.ToString());
        return fields.ToArray();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return (bytes / 1024f).ToString("0.0") + " KB";
        return (bytes / (1024f * 1024f)).ToString("0.0") + " MB";
    }
}
#endif
