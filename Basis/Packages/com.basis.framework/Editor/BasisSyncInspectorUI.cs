using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>One validation message for a synced-component inspector.</summary>
public struct BasisSyncIssue
{
    public bool IsError;
    public string Message;
    public BasisSyncIssue(bool isError, string message) { IsError = isError; Message = message; }
    public static BasisSyncIssue Error(string message) => new BasisSyncIssue(true, message);
    public static BasisSyncIssue Warning(string message) => new BasisSyncIssue(false, message);
}

/// <summary>Shared Basis-styled building blocks for the synced-object / synced-transform inspectors.</summary>
public static class BasisSyncInspectorUI
{
    public static readonly Color Accent = new Color(239f / 255f, 40f / 255f, 90f / 255f);
    private static readonly Color HeaderBg = new Color(0f, 0f, 0f, 0.54f);
    private static readonly Color CardBg = new Color(0f, 0f, 0f, 0.30f);
    private static readonly Color Subtle = new Color(0.8f, 0.8f, 0.8f, 1f);
    private static readonly Color ErrorBg = new Color(1f, 0.5f, 0.5f, 0.5f);
    private static readonly Color WarnBg = new Color(0.651f, 0.631f, 0.051f, 0.5f);

    public static VisualElement Header(string title, string subtitle)
    {
        var box = new VisualElement();
        box.style.marginBottom = 10;
        box.style.paddingTop = 8;
        box.style.paddingBottom = 8;
        box.style.paddingLeft = 10;
        box.style.paddingRight = 10;
        box.style.backgroundColor = new StyleColor(HeaderBg);
        box.style.borderBottomWidth = 3;
        box.style.borderBottomColor = new StyleColor(Accent);
        Round(box, 5);

        var titleLabel = new Label(title);
        titleLabel.style.fontSize = 15;
        titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleLabel.style.color = new StyleColor(Color.white);

        var subtitleLabel = new Label(subtitle);
        subtitleLabel.style.fontSize = 11;
        subtitleLabel.style.color = new StyleColor(Subtle);
        subtitleLabel.style.whiteSpace = WhiteSpace.Normal;
        subtitleLabel.style.marginTop = 2;

        box.Add(titleLabel);
        box.Add(subtitleLabel);
        return box;
    }

    public static VisualElement Card(string title)
    {
        var box = new VisualElement();
        box.style.marginBottom = 8;
        box.style.paddingTop = 6;
        box.style.paddingBottom = 8;
        box.style.paddingLeft = 8;
        box.style.paddingRight = 8;
        box.style.backgroundColor = new StyleColor(CardBg);
        box.style.borderBottomWidth = 2;
        box.style.borderBottomColor = new StyleColor(Accent);
        Round(box, 5);

        var titleLabel = new Label(title);
        titleLabel.style.fontSize = 12;
        titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
        titleLabel.style.color = new StyleColor(Accent);
        titleLabel.style.marginBottom = 4;
        box.Add(titleLabel);
        return box;
    }

    /// <summary>Live validation panel (red errors / yellow warnings) that re-evaluates twice a second.</summary>
    public static VisualElement ValidationContainer(Func<List<BasisSyncIssue>> validate)
    {
        var container = new VisualElement();
        container.style.marginBottom = 4;

        void Refresh()
        {
            container.Clear();
            List<BasisSyncIssue> issues = validate != null ? validate() : null;
            if (issues == null || issues.Count == 0) return;

            var errors = issues.Where(i => i.IsError).Select(i => i.Message).ToList();
            var warnings = issues.Where(i => !i.IsError).Select(i => i.Message).ToList();
            if (errors.Count > 0) container.Add(IssuePanel(errors, true));
            if (warnings.Count > 0) container.Add(IssuePanel(warnings, false));
        }

        Refresh();
        container.schedule.Execute(Refresh).Every(500);
        return container;
    }

    private static VisualElement IssuePanel(List<string> messages, bool error)
    {
        var panel = new VisualElement();
        panel.style.backgroundColor = new StyleColor(error ? ErrorBg : WarnBg);
        panel.style.paddingTop = 5;
        panel.style.paddingBottom = 5;
        panel.style.paddingLeft = 6;
        panel.style.paddingRight = 6;
        panel.style.marginBottom = 6;
        panel.style.borderBottomWidth = 2;
        panel.style.borderBottomColor = new StyleColor(error ? Color.red : Color.yellow);
        Round(panel, 5);

        var label = new Label(string.Join("\n", messages.Select(m => "• " + m)));
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.color = new StyleColor(Color.white);
        panel.Add(label);
        return panel;
    }

    public static VisualElement NetworkingCard(SerializedObject so)
    {
        VisualElement card = Card("Networking");
        card.Add(RateSlider(so));
        card.Add(KeyframeSlider(so));
        card.Add(new PropertyField(so.FindProperty("Delivery"), "Delta Delivery"));
        card.Add(new PropertyField(so.FindProperty("KeyframeDelivery")));

        var p2p = new Toggle("Use Direct P2P If Able") { bindingPath = "UseDirectP2P" };
        var forceP2P = new PropertyField(so.FindProperty("ForceP2POnly"), "Force P2P Only (No Server Fallback)");
        var overrideP2P = new Toggle("Override P2P Rate") { bindingPath = "OverrideP2PRate" };
        var p2pRate = RateSlider(so, "P2PSendIntervalSeconds", "P2P Send Rate (Hz)", 120f);
        var p2pKey = KeyframeSlider(so, "P2PKeyframeIntervalSeconds", "P2P Keyframe Interval (s)");

        bool useP2P0 = so.FindProperty("UseDirectP2P").boolValue;
        bool ovr0 = so.FindProperty("OverrideP2PRate").boolValue;
        forceP2P.SetEnabled(useP2P0);
        overrideP2P.SetEnabled(useP2P0);
        p2pRate.SetEnabled(useP2P0 && ovr0);
        p2pKey.SetEnabled(useP2P0 && ovr0);

        p2p.RegisterValueChangedCallback(evt =>
        {
            forceP2P.SetEnabled(evt.newValue);
            overrideP2P.SetEnabled(evt.newValue);
            p2pRate.SetEnabled(evt.newValue && overrideP2P.value);
            p2pKey.SetEnabled(evt.newValue && overrideP2P.value);
        });
        overrideP2P.RegisterValueChangedCallback(evt =>
        {
            p2pRate.SetEnabled(p2p.value && evt.newValue);
            p2pKey.SetEnabled(p2p.value && evt.newValue);
        });

        card.Add(p2p);
        card.Add(forceP2P);
        card.Add(overrideP2P);
        card.Add(p2pRate);
        card.Add(p2pKey);

        card.Add(new PropertyField(so.FindProperty("ContinuousEpsilon")));
        card.Add(new PropertyField(so.FindProperty("DistanceReduction")));
        card.Add(new PropertyField(so.FindProperty("RelevanceCulling")));
        card.Add(new PropertyField(so.FindProperty("RelevanceRadius")));
        return card;
    }

    public static VisualElement SmoothingCard(SerializedObject so)
    {
        VisualElement card = Card("Smoothing");
        card.Add(new PropertyField(so.FindProperty("Extrapolate")));
        card.Add(new PropertyField(so.FindProperty("MaxExtrapolationSeconds")));
        card.Add(new PropertyField(so.FindProperty("UseTeleportThreshold")));
        card.Add(new PropertyField(so.FindProperty("TeleportThreshold")));
        return card;
    }

    public static VisualElement RateSlider(SerializedObject so) => RateSlider(so, "SendIntervalSeconds", "Send Rate (Hz)", 60f);

    public static VisualElement RateSlider(SerializedObject so, string propName, string label, float maxHz)
    {
        SerializedProperty prop = so.FindProperty(propName);
        float hz = prop.floatValue > 0f ? 1f / prop.floatValue : 20f;

        var slider = new Slider(label, 1f, maxHz) { value = hz, showInputField = true };
        slider.RegisterValueChangedCallback(evt =>
        {
            float h = Mathf.Clamp(evt.newValue, 1f, maxHz);
            so.Update();
            prop.floatValue = 1f / h;
            so.ApplyModifiedProperties();
        });
        return slider;
    }

    public static VisualElement KeyframeSlider(SerializedObject so) => KeyframeSlider(so, "KeyframeIntervalSeconds", "Keyframe Interval (s)");

    public static VisualElement KeyframeSlider(SerializedObject so, string propName, string label)
    {
        SerializedProperty prop = so.FindProperty(propName);

        var slider = new Slider(label, 0.1f, 5f) { value = prop.floatValue, showInputField = true };
        slider.RegisterValueChangedCallback(evt =>
        {
            so.Update();
            prop.floatValue = evt.newValue;
            so.ApplyModifiedProperties();
        });
        return slider;
    }

    private static void Round(VisualElement e, float r)
    {
        e.style.borderTopLeftRadius = r;
        e.style.borderTopRightRadius = r;
        e.style.borderBottomLeftRadius = r;
        e.style.borderBottomRightRadius = r;
    }
}
