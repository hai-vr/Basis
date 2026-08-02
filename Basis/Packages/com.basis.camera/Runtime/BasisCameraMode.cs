using UnityEngine;

/// <summary>
/// A named bundle of camera settings for one job. The handheld camera carries far more controls
/// than any single use needs, so a mode does two things: it writes the settings that job wants,
/// and it labels every panel section with the part that section plays. Nothing is ever locked —
/// editing a value a mode writes drops the camera to <see cref="Custom"/>, which is how the panel
/// says "these are no longer the preset's numbers" without taking the control away.
/// </summary>
public enum BasisCameraMode
{
    /// <summary>Hand-tuned. Only ever detected, never applied — there is nothing to apply.</summary>
    Custom = 0,

    /// <summary>The ordinary handheld camera: held in your hand, shallow depth of field, stills.</summary>
    Photo = 1,

    /// <summary>Parked in the world and flown by hand. Steady and wide, for streaming a room.</summary>
    FlyingPuck = 2,

    /// <summary>Flies itself, keeping you in frame and in focus.</summary>
    FollowMe = 3,

    /// <summary>Driven by the shot rig — dolly, orbit and framing move the camera, not you.</summary>
    Cinematic = 4,
}

/// <summary>
/// The panel's collapsible sections, named so a mode can say what each one is for. This is a UI
/// vocabulary, not a settings model: it exists to key <see cref="BasisCameraSectionRole"/>.
/// </summary>
public enum BasisCameraPanelSection
{
    Actions,
    Lens,
    DepthOfField,
    Colour,
    Effects,
    Output,
    Follow,
    Cinematic,
    Composition,
    Orbit,
    Noise,
    Dolly,
    Background,
    Layers,
    Performance,
    Gizmos,
}

/// <summary>What a section is doing under the current mode. Drives the section's background tint.</summary>
public enum BasisCameraSectionRole
{
    /// <summary>The mode writes at least one value here. Changing any of those drops to Custom.</summary>
    Driven,

    /// <summary>Yours to set. The mode has no opinion, and editing here keeps the mode.</summary>
    Available,

    /// <summary>Nothing here has any effect while this mode is active.</summary>
    Inactive,
}

/// <summary>
/// Everything the panel needs to present one mode: its name, what it is for, the colour it paints
/// its sections, and the part each section plays.
/// </summary>
public sealed class BasisCameraModeDescriptor
{
    public readonly BasisCameraMode Mode;
    public readonly string TitleKey;
    public readonly string DescriptionKey;

    /// <summary>The mode's identity colour. Sections are tinted toward it, never painted with it flat.</summary>
    public readonly Color Tint;

    private readonly BasisCameraSectionRole[] _roles;

    internal BasisCameraModeDescriptor(
        BasisCameraMode mode,
        string titleKey,
        string descriptionKey,
        Color tint,
        BasisCameraSectionRole[] roles)
    {
        Mode = mode;
        TitleKey = titleKey;
        DescriptionKey = descriptionKey;
        Tint = tint;
        _roles = roles;
    }

    public BasisCameraSectionRole RoleOf(BasisCameraPanelSection section)
    {
        int index = (int)section;
        return index >= 0 && index < _roles.Length ? _roles[index] : BasisCameraSectionRole.Available;
    }
}

/// <summary>
/// The mode table. Pure data with no scene dependencies, so both the panel and the tests can read
/// it without a camera in hand. The values each mode <em>writes</em> live next to the code that
/// writes them, in the camera's own mode partial.
/// </summary>
public static class BasisCameraModes
{
    /// <summary>Presentation order, and the order of the panel's mode dropdown.</summary>
    public static readonly BasisCameraMode[] Ordered =
    {
        BasisCameraMode.Photo,
        BasisCameraMode.FlyingPuck,
        BasisCameraMode.FollowMe,
        BasisCameraMode.Cinematic,
        BasisCameraMode.Custom,
    };

    private const int SectionCount = (int)BasisCameraPanelSection.Gizmos + 1;

    // How far a section's background moves toward the mode colour. Driven has to be obvious at a
    // glance across a scrolling page; available is a hint that the panel belongs to a mode at all
    // and must stay quiet enough to read a slider against.
    private const float DrivenBlend = 0.34f;
    private const float AvailableBlend = 0.07f;

    // Inactive is darkened rather than tinted: it should read as "switched off", which is the one
    // thing a colour cannot say by being more colourful.
    private const float InactiveDarken = 0.30f;
    private const float InactiveAlpha = 0.72f;

    /// <summary>
    /// The shot rig and the follow solver, both of which only run in their own mode.
    ///
    /// ⚠️ Declared before <see cref="Descriptors"/> on purpose: static field initializers run in
    /// textual order, so a table built above this line would read it as null and silently give
    /// every mode an empty inactive set.
    /// </summary>
    private static readonly BasisCameraPanelSection[] RigSections =
    {
        BasisCameraPanelSection.Follow,
        BasisCameraPanelSection.Cinematic,
        BasisCameraPanelSection.Composition,
        BasisCameraPanelSection.Orbit,
        BasisCameraPanelSection.Noise,
        BasisCameraPanelSection.Dolly,
    };

    private static readonly BasisCameraModeDescriptor[] Descriptors = BuildDescriptors();

    public static BasisCameraModeDescriptor Get(BasisCameraMode mode)
    {
        for (int Index = 0; Index < Descriptors.Length; Index++)
        {
            if (Descriptors[Index].Mode == mode) return Descriptors[Index];
        }

        return Descriptors[0];
    }

    public static BasisCameraSectionRole RoleOf(BasisCameraMode mode, BasisCameraPanelSection section) =>
        Get(mode).RoleOf(section);

    /// <summary>
    /// The colour a section's background should carry under this mode, derived from the colour the
    /// palette already gave it. Blending rather than replacing keeps the user's palette choices —
    /// and the card's own translucency — intact, so a tinted panel still looks like the rest of the
    /// menu rather than a block of paint.
    /// </summary>
    public static Color TintFor(BasisCameraMode mode, BasisCameraPanelSection section, Color baseline)
    {
        BasisCameraModeDescriptor descriptor = Get(mode);
        switch (descriptor.RoleOf(section))
        {
            case BasisCameraSectionRole.Driven:
                return Blend(baseline, descriptor.Tint, DrivenBlend);

            case BasisCameraSectionRole.Inactive:
                Color dimmed = Color.Lerp(baseline, Color.black, InactiveDarken);
                dimmed.a = baseline.a * InactiveAlpha;
                return dimmed;

            default:
                return Blend(baseline, descriptor.Tint, AvailableBlend);
        }
    }

    /// <summary>Alpha is the card's own translucency and belongs to the palette, so only hue moves.</summary>
    private static Color Blend(Color baseline, Color tint, float weight)
    {
        Color blended = Color.Lerp(baseline, tint, weight);
        blended.a = baseline.a;
        return blended;
    }

    private static BasisCameraModeDescriptor[] BuildDescriptors()
    {
        return new[]
        {
            // Custom is the absence of a preset, so nothing is driven and nothing is switched off.
            // Its colour is a neutral slate: present enough that the panel still looks deliberate,
            // with no hue to suggest a mode is running.
            new BasisCameraModeDescriptor(
                BasisCameraMode.Custom,
                "camera.modePreset.custom",
                "camera.modePreset.custom.description",
                new Color(0.62f, 0.66f, 0.74f),
                Roles(null, null)),

            new BasisCameraModeDescriptor(
                BasisCameraMode.Photo,
                "camera.modePreset.photo",
                "camera.modePreset.photo.description",
                new Color(1.00f, 0.72f, 0.32f),
                Roles(
                    driven: new[]
                    {
                        BasisCameraPanelSection.Actions,
                        BasisCameraPanelSection.Lens,
                        BasisCameraPanelSection.DepthOfField,
                    },
                    inactive: RigSections)),

            new BasisCameraModeDescriptor(
                BasisCameraMode.FlyingPuck,
                "camera.modePreset.flyingPuck",
                "camera.modePreset.flyingPuck.description",
                new Color(0.32f, 0.78f, 1.00f),
                Roles(
                    driven: new[]
                    {
                        BasisCameraPanelSection.Actions,
                        BasisCameraPanelSection.Lens,
                        BasisCameraPanelSection.DepthOfField,
                    },
                    inactive: RigSections)),

            new BasisCameraModeDescriptor(
                BasisCameraMode.FollowMe,
                "camera.modePreset.followMe",
                "camera.modePreset.followMe.description",
                new Color(0.42f, 0.88f, 0.56f),
                Roles(
                    driven: new[]
                    {
                        BasisCameraPanelSection.Actions,
                        BasisCameraPanelSection.Follow,
                        BasisCameraPanelSection.Lens,
                        BasisCameraPanelSection.DepthOfField,
                    },
                    inactive: new[]
                    {
                        BasisCameraPanelSection.Cinematic,
                        BasisCameraPanelSection.Composition,
                        BasisCameraPanelSection.Orbit,
                        BasisCameraPanelSection.Noise,
                        BasisCameraPanelSection.Dolly,
                    })),

            // The rig owns where the camera goes, so Follow is the one thing that cannot also run.
            // Orbit, noise and dolly stay available: they are the rig's own controls.
            new BasisCameraModeDescriptor(
                BasisCameraMode.Cinematic,
                "camera.modePreset.cinematic",
                "camera.modePreset.cinematic.description",
                new Color(0.74f, 0.56f, 1.00f),
                Roles(
                    driven: new[]
                    {
                        BasisCameraPanelSection.Actions,
                        BasisCameraPanelSection.Cinematic,
                        BasisCameraPanelSection.Composition,
                        BasisCameraPanelSection.Lens,
                        BasisCameraPanelSection.DepthOfField,
                        BasisCameraPanelSection.Effects,
                    },
                    inactive: new[] { BasisCameraPanelSection.Follow })),
        };
    }

    /// <summary>Anything not named is available — the safe default for a section added later.</summary>
    private static BasisCameraSectionRole[] Roles(
        BasisCameraPanelSection[] driven,
        BasisCameraPanelSection[] inactive)
    {
        BasisCameraSectionRole[] roles = new BasisCameraSectionRole[SectionCount];
        for (int Index = 0; Index < roles.Length; Index++)
        {
            roles[Index] = BasisCameraSectionRole.Available;
        }

        if (driven != null)
        {
            for (int Index = 0; Index < driven.Length; Index++)
            {
                roles[(int)driven[Index]] = BasisCameraSectionRole.Driven;
            }
        }

        if (inactive != null)
        {
            for (int Index = 0; Index < inactive.Length; Index++)
            {
                roles[(int)inactive[Index]] = BasisCameraSectionRole.Inactive;
            }
        }

        return roles;
    }
}
