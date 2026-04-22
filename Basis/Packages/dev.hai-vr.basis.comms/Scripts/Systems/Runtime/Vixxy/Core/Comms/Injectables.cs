using System;

namespace HVR.Vixxy
{
    /// Denotes a field that can always be injected before Play Mode is even entered,
    /// because the instance always comes alongside it with the same scene or prefab.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class EarlyInjectable : Attribute
    {
    }

    /// Denotes a field that can only be injected late, because the instance comes from
    /// another scene or an unconnected prefab.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class LateInjectable : Attribute
    {
    }

    /// Denotes a field that isn't meant for the user to fill, and should probably be null by default.
    /// Used for quick and visible system outputs.
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class AutoOutput : Attribute
    {
    }
}
