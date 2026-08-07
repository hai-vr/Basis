# media-fuzz — coverage-guided fuzzing of the demux/parse layer

Developer and CI tooling. The native plugin parses attacker-controlled container and protocol bytes
by hand, in-process, with no sandbox — in multiplayer a peer-broadcast URL is parsed by every
client. This fuzzes those parsers in isolation (no decoder, no Media Foundation, no Unity)
under AddressSanitizer + UndefinedBehaviorSanitizer, so a malformed stream that reads out of
bounds or trips UB faults here instead of on a user's machine.

It compiles the real `Basis/Packages/com.basis.mediaplayer/Native~/protocol/*.c` — the same
source that ships — against a libFuzzer driver, so a find is a find in the shipping parser.

## Build

Needs clang with the fuzzer + sanitizer runtimes. On Linux/CI that's `clang` on PATH; on
Windows install LLVM (`winget install LLVM.LLVM`) and build from Git Bash.

```
./build.sh          # all targets
./build.sh ts       # just the TS demuxer target
```

Output goes to `build/`. On Windows the ASan runtime DLL is staged next to the exe so it runs
in place.

## Run

```
cd build
./fuzz_ts.exe ../corpus/ts ../seeds/ts -max_len=65536 -max_total_time=180
```

`seeds/` and `corpus/` are untracked (corpora get large and are regenerable), so a fresh clone
has neither and libFuzzer refuses to start against a missing directory — from `build/`, run
`mkdir -p ../corpus/<target> ../seeds/<target>` first, for whichever target you are running
(`ts`, `wav`, `rtsp`, and so on). `seeds/ts/` holds small slices of real fixtures (carve them with
`head -c 49152 <real.ts> > seeds/ts/name.ts`); `corpus/ts/` is where libFuzzer saves inputs
that reach new coverage. A crash writes a `crash-<hash>` artifact — replay it with
`./fuzz_ts.exe <artifact>` for the full ASan report.

Note: libFuzzer's `-minimize_crash` can over-reduce a position-sensitive stack overflow past
the crash; if the minimized file stops reproducing, keep the original artifact.

## CI

The `fuzz-demux` job in `.github/workflows/media-native.yml` runs `./build.sh` and then
`./ci-replay.sh` on every change under `Native~/` or `tools/media-fuzz/`. The gate is
deterministic — it fuzzes nothing and grows no corpus. It replays each pinned repro under
`testcases/` and fails if any still crashes, so reverting a memory-safety fix or reintroducing
the bug another way turns the PR red. Coverage-growing runs stay a local (or future scheduled)
activity, which is why finding a crash and *pinning* it are two separate steps: until a repro
lands in `testcases/`, nothing about that bug is enforced.

## Targets

| Target | Parser under test | Sources compiled |
| --- | --- | --- |
| `fuzz_ts` | `basis_ts_run` — MPEG-TS PAT/PMT/PES demux | `basis_ts.c` + `basis_bitstream.c` + `basis_caption.c` |
| `fuzz_mp4` | `basis_mp4_run` — MP4/fMP4 box + sample-table demux | `basis_mp4.c` + `basis_bitstream.c` + `basis_caption.c` |
| `fuzz_webm` | `basis_webm_run` — WebM/Matroska EBML demux | `basis_webm.c` + `basis_bitstream.c` |
| `fuzz_ogg` | `basis_ogg_run` — Ogg page/lacing/CRC demux (`.opus`) | `basis_ogg.c` |
| `fuzz_mp3` | `basis_mp3_run` — MP3 frame/Xing/VBRI demux | `basis_mp3.c` |
| `fuzz_wav` | `basis_wav_run` — RIFF chunk walk, fmt parse, PCM block/seek maths | `basis_wav.c` |
| `fuzz_caption` | `basis_caption_scan_au` — in-band CEA-608 SEI scan | `basis_caption.c` + `basis_bitstream.c` |
| `fuzz_url` | `basis_url_parse` — scheme/userinfo/host/port/path split | `basis_url.c` |
| `fuzz_hls` | `basis_hls_*` — M3U8 master/media parse, URI resolve, segment stitch, seek/reposition | `basis_hls.c` + `basis_url.c` |
| `fuzz_rtsp` | `parse_sdp` + `depkt_video`/`depkt_audio` (RTP FU/AP/afrag reassembly) + `rtsp_recv` | `basis_rtsp.c` (via `#include`) + `basis_bitstream.c` |
| `fuzz_rtmp` | `amf_find_stream_id` + `handle_video`/`handle_audio` (FLV) + `rtmp_read_message` (chunk assembler) | `basis_rtmp.c` (via `#include`) + `basis_bitstream.c` |

`fuzz_hls` injects an in-memory HTTP provider (the fuzz bytes are the body of every fetched
URL — playlist and segments), so no network is touched; it stubs `basis_io_host_is_blocked`
to always-allow so playlist parsing is actually reached (the real SSRF host check resolves DNS
and is exercised at runtime, not in-process — its URL-parsing half is covered by `fuzz_url`).
`basis_hls.c` spawns a producer thread, so `build.sh` links `-pthread` off-Windows.

**RTSP/RTMP.** These own their sockets, so their harness `#include`s the real `basis_rtsp.c` /
`basis_rtmp.c` (statics become reachable, and a find is still a find in the shipping parser) and
provides a link-time `basis_io` stub — no-op for the write paths, byte-serving for the read paths
(`rtsp_recv`, `rtmp_read_message`). The buffer-taking parsers (`parse_sdp`, `depkt_video`/`_audio`,
`amf_find_stream_id`, `handle_video`/`_audio`) are called directly with no handshake to script, so
they hit the exact code where review found the H1/M2 OOBs. This first run found a **signed left-shift
UB** in the RTP timestamp read (`rtp[4] << 24` on a byte ≥ 128, in both `depkt_video`/`depkt_audio`,
plus the same in the RTMP chunk stream id) — fixed and pinned as `testcases/rtsp/rtp_ts_shift_ub.bin`.
A full-session seam (scripted handshake, or an injected transport vtable that would also enable
RTSP/RTMP unit tests) is the remaining depth work.

New targets slot in the same way — one `fuzz_<name>.c` driver plus its protocol sources in
`build.sh`. Any offset-driven parser needs a `reseek` callback from the driver or it never
reaches the code behind a seek: MP4 (`moov` sample tables and chunk offsets) and WebM
(SeekHead/Cues index) need one to reach the sample data at all, while MP3 and WAV use it for
the resync and byte-offset seek paths, which is also why those two drivers post a couple of
`take_seek` requests rather than only reading forward. The
caption scanner takes an AU buffer directly (no sink/read), so the fuzz input *is* the AU; it runs
both the H.264 and H.265 SEI layouts.

**Runs so far:** the TS target found four bugs (all fixed in #962, repros below). MP4 (~388k),
WebM (~5.8M, against #960's AV1 code), and caption (~280k) all ran with **zero findings** against
the #962-fixed bitstream — those parsers guard their length fields, unlike the TS section loop.
WAV (~24M) found nothing either. Where a target has no run recorded here, treat it as unfuzzed
beyond whatever the seed corpus reached, not as clean.

**Fuzzing against an open PR's code:** to fuzz code that isn't on `developer` yet, overlay that
branch's protocol sources into the working tree before building (`git checkout <branch> -- <file>`;
don't commit). WebM was fuzzed against `feat/mediaplayer-av1` (#960, adds the `V_AV1` `CodecPrivate`
path) this way. Run MP4/WebM against the #962-fixed `basis_bitstream.c`, or they re-find the shared
SPS bugs the TS run already surfaced.

**Sink contract:** the driver's sink must supply `on_state`/`on_error`/`on_end_of_stream` — the
parsers call these without a NULL check (only `on_duration`/`on_transport`/`take_seek` may be
NULL). A sink missing them faults inside the parser and reads as a false parser bug.

## testcases/

Every crash the fuzzer finds is pinned here as a regression input, so a re-run confirms the
fix and guards against reintroduction. Replay one with `build/fuzz_<name>.exe testcases/...`.
The three SPS repros run through `fuzz_ts`, which guards the shared Exp-Golomb reader itself —
a regression in `gb_ue` or the bit-position accounting fails here. They do **not** enter
`basis_mp4_run` or `basis_webm_run`, so a bug specific to how those containers call the reader
is not covered by replaying them.

A repro that proves a *bound* rather than a crash needs libFuzzer told what the bound is, or
the replay passes whether or not the bound still exists. Those flags live in
`testcases/<target>/replay-opts`, which `ci-replay.sh` reads and excludes from the repro list.

- `ts/pat_pmt_section_len_oob.ts` — out-of-bounds read in `parse_pat`/`parse_pmt`: the 12-bit
  `section_len` (and PMT `prog_info_len`) is trusted and walked up to ~4 KB past the ~184-byte
  TS payload, running off the demux buffer when the section packet is the last one buffered.
- `ts/sps_ue_shift_ub.ts` — UB in the H.264 Exp-Golomb reader: `gb_ue` shifted `1u` by up to 32.
  Fixed by capping the leading-zero count at 31, past which a `ue(v)` is malformed anyway.
- `ts/sps_bitpos_overflow.ts` — `gb_u` advanced an `int` bit position with no bound, and the
  `poc_type == 1` loop reads an uncapped `ue(v)` count, so one SPS both overflowed the position
  and span billions of iterations (an effective hang). Fixed by freezing the position past the
  end of the buffer and capping the cycle at 255, H.264's real maximum.
- `ts/sps_crop_int_overflow.ts` — the width/height crop maths cast attacker-controlled `ue(v)`
  sums to `int` and doubled them, overflowing before the existing 1..8192 range check could
  reject the result. Fixed by computing in `int64`.
- `rtsp/rtp_ts_shift_ub.bin` — signed left-shift UB in the RTP timestamp read: `rtp[4] << 24`
  shifted a byte ≥ 128 into the `int` sign bit (`depkt_video`/`depkt_audio`). A 12-byte RTP
  header with `byte[4] = 0xFF` trips it; fixed by reading the timestamp through `uint32_t`.
- `rtsp/sdp_b64_shift_ub.bin` — signed left-shift UB in the SDP base64 decoder: `b64dec`
  feeds the whole input through one `int` accumulator (`val = (val << 6) | tab[c]`), which
  reaches the sign bit a few characters into any `sprop-*` blob the server chooses. Fixed by
  accumulating in `uint32_t`; only the low bits are read back out, so the defined wrap is
  harmless.
- `rtmp/msg_len_alloc_bomb.bin` — the RTMP per-message allocation cap (`RTMP_MAX_MSG`). A
  12-byte fmt-0 chunk header declaring a 24-bit message length of `0xFFFFFF` grew the buffer to
  16 MiB before a single payload byte arrived. Replayed under `-malloc_limit_mb=8`: with the cap
  the header is refused, without it libFuzzer reports `out-of-memory (malloc(16777216))`. Unlike
  the crash repros, this one only means anything with that flag, hence `rtmp/replay-opts`.
- `rtmp/chunk_streamid_shift_ub.bin` — the same signed-shift UB in the RTMP chunk stream id
  (`sid[3] << 24`, `rtmp_read_message`), which the RTP repro above doesn't reach. A 12-byte fmt-0
  chunk header with the stream-id MSB set trips it; fixed by shifting through `uint32_t`.
