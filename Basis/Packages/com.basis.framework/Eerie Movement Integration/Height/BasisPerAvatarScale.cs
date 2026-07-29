using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Settings;
using UnityEngine;

/// <summary>
/// A per-avatar size nudge, remembered for that avatar alone.
///
/// The body measurements describe the PLAYER and are deliberately global — you are the same size
/// whatever you are wearing. But avatars are not obliged to be anatomical: a chibi with a head half
/// its height, or a lanky one with arms a third longer than its legs, will land somewhere the fit
/// cannot fully rescue, because one uniform scale plus a bounded stretch cannot make every set of
/// proportions feel right. Without a per-avatar escape hatch the only way out is to distort the
/// player's measured size, which then follows them onto every other avatar.
///
/// Stored through <see cref="BasisSettingsSystem"/> under a hashed avatar key, the same approach the
/// per-device tracker mounting offsets use.
/// </summary>
public static class BasisPerAvatarScale
{
    public const float Min = 0.5f;
    public const float Max = 2f;
    public const float None = 1f;

    /// <summary>The multiplier for the avatar currently worn; 1 when it has never been nudged.</summary>
    public static float Current { get; private set; } = None;

    static string s_loadedForAvatar;

    static string KeyFor(string avatarId)
    {
        // Avatar identifiers are URLs — long, and full of characters a settings key should not carry.
        // A stable hash keeps the key short without needing them to be readable.
        return $"avatarscale::{(uint)avatarId.GetHashCode():X8}";
    }

    /// <summary>
    /// Refreshes <see cref="Current"/> if the worn avatar changed. Cheap enough to call from the scale
    /// path every time, which removes any chance of a stale override outliving an avatar swap.
    /// </summary>
    public static void RefreshForCurrentAvatar()
    {
        string avatarId = BasisLocalPlayer.CurrentAvatarUniqueID;
        if (string.IsNullOrEmpty(avatarId))
        {
            Current = None;
            s_loadedForAvatar = null;
            return;
        }
        if (avatarId == s_loadedForAvatar)
        {
            return;
        }

        s_loadedForAvatar = avatarId;
        string key = KeyFor(avatarId);
        Current = BasisSettingsSystem.HasSaveData(key)
            ? Sanitize(BasisSettingsSystem.LoadFloat(key, None))
            : None;
        if (!Mathf.Approximately(Current, None))
        {
            BasisDebug.Log($"Per-avatar size nudge for this avatar: {Current:P0}", BasisDebug.LogTag.Avatar);
        }
    }

    public static void SetForCurrentAvatar(float scale)
    {
        string avatarId = BasisLocalPlayer.CurrentAvatarUniqueID;
        if (string.IsNullOrEmpty(avatarId))
        {
            return;
        }
        s_loadedForAvatar = avatarId;
        Current = Sanitize(scale);
        BasisSettingsSystem.SaveFloat(KeyFor(avatarId), Current);
        BasisHeightDriver.ApplyScaleAndHeight();
    }

    public static void ClearForCurrentAvatar() => SetForCurrentAvatar(None);

    public static bool IsOverridden => !Mathf.Approximately(Current, None);

    static float Sanitize(float scale)
    {
        if (float.IsNaN(scale) || float.IsInfinity(scale))
        {
            return None;
        }
        return Mathf.Clamp(scale, Min, Max);
    }
}
