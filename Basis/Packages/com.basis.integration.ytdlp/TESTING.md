# Testing the yt-dlp integration

How to verify page-URL resolution (YouTube, Twitch, and friends) after changing this package.
The base player's guide —
[`com.basis.mediaplayer/TESTING.md`](../com.basis.mediaplayer/TESTING.md) — covers everything
downstream of resolution and deliberately uses **direct stream URLs only**; page URLs belong
here, because they only work when this integration (and its `com.yewnyx.ytdlp` dependency)
is installed.

## Prerequisites

- `com.basis.mediaplayer` and `com.yewnyx.ytdlp` present — this package compiles to nothing
  without both (asmdef define constraints), so first confirm it's actually active: loading a
  page URL without it reports that a resolver is needed.
- **Windows** — the yt-dlp native plugin is Windows-first.
- The base player passing its own matrix. If direct streams are broken, nothing here will
  tell you anything about the resolver.

## Rule zero: separate resolver bugs from player bugs

Resolution and playback are different failure domains. Before blaming either:

```csharp
// Inspect what resolution actually produced, without playing it:
BasisMediaSource source = await BasisYtDlpResolver.ResolveSourceAsync(pageUrl);
```

If the resolved `Url`/`AudioUri` look sane, load them **directly** in the player — if that
also fails, it's a player bug and the base guide applies. If resolution itself fails,
remember the other moving part: **the sites change constantly.** YouTube rotates signature
challenges; a resolution failure on unchanged code is usually an aged yt-dlp, not your
regression. Confirm the yt-dlp runtime is current before filing anything.

## What to test

Pick stable, public content you have the right to view — long-standing official uploads
(e.g. the Blender Foundation films) beat trending links that vanish. Twitch: any live channel
from the front page, plus a recent VOD from the same channel.

| Scenario | Expected |
| --- | --- |
| YouTube VOD, >360p | Resolves to **split stream** (H.264 video-only + AAC audio-only), plays paced as on-demand, A/V locked |
| YouTube VOD, ≤360p (or format-forced) | Single muxed stream, delivery auto-detected |
| YouTube live | Single HLS playlist, plays as live |
| Twitch live | HLS live; join near the live edge |
| Twitch VOD | HLS VOD |
| Format cap | Chosen video is `avc1` ≤1080p with `mp4a` audio — never VP9/AV1, even on 4K uploads |
| Metadata | Title / uploader / thumbnail appear on the player after resolve |
| First-ever resolution | One-off multi-second pause while the bundled Python runtime unpacks — expected, not a hang; later loads skip it |
| Every resolution | A few seconds of in-process resolving is normal; the player shows nothing during the gap by design |
| Direct URLs with this package installed | `.mp4`/`.ts`/`.m3u8`/transport-scheme URLs load **untouched** — no resolver round-trip |
| Extensionless direct HTTP stream | Known gap: routed to yt-dlp and fails; documented, not a regression |
| Invalid / dead page URL | Clean failure surfaced to the player — no crash, no silent hang |
| Package removed | Page URLs report "resolver needed"; direct streams unaffected |

## Security and networking

- Resolved stream URLs pass `BasisMediaPlayerSecurity` like any other URL — a resolver change
  must never become a way around the host gate. Negative-test with a page URL crafted to
  resolve somewhere refused (or verify the gate log lines fire on the resolved hosts).
- In multiplayer, the **page URL** syncs and each client resolves independently. Two clients
  on the same video may legitimately hold different CDN URLs; state (play/pause/position
  intent) must still agree. Test with two clients minimum.
- Resolution runs off the main thread — the Editor should stay responsive during the resolve
  gap; a frozen editor during resolution is a regression.

## Reporting

Base-guide report contents apply, plus: the page URL, the yt-dlp runtime version, and —
for resolution failures — whether `ResolveSourceAsync` fails too or only playback of the
resolved result.
