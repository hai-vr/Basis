/*
 * basis_wav.c — RIFF/WAVE demuxer.
 *
 * Parses the fmt chunk, announces the data chunk as BASIS_CODEC_LPCM with the
 * little-endian/WAVE-order flags byte in the config blob, and streams the PCM
 * in ~20 ms whole-frame blocks with a sample-counted timeline. The decode
 * layer's LPCM bypass converts straight into the ring — no OS decoder.
 *
 * Supported: WAVE_FORMAT_PCM (1) and WAVE_FORMAT_EXTENSIBLE (0xFFFE) wrapping
 * PCM, 16- or 24-bit integer samples, 1-8 channels, 8-96 kHz. Anything else
 * raises on_error — a WAV is audio-only, so unplayable audio means an
 * unplayable file (unlike the muxed lanes, where audio fails muted).
 */

#include "basis_wav.h"

#include <stdlib.h>
#include <string.h>

static uint32_t rd_le32(const uint8_t* p) {
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}
static uint16_t rd_le16(const uint8_t* p) { return (uint16_t)(p[0] | (p[1] << 8)); }

/* Fill exactly n bytes; returns bytes read (== n unless EOF/stop). */
static int read_full(basis_media_sink_t* sink, basis_read_fn rd, void* ctx, uint8_t* buf, int n) {
    int got = 0;
    while (got < n && sink->is_running(sink->user)) {
        int r = rd(ctx, buf + got, n - got);
        if (r <= 0) break;
        got += r;
    }
    return got;
}

/* Discard n bytes from a sequential-only source. Returns 0 on EOF/stop. */
static int skip_bytes(basis_media_sink_t* sink, basis_read_fn rd, void* ctx, uint32_t n) {
    uint8_t scratch[4096];
    while (n > 0) {
        int want = n > sizeof(scratch) ? (int)sizeof(scratch) : (int)n;
        int got = read_full(sink, rd, ctx, scratch, want);
        if (got < want) return 0;
        n -= (uint32_t)got;
    }
    return 1;
}

int basis_wav_run(basis_media_sink_t* sink, basis_read_fn rd, void* ctx) {
    uint8_t hdr[12];
    if (read_full(sink, rd, ctx, hdr, 12) < 12 ||
        memcmp(hdr, "RIFF", 4) != 0 || memcmp(hdr + 8, "WAVE", 4) != 0) {
        sink->on_error(sink->user, "not a RIFF/WAVE stream");
        return -1;
    }

    int fmt_seen = 0;
    int channels = 0, rate = 0, bits = 0, block_align = 0;
    uint32_t byte_rate = 0;

    for (;;) {
        uint8_t ch8[8];
        if (read_full(sink, rd, ctx, ch8, 8) < 8) return 0; /* clean EOF before data */
        uint32_t csz = rd_le32(ch8 + 4);

        if (memcmp(ch8, "fmt ", 4) == 0) {
            uint8_t f[40];
            uint32_t take = csz < sizeof(f) ? csz : (uint32_t)sizeof(f);
            if (read_full(sink, rd, ctx, f, (int)take) < (int)take) return 0;
            if (take < 16 || !skip_bytes(sink, rd, ctx, (csz - take) + (csz & 1))) {
                if (take < 16) { sink->on_error(sink->user, "WAV fmt chunk truncated"); return -1; }
                return 0;
            }
            int tag    = rd_le16(f);
            channels   = rd_le16(f + 2);
            rate       = (int)rd_le32(f + 4);
            byte_rate  = rd_le32(f + 8);
            block_align = rd_le16(f + 12);
            bits       = rd_le16(f + 14);
            if (tag == 0xFFFE && take >= 40) {
                int valid = rd_le16(f + 18);
                if (valid) bits = valid;         /* container bits may pad */
                tag = rd_le16(f + 24);           /* SubFormat GUID leads with the tag */
            }
            if (tag != 1) { sink->on_error(sink->user, "WAV: only integer PCM is supported"); return -1; }
            fmt_seen = 1;
            continue;
        }

        if (memcmp(ch8, "data", 4) != 0) {
            if (!skip_bytes(sink, rd, ctx, csz + (csz & 1))) return 0;
            continue;
        }

        /* data chunk */
        if (!fmt_seen) { sink->on_error(sink->user, "WAV: data chunk before fmt"); return -1; }
        if ((bits != 16 && bits != 24) || channels < 1 || channels > 8 ||
            rate < 8000 || rate > 96000 || block_align != channels * (bits / 8)) {
            sink->on_error(sink->user, "WAV format unsupported (need 16/24-bit integer PCM, 1-8 ch, 8-96 kHz)");
            return -1;
        }

        /* config blob: assignment 0 (identity order), bits code (1=16, 3=24 —
         * shared with the Blu-ray lane), flags bit0 = little-endian samples. */
        uint8_t cfg[3] = { 0, (uint8_t)(bits == 16 ? 1 : 3), 1 };
        sink->on_audio_format(sink->user, BASIS_CODEC_LPCM, rate, channels, cfg, 3);

        /* 0/0xFFFFFFFF = unknown length (streaming capture): play to EOF. */
        int bounded = (csz != 0 && csz != 0xFFFFFFFFu);
        if (bounded && sink->on_duration && byte_rate > 0)
            sink->on_duration(sink->user, (int64_t)csz * 1000000 / byte_rate);

        int frame_bytes = block_align;
        int chunk_frames = rate / 50 > 0 ? rate / 50 : 1;   /* ~20 ms */
        int chunk_bytes = chunk_frames * frame_bytes;
        uint8_t* buf = (uint8_t*)malloc((size_t)chunk_bytes);
        if (!buf) { sink->on_error(sink->user, "out of memory"); return -1; }

        int64_t frames_sent = 0;
        uint32_t left = csz;
        while (sink->is_running(sink->user)) {
            int want = chunk_bytes;
            if (bounded && (uint32_t)want > left) want = (int)left;
            if (want <= 0) break;
            int got = read_full(sink, rd, ctx, buf, want);
            got -= got % frame_bytes;      /* whole interleaved frames only */
            if (got <= 0) break;
            int64_t pts = frames_sent * 1000000 / rate;
            sink->on_audio_frame(sink->user, buf, got, pts);
            frames_sent += got / frame_bytes;
            if (bounded) left -= (uint32_t)got;
        }
        free(buf);
        if (sink->on_end_of_stream && bounded && left == 0)
            sink->on_end_of_stream(sink->user);
        return 0;
    }
}
