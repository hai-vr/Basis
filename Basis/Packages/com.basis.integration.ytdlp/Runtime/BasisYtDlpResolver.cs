#if BASIS_MEDIAPLAYER_EXISTS && YTDLP_EXISTS
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using YtDlp;

namespace Basis.Integration.YtDlp
{
    /// <summary>
    /// Bridges the in-process yt-dlp resolver (com.yewnyx.ytdlp) to BasisMediaPlayer:
    /// turns a page URL (YouTube, Twitch, …) into the stream(s) the OS-codec engine
    /// can open, then loads them. The core media player has no reference to this type,
    /// so removing this package removes the feature with nothing dangling.
    ///
    /// Sources that deliver high-res video and audio as separate streams (YouTube VOD
    /// above ~360p: H.264 video-only + AAC audio-only) resolve to a split
    /// BasisMediaSource (Uri + AudioUri); single muxed sources (Twitch / live YouTube
    /// HLS, progressive ~360p) resolve to a single Uri.
    /// </summary>
    public static class BasisYtDlpResolver
    {
        // yt-dlp's first init unpacks the bundled Python stdlib + yt-dlp (tens of MB)
        // to persistentDataPath. Share one bootstrap Task across all callers so a
        // burst of requests triggers exactly one extraction.
        private static Task initTask;
        private static readonly object initLock = new object();

        /// <summary>
        /// Resolves <paramref name="pageUrl"/> to its stream(s) and loads them into
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

            // Capture the player's load generation before the async resolve. If another
            // LoadUrl bumps it while yt-dlp runs, this resolve is stale — drop it rather than
            // overwrite the newer load (load A then B: A must not win if it finishes last).
            int loadGen = player.LoadGeneration;

            try
            {
                BasisMediaSource source = await ResolveSourceAsync(pageUrl, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (loadGen != player.LoadGeneration) return; // superseded by a newer load
                // OPEN QUESTION (REQUIREMENTS.md §Trust & consent): the resolved CDN host
                // (googlevideo, Twitch edge) differs from pageUrl, so LoadSource runs both
                // Uri and AudioUri through BasisMediaPlayerSecurity.IsUrlAllowed — public
                // https hosts pass; a host allowlist or page-URL-approval policy is the
                // deferred decision.
                // Hand back the captured generation so the player matches this to the
                // metadata seed its LoadUrl planted, rather than one a racing load left.
                player.LoadResolvedSource(source, loadGen);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Log the exception type, not ex.Message — yt-dlp/extractor messages embed the
                // raw page URL (and its tokens), which would defeat the redaction above.
                BasisDebug.LogError($"yt-dlp resolution failed for '{BasisMediaUrlRouter.Redact(pageUrl)}' ({ex.GetType().Name}).", BasisDebug.LogTag.Video);
                // Only report if this resolve still owns the player — a newer LoadUrl since
                // capture supersedes us, and its outcome must not be clobbered by our failure.
                if (loadGen == player.LoadGeneration) onError?.Invoke(ex);
            }
        }

        /// <summary>
        /// Resolves a page URL to a BasisMediaSource — a split video+audio pair when the
        /// source offers separate avc1/mp4a streams, otherwise a single muxed Uri —
        /// without loading it. Extraction runs on a thread-pool thread inside yt-dlp.
        /// </summary>
        public static async Task<BasisMediaSource> ResolveSourceAsync(string pageUrl, CancellationToken cancellationToken = default)
        {
            await EnsureInitAsync();
            VideoInfo info = await YtDlpApi.ExtractAsync(pageUrl, opts: null, cancellationToken: cancellationToken);
            BasisMediaSource source = SelectSource(info);
            if (source == null || string.IsNullOrEmpty(source.Uri))
                throw new YtDlpException($"yt-dlp returned no player-ingestible format for '{BasisMediaUrlRouter.Redact(pageUrl)}'.");
            // Carry display metadata on the source: the player keys its metadata on
            // the page URL (matching what networking syncs, not the per-client CDN
            // endpoint) and shows the real title. Everything here comes from the
            // extraction that just ran — no extra fetch.
            source.Metadata = new BasisMediaMetadata
            {
                SourceUrl = pageUrl,
                Title = info.Title,
                Uploader = info.Uploader,
                ThumbnailUrl = info.Thumbnail,
                Duration = info.Duration.HasValue && info.Duration.Value > 0
                    ? TimeSpan.FromSeconds(info.Duration.Value)
                    : (TimeSpan?)null,
                Provider = "ytdlp",
            };
            return source;
        }

        /// <summary>
        /// Resolution-only convenience returning just the primary stream URL (the video
        /// leg of a split source, or the single muxed URL). Callers needing the audio
        /// leg use <see cref="ResolveSourceAsync"/>.
        /// </summary>
        public static async Task<string> ResolveAsync(string pageUrl, CancellationToken cancellationToken = default)
        {
            BasisMediaSource source = await ResolveSourceAsync(pageUrl, cancellationToken);
            return source.Uri;
        }

        private static Task EnsureInitAsync()
        {
            lock (initLock)
            {
                return initTask ??= DlpBootstrap.EnsureInitAsync();
            }
        }

        // Whether a URL should be resolved by yt-dlp, or is already something the player
        // opens directly. The directly-playable classification is owned by the player
        // (BasisMediaUrlRouter.IsDirectlyPlayable) so there's one source of truth; a page
        // URL (YouTube, Twitch, …) is anything that isn't directly playable. This only
        // steers — an unsupported page URL simply fails to resolve and surfaces via
        // ResolveAndPlay's onError.
        internal static bool NeedsResolution(string url)
            => !string.IsNullOrEmpty(url) && !BasisMediaUrlRouter.IsDirectlyPlayable(url);

        // Builds the BasisMediaSource from yt-dlp's format list. Prefers a split pair
        // (avc1 video-only + mp4a audio-only) when both exist — that's how YouTube
        // serves anything above ~360p, and the player presents the two in sync via
        // AudioUri. Falls back to a single muxed/HLS stream (Twitch, live YouTube,
        // progressive ~360p) otherwise. Assumes a split-capable player; emitting the
        // pair only against one is a version-define refinement.
        private static BasisMediaSource SelectSource(VideoInfo info)
        {
            if (info == null) return null;

            Format video = BestVideoOnly(info.Formats);
            Format audio = BestAudioOnly(info.Formats);
            if (video != null && audio != null)
                // A split avc1+mp4a pair is adaptive VOD (YouTube serves these only
                // above ~360p), delivered faster than real time — force on-demand pacing.
                return new BasisMediaSource { Uri = video.Url, AudioUri = audio.Url, Delivery = BasisMediaDelivery.OnDemand };

            Format muxed = BestMuxed(info.Formats);
            if (muxed != null) return new BasisMediaSource { Uri = muxed.Url, Delivery = DeliveryFor(info) };

            // Last resort: yt-dlp's top-level URL — but only if the player can open it
            // directly. An unvalidated DirectUrl can be an unsupported manifest/codec that
            // would bypass the avc1/mp4a filtering above, so reject it and let
            // ResolveSourceAsync fail loudly ("no player-ingestible format") instead.
            if (!string.IsNullOrEmpty(info.DirectUrl) && BasisMediaUrlRouter.IsDirectlyPlayable(info.DirectUrl))
                return new BasisMediaSource { Uri = info.DirectUrl, Delivery = DeliveryFor(info) };

            return null;
        }

        // Maps yt-dlp's live-status metadata onto the engine's delivery hint, so the
        // live-vs-VOD clock is chosen at open instead of sniffed from the byte stream.
        // Absent/unknown status falls back to Auto (the engine detects a seekable,
        // finite body as VOD and an open-ended stream as live). The split avc1+mp4a
        // path doesn't consult this — that pairing is only ever adaptive VOD.
        private static BasisMediaDelivery DeliveryFor(VideoInfo info)
        {
            if (info.IsLive == true) return BasisMediaDelivery.Live;
            switch (info.LiveStatus)
            {
                case "is_live":
                case "is_upcoming": return BasisMediaDelivery.Live;
                case "was_live":
                case "post_live":   // ended broadcast, VOD still processing — watched as a recording
                case "not_live":    return BasisMediaDelivery.OnDemand;
                default:            return BasisMediaDelivery.Auto;
            }
        }

        // H.264 video-only, no higher than 1080p (avc1 is the player's ceiling — no
        // VP9/AV1 decode), best height then bitrate, direct byte URL (not a manifest).
        private static Format BestVideoOnly(List<Format> formats)
        {
            if (formats == null) return null;
            Format best = null;
            for (int i = 0; i < formats.Count; i++)
            {
                Format f = formats[i];
                if (f == null || string.IsNullOrEmpty(f.Url)) continue;
                if (!HasCodec(f.VCodec) || HasCodec(f.ACodec)) continue;         // video-only
                if (!StartsWithCI(f.VCodec, "avc1")) continue;
                if ((f.Height ?? 0) > 1080) continue;
                if (!IsDirectByteStream(f.Protocol)) continue;
                if (best == null || VideoBetter(f, best)) best = f;
            }
            return best;
        }

        // AAC audio-only, highest bitrate, direct byte URL.
        private static Format BestAudioOnly(List<Format> formats)
        {
            if (formats == null) return null;
            Format best = null;
            for (int i = 0; i < formats.Count; i++)
            {
                Format f = formats[i];
                if (f == null || string.IsNullOrEmpty(f.Url)) continue;
                if (!HasCodec(f.ACodec) || HasCodec(f.VCodec)) continue;         // audio-only
                if (!StartsWithCI(f.ACodec, "mp4a")) continue;
                if (!IsDirectByteStream(f.Protocol)) continue;
                if (best == null || Bitrate(f.AudioBitrate, f) > Bitrate(best.AudioBitrate, best)) best = f;
            }
            return best;
        }

        // Single stream carrying both tracks: progressive avc1+mp4a (e.g. itag 18) or an
        // HLS variant. HLS manifests are allowed here — the player ingests them as one
        // stream — so this path covers Twitch / live YouTube.
        private static Format BestMuxed(List<Format> formats)
        {
            if (formats == null) return null;
            Format best = null;
            for (int i = 0; i < formats.Count; i++)
            {
                Format f = formats[i];
                if (f == null || string.IsNullOrEmpty(f.Url)) continue;
                if (!HasCodec(f.VCodec) || !HasCodec(f.ACodec)) continue;        // muxed
                if (!StartsWithCI(f.VCodec, "avc1")) continue;                   // H.264 only
                if (!StartsWithCI(f.ACodec, "mp4a")) continue;                   // AAC only
                if (!IsIngestibleProtocol(f.Protocol)) continue;                 // byte stream or HLS
                if ((f.Height ?? 0) > 1080) continue;
                if (best == null || VideoBetter(f, best)) best = f;
            }
            return best;
        }

        private static bool VideoBetter(Format a, Format b)
        {
            int ha = a.Height ?? 0, hb = b.Height ?? 0;
            if (ha != hb) return ha > hb;
            return Bitrate(a.VideoBitrate, a) > Bitrate(b.VideoBitrate, b);
        }

        private static double Bitrate(double? primary, Format f) => primary ?? f.TotalBitrate ?? 0;

        private static bool HasCodec(string codec) => !string.IsNullOrEmpty(codec) && codec != "none";

        private static bool StartsWithCI(string s, string prefix)
            => s != null && s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        // Direct byte/range URL the engine fetches over WinHTTP. Skip DASH-manifest
        // protocols the demuxers don't consume as a single byte stream; HLS is handled
        // separately in BestMuxed (the player has its own HLS source).
        private static bool IsDirectByteStream(string protocol)
            => string.IsNullOrEmpty(protocol) || protocol == "https" || protocol == "http";

        // Protocols the player can ingest as a single stream: a direct byte stream
        // (http/https) or HLS (the player has its own HLS source). Excludes DASH /
        // fragment manifests the demuxers can't open. Used for the muxed path, which
        // covers both progressive (http) and HLS (Twitch / live) sources.
        private static bool IsIngestibleProtocol(string protocol)
            => IsDirectByteStream(protocol) || protocol == "m3u8" || protocol == "m3u8_native";
    }
}
#endif
