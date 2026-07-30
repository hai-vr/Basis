using System.Collections.Generic;
using System.IO;
using System.Text;
using Basis.Scripts.Networking.Sync.Testing;
using UnityEditor;
using UnityEngine;

namespace Basis.Scripts.Networking.Sync.EditorTools
{
    /// <summary>
    /// Editor front-end for <see cref="BasisSyncLatencyMatrix"/>. Measures the owner-set -> remote-render
    /// latency of the real <see cref="BasisSyncReceiver"/> across send-rate / viewer-distance / jitter
    /// sweeps and exports the per-scenario table as CSV. Separate from the convergence matrix window
    /// because this is a timing/feel axis, not a pass/fail correctness axis. Pure offline simulation.
    /// </summary>
    public sealed class BasisSyncLatencyTab : BasisEditorTabPage
    {
        public override string Title => "Latency";
        public override string Subtitle =>
            "Owner-set to remote-render latency across send-rate, distance and jitter sweeps.";

        static readonly Color Hot = new Color(0.95f, 0.5f, 0.5f);
        static readonly Color Warm = new Color(0.95f, 0.85f, 0.35f);
        static readonly Color Cool = new Color(0.45f, 0.9f, 0.45f);

        BasisSyncLatencyMatrix.Options _options = new BasisSyncLatencyMatrix.Options();
        List<BasisSyncLatencyResult> _results;

        public override void Draw()
        {
            EditorGUILayout.Space(6);
            BasisEditorUI.SectionTitle("Generic Value Sync - End-to-End Latency");
            EditorGUILayout.HelpBox(
                "Drives the real BasisSyncReceiver over a linear ramp and measures owner-set -> remote-render " +
                "latency every frame. 'meanBufferMs' is the jitter buffer's share; 'effSendIntervalMs' shows " +
                "what distance reduction does to the owner's send rate. Higher numbers = more felt lag.",
                MessageType.Info);

            EditorGUILayout.Space(4);
            BasisEditorUI.SectionTitle("Sweeps");
            _options.SendRate = EditorGUILayout.Toggle(new GUIContent("Send rate (20/30/60 Hz)"), _options.SendRate);
            _options.Distance = EditorGUILayout.Toggle(new GUIContent("Distance reduction sweep", "20 Hz, reduction ON, nearest-viewer distance 0..30 m."), _options.Distance);
            _options.NoReductionControl = EditorGUILayout.Toggle(new GUIContent("No-reduction control", "Same distances with reduction OFF (flat baseline)."), _options.NoReductionControl);
            _options.Jitter = EditorGUILayout.Toggle(new GUIContent("Network jitter"), _options.Jitter);
            _options.Extrapolate = EditorGUILayout.Toggle(new GUIContent("Extrapolation on/off"), _options.Extrapolate);
            _options.Fixes = EditorGUILayout.Toggle(new GUIContent("Fix A/B (buffer depth, full-rate-while-held)", "Before/after for the two latency fixes."), _options.Fixes);
            _options.RenderHz = EditorGUILayout.Slider(new GUIContent("Render Hz"), (float)_options.RenderHz, 30f, 144f);

            EditorGUILayout.Space(6);
            using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
                if (BasisEditorUI.PrimaryButton("Run Latency Sweep", 30f))
                    _results = BasisSyncLatencyMatrix.RunAll(_options);

            using (new EditorGUI.DisabledScope(_results == null || _results.Count == 0))
                if (GUILayout.Button("Export CSV..."))
                    ExportCsv();

            DrawResults();

        }

        void DrawResults()
        {
            if (_results == null || _results.Count == 0) return;

            EditorGUILayout.Space(6);
            BasisEditorUI.SectionTitle("Results (ms)");

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            Cell("scenario", 130, true);
            Cell("effSend", 64, true);
            Cell("mean", 60, true);
            Cell("p50", 54, true);
            Cell("p95", 54, true);
            Cell("max", 60, true);
            Cell("latσ", 54, true);
            Cell("bufMs", 60, true);
            Cell("depth", 56, true);
            Cell("sends", 54, true);
            EditorGUILayout.EndHorizontal();

            for (int i = 0; i < _results.Count; i++)
            {
                BasisSyncLatencyResult r = _results[i];
                EditorGUILayout.BeginHorizontal();
                Cell(r.Name, 130);
                Cell(r.EffSendIntervalMs.ToString("0"), 64);
                ColoredCell(r.MeanLatencyMs.ToString("0"), 60, LatencyColor(r.MeanLatencyMs));
                Cell(r.P50Ms.ToString("0"), 54);
                Cell(r.P95Ms.ToString("0"), 54);
                Cell(r.MaxLatencyMs.ToString("0"), 60);
                Cell(r.LatencyStdMs.ToString("0.0"), 54);
                Cell(r.MeanBufferMs.ToString("0"), 60);
                Cell(r.MeanDynamicDepth.ToString("0.00"), 56);
                Cell(r.Sends.ToString(), 54);
                EditorGUILayout.EndHorizontal();
            }
        }

        static Color LatencyColor(float ms) => ms >= 300f ? Hot : (ms >= 180f ? Warm : Cool);

        static void Cell(string text, float width, bool header = false)
        {
            EditorGUILayout.LabelField(text, header ? EditorStyles.miniBoldLabel : EditorStyles.miniLabel, GUILayout.Width(width));
        }

        static void ColoredCell(string text, float width, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            EditorGUILayout.LabelField(text, EditorStyles.miniBoldLabel, GUILayout.Width(width));
            GUI.color = prev;
        }

        void ExportCsv()
        {
            string path = EditorUtility.SaveFilePanel("Export Sync Latency Results", "", "BasisSyncLatency.csv", "csv");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, BasisSyncLatencyMatrix.ToCsv(_results), new UTF8Encoding(false));
            Debug.Log($"[BasisSyncLatency] wrote {_results.Count} rows to {path}");
            EditorUtility.RevealInFinder(path);
        }
    }
}
