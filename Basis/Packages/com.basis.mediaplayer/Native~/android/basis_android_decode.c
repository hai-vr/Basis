/*
 * basis_android_decode.c — Android/Quest OS-codec backend (implements basis_decoder_*).
 *
 * Two entry paths:
 *   try_open_url  — https TS/MP4: AMediaExtractor (TLS+demux) -> AMediaCodec.
 *                   A worker thread pumps samples and renders to a Surface.
 *   submit_video  — rtsp/rtmp: the core demuxer feeds Annex-B AUs; we create the
 *                   codec from the SPS/PPS extradata and queue AUs directly.
 *
 * Video output goes to an AImageReader Surface in HardwareBuffer mode; each
 * decoded AHardwareBuffer is handed to the Vulkan present (basis_android_vk),
 * which imports it and resolves YCbCr -> an RGBA VkImage Unity samples.
 * Audio (AAC) is decoded to PCM and written to a ring the C# sink pulls.
 */

#include "../basis_media_internal.h"
#include "basis_android_vk.h"

#include <media/NdkMediaCodec.h>
#include <media/NdkMediaExtractor.h>
#include <media/NdkMediaFormat.h>
#include <media/NdkImageReader.h>
#include <media/NdkImage.h>
#include <android/native_window.h>
#include <android/hardware_buffer.h>
#include <android/log.h>

#include <pthread.h>
#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <unistd.h>
#include <time.h>

/* ---- monotonic clock ---------------------------------------------------- */

static int64_t now_monotonic_us(void) {
    struct timespec ts;
    clock_gettime(CLOCK_MONOTONIC, &ts);
    return (int64_t)ts.tv_sec * 1000000LL + ts.tv_nsec / 1000;
}

/* ---- PCM ring ----------------------------------------------------------- */

/* Interleaved float FIFO with per-chunk PTS metadata, mirroring the Windows
 * PcmRing (basis_win_decode.cpp): the decode thread writes chunks tagged with
 * their media timestamps; the audio thread reads gated against the presentation
 * clock, so a connect burst or post-stall backlog is trimmed rather than served
 * out forever behind the video. Drops are always whole-frame counts, so the
 * surviving stream keeps its channel phase; reads may return partial frames —
 * the managed splitter carries sub-frame remainders across pulls. */
#define PCM_CHUNKS 1024
typedef struct { int64_t pts; int floats; } pcm_chunk;
typedef struct {
    float* buf; int cap, head, tail;
    int frame; /* floats per interleaved frame (channel count) */
    int sr;    /* sample rate, for chunk durations */
    pcm_chunk chunks[PCM_CHUNKS];
    int chead, ccount;
    long trims;          /* diagnostics: clock-gated trims fired */
    int lastTrimFloats;  /* diagnostics: floats dropped by the last trim */
    pthread_mutex_t m;
} pcm_ring;

/* Serving is gated on media time (mirroring the Windows PcmRing): a sample is
 * released when its PTS comes due against the serve target (presentation clock
 * + the sink's output latency, so alignment lands at the speaker). Surplus the
 * mux delivered early waits in the ring instead of becoming output latency,
 * and just-in-time delivery banks a cushion behind the video hold instead of
 * running dry. The caller's early-hold is serve hysteresis sized above the
 * sink's pull-batch depth. A head further than TRIM_LATE overdue (connect
 * burst, post-stall backlog, PTS jump) is trimmed to the target — re-anchoring
 * on the discontinuity rather than discarding real-time delivery forever. */
#define PCM_TRIM_LATE_US     150000LL

static void ring_init(pcm_ring* r, int floats) { r->buf = malloc(sizeof(float) * floats); r->cap = floats; r->head = r->tail = 0; r->frame = 2; r->sr = 48000; r->chead = r->ccount = 0; r->trims = 0; r->lastTrimFloats = 0; pthread_mutex_init(&r->m, NULL); }
static void ring_free(pcm_ring* r) { free(r->buf); r->buf = NULL; pthread_mutex_destroy(&r->m); }
static void ring_set_frame(pcm_ring* r, int frame, int sr) {
    pthread_mutex_lock(&r->m);
    r->frame = frame > 0 ? frame : 1;
    if (sr > 0) r->sr = sr;
    r->head = r->tail = 0;   /* buffered floats are in the old framing */
    r->chead = r->ccount = 0;
    pthread_mutex_unlock(&r->m);
}

static int ring_fill(const pcm_ring* r) { return (r->tail - r->head + r->cap) % r->cap; }

/* Drops the oldest `n` floats (rounded down to whole frames) from the float ring
 * and the chunk metadata together. Caller holds r->m. */
static void ring_drop_oldest(pcm_ring* r, int n) {
    n -= n % r->frame;
    int avail = ring_fill(r);
    if (n > avail) n = avail - (avail % r->frame);
    if (n <= 0) return;
    r->head = (r->head + n) % r->cap;
    int srr = r->sr > 0 ? r->sr : 48000;
    while (n > 0 && r->ccount > 0) {
        pcm_chunk* c = &r->chunks[r->chead];
        if (c->floats <= n) { n -= c->floats; r->chead = (r->chead + 1) % PCM_CHUNKS; r->ccount--; }
        else { c->floats -= n; c->pts += (int64_t)(n / r->frame) * 1000000LL / srr; n = 0; }
    }
}

static void ring_write(pcm_ring* r, const float* s, int n, int64_t pts) {
    if (n <= 0) return;
    pthread_mutex_lock(&r->m);
    int srr = r->sr > 0 ? r->sr : 48000;
    if (n > r->cap - 1) {
        /* Over-capacity write: drop the oldest whole frames and carry the PTS
         * forward so the retained tail keeps a correct timestamp. */
        int keep = (r->cap - 1) - ((r->cap - 1) % r->frame);
        int drop = n - keep;
        s += drop;
        pts += (int64_t)(drop / r->frame) * 1000000LL / srr;
        n = keep;
    }
    int space = r->cap - 1 - ring_fill(r);
    if (n > space) {
        int need = (n - space) + r->frame - 1;
        ring_drop_oldest(r, need - need % r->frame);
    }
    for (int i = 0; i < n; ++i) { r->buf[r->tail] = s[i]; r->tail = (r->tail + 1) % r->cap; }
    if (r->ccount == PCM_CHUNKS) {
        r->chunks[(r->chead + r->ccount - 1) % PCM_CHUNKS].floats += n;   /* metadata full: coalesce into the tail chunk */
    } else {
        pcm_chunk* c = &r->chunks[(r->chead + r->ccount) % PCM_CHUNKS];
        c->pts = pts; c->floats = n; r->ccount++;
    }
    pthread_mutex_unlock(&r->m);
}

/* target_us = INT64_MIN reads ungated (audio-only stream, no clock). */
static int ring_read(pcm_ring* r, float* out, int n, int64_t target_us, int64_t early_hold_us) {
    pthread_mutex_lock(&r->m);
    int srr = r->sr > 0 ? r->sr : 48000;
    if (target_us != INT64_MIN && r->ccount > 0) {
        int64_t late = target_us - r->chunks[r->chead].pts;
        if (late > PCM_TRIM_LATE_US) {
            int drop = (int)(late * srr / 1000000LL) * r->frame;
            ring_drop_oldest(r, drop);
            r->trims++; r->lastTrimFloats = drop;
        }
    }
    int got = 0;
    while (got < n && r->ccount > 0) {
        pcm_chunk* c = &r->chunks[r->chead];
        if (target_us != INT64_MIN && c->pts > target_us + early_hold_us) break;
        int take = c->floats < n - got ? c->floats : n - got;
        for (int i = 0; i < take; ++i) { out[got + i] = r->buf[r->head]; r->head = (r->head + 1) % r->cap; }
        got += take;
        if (take == c->floats) { r->chead = (r->chead + 1) % PCM_CHUNKS; r->ccount--; }
        else { c->floats -= take; c->pts += (int64_t)take * 1000000LL / ((int64_t)r->frame * srr); }
    }
    pthread_mutex_unlock(&r->m);
    return got;
}

/* PTS just past the newest queued sample — the audio delivery edge.
 * INT64_MIN when empty. */
static int64_t ring_newest_pts(pcm_ring* r) {
    pthread_mutex_lock(&r->m);
    int64_t v = INT64_MIN;
    if (r->ccount > 0) {
        pcm_chunk* c = &r->chunks[(r->chead + r->ccount - 1) % PCM_CHUNKS];
        v = c->pts + (int64_t)(c->floats / (r->frame > 0 ? r->frame : 1)) * 1000000LL / (r->sr > 0 ? r->sr : 48000);
    }
    pthread_mutex_unlock(&r->m);
    return v;
}

/* Diagnostics: currently-queued audio, in milliseconds. */
static int ring_fill_ms(pcm_ring* r) {
    pthread_mutex_lock(&r->m);
    int frames = r->frame > 0 ? ring_fill(r) / r->frame : 0;
    int srr = r->sr > 0 ? r->sr : 48000;
    pthread_mutex_unlock(&r->m);
    return (int)((int64_t)frames * 1000 / srr);
}

/* ---- video frame ring --------------------------------------------------- */

/* Decoded frames are held as acquired AImages (each owning an AHardwareBuffer),
 * tagged with their presentation PTS, so render_update can present the frame due
 * on the presentation clock instead of always the newest — the Android mirror of
 * the Windows frame ring. Sized to span the jitter buffer plus a few slots of
 * decode headroom (the codec needs free buffers to render into); each slot is a
 * full-resolution hardware buffer, so it's kept modest — but large enough that
 * maxBuf ((VRING-6) * frame-period) comfortably exceeds the sync hold at 24-30fps
 * (26 usable frames = ~1.08s @24fps, ~867ms @30fps). */
#define VRING 32

/* ---- decoder ------------------------------------------------------------ */

struct basis_decoder {
    basis_media_engine_t* engine;

    AImageReader* reader;
    ANativeWindow* window;

    AMediaCodec* vcodec;
    AMediaCodec* acodec;
    AMediaExtractor* extractor;
    int video_track, audio_track;

    basis_codec_t vc;
    int vw, vh;
    int vconfigured, aconfigured;

    int asr, ach;
    int apcm_float; /* decoder emits float PCM (pcm-encoding 4) instead of 16-bit */

    basis_codec_t ac;       /* audio lane: AAC (MediaCodec) or LPCM (direct convert) */
    int aLpcmAssign;        /* Blu-ray channel_assignment (from the format's config blob) */
    int aLpcmBits;          /* 16 or 24 */
    int aLpcmLE;            /* 1 = little-endian samples (RIFF/WAV lane) */
    float* lpcmBuf;         /* conversion scratch, grown to the largest frame batch */
    int lpcmBufCap;

    basis_vk_present* vk;

    pthread_t worker;
    int worker_started;

    int64_t lastPtsUs;      /* decode edge: PTS of the newest frame written to the ring */
    pcm_ring pcm;

    /* video frame ring (parallel arrays; img==NULL marks an empty slot) */
    AImage* vimg[VRING];
    int64_t vpts[VRING];
    int vfw[VRING], vfh[VRING];
    pthread_mutex_t vm;

    /* presentation clock (render thread), mirroring basis_win_decode.cpp */
    int clockStarted;
    int64_t wallStartUs;      /* monotonic-us origin of the current clock lock */
    int64_t lastRenderUs;
    int64_t renderTickUs;     /* EMA of the render-callback period (due-check lookahead) */
    int64_t primeStartUs;     /* first render tick with a frame (VOD prime window) */
    int64_t mediaStartUs;
    int64_t lastPresentedPts; /* PTS of the frame currently shown; INT64_MIN = none */
    int64_t presentedPosUs;   /* stable position for get_position_us; -1 until first present */
    int64_t frameIntervalUs;  /* EMA of inter-frame PTS delta (source frame period) */
    int64_t prevWritePts;     /* last frame PTS enqueued (for the interval EMA) */
    int64_t audClockOffsetUs; /* published media-time offset from the monotonic clock; INT64_MIN = not started */
    int bufferUs;             /* jitter buffer: how far behind live we present */
    int bufferMode;           /* 0 = fixed, 1 = dynamic */
    int audioLatencyUs;       /* managed sink's reported output latency; drives the video hold + audio lead */

    /* debug counters */
    long dbg_render, dbg_nodue, dbg_acqfail, dbg_drop, dbg_lagms;
};

/* ---- AImageReader callback: enqueue decoded frames into the video ring --- */

/* Deletes the oldest frame in the ring (freeing its reader slot). Caller holds
 * d->vm. */
static void vring_drop_oldest_locked(basis_decoder_t* d) {
    int oldest = -1; int64_t best = INT64_MAX;
    for (int i = 0; i < VRING; ++i) if (d->vimg[i] && d->vpts[i] < best) { best = d->vpts[i]; oldest = i; }
    if (oldest >= 0) { AImage_delete(d->vimg[oldest]); d->vimg[oldest] = NULL; d->dbg_drop++; }
}

static void on_image(void* ctx, AImageReader* reader) {
    basis_decoder_t* d = (basis_decoder_t*)ctx;
    for (;;) {
        /* Can't hold more than the reader's maxImages at once, so free the oldest
         * slot before acquiring when the ring is full (drops the least useful
         * frame rather than stalling the decoder). */
        pthread_mutex_lock(&d->vm);
        int held = 0; for (int i = 0; i < VRING; ++i) if (d->vimg[i]) held++;
        if (held >= VRING) vring_drop_oldest_locked(d);
        pthread_mutex_unlock(&d->vm);

        AImage* img = NULL;
        if (AImageReader_acquireNextImage(reader, &img) != AMEDIA_OK || !img) break;

        int64_t ts_ns = 0;
        AImage_getTimestamp(img, &ts_ns);       /* MediaCodec propagates the input PTS (ns) */
        int64_t pts = ts_ns / 1000;
        int32_t aw = 0, ah = 0;
        AImage_getWidth(img, &aw); AImage_getHeight(img, &ah);

        pthread_mutex_lock(&d->vm);
        int slot = -1; for (int i = 0; i < VRING; ++i) if (!d->vimg[i]) { slot = i; break; }
        if (slot >= 0) {
            d->vimg[slot] = img;
            d->vpts[slot] = pts;
            d->vfw[slot] = aw > 0 ? aw : d->vw;
            d->vfh[slot] = ah > 0 ? ah : d->vh;
            /* frame-period EMA, for the jitter-buffer ceiling */
            if (d->prevWritePts != INT64_MIN) {
                int64_t dpts = pts - d->prevWritePts;
                if (dpts > 0 && dpts < 1000000)
                    d->frameIntervalUs = d->frameIntervalUs > 0 ? (d->frameIntervalUs * 7 + dpts) / 8 : dpts;
            }
            d->prevWritePts = pts;
            img = NULL;
        }
        pthread_mutex_unlock(&d->vm);

        if (img) AImage_delete(img); /* ring still full after a drop: shouldn't happen */
    }
}

static int ensure_reader(basis_decoder_t* d, int w, int h) {
    if (d->reader) return 0;
    media_status_t st = AImageReader_newWithUsage(
        w, h, AIMAGE_FORMAT_PRIVATE,
        AHARDWAREBUFFER_USAGE_GPU_SAMPLED_IMAGE, VRING, &d->reader);
    if (st != AMEDIA_OK || !d->reader) { basis_engine_set_error(d->engine, "AImageReader_newWithUsage failed"); return -1; }

    AImageReader_ImageListener listener = { d, on_image };
    AImageReader_setImageListener(d->reader, &listener);
    AImageReader_getWindow(d->reader, &d->window);
    d->vw = w; d->vh = h;
    return 0;
}

/* ---- output draining (push decoded frames to the Surface) --------------- */

static void drain_video_output(basis_decoder_t* d) {
    if (!d->vcodec) return;
    for (;;) {
        AMediaCodecBufferInfo info;
        ssize_t oi = AMediaCodec_dequeueOutputBuffer(d->vcodec, &info, 0);
        if (oi >= 0) {
            d->lastPtsUs = info.presentationTimeUs;
            /* render=true pushes the frame onto the AImageReader Surface */
            AMediaCodec_releaseOutputBuffer(d->vcodec, oi, info.size != 0);
        } else if (oi == AMEDIACODEC_INFO_OUTPUT_FORMAT_CHANGED) {
            AMediaFormat* f = AMediaCodec_getOutputFormat(d->vcodec);
            int32_t w = 0, h = 0;
            AMediaFormat_getInt32(f, AMEDIAFORMAT_KEY_WIDTH, &w);
            AMediaFormat_getInt32(f, AMEDIAFORMAT_KEY_HEIGHT, &h);
            if (w > 0 && h > 0) { d->vw = w; d->vh = h; }
            AMediaFormat_delete(f);
        } else {
            break; /* try again later / no buffer */
        }
    }
}

static void drain_audio_output(basis_decoder_t* d) {
    if (!d->acodec) return;
    for (;;) {
        AMediaCodecBufferInfo info;
        ssize_t oi = AMediaCodec_dequeueOutputBuffer(d->acodec, &info, 0);
        if (oi >= 0) {
            size_t cap = 0;
            uint8_t* buf = AMediaCodec_getOutputBuffer(d->acodec, oi, &cap);
            if (buf && info.size >= 2) {
                int64_t pts = info.presentationTimeUs;
                int frame = d->ach > 0 ? d->ach : (d->pcm.frame > 0 ? d->pcm.frame : 2);
                int srr = d->asr > 0 ? d->asr : 48000;
                if (d->apcm_float) {
                    ring_write(&d->pcm, (const float*)(buf + info.offset), (int)(info.size / 4), pts);
                } else {
                    int n = info.size / 2; /* 16-bit PCM */
                    float tmp[4096];
                    const int16_t* s16 = (const int16_t*)(buf + info.offset);
                    int off = 0;
                    while (off < n) {
                        int chunk = n - off; if (chunk > 4096) chunk = 4096;
                        for (int i = 0; i < chunk; ++i) tmp[i] = s16[off + i] / 32768.0f;
                        ring_write(&d->pcm, tmp, chunk, pts + (int64_t)(off / frame) * 1000000LL / srr);
                        off += chunk;
                    }
                }
            }
            AMediaCodec_releaseOutputBuffer(d->acodec, oi, false);
        } else if (oi == AMEDIACODEC_INFO_OUTPUT_FORMAT_CHANGED) {
            AMediaFormat* f = AMediaCodec_getOutputFormat(d->acodec);
            AMediaFormat_getInt32(f, AMEDIAFORMAT_KEY_SAMPLE_RATE, &d->asr);
            AMediaFormat_getInt32(f, AMEDIAFORMAT_KEY_CHANNEL_COUNT, &d->ach);
            int32_t enc = 2; /* android.media.AudioFormat.ENCODING_PCM_*: 2 = 16-bit (the default when the key is absent), 4 = float */
            AMediaFormat_getInt32(f, "pcm-encoding", &enc);
            d->apcm_float = (enc == 4);
            if (d->ach > 0 && d->ach != d->pcm.frame) ring_set_frame(&d->pcm, d->ach, d->asr);
            __android_log_print(ANDROID_LOG_INFO, "basis_media",
                "audio output format: %d Hz, %d ch, pcm-encoding %d", d->asr, d->ach, (int)enc);
            AMediaFormat_delete(f);
        } else break;
    }
}

/* Ask the audio decoder for the stream's full channel layout: AAC decoders on
 * some devices fold multichannel down to stereo unless configured with an
 * output-channel ceiling. Both the generic (API 32+) and the legacy AAC key
 * are set — unknown keys are ignored, and values above the stream's channel
 * count clamp to it. */
static void request_full_channel_output(AMediaFormat* fmt) {
    AMediaFormat_setInt32(fmt, "max-output-channel-count", 99);
    AMediaFormat_setInt32(fmt, "aac-max-output-channel_count", 99);
}

/* ---- URL path: extractor + worker thread -------------------------------- */

static AMediaCodec* create_codec_for_track(basis_decoder_t* d, AMediaFormat* fmt, int video) {
    const char* mime = NULL;
    if (!AMediaFormat_getString(fmt, AMEDIAFORMAT_KEY_MIME, &mime) || !mime) return NULL;
    AMediaCodec* c = AMediaCodec_createDecoderByType(mime);
    if (!c) return NULL;
    ANativeWindow* surface = NULL;
    if (!video) request_full_channel_output(fmt);
    if (video) {
        int32_t w = 1280, h = 720;
        AMediaFormat_getInt32(fmt, AMEDIAFORMAT_KEY_WIDTH, &w);
        AMediaFormat_getInt32(fmt, AMEDIAFORMAT_KEY_HEIGHT, &h);
        if (ensure_reader(d, w, h) != 0) { AMediaCodec_delete(c); return NULL; }
        surface = d->window;
    }
    if (AMediaCodec_configure(c, fmt, surface, NULL, 0) != AMEDIA_OK) { AMediaCodec_delete(c); return NULL; }
    if (AMediaCodec_start(c) != AMEDIA_OK) { AMediaCodec_delete(c); return NULL; }
    return c;
}

static void feed_extractor_sample(basis_decoder_t* d, AMediaCodec* codec, int track) {
    ssize_t ii = AMediaCodec_dequeueInputBuffer(codec, 0);
    if (ii < 0) return;
    size_t cap = 0;
    uint8_t* buf = AMediaCodec_getInputBuffer(codec, ii, &cap);
    ssize_t sz = AMediaExtractor_readSampleData(d->extractor, buf, cap);
    if (sz < 0) {
        AMediaCodec_queueInputBuffer(codec, ii, 0, 0, 0, AMEDIACODEC_BUFFER_FLAG_END_OF_STREAM);
    } else {
        int64_t pts = AMediaExtractor_getSampleTime(d->extractor);
        AMediaCodec_queueInputBuffer(codec, ii, 0, sz, pts, 0);
        AMediaExtractor_advance(d->extractor);
    }
}

static void* url_worker(void* arg) {
    basis_decoder_t* d = (basis_decoder_t*)arg;
    basis_engine_set_state(d->engine, BASIS_MEDIA_STATE_PLAYING);
    while (basis_engine_is_running(d->engine)) {
        if (basis_engine_is_paused(d->engine)) { usleep(10000); continue; }

        int track = AMediaExtractor_getSampleTrackIndex(d->extractor);
        if (track == d->video_track && d->vcodec) feed_extractor_sample(d, d->vcodec, track);
        else if (track == d->audio_track && d->acodec) feed_extractor_sample(d, d->acodec, track);
        else if (track < 0) { usleep(5000); } /* EOS or none ready */
        else AMediaExtractor_advance(d->extractor);

        drain_video_output(d);
        drain_audio_output(d);
    }
    return NULL;
}

/* ---- internal API ------------------------------------------------------- */

basis_decoder_t* basis_decoder_create(basis_media_engine_t* engine) {
    basis_decoder_t* d = (basis_decoder_t*)calloc(1, sizeof(*d));
    if (!d) return NULL;
    d->engine = engine;
    d->video_track = d->audio_track = -1;
    d->lastPtsUs = -1;
    d->vk = basis_vk_create();
    ring_init(&d->pcm, 48000 * 8 * 4); /* ~4s at 8ch — the PTS-gated serve banks
                                        * mux lead + the jitter cushion in the
                                        * ring, so capacity holds both at full
                                        * width */

    pthread_mutex_init(&d->vm, NULL);
    d->lastPresentedPts = INT64_MIN;
    d->presentedPosUs = -1;
    d->prevWritePts = INT64_MIN;
    d->audClockOffsetUs = INT64_MIN;
    d->bufferUs = 120000;
    d->bufferMode = 1;
    d->audioLatencyUs = 60000; /* ~the tap's DSP-buffer figure until the sink reports */
    return d;
}

void basis_decoder_destroy(basis_decoder_t* d) {
    if (!d) return;
    if (d->worker_started) pthread_join(d->worker, NULL);
    basis_decoder_render_release(d);
    if (d->vcodec) { AMediaCodec_stop(d->vcodec); AMediaCodec_delete(d->vcodec); }
    if (d->acodec) { AMediaCodec_stop(d->acodec); AMediaCodec_delete(d->acodec); }
    if (d->extractor) AMediaExtractor_delete(d->extractor);
    /* Release any frames still held in the video ring before the reader they
     * belong to (the worker is already joined, so nothing enqueues concurrently). */
    for (int i = 0; i < VRING; ++i) if (d->vimg[i]) { AImage_delete(d->vimg[i]); d->vimg[i] = NULL; }
    if (d->reader) AImageReader_delete(d->reader);
    if (d->vk) basis_vk_destroy(d->vk);
    pthread_mutex_destroy(&d->vm);
    ring_free(&d->pcm);
    free(d->lpcmBuf);
    free(d);
}

int basis_decoder_try_open_url(basis_decoder_t* d, const char* url) {
    /* Android: try AMediaExtractor first (HW demux of seekable containers).
     * On failure (live TS, HLS/DASH manifest, etc.) return 0 so the core falls
     * back to basis_jni_https + the portable basis_ts/basis_mp4 demuxer. */
    d->extractor = AMediaExtractor_new();
    media_status_t st = AMediaExtractor_setDataSource(d->extractor, url);
    if (st != AMEDIA_OK) {
        __android_log_print(ANDROID_LOG_INFO, "basis_media",
            "AMediaExtractor rejected url (code %d); falling back to JNI HTTPS + TS/MP4 demuxer", (int)st);
        AMediaExtractor_delete(d->extractor); d->extractor = NULL;
        return 0;
    }
    size_t n = AMediaExtractor_getTrackCount(d->extractor);
    for (size_t i = 0; i < n; ++i) {
        AMediaFormat* f = AMediaExtractor_getTrackFormat(d->extractor, i);
        const char* mime = NULL;
        AMediaFormat_getString(f, AMEDIAFORMAT_KEY_MIME, &mime);
        if (mime && strncmp(mime, "video/", 6) == 0 && d->video_track < 0) {
            d->video_track = (int)i;
            AMediaExtractor_selectTrack(d->extractor, i);
            d->vcodec = create_codec_for_track(d, f, 1);
        } else if (mime && strncmp(mime, "audio/", 6) == 0 && d->audio_track < 0) {
            d->audio_track = (int)i;
            AMediaExtractor_selectTrack(d->extractor, i);
            AMediaFormat_getInt32(f, AMEDIAFORMAT_KEY_SAMPLE_RATE, &d->asr);
            AMediaFormat_getInt32(f, AMEDIAFORMAT_KEY_CHANNEL_COUNT, &d->ach);
            if (d->ach > 0) ring_set_frame(&d->pcm, d->ach, d->asr);
            d->acodec = create_codec_for_track(d, f, 0);
            d->aconfigured = 1;
        }
        AMediaFormat_delete(f);
    }
    if (!d->vcodec) {
        /* No decodable video: audio-only containers (WAV, audio-only MP4) go to
         * the portable demuxers instead, which share the audio-only state
         * handling (PLAYING / unsupported-format error) with the other
         * platforms. Undo whatever the track scan configured and decline the
         * URL so the core falls back. */
        __android_log_print(ANDROID_LOG_INFO, "basis_media",
            "AMediaExtractor found no decodable video track; falling back to JNI HTTPS + portable demuxers");
        if (d->acodec) { AMediaCodec_delete(d->acodec); d->acodec = NULL; }
        d->aconfigured = 0;
        d->asr = 0; d->ach = 0;
        d->video_track = d->audio_track = -1;
        AMediaExtractor_delete(d->extractor); d->extractor = NULL;
        return 0;
    }
    d->vconfigured = 1;

    pthread_create(&d->worker, NULL, url_worker, d);
    d->worker_started = 1;
    return 1; /* took ownership of the URL */
}

int basis_decoder_set_video_format(basis_decoder_t* d, basis_codec_t codec,
                                   const uint8_t* extradata, int extradata_len, int w, int h) {
    if (!d || d->vconfigured) return 0;
    d->vc = codec; if (w > 0) d->vw = w; if (h > 0) d->vh = h;
    const char* mime = (codec == BASIS_CODEC_H265) ? "video/hevc" : "video/avc";

    if (ensure_reader(d, d->vw ? d->vw : 1280, d->vh ? d->vh : 720) != 0) return -1;

    AMediaFormat* fmt = AMediaFormat_new();
    AMediaFormat_setString(fmt, AMEDIAFORMAT_KEY_MIME, mime);
    AMediaFormat_setInt32(fmt, AMEDIAFORMAT_KEY_WIDTH, d->vw ? d->vw : 1280);
    AMediaFormat_setInt32(fmt, AMEDIAFORMAT_KEY_HEIGHT, d->vh ? d->vh : 720);
    if (extradata && extradata_len > 0)
        AMediaFormat_setBuffer(fmt, "csd-0", (void*)extradata, extradata_len); /* Annex-B SPS/PPS(/VPS) */

    AMediaCodec* c = AMediaCodec_createDecoderByType(mime);
    if (!c || AMediaCodec_configure(c, fmt, d->window, NULL, 0) != AMEDIA_OK ||
        AMediaCodec_start(c) != AMEDIA_OK) {
        if (c) AMediaCodec_delete(c);
        AMediaFormat_delete(fmt);
        basis_engine_set_error(d->engine, "Android: video AMediaCodec configure/start failed");
        return -1;
    }
    d->vcodec = c;
    AMediaFormat_delete(fmt);
    d->vconfigured = 1;
    basis_engine_set_state(d->engine, BASIS_MEDIA_STATE_PLAYING);
    return 0;
}

int basis_decoder_set_audio_format(basis_decoder_t* d, basis_codec_t codec,
                                   int sample_rate, int channels, const uint8_t* asc, int asc_len) {
    if (!d || d->aconfigured) return 0;

    if (codec == BASIS_CODEC_LPCM) {
        /* Decoder bypass, mirroring the Windows lane: no MediaCodec involved —
         * submit_audio converts straight into the ring. The config blob carries
         * the channel-assignment + bits codes, plus an optional flags byte:
         * bit0 = little-endian WAVE-order samples (the RIFF/WAV lane, played at
         * the file rate — the splitter resamples). Blu-ray TS (2-byte config,
         * big-endian) stays 48 kHz only; the TS demuxer pre-filters, this is
         * the matching backstop. 16- or 24-bit only either way. */
        if (channels < 1 || channels > 8 || !asc || asc_len < 2) return 0;
        int le = asc_len >= 3 && (asc[2] & 1);
        if (le ? (sample_rate < 8000 || sample_rate > 96000) : (sample_rate != 48000)) return 0;
        int bits = asc[1] == 1 ? 16 : asc[1] == 3 ? 24 : 0;
        if (!bits) return 0; /* 20-bit unsupported */
        d->ac = BASIS_CODEC_LPCM;
        d->asr = sample_rate; d->ach = channels;
        d->aLpcmAssign = asc[0];
        d->aLpcmBits = bits;
        d->aLpcmLE = le;
        ring_set_frame(&d->pcm, channels, sample_rate);
        d->aconfigured = 1;
        return 0;
    }

    if (codec != BASIS_CODEC_AAC) return 0;
    d->ac = BASIS_CODEC_AAC;
    d->asr = sample_rate; d->ach = channels;
    ring_set_frame(&d->pcm, channels ? channels : 2, sample_rate);
    AMediaFormat* fmt = AMediaFormat_new();
    AMediaFormat_setString(fmt, AMEDIAFORMAT_KEY_MIME, "audio/mp4a-latm");
    AMediaFormat_setInt32(fmt, AMEDIAFORMAT_KEY_SAMPLE_RATE, sample_rate ? sample_rate : 48000);
    AMediaFormat_setInt32(fmt, AMEDIAFORMAT_KEY_CHANNEL_COUNT, channels ? channels : 2);
    request_full_channel_output(fmt);
    /* AAC frames reach ~8 KB (13-bit ADTS length); the default input buffer was
     * smaller, so large 5.1 frames were fed truncated and the decoder rejected
     * them (0x4004 -> silence). Give it headroom so whole multichannel frames fit. */
    AMediaFormat_setInt32(fmt, "max-input-size", 32768);
    if (asc && asc_len > 0) AMediaFormat_setBuffer(fmt, "csd-0", (void*)asc, asc_len);
    AMediaCodec* c = AMediaCodec_createDecoderByType("audio/mp4a-latm");
    if (!c || AMediaCodec_configure(c, fmt, NULL, NULL, 0) != AMEDIA_OK ||
        AMediaCodec_start(c) != AMEDIA_OK) {
        if (c) AMediaCodec_delete(c);
        AMediaFormat_delete(fmt);
        d->aconfigured = 1;
        return -1;
    }
    d->acodec = c;
    AMediaFormat_delete(fmt);
    d->aconfigured = 1;
    return 0;
}

int basis_decoder_submit_video(basis_decoder_t* d, const uint8_t* annexb, int len, int64_t pts_us, int key) {
    (void)key;
    if (!d || !d->vcodec) return -1;
    ssize_t ii = AMediaCodec_dequeueInputBuffer(d->vcodec, 2000);
    if (ii >= 0) {
        size_t cap = 0;
        uint8_t* buf = AMediaCodec_getInputBuffer(d->vcodec, ii, &cap);
        int n = len < (int)cap ? len : (int)cap;
        memcpy(buf, annexb, n);
        AMediaCodec_queueInputBuffer(d->vcodec, ii, 0, n, pts_us, 0);
    }
    drain_video_output(d);
    return 0;
}

/* Source-order -> WAVE-order channel map for the Blu-ray HDMV LPCM
 * channel_assignment values whose stream order differs from WAVE (Blu-ray
 * places the LFE last and the side pair before the rears). Same tables as the
 * Windows lane, which match ffmpeg's pcm_bluray remap for assignments 9 (5.1),
 * 10 (7.0) and 11 (7.1) and were verified by ear against a 7.1 channel-marker
 * stream. NULL = identity (mono/stereo/3.0/4.0/5.0 already arrive in WAVE
 * order). */
static const int* lpcm_remap(int assign) {
    static const int k51[6] = { 0, 1, 2, 4, 5, 3 };
    static const int k70[7] = { 0, 1, 2, 5, 3, 4, 6 };
    static const int k71[8] = { 0, 1, 2, 6, 4, 5, 7, 3 };
    if (assign == 9) return k51;
    if (assign == 10) return k70;
    if (assign == 11) return k71;
    return NULL;
}

/* LPCM bypass: big-endian 16/24-bit Blu-ray-order PCM -> interleaved WAVE-order
 * float, straight into the ring. Whole frames only, so the ring keeps its
 * channel phase (the alignment contract in the ring comment above). */
static void submit_lpcm(basis_decoder_t* d, const uint8_t* p, int len, int64_t pts_us) {
    int ch = d->ach;
    int bytes = d->aLpcmBits / 8;
    int frame_bytes = ch * bytes;
    int frames = frame_bytes > 0 ? len / frame_bytes : 0;
    if (frames <= 0) return;
    int floats = frames * ch;
    if (floats > d->lpcmBufCap) {
        float* nb = (float*)realloc(d->lpcmBuf, sizeof(float) * (size_t)floats);
        if (!nb) return;
        d->lpcmBuf = nb; d->lpcmBufCap = floats;
    }
    const int* map = lpcm_remap(d->aLpcmAssign);
    for (int f = 0; f < frames; ++f) {
        const uint8_t* s = p + f * frame_bytes;
        float* o = d->lpcmBuf + f * ch;
        for (int c = 0; c < ch; ++c) {
            int oc = map ? map[c] : c;
            if (bytes == 2) {
                int v = d->aLpcmLE ? (int16_t)(s[c * 2] | (s[c * 2 + 1] << 8))
                                   : (int16_t)((s[c * 2] << 8) | s[c * 2 + 1]);
                o[oc] = v / 32768.0f;
            } else {
                int v = d->aLpcmLE ? ((s[c * 3 + 2] << 16) | (s[c * 3 + 1] << 8) | s[c * 3])
                                   : ((s[c * 3] << 16) | (s[c * 3 + 1] << 8) | s[c * 3 + 2]);
                if (v & 0x800000) v -= 0x1000000;
                o[oc] = v / 8388608.0f;
            }
        }
    }
    ring_write(&d->pcm, d->lpcmBuf, floats, pts_us);
}

int basis_decoder_submit_audio(basis_decoder_t* d, const uint8_t* data, int len, int64_t pts_us) {
    if (!d || !data || len <= 0) return -1;
    if (d->ac == BASIS_CODEC_LPCM) { submit_lpcm(d, data, len, pts_us); return 0; }
    if (!d->acodec) return -1;
    ssize_t ii = AMediaCodec_dequeueInputBuffer(d->acodec, 2000);
    if (ii >= 0) {
        size_t cap = 0;
        uint8_t* buf = AMediaCodec_getInputBuffer(d->acodec, ii, &cap);
        if ((size_t)len <= cap) {
            memcpy(buf, data, (size_t)len);
            AMediaCodec_queueInputBuffer(d->acodec, ii, 0, len, pts_us, 0);
        } else {
            /* Never feed a partial frame — it decodes to an error + silence.
             * max-input-size should prevent this; return the buffer empty if not. */
            AMediaCodec_queueInputBuffer(d->acodec, ii, 0, 0, pts_us, 0);
        }
    }
    drain_audio_output(d);
    return 0;
}

/* ---- render thread + accessors ----------------------------------------- */

/* Runs the presentation clock and hands the due frame's hardware buffer to the
 * Vulkan present. Mirrors the render-thread clock in basis_win_decode.cpp: a
 * wall-rate clock slewed toward the decode edge with a capped correction rate,
 * clamped at edge + buffer so a stall can't run it ahead, a jitter buffer
 * behind the edge that doubles as the audio bank (the PCM serve is gated to
 * this same clock), and the audio-gate offset published for the ring. Render
 * thread. */
static void present_select(basis_decoder_t* d) {
    pthread_mutex_lock(&d->vm);

    int64_t newest = INT64_MIN;
    for (int i = 0; i < VRING; ++i) if (d->vimg[i] && d->vpts[i] > newest) newest = d->vpts[i];

    int paced = basis_engine_is_paced(d->engine) != 0;

    /* Audio-first start (live): with no decodable video yet — a mid-GOP join
     * waits for the next IDR, up to a full GOP — run the presentation clock
     * from the audio delivery edge instead, so audio plays immediately and
     * video joins the already-running clock when its first frame decodes
     * (both tracks share a timeline, so joining needs no re-anchor). The
     * audio edge stands in for `newest` below; the present loop no-ops on an
     * empty frame ring. VOD keeps the primed, synchronised start, and an
     * audio-only stream (video never configured) keeps its ungated serve. */
    int noVideoYet = (newest == INT64_MIN);
    if (noVideoYet) {
        if (!d->vconfigured || !d->aconfigured || paced) { pthread_mutex_unlock(&d->vm); return; }
        newest = ring_newest_pts(&d->pcm);
        if (newest == INT64_MIN) { pthread_mutex_unlock(&d->vm); return; }
    }

    int64_t nowq = now_monotonic_us();
    int64_t interval = d->frameIntervalUs > 0 ? d->frameIntervalUs : 16666;

    /* Hold sizes: with audio, the jitter cushion both streams play behind —
     * the audio serve is gated to the same clock, so the video hold is also
     * the audio bank that absorbs delivery burst/starve cycles. Capped to the
     * ring's frame span so the decoder can't lap the presenter. */
    int64_t pacedBuf = d->aconfigured ? 460000 : 250000;
    {
        int64_t ringSpanCap = (int64_t)(VRING - 6) * interval;
        if (pacedBuf > ringSpanCap) pacedBuf = ringSpanCap;
    }

    /* VOD prime: hold presentation until the ring has banked a hold's worth
     * of frames (3s fallback), so a start against struggling delivery buffers
     * first instead of presenting the first frame, starving, and churning
     * through resyncs. */
    if (paced && !d->clockStarted) {
        if (!d->primeStartUs) d->primeStartUs = nowq;
        int held = 0;
        for (int i = 0; i < VRING; ++i) if (d->vimg[i]) held++;
        if ((int64_t)held * interval < pacedBuf + 2 * interval && nowq - d->primeStartUs < 3000000) {
            pthread_mutex_unlock(&d->vm);
            return;
        }
    }

    if (!d->clockStarted) {
        d->clockStarted = 1;
        d->wallStartUs = nowq;
        d->lastRenderUs = nowq;
        d->mediaStartUs = newest;
        d->lastPresentedPts = INT64_MIN;
    }
    int64_t dtUs = nowq - d->lastRenderUs;
    d->lastRenderUs = nowq;
    if (dtUs < 0) dtUs = 0; else if (dtUs > 1000000) dtUs = 1000000;
    if (dtUs > 1000 && dtUs < 100000) d->renderTickUs += (dtUs - d->renderTickUs) / 8;
    int64_t wallElapsed = nowq - d->wallStartUs;

    /* Presentation clock: wall-rate advance, slewed toward the decode edge
     * with a capped correction rate — 50% during the first ~1.2s after an
     * anchor, ~2% after — so burst error is absorbed by the jitter buffer
     * instead of being chased in slow/fast swings. Clamped at edge + buffer
     * so a delivery stall can't run it ahead (resume would dump the backlog
     * in a skip burst). Large gaps hard-resync; the paced forward threshold
     * scales with the ring span so startup fill is slewed away, not chased. */
    int64_t nowMedia;
    int64_t clk = d->mediaStartUs + wallElapsed;
    int64_t err = newest - clk;
    if (paced) {
        int64_t posLimit = (int64_t)(VRING - 4) * interval;
        if (posLimit < 1000000) posLimit = 1000000;
        if (err > posLimit || err < -1000000) {
            d->wallStartUs = nowq; d->mediaStartUs = newest; d->lastPresentedPts = INT64_MIN;
            clk = newest; wallElapsed = 0;
        } else {
            int64_t corr = err * dtUs / 250000;
            int64_t cap = dtUs / 50;
            if (corr > cap) corr = cap; else if (corr < -cap) corr = -cap;
            d->mediaStartUs += corr; clk += corr;
        }
        int64_t edgeMax = newest + pacedBuf;
        if (clk > edgeMax) { d->mediaStartUs -= clk - edgeMax; clk = edgeMax; }
        nowMedia = clk - pacedBuf;
    } else {
        if (err > 700000 || err < -700000) {
            d->wallStartUs = nowq; d->mediaStartUs = newest; d->lastPresentedPts = INT64_MIN;
            clk = newest; wallElapsed = 0;
        } else {
            int64_t corr = err * dtUs / 250000;
            int64_t cap = (wallElapsed < 1200000) ? dtUs / 2 : dtUs / 50;
            if (corr > cap) corr = cap; else if (corr < -cap) corr = -cap;
            d->mediaStartUs += corr; clk += corr;
        }
        int64_t edgeMax = newest + d->bufferUs;
        if (clk > edgeMax) { d->mediaStartUs -= clk - edgeMax; clk = edgeMax; }

        /* Jitter buffer: capped to the ring span; dynamic mode grows on
         * underrun risk and shrinks when over-buffered. With audio the floor
         * is the shared cushion that banks audio in the ring. */
        int64_t maxBuf = (int64_t)(VRING - 6) * interval; if (maxBuf < 60000) maxBuf = 60000;
        int64_t buf = d->bufferUs;
        int64_t fillUs = newest - (clk - buf);
        if (d->bufferMode == 1) {
            if (fillUs < 2 * interval) buf += interval;
            else if (fillUs > buf + 200000) buf -= 10000;
        }
        int64_t minBuf = d->aconfigured ? 460000 : 40000;
        if (buf < minBuf) buf = minBuf;
        if (buf > maxBuf) buf = maxBuf;
        d->bufferUs = (int)buf;

        /* Fast start (video-only): ramp the cushion so the first frame shows
         * almost immediately. With audio the start is synchronised on the
         * full buffer instead — a sub-1x clock during the ramp would force
         * the PTS-gated audio serve to under-fill every block. */
        int64_t effBuf = (!d->aconfigured && wallElapsed < 1200000) ? (buf * wallElapsed / 1200000) : buf;
        nowMedia = clk - effBuf;
    }
    d->dbg_lagms = (long)((newest - nowMedia) / 1000);

    /* Publish the audio-gate clock as an offset from the monotonic clock. Live
     * low-passes (~2s) to absorb the segment-cadence wobble of the edge lock;
     * paced publishes directly. Large jumps snap so the gate follows resyncs. */
    {
        int64_t off = nowMedia - nowq;
        int64_t prev = __atomic_load_n(&d->audClockOffsetUs, __ATOMIC_RELAXED);
        if (paced || prev == INT64_MIN || off - prev > 700000 || off - prev < -700000)
            __atomic_store_n(&d->audClockOffsetUs, off, __ATOMIC_RELAXED);
        else
            __atomic_store_n(&d->audClockOffsetUs, prev + (off - prev) * dtUs / 2000000, __ATOMIC_RELAXED);
    }

    /* recover from a non-monotonic/bogus PTS leaving lastPresentedPts stuck */
    if (d->lastPresentedPts != INT64_MIN && d->lastPresentedPts > newest) d->lastPresentedPts = INT64_MIN;

    /* Present the latest frame that is due and newer than the last shown; then
     * delete every frame at or before it (consumed), keeping the future ones
     * queued. The due check looks ahead half a render tick so a frame lands on
     * the tick nearest its due time, not the tick after it (due times drift
     * through the tick phase whenever the source rate doesn't divide the
     * refresh rate); capped at half the source frame period so a high-rate
     * source can't be shown a whole frame early. The edge clamp above makes a
     * stalled clock impossible, so no forced-present guard is needed: the ring
     * drains through normal due presents as the clock reaches them. */
    int64_t lookahead = d->renderTickUs / 2;
    if (lookahead > interval / 2) lookahead = interval / 2;
    int64_t dueBy = nowMedia + lookahead;

    int best = -1; int64_t bestPts = d->lastPresentedPts;
    for (int i = 0; i < VRING; ++i)
        if (d->vimg[i] && d->vpts[i] <= dueBy && d->vpts[i] > bestPts) { bestPts = d->vpts[i]; best = i; }

    if (best < 0) {
        d->dbg_nodue++;
        pthread_mutex_unlock(&d->vm);
        return;
    }

    AHardwareBuffer* ahb = NULL;
    int fw = d->vfw[best], fh = d->vfh[best];
    if (AImage_getHardwareBuffer(d->vimg[best], &ahb) == AMEDIA_OK && ahb && d->vk)
        basis_vk_set_hardware_buffer(d->vk, ahb, fw, fh); /* present acquires its own ref */

    d->lastPresentedPts = bestPts;
    __atomic_store_n(&d->presentedPosUs, bestPts, __ATOMIC_RELAXED);
    d->dbg_render++;

    for (int i = 0; i < VRING; ++i)
        if (d->vimg[i] && d->vpts[i] <= bestPts) { AImage_delete(d->vimg[i]); d->vimg[i] = NULL; }

    pthread_mutex_unlock(&d->vm);
}

int basis_decoder_render_update(basis_decoder_t* d) {
    if (!d || !d->vk) return -1;
    if (basis_engine_is_paused(d->engine)) return 0;
    present_select(d);
    return basis_vk_render_update(d->vk);
}
void basis_decoder_render_release(basis_decoder_t* d) { if (d && d->vk) basis_vk_release(d->vk); }

void* basis_decoder_get_texture(basis_decoder_t* d, int* w, int* h) {
    if (!d || !d->vk) { if (w) *w = 0; if (h) *h = 0; return NULL; }
    uint64_t img = basis_vk_get_image(d->vk, w, h);
    return (void*)(uintptr_t)img;
}
uint64_t basis_decoder_get_frame_counter(basis_decoder_t* d) { return d && d->vk ? basis_vk_frame_counter(d->vk) : 0; }
int basis_decoder_get_video_size(basis_decoder_t* d, int* w, int* h) {
    if (!d || d->vw <= 0) return -1; if (w) *w = d->vw; if (h) *h = d->vh; return 0;
}
/* The Vulkan resolve always flips to upright via a negative-height viewport, so
 * the published frame is bottom-left origin on every Android GPU. */
int basis_decoder_get_frame_origin(basis_decoder_t* d) { (void)d; return 0; }
/* Presentation position once a frame has shown; decode edge before that
 * (start-up, audio-only) so early consumers still see the clock move. */
int64_t basis_decoder_get_position_us(basis_decoder_t* d) {
    if (!d) return -1;
    int64_t presented = __atomic_load_n(&d->presentedPosUs, __ATOMIC_RELAXED);
    return presented >= 0 ? presented : d->lastPtsUs;
}
int basis_decoder_get_audio_format(basis_decoder_t* d, int* r, int* c) {
    if (!d || !d->aconfigured) return -1; if (r) *r = d->asr ? d->asr : 48000; if (c) *c = d->ach ? d->ach : 2; return 0;
}
int basis_decoder_read_audio(basis_decoder_t* d, float* out, int max) {
    if (!d) return 0;
    if (basis_engine_is_paused(d->engine)) return 0;
    /* Reconstruct the presentation clock from the published offset and serve
     * against it, biased forward by the sink's output latency so release-now
     * lands on the clock at the speaker. Before the clock exists, a stream
     * with video holds audio — on live that is only until the next render
     * tick bootstraps the clock from the audio edge (audio-first start; on
     * VOD, until the prime releases), so playout can never free-run on a
     * timeline the clock won't match. Audio-only streams read ungated. */
    int64_t target = INT64_MIN;
    int64_t off = __atomic_load_n(&d->audClockOffsetUs, __ATOMIC_RELAXED);
    if (off != INT64_MIN) {
        target = now_monotonic_us() + off + d->audioLatencyUs;
    } else if (d->vconfigured) {
        return 0;
    }
    /* Hysteresis must exceed the sink's pull depth (it drains several DSP
     * blocks back-to-back); the reported output latency is that depth plus
     * headroom, so size the hold from it. */
    int64_t hold = 60000 + d->audioLatencyUs;
    return ring_read(&d->pcm, out, max, target, hold);
}
int basis_decoder_get_debug(basis_decoder_t* d, char* buf, int size) {
    if (!d || !buf || size <= 0) return 0;
    int vq = 0;
    pthread_mutex_lock(&d->vm);
    for (int i = 0; i < VRING; ++i) if (d->vimg[i]) vq++;
    pthread_mutex_unlock(&d->vm);
    int aq = ring_fill_ms(&d->pcm);
    int srr = d->pcm.sr > 0 ? d->pcm.sr : 48000;
    int frm = d->pcm.frame > 0 ? d->pcm.frame : 2;
    int atrimms = (int)((int64_t)(d->pcm.lastTrimFloats / frm) * 1000 / srr);
    /* vq = video frames held; aq = audio queued (ms); atrim = clock-gated trims
     * fired; lag = live edge minus present clock. Video keys (render/nodue/lag/
     * buf/mode/acq) are also parsed into the diagnostics CSV. */
    return snprintf(buf, (size_t)size,
                    "render=%ld nodue=%ld acq=%ld drop=%ld lag=%ldms buf=%dms mode=%d vq=%d aq=%dms atrim=%ld atrimms=%d alat=%dms | acfg=%d asr=%d ach=%d vw=%d vh=%d",
                    d->dbg_render, d->dbg_nodue, d->dbg_acqfail, d->dbg_drop, d->dbg_lagms,
                    d->bufferUs / 1000, d->bufferMode, vq, aq, d->pcm.trims, atrimms, d->audioLatencyUs / 1000,
                    d->aconfigured, d->asr, d->ach, d->vw, d->vh);
}
void basis_decoder_set_buffer(basis_decoder_t* d, int mode, int ms) {
    if (!d) return;
    d->bufferMode = (mode != 0) ? 1 : 0;
    if (ms > 0) d->bufferUs = ms * 1000;
}

/* The managed audio sink reports its measured output latency; it biases the
 * audio serve target forward so samples released now come due exactly when
 * they reach the speaker. Clamped to a sane range. */
void basis_decoder_set_audio_latency(basis_decoder_t* d, int latency_us) {
    if (!d) return;
    if (latency_us < 0) latency_us = 0;
    else if (latency_us > 500000) latency_us = 500000;
    d->audioLatencyUs = latency_us;
}

void basis_decoder_set_output_texture(basis_decoder_t* d, void* native_texture, int w, int h) {
    if (d && d->vk) basis_vk_set_output_texture(d->vk, native_texture, w, h);
}
