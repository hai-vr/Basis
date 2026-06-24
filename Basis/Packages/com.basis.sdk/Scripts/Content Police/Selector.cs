public partial class BundledContentHolder
{
    public enum Selector
    {
        Avatar = 0,
        System = 1,
        Prop = 2,
        World = 3,
    }
    public enum Mode
    {
        Avatar = 0,
        World = 1,
        Prop = 2,
        Legacy = 3, // this will be used to flag items before new basis metadata for now
    }
    
    // used to determine which item key stores are local or networked
    // used as reference by the library provider to determine network type of item
    public enum NetworkType
    {
        Local = 0,
        Networked = 1,
        /// <summary>
        /// Downloads to all connected players, each reports readiness to the server,
        /// then the server signals everyone to spawn simultaneously.
        /// Has a 5-minute timeout; players that fail to load are skipped.
        /// </summary>
        Synchronized = 2,
        /// <summary>
        /// Loads locally now and persists the item so it is reloaded automatically on
        /// every startup (see BasisPreloadContentStore / BasisBootContentLoader). Worlds
        /// become the initial world; props respawn at the player origin.
        /// </summary>
        LoadOnBoot = 3,
        /// <summary>
        /// Tells every connected client to download and cache the bundle to disc now,
        /// without spawning it. A later Networked load resolves from each client's
        /// on-disc cache, so it is near-instant for everyone who pre-downloaded.
        /// </summary>
        Predownload = 4,
    }

    // used to determine the way an item can be placed into a world
    public enum PlacementType
    {
        SpawnAtRaycast = 0, // will spawn the item using the players desired location using raycast placement
        SpawnInFrontOfPlayer = 1, // will spawn the item at the players eye height and in front of them, and face towards them
        SpawnAtPlayerOrigin = 2, // will spawn the item at the players origin
    }
}
