# Testing the media player

How to test changes to `com.basis.mediaplayer` without fooling yourself. The README describes
what the player does; this describes how to prove it still does it after your change.

There is no automated test suite for playback — the player's job is realtime A/V against real
networks and real hardware decoders, and regressions live exactly in the parts a mock can't
reach. Testing is therefore structured manual verification: known-good streams, a repeatable
matrix, and evidence capture.

Playback is only half of it. The native plugin parses attacker-controlled container and
protocol bytes in-process, so a change under `Native~/` carries a security exposure that a
playback matrix does not cover. If you are touching the C core, read
[Native plugin changes: the security boundary](#native-plugin-changes-the-security-boundary)
first — it sets the threat model and the malformed-input and fuzz testing that a parser change
needs, over and above "a good file still plays."

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
| RTSP live | VRCDN or stack `rtsp://<host>:8554/main` | Join latency ≈ GOP-bound; pause/resume recovers cleanly. `rtsp://` negotiates UDP transport first and falls back to TCP-interleaved; the Console logs the settled choice once per load (`[NativeMedia] transport: RTSP over UDP`), and it's queryable via `BasisMediaPlayer.CurrentTransport` |
| RTSP adversarial join | stack `rtsp://<host>:8554/slowjoin` | Audio leads video by up to the GOP length on join, then locks — no permanent desync |
| RTSP refusal fallback | stack with `rtspTransports: [tcp]` in `mediamtx.yml` | UDP SETUP is refused (461); playback is indistinguishable from today, no error surfaced; Console logs `RTSP over TCP (UDP unavailable)` |
| RTSP timer fallback | stack with the host's `8000-8001/udp` blocked (or any network that silently eats UDP) | First join stalls ~3 s, then restarts transparently over TCP with the same fallback log line; a reload of the same host skips the probe and goes straight to TCP |
| RTSP forced TCP | `rtspt://` form of any RTSP URL | No UDP attempt at all (no UDP `SETUP` in the server log); Console logs `RTSP over TCP` |
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
| VP9 in WebM (`https://mr.town/vod/tos_vp9.webm`) | Plays on Windows (Store "VP9 Video Extensions" + a GPU with hardware VP9 — the probe gates both) and Quest (hardware everywhere). The fixture is a two-pass encode carrying superframes, so whole-superframe feeding is exercised by playing it |
| VP9 in MP4 (`https://mr.town/vod/tos_vp9.mp4`) | The `vp09` sample-entry lane; same decode path as WebM |
| WebM Cues placements | `tos_vp9.webm` (Cues at front — parsed inline) and `tos_vp9_cuesend.webm` (Cues trailing — ranged-fetched at open via SeekHead) both report a duration and seek; `tos_vp9_nocues.webm` (streamed mux, no Cues) plays forward-only with **no seek bar / duration 0** — a duration on that file is a bug (duration > 0 must always mean seek works) |
| Unsupported video codec | `https://mr.town/vod/tos_vp8.webm` and `https://mr.town/vod/tos_mp4v.mp4` refuse with a clear "video codec 'x' is not supported" error naming the codec — never silent audio under a black screen |
| VP9 software-fallback guard | On a GPU without hardware VP9, a direct VP9 URL must produce the "video decoder produced software frames" error, not a black screen (the Store MFT silently falls back to CPU; only reproducible on a no-hw box or with the extension's fallback forced) |
| AAC 5.1 | Windows MF decodes ≤ 5.1; correct channel mapping (use content with known channel placement, judge by ear per output speaker) |
| AAC 5.1 in a progressive MP4 (Android) | Decodes to discrete 5.1, not silence. The esds can carry an inert SBR sync extension the Android decoder otherwise rejects (`aacDecoder 0x1001` in logcat); fixture `https://mr.town/vod/scope.mp4` |
| LPCM 7.1 M2TS | All 8 lanes audible and correctly placed — the only full-7.1 path on Windows |
| PCE-signalled / >6-ch AAC | **Graceful refusal** on Windows (mute or clean error, never a crash) |
| Trailing-moov progressive MP4 | Non-faststart file (`ffmpeg -i in.mp4 -c copy out.mp4` leaves `moov` after `mdat`): on a range/`206` server it plays with seek + duration; over a one-way stream (no ranges) it refuses cleanly with a faststart-remux hint |
| CEA-608 captions | Stack caption fixture: cues appear on time, accented characters correct, clear-cue clears, CC toggle + opacity sliders live-apply |
| 44.1 kHz audio | Resamples cleanly to the DSP rate (dominant path is 48 kHz — don't let 44.1k rot) |
| Non-16-aligned coded height | No pad strip on the video edge (a thin top strip on Windows, a grey bottom strip on Android) and the RenderTexture matches the display aspect. 720p and other 16-aligned heights are clean, so test a padded height specifically — 1080p (→1088) on Windows, 640×360 (→368) on Android |

### Platforms and backends

| Target | Notes |
| --- | --- |
| Windows D3D11 | Default editor/player path |
| Windows D3D12 | Launch with `-force-d3d12`; shared-handle texture path is separate code — video must appear, no `dxgi-fmt` errors in the log |
| Android/Quest | Vulkan path, `AMediaCodec`; https for TS/HLS lanes; check `adb logcat` for codec errors; AAC 5.1 arrives in WAVE order. 5.1 AAC in a progressive MP4 decodes discretely (see the codec row); the coded-height pad is cropped off the present (grey bottom strip) |
| Desktop ↔ VR swap | Toggle mode mid-playback — the external texture must survive the graphics-device swap |

### Behaviour checklists

**Playback lifecycle** — load → play → pause → resume → stop → replay same URL → load a
different URL mid-play. No stale frames, no orphaned audio, position resets correctly.

**Seek (VOD)** — slider to arbitrary positions; rapid successive seeks (input is debounced);
seek-then-pause shows the sought frame. The byte-source ranged refetch that backs a seek now
runs on **Android** too (JNI `HttpsURLConnection`), not just Windows — run the same slider
checks on a Quest against a range/`206` VOD host (`https://`), watching `adb logcat` for a clean
reposition (no decoder error, playback resumes at the target).

**Seek (HLS-TS VOD)** — on the Mux master (`https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8`),
seek both directions and confirm playback resumes **paced at 1x from the target**: a forward
seek must not freeze for the jump distance, and a backward seek must not fast-forward through
the intervening segments back to the pre-seek position. The segment producer repositions and
the demux leg re-anchors delivery pacing at the flushed boundary, so a mis-anchored pace clock
(stall forward / flood backward) is the failure to watch for. Shared clock, so check both the
Editor (Windows) and Quest.

**Seek (integrated fMP4)** — on a self-contained fragmented MP4 (moof/mdat fragments indexed by a
`sidx`) served from a range/`206` host — e.g.
`https://zipline.space.superneko.net/raw/bbb_sunflower_1080p_30fps_normal_idfmp4.mp4` — confirm
`Delivery=Auto` detects OnDemand and seeks in both directions reposition cleanly and resume at the
target with no decoder error. This is the `sidx`-driven byte-source reseek; it shares the
byte-source seek path with progressive/trailing-moov MP4, so a regression here usually surfaces on
those too. Distinct from fMP4 carried *in HLS*, which isn't seekable — a mid-fragment ring flush
can't resynchronise the box parser. Check the Editor (Windows) and Quest.

> On-demand multiplayer sync **drift-corrects by seeking**: the owner broadcasts its playhead
> and a client that drifts past `DriftSeekThresholdSeconds` seeks to catch up (set 0 to
> disable). Catch-up needs a seekable source — TS-segment HLS VOD, progressive/trailing-moov
> MP4, and integrated fMP4 qualify; a live source can't seek, so those clients converge
> independently to the live edge rather than using playhead-seek correction.

**Seek (WebM Cues)** — on `https://mr.town/vod/tos_vp9.webm` and the trailing-Cues variant,
seek both directions: playback lands at or just before the target (cue/cluster granularity, on
a keyframe) and resumes paced at 1x — the same stall-forward / flood-backward failure shapes as
the HLS row apply. Seek near the very end of the file as well (EOS race). The cueless variant
must show no seek bar at all. Check the Editor (Windows) and Quest.

**Networking** — two clients minimum: owner loads URL → both play; non-owner requests control
→ ownership transfers; owner pause/stop propagates; late joiner receives current state; each
client resolves the URL independently (per-client CDN/bitrate differences are fine, state
divergence is not).

**Panel UI** ("Media Players" panel, `Runtime/UI/BasisMediaPlayerPanelProvider.cs`) — URL
load, transport buttons, seek slider (VOD only), volume, bitrate dropdown (HLS multi-variant),
audio-track dropdown (multi-audio content), captions toggle + opacity sliders, subtitles
dropdown (only when the loaded media offers sidecar subtitle tracks — resolver-supplied, so
the scenarios live in the resolver package's guide; with plain stream URLs the dropdown must
be entirely absent). Controls that don't apply to the loaded media should be absent or inert,
not broken.

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

## Native plugin changes: the security boundary

`Native~/` is where the player is most exposed, and a change there is not verified the same
way a C# change is. The C core parses container and protocol bytes **by hand** — MP4 box
walking (`esds`/`avcc`/`hvcC`), MPEG-TS section parsing, RTSP/RTMP, WebM — and it does so
**in-process, with no sandbox**. The bytes are attacker-controlled: a media URL is opened
from world content and, in multiplayer, broadcast by a peer so that every other client parses
the same hostile stream at once. A parser that reads past a buffer, trusts a length field it
never bounds-checked, or dereferences a pointer it never validated is therefore reachable
remotely, on every client simultaneously.

Two outcomes to test against, in priority order:

- **Denial of service** — the common, proven case. A malformed stream crashes or hangs the
  decode thread and takes the process (editor or client) down with it. This has happened from
  an ordinary `ffmpeg`-produced file: an HEVC elementary stream that reaches the decoder with
  no frame size made the Windows Store HEVC MFT dereference a null pointer on its own worker
  thread. The parser must refuse a sizeless or otherwise under-specified track **before** it
  hands bytes to the decoder, not let it fail somewhere downstream.
- **Memory corruption** — the worst case, and the reason this is a security boundary and not
  just a stability one. Hand-rolled parsers with untrusted lengths are exactly where
  out-of-bounds reads and writes live. Treat *any* out-of-bounds access as a security bug,
  including a read that "only" crashes — the same missing bound is often writable with a
  different input.

So "a good file still plays" does not verify a parser change. Proving a *hostile* file cannot
crash, hang, or corrupt does.

### What to test after a parser or protocol change

- **Malformed and truncated input, expecting a clean refusal.** For every parser you touch:
  truncate the file mid-box or mid-packet; corrupt a length or size field so it points past
  the buffer; set a dimension, channel count, or entry count to zero or to `UINT32_MAX`; nest
  boxes to absurd depth; point an offset back at itself. The bar is **errors cleanly, never
  crashes or hangs** — a surfaced error string is a pass, a segfault or a spin is a failure.
  Valid-file checks miss all of this by construction; the regressions live in the inputs the
  author didn't picture.
- **Fuzz the demux and parse entry points under sanitizers.** Build the plugin with
  AddressSanitizer and UndefinedBehaviorSanitizer (`/fsanitize=address` on MSVC; ASan + UBSan
  on the Android/Clang build) and drive the container and protocol readers with mutated
  inputs. ASan turns a silent out-of-bounds read into a named fault with a stack — it is both
  how you find these and how you prove one is gone. An unsanitised "it didn't crash this time"
  is not proof. Fuzzing corrupt input is the single highest-value test this code has; a parser
  change that ships without one is under-tested.
- **Keep every crash's repro as a permanent fixture.** When a malformed stream is found to
  crash, the exact file that triggered it earns a permanent place in the fixture corpus and is
  re-run before every subsequent native change. A fixed memory-safety bug that isn't pinned by
  a regression fixture comes back the next time the surrounding code moves.
- **Regress the good path bit-for-bit, not by eye.** A protocol fix on one transport can shift
  the packets another transport emits, because they share the AU path. After any demux change,
  re-run the known-good fixtures and confirm the demuxer still produces the same packets and
  the same decoded frames — a comparison against a reference decoder (`ffprobe -show_packets`
  for packets, `ffmpeg` frame hashes for pixels) is what makes "the same" objective instead of
  "looked fine to me."

### Rebuilding and platform coverage

Any change under `Native~/` needs the rebuilt binaries verified on **both** platforms it ships
for (Windows x64 DLL, Android arm64 `.so`) — the shared C core means a protocol fix on one
platform can regress the other, and the malformed-input and fuzz checks above apply to each
backend's decode path (Media Foundation on Windows, `AMediaCodec` on Android) as well as the
shared parsers. See the README's "Building the native plugin" section. Note the Windows DLL
cannot be replaced while any Unity instance holds it loaded — close Unity, swap, reopen.
