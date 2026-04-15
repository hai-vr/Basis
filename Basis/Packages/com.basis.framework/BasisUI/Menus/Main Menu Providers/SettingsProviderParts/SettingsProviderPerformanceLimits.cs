using Basis;
using Basis.BasisUI;
using Basis.Scripts.Avatar;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Settings panel for the opt-in client-side avatar performance limits. Every limit
/// is a (toggle, slider) pair — the toggle flips <c>UsePerfLimit*</c> on, the slider
/// sets the corresponding <c>MaxPerf*</c> threshold. All values live in
/// <see cref="BasisSettingsDefaults"/> and are applied at runtime by
/// <c>SMModuleAvatarPerformanceLimits</c>, which re-evaluates every currently-loaded
/// remote avatar when any value changes.
/// </summary>
public static class SettingsProviderPerformanceLimits
{
    /// <summary>
    /// The root RectTransform of the currently-built tab. Captured once at tab
    /// creation and read by the toggle callbacks so flipping a limit on/off can
    /// trigger a layout rebuild — hiding/showing a slider with <c>SetActive</c>
    /// drops it out of the layout pass, but LayoutGroups don't re-flow siblings
    /// until somebody calls <c>LayoutRebuilder.ForceRebuildLayoutImmediate</c>.
    /// </summary>
    private static RectTransform _layoutRoot;

    public static PanelTabPage PerformanceLimitsTab(PanelTabGroup tabGroup)
    {
        PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
        PanelElementDescriptor descriptor = tab.Descriptor;
        descriptor.SetIcon(AddressableAssets.Sprites.Settings);
        descriptor.SetTitle("Performance Limits");

        RectTransform container = descriptor.ContentParent;
        _layoutRoot = container;

        // Session-only bypass. Lives at the top so it's the first thing users see
        // when they want to quickly check what an avatar actually looks like
        // unfiltered. Not wired to BasisSettingsDefaults because it deliberately
        // doesn't persist — flipping it writes straight to the runtime flag on
        // BasisAvatarPerformanceLimits, which fires OnBypassChanged and pulls
        // every affected avatar through the same debounced reconcile as a normal
        // limit change.
        PanelElementDescriptor bypassGroup =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        bypassGroup.SetTitle("Session Bypass");
        bypassGroup.SetDescription(
            "Temporarily ignores every performance limit for this session only. " +
            "Resets on relaunch — never saved to disc. Useful when you want to see " +
            "a specific avatar at its full fidelity without editing your saved caps.");

        PanelToggle bypassToggle = PanelToggle.CreateNewEntry(bypassGroup.ContentParent);
        bypassToggle.Descriptor.SetTitle("Bypass All Performance Limits");
        bypassToggle.SetValueWithoutNotify(BasisAvatarPerformanceLimits.BypassAllLimits);
        bypassToggle.OnValueChanged += on =>
        {
            BasisAvatarPerformanceLimits.BypassAllLimits = on;
        };

        // Header explanation. Trim limits (animators, lights, particles, trails, lines,
        // cloth, unity colliders) destroy excess components on remote avatars at load
        // time and ship on by default. Hard-block limits (triangles, bounds, texture
        // memory, material slots, bones) fall back to the loading mesh when tripped;
        // only texture memory is on by default at 512 MB.
        PanelElementDescriptor intro =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        intro.SetTitle("Avatar Performance Limits");
        intro.SetDescription(
            "Opt-in filters applied to remote avatars as they load. Trim limits destroy " +
            "excess extras (animators, lights, particles, trails, lines, cloth, jiggle " +
            "rigs, unity colliders) so the avatar still renders. Hard-block limits " +
            "(triangles, bounds, texture memory, material slots, bones, skinned/basic " +
            "meshes) fall back to the loading mesh instead — meshes aren't trimmed " +
            "because deleting one usually destroys the body or head. Sensible defaults " +
            "are on; raise or disable any limit below.");

        // ---------------- Geometry ----------------
        PanelElementDescriptor geometry =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        geometry.SetTitle("Geometry");
        geometry.SetDescription("Raw polygon and skeleton counts from the asset bundle header.");

        AddLimitPair(geometry.ContentParent,
            "Limit Triangles", "Max Triangles",
            BasisSettingsDefaults.UsePerfLimitTriangles,
            BasisSettingsDefaults.MaxPerfTriangles,
            1000, 2_000_000, true, displayMode: ValueDisplayMode.Compact);

        // Minimum starts at 10 m — anything smaller rejects a normal humanoid on
        // principle, and the bounds check is there to catch 50 m-tall monster
        // bundles rather than tune small avatars.
        AddLimitPair(geometry.ContentParent,
            "Limit Bounds Size (m)", "Max Bounds Diagonal (m)",
            BasisSettingsDefaults.UsePerfLimitBoundsSize,
            BasisSettingsDefaults.MaxPerfBoundsSize,
            10f, 50f, false, decimals: 1);

        // Hard-block cap. Intended as a guard rail for pathological bundles rather
        // than a daily driver — label reflects that so users don't set it low.
        // "per avatar" is explicit because bones is the one limit where users
        // reasonably ask whether it sums across the room or counts per-player.
        AddLimitPair(geometry.ContentParent,
            "Hard-block Bones", "Max Skinned Bones (per avatar)",
            BasisSettingsDefaults.UsePerfLimitBones,
            BasisSettingsDefaults.MaxPerfBones,
            16, 16384, true, displayMode: ValueDisplayMode.Compact);

        // ---------------- Meshes & Materials ----------------
        PanelElementDescriptor meshes =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        meshes.SetTitle("Meshes & Materials");

        AddLimitPair(meshes.ContentParent,
            "Limit Skinned Meshes", "Max Skinned Meshes",
            BasisSettingsDefaults.UsePerfLimitSkinnedMeshes,
            BasisSettingsDefaults.MaxPerfSkinnedMeshes,
            1, 64, true);

        AddLimitPair(meshes.ContentParent,
            "Limit Basic Meshes", "Max Basic Meshes",
            BasisSettingsDefaults.UsePerfLimitBasicMeshes,
            BasisSettingsDefaults.MaxPerfBasicMeshes,
            1, 128, true);

        AddLimitPair(meshes.ContentParent,
            "Limit Material Slots", "Max Material Slots",
            BasisSettingsDefaults.UsePerfLimitMaterialSlots,
            BasisSettingsDefaults.MaxPerfMaterialSlots,
            1, 256, true);

        AddLimitPair(meshes.ContentParent,
            "Limit Texture Memory", "Max Texture Memory (MB)",
            BasisSettingsDefaults.UsePerfLimitTextureMemory,
            BasisSettingsDefaults.MaxPerfTextureMemoryMB,
            8, 4096, true);

        // ---------------- Physics ----------------
        PanelElementDescriptor physics =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        physics.SetTitle("Physics & Jiggle");

        AddLimitPair(physics.ContentParent,
            "Limit Jiggle Bones", "Max Jiggle Bones",
            BasisSettingsDefaults.UsePerfLimitJiggleBones,
            BasisSettingsDefaults.MaxPerfJiggleBones,
            0, 128, true);

        // JiggleColliderExample is not in AvatarContentPoliceSelector.asset, so the
        // content police strips every instance at load time before this limit could
        // ever run. Surface nothing for it in the UI.
        // AddLimitPair(physics.ContentParent,
        //     "Limit Jiggle Colliders", "Max Jiggle Colliders",
        //     BasisSettingsDefaults.UsePerfLimitJiggleColliders,
        //     BasisSettingsDefaults.MaxPerfJiggleColliders,
        //     0, 64, true);

        AddLimitPair(physics.ContentParent,
            "Limit Colliders", "Max Colliders",
            BasisSettingsDefaults.UsePerfLimitColliders,
            BasisSettingsDefaults.MaxPerfColliders,
            0, 128, true);

        AddLimitPair(physics.ContentParent,
            "Limit Unity Cloth", "Max Unity Cloth",
            BasisSettingsDefaults.UsePerfLimitCloth,
            BasisSettingsDefaults.MaxPerfCloth,
            0, 16, true);

        // ---------------- Effects ----------------
        PanelElementDescriptor effects =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        effects.SetTitle("Effects");

        // UnityEngine.Light is not in AvatarContentPoliceSelector.asset, so the
        // content police strips every Light on a downloaded remote avatar before
        // this limit could ever run. Addressable (in-build) avatars are curated,
        // so lights there are effectively impossible too. Surface nothing for it.
        // AddLimitPair(effects.ContentParent,
        //     "Limit Lights", "Max Lights",
        //     BasisSettingsDefaults.UsePerfLimitLights,
        //     BasisSettingsDefaults.MaxPerfLights,
        //     0, 32, true);

        AddLimitPair(effects.ContentParent,
            "Limit Particle Systems", "Max Particle Systems",
            BasisSettingsDefaults.UsePerfLimitParticleSystems,
            BasisSettingsDefaults.MaxPerfParticleSystems,
            0, 128, true);

        AddLimitPair(effects.ContentParent,
            "Limit Trail Renderers", "Max Trail Renderers",
            BasisSettingsDefaults.UsePerfLimitTrailRenderers,
            BasisSettingsDefaults.MaxPerfTrailRenderers,
            0, 64, true);

        AddLimitPair(effects.ContentParent,
            "Limit Line Renderers", "Max Line Renderers",
            BasisSettingsDefaults.UsePerfLimitLineRenderers,
            BasisSettingsDefaults.MaxPerfLineRenderers,
            0, 64, true);

        // ---------------- Runtime ----------------
        PanelElementDescriptor runtime =
            PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
        runtime.SetTitle("Runtime");
        runtime.SetDescription("The remote driver disables the root Animator, but child Animators still tick.");

        AddLimitPair(runtime.ContentParent,
            "Limit Animators", "Max Animators",
            BasisSettingsDefaults.UsePerfLimitAnimators,
            BasisSettingsDefaults.MaxPerfAnimators,
            1, 32, true);

        SettingsProvider.AddResetPageButton(container, "Performance Limits", ResetPerformanceLimitDefaults);

        descriptor.ForceRebuild();
        return tab;
    }

    /// <summary>
    /// Adds a toggle and slider for a single limit. The toggle controls whether the
    /// limit is enforced; the slider sets the threshold. When the toggle is off the
    /// slider's entire GameObject is hidden so the settings panel only shows the
    /// knob for an active limit — matches how VRChat / NeosVR hide inactive options.
    /// <paramref name="displayMode"/> lets large-number sliders (triangles, bones)
    /// opt into the compact "1k / 32.5k / 2M" readout instead of raw integers.
    /// </summary>
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

        PanelSlider slider = PanelSlider.CreateEntryAndBind(
            parent,
            PanelSlider.SliderSettings.Advanced(sliderTitle, sliderMin, sliderMax, wholeNumbers, decimals, displayMode),
            maxBinding);

        // Seed visibility from the saved toggle state, then keep it in sync when the
        // user flips the toggle. SetActive on the slider GameObject removes it from
        // the layout pass entirely — the container re-flows so only active limits
        // take up vertical space. LayoutRebuilder.ForceRebuildLayoutImmediate is
        // what actually makes the surrounding groups close the gap / reopen space;
        // without it the hidden slider leaves dead vertical space behind.
        if (slider != null)
        {
            slider.gameObject.SetActive(useBinding.RawValue);
            toggle.OnValueChanged += on =>
            {
                if (slider == null) return;
                slider.gameObject.SetActive(on);
                if (_layoutRoot != null)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_layoutRoot);
                }
            };
        }
    }

    private static void ResetPerformanceLimitDefaults()
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
    }
}
