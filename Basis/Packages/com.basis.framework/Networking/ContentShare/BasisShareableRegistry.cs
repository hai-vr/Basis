using System;
using System.Collections.Generic;

public enum BasisShareableKind
{
    Server,
    World,
    Prop,
    Avatar,
    Image,
    Other
}

public sealed class BasisShareableEntry
{
    public string Id;
    public BasisShareableKind Kind;
    public string Title;
    public string SharerName;
    public Action Remove;
}

/// <summary>
/// Framework-side list of shareables currently active in the instance, surfaced in the Library
/// "Instantiated" tab. Content shares register their spheres here, and optional add-on packages
/// (e.g. the image pickup) register their own entries, so the Library UI can monitor and remove them
/// without the framework referencing those packages.
/// </summary>
public static class BasisShareableRegistry
{
    private static readonly Dictionary<string, BasisShareableEntry> Entries = new Dictionary<string, BasisShareableEntry>();

    public static Action OnChanged;

    public static void Register(BasisShareableEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.Id)) return;
        Entries[entry.Id] = entry;
        OnChanged?.Invoke();
    }

    public static void Unregister(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (Entries.Remove(id)) OnChanged?.Invoke();
    }

    public static void SetDetail(string id, string detail)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (Entries.TryGetValue(id, out BasisShareableEntry entry))
        {
            entry.Title = detail;
            OnChanged?.Invoke();
        }
    }

    public static IReadOnlyCollection<BasisShareableEntry> GetAll() => Entries.Values;
}
