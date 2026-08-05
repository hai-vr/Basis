namespace Basis.Scripts.UI.NamePlate
{
    /// <summary>
    /// Where a remote player's nameplate sits vertically.
    ///
    /// The plate used to be placed at <c>hips.y + 1.2 * (liveScale / modelScale)</c> — a constant
    /// 1.2 metres above the HIPS, with the only per-avatar term being a runtime resize ratio that is
    /// 1.0 for every avatar nobody has rescaled. Nothing in it read the avatar's head at all, so the
    /// plate's height above the head was whatever 1.2 minus that avatar's hips→crown distance
    /// happened to be:
    ///
    ///   * 1.75 m human  (hips 0.95, crown 1.75) → plate bottom 31 cm over the head. Passable, and
    ///     the reason the constant survived: it was tuned on this body plan.
    ///   * 1.2 m chibi   (hips 0.50, crown 1.20) → 41 cm over the head, a third of the avatar's own
    ///     height floating above it.
    ///   * 3.5 m dragon  (hips 1.90, crown 3.50) → 49 cm BELOW the crown; the plate renders inside
    ///     the chest.
    ///
    /// That is the "some avatars are higher or lower" report: the error is exactly the avatar's
    /// deviation from one particular hips→crown distance, which is why it looks random per avatar
    /// rather than uniformly wrong.
    ///
    /// This class replaces the constant with the avatar's own measured crown plus a proportional
    /// gap. Everything here is plain float arithmetic in ONE frame — heights above the animator
    /// root (i.e. above the feet) in root-relative RENDERED metres, the frame
    /// <c>BasisTransformMapping.TposeWorld</c> and <c>BasisAvatar.AvatarEyePosition</c> are both
    /// already in — so it is Burst-safe, unit-testable without a scene, and shared by every
    /// placement site instead of being copied into each one.
    /// </summary>
    internal static class BasisNamePlateAnchorMath
    {
        /// <summary>
        /// Half-height of the baked name panel in plate-local units (see
        /// <c>BasisRemoteNamePlateDriver.BakeNameMeshGlobal</c>, which builds the quad at this
        /// half-height). The plate's origin is the quad's CENTRE, so the plate hangs this far below
        /// whatever point it is anchored at — 9 cm of it at the default plate world scale, which is
        /// most of the reason the old placement grazed tall avatars' hair even when the anchor
        /// itself cleared the head.
        /// </summary>
        public const float PanelHalfHeightUnits = 4.5f;

        /// <summary>The pre-fix constant, kept as the last-resort fallback and as the regression witness.</summary>
        public const float LegacyHeightAboveHips = 1.2f;

        /// <summary>
        /// Crown height above the eyes, as a multiple of the head bone → eye distance.
        ///
        /// The point of measuring the head against the EYE rather than against the torso is that the
        /// eye is the only landmark that scales with the head instead of with the body. A chibi's
        /// head is several times the head-to-torso ratio of an adult's, and its head bone → eye
        /// distance grows in the same proportion, so this estimate tracks it; any torso-relative rule
        /// does not.
        ///
        /// Adult anthropometry alone would say 1.1 (head bone ≈ 1.52 m, eye ≈ 1.63 m, crown ≈ 1.75 m
        /// on a 1.75 m stature — legs of 0.11 and 0.12). 1.35 instead, because stylised heads carry
        /// proportionally more skull above the eye line than human ones do, and the failure is not
        /// symmetric: this value is a FLOOR that renderer bounds may raise, so overshooting an adult
        /// by under a centimetre costs nothing, while undershooting an anime crown sinks the plate
        /// into the hair on exactly the avatars this engine is mostly used with.
        /// </summary>
        public const float EyeToCrownRatio = 1.35f;

        /// <summary>
        /// How far above the head bone renderer bounds are allowed to place the crown, as a multiple
        /// of the eye-derived head bone → crown distance. Bounds are what catch hair, ears, horns and
        /// hats — real geometry the skull-crown estimate misses — but in T-pose they also contain
        /// wings, haloes and floating props, and those must not drag the plate into the sky.
        ///
        /// 1.8 puts the ceiling ≈ 21 cm over an adult's crown: a top hat is admitted almost whole
        /// (and the clearance gap added afterwards covers the few centimetres clipped off it), while
        /// a wing arching a metre overhead is refused. There is no geometry in a renderer's bounds
        /// that says which part of it is a head, so a cap tuned between "tall hat" and "wingspan" is
        /// the whole of what can be done here.
        /// </summary>
        public const float CrownCapMultiple = 1.8f;

        /// <summary>Head bone → crown as a fraction of the hips → head bone span, used only when the
        /// avatar has no authored eye height. Adult proportions (0.23 / 0.57); wrong for a chibi,
        /// which is why it is the last resort rather than the primary estimate.</summary>
        public const float NoEyeCrownRatio = 0.45f;

        /// <summary>Bounds window when there is no eye to calibrate the head against — deliberately
        /// wide, because without the eye the bounds are the only real information about the head.</summary>
        public const float NoEyeBoundsFloorRatio = 0.15f;
        /// <inheritdoc cref="NoEyeBoundsFloorRatio"/>
        public const float NoEyeBoundsCeilingRatio = 2.5f;

        /// <summary>
        /// Clear air between the crown and the bottom edge of the plate, as a fraction of the
        /// avatar's height, clamped. Proportional rather than fixed so a 35 cm avatar does not wear
        /// its plate a hand's width up and a 4 m one does not wear it flush against its scalp; the
        /// clamps stop either extreme from running away.
        /// </summary>
        public const float GapFraction = 0.05f;
        /// <inheritdoc cref="GapFraction"/>
        public const float MinGap = 0.03f;
        /// <inheritdoc cref="GapFraction"/>
        public const float MaxGap = 0.15f;

        /// <summary>Smallest crown-above-head-bone distance we will believe, so a degenerate rig can
        /// never park the plate inside the skull.</summary>
        public const float MinHeadSpan = 0.01f;

        /// <summary>
        /// Estimates the top of the avatar's head in root-relative rendered metres.
        ///
        /// Two independent sources, because neither is sufficient alone: the authored eye height
        /// tells us the size of the head but not what is on top of it, and the renderer bounds tell
        /// us the whole silhouette but cannot say which part of it is the head. The eye-derived
        /// skull crown is the floor, the bounds raise it to real hair/hat geometry, and the cap keeps
        /// non-head geometry out.
        /// </summary>
        /// <param name="hipsAboveRoot">Hips bone height above the animator root at T-pose.</param>
        /// <param name="headAboveRoot">Head bone height above the animator root at T-pose.</param>
        /// <param name="eyeAboveRoot">Authored centre-eye height (<c>BasisAvatar.AvatarEyePosition.x</c>);
        /// pass 0 or anything at/below the head bone to declare it absent.</param>
        /// <param name="boundsTopAboveRoot">Highest renderer bounds top above the root at T-pose.</param>
        /// <param name="hasBounds">False when no renderer could be measured.</param>
        public static float EstimateCrownAboveRoot(float hipsAboveRoot, float headAboveRoot, float eyeAboveRoot,
            float boundsTopAboveRoot, bool hasBounds)
        {
            float torso = headAboveRoot - hipsAboveRoot;
            if (!IsFinite(torso) || torso < 0f)
            {
                torso = 0f;
            }

            bool hasEye = IsFinite(eyeAboveRoot) && eyeAboveRoot > headAboveRoot + MinHeadSpan;
            bool boundsUsable = hasBounds && IsFinite(boundsTopAboveRoot) && boundsTopAboveRoot > headAboveRoot;

            if (hasEye)
            {
                float eyeAboveHead = eyeAboveRoot - headAboveRoot;
                float skullCrown = eyeAboveRoot + (EyeToCrownRatio * eyeAboveHead);
                float headSpan = Max(skullCrown - headAboveRoot, MinHeadSpan);

                if (!boundsUsable)
                {
                    return skullCrown;
                }

                // Bounds only ever RAISE the crown, and only up to the cap: too-small bounds (a
                // hidden or mis-authored head mesh) must not pull the plate down into the face.
                float ceiling = headAboveRoot + (headSpan * CrownCapMultiple);
                return Max(skullCrown, Min(boundsTopAboveRoot, ceiling));
            }

            if (boundsUsable)
            {
                float floor = headAboveRoot + Max(torso * NoEyeBoundsFloorRatio, MinHeadSpan);
                float ceiling = headAboveRoot + Max(torso * NoEyeBoundsCeilingRatio, MinHeadSpan);
                return Clamp(boundsTopAboveRoot, floor, ceiling);
            }

            return headAboveRoot + Max(torso * NoEyeCrownRatio, MinHeadSpan);
        }

        /// <summary>Clear air we want between the crown and the plate's bottom edge, for an avatar
        /// whose crown is <paramref name="crownAboveRoot"/> metres off the floor.</summary>
        public static float ClearanceGap(float crownAboveRoot)
        {
            if (!IsFinite(crownAboveRoot) || crownAboveRoot <= 0f)
            {
                return MinGap;
            }
            return Clamp(crownAboveRoot * GapFraction, MinGap, MaxGap);
        }

        /// <summary>
        /// The distance from the hips to the plate's BOTTOM EDGE, in rendered metres. The hips are
        /// the anchor because the hips world pose is what the network actually carries and what the
        /// placement jobs already hold; the head bone's world position exists per frame too, but it
        /// dips and bobs with head tracking, and a plate that bobs with the wearer's neck reads as
        /// broken. Measuring a fixed T-pose height off the hips keeps the plate steady.
        /// </summary>
        public static float HeightAboveHips(float hipsAboveRoot, float crownAboveRoot)
        {
            float height = (crownAboveRoot - hipsAboveRoot) + ClearanceGap(crownAboveRoot);
            return IsFinite(height) && height > 0f ? height : LegacyHeightAboveHips;
        }

        /// <summary>
        /// Full registration-time measurement: crown estimate → hips-relative height → model units.
        ///
        /// The divide is the same conversion <c>BasisRemoteBoneMath.HeadAnchorOffset</c> makes for
        /// the eye/mouth anchors: the inputs are rendered metres (the root's scale baked in), the
        /// solve multiplies by the root's LIVE scale every frame, so what gets stored has to be
        /// model units or a networked resize would apply the avatar's scale twice.
        /// </summary>
        public static float MeasureHeightAboveHipsModelUnits(float hipsAboveRoot, float headAboveRoot,
            float eyeAboveRoot, float boundsTopAboveRoot, bool hasBounds, float rootScaleY)
        {
            float crown = EstimateCrownAboveRoot(hipsAboveRoot, headAboveRoot, eyeAboveRoot, boundsTopAboveRoot, hasBounds);
            float rendered = HeightAboveHips(hipsAboveRoot, crown);

            float scale = (IsFinite(rootScaleY) && rootScaleY > 1e-6f) ? rootScaleY : 1f;
            float model = rendered / scale;
            return IsFinite(model) && model > 0f ? model : LegacyHeightAboveHips / scale;
        }

        /// <summary>
        /// The plate transform's world Y. Called by every placement site — the transform-driving job
        /// in <c>BasisRemoteBoneDriver</c> and the merged-mesh matrix build in
        /// <c>BasisGlobalNamePlateRenderer</c> — so the two cannot drift apart.
        /// </summary>
        /// <param name="hipsWorldY">Live hips world height.</param>
        /// <param name="heightAboveHips">Per-frame hips→plate-bottom distance
        /// (<c>RemoteFrameOutput.NamePlateHeightAboveHips</c>, already at the avatar's live scale).</param>
        /// <param name="panelHalfHeightWorld">Half the plate's rendered height. Added here rather
        /// than baked into the measurement because plate size follows the VIEWER's scale and the
        /// nameplate size setting, not the avatar being labelled.</param>
        public static float AnchorWorldY(float hipsWorldY, float heightAboveHips, float panelHalfHeightWorld)
        {
            return hipsWorldY + heightAboveHips + panelHalfHeightWorld;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static float Min(float a, float b) => a < b ? a : b;
        private static float Max(float a, float b) => a > b ? a : b;
        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
