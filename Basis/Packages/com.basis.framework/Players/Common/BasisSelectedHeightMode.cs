namespace Basis.Scripts.BasisSdk.Players
{
    /// <summary>
    /// Defines the available modes for determining a player's selected height
    /// within the Basis SDK. These modes control how height is measured or 
    /// specified when adapting to different player setups.
    /// </summary>
    public enum BasisSelectedHeightMode
    {
        /// <summary>
        /// Height is estimated based on the player's arm span. 
        /// This is often used when direct height data is unavailable 
        /// but arm span can be measured or inferred.
        /// </summary>
        ArmSpan,

        /// <summary>
        /// Height is set according to the player's eye level. 
        /// This mode is commonly applied in VR setups where 
        /// eye position is tracked directly.
        /// </summary>
        EyeHeight,

        /// <summary>
        /// Fits the player into the avatar using every measurement available rather than picking one.
        /// Resolved by BasisHeightDriver.ResolveHeightMode; this is what Auto becomes in VR.
        /// </summary>
        Auto,

        /// <summary>
        /// Scale chosen by <see cref="Basis.IK.BasisScaleFitCore"/> from every body measurement held —
        /// eye height, arm span, hip height — with the positional stretcher taking up whatever the one
        /// uniform scale could not cover. Sits as close to the eye-matched scale as the other segments
        /// allow, so while the stretcher can absorb the difference the viewpoint stays exactly right.
        ///
        /// Not offered in the height-mode dropdown: it is what Auto resolves to, not a fourth choice
        /// the player picks between. The dropdown maps its indices by hand (SMModuleCalibration), so
        /// this value never surfaces there.
        /// </summary>
        BestFit,
    }
}
