#if BASIS_MEDIAPLAYER_EXISTS && YTDLP_EXISTS
using UnityEngine;

namespace Basis.Integration.YtDlp
{
    /// <summary>
    /// Installs the yt-dlp resolver into <see cref="BasisMediaUrlRouter"/> at startup,
    /// so any player URL field (e.g. <c>BasisMediaPlayerStreaming</c>) steers page URLs
    /// through yt-dlp while directly-playable streams load unchanged. The player core
    /// holds no reference to this package; removing the package removes the
    /// registration (the router falls back to direct loads), with nothing dangling.
    /// </summary>
    internal static class BasisYtDlpRouterInstaller
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            // The router holds a single resolver slot. Don't clobber another package's resolver
            // if one is already installed — first-come wins, and the collision is surfaced rather
            // than silently overwritten.
            if (BasisMediaUrlRouter.Resolver != null)
            {
                BasisDebug.LogWarning(
                    "A BasisMediaUrlRouter resolver is already installed; the yt-dlp resolver will not replace it.",
                    BasisDebug.LogTag.Video);
                return;
            }

            BasisMediaUrlRouter.Resolver = (player, url) =>
            {
                // Already directly playable (transport scheme or media-extension URL):
                // decline so the caller loads it directly. Otherwise it's a page URL —
                // resolve it to its stream(s) and load (async).
                if (!BasisYtDlpResolver.NeedsResolution(url)) return false;
                BasisYtDlpResolver.ResolveAndPlay(player, url);
                return true;
            };
        }
    }
}
#endif
