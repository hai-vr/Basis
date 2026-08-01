using Basis.Scripts.Drivers;
using TMPro;
using UnityEngine;

namespace Basis.BasisUI
{
    public static class SettingsProviderFrameBottleneck
    {
        private const int RefreshIntervalTicks = 15;
        private const int TicksBeforeFreeze = 2;
        private const int RefreshesBeforeVerdictChange = 4;
        private const BasisFrameBottleneckKind UnsetKind = (BasisFrameBottleneckKind)(-1);

        private static readonly BasisFrameBottleneckKind[] AllKinds =
        {
            BasisFrameBottleneckKind.Measuring,
            BasisFrameBottleneckKind.Cpu,
            BasisFrameBottleneckKind.Gpu,
            BasisFrameBottleneckKind.Balanced,
            BasisFrameBottleneckKind.FrameCap,
            BasisFrameBottleneckKind.NoGpuTimer
        };

        private static PanelElementDescriptor _group;
        private static PanelElementDescriptor _frameField;
        private static PanelElementDescriptor _cpuField;
        private static PanelElementDescriptor _gpuField;
        private static BasisPanelTint.Handle _tint;

        private static int _tickCounter;
        private static int _refreshCount;
        private static bool _subscribed;
        private static bool _layoutFrozen;
        private static BasisFrameBottleneckKind _shownKind = UnsetKind;
        private static BasisFrameBottleneckKind _pendingKind = UnsetKind;
        private static int _pendingHolds;

        public static void BuildFrameBottleneckGroup(RectTransform container)
        {
            PanelElementDescriptor group = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, container);
            group.SetTitle(BasisLocalization.Get("settings.graphics.bottleneck.title"));
            group.SetDescription(BasisLocalization.Get("settings.graphics.bottleneck.measuring"));
            group.SetTooltip(BasisLocalization.Get("settings.graphics.bottleneck.tooltip"));

            PanelElementDescriptor frameField = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, group.ContentParent);
            frameField.SetTitle(BasisLocalization.Get("settings.graphics.bottleneck.frame"));
            frameField.SetDescription("...");

            PanelElementDescriptor cpuField = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, group.ContentParent);
            cpuField.SetTitle(BasisLocalization.Get("settings.graphics.bottleneck.cpu"));
            cpuField.SetDescription("...");

            PanelElementDescriptor gpuField = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, group.ContentParent);
            gpuField.SetTitle(BasisLocalization.Get("settings.graphics.bottleneck.gpu"));
            gpuField.SetDescription("...");

            group.IsolateAsCanvas();

            Attach(group, frameField, cpuField, gpuField);
            group.OnInstanceReleased += () => Detach(group);
        }

        private static void Attach(
            PanelElementDescriptor group,
            PanelElementDescriptor frameField,
            PanelElementDescriptor cpuField,
            PanelElementDescriptor gpuField)
        {
            _group = group;
            _frameField = frameField;
            _cpuField = cpuField;
            _gpuField = gpuField;

            _frameField.DisableRichText();
            _cpuField.DisableRichText();
            _gpuField.DisableRichText();

            _tint = BasisPanelTint.Capture(group);
            _tickCounter = 0;
            _refreshCount = 0;
            _layoutFrozen = false;
            _shownKind = UnsetKind;
            _pendingKind = UnsetKind;
            _pendingHolds = 0;

            BasisFrameBottleneck.Reset();

            if (!_subscribed)
            {
                BasisFrameClock.OnTick += OnTick;
                BasisFrameClock.AddRequest();
                _subscribed = true;
            }

            Refresh();
        }

        private static void Detach(PanelElementDescriptor group)
        {
            if (!ReferenceEquals(_group, group))
            {
                return;
            }

            Unsubscribe();

            _group = null;
            _frameField = null;
            _cpuField = null;
            _gpuField = null;
            _tint = null;
        }

        private static void Unsubscribe()
        {
            if (!_subscribed)
            {
                return;
            }

            BasisFrameClock.OnTick -= OnTick;
            BasisFrameClock.RemoveRequest();
            _subscribed = false;
        }

        private static void OnTick()
        {
            if (_group == null)
            {
                Unsubscribe();
                return;
            }

            if (!_group.gameObject.activeInHierarchy)
            {
                return;
            }

            BasisFrameBottleneck.Sample();

            if (++_tickCounter < RefreshIntervalTicks)
            {
                return;
            }
            _tickCounter = 0;
            Refresh();
        }

        private static void Refresh()
        {
            if (_group == null)
            {
                return;
            }

            BasisFrameBottleneckReading reading = BasisFrameBottleneck.Read(_shownKind);

            if (ShouldCommitVerdict(reading.Kind))
            {
                _shownKind = reading.Kind;
                _group.SetTitle(TitleFor(reading.Kind));
                _group.SetDescription(BasisLocalization.Get(AdviceKeyFor(reading.Kind)));
                ApplyTint(reading.Kind);
            }

            if (_shownKind == BasisFrameBottleneckKind.Measuring)
            {
                string pending = BasisLocalization.Get("settings.graphics.bottleneck.pending");
                _frameField.SetDescription(pending);
                _cpuField.SetDescription(pending);
                _gpuField.SetDescription(pending);
                return;
            }

            double fps = reading.FrameMs > 0.0 ? 1000.0 / reading.FrameMs : 0.0;
            _frameField.SetDescription(reading.TargetMs > 0.0
                ? BasisLocalization.Get("settings.graphics.bottleneck.frame.capped",
                    reading.FrameMs, fps, 1000.0 / reading.TargetMs)
                : BasisLocalization.Get("settings.graphics.bottleneck.frame.value",
                    reading.FrameMs, fps));

            _cpuField.SetDescription(BasisLocalization.Get("settings.graphics.bottleneck.cpu.value",
                reading.CpuBusyMs, reading.MainThreadMs, reading.RenderThreadMs, reading.PresentWaitMs));

            _gpuField.SetDescription(reading.HasGpuTiming
                ? BasisLocalization.Get("settings.graphics.bottleneck.gpu.value", reading.GpuMs)
                : BasisLocalization.Get("settings.graphics.bottleneck.gpu.unavailable"));

            if (_layoutFrozen)
            {
                return;
            }

            if (++_refreshCount < TicksBeforeFreeze)
            {
                return;
            }

            _layoutFrozen = true;
            _frameField.FreezeLayoutSize(110f);
            _cpuField.FreezeLayoutSize(150f);
            _gpuField.FreezeLayoutSize(110f);
            FreezeGroupLayout();
        }

        private static bool ShouldCommitVerdict(BasisFrameBottleneckKind kind)
        {
            if (kind == _shownKind)
            {
                _pendingKind = UnsetKind;
                _pendingHolds = 0;
                return false;
            }

            if (_shownKind == UnsetKind || _shownKind == BasisFrameBottleneckKind.Measuring)
            {
                _pendingKind = UnsetKind;
                _pendingHolds = 0;
                return true;
            }

            if (_pendingKind != kind)
            {
                _pendingKind = kind;
                _pendingHolds = 1;
                return false;
            }

            if (++_pendingHolds < RefreshesBeforeVerdictChange)
            {
                return false;
            }

            _pendingKind = UnsetKind;
            _pendingHolds = 0;
            return true;
        }

        private static void FreezeGroupLayout()
        {
            string title = _group.Title;
            string description = _group.Description;

            _group.SetTitle(TallestOf(_group.HasTitle ? _group.TitleLabel : null, true));
            _group.SetDescription(TallestOf(_group.HasDescription ? _group.DescriptionLabel : null, false));

            _group.FreezeLayoutSize();

            _group.SetTitle(title);
            _group.SetDescription(description);
        }

        private static string TallestOf(TextMeshProUGUI label, bool titles)
        {
            string tallest = titles ? TitleFor(AllKinds[0]) : BasisLocalization.Get(AdviceKeyFor(AllKinds[0]));
            float width = label != null ? label.rectTransform.rect.width : 0f;
            float best = width > 0f ? label.GetPreferredValues(tallest, width, 0f).y : tallest.Length;

            for (int index = 1; index < AllKinds.Length; index++)
            {
                BasisFrameBottleneckKind kind = AllKinds[index];
                string candidate = titles ? TitleFor(kind) : BasisLocalization.Get(AdviceKeyFor(kind));
                float height = width > 0f ? label.GetPreferredValues(candidate, width, 0f).y : candidate.Length;
                if (height > best)
                {
                    best = height;
                    tallest = candidate;
                }
            }

            return tallest;
        }

        private static string TitleFor(BasisFrameBottleneckKind kind)
        {
            string baseTitle = BasisLocalization.Get("settings.graphics.bottleneck.title");
            switch (kind)
            {
                case BasisFrameBottleneckKind.Cpu:
                case BasisFrameBottleneckKind.Gpu:
                case BasisFrameBottleneckKind.Balanced:
                case BasisFrameBottleneckKind.FrameCap:
                case BasisFrameBottleneckKind.NoGpuTimer:
                    return BasisLocalization.Get("settings.graphics.bottleneck.title.verdict",
                        baseTitle, BasisLocalization.Get(VerdictKeyFor(kind)));
                default:
                    return baseTitle;
            }
        }

        private static string VerdictKeyFor(BasisFrameBottleneckKind kind)
        {
            switch (kind)
            {
                case BasisFrameBottleneckKind.Cpu: return "settings.graphics.bottleneck.verdict.cpu";
                case BasisFrameBottleneckKind.Gpu: return "settings.graphics.bottleneck.verdict.gpu";
                case BasisFrameBottleneckKind.Balanced: return "settings.graphics.bottleneck.verdict.balanced";
                case BasisFrameBottleneckKind.FrameCap: return "settings.graphics.bottleneck.verdict.frameCap";
                case BasisFrameBottleneckKind.NoGpuTimer: return "settings.graphics.bottleneck.verdict.noGpuTimer";
                default: return "settings.graphics.bottleneck.verdict.measuring";
            }
        }

        private static string AdviceKeyFor(BasisFrameBottleneckKind kind)
        {
            switch (kind)
            {
                case BasisFrameBottleneckKind.Cpu: return "settings.graphics.bottleneck.advice.cpu";
                case BasisFrameBottleneckKind.Gpu: return "settings.graphics.bottleneck.advice.gpu";
                case BasisFrameBottleneckKind.Balanced: return "settings.graphics.bottleneck.advice.balanced";
                case BasisFrameBottleneckKind.FrameCap: return "settings.graphics.bottleneck.advice.frameCap";
                case BasisFrameBottleneckKind.NoGpuTimer: return "settings.graphics.bottleneck.advice.noGpuTimer";
                default: return "settings.graphics.bottleneck.measuring";
            }
        }

        private static void ApplyTint(BasisFrameBottleneckKind kind)
        {
            BasisPanelTint.Apply(_tint, SeverityFor(kind));
        }

        private static BasisPanelSeverity SeverityFor(BasisFrameBottleneckKind kind)
        {
            switch (kind)
            {
                case BasisFrameBottleneckKind.FrameCap: return BasisPanelSeverity.Calm;
                case BasisFrameBottleneckKind.Balanced: return BasisPanelSeverity.Caution;
                case BasisFrameBottleneckKind.Cpu:
                case BasisFrameBottleneckKind.Gpu: return BasisPanelSeverity.Hot;
                default: return BasisPanelSeverity.None;
            }
        }
    }
}
