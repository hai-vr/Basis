#if BASIS_MEDIAPLAYER_EXISTS && YTDLP_EXISTS
using System;
using System.Threading;
using System.Threading.Tasks;
using YtDlp;

namespace Basis.Integration.YtDlp
{
    /// <summary>
    /// Bridges the in-process yt-dlp resolver (com.yewnyx.ytdlp) to BasisMediaPlayer:
    /// turns a page URL (YouTube, Twitch, …) into a stream URL the OS-codec engine can
    /// open, then loads it. The core media player has no reference to this type, so
    /// removing this package removes the feature with nothing dangling.
    ///
    /// SCAFFOLD — the marked decisions are deferred to Documentation~/REQUIREMENTS.md
    /// (trust-gate policy, format selection, init handshake). Not wired to a consumer
    /// yet; this exists so the proposal has runnable bones to point at.
    /// </summary>
    public static class BasisYtDlpResolver
    {
        // yt-dlp's first init unpacks the bundled Python stdlib + yt-dlp (tens of MB)
        // to persistentDataPath. Share one bootstrap Task across all callers so a
        // burst of requests triggers exactly one extraction.
        private static Task initTask;
        private static readonly object initLock = new object();

        /// <summary>
        /// Resolves <paramref name="pageUrl"/> to a stream URL and loads it into
        /// <paramref name="player"/>. URLs already naming a transport scheme the player
        /// opens directly (rtsp/rtmp) are passed through unresolved. Call from the main
        /// thread — the continuation resumes there via Unity's synchronization context,
        /// so the final load is main-thread safe.
        /// </summary>
        /// <param name="onError">Invoked on the main thread if resolution fails. The
        /// core player exposes no externally-raisable error entry point, so the caller
        /// owns failure reporting.</param>
        public static async void ResolveAndPlay(
            BasisMediaPlayer player,
            string pageUrl,
            Action<Exception> onError = null,
            CancellationToken cancellationToken = default)
        {
            if (player == null) throw new ArgumentNullException(nameof(player));
            if (string.IsNullOrEmpty(pageUrl))
            {
                BasisDebug.LogWarning("BasisYtDlpResolver.ResolveAndPlay called with empty URL.", BasisDebug.LogTag.Video);
                return;
            }

            if (!NeedsResolution(pageUrl))
            {
                player.LoadUrl(pageUrl);
                return;
            }

            try
            {
                string streamUrl = await ResolveAsync(pageUrl, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                // OPEN QUESTION (REQUIREMENTS.md §Trust & consent): the resolved CDN host
                // differs from pageUrl, so whatever gate the player enforces must be
                // applied to the resolved host — or the page URL approved up front — or
                // BasisMediaPlayerSecurity.IsUrlAllowed refuses it at LoadSource.
                player.LoadUrl(streamUrl);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                BasisDebug.LogError($"yt-dlp resolution failed for '{pageUrl}': {ex.Message}", BasisDebug.LogTag.Video);
                onError?.Invoke(ex);
            }
        }

        /// <summary>
        /// Resolves a page URL to the best player-ingestible stream URL without loading
        /// it. Extraction runs on a thread-pool thread inside yt-dlp.
        /// </summary>
        public static async Task<string> ResolveAsync(string pageUrl, CancellationToken cancellationToken = default)
        {
            await EnsureInitAsync();
            VideoInfo info = await YtDlpApi.ExtractAsync(pageUrl, opts: null, cancellationToken: cancellationToken);
            string url = SelectPlayableUrl(info);
            if (string.IsNullOrEmpty(url))
                throw new YtDlpException($"yt-dlp returned no player-ingestible format for '{pageUrl}'.");
            return url;
        }

        private static Task EnsureInitAsync()
        {
            // The exact bootstrap/native-init handshake is to be confirmed against
            // dlp-native (REQUIREMENTS.md §Lifecycle); DlpBootstrap.EnsureInitAsync is
            // documented as the single entry point that must complete before extraction.
            lock (initLock)
            {
                return initTask ??= DlpBootstrap.EnsureInitAsync();
            }
        }

        // Whether a URL should go through yt-dlp at all. Transport schemes the OS-codec
        // engine opens directly never need resolution.
        private static bool NeedsResolution(string url)
        {
            // SCAFFOLD: refine per REQUIREMENTS.md §Scope. rtsp/rtmp are always direct;
            // http(s) is ambiguous (a page vs. a direct .m3u8/.mp4/.ts) and needs a
            // real policy rather than this placeholder.
            if (url.StartsWith("rtsp", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("rtmp", StringComparison.OrdinalIgnoreCase))
                return false;
            return true;
        }

        // Picks the stream URL the OS-codec engine can open from yt-dlp's output.
        private static string SelectPlayableUrl(VideoInfo info)
        {
            // SCAFFOLD: real selection is specified in REQUIREMENTS.md §Format selection.
            // Twitch and live YouTube resolve to a single HLS m3u8 the player already
            // ingests; high-res YouTube VOD is split (separate video-only + audio-only
            // representations) and depends on split-stream playback landing in the media
            // player — see the sibling proposal referenced in the doc.
            return info?.DirectUrl;
        }
    }
}
#endif
