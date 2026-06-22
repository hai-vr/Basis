using Basis;
using Basis.BasisUI;
using Basis.Scripts.Avatar;
using UnityEngine;
using UnityEngine.UI;
public static class SettingsProviderPerformanceLimits
{
    private static RectTransform _layoutRoot;

    public static PanelTabPage PerformanceLimitsTab(PanelTabGroup tabGroup)
    {
        PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
        PanelElementDescriptor descriptor = tab.Descriptor;
        descriptor.SetIcon(AddressableAssets.Sprites.Settings);
        descriptor.SetTitle(BasisLocalization.Get("settings.tab.performancelimits"));

        RectTransform container = descriptor.ContentParent;
        BuildPerformanceLimitsContent(container);

        SettingsProvider.AddResetPageButton(container, "settings.tab.performancelimits", ResetPerformanceLimitDefaults);

        descriptor.ForceRebuild();
        return tab;
    }
    public static void BuildPerformanceLimitsContent(RectTransform container, bool wrapInSection = false)
    {
        _layoutRoot = container;
        RectTransform contentParent = container;

        PanelSectionToggle performanceToggle = null;
        PanelElementDescriptor performanceGroup = null;
        if (wrapInSection)
        {
            performanceToggle = PanelSectionToggle.CreateNewEntry(container);
            performanceGroup = CreateCollapsibleContentGroup(
                performanceToggle,
                container,
                "settings.perf.intro.title",
                false);
            contentParent = performanceGroup.ContentParent;
        }

        PanelElementDescriptor bypassGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, contentParent);
        bypassGroup.SetTitle(BasisLocalization.Get("settings.perf.sessionBypass.title"));

        PanelToggle bypassToggle = PanelToggle.CreateNewEntry(bypassGroup.ContentParent);
        bypassToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.perf.sessionBypass.toggle"));
        bypassToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.perf.sessionBypass.toggle.tooltip"));
        bypassToggle.SetValueWithoutNotify(BasisAvatarPerformanceLimits.BypassAllLimits);
        bypassToggle.OnValueChanged += on =>
        {
            BasisAvatarPerformanceLimits.BypassAllLimits = on;
        };

        PanelSectionToggle geometryToggle = PanelSectionToggle.CreateNewEntry(contentParent);
        PanelElementDescriptor geometry = CreateCollapsibleContentGroup(
            geometryToggle,
            contentParent,
            "settings.perf.group.geometry");

        AddLimitPair(geometry.ContentParent,
            BasisLocalization.Get("settings.perf.triangles.toggle"),
            BasisLocalization.Get("settings.perf.triangles.slider"),
            BasisSettingsDefaults.UsePerfLimitTriangles,
            BasisSettingsDefaults.MaxPerfTriangles,
            1000, 2_000_000, true, displayMode: ValueDisplayMode.Compact,
            toggleTooltip: BasisLocalization.Get("settings.perf.triangles.toggle.tooltip"),
            sliderTooltip: BasisLocalization.Get("settings.perf.triangles.slider.tooltip"));

        AddLimitPair(geometry.ContentParent,
            BasisLocalization.Get("settings.perf.boundsSize.toggle"),
            BasisLocalization.Get("settings.perf.boundsSize.slider"),
            BasisSettingsDefaults.UsePerfLimitBoundsSize,
            BasisSettingsDefaults.MaxPerfBoundsSize,
            10f, 50f, false, decimals: 1,
            toggleTooltip: BasisLocalization.Get("settings.perf.boundsSize.toggle.tooltip"),
            sliderTooltip: BasisLocalization.Get("settings.perf.boundsSize.slider.tooltip"));

        AddLimitPair(geometry.ContentParent,
            BasisLocalization.Get("settings.perf.bones.toggle"),
            BasisLocalization.Get("settings.perf.bones.slider"),
            BasisSettingsDefaults.UsePerfLimitBones,
            BasisSettingsDefaults.MaxPerfBones,
            16, 16384, true, displayMode: ValueDisplayMode.Compact,
            toggleTooltip: BasisLocalization.Get("settings.perf.bones.toggle.tooltip"),
            sliderTooltip: BasisLocalization.Get("settings.perf.bones.slider.tooltip"));

        FinalizeCollapsibleGroup(geometryToggle, geometry, false);

        PanelSectionToggle meshesToggle = PanelSectionToggle.CreateNewEntry(contentParent);
        PanelElementDescriptor meshes = CreateCollapsibleContentGroup(
            meshesToggle,
            contentParent,
            "settings.perf.group.meshesMaterials");

        AddLimitPair(meshes.ContentParent,
            BasisLocalization.Get("settings.perf.skinnedMeshes.toggle"),
            BasisLocalization.Get("settings.perf.skinnedMeshes.slider"),
            BasisSettingsDefaults.UsePerfLimitSkinnedMeshes,
            BasisSettingsDefaults.MaxPerfSkinnedMeshes,
            1, 64, true,
            toggleTooltip: BasisLocalization.Get("settings.perf.skinnedMeshes.toggle.tooltip"),
            sliderTooltip: BasisLocalization.Get("settings.perf.skinnedMeshes.slider.tooltip"));

        AddLimitPair(meshes.ContentParent,
            BasisLocalization.Get("settings.perf.basicMeshes.toggle"),
            BasisLocalization.Get("settings.perf.basicMeshes.slider"),
            BasisSettingsDefaults.UsePerfLimitBasicMeshes,
            BasisSettingsDefaults.MaxPerfBasicMeshes,
            1, 128, true,
            toggleTooltip: BasisLocalization.Get("settings.perf.basicMeshes.toggle.tooltip"),
            sliderTooltip: BasisLocalization.Get("settings.perf.basicMeshes.slider.tooltip"));

        AddLimitPair(meshes.ContentParent,
            BasisLocalization.Get("settings.perf.materialSlots.toggle"),
            BasisLocalization.Get("settings.perf.materialSlots.slider"),
            BasisSettingsDefaults.UsePerfLimitMaterialSlots,
            BasisSettingsDefaults.MaxPerfMaterialSlots,
            1, 256, true,
            toggleTooltip: BasisLocalization.Get("settings.perf.materialSlots.toggle.tooltip"),
            sliderTooltip: BasisLocalization.Get("settings.perf.materialSlots.slider.tooltip"));

        AddLimitPair(meshes.ContentParent,
            BasisLocalization.Get("settings.perf.textureMemory.toggle"),
            BasisLocalization.Get("settings.perf.textureMemory.slider"),
            BasisSettingsDefaults.UsePerfLimitTextureMemory,
            BasisSettingsDefaults.MaxPerfTextureMemoryMB,
            8, 4096, true,
            toggleTooltip: BasisLocalization.Get("settings.perf.textureMemory.toggle.tooltip"),
            sliderTooltip: BasisLocalization.Get("settings.perf.textureMemory.slider.tooltip"));

        FinalizeCollapsibleGroup(meshesToggle, meshes, false);

        PanelSectionToggle physicsToggle = PanelSectionToggle.CreateNewEntry(contentParent);
        PanelElementDescriptor physics = CreateCollapsibleContentGroup(
            physicsToggle,
            contentParent,
            "settings.perf.group.physics");

        AddLimitPair(physics.ContentParent,
            BasisLocalization.Get("settings.perf.jiggleBones.toggle"),
            BasisLocalization.Get("settings.perf.jiggleBones.slider"),
            BasisSettingsDefaults.UsePerfLimitJiggleBones,
            BasisSettingsDefaults.MaxPerfJiggleBones,
            0, 128, true,
            toggleTooltip: BasisLocalization.Get("settings.perf.jiggleBones.toggle.tooltip"),
            sliderTooltip: BasisLocalization.Get("settings.perf.jiggleBones.slider.tooltip"));

        AddLimitPair(physics.ContentParent,
            BasisLocalization.Get("settings.perf.colliders.toggle"),
            BasisLocalization.Get("settings.perf.colliders.slider"),
            BasisSettingsDefaults.UsePerfLimitColliders,
            BasisSettingsDefaults.MaxPerfColliders,
            0, 128, true,
            toggleTooltip: BasisLocalization.Get("settings.perf.colliders.toggle.tooltip"),
            sliderTooltip: BasisLocalization.Get("settings.perf.colliders.slider.tooltip"));

        AddLimitPair(physics.ContentParent,
            BasisLocalization.Get("settings.perf.cloth.toggle"),
            BasisLocalization.Get("settings.perf.cloth.slider"),
            BasisSettingsDefaults.UsePerfLimitCloth,
            BasisSettingsDefaults.MaxPerfCloth,
            0, 16, true,
            toggleTooltip: BasisLocalization.Get("settings.perf.cloth.toggle.tooltip"),
            sliderTooltip: BasisLocalization.Get("settings.perf.cloth.slider.tooltip"));

        PanelToggle jiggleFrustumToggle = PanelToggle.CreateNewEntry(physics.ContentParent);
        jiggleFrustumToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.perf.jiggleFrustumCull.toggle"));
        jiggleFrustumToggle.Descriptor.SetTooltip(BasisLocalization.Get("settings.perf.jiggleFrustumCull.toggle.tooltip"));
        jiggleFrustumToggle.AssignBinding(BasisSettingsDefaults.UseJiggleCollisionFrustumCull);

        AddLimitPair(physics.ContentParent,
            BasisLocalization.Get("settings.perf.jiggleDistanceCull.toggle"),
            BasisLocalization.Get("settings.perf.jiggleDistanceCull.slider"),
            BasisSettingsDefaults.UseJiggleCollisionDistanceCull,
            BasisSettingsDefaults.JiggleCollisionCullDistance,
            2f, 100f, false, decimals: 0,
            toggleTooltip: BasisLocalization.Get("settings.perf.jiggleDistanceCull.toggle.tooltip"),
            sliderTooltip: BasisLocalization.Get("settings.perf.jiggleDistanceCull.slider.tooltip"));

        FinalizeCollapsibleGroup(physicsToggle, physics, false);

        PanelSectionToggle effectsToggle = PanelSectionToggle.CreateNewEntry(contentParent);
        PanelElementDescriptor effects = CreateCollapsibleContentGroup(
            effectsToggle,
            contentParent,
            "settings.perf.group.effects");

        AddLimitPair(effects.ContentParent,
            BasisLocalization.Get("settings.perf.particleSystems.toggle"),
            BasisLocalization.Get("settings.perf.particleSystems.slider"),
            BasisSettingsDefaults.UsePerfLimitParticleSystems,
            BasisSettingsDefaults.MaxPerfParticleSystems,
            0, 128, true,
            toggleTooltip: BasisLocalization.Get("settings.perf.particleSystems.toggle.tooltip"),
            sliderTooltip: BasisLocalization.Get("settings.perf.particleSystems.slider.tooltip"));

        AddLimitPair(effects.ContentParent,
            BasisLocalization.Get("settings.perf.trailRenderers.toggle"),
            BasisLocalization.Get("settings.perf.trailRenderers.slider"),
            BasisSettingsDefaults.UsePerfLimitTrailRenderers,
            BasisSettingsDefaults.MaxPerfTrailRenderers,
            0, 64, true,
            toggleTooltip: BasisLocalization.Get("settings.perf.trailRenderers.toggle.tooltip"),
            sliderTooltip: BasisLocalization.Get("settings.perf.trailRenderers.slider.tooltip"));

        AddLimitPair(effects.ContentParent,
            BasisLocalization.Get("settings.perf.lineRenderers.toggle"),
            BasisLocalization.Get("settings.perf.lineRenderers.slider"),
            BasisSettingsDefaults.UsePerfLimitLineRenderers,
            BasisSettingsDefaults.MaxPerfLineRenderers,
            0, 64, true,
            toggleTooltip: BasisLocalization.Get("settings.perf.lineRenderers.toggle.tooltip"),
            sliderTooltip: BasisLocalization.Get("settings.perf.lineRenderers.slider.tooltip"));

        FinalizeCollapsibleGroup(effectsToggle, effects, false);

        PanelSectionToggle runtimeToggle = PanelSectionToggle.CreateNewEntry(contentParent);
        PanelElementDescriptor runtime = CreateCollapsibleContentGroup(
            runtimeToggle,
            contentParent,
            "settings.perf.group.runtime");

        AddLimitPair(runtime.ContentParent,
            BasisLocalization.Get("settings.perf.animators.toggle"),
            BasisLocalization.Get("settings.perf.animators.slider"),
            BasisSettingsDefaults.UsePerfLimitAnimators,
            BasisSettingsDefaults.MaxPerfAnimators,
            1, 32, true,
            toggleTooltip: BasisLocalization.Get("settings.perf.animators.toggle.tooltip"),
            sliderTooltip: BasisLocalization.Get("settings.perf.animators.slider.tooltip"));

        AddLimitPair(runtime.ContentParent,
            BasisLocalization.Get("settings.perf.cilboxBehaviours.toggle"),
            BasisLocalization.Get("settings.perf.cilboxBehaviours.slider"),
            BasisSettingsDefaults.UsePerfLimitCilboxBehaviours,
            BasisSettingsDefaults.MaxPerfCilboxBehaviours,
            0, 64, true,
            toggleTooltip: BasisLocalization.Get("settings.perf.cilboxBehaviours.toggle.tooltip"),
            sliderTooltip: BasisLocalization.Get("settings.perf.cilboxBehaviours.slider.tooltip"));

        FinalizeCollapsibleGroup(runtimeToggle, runtime, false);

        SettingsProviderContentTags.BuildContentTagsContent(container);

        if (performanceToggle != null)
        {
            FinalizeCollapsibleGroup(performanceToggle, performanceGroup, false);
        }
    }

    private static PanelElementDescriptor CreateCollapsibleContentGroup(
        PanelSectionToggle sectionToggle,
        RectTransform parent,
        string titleKey,
        bool showGroupTitle = true)
    {
        string title = BasisLocalization.Get(titleKey);
        sectionToggle.SetTitle(title);

        PanelElementDescriptor group =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, parent);
        if (showGroupTitle)
        {
            group.SetTitle(title);
        }
        else if (group.Header != null)
        {
            group.Header.gameObject.SetActive(false);
        }

        sectionToggle.RegisterContentContainer(group);
        return group;
    }

    private static void FinalizeCollapsibleGroup(
        PanelSectionToggle sectionToggle,
        PanelElementDescriptor group,
        bool defaultOpen)
    {
        if (sectionToggle == null || group == null)
        {
            return;
        }

        group.gameObject.SetActive(defaultOpen);
        sectionToggle.SetExpandedWithoutNotify(defaultOpen);
        sectionToggle.OnExpandedChanged += visible =>
        {
            group.gameObject.SetActive(visible);
            ForceLayout();
        };
    }

    private static void ForceLayout()
    {
        if (_layoutRoot != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_layoutRoot);
        }
    }

    private static void AddLimitPair(
        Component parent,
        string toggleTitle,
        string sliderTitle,
        BasisSettingsBinding<bool> useBinding,
        BasisSettingsBinding<float> maxBinding,
        float sliderMin,
        float sliderMax,
        bool wholeNumbers,
        int decimals = 0,
        ValueDisplayMode displayMode = ValueDisplayMode.Raw,
        string toggleTooltip = null,
        string sliderTooltip = null)
    {
        PanelToggle toggle = PanelToggle.CreateNewEntry(parent);
        toggle.Descriptor.SetTitle(toggleTitle);
        toggle.Descriptor.SetTooltip(toggleTooltip);
        toggle.AssignBinding(useBinding);

        PanelSlider slider = PanelSlider.CreateEntryAndBind(parent, PanelSlider.SliderSettings.Advanced(sliderTitle, sliderMin, sliderMax, wholeNumbers, decimals, displayMode), maxBinding);

        if (slider != null)
        {
            slider.Descriptor.SetTooltip(sliderTooltip);

            void Sync(bool on)
            {
                if (slider == null) return;
                slider.gameObject.SetActive(on);
                ForceLayout();
            }

            Sync(useBinding.RawValue);
            useBinding.OnChanged += Sync;
        }
    }

    public static void ResetPerformanceLimitDefaults()
    {
        BasisSettingsDefaults.UsePerfLimitTriangles.ResetToDefault();
        BasisSettingsDefaults.MaxPerfTriangles.ResetToDefault();
        BasisSettingsDefaults.UsePerfLimitBoundsSize.ResetToDefault();
        BasisSettingsDefaults.MaxPerfBoundsSize.ResetToDefault();
        BasisSettingsDefaults.UsePerfLimitTextureMemory.ResetToDefault();
        BasisSettingsDefaults.MaxPerfTextureMemoryMB.ResetToDefault();
        BasisSettingsDefaults.UsePerfLimitSkinnedMeshes.ResetToDefault();
        BasisSettingsDefaults.MaxPerfSkinnedMeshes.ResetToDefault();
        BasisSettingsDefaults.UsePerfLimitBasicMeshes.ResetToDefault();
        BasisSettingsDefaults.MaxPerfBasicMeshes.ResetToDefault();
        BasisSettingsDefaults.UsePerfLimitMaterialSlots.ResetToDefault();
        BasisSettingsDefaults.MaxPerfMaterialSlots.ResetToDefault();
        BasisSettingsDefaults.UsePerfLimitJiggleBones.ResetToDefault();
        BasisSettingsDefaults.MaxPerfJiggleBones.ResetToDefault();
        BasisSettingsDefaults.UsePerfLimitJiggleColliders.ResetToDefault();
        BasisSettingsDefaults.MaxPerfJiggleColliders.ResetToDefault();
        BasisSettingsDefaults.UseJiggleCollisionFrustumCull.ResetToDefault();
        BasisSettingsDefaults.UseJiggleCollisionDistanceCull.ResetToDefault();
        BasisSettingsDefaults.JiggleCollisionCullDistance.ResetToDefault();
        BasisSettingsDefaults.UsePerfLimitAnimators.ResetToDefault();
        BasisSettingsDefaults.MaxPerfAnimators.ResetToDefault();
        BasisSettingsDefaults.UsePerfLimitBones.ResetToDefault();
        BasisSettingsDefaults.MaxPerfBones.ResetToDefault();
        BasisSettingsDefaults.UsePerfLimitLights.ResetToDefault();
        BasisSettingsDefaults.MaxPerfLights.ResetToDefault();
        BasisSettingsDefaults.UsePerfLimitParticleSystems.ResetToDefault();
        BasisSettingsDefaults.MaxPerfParticleSystems.ResetToDefault();
        BasisSettingsDefaults.UsePerfLimitTrailRenderers.ResetToDefault();
        BasisSettingsDefaults.MaxPerfTrailRenderers.ResetToDefault();
        BasisSettingsDefaults.UsePerfLimitLineRenderers.ResetToDefault();
        BasisSettingsDefaults.MaxPerfLineRenderers.ResetToDefault();
        BasisSettingsDefaults.UsePerfLimitCloth.ResetToDefault();
        BasisSettingsDefaults.MaxPerfCloth.ResetToDefault();
        BasisSettingsDefaults.UsePerfLimitColliders.ResetToDefault();
        BasisSettingsDefaults.MaxPerfColliders.ResetToDefault();
        BasisSettingsDefaults.UsePerfLimitCilboxBehaviours.ResetToDefault();
        BasisSettingsDefaults.MaxPerfCilboxBehaviours.ResetToDefault();
    }
}
