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
        public int Priority => 0;

        public bool CanResolve(string url) => BasisYtDlpResolver.NeedsResolution(url);

        public bool TryResolve(BasisMediaPlayer player, string url)
        {
            BasisYtDlpResolver.ResolveAndPlay(player, url);
            return true;
        }
    }
}
#endif
