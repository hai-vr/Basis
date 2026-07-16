/*
 * basis_ogg.c — Ogg demuxer for Opus (`.opus` files).
 *
 * Ogg framing: pages begin with the "OggS" capture pattern, a 27-byte header
 * (version, header-type flags, granule position, serial, page seq, CRC, and a
 * segment count), a segment table, then the page body. A packet is a run of
 * segments; a 255 lacing value continues the packet (into the next segment, and
 * across a page boundary when the page ends on 255), any value < 255 ends it.
 *
 * Scope: one logical Opus bitstream. First packet = OpusHead (identification),
 * second = OpusTags (comments, skipped), then audio packets. Per-packet PTS
 * accumulates from Opus TOC-derived durations starting at zero (the encoder
 * pre-skip is a decode-layer concern, matching the WebM lane). Chained streams
 * (a new serial after EOS) are treated as end-of-stream. Each page's CRC-32 is
 * verified; a page that fails is dropped and the walk resyncs on the next
 * "OggS". Forward playback only — no granulepos seek, so no duration.
 *
 * Everything here parses attacker-controlled bytes, so every length is bounded
 * before use (this is a fuzz target: tools/media-fuzz/fuzz_ogg).
 */
#include "basis_ogg.h"

#include <stdlib.h>
#include <string.h>

#define OGG_HDR 27                       /* fixed page header size */
#define OGG_MAX_BODY (255 * 255)         /* max page body (255 segments * 255) */
#define OGG_MAX_PACKET (8 * 1024 * 1024) /* runaway guard for reassembled packets */

typedef struct {
    basis_media_sink_t* sink;
    basis_read_fn read;
    void* ctx;

    uint32_t serial;
    int have_serial;
    int ended;

    long long packet_index;   /* 0 = OpusHead, 1 = OpusTags, 2+ = audio */
    int channels, pre_skip;
    int announced;
    int64_t next_pts_us;

    uint8_t* pkt;             /* packet reassembled across segments/pages */
    int pkt_len, pkt_cap;
} ogg_t;

/* ---- Ogg CRC-32 (poly 0x04c11db7, MSB-first, no reflection, no final xor) --
 * Chainable so header + segment table + body hash contiguously across regions. */
static uint32_t ogg_crc(uint32_t crc, const uint8_t* p, int n) {
    for (int i = 0; i < n; ++i) {
        crc ^= (uint32_t)p[i] << 24;
        for (int b = 0; b < 8; ++b)
            crc = (crc & 0x80000000u) ? (crc << 1) ^ 0x04c11db7u : (crc << 1);
    }
    return crc;
}

static int oread(ogg_t* o, uint8_t* buf, int n) {
    int got = 0;
    while (got < n) {
        if (!o->sink->is_running(o->sink->user)) return 0;
        int r = o->read(o->ctx, buf + got, n - got);
        if (r <= 0) return 0;
        got += r;
    }
    return 1;
}

/* Opus TOC -> samples at 48 kHz (frame size * frame count). */
static const int kOpusFrame48k[32] = {
    480, 960, 1920, 2880,  480, 960, 1920, 2880,  480, 960, 1920, 2880,
    480, 960,  480,  960,  120, 240,  480,  960,  120, 240,  480,  960,
    120, 240,  480,  960,  120, 240,  480,  960
};

static int opus_packet_samples(const uint8_t* p, int len) {
    if (len < 1) return 0;
    int config = p[0] >> 3;
    int code = p[0] & 0x3;
    int frames = code == 0 ? 1 : code == 3 ? (len >= 2 ? (p[1] & 0x3F) : 0) : 2;
    long s = (long)kOpusFrame48k[config] * frames;
    if (s < 0 || s > 5760) s = s < 0 ? 0 : 5760; /* clamp to Opus's per-packet max */
    return (int)s;
}

static void emit_packet(ogg_t* o, const uint8_t* p, int len) {
    if (len <= 0) return;
    if (o->packet_index == 0) {
        /* OpusHead: "OpusHead" ver ch pre_skip(LE16) rate(LE32) gain(LE16) family */
        if (len >= 19 && memcmp(p, "OpusHead", 8) == 0) {
            o->channels = p[9];
            o->pre_skip = p[10] | (p[11] << 8);
            if (o->channels >= 1 && o->channels <= 8) {
                o->sink->on_audio_format(o->sink->user, BASIS_CODEC_OPUS, 48000,
                                         o->channels, p, len);
                o->announced = 1;
            }
        }
    } else if (o->packet_index == 1) {
        /* OpusTags: comment header, no audio — skip. */
    } else if (o->announced) {
        o->sink->on_audio_frame(o->sink->user, p, len, o->next_pts_us);
        int samples = opus_packet_samples(p, len);
        o->next_pts_us += (int64_t)samples * 1000000LL / 48000;
    }
    o->packet_index++;
}

/* Append body bytes to the pending packet; flush on any lacing value < 255. The
 * final segment being 255 leaves the packet open to continue on the next page. */
static void feed_segments(ogg_t* o, const uint8_t* segtab, int nsegs,
                          const uint8_t* body, int bodylen) {
    int off = 0;
    for (int i = 0; i < nsegs; ++i) {
        int seg = segtab[i];
        if (off + seg > bodylen) return;               /* truncated page */
        if (o->pkt_len + seg > o->pkt_cap) {
            int ncap = o->pkt_cap ? o->pkt_cap : 4096;
            while (ncap < o->pkt_len + seg && ncap < OGG_MAX_PACKET) ncap *= 2;
            if (ncap < o->pkt_len + seg) { o->pkt_len = 0; return; } /* over cap: drop */
            uint8_t* nb = (uint8_t*)realloc(o->pkt, (size_t)ncap);
            if (!nb) { o->pkt_len = 0; return; }
            o->pkt = nb; o->pkt_cap = ncap;
        }
        memcpy(o->pkt + o->pkt_len, body + off, (size_t)seg);
        o->pkt_len += seg;
        off += seg;
        if (seg < 255) {                                /* packet complete */
            emit_packet(o, o->pkt, o->pkt_len);
            o->pkt_len = 0;
        }
    }
}

int basis_ogg_run(basis_media_sink_t* sink, basis_read_fn read, void* ctx,
                  basis_reseek_fn reseek, void* reseek_ctx) {
    (void)reseek; (void)reseek_ctx;
    ogg_t o;
    memset(&o, 0, sizeof(o));
    o.sink = sink; o.read = read; o.ctx = ctx;

    uint8_t* body = (uint8_t*)malloc(OGG_MAX_BODY);
    if (!body) return 0;

    uint8_t sync[4] = {0};
    int have = 0;                        /* bytes of the capture pattern matched */

    while (sink->is_running(sink->user)) {
        /* Find the "OggS" capture pattern, byte by byte (this is also the resync
         * path after a bad page). */
        if (have < 4) {
            uint8_t c;
            if (!oread(&o, &c, 1)) break;
            const char* cap = "OggS";
            if (c == (uint8_t)cap[have]) sync[have++] = c;
            else have = (c == (uint8_t)'O') ? (sync[0] = 'O', 1) : 0;
            continue;
        }
        have = 0;

        uint8_t hdr[OGG_HDR];
        memcpy(hdr, "OggS", 4);
        if (!oread(&o, hdr + 4, OGG_HDR - 4)) break;   /* rest of the header */

        int nsegs = hdr[26];
        uint8_t segtab[255];
        if (nsegs > 0 && !oread(&o, segtab, nsegs)) break;

        int bodylen = 0;
        for (int i = 0; i < nsegs; ++i) bodylen += segtab[i];
        if (bodylen > 0 && !oread(&o, body, bodylen)) break;

        /* CRC-32 over the whole page (header + segment table + body) with the
         * checksum field zeroed, compared to the stored value. */
        uint32_t stored = (uint32_t)hdr[22] | ((uint32_t)hdr[23] << 8) |
                          ((uint32_t)hdr[24] << 16) | ((uint32_t)hdr[25] << 24);
        hdr[22] = hdr[23] = hdr[24] = hdr[25] = 0;
        uint32_t crc = ogg_crc(0, hdr, OGG_HDR);
        crc = ogg_crc(crc, segtab, nsegs);
        crc = ogg_crc(crc, body, bodylen);
        if (crc != stored) continue;                   /* damaged page: resync */

        uint8_t htype = hdr[5];
        uint32_t serial = (uint32_t)hdr[14] | ((uint32_t)hdr[15] << 8) |
                          ((uint32_t)hdr[16] << 16) | ((uint32_t)hdr[17] << 24);

        if (htype & 0x02) {                            /* BOS: start of a stream */
            if (o.ended) break;                        /* chained stream: stop at EOS */
            if (!o.have_serial) { o.serial = serial; o.have_serial = 1; }
        }
        if (!o.have_serial || serial != o.serial) continue; /* other logical stream */

        /* A page whose first packet is a continuation resumes o.pkt; a fresh page
         * after a completed packet starts clean. The 0x01 continued flag is
         * advisory — feed_segments already tracks the open packet across pages. */
        feed_segments(&o, segtab, nsegs, body, bodylen);

        if (htype & 0x04) { o.ended = 1; break; }      /* EOS */
    }

    free(o.pkt);
    free(body);
    if (!o.announced) sink->on_error(sink->user, "Ogg has no Opus stream this player supports");
    return 0;
}
