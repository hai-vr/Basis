namespace Basis.Scripts.Drivers
{
    public enum BasisScriptedInputBlend
    {
        /// <summary>Script input is summed with the player's own input; the player keeps control.</summary>
        Additive = 0,
        /// <summary>Script input replaces the player's own input for the frames it is driving.</summary>
        Override = 1,
    }
}
