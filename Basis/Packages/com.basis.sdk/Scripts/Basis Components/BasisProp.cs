using UnityEngine;

/// <summary>
/// Represents a prop in the Basis system. Any script deriving from <see cref="BasisContentBase"/>
/// can participate in the prop ecosystem and be networked with minimal setup.
/// </summary>
public class BasisProp : BasisContentBase
{
    /// <summary>
    /// How this prop asks to be placed when a player spawns it. Left at
    /// <see cref="BasisPropSpawnPlacement.Unspecified"/> the spawning client places it the way it
    /// always has, so existing props need no change.
    /// </summary>
    public BasisPropSpawnMetaData SpawnMetaData = BasisPropSpawnMetaData.Authoring;
}
