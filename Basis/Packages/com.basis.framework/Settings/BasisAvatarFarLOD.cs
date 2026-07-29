using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking;
using UnityEngine;

/// <summary>
/// Distance-based swap of remote avatars to their baked far LODs (see
/// <see cref="BasisAvatarFarLodRenderer"/>). Beyond the configured distance the whole avatar
/// GameObject sleeps — no full-res skinning, no jiggle, no face work — and a ~20-bone,
/// ~1.5k-triangle proxy driven by the same networked bone data renders instead.
///
/// Only avatars whose bundle connector carries an far LOD payload participate; everything
/// else keeps the existing mesh/skin/shadow LOD behavior. Transitions are hysteretic (10%)
/// and budgeted per transmit tick because each swap forces one bone-job sync, mirroring how
/// avatar reloads are budgeted.
/// </summary>
public static class BasisAvatarFarLOD
{
    /// <summary>Master switch. When false every remote is restored to its real avatar.</summary>
    public static bool Enabled;

    /// <summary>Distance in meters past which a remote swaps to its far LOD.</summary>
    public static float ImposterDistance = 20f;

    /// <summary>Swaps admitted per transmit tick; each one costs a bone-job sync.</summary>
    public static int MaxTransitionsPerTick = 4;

    private static float _enterDistanceSq = 400f;
    private static float _exitDistanceSq = 400f / (1.1f * 1.1f);

    public static void ApplyFromSettings()
    {
        bool wasEnabled = Enabled;
        float wasDistance = ImposterDistance;
        Enabled = BasisSettingsDefaults.UseAvatarFarLod.RawValue;
        ImposterDistance = Mathf.Max(1f, BasisSettingsDefaults.AvatarFarLodDistance.RawValue);
        _enterDistanceSq = ImposterDistance * ImposterDistance;
        _exitDistanceSq = _enterDistanceSq / (1.1f * 1.1f);

        if (wasEnabled != Enabled || !Mathf.Approximately(wasDistance, ImposterDistance))
        {
            ReapplyAllRemotes();
        }
    }

    public static bool WantsFarLod(float distanceSq, bool currentlyFarLod)
    {
        return currentlyFarLod ? distanceSq > _exitDistanceSq : distanceSq > _enterDistanceSq;
    }

    /// <summary>
    /// Per-remote evaluation, called from the transmit tick's merged post-processing loop with
    /// the distance it already has in hand. Edge-triggered: does nothing while the desired
    /// state matches, and consumes one unit of <paramref name="transitionBudget"/> on a swap.
    /// </summary>
    public static void Tick(BasisRemotePlayer remote, float distanceSq, ref int transitionBudget)
    {
        if (remote == null || transitionBudget <= 0)
        {
            return;
        }
        bool current = remote.IsFarLodActive;
        // Stand-in mode ignores distance and the swap toggle: the far LOD IS the avatar
        // (no build exists for this platform), so it stays up until a real avatar calibrates.
        bool desired = remote.FarLodIsAvatar
            ? IsEligible(remote)
            : Enabled && WantsFarLod(distanceSq, current) && IsEligible(remote);
        if (desired == current)
        {
            return;
        }
        if (remote.SetFarLodActive(desired))
        {
            transitionBudget--;
        }
    }

    /// <summary>
    /// Remembers the original bundle's far LOD payload when the real avatar can't be used
    /// (no section for this platform, load failure, performance block). The fallback avatar
    /// still loads and calibrates as usual — the far LOD then renders in its place at every
    /// distance, driven by the same networked bone data.
    /// </summary>
    public static void CaptureFarLodFallback(BasisRemotePlayer remote, BasisLoadableBundle bundle)
    {
        BasisBundleConnector connector = bundle?.BasisBundleConnector;
        if (remote == null || connector == null ||
            string.IsNullOrEmpty(connector.UniqueVersion) || string.IsNullOrEmpty(connector.FarLodBase64))
        {
            return;
        }
        remote.FarLodOverridePayload = connector.FarLodBase64;
        remote.FarLodOverrideVersion = connector.UniqueVersion;
        remote.FarLodIsAvatar = true;
    }

    /// <summary>
    /// Seed hook, run at the end of remote calibration. The tick is edge-triggered on distance
    /// crossings, so an avatar that loads while already far away would otherwise pop in at full
    /// detail until the next boundary crossing. Also releases any far LOD built for the
    /// previous avatar — a new calibration means a new avatar version.
    /// </summary>
    public static void SeedAfterCalibration(BasisRemotePlayer remote)
    {
        if (remote == null)
        {
            return;
        }
        remote.ResetFarLodForNewAvatar();

        // Stand-in mode: this calibration is the fallback avatar hosting the skeleton —
        // put the far LOD up immediately, at any distance, toggle or not.
        if (remote.FarLodIsAvatar)
        {
            if (IsEligible(remote))
            {
                remote.SetFarLodActive(true);
            }
            return;
        }

        if (!Enabled)
        {
            return;
        }
        var receiver = remote.NetworkReceiver;
        if (receiver == null)
        {
            return;
        }
        receiver.GetLatestNetworkPose(out var hipsWorldPos, out _, out _);
        float distanceSq = ((Vector3)hipsWorldPos - Basis.Scripts.Drivers.BasisLocalCameraDriver.HeadPosition).sqrMagnitude;
        if (WantsFarLod(distanceSq, false) && IsEligible(remote))
        {
            remote.SetFarLodActive(true);
        }
    }

    private static bool IsEligible(BasisRemotePlayer remote)
    {
        if (remote.IsEffectivelyBlocked || remote.IsLoadingAnAvatar ||
            remote.RemoteAvatarDriver == null || !remote.RemoteAvatarDriver.InBoneDriver || !remote.HasFarLodPayload)
        {
            return false;
        }
        if (remote.FarLodIsAvatar)
        {
            // Stand-in mode runs ON the fallback avatar, and AlwaysShowAvatar means "show me
            // this player" — the far LOD is the best available representation of them.
            return true;
        }
        return !remote.AlwaysShowAvatar && !remote.IsConsideredFallBackAvatar;
    }

    private static void ReapplyAllRemotes()
    {
        foreach (var kvp in BasisNetworkPlayers.RemotePlayers)
        {
            BasisRemotePlayer remote = kvp.Value;
            if (remote == null)
            {
                continue;
            }
            if (!Enabled && remote.IsFarLodActive && !remote.FarLodIsAvatar)
            {
                remote.SetFarLodActive(false);
            }
            // Enabling (or a distance change) is picked up by the next transmit tick — it owns
            // the per-tick swap budget, so no bulk transition storm starts from here.
        }
    }
}
