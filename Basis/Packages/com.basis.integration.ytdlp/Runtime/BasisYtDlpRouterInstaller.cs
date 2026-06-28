#if BASIS_MEDIAPLAYER_EXISTS && YTDLP_EXISTS
using UnityEngine;

namespace Basis.Integration.YtDlp
{
    /// <summary>
    /// Registers the yt-dlp resolver with <see cref="BasisMediaUrlRouter"/> at startup, so any
    /// player URL field (e.g. <c>BasisMediaPlayerStreaming</c>) steers page URLs through yt-dlp
    /// while directly-playable streams load unchanged. The player core holds no reference to this
    /// package; removing the package removes the registration (the router falls back to direct
    /// loads, or to another registered resolver), with nothing dangling.
    /// </summary>
    internal static class BasisYtDlpRouterInstaller
    {
        private static BasisYtDlpVideoResolver installed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (installed != null) return; // idempotent across domain reloads
            installed = new BasisYtDlpVideoResolver();
            BasisMediaUrlRouter.Register(installed);
        }
    }

    /// <summary>
    /// Routes page URLs (YouTube, Twitch, …) through the in-process yt-dlp resolver. Directly
    /// playable URLs are declined via <see cref="CanResolve"/> so the player opens them itself.
    /// </summary>
    internal sealed class BasisYtDlpVideoResolver : IBasisVideoResolver
    {
        // yt-dlp is the generic last-resort resolver — it can attempt almost any page URL, so it
        // registers at the lowest priority and only sees URLs no more-specific resolver claimed.
        // CanResolve stays broad on purpose: there's no cheap, non-blocking way to know which of
        // yt-dlp's sites a URL belongs to without running (async) extraction, so the fallback takes
        // any non-directly-playable URL and surfaces failures via the async resolve path.
        public int Priority => int.MinValue;

        public bool CanResolve(string url) => BasisYtDlpResolver.NeedsResolution(url);

        public bool TryResolve(BasisMediaPlayer player, string url)
        {
            // We claim ownership (return true), so LoadUrl stops here and relies on us to report
            // failure. Route resolve errors back to the player so an unsupported/failed page URL
            // surfaces a message instead of leaving it silently at NoMedia.
            BasisYtDlpResolver.ResolveAndPlay(player, url, onError: player.ReportLoadError);
            return true;
        }
    }
}
#endif
