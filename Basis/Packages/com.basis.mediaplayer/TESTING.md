# Testing the media player

How to test changes to `com.basis.mediaplayer` without fooling yourself. The README describes
what the player does; this describes how to prove it still does it after your change.

There is no automated test suite for playback — the player's job is realtime A/V against real
networks and real hardware decoders, and regressions live exactly in the parts a mock can't
reach. Testing is therefore structured manual verification: known-good streams, a repeatable
matrix, and evidence capture.

## Rule zero: prove the feed before you blame the player

Most "player bugs" found during development turn out to be feeder problems: a stalled stream,
an under-provisioned link, a server that lied about ranges. A player symptom only earns
player-side investigation once the feed is proven healthy — probe it with something that isn't
the player first:

```
# Stream shape + first decodable frame (RTSP; works for any transport ffmpeg speaks)
ffprobe -rtsp_transport tcp -show_entries stream=codec_type,codec_name,channels rtsp://host:8554/path

# Wall-time to first video frame — long GOPs make mid-stream joins slow by nature
ffmpeg -v error -rtsp_transport tcp -i <url> -map 0:v:0 -frames:v 1 -f null -
```

Things that regularly masquerade as player bugs:

- **Link capacity.** A 28 Mbps stream over a 22 Mbps path cannot play live, and no player-side
  change will fix it. Measure the actual path throughput before investigating stutter (on
  Windows, `curl.exe` under-reads badly — use a timed `urllib` read from Python instead).
- **Mid-GOP joins.** Joining a live stream between keyframes delivers audio immediately and no
  video until the next IDR. With a 10-second GOP that is a 10-second "video hang" that is
  entirely the source's fault. Know your test stream's GOP.
- **Single-client feeders.** `ffmpeg -listen 1` serves one client, then lingers in a stale
  state that trickles junk. If a second connection behaves strangely, restart the feeder.
- **Wrong delivery detection.** `Delivery = Auto` probes `Range: bytes=0-` and needs a real
  `206 Partial Content` to detect on-demand content. `python -m http.server` and
  `ffmpeg -listen` answer `200` and get treated as **live**. Serve VOD files from nginx (or
  anything with real range support).

The test-stream stack in [`tools/media-test-streams/`](../../../tools/media-test-streams/)
ships a `preflight.py` that runs these probes across all of its lanes in ~30 seconds.

## Where test streams may live (the security gates)

`BasisMediaPlayerSecurity` validates every URL before the engine opens it
(`Runtime/Core/BasisMediaPlayerSecurity.cs`). The rules shape where your streams must run:

| Rule | Effect on testing |
| --- | --- |
| Loopback allowed **in the Editor only** | `localhost` streams work for fast in-editor iteration; the same URL is refused in a build |
| RFC1918 / CGNAT / link-local always blocked | LAN servers (`192.168.*`, `10.*`, …) never work, Editor included — don't bother |
| Hostnames are DNS-validated, fail-closed | A name that resolves to a private address (or doesn't resolve) is refused |
| Scheme allowlist | `http`, `https`, `rtsp`, `rtspt`, `rtmp`, `rtmps`, `rist` — anything else (incl. `file://`) is refused |

Practical consequences:

- **Editor iteration:** run the test stack locally with Docker and use `localhost` URLs.
- **Builds, Quest, multi-client tests:** the stream must come from a **public host with real
  DNS**. Any cheap VPS running the same stack works.
- **Quest/Android:** the OS cleartext policy blocks plain `http://` on the JNI fetch path —
  HTTP-TS and HLS lanes need `https://` with a certificate chain the device actually trusts
  (serve the full chain; standalone headsets are missing more roots than desktop browsers).
  `rtsp://` is unaffected.
- The separate world-content trust allowlist (`BasisDefaultTrustedUrls`, https-only) gates the
  sandboxed `VideoPlayer` shim path, not this package — but streams hosted on already-trusted
  domains spare testers a consent prompt when worlds use the same URL.

## Public always-on endpoints (zero setup)

These cover the common lanes without standing up anything. They are third-party services —
fine for interactive test sessions, not for soak loops.

| URL | Exercises | Notes |
| --- | --- | --- |
| `rtsp://stream.vrcdn.live/live/vrcdn` | RTSP live, H.264 720p + AAC 2.0 @ 48 kHz | VRCDN's own 24/7 channel; the primary PC low-latency lane; host is on the default trust list |
| `https://stream.vrcdn.live/live/vrcdn.live.ts` | MPEG-TS over HTTPS, live | Same channel, the standalone-friendly lane (https, so Quest-safe) |
| `https://download.blender.org/peach/bigbuckbunny_movies/BigBuckBunny_640x360.m4v` | Progressive MP4 VOD, range/`206` | Official Blender hosting of the full 10-minute film, H.264 + AAC (`.m4v` is recognised as an MP4 extension). Good for seek/pause and delivery auto-detect testing |
| `https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8` | HLS VOD, multi-variant master | Exercises the panel's bitrate dropdown |
| [Fraunhofer AAC multichannel page](https://www2.iis.fraunhofer.de/AAC/multichannel.html) | AAC 5.1/7.1 VOD fixtures | Includes adversarial layouts: PCE-signalled 7.1 must fail **gracefully** on Windows (muted audio or a clean error — never a crash) |

> Live endpoints join mid-GOP like any live stream — audio-before-video on join is expected
> behaviour, not a regression, unless the gap exceeds the stream's GOP length.

**Page URLs (YouTube, Twitch, …) are deliberately absent from this guide.** They only work
through an optional resolver integration, so the base package can't assume they're testable.
Everything in this document uses direct stream URLs; resolver-dependent testing lives in the
integration package that provides it — e.g.
[`com.basis.integration.ytdlp/TESTING.md`](../com.basis.integration.ytdlp/TESTING.md).
The same split applies to any future integration: endpoints that need an integration package
to function are tested in that package's own TESTING.md.

## What the public internet can't give you: the test-stream stack

Some lanes have no reliable public endpoint. [`tools/media-test-streams/`](../../../tools/media-test-streams/)
is a Docker Compose stack that provides them, runnable **locally for Editor work** (loopback
is allowed in-editor) and **on any public VPS** for build/Quest/multi-client work:

- RTSP/RTSPT under your control (including an adversarial long-GOP path for join testing)
- HTTP-TS live feeds
- nginx VOD with real range support
- CEA-608 caption-bearing TS (generated fixture — no public stream carries captions reliably)
- LPCM 5.1/7.1 over M2TS (the full-multichannel lane; AAC on Windows caps at 5.1)
- RIST sender, plain and AES-encrypted (needs the opt-in `-DBASIS_WITH_RIST=ON` plugin build)
- Split-stream video+audio pairs

Its README covers deployment, asset preparation (bring your own content — real footage with
visible lip-sync moments beats synthetic patterns for A/V sync work), and per-lane URLs.

## The regression matrix

Run the rows your change plausibly touches; run everything before a release-bound merge.
"Verify" always includes: plays within a sane time, A/V stays in sync, no console errors
(`BasisDebug` tag `Video`), clean stop/unload.

### Transports

| Lane | Source | Verify additionally |
| --- | --- | --- |
| RTSP live | VRCDN or stack `rtsp://<host>:8554/main` | Join latency ≈ GOP-bound; pause/resume recovers cleanly |
| RTSP adversarial join | stack `rtsp://<host>:8554/slowjoin` | Audio leads video by up to the GOP length on join, then locks — no permanent desync |
| HTTP-TS live | VRCDN `.live.ts` or stack | Same checks over plain TS; on Quest use the https lane |
| HLS VOD | Mux master or stack packaging | Variant switch via panel bitrate dropdown mid-play |
| Progressive/fMP4 MP4 | Big Buck Bunny | `Delivery=Auto` detects OnDemand (needs the 206); seek slider works |
| RTMP | stack `rtmp://<host>:1935/main` | Minimal client — plain `rtmp://` pull only |
| RIST plain + AES | stack (RIST profile) | Requires RIST-enabled plugin build; loss recovery under induced packet loss |
| WAV audio-only | stack VOD | 16/24-bit, up to 8 ch; no video track is not an error |
| Split-stream | stack pair | Windows-only today; `AudioUri` lane syncs to video |

### Content and codecs

| Fixture | Verify |
| --- | --- |
| H.264 + AAC stereo | The baseline — everything else assumes this passes |
| H.265/HEVC | Windows needs the system HEVC codec present; absence degrades cleanly |
| AAC 5.1 | Windows MF decodes ≤ 5.1; correct channel mapping (use content with known channel placement, judge by ear per output speaker) |
| LPCM 7.1 M2TS | All 8 lanes audible and correctly placed — the only full-7.1 path on Windows |
| PCE-signalled / >6-ch AAC | **Graceful refusal** on Windows (mute or clean error, never a crash) |
| CEA-608 captions | Stack caption fixture: cues appear on time, accented characters correct, clear-cue clears, CC toggle + opacity sliders live-apply |
| 44.1 kHz audio | Resamples cleanly to the DSP rate (dominant path is 48 kHz — don't let 44.1k rot) |

### Platforms and backends

| Target | Notes |
| --- | --- |
| Windows D3D11 | Default editor/player path |
| Windows D3D12 | Launch with `-force-d3d12`; shared-handle texture path is separate code — video must appear, no `dxgi-fmt` errors in the log |
| Android/Quest | Vulkan path, `AMediaCodec`; https for TS/HLS lanes; check `adb logcat` for codec errors; AAC 5.1 arrives in WAVE order |
| Desktop ↔ VR swap | Toggle mode mid-playback — the external texture must survive the graphics-device swap |

### Behaviour checklists

**Playback lifecycle** — load → play → pause → resume → stop → replay same URL → load a
different URL mid-play. No stale frames, no orphaned audio, position resets correctly.

**Seek (VOD)** — slider to arbitrary positions; rapid successive seeks (input is debounced);
seek-then-pause shows the sought frame.

> On-demand multiplayer sync is **start-together, not catch-up**: the native backend exposes
> no absolute seek, so a client that falls behind stays behind until the next shared (re)load.
> Late-joiner-starts-at-zero on VOD is a known limit, not a regression.

**Networking** — two clients minimum: owner loads URL → both play; non-owner requests control
→ ownership transfers; owner pause/stop propagates; late joiner receives current state; each
client resolves the URL independently (per-client CDN/bitrate differences are fine, state
divergence is not).

**Panel UI** ("Media Players" panel, `Runtime/UI/BasisMediaPlayerPanelProvider.cs`) — URL
load, transport buttons, seek slider (VOD only), volume, bitrate dropdown (HLS multi-variant),
audio-track dropdown (multi-audio content), captions toggle + opacity sliders. Controls that
don't apply to the loaded media should be absent or inert, not broken.

**Security gates** — negative tests matter: `http://192.168.1.10/x.ts` must refuse with a
clear reason on every platform; `localhost` must refuse **in a build** (and work in the
Editor); `file:///` must refuse. A regression that *opens* a gate is a security bug —
flag it as such, not as a playback bug.

**A/V sync judgement** — use real footage with visible speech; synthetic patterns hide sync
drift. Watch a full minute at the live edge, not five seconds. For anything subtle, capture
diagnostics (below) rather than trusting perception.

**Orientation** — a horizontal mirror is invisible on symmetric content. Verify left/right
with on-screen text or a logo, every time video-path code changes.

## Diagnostics and evidence

- **`BasisMediaPlayerDiagnostics`** (`Runtime/BasisMediaPlayerDiagnostics.cs`): add the
  component next to a `BasisMediaPlayer`, enable `AutoStart` (or call `StartLogging()`), and
  it samples ~50 snapshots/s to `Application.persistentDataPath/BasisMediaPlayerDiag.csv`
  (Windows: `%USERPROFILE%\AppData\LocalLow\<company>\<product>\`). Useful signals:
  `eng_ttff_ms` (time to first frame), `engine_pos_us` step distribution (late presents show
  as double-steps coinciding with wall-clock gaps), `eng_lag_ms`/`eng_buf_ms` (clock vs
  buffer health), `audio_queue_depth`/`eng_audio_trims` (audio starvation/overrun),
  `cpu_*_drops/skips` (CPU-path frame accounting). Filter rows to `engine_state == Playing`
  before drawing conclusions.
- **Debug window**: `Basis → Debug → Media Player Debug` shows live engine state per player.
- **Feedless harness**: `BasisSyntheticTestSource` (`Runtime/Sources/`) drives the player
  without any network feed — isolates render-path changes from transport noise.
- **Logs**: the package logs exclusively under the `Video` tag via `BasisDebug`. On Android,
  `adb logcat -s Unity` plus the codec tags carry the native side.

## Reporting a regression

A report that can be acted on contains:

1. The exact URL (or the stack lane + asset recipe) — full URL, not a fragment
2. Platform, graphics API, Editor-or-build, headset if relevant
3. What was expected, what happened, and how reliably it reproduces
4. Console output around the failure (the `Video`-tagged lines) and, for timing/sync issues,
   the diagnostics CSV covering the incident
5. Whether the preflight/ffprobe of the same URL was green at the time

## Acknowledgements

The always-on live lanes above are [VRCDN](https://vrcdn.live/)'s own public channel, listed
here with their permission — thanks to the VRCDN team for keeping a reliable 24/7 reference
stream running and for letting this guide point testers at it. Be a good guest: use it for
interactive test sessions, not automated soak loops, and stand up the self-hosted stack for
anything sustained.

## Native plugin changes

Any change under `Native~/` needs the rebuilt binaries verified on **both** platforms it
ships for (Windows x64 DLL, Android arm64 `.so`) — the shared C core means a protocol fix on
one platform can regress the other. See the README's "Building the native plugin" section.
Note the Windows DLL cannot be replaced while any Unity instance holds it loaded — close
Unity, swap, reopen.
