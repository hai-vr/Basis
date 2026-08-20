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

    // The four below are camera <em>kinds</em> rather than jobs: each hands you a different body,
    // and the body is the half of them a slider cannot undo. See <see cref="BasisCameraBodyKind"/>.

    /// <summary>A single-use 35mm: twenty-seven warm, grainy frames and a flash you wait for.</summary>
    Disposable = 5,

    /// <summary>Instant film: eight square prints a pack, each one a minute in coming up.</summary>
    Instant = 6,

    /// <summary>A tape camcorder. Soft, smeared, desaturated 4:3 with the clock burned in.</summary>
    Camcorder = 7,

    /// <summary>A ceiling camera: very wide, nearly grey, and stamped with the time.</summary>
    Security = 8,
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
    Subject,
    PositionModifier,
    RotationModifier,
    ModifierEffects,
    Dolly,
    Background,
    Layers,
    Performance,
    Gizmos,
    PhotoMetadata,
    Anchor,
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

    /// <summary>
    /// A saved mode's name, shown as-is. Null for the built-ins, whose titles are localization
    /// keys. The two cannot be the same field: a name someone typed must never be looked up in the
    /// language table, or a mode called "Photo" — or worse, one named after a key — would come
    /// back translated into somebody else's mode.
    /// </summary>
    public readonly string LiteralTitle;

    /// <summary>True for a mode somebody saved, which is the half of the roster that can be deleted.</summary>
    public bool IsUserMode => LiteralTitle != null;

    /// <summary>The mode's identity colour. Sections are tinted toward it, never painted with it flat.</summary>
    public readonly Color Tint;

    private readonly BasisCameraSectionRole[] _roles;

    internal BasisCameraModeDescriptor(
        BasisCameraMode mode,
        string titleKey,
        string descriptionKey,
        Color tint,
        BasisCameraSectionRole[] roles,
        string literalTitle = null)
    {
        Mode = mode;
        TitleKey = titleKey;
        DescriptionKey = descriptionKey;
        Tint = tint;
        _roles = roles;
        LiteralTitle = literalTitle;
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

        // The kinds come after the jobs, and in the order they were made: a roll of film, then a
        // pack of instant, then tape, then whatever a security camera runs on.
        BasisCameraMode.Disposable,
        BasisCameraMode.Instant,
        BasisCameraMode.Camcorder,
        BasisCameraMode.Security,

        BasisCameraMode.Custom,
    };

    private const int SectionCount = (int)BasisCameraPanelSection.Anchor + 1;

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
    /// The modifier stack, which only drives the camera once something is fitted.
    ///
    /// ⚠️ Declared before <see cref="Descriptors"/> on purpose: static field initializers run in
    /// textual order, so a table built above this line would read it as null and silently give
    /// every mode an empty inactive set.
    /// </summary>
    private static readonly BasisCameraPanelSection[] RigSections =
    {
        BasisCameraPanelSection.Subject,
        BasisCameraPanelSection.PositionModifier,
        BasisCameraPanelSection.RotationModifier,
        BasisCameraPanelSection.ModifierEffects,
        BasisCameraPanelSection.Dolly,
    };

    /// <summary>
    /// What a camera kind switches off: the rig, plus the resolution list its own frame size takes
    /// over.
    ///
    /// ⚠️ Above <see cref="Descriptors"/> for the same reason <see cref="RigSections"/> is — a table
    /// declared below the initializer that reads it is null when the builder runs, and every kind
    /// would silently come out with nothing switched off.
    /// </summary>
    private static readonly BasisCameraPanelSection[] KindInactiveSections =
    {
        BasisCameraPanelSection.Output,
        BasisCameraPanelSection.Subject,
        BasisCameraPanelSection.PositionModifier,
        BasisCameraPanelSection.RotationModifier,
        BasisCameraPanelSection.ModifierEffects,
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
    public static Color TintFor(BasisCameraMode mode, BasisCameraPanelSection section, Color baseline) =>
        TintFor(Get(mode), section, baseline);

    /// <summary>
    /// The same rule against a descriptor rather than a built-in. Saved modes have no entry in the
    /// table — their colour is whatever their owner picked — so the tint has to be reachable from
    /// the descriptor alone.
    /// </summary>
    public static Color TintFor(BasisCameraModeDescriptor descriptor, BasisCameraPanelSection section, Color baseline)
    {
        if (descriptor == null) return baseline;

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
                        BasisCameraPanelSection.Anchor,
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
                        BasisCameraPanelSection.Anchor,
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
                        BasisCameraPanelSection.Anchor,
                        BasisCameraPanelSection.Subject,
                        BasisCameraPanelSection.Lens,
                        BasisCameraPanelSection.DepthOfField,
                    },
                    inactive: new[]
                    {
                        BasisCameraPanelSection.PositionModifier,
                        BasisCameraPanelSection.RotationModifier,
                        BasisCameraPanelSection.ModifierEffects,
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
                        BasisCameraPanelSection.Anchor,
                        BasisCameraPanelSection.PositionModifier,
                        BasisCameraPanelSection.RotationModifier,
                        BasisCameraPanelSection.Lens,
                        BasisCameraPanelSection.DepthOfField,
                        BasisCameraPanelSection.Effects,
                    },
                    inactive: new[] { BasisCameraPanelSection.Subject })),

            // The camera kinds. All four share a role table because they are all the same shape of
            // opinion: the body owns the picture end to end — lens, focus, colour, effects — and
            // the frame it shoots is the body's own, so the resolution list has nothing left to
            // say. What separates them is which body you are handed, not which sections they claim.
            new BasisCameraModeDescriptor(
                BasisCameraMode.Disposable,
                "camera.modePreset.disposable",
                "camera.modePreset.disposable.description",
                new Color(1.00f, 0.44f, 0.30f),
                KindRoles()),

            new BasisCameraModeDescriptor(
                BasisCameraMode.Instant,
                "camera.modePreset.instant",
                "camera.modePreset.instant.description",
                new Color(1.00f, 0.66f, 0.90f),
                KindRoles()),

            new BasisCameraModeDescriptor(
                BasisCameraMode.Camcorder,
                "camera.modePreset.camcorder",
                "camera.modePreset.camcorder.description",
                new Color(0.36f, 0.86f, 0.80f),
                KindRoles()),

            new BasisCameraModeDescriptor(
                BasisCameraMode.Security,
                "camera.modePreset.security",
                "camera.modePreset.security.description",
                new Color(0.42f, 0.52f, 0.86f),
                KindRoles()),
        };
    }

    /// <summary>
    /// The sections a camera kind claims. Written once and handed out four times: the day a fifth
    /// body is added it should have to disagree with the others on purpose, not by being typed out
    /// separately and drifting.
    /// </summary>
    private static BasisCameraSectionRole[] KindRoles()
    {
        return Roles(
            driven: new[]
            {
                BasisCameraPanelSection.Actions,
                BasisCameraPanelSection.Anchor,
                BasisCameraPanelSection.Lens,
                BasisCameraPanelSection.DepthOfField,
                BasisCameraPanelSection.Colour,
                BasisCameraPanelSection.Effects,
            },
            // Output is switched off rather than driven: a body shoots the frame it shoots, so the
            // resolution list is not a setting this mode chose — it is one the camera does not have.
            inactive: KindInactiveSections);
    }


    /// <summary>
    /// Presents a saved mode the way the panel presents a built-in one.
    ///
    /// <para>Where a built-in drives a hand-picked few sections, a saved mode is a snapshot of the
    /// whole camera and so drives every section that has anywhere in the settings file to be
    /// saved. The three that do not — render layers, the performance limiter and the gizmos — stay
    /// available: they are real controls that a saved mode simply has no opinion about, and
    /// colouring them as driven would promise a restore that never comes.</para>
    ///
    /// <para>The rig sections follow the same rule the built-ins do. A mode that does not arm
    /// follow greys the follow section out, because with follow off nothing in it does anything;
    /// orbit, shake and the dolly track stay available under a cinematic mode because they are the
    /// rig's own controls rather than the mode's.</para>
    /// </summary>
    public static BasisCameraModeDescriptor DescribeUserMode(BasisCameraUserMode mode)
    {
        if (mode == null) return Get(BasisCameraMode.Custom);

        // Built as two disjoint lists rather than two literal tables. Follow and the rig are not
        // quite mutually exclusive — a record hand-edited on disk can arm both — and a section
        // named in each would silently take whichever role happened to be written second.
        System.Collections.Generic.List<BasisCameraPanelSection> driven =
            new System.Collections.Generic.List<BasisCameraPanelSection>
            {
                BasisCameraPanelSection.Actions,
                BasisCameraPanelSection.Anchor,
                BasisCameraPanelSection.Lens,
                BasisCameraPanelSection.DepthOfField,
                BasisCameraPanelSection.Colour,
                BasisCameraPanelSection.Effects,
                BasisCameraPanelSection.Output,
                BasisCameraPanelSection.Background,
            };

        System.Collections.Generic.List<BasisCameraPanelSection> inactive =
            new System.Collections.Generic.List<BasisCameraPanelSection>();

        Basis.Cinematics.BasisCameraModifierStack stack = mode.settings?.modifiers;
        bool drivesPosition = stack != null && stack.DrivesPosition;
        bool drivesRotation = stack != null && stack.DrivesRotation;

        (drivesPosition || drivesRotation ? driven : inactive).Add(BasisCameraPanelSection.Subject);
        (drivesPosition ? driven : inactive).Add(BasisCameraPanelSection.PositionModifier);
        (drivesRotation ? driven : inactive).Add(BasisCameraPanelSection.RotationModifier);
        (stack != null && stack.EffectCount > 0 ? driven : inactive).Add(BasisCameraPanelSection.ModifierEffects);
        (stack != null && stack.positionModifier == Basis.Cinematics.BasisCameraPositionModifier.DollyTrack
            ? driven : inactive).Add(BasisCameraPanelSection.Dolly);

        return new BasisCameraModeDescriptor(
            BasisCameraMode.Custom,
            "camera.modePreset.custom",
            "camera.userMode.description",
            mode.tint,
            Roles(driven, inactive),
            mode.name);
    }

    /// <summary>Anything not named is available — the safe default for a section added later.</summary>
    private static BasisCameraSectionRole[] Roles(
        System.Collections.Generic.IReadOnlyList<BasisCameraPanelSection> driven,
        System.Collections.Generic.IReadOnlyList<BasisCameraPanelSection> inactive)
    {
        BasisCameraSectionRole[] roles = new BasisCameraSectionRole[SectionCount];
        for (int Index = 0; Index < roles.Length; Index++)
        {
            roles[Index] = BasisCameraSectionRole.Available;
        }

        if (driven != null)
        {
            for (int Index = 0; Index < driven.Count; Index++)
            {
                roles[(int)driven[Index]] = BasisCameraSectionRole.Driven;
            }
        }

        if (inactive != null)
        {
            for (int Index = 0; Index < inactive.Count; Index++)
            {
                roles[(int)inactive[Index]] = BasisCameraSectionRole.Inactive;
            }
        }

        return roles;
    }
}
