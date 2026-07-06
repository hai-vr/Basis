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

    /// <summary>Optional secondary action, rendered as a labeled button next to the
    /// remove button (e.g. a "Share"/"Unshare" toggle). Null/empty label = no button.
    /// The registering package owns the semantics; the Library UI just presents it.</summary>
    public string ActionLabel;
    public Action Action;
    /// <summary>Non-null = the Library shows a yes/no dialog with this title/body
    /// before invoking <see cref="Action"/> (for consent-style actions).</summary>
    public string ActionConfirmTitle;
    public string ActionConfirmBody;
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

    /// <summary>Update an entry's secondary action presentation (label + optional
    /// confirm text) — e.g. flipping a Share button to Unshare after it's invoked.
    /// A null/empty label removes the button. The Action delegate itself stays as
    /// registered.</summary>
    public static void SetAction(string id, string label, string confirmTitle = null, string confirmBody = null)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (Entries.TryGetValue(id, out BasisShareableEntry entry))
        {
            entry.ActionLabel = label;
            entry.ActionConfirmTitle = confirmTitle;
            entry.ActionConfirmBody = confirmBody;
            OnChanged?.Invoke();
        }
    }

    /// <summary>Update who shared an entry (rendered as "shared by …") after
    /// registration — e.g. once a received share's sharer is identified.</summary>
    public static void SetSharerName(string id, string sharerName)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (Entries.TryGetValue(id, out BasisShareableEntry entry))
        {
            entry.SharerName = sharerName;
            OnChanged?.Invoke();
        }
    }

    public static IReadOnlyCollection<BasisShareableEntry> GetAll() => Entries.Values;
}
