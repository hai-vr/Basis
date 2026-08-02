using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The hand-instrumented breakdown of BasisEventDriver's tick: per-callback cost, the job waits the
/// main thread actually paid for, and the counts (receivers, remotes, LOD skips) a marker name can't
/// carry. Reads <see cref="BasisEventDriverProfilerData"/>, which the driver only fills while this
/// tab has it enabled.
/// </summary>
public sealed class BasisEventDriverProfilerTab : BasisEditorTabPage
{
    private bool _showLateUpdate = true;
    private bool _showNetworkDeep = true;
    private bool _showTransmitSim = true;
    private bool _showRemoteAudio = true;
    private bool _showRemoteFace = true;
    private bool _showLocal = true;
    private bool _showPhysics = true;
    private bool _showMisc = true;
    private bool _showPoseLod = true;
    private bool _showGraph = true;
    private bool _showThreading = true;

    private static readonly Color BarColor = new Color(0.2f, 0.7f, 1f, 0.8f);
    private static readonly Color GraphBg = new Color(0.12f, 0.12f, 0.12f, 1f);

    private static readonly Color ColNetwork = new Color(1f, 0.4f, 0.4f, 0.9f);
    private static readonly Color ColRemoteAudio = new Color(0.4f, 1f, 0.4f, 0.9f);
    private static readonly Color ColRemoteFace = new Color(0.4f, 0.4f, 1f, 0.9f);
    private static readonly Color ColLocal = new Color(1f, 1f, 0.3f, 0.9f);
    private static readonly Color ColJiggle = new Color(1f, 0.5f, 1f, 0.9f);
    private static readonly Color ColTotal = new Color(1f, 1f, 1f, 0.5f);

    // Wide enough for "CompleteScheduledRemoteLerp" — the longest label in the breakdown.
    private const float k_LabelWidth = 210f;

    private float _budgetMs = 11.1f;

    public override string Title => "EventDriver";

    public override string Subtitle =>
        "Per-callback cost of the central tick, so an expensive subscriber is obvious.";

    public override void OnEnable() => BasisEventDriverProfilerData.Enabled = true;

    public override void OnDisable() => BasisEventDriverProfilerData.Enabled = false;

    public override void Draw()
    {
        if (!Application.isPlaying)
        {
            BasisEditorUI.Help("Enter Play Mode to see live data.", MessageType.Info);
            return;
        }

        using (BasisEditorUI.Card())
        {
            BasisEditorUI.Row("Frame", BasisEventDriverProfilerData.FrameCount.ToString());
            _budgetMs = EditorGUILayout.Slider("Budget (ms)", _budgetMs, 2f, 33.3f);
        }

        Section(ref _showLateUpdate, "LateUpdate Overview", DrawLateUpdate);
        Section(ref _showNetworkDeep, "Network Apply Breakdown", DrawNetworkDeep);
        Section(ref _showTransmitSim, "TransmissionResults.Simulate()", DrawTransmitSim);
        Section(ref _showPoseLod, "Pose LOD Diagnostics", DrawPoseLod);
        Section(ref _showRemoteAudio, "Remote Audio", DrawRemoteAudio);
        Section(ref _showRemoteFace, "Remote Face", DrawRemoteFace);
        Section(ref _showLocal, "Local Player", DrawLocal);
        Section(ref _showPhysics, "JigglePhysics", DrawPhysics);
        Section(ref _showMisc, "Misc", DrawMisc);
        Section(ref _showThreading, "Job Completion Status", DrawThreading);
        Section(ref _showGraph, "Frame History", DrawGraph);
    }

    // ── sections ────────────────────────────────────────────────────────────

    private void DrawLateUpdate()
    {
        TimingRow("LateUpdate Total", BasisEventDriverProfilerData.LateUpdateTotalMs, _budgetMs);
        TimingRow("OnBeforeRender", BasisEventDriverProfilerData.OnBeforeRenderMs, _budgetMs * 0.2f);
        TimingRow("Network Apply (group)", BasisEventDriverProfilerData.NetworkApplyMs, 3f);
        TimingRow("Network Transmit", BasisEventDriverProfilerData.NetworkTransmitMs, 1f);
        TimingRow("Remote Audio (sim+apply)", BasisEventDriverProfilerData.RemoteAudioSimulateMs + BasisEventDriverProfilerData.RemoteAudioApplyMs, 2f);
        TimingRow("Remote Face (sim+apply)", BasisEventDriverProfilerData.RemoteFaceSimulateMs + BasisEventDriverProfilerData.RemoteFaceApplyMs, 2f);
        TimingRow("Local Player", BasisEventDriverProfilerData.LocalPlayerMs, 2f);
        TimingRow("JigglePhysics (all)", BasisEventDriverProfilerData.JiggleScheduleMs + BasisEventDriverProfilerData.JigglePoseMs + BasisEventDriverProfilerData.JiggleCompletePoseMs, 3f);
    }

    private void DrawNetworkDeep()
    {
        TimingRow("TransmitOwnedPickups", BasisEventDriverProfilerData.Net_TransmitPickupsMs, 0.5f);
        TimingRow("FireJustBeforeNetworkApply", BasisEventDriverProfilerData.Net_FireBeforeApplyMs, 0.5f);
        TimingRow("SimulateNetworkApply", BasisEventDriverProfilerData.Net_SimulateNetworkApplyMs, 3f);
        TimingRow("CompleteScheduledRemoteLerp", BasisEventDriverProfilerData.Net_CompleteRemoteLerpMs, 1f);

        EditorGUILayout.Space(4);
        BasisEditorUI.SectionTitle("Inside SimulateNetworkApply");

        TimingRow("Interpolation Complete (stall)", BasisEventDriverProfilerData.Net_RemoteDriverApplyMs, 1f);
        JobStatusRow("Interpolation Job (from Update)", BasisEventDriverProfilerData.Net_InterpolationJobWasIncomplete);

        BasisEditorUI.Row("Receiver Count", BasisEventDriverProfilerData.Net_ReceiverCount.ToString());
        TimingRow("Receiver Apply Loop", BasisEventDriverProfilerData.Net_ReceiverApplyLoopMs, 2f);
        if (BasisEventDriverProfilerData.Net_ReceiverCount > 0)
        {
            double perReceiver = BasisEventDriverProfilerData.Net_ReceiverApplyLoopMs / BasisEventDriverProfilerData.Net_ReceiverCount;
            BasisEditorUI.Row("Per-Receiver Avg", $"{perReceiver:F4} ms");
        }

        TimingRow("BoneJob Schedule", BasisEventDriverProfilerData.Net_BoneJobScheduleMs, 0.5f);
        TimingRow("BoneJob Complete (stall)", BasisEventDriverProfilerData.Net_BoneJobCompleteMs, 1f);
        JobStatusRow("BoneJob", BasisEventDriverProfilerData.Net_BoneJobWasIncomplete);

        // How much of ApplySkeletonRotationsJob the write-skip is actually eliding. A low skip
        // share on a crowded instance means the bones genuinely moved; a high one means the
        // transform writes (and their subtree dirtying) are being avoided.
        int boneTotal = BasisEventDriverProfilerData.BoneWrite_Total;
        if (boneTotal > 0)
        {
            int boneSkipped = boneTotal - BasisEventDriverProfilerData.BoneWrite_Written;
            float skipPct = 100f * boneSkipped / boneTotal;
            BasisEditorUI.Row("Bone Writes Skipped", $"{boneSkipped} / {boneTotal}  ({skipPct:F0}%)");
        }

        EditorGUILayout.Space(2);
        double totalStall = BasisEventDriverProfilerData.Net_RemoteDriverApplyMs + BasisEventDriverProfilerData.Net_BoneJobCompleteMs;
        BasisEditorUI.Row("Total Job Stall Time", $"{totalStall:F3} ms");
        BasisEditorUI.Row("Total Main Thread Work", $"{BasisEventDriverProfilerData.Net_ReceiverApplyLoopMs:F3} ms");
    }

    private void DrawTransmitSim()
    {
        BasisEditorUI.PillRow("Ran This Tick",
            BasisEventDriverProfilerData.Net_TransmitSimRanThisTick ? "YES" : "NO",
            BasisEditorUI.OnOff(BasisEventDriverProfilerData.Net_TransmitSimRanThisTick));
        if (!BasisEventDriverProfilerData.Net_TransmitSimRanThisTick) return;

        TimingRow("Fill Positions", BasisEventDriverProfilerData.Net_TransmitSim_FillPositionsMs, 0.5f);
        TimingRow("Job Schedule", BasisEventDriverProfilerData.Net_TransmitSim_JobScheduleMs, 0.2f);
        TimingRow("Avatar Compress", BasisEventDriverProfilerData.Net_TransmitSim_CompressMs, 1f);
        TimingRow("Job Complete (stall)", BasisEventDriverProfilerData.Net_TransmitSim_JobCompleteMs, 1f);
        TimingRow("Post-Process Loop", BasisEventDriverProfilerData.Net_TransmitSim_PostProcessMs, 1f);
        TimingRow("Talking Points", BasisEventDriverProfilerData.Net_TransmitSim_TalkingPointsMs, 0.2f);
    }

    private void DrawPoseLod()
    {
        float bias = BasisEventDriverProfilerData.PoseLod_Bias;
        int applied = BasisEventDriverProfilerData.PoseLod_Applied;
        int skipped = BasisEventDriverProfilerData.PoseLod_Skipped;
        int total = applied + skipped;

        BasisEditorUI.Row("Bias (setting)", $"{bias:F1}");
        BasisEditorUI.PillRow("Active", bias > 0f ? "YES" : "NO", BasisEditorUI.OnOff(bias > 0f));

        var skipByLod = SMModuleDistanceBasedReductions.PoseSkipByLod;
        BasisEditorUI.Row("Skip Rates [L0,L1,L2,L3]", $"[{skipByLod[0]}, {skipByLod[1]}, {skipByLod[2]}, {skipByLod[3]}]");

        EditorGUILayout.Space(4);
        BasisEditorUI.Row("LOD 0 (closest)", BasisEventDriverProfilerData.PoseLod_Lod0.ToString());
        BasisEditorUI.Row("LOD 1", BasisEventDriverProfilerData.PoseLod_Lod1.ToString());
        BasisEditorUI.Row("LOD 2", BasisEventDriverProfilerData.PoseLod_Lod2.ToString());
        BasisEditorUI.Row("LOD 3 (furthest)", BasisEventDriverProfilerData.PoseLod_Lod3.ToString());

        EditorGUILayout.Space(4);
        BasisEditorUI.SectionTitle("This Frame");
        BasisEditorUI.Row("SetHumanPose Applied", applied.ToString());
        BasisEditorUI.Row("SetHumanPose Skipped", skipped.ToString());
        if (total > 0)
        {
            float pct = (skipped / (float)total) * 100f;
            BasisEditorUI.Bar(null, skipped / (float)total, $"{pct:F0}% skipped",
                pct > 50 ? BasisEditorUI.Good : (pct > 10 ? BasisEditorUI.Warn : BarColor));
        }

        if (bias > 0f && skipped == 0 && total > 0)
        {
            BasisEditorUI.Help(
                "Bias is set but nothing is being skipped.\n" +
                "Check that CurrentLodLevel is being set on remote players.\n" +
                "If all players are LOD 0, nothing will be skipped.",
                MessageType.Warning);
        }
    }

    private void DrawRemoteAudio()
    {
        BasisEditorUI.Row("Driver Count", BasisEventDriverProfilerData.RemoteAudioDriverCount.ToString());
        TimingRow("Simulate (viseme decode)", BasisEventDriverProfilerData.RemoteAudioSimulateMs, 1f);
        TimingRow("Apply (viseme write)", BasisEventDriverProfilerData.RemoteAudioApplyMs, 1f);
        if (BasisEventDriverProfilerData.RemoteAudioDriverCount > 0)
        {
            double perDriver = (BasisEventDriverProfilerData.RemoteAudioSimulateMs + BasisEventDriverProfilerData.RemoteAudioApplyMs) / BasisEventDriverProfilerData.RemoteAudioDriverCount;
            BasisEditorUI.Row("Per-Driver Avg", $"{perDriver:F4} ms");
        }
    }

    private void DrawRemoteFace()
    {
        BasisEditorUI.Row("Remote Count", BasisEventDriverProfilerData.RemoteFace_Count.ToString());
        TimingRow("Simulate (job schedule)", BasisEventDriverProfilerData.RemoteFaceSimulateMs, 0.5f);
        TimingRow("Apply Total", BasisEventDriverProfilerData.RemoteFaceApplyMs, 2f);

        EditorGUILayout.Space(2);
        BasisEditorUI.SectionTitle("Inside Apply");
        TimingRow("Job Complete (stall)", BasisEventDriverProfilerData.RemoteFace_JobCompleteMs, 1f);
        TimingRow("Eye+Blink Write Loop", BasisEventDriverProfilerData.RemoteFace_EyeWriteMs, 1f);
        BasisEditorUI.Row("Blink Mesh Writes", BasisEventDriverProfilerData.RemoteFace_BlinkWriteCount.ToString());
        if (BasisEventDriverProfilerData.RemoteFace_Count > 0)
        {
            double perRemote = BasisEventDriverProfilerData.RemoteFace_EyeWriteMs / BasisEventDriverProfilerData.RemoteFace_Count;
            BasisEditorUI.Row("Per-Remote Avg", $"{perRemote:F4} ms");
        }
        JobStatusRow("Face Job", BasisEventDriverProfilerData.RemoteFaceJobWasIncomplete);
    }

    private void DrawLocal()
    {
        TimingRow("Local Player Total", BasisEventDriverProfilerData.LocalPlayerMs, 2f);
        TimingRow("Microphone", BasisEventDriverProfilerData.MicrophoneMs, 1f);
        TimingRow("Device Management", BasisEventDriverProfilerData.DeviceManagementMs, 1f);
    }

    private void DrawPhysics()
    {
        TimingRow("Schedule", BasisEventDriverProfilerData.JiggleScheduleMs, 2f);
        TimingRow("Pose", BasisEventDriverProfilerData.JigglePoseMs, 1f);
        TimingRow("Complete Pose (stall)", BasisEventDriverProfilerData.JiggleCompletePoseMs, 2f);
    }

    private void DrawMisc()
    {
        TimingRow("NamePlate Schedule", BasisEventDriverProfilerData.NamePlateScheduleMs, 0.5f);
        TimingRow("NamePlate Complete", BasisEventDriverProfilerData.NamePlateCompleteMs, 0.5f);
        TimingRow("BTween", BasisEventDriverProfilerData.BTweenMs, 0.5f);
        TimingRow("Shadow Clone BS", BasisEventDriverProfilerData.ShadowCloneMs, 0.5f);
    }

    private void DrawThreading()
    {
        JobStatusRow("Interpolation Job", BasisEventDriverProfilerData.Net_InterpolationJobWasIncomplete);
        JobStatusRow("BoneJob", BasisEventDriverProfilerData.Net_BoneJobWasIncomplete);
        JobStatusRow("Remote Face Job", BasisEventDriverProfilerData.RemoteFaceJobWasIncomplete);
        JobStatusRow("NamePlate Job", BasisEventDriverProfilerData.NamePlateJobWasIncomplete);
        EditorGUILayout.Space(2);
        BasisEditorUI.Note("STALLED = main thread waited for job to finish.");
        BasisEditorUI.Note("Ideally all jobs complete before their Apply call.");
    }

    // ── widgets ─────────────────────────────────────────────────────────────

    private void Section(ref bool expanded, string title, Action drawContent)
    {
        if (BasisEditorUI.BeginFoldout(ref expanded, title))
        {
            drawContent();
        }
        BasisEditorUI.EndFoldout();
    }

    private void TimingRow(string label, double ms, float warnThreshold)
    {
        Color barCol = ms > warnThreshold
            ? (ms > warnThreshold * 2 ? BasisEditorUI.Bad : BasisEditorUI.Warn)
            : BarColor;
        BasisEditorUI.Bar(label, (float)(ms / _budgetMs), $"{ms:F3} ms", barCol, k_LabelWidth);
    }

    private void JobStatusRow(string label, bool wasIncomplete) =>
        BasisEditorUI.PillRow(label, wasIncomplete ? "STALLED" : "OK",
            wasIncomplete ? BasisEditorUI.State.Warn : BasisEditorUI.State.Good);

    private void DrawGraph()
    {
        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal();
        DrawLegendSwatch(ColTotal, "Total");
        DrawLegendSwatch(ColNetwork, "Network");
        DrawLegendSwatch(ColLocal, "Local");
        DrawLegendSwatch(ColRemoteAudio, "Audio");
        DrawLegendSwatch(ColRemoteFace, "Face");
        DrawLegendSwatch(ColJiggle, "Jiggle");
        EditorGUILayout.EndHorizontal();

        Rect graphRect = GUILayoutUtility.GetRect(0, 140, GUILayout.ExpandWidth(true));
        BasisEditorUI.Fill(graphRect, GraphBg, 4f);

        int histLen = BasisEventDriverProfilerData.HistorySize;
        int current = BasisEventDriverProfilerData.HistoryIndex;
        if (current == 0) return;

        int drawCount = Mathf.Min(current, (int)graphRect.width);
        float maxMs = _budgetMs * 2f;
        float budgetY = graphRect.yMax - ((_budgetMs / maxMs) * graphRect.height);

        Handles.BeginGUI();

        Handles.color = new Color(1f, 0f, 0f, 0.5f);
        Handles.DrawLine(new Vector3(graphRect.x, budgetY), new Vector3(graphRect.xMax, budgetY));

        DrawGraphLayer(graphRect, BasisEventDriverProfilerData.LateUpdateHistory, histLen, current, drawCount, maxMs, ColTotal);
        DrawGraphLayer(graphRect, BasisEventDriverProfilerData.NetworkApplyHistory, histLen, current, drawCount, maxMs, ColNetwork);
        DrawGraphLayer(graphRect, BasisEventDriverProfilerData.LocalPlayerHistory, histLen, current, drawCount, maxMs, ColLocal);
        DrawGraphLayer(graphRect, BasisEventDriverProfilerData.RemoteAudioHistory, histLen, current, drawCount, maxMs, ColRemoteAudio);
        DrawGraphLayer(graphRect, BasisEventDriverProfilerData.RemoteFaceHistory, histLen, current, drawCount, maxMs, ColRemoteFace);
        DrawGraphLayer(graphRect, BasisEventDriverProfilerData.JiggleHistory, histLen, current, drawCount, maxMs, ColJiggle);

        Handles.EndGUI();

        EditorGUI.LabelField(new Rect(graphRect.x + 2, graphRect.y, 100, 16), $"{maxMs:F0} ms", EditorStyles.miniLabel);
        EditorGUI.LabelField(new Rect(graphRect.x + 2, budgetY - 14, 100, 16), $"budget {_budgetMs:F1} ms", EditorStyles.miniLabel);
    }

    private void DrawGraphLayer(Rect rect, double[] history, int histLen, int current, int drawCount, float maxMs, Color color)
    {
        Handles.color = color;
        for (int i = 1; i < drawCount; i++)
        {
            int idx0 = (current - drawCount + i - 1) % histLen;
            int idx1 = (current - drawCount + i) % histLen;
            if (idx0 < 0) idx0 += histLen;
            if (idx1 < 0) idx1 += histLen;

            float x0 = rect.x + (i - 1);
            float x1 = rect.x + i;
            float y0 = rect.yMax - ((float)(history[idx0] / maxMs) * rect.height);
            float y1 = rect.yMax - ((float)(history[idx1] / maxMs) * rect.height);

            y0 = Mathf.Clamp(y0, rect.y, rect.yMax);
            y1 = Mathf.Clamp(y1, rect.y, rect.yMax);

            Handles.DrawLine(new Vector3(x0, y0), new Vector3(x1, y1));
        }
    }

    private void DrawLegendSwatch(Color color, string label)
    {
        Rect r = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
        BasisEditorUI.Fill(r, color, 2f);
        EditorGUILayout.LabelField(label, EditorStyles.miniLabel, GUILayout.Width(65));
    }
}
