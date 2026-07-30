using System.Threading;
using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking;
using UnityEngine;

/// <summary>
/// Far avatars for remote players whose real avatar isn't loaded (see
/// <see cref="BasisAvatarFarLodRenderer"/>): a ~20-bone, ~8k-triangle proxy baked into the
/// bee connector, driven by the same networked bone data as the real avatar.
///
/// The far avatar lives PAST the max avatar range slider: inside avatar range the real
/// avatar always shows; beyond it the range system unloads the real avatar onto the fallback
/// skeleton and the far avatar renders there instead of the loading dummy. The same stand-in
/// path covers avatars that are still downloading, have no build for this platform, or are
/// performance-blocked. Transitions are budgeted per transmit tick because each swap forces
/// one bone-job sync, mirroring how avatar reloads are budgeted.
/// </summary>
public static class BasisAvatarFarLOD
{
    /// <summary>Master switch. When false, players without a real avatar show the loading dummy.</summary>
    public static bool Enabled;

    /// <summary>Swaps admitted per transmit tick; each one costs a bone-job sync.</summary>
    public static int MaxTransitionsPerTick = 4;

    public static void ApplyFromSettings()
    {
        bool wasEnabled = Enabled;
        Enabled = BasisSettingsDefaults.UseAvatarFarLod.RawValue;
        if (wasEnabled != Enabled)
        {
            ReapplyAllRemotes();
        }
    }

    /// <summary>
    /// Per-remote evaluation, called from the transmit tick's merged post-processing loop.
    /// Edge-triggered: does nothing while the desired state matches, and consumes one unit of
    /// <paramref name="transitionBudget"/> on a swap.
    /// </summary>
    public static void Tick(BasisRemotePlayer remote, ref int transitionBudget)
    {
        if (remote == null || transitionBudget <= 0)
        {
            return;
        }

        // While a real avatar downloads, promote to stand-in as soon as the connector (which
        // downloads first) carries a payload — the player shows as their own silhouette on the
        // fallback skeleton instead of as the loading dummy.
        if (remote.IsLoadingAnAvatar && !remote.FarLodIsAvatar)
        {
            BasisLoadableBundle loadingBundle = remote.AlwaysRequestedAvatar;
            if (loadingBundle?.BasisBundleConnector != null && !string.IsNullOrEmpty(loadingBundle.BasisBundleConnector.FarLodBase64))
            {
                CaptureFarLodFallback(remote, loadingBundle);
            }
        }

        // Self-heal a latched stand-in: a real, calibrated, non-fallback avatar means every
        // stand-in reason (platform missing, downloading, out of range) has expired. The
        // calibration seed normally clears this — if that edge was missed, the far avatar
        // would otherwise stay up at ANY distance and never swap back on approach.
        if (remote.FarLodIsAvatar && !remote.IsLoadingAnAvatar && !remote.IsConsideredFallBackAvatar && remote.BasisAvatar != null)
        {
            remote.FarLodIsAvatar = false;
            BasisDebug.Log($"Far avatar stand-in expired for {remote.DisplayName} (real avatar is loaded).", BasisDebug.LogTag.Avatar);
        }

        // A permanently pinned stand-in (real load failed) is BY DESIGN — but it looks
        // identical to a broken swap-back, so say so once instead of staying silent.
        if (remote.FarLodIsAvatar && remote.IsFarLodActive && remote.HasFailedAvatarLoadGlobally && !remote.FarLodPinLogged)
        {
            remote.FarLodPinLogged = true;
            BasisDebug.LogWarning($"Far avatar for {remote.DisplayName} is standing in at ALL distances because their real avatar FAILED to load (see the earlier 'Loading avatar failed' error). It will not swap to the full avatar until a working avatar arrives.", BasisDebug.LogTag.Avatar);
        }

        bool current = remote.IsFarLodActive;

        // Range decisions are hips-fed for far players at the job-input level, but the
        // registration's mouth output is still consumed elsewhere (voice positioning), so
        // cross-check it against the network hips every few seconds. The transforms in the
        // log separate a corrupt payload (sane farHead, huge offset lever) from a flung
        // skeleton or a desynced job slot.
        if (current && Time.unscaledTime >= remote.FarLodNextDistanceCheckTime)
        {
            remote.FarLodNextDistanceCheckTime = Time.unscaledTime + 5f;
            var poseReceiver = remote.NetworkReceiver;
            if (poseReceiver != null && RemoteBoneJobSystem.GetOutGoingMouth(poseReceiver.playerId, out var mouth))
            {
                poseReceiver.GetLatestNetworkPose(out var hipsWorldPos, out _, out _);
                if (((Vector3)mouth - (Vector3)hipsWorldPos).sqrMagnitude > 100f)
                {
                    var farLod = remote.FarLod;
                    string farState = farLod != null && farLod.Head != null && farLod.RootTransform != null
                        ? $" farHead={farLod.Head.position} farRoot={farLod.RootTransform.position} farScale={farLod.RootTransform.lossyScale}"
                        : " farState=<destroyed>";
                    BasisDebug.LogError($"Far avatar mouth output broken for {remote.DisplayName}: mouth={(Vector3)mouth} vs network hips={(Vector3)hipsWorldPos} (standIn={remote.FarLodIsAvatar}).{farState}", BasisDebug.LogTag.Avatar);
                }
            }
        }

        // The far avatar only ever runs as the stand-in: past avatar range, mid-download,
        // platform-missing, or perf-blocked — the range system owns the distance decision
        // (its slider is the boundary), this system owns what shows beyond it.
        bool desired = remote.FarLodIsAvatar && IsEligible(remote);
        if (desired == current)
        {
            return;
        }
        if (remote.SetFarLodActive(desired))
        {
            transitionBudget--;
        }
        else if (!desired)
        {
            // A refused deactivation is the "won't swap back" symptom — say why.
            BasisDebug.LogWarning($"Far avatar swap-back did not transition for {remote.DisplayName}: active={remote.IsFarLodActive} standIn={remote.FarLodIsAvatar} loading={remote.IsLoadingAnAvatar} fallback={remote.IsConsideredFallBackAvatar} avatar={(remote.BasisAvatar != null)} inBoneDriver={remote.RemoteAvatarDriver?.InBoneDriver}", BasisDebug.LogTag.Avatar);
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
        remote.FarLodOverrideSource = bundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation;
        remote.FarLodIsAvatar = true;
    }

    private static readonly SemaphoreSlim sConnectorFetchGate = new SemaphoreSlim(4);

    /// <summary>
    /// Fetches just the bee connector (two ranged requests, no bundle download) for a player
    /// whose avatar was never loaded — out-of-range players get their real silhouette without
    /// ever paying for the full avatar. Fire-and-forget; the transmit tick activates the far
    /// LOD once the payload lands.
    /// </summary>
    public static async void RequestFarLodPayload(BasisRemotePlayer remote, BasisLoadableBundle bundle)
    {
        if (remote == null || bundle == null || remote.FarLodConnectorFetchInFlight ||
            string.IsNullOrEmpty(bundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation))
        {
            return;
        }
        if (bundle.BasisBundleConnector != null)
        {
            CaptureFarLodFallback(remote, bundle);
            return;
        }

        remote.FarLodConnectorFetchInFlight = true;
        try
        {
            await sConnectorFetchGate.WaitAsync();
            try
            {
                if (remote.IsDestroyed || bundle.BasisBundleConnector != null)
                {
                    return;
                }
                BasisTrackedBundleWrapper wrapper = new BasisTrackedBundleWrapper { LoadableBundle = bundle };
                await BasisBeeManagement.HandleMetaOnlyLoad(wrapper, new BasisProgressReport(), CancellationToken.None);
            }
            finally
            {
                sConnectorFetchGate.Release();
            }

            // The player may have disconnected or switched avatars during the fetch.
            if (!remote.IsDestroyed && remote.AlwaysRequestedAvatar == bundle && bundle.BasisBundleConnector != null)
            {
                CaptureFarLodFallback(remote, bundle);
            }
        }
        catch (System.Exception e)
        {
            BasisDebug.Log($"Far LOD connector fetch failed: {e.Message}", BasisDebug.LogTag.Avatar);
        }
        finally
        {
            remote.FarLodConnectorFetchInFlight = false;
        }
    }

    /// <summary>
    /// Seed hook, run at the end of remote calibration. Releases any far avatar built for the
    /// previous avatar (a new calibration means a new avatar version) and, when this
    /// calibration is the fallback skeleton hosting a stand-in, puts the far avatar up
    /// immediately instead of waiting for the next transmit tick.
    /// </summary>
    public static void SeedAfterCalibration(BasisRemotePlayer remote)
    {
        if (remote == null)
        {
            return;
        }
        remote.ResetFarLodForNewAvatar();

        if (remote.FarLodIsAvatar && IsEligible(remote))
        {
            remote.SetFarLodActive(true);
        }
    }

    private static bool IsEligible(BasisRemotePlayer remote)
    {
        // Stand-ins run ON the fallback avatar (including mid-download), and AlwaysShowAvatar
        // means "show me this player" — the far avatar is the best available representation.
        return Enabled && !remote.IsEffectivelyBlocked &&
               remote.RemoteAvatarDriver != null && remote.RemoteAvatarDriver.InBoneDriver &&
               remote.HasFarLodPayload;
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
            if (!Enabled && remote.IsFarLodActive)
            {
                // FarLodIsAvatar stays latched, so re-enabling restores the stand-in on the
                // next transmit tick — which owns the per-tick swap budget, so no bulk
                // transition storm starts from here.
                remote.SetFarLodActive(false);
            }
        }
    }
}
