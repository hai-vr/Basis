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
    public static void BuildPerformanceLimitsContent(RectTransform container)
    {
        _layoutRoot = container;

        PanelElementDescriptor bypassGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        bypassGroup.SetTitle(BasisLocalization.Get("settings.perf.sessionBypass.title"));
        bypassGroup.SetDescription(BasisLocalization.Get("settings.perf.sessionBypass.description"));

        PanelToggle bypassToggle = PanelToggle.CreateNewEntry(bypassGroup.ContentParent);
        bypassToggle.Descriptor.SetTitle(BasisLocalization.Get("settings.perf.sessionBypass.toggle"));
        bypassToggle.SetValueWithoutNotify(BasisAvatarPerformanceLimits.BypassAllLimits);
        bypassToggle.OnValueChanged += on =>
        {
            BasisAvatarPerformanceLimits.BypassAllLimits = on;
        };

        PanelElementDescriptor intro =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        intro.SetTitle(BasisLocalization.Get("settings.perf.intro.title"));
        intro.SetDescription(BasisLocalization.Get("settings.perf.intro.description"));

        PanelElementDescriptor geometry =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        geometry.SetTitle(BasisLocalization.Get("settings.perf.group.geometry"));
        geometry.SetDescription(BasisLocalization.Get("settings.perf.group.geometry.description"));

        AddLimitPair(geometry.ContentParent,
            BasisLocalization.Get("settings.perf.triangles.toggle"),
            BasisLocalization.Get("settings.perf.triangles.slider"),
            BasisSettingsDefaults.UsePerfLimitTriangles,
            BasisSettingsDefaults.MaxPerfTriangles,
            1000, 2_000_000, true, displayMode: ValueDisplayMode.Compact);

        AddLimitPair(geometry.ContentParent,
            BasisLocalization.Get("settings.perf.boundsSize.toggle"),
            BasisLocalization.Get("settings.perf.boundsSize.slider"),
            BasisSettingsDefaults.UsePerfLimitBoundsSize,
            BasisSettingsDefaults.MaxPerfBoundsSize,
            10f, 50f, false, decimals: 1);

        AddLimitPair(geometry.ContentParent,
            BasisLocalization.Get("settings.perf.bones.toggle"),
            BasisLocalization.Get("settings.perf.bones.slider"),
            BasisSettingsDefaults.UsePerfLimitBones,
            BasisSettingsDefaults.MaxPerfBones,
            16, 16384, true, displayMode: ValueDisplayMode.Compact);

        PanelElementDescriptor meshes =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        meshes.SetTitle(BasisLocalization.Get("settings.perf.group.meshesMaterials"));

        AddLimitPair(meshes.ContentParent,
            BasisLocalization.Get("settings.perf.skinnedMeshes.toggle"),
            BasisLocalization.Get("settings.perf.skinnedMeshes.slider"),
            BasisSettingsDefaults.UsePerfLimitSkinnedMeshes,
            BasisSettingsDefaults.MaxPerfSkinnedMeshes,
            1, 64, true);

        AddLimitPair(meshes.ContentParent,
            BasisLocalization.Get("settings.perf.basicMeshes.toggle"),
            BasisLocalization.Get("settings.perf.basicMeshes.slider"),
            BasisSettingsDefaults.UsePerfLimitBasicMeshes,
            BasisSettingsDefaults.MaxPerfBasicMeshes,
            1, 128, true);

        AddLimitPair(meshes.ContentParent,
            BasisLocalization.Get("settings.perf.materialSlots.toggle"),
            BasisLocalization.Get("settings.perf.materialSlots.slider"),
            BasisSettingsDefaults.UsePerfLimitMaterialSlots,
            BasisSettingsDefaults.MaxPerfMaterialSlots,
            1, 256, true);

        AddLimitPair(meshes.ContentParent,
            BasisLocalization.Get("settings.perf.textureMemory.toggle"),
            BasisLocalization.Get("settings.perf.textureMemory.slider"),
            BasisSettingsDefaults.UsePerfLimitTextureMemory,
            BasisSettingsDefaults.MaxPerfTextureMemoryMB,
            8, 4096, true);

        PanelElementDescriptor physics =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        physics.SetTitle(BasisLocalization.Get("settings.perf.group.physics"));

        AddLimitPair(physics.ContentParent,
            BasisLocalization.Get("settings.perf.jiggleBones.toggle"),
            BasisLocalization.Get("settings.perf.jiggleBones.slider"),
            BasisSettingsDefaults.UsePerfLimitJiggleBones,
            BasisSettingsDefaults.MaxPerfJiggleBones,
            0, 128, true);

        AddLimitPair(physics.ContentParent,
            BasisLocalization.Get("settings.perf.colliders.toggle"),
            BasisLocalization.Get("settings.perf.colliders.slider"),
            BasisSettingsDefaults.UsePerfLimitColliders,
            BasisSettingsDefaults.MaxPerfColliders,
            0, 128, true);

        AddLimitPair(physics.ContentParent,
            BasisLocalization.Get("settings.perf.cloth.toggle"),
            BasisLocalization.Get("settings.perf.cloth.slider"),
            BasisSettingsDefaults.UsePerfLimitCloth,
            BasisSettingsDefaults.MaxPerfCloth,
            0, 16, true);

        PanelElementDescriptor effects =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        effects.SetTitle(BasisLocalization.Get("settings.perf.group.effects"));

        AddLimitPair(effects.ContentParent,
            BasisLocalization.Get("settings.perf.particleSystems.toggle"),
            BasisLocalization.Get("settings.perf.particleSystems.slider"),
            BasisSettingsDefaults.UsePerfLimitParticleSystems,
            BasisSettingsDefaults.MaxPerfParticleSystems,
            0, 128, true);

        AddLimitPair(effects.ContentParent,
            BasisLocalization.Get("settings.perf.trailRenderers.toggle"),
            BasisLocalization.Get("settings.perf.trailRenderers.slider"),
            BasisSettingsDefaults.UsePerfLimitTrailRenderers,
            BasisSettingsDefaults.MaxPerfTrailRenderers,
            0, 64, true);

        AddLimitPair(effects.ContentParent,
            BasisLocalization.Get("settings.perf.lineRenderers.toggle"),
            BasisLocalization.Get("settings.perf.lineRenderers.slider"),
            BasisSettingsDefaults.UsePerfLimitLineRenderers,
            BasisSettingsDefaults.MaxPerfLineRenderers,
            0, 64, true);

        PanelElementDescriptor runtime =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        runtime.SetTitle(BasisLocalization.Get("settings.perf.group.runtime"));
        runtime.SetDescription(BasisLocalization.Get("settings.perf.group.runtime.description"));

        AddLimitPair(runtime.ContentParent,
            BasisLocalization.Get("settings.perf.animators.toggle"),
            BasisLocalization.Get("settings.perf.animators.slider"),
            BasisSettingsDefaults.UsePerfLimitAnimators,
            BasisSettingsDefaults.MaxPerfAnimators,
            1, 32, true);

        AddLimitPair(runtime.ContentParent,
            BasisLocalization.Get("settings.perf.cilboxBehaviours.toggle"),
            BasisLocalization.Get("settings.perf.cilboxBehaviours.slider"),
            BasisSettingsDefaults.UsePerfLimitCilboxBehaviours,
            BasisSettingsDefaults.MaxPerfCilboxBehaviours,
            0, 64, true);

        SettingsProviderContentTags.BuildContentTagsContent(container);
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
        ValueDisplayMode displayMode = ValueDisplayMode.Raw)
    {
        PanelToggle toggle = PanelToggle.CreateNewEntry(parent);
        toggle.Descriptor.SetTitle(toggleTitle);
        toggle.AssignBinding(useBinding);

        PanelSlider slider = PanelSlider.CreateEntryAndBind(parent, PanelSlider.SliderSettings.Advanced(sliderTitle, sliderMin, sliderMax, wholeNumbers, decimals, displayMode), maxBinding);

        if (slider != null)
        {
            void Sync(bool on)
            {
                if (slider == null) return;
                slider.gameObject.SetActive(on);
                if (_layoutRoot != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_layoutRoot);
                }
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
