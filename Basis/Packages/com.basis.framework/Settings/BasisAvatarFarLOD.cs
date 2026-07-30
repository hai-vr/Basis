using System.Threading;
using Basis.BasisUI;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking;

/// <summary>
/// Far avatars for remote players whose real avatar isn't loaded: a ~20-bone, ~8k-triangle
/// proxy baked into the bee connector, built at runtime as a REAL <see cref="BasisAvatar"/>
/// (see <see cref="BasisFarAvatarBuilder"/>) and installed through the exact same pipeline as
/// every other avatar. Loading avatar, bundle avatar, glTF avatar and far avatar are all the
/// same thing to the rest of the system — avatars are swapped, never hidden or disabled.
///
/// The far avatar lives PAST the max avatar range slider: inside avatar range the real
/// avatar always shows; beyond it the range system unloads the real avatar and the far
/// avatar replaces the loading dummy. It also fronts the player while their real avatar
/// downloads, has no build for this platform, or failed to load.
/// </summary>
public static class BasisAvatarFarLOD
{
    /// <summary>Master switch. When false, players without a real avatar show the loading dummy.</summary>
    public static bool Enabled;

    /// <summary>Avatar swaps admitted per transmit tick; each one costs a full avatar install.</summary>
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
    /// Per-remote reconciliation, called from the transmit tick's merged post-processing
    /// loop. Edge-triggered: does nothing while the current avatar kind matches the desired
    /// one, and consumes one unit of <paramref name="transitionBudget"/> per swap.
    /// </summary>
    public static void Tick(BasisRemotePlayer remote, ref int transitionBudget)
    {
        if (remote == null || transitionBudget <= 0)
        {
            return;
        }

        // While a real avatar downloads, cache the payload as soon as the connector (which
        // downloads first) carries one — the far avatar can then front the download.
        if (remote.IsLoadingAnAvatar && string.IsNullOrEmpty(remote.FarLodOverridePayload))
        {
            BasisLoadableBundle loadingBundle = remote.AlwaysRequestedAvatar;
            if (loadingBundle?.BasisBundleConnector != null && !string.IsNullOrEmpty(loadingBundle.BasisBundleConnector.FarLodBase64))
            {
                CaptureFarLodFallback(remote, loadingBundle);
            }
        }

        // Join-time evaluation, deferred to this loop so it uses the job's distance: joiners
        // load only the fallback, and an out-of-range joiner never gets a range edge, so run
        // one CreateAvatar pass for them — it reads visibility settings and either starts the
        // far avatar fetch or keeps the fallback. In-range joiners are excluded (their range
        // edge, pending or committed, loads the full avatar).
        if (!remote.FarLodInitialEvaluated && !remote.InAvatarRange && !remote.PendingRangeActive &&
            !remote.IsLoadingAnAvatar && remote.IsConsideredFallBackAvatar && !remote.IsFarLodActive &&
            remote.AlwaysRequestedAvatar != null)
        {
            remote.FarLodInitialEvaluated = true;
            transitionBudget--;
            remote.ReloadAvatar();
            return;
        }

        bool wearingFar = remote.IsFarLodActive;
        // No avatar-range check here: the far avatar IS the beyond-range representation, so
        // a range of zero simply means everyone wears their far avatar. UseAvatarFarLod off
        // is the switch that drops players to the loading dummy instead.
        bool wantsFar = Enabled &&
            !remote.IsEffectivelyBlocked && remote.HasFarLodPayload &&
            (!remote.InAvatarRange || remote.HasFailedAvatarLoadGlobally || remote.IsLoadingAnAvatar);
        if (remote.AlwaysShowAvatar && !remote.IsLoadingAnAvatar && !remote.HasFailedAvatarLoadGlobally)
        {
            // "Show me this player" wants the real avatar; the far avatar only bridges
            // downloads and dead loads for them.
            wantsFar = false;
        }

        if (wantsFar && !wearingFar)
        {
            // Far avatars only ever replace the fallback dummy — a live real avatar is the
            // range machinery's to unload (its edge drops the player to the dummy first).
            if (remote.IsConsideredFallBackAvatar && BasisFarAvatarBuilder.TryInstall(remote))
            {
                transitionBudget--;
            }
        }
        else if (!wantsFar && wearingFar && !remote.IsLoadingAnAvatar)
        {
            // Back in range (or the far avatar became unwanted) — reload through the normal
            // pipeline: in range loads the real avatar, hidden/blocked/disabled drops to the
            // dummy. The range edge usually beats this branch; it covers missed edges.
            transitionBudget--;
            remote.ReloadAvatar();
        }
    }

    /// <summary>
    /// Caches the original bundle's far avatar payload on the player. Used whenever the real
    /// avatar isn't in hand: mid-download, no section for this platform, load failure, out of
    /// range. The payload is a fixed small cost regardless of how heavy the real avatar is.
    /// </summary>
    public static void CaptureFarLodFallback(BasisRemotePlayer remote, BasisLoadableBundle bundle)
    {
        BasisBundleConnector connector = bundle?.BasisBundleConnector;
        if (remote == null || connector == null)
        {
            return;
        }
        if (string.IsNullOrEmpty(connector.FarLodBase64))
        {
            BasisDebug.Log($"Avatar bee for {remote.DisplayName} carries no far avatar payload — staying on the fallback.", BasisDebug.LogTag.Avatar);
            return;
        }
        if (string.IsNullOrEmpty(connector.UniqueVersion))
        {
            BasisDebug.LogWarning($"Far avatar capture for {remote.DisplayName} declined: connector has no UniqueVersion.", BasisDebug.LogTag.Avatar);
            return;
        }
        string source = bundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation;
        if (remote.FarLodOverrideSource != source || string.IsNullOrEmpty(remote.FarLodOverridePayload))
        {
            remote.FarLodOverridePayload = connector.FarLodBase64;
            remote.FarLodOverrideVersion = connector.UniqueVersion;
            remote.FarLodOverrideSource = source;
            remote.ResetFarLodForNewAvatar();
        }
    }

    private static readonly SemaphoreSlim sConnectorFetchGate = new SemaphoreSlim(4);

    /// <summary>
    /// Fetches just the bee connector (two ranged requests, no bundle download) for a player
    /// whose avatar was never loaded — out-of-range players get their real silhouette without
    /// ever paying for the full avatar. Fire-and-forget; the transmit tick installs the far
    /// avatar once the payload lands.
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
            BasisMetaLoadResult metaResult = BasisMetaLoadResult.Success;
            await sConnectorFetchGate.WaitAsync();
            try
            {
                if (!remote.IsDestroyed && bundle.BasisBundleConnector == null)
                {
                    BasisTrackedBundleWrapper wrapper = new BasisTrackedBundleWrapper { LoadableBundle = bundle };
                    metaResult = await BasisBeeManagement.HandleMetaOnlyLoad(wrapper, new BasisProgressReport(), CancellationToken.None);
                }
            }
            finally
            {
                sConnectorFetchGate.Release();
            }

            if (remote.IsDestroyed)
            {
                return;
            }
            // Identity by bee URL, not instance — a second avatar message during the fetch
            // recreates AlwaysRequestedAvatar as a new object for the same bee.
            string fetchedSource = bundle.BasisRemoteBundleEncrypted.RemoteBeeFileLocation;
            string currentSource = remote.AlwaysRequestedAvatar?.BasisRemoteBundleEncrypted.RemoteBeeFileLocation;
            if (bundle.BasisBundleConnector != null && fetchedSource == currentSource)
            {
                CaptureFarLodFallback(remote, bundle);
            }
            else
            {
                BasisDebug.LogWarning($"Far avatar connector fetch for {remote.DisplayName} produced nothing to capture: connector={(bundle.BasisBundleConnector != null)} sameAvatar={fetchedSource == currentSource} loaded={metaResult.Loaded} transient={metaResult.IsTransient} error={metaResult.Error ?? "none"}", BasisDebug.LogTag.Avatar);
            }
        }
        catch (System.Exception e)
        {
            BasisDebug.LogWarning($"Far avatar connector fetch failed for {remote.DisplayName}: {e.Message}", BasisDebug.LogTag.Avatar);
        }
        finally
        {
            remote.FarLodConnectorFetchInFlight = false;
        }
    }

    /// <summary>
    /// Seed hook, run at the end of remote calibration. A far avatar's own calibration is a
    /// no-op here; any other avatar re-evaluates payload availability (a new calibration may
    /// mean a new avatar version).
    /// </summary>
    public static void SeedAfterCalibration(BasisRemotePlayer remote)
    {
        if (remote?.BasisAvatar == null || remote.BasisAvatar.IsFarLodAvatar)
        {
            return;
        }
        remote.ResetFarLodForNewAvatar();
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
            if (!Enabled && remote.IsFarLodActive && !remote.IsLoadingAnAvatar)
            {
                // Reload routes them back to the dummy (or real avatar if in range). The
                // payload cache stays, so re-enabling restores far avatars next tick.
                remote.ReloadAvatar();
            }
        }
    }
}
