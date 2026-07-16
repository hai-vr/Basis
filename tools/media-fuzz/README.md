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

`seeds/ts/` holds small slices of real fixtures (carve them with
`head -c 49152 <real.ts> > seeds/ts/name.ts`); `corpus/ts/` is where libFuzzer saves inputs
that reach new coverage. A crash writes a `crash-<hash>` artifact — replay it with
`./fuzz_ts.exe <artifact>` for the full ASan report.

Note: libFuzzer's `-minimize_crash` can over-reduce a position-sensitive stack overflow past
the crash; if the minimized file stops reproducing, keep the original artifact.

## Targets

| Target | Parser under test | Sources compiled |
| --- | --- | --- |
| `fuzz_ts` | `basis_ts_run` — MPEG-TS PAT/PMT/PES demux | `basis_ts.c` + `basis_bitstream.c` + `basis_caption.c` |
| `fuzz_mp4` | `basis_mp4_run` — MP4/fMP4 box + sample-table demux | `basis_mp4.c` + `basis_bitstream.c` + `basis_caption.c` |
| `fuzz_webm` | `basis_webm_run` — WebM/Matroska EBML demux | `basis_webm.c` + `basis_bitstream.c` |
| `fuzz_ogg` | `basis_ogg_run` — Ogg page/lacing/CRC demux (`.opus`) | `basis_ogg.c` |
| `fuzz_caption` | `basis_caption_scan_au` — in-band CEA-608 SEI scan | `basis_caption.c` + `basis_bitstream.c` |

New targets slot in the same way — one `fuzz_<name>.c` driver plus its protocol sources in
`build.sh`. The MP4 and WebM drivers add a `reseek` callback (both are offset-driven — MP4's `moov`
sample tables and chunk offsets, WebM's SeekHead/Cues index), so the fuzzer must reposition the
in-memory cursor or the parser never reaches the sample data or the ranged-fetch seek paths. The
caption scanner takes an AU buffer directly (no sink/read), so the fuzz input *is* the AU; it runs
both the H.264 and H.265 SEI layouts.

**Runs so far:** the TS target found four bugs (all fixed in #962, repros below). MP4 (~388k),
WebM (~5.8M, against #960's AV1 code), and caption (~280k) all ran with **zero findings** against
the #962-fixed bitstream — those parsers guard their length fields, unlike the TS section loop.

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

- `ts/pat_pmt_section_len_oob.ts` — out-of-bounds read in `parse_pat`/`parse_pmt`: the 12-bit
  `section_len` (and PMT `prog_info_len`) is trusted and walked up to ~4 KB past the ~184-byte
  TS payload, running off the demux buffer when the section packet is the last one buffered.
