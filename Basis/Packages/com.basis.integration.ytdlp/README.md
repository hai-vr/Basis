# Basis yt-dlp Integration

Resolves **page URLs** — a YouTube or Twitch watch page — into the actual stream(s)
the [Basis Media Player](../com.basis.mediaplayer/README.md) can open, using yt-dlp
running in-process. An **optional bolt-on**: the player works without it; this just
adds common-site resolution.

## How it works

At load it registers a resolver on the player's `BasisMediaUrlRouter`. Any player URL
field — e.g. `BasisMediaPlayerStreaming.StreamUrl` — then steers each URL:

- A **directly-playable** URL (a transport scheme, or an HTTP URL whose path ends in
  `.mp4`/`.m4s`/`.ts`/`.m3u8`/`.mpd`) loads directly, untouched.
- A **page URL** (HTTP, no media extension) is handed to yt-dlp, which extracts the
  best playable format(s); the result is loaded into the player.

The player core has no reference to this package — the only link is the router seam —
so it can be added or removed cleanly (see *Removing it*).

## What it resolves

| Source | yt-dlp returns | Played as |
|---|---|---|
| YouTube VOD (>360p) | separate H.264 video-only + AAC audio-only | split stream, real-time paced (on-demand) |
| YouTube / Twitch live | single HLS playlist | live |
| Progressive / muxed (≤360p) | one muxed stream | delivery auto-detected |

**Codec ceiling: H.264 + AAC, ~1080p.** 4K YouTube is VP9/AV1-only, which the player
can't decode, so format selection caps the chosen video at 1080p `avc1` with `mp4a`
audio. Above ~360p YouTube serves video and audio separately, so those resolve to a
[split stream](../com.basis.mediaplayer/README.md#split-stream-separate-video--audio)
the player syncs on one clock.

## Usage

Drop a YouTube/Twitch link into the `StreamUrl` field of a `BasisMediaPlayerStreaming`
component — or call `player.LoadUrl(pageUrl)` directly, which routes the same way.
That's it; resolution and loading are automatic.

Programmatic, if you need the resolver directly:

```csharp
// Resolve a page URL and load it into the player:
BasisYtDlpResolver.ResolveAndPlay(player, "https://www.youtube.com/watch?v=…");

// Or resolve without loading (e.g. to inspect / cache the result):
BasisMediaSource source = await BasisYtDlpResolver.ResolveSourceAsync(pageUrl);
```

## Requirements

- **`com.basis.mediaplayer`** (the player) and **`com.yewnyx.ytdlp`** (the in-process
  yt-dlp native plugin — an embedded CPython runtime + yt-dlp). This package compiles
  to nothing unless both are present (asmdef define constraints
  `BASIS_MEDIAPLAYER_EXISTS` + `YTDLP_EXISTS`).
- **Windows** today — the yt-dlp native plugin is Windows-first.
- **Expect a few seconds per page-URL load.** The *first* resolution also unpacks the
  bundled Python runtime (tens of MB) to persistent storage — a one-off, noticeably slow
  step that later loads skip. But every page-URL load still spends a few seconds resolving
  in-process (network round-trips plus YouTube's JS signature challenge), not just the
  first. Resolution runs off the main thread, and nothing is surfaced on the player during
  the gap, so a consuming UI should show its own loading state.

## Removing it

Remove the package and page-URL resolution goes away: the player still plays every
direct stream URL, but YouTube/Twitch links no longer resolve — loading one reports
that the resolver is needed, rather than failing silently. Nothing else changes.

## Trust

Resolved stream URLs pass the player's host trust gate (`BasisMediaPlayerSecurity`)
like any other URL. Interactive consent for the page URL itself is a UI-layer concern
and is not driven from here.

## Known gap

A direct HTTP stream with **no file extension** can't be told apart from a page URL, so
with this package installed it is sent to yt-dlp and fails, rather than loading
directly. Give direct HTTP streams a recognised extension, or use a transport scheme
(`rtsp`/`rtmp`). See the
[player README](../com.basis.mediaplayer/README.md#page-urls-optional-resolver-package).
