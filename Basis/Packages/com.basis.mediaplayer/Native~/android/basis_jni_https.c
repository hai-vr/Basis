/*
 * basis_jni_https.c — bridges to java.net.URL/HttpsURLConnection so the portable
 * MPEG-TS / fMP4 demuxers can read HTTPS streams on Android. See header for the
 * contract and why this exists.
 *
 * Method/class IDs are cached at JNI_OnLoad (the only place the harness reliably
 * gives us the *system* class loader — FindClass from arbitrary threads can hit
 * the calling thread's class loader and miss app classes; for java.net types it
 * works either way, but caching avoids the per-read FindClass cost).
 */

#include "basis_jni_https.h"
#include "../protocol/basis_io.h"

#include <jni.h>
#include <android/log.h>
#include <pthread.h>
#include <stdlib.h>
#include <string.h>
#include <strings.h>
#include <stdio.h>
#include <errno.h>

#define LOG_TAG "basis_media"
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, LOG_TAG, __VA_ARGS__)
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO,  LOG_TAG, __VA_ARGS__)

static JavaVM* g_jvm = NULL;

static struct {
    jclass  url_cls;            /* java/net/URL                                  */
    jmethodID url_ctor;         /* URL(String)                                   */
    jmethodID url_ctor_rel;     /* URL(URL, String) — resolves a relative spec   */
    jmethodID url_open;         /* openConnection() -> URLConnection             */
    jmethodID url_protocol;     /* getProtocol() -> String                       */
    jmethodID url_host;         /* getHost() -> String                           */

    jclass  conn_cls;           /* java/net/URLConnection                        */
    jmethodID conn_set_ct;      /* setConnectTimeout(int)                        */
    jmethodID conn_set_rt;      /* setReadTimeout(int)                           */
    jmethodID conn_set_req;     /* setRequestProperty(String, String)            */
    jmethodID conn_connect;     /* connect()                                     */
    jmethodID conn_get_is;      /* getInputStream() -> InputStream               */
    jmethodID conn_get_hdr;     /* getHeaderField(String) -> String              */

    jclass  http_conn_cls;      /* java/net/HttpURLConnection                    */
    jmethodID http_set_follow;  /* setInstanceFollowRedirects(boolean)           */
    jmethodID http_get_code;    /* getResponseCode() -> int                      */
    jmethodID http_disconnect;  /* disconnect()                                  */

    jclass  is_cls;             /* java/io/InputStream                           */
    jmethodID is_read;          /* read(byte[], int, int) -> int                 */
    jmethodID is_close;         /* close()                                       */
} g_ids;

static int g_init_ok = 0;

JNIEXPORT jint JNICALL JNI_OnLoad(JavaVM* vm, void* reserved) {
    (void)reserved;
    g_jvm = vm;

    JNIEnv* env = NULL;
    if ((*vm)->GetEnv(vm, (void**)&env, JNI_VERSION_1_6) != JNI_OK) return JNI_ERR;

    jclass url        = (*env)->FindClass(env, "java/net/URL");
    jclass conn       = (*env)->FindClass(env, "java/net/URLConnection");
    jclass httpconn   = (*env)->FindClass(env, "java/net/HttpURLConnection");
    jclass is         = (*env)->FindClass(env, "java/io/InputStream");
    if (!url || !conn || !httpconn || !is) {
        if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
        return JNI_VERSION_1_6;
    }

    g_ids.url_cls       = (jclass)(*env)->NewGlobalRef(env, url);
    g_ids.conn_cls      = (jclass)(*env)->NewGlobalRef(env, conn);
    g_ids.http_conn_cls = (jclass)(*env)->NewGlobalRef(env, httpconn);
    g_ids.is_cls        = (jclass)(*env)->NewGlobalRef(env, is);

    g_ids.url_ctor       = (*env)->GetMethodID(env, g_ids.url_cls, "<init>", "(Ljava/lang/String;)V");
    g_ids.url_ctor_rel   = (*env)->GetMethodID(env, g_ids.url_cls, "<init>", "(Ljava/net/URL;Ljava/lang/String;)V");
    g_ids.url_open       = (*env)->GetMethodID(env, g_ids.url_cls, "openConnection", "()Ljava/net/URLConnection;");
    g_ids.url_protocol   = (*env)->GetMethodID(env, g_ids.url_cls, "getProtocol", "()Ljava/lang/String;");
    g_ids.url_host       = (*env)->GetMethodID(env, g_ids.url_cls, "getHost", "()Ljava/lang/String;");
    g_ids.conn_set_ct    = (*env)->GetMethodID(env, g_ids.conn_cls, "setConnectTimeout", "(I)V");
    g_ids.conn_set_rt    = (*env)->GetMethodID(env, g_ids.conn_cls, "setReadTimeout", "(I)V");
    g_ids.conn_set_req   = (*env)->GetMethodID(env, g_ids.conn_cls, "setRequestProperty", "(Ljava/lang/String;Ljava/lang/String;)V");
    g_ids.conn_connect   = (*env)->GetMethodID(env, g_ids.conn_cls, "connect", "()V");
    g_ids.conn_get_is    = (*env)->GetMethodID(env, g_ids.conn_cls, "getInputStream", "()Ljava/io/InputStream;");
    g_ids.conn_get_hdr   = (*env)->GetMethodID(env, g_ids.conn_cls, "getHeaderField", "(Ljava/lang/String;)Ljava/lang/String;");
    g_ids.http_set_follow= (*env)->GetMethodID(env, g_ids.http_conn_cls, "setInstanceFollowRedirects", "(Z)V");
    g_ids.http_get_code  = (*env)->GetMethodID(env, g_ids.http_conn_cls, "getResponseCode", "()I");
    g_ids.http_disconnect= (*env)->GetMethodID(env, g_ids.http_conn_cls, "disconnect", "()V");
    g_ids.is_read        = (*env)->GetMethodID(env, g_ids.is_cls, "read", "([BII)I");
    g_ids.is_close       = (*env)->GetMethodID(env, g_ids.is_cls, "close", "()V");

    if ((*env)->ExceptionCheck(env)) {
        (*env)->ExceptionClear(env);
        return JNI_VERSION_1_6; /* leave g_init_ok == 0 — open() will refuse */
    }

    g_init_ok = 1;
    return JNI_VERSION_1_6;
}

/* ---- helpers ------------------------------------------------------------ */

typedef struct {
    JNIEnv* env;
    int     attached;
} jenv_lease;

static int jenv_acquire(jenv_lease* lease) {
    lease->env = NULL;
    lease->attached = 0;
    if (!g_jvm) return -1;
    jint rc = (*g_jvm)->GetEnv(g_jvm, (void**)&lease->env, JNI_VERSION_1_6);
    if (rc == JNI_OK) return 0;
    if (rc == JNI_EDETACHED) {
        if ((*g_jvm)->AttachCurrentThread(g_jvm, &lease->env, NULL) != JNI_OK) return -1;
        lease->attached = 1;
        return 0;
    }
    return -1;
}

static void jenv_release(jenv_lease* lease) {
    if (lease->attached && g_jvm) (*g_jvm)->DetachCurrentThread(g_jvm);
}

static void log_and_clear_pending(JNIEnv* env, const char* where) {
    if ((*env)->ExceptionCheck(env)) {
        (*env)->ExceptionDescribe(env);
        (*env)->ExceptionClear(env);
        LOGE("basis_jni_https: %s: java exception (cleared)", where);
    }
}

/* ---- context ------------------------------------------------------------ */

/* `conn` and `is` are shared between the read-ahead reader thread and the demux
 * thread, which disconnects and re-creates them for a reseek. Deleting a global
 * ref the reader is still calling through is a use-after-free in the JVM's ref
 * table, so `lock` covers the fields, `inflight` counts readers holding a copy,
 * and `idle` lets a detaching thread wait them out before anything is deleted.
 * Mirrors the WinHTTP source's handling of its request handle. */
typedef struct {
    jobject conn;       /* global ref: HttpURLConnection                    */
    jobject is;         /* global ref: InputStream                          */
    /* Single-reader only. `inflight` is a counter because a claim is cheaper to
     * count than to prove unique, but this buffer is shared and unsynchronised
     * (ensure_scratch mutates it and scratch_cap outside `lock`), so two
     * concurrent readers would overwrite each other. Exactly one thread reads
     * today: the read-ahead reader, or the demux thread when read-ahead is off.
     * Give each reader its own scratch before adding a second. */
    jbyteArray scratch; /* global ref: reusable byte[scratch_cap]           */
    pthread_mutex_t lock;
    pthread_cond_t idle;
    int inflight;
    int scratch_cap;
    int eof;
    int seekable;       /* finite, byte-range-fetchable body (VOD detect)   */
    int range_ok;       /* probe answered 206 — ranged re-request honoured  */
    long long total_bytes;   /* read cursor (absolute offset); reset on reseek   */
    long long content_length;/* HTTP body size, captured once at open; -1 unknown */
    char* url;          /* kept for ranged re-requests (reseek)             */
    int timeout_ms;
} https_ctx;

/* Drop a reader's claim on the stream. `set_eof` records end-of-stream, but only
 * for the response still installed — a read that raced a reseek must not mark the
 * new one finished. Returns non-zero when the claim went stale, i.e. the caller's
 * bytes belong to a response that has been replaced. */
static int https_release_inflight_advance(https_ctx* h, jobject is, int set_eof, int advance) {
    pthread_mutex_lock(&h->lock);
    int stale = (h->is != is);
    if (!stale) {
        if (set_eof) h->eof = 1;
        /* Inside the same critical section as the staleness test on purpose.
         * Dropping the claim first would let a reseek waiting on `idle` wake,
         * install its stream and set the cursor to the new offset, only for this
         * thread to add its byte count on top — leaving the cursor reporting a
         * position the stream is not at. It only feeds the diagnostics, which is
         * exactly where a quietly wrong number does its damage. */
        h->total_bytes += advance;
    }
    if (--h->inflight == 0) pthread_cond_broadcast(&h->idle);
    pthread_mutex_unlock(&h->lock);
    return stale;
}

static int https_release_inflight(https_ctx* h, jobject is, int set_eof) {
    return https_release_inflight_advance(h, is, set_eof, 0);
}

/* Unblock whatever read is in flight and wait for it to return, so the caller can
 * delete the global refs it was using. Disconnecting closes the socket, which makes
 * a blocked InputStream.read() throw — that is what bounds the wait. Returning
 * before the reader is out is the use-after-free this exists to prevent. */
static void https_quiesce(https_ctx* h, JNIEnv* env) {
    pthread_mutex_lock(&h->lock);
    h->eof = 1;                       /* stop a fresh read starting mid-teardown */
    /* The disconnect runs under the lock rather than against a snapshot taken and
     * then released. reseek and close clear these fields under this same lock and
     * delete the global refs immediately after, so a snapshot used outside it can
     * be called through after the ref is gone — a reference-table use-after-free,
     * and an abort can run concurrently with a reseek whenever the two come from
     * different threads. Holding the lock across the call makes both orders safe:
     * either this runs first and the other waits, or the field is already NULL and
     * there is nothing to disconnect.
     *
     * Blocking a reader that is trying to start is the intent. One already inside
     * InputStream.read does not hold this lock, and the cond_wait below releases it
     * anyway, so the reader can always finish and be counted out. */
    if (h->conn && (*env)->IsInstanceOf(env, h->conn, g_ids.http_conn_cls)) {
        (*env)->CallVoidMethod(env, h->conn, g_ids.http_disconnect);
        if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
    }

    while (h->inflight > 0) pthread_cond_wait(&h->idle, &h->lock);
    pthread_mutex_unlock(&h->lock);
}

/* Releases a context that never got as far as installing a stream. */
static void https_ctx_free(https_ctx* h) {
    pthread_cond_destroy(&h->idle);
    pthread_mutex_destroy(&h->lock);
    free(h->url);
    free(h);
}

/* Copies a NUL-terminated java string into buf. Returns 0 when it is absent,
 * unreadable, or too long to hold: snprintf would leave a truncated value that
 * still looks like a successful read, and a prefix of a header or a URL is not
 * the thing the request will act on. Every caller here feeds either the address
 * policy or the next hop's target, so a silent prefix is the wrong answer to
 * hand back. */
static int copy_jstring(JNIEnv* env, jstring val, char* buf, int cap) {
    if (!val || cap <= 0) return 0;
    const char* c = (*env)->GetStringUTFChars(env, val, NULL);
    if (!c) {
        /* Only ever NULL with an OutOfMemoryError already thrown; leaving it
         * pending would poison every later JNI call on this thread. */
        if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
        return 0;
    }
    int n = snprintf(buf, (size_t)cap, "%s", c);
    (*env)->ReleaseStringUTFChars(env, val, c);
    return (n >= 0 && n < cap);
}

/* Reads a response header into buf; returns 0 when absent or over-long. */
static int get_header(JNIEnv* env, jobject conn, const char* name, char* buf, int cap) {
    jstring key = (*env)->NewStringUTF(env, name);
    if (!key) {
        /* Allocation failed, so an OutOfMemoryError is already pending and the
         * call below would be a JNI call made with an exception outstanding. */
        if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
        return 0;
    }
    jstring val = (jstring)(*env)->CallObjectMethod(env, conn, g_ids.conn_get_hdr, key);
    (*env)->DeleteLocalRef(env, key);
    if ((*env)->ExceptionCheck(env)) { (*env)->ExceptionClear(env); return 0; }
    if (!val) return 0;
    int ok = copy_jstring(env, val, buf, cap);
    (*env)->DeleteLocalRef(env, val);
    return ok;
}

/* Strict unsigned decimal from *sp up to `delim`: digits only (no sign, no
 * whitespace), overflow-checked, and the delimiter must follow. Advances *sp
 * past it and returns the value, or -1 on any malformation. Mirrors
 * basis_win_http's parse_u64_exact — strtoll would take a leading '+' and needs
 * an errno dance for overflow. */
static long long parse_u64_until(const char** sp, char delim) {
    const char* s = *sp;
    if (*s < '0' || *s > '9') return -1;
    unsigned long long v = 0;
    for (; *s >= '0' && *s <= '9'; ++s) {
        unsigned d = (unsigned)(*s - '0');
        if (v > (0x7FFFFFFFFFFFFFFFULL - d) / 10ULL) return -1;
        v = v * 10ULL + d;
    }
    if (*s != delim) return -1;
    *sp = s + 1;
    return (long long)v;
}

/* Parse a Content-Range "bytes first-last/total" strictly (only outer padding
 * tolerated), writing the first-byte-position and returning 0, or -1 on any
 * malformation. Mirrors basis_win_http's parse_content_range. */
static int parse_content_range_first(const char* s, long long* out_first) {
    while (*s == ' ' || *s == '\t') ++s;
    if (strncasecmp(s, "bytes", 5) != 0) return -1;
    s += 5;
    while (*s == ' ' || *s == '\t') ++s;
    long long first = parse_u64_until(&s, '-');
    long long last  = parse_u64_until(&s, '/');
    if (first < 0 || last < 0) return -1;
    /* total runs to the end (trailing padding aside), so parse it inline. */
    if (*s < '0' || *s > '9') return -1;
    unsigned long long total = 0;
    for (; *s >= '0' && *s <= '9'; ++s) {
        unsigned d = (unsigned)(*s - '0');
        if (total > (0x7FFFFFFFFFFFFFFFULL - d) / 10ULL) return -1;
        total = total * 10ULL + d;
    }
    while (*s == ' ' || *s == '\t' || *s == '\r' || *s == '\n') ++s;
    if (*s || last < first || total == 0 || (unsigned long long)last >= total) return -1;
    *out_first = first;
    return 0;
}

/* Host of `url`, for log lines. A media URL routinely carries a signed query
 * token or userinfo, and logcat is persisted and swept up by bug reports, so a
 * failure names the host it was talking to rather than the whole URL. Purely a
 * logging aid: nothing decides anything on this, so it does not need to agree
 * with the platform's parse the way the address guard does. */
static void url_host_for_log(const char* url, char* out, size_t cap) {
    if (!out || cap == 0) return;
    out[0] = '\0';
    if (!url) return;
    const char* p = strstr(url, "://");
    p = p ? p + 3 : url;
    const char* end = p;
    while (*end && *end != '/' && *end != '?' && *end != '#') end++;
    /* Last '@' in the authority, not the first: a password may carry one, and
     * stopping at the first would leave the tail of the credential in the log. */
    for (const char* at = end; at > p; at--)
        if (at[-1] == '@') { p = at; break; }
    size_t n = (size_t)(end - p);
    if (n >= cap) n = cap - 1;
    memcpy(out, p, n);
    out[n] = '\0';
}

/* setRequestProperty with both strings checked, 0 on failure. NewStringUTF
 * returns NULL when the JVM cannot allocate, leaving an OutOfMemoryError
 * pending, and setRequestProperty(null, …) would raise an NPE on top of it.
 * Neither can be left to surface at the next check: a JNI call made while an
 * exception is pending is illegal, and CheckJNI aborts the process for it
 * rather than returning an error. */
static int set_request_header(JNIEnv* env, jobject conn, const char* key, const char* value) {
    jstring k = (*env)->NewStringUTF(env, key);
    jstring v = k ? (*env)->NewStringUTF(env, value) : NULL;
    int ok = 0;
    if (k && v) {
        (*env)->CallVoidMethod(env, conn, g_ids.conn_set_req, k, v);
        ok = !(*env)->ExceptionCheck(env);
    }
    /* Deleting a local ref is one of the few calls allowed with an exception
     * pending, so this cleanup is safe on every path. */
    if (v) (*env)->DeleteLocalRef(env, v);
    if (k) (*env)->DeleteLocalRef(env, k);
    return ok;
}

/* Copies a java String result into buf, consuming the local ref; 0 if it was
 * null, a pending exception made it unreadable, or it did not fit. */
static int take_jstring(JNIEnv* env, jstring val, char* buf, int cap) {
    if ((*env)->ExceptionCheck(env)) {
        (*env)->ExceptionClear(env);
        if (val) (*env)->DeleteLocalRef(env, val);
        return 0;
    }
    int ok = copy_jstring(env, val, buf, cap);
    if (val) (*env)->DeleteLocalRef(env, val);
    return ok;
}

enum { MAX_REDIRECTS = 10 };

static int is_redirect_status(jint code) {
    return code == 301 || code == 302 || code == 303 || code == 307 || code == 308;
}

/* The address policy, applied to the URL this hop is about to fetch. The managed
 * gate only ever sees the entry URL, so without this a redirect walks straight
 * past it. getHost() leaves an IPv6 literal in its brackets, which the guard
 * accepts. */
/* `entry_secure` carries the entry URL's transport across the hop loop: -1 on the
 * first call, then 0/1. Turning off setInstanceFollowRedirects means the JRE no
 * longer refuses the hop that leaves TLS behind, so that has to be refused here
 * — a plaintext target can be an ordinary public host, which the address guard
 * passes, and the body would then travel readable and rewritable in transit. An
 * http entry URL stays allowed; it is the downgrade that is refused. Kept
 * deliberately identical to the WinHTTP source's rule. */
static int url_target_allowed(JNIEnv* env, jobject urlObj, int* entry_secure) {
    char scheme[16] = {0}, host[256] = {0};

    if (!take_jstring(env, (jstring)(*env)->CallObjectMethod(env, urlObj, g_ids.url_protocol),
                      scheme, sizeof(scheme))) {
        LOGE("basis_jni_https: refusing URL with an unreadable or over-long scheme");
        return 0;
    }
    if (strcasecmp(scheme, "http") != 0 && strcasecmp(scheme, "https") != 0) {
        LOGE("basis_jni_https: refusing scheme %s", scheme);
        return 0;
    }
    {
        int secure = (strcasecmp(scheme, "https") == 0);
        if (*entry_secure < 0) *entry_secure = secure;
        else if (*entry_secure && !secure) {
            LOGE("basis_jni_https: refusing https -> http downgrade on a redirect");
            return 0;
        }
    }

    if (!take_jstring(env, (jstring)(*env)->CallObjectMethod(env, urlObj, g_ids.url_host),
                      host, sizeof(host))) {
        LOGE("basis_jni_https: refusing URL with an unreadable or over-long host");
        return 0;
    }
    if (basis_io_host_is_blocked(host)) {
        LOGE("basis_jni_https: refusing blocked host %s", host);
        return 0;
    }
    return 1;
}

/* Opens a connected HttpURLConnection GET for `url` with the given Range header
 * value. On success returns a local ref to the connection and writes the HTTP
 * status to *out_code; the caller reads headers / getInputStream and owns the
 * ref. Returns NULL on any failure, with the java exception cleared. Shared by
 * open and reseek so the connect sequence lives in one place.
 *
 * Redirects are followed here rather than by HttpURLConnection. Its own
 * following is transparent — the same-protocol hop is taken inside the JRE and
 * getResponseCode() reports the final 2xx — so the target of that hop is never
 * offered to the address policy, and a public origin that passes the entry check
 * can answer 302 Location: http://192.168.1.1/… from a headset sitting on the
 * user's LAN. With following off the 3xx surfaces here, and each hop is resolved
 * against the URL that produced it, held to http(s), and put through the same
 * guard as the entry URL. Mirrors the WinHTTP source, deliberately: a redirect
 * rule that differs per platform is a rule one platform doesn't have.
 *
 * `range_val` is required, not optional: it reaches NewStringUTF, which is
 * undefined on NULL and aborts the VM under CheckJNI rather than failing the
 * open. Both callers pass one. An unranged GET would need a guard here first. */
static jobject https_connect(JNIEnv* env, const char* url, int timeout_ms,
                             const char* range_val, jint* out_code) {
    *out_code = 0;

    char loghost[256];
    url_host_for_log(url, loghost, sizeof(loghost));

    jstring jurl = (*env)->NewStringUTF(env, url);
    if (!jurl) {
        if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
        return NULL;
    }
    jobject urlObj = (*env)->NewObject(env, g_ids.url_cls, g_ids.url_ctor, jurl);
    (*env)->DeleteLocalRef(env, jurl);
    if ((*env)->ExceptionCheck(env) || !urlObj) {
        log_and_clear_pending(env, "new URL");
        if (urlObj) (*env)->DeleteLocalRef(env, urlObj);
        return NULL;
    }

    int entry_secure = -1;
    for (int hop = 0; ; hop++) {
        if (!url_target_allowed(env, urlObj, &entry_secure)) {
            (*env)->DeleteLocalRef(env, urlObj);
            return NULL;
        }

        /* Re-taken per hop, so a failure names the host it actually happened
         * against. Computed once from the entry URL, every message after a redirect
         * pointed at the origin the user asked for rather than the one that failed,
         * which is the wrong end of the chain to be looking at during an incident.
         * Safe to read from the URL object here: the target has just passed the
         * address policy, so this is a host we have already agreed to contact. */
        if (hop > 0 &&
            !take_jstring(env, (jstring)(*env)->CallObjectMethod(env, urlObj, g_ids.url_host),
                          loghost, sizeof(loghost))) {
            if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
            snprintf(loghost, sizeof(loghost), "<hop %d>", hop);
        }

        jobject conn = (*env)->CallObjectMethod(env, urlObj, g_ids.url_open);
        if ((*env)->ExceptionCheck(env) || !conn) {
            log_and_clear_pending(env, "openConnection");
            if (conn) (*env)->DeleteLocalRef(env, conn);
            (*env)->DeleteLocalRef(env, urlObj);
            return NULL;
        }

        if (timeout_ms > 0) {
            (*env)->CallVoidMethod(env, conn, g_ids.conn_set_ct, (jint)timeout_ms);
            (*env)->CallVoidMethod(env, conn, g_ids.conn_set_rt, (jint)timeout_ms);
        }
        /* Checked before the request is sent, and before any further JNI call:
         * the setters above can throw, and everything from here to
         * getResponseCode assumes a clean thread. */
        if ((*env)->ExceptionCheck(env) ||
            !set_request_header(env, conn, "User-Agent", "BasisMediaPlayer/1.0") ||
            !set_request_header(env, conn, "Range", range_val)) {
            log_and_clear_pending(env, "request headers");
            (*env)->DeleteLocalRef(env, conn);
            (*env)->DeleteLocalRef(env, urlObj);
            return NULL;
        }

        /* url_target_allowed has already held the scheme to http(s), so the
         * connection is always an HttpURLConnection (or its HttpsURLConnection
         * subclass) and the redirect + status APIs are there to use. Anything
         * else would leave the status unchecked, so refuse it. */
        if (!(*env)->IsInstanceOf(env, conn, g_ids.http_conn_cls)) {
            LOGE("basis_jni_https: not an HttpURLConnection at hop %d of %s", hop, loghost);
            (*env)->DeleteLocalRef(env, conn);
            (*env)->DeleteLocalRef(env, urlObj);
            return NULL;
        }
        (*env)->CallVoidMethod(env, conn, g_ids.http_set_follow, JNI_FALSE);
        if ((*env)->ExceptionCheck(env)) {
            log_and_clear_pending(env, "setInstanceFollowRedirects");
            (*env)->DeleteLocalRef(env, conn);
            (*env)->DeleteLocalRef(env, urlObj);
            return NULL;
        }

        (*env)->CallVoidMethod(env, conn, g_ids.conn_connect);
        if ((*env)->ExceptionCheck(env)) {
            log_and_clear_pending(env, "connect");
            (*env)->DeleteLocalRef(env, conn);
            (*env)->DeleteLocalRef(env, urlObj);
            return NULL;
        }

        jint code = (*env)->CallIntMethod(env, conn, g_ids.http_get_code);
        if ((*env)->ExceptionCheck(env)) {
            log_and_clear_pending(env, "getResponseCode");
            (*env)->CallVoidMethod(env, conn, g_ids.http_disconnect);
            if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
            (*env)->DeleteLocalRef(env, conn);
            (*env)->DeleteLocalRef(env, urlObj);
            return NULL;
        }

        if (is_redirect_status(code)) {
            /* Past the cap the chain is either hostile or broken — a self-redirect
             * loops here forever otherwise. URL(URL, String) does the relative
             * resolution, so a Location of "/seg/1.ts" or "../b.ts" lands where the
             * server meant it to. */
            jobject nextUrl = NULL;
            char loc[2048];
            if (hop < MAX_REDIRECTS && get_header(env, conn, "Location", loc, sizeof(loc)) && loc[0]) {
                jstring jloc = (*env)->NewStringUTF(env, loc);
                if (jloc) {
                    nextUrl = (*env)->NewObject(env, g_ids.url_cls, g_ids.url_ctor_rel, urlObj, jloc);
                    (*env)->DeleteLocalRef(env, jloc);
                    if ((*env)->ExceptionCheck(env)) {
                        log_and_clear_pending(env, "resolve Location");
                        nextUrl = NULL;
                    }
                } else if ((*env)->ExceptionCheck(env)) {
                    /* Allocation failed. The disconnect below is a JNI call, so
                     * the pending OutOfMemoryError cannot be carried into it. */
                    (*env)->ExceptionClear(env);
                }
            }
            (*env)->CallVoidMethod(env, conn, g_ids.http_disconnect);
            if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
            (*env)->DeleteLocalRef(env, conn);
            (*env)->DeleteLocalRef(env, urlObj);
            if (!nextUrl) {
                LOGE("basis_jni_https: HTTP %d not followed at hop %d of %s", (int)code, hop, loghost);
                return NULL;
            }
            urlObj = nextUrl;
            continue;
        }

        (*env)->DeleteLocalRef(env, urlObj);
        if (code < 200 || code >= 300) {
            LOGE("basis_jni_https: HTTP %d at hop %d of %s", (int)code, hop, loghost);
            (*env)->CallVoidMethod(env, conn, g_ids.http_disconnect);
            if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
            (*env)->DeleteLocalRef(env, conn);
            return NULL;
        }
        *out_code = code;
        return conn; /* local ref */
    }
}

void* basis_jni_https_open(const char* url, int timeout_ms) {
    if (!url) return NULL;
    if (!g_init_ok) { LOGE("basis_jni_https: JNI not initialised"); return NULL; }

    jenv_lease L; if (jenv_acquire(&L) != 0) return NULL;
    JNIEnv* env = L.env;

    https_ctx* h = (https_ctx*)calloc(1, sizeof(*h));
    if (!h) { jenv_release(&L); return NULL; }
    pthread_mutex_init(&h->lock, NULL);
    pthread_cond_init(&h->idle, NULL);

    /* The bytes=0- probe: identical body, but a server that really implements
     * ranges answers 206 — the seekability signal the live-vs-VOD delivery
     * auto-detect needs (mirrors the WinHTTP source; nginx omits Accept-Ranges
     * on 206 responses, so the status is the only proof there). */
    jint code = 0;
    jobject conn = https_connect(env, url, timeout_ms, "bytes=0-", &code);
    if (!conn) { https_ctx_free(h); jenv_release(&L); return NULL; }

    /* Seekability (live-vs-VOD auto-detect): a finite, range-fetchable body is
     * on-demand. Range support is proven by the probe answering 206 or by an
     * Accept-Ranges: bytes advertisement; a known Content-Length is required
     * either way so a chunked / open-ended live stream is never mistaken for VOD
     * (which would mis-pace it). range_ok keeps the stricter 206-only proof that
     * a later ranged refetch relies on. */
    {
        char ranges[64], clen[32];
        h->range_ok = (code == 206);
        int rangeable = h->range_ok;
        if (!rangeable && get_header(env, conn, "Accept-Ranges", ranges, sizeof(ranges)))
            rangeable = strcasecmp(ranges, "bytes") == 0;
        /* atoll takes a numeric prefix, answers 0 for anything unparseable and is
         * undefined past LLONG_MAX, on a header the server chooses. The portable
         * source parses this field the same strict way: digits and nothing else,
         * with errno checked. A value that fails leaves len at 0, which is already
         * the "unknown length" answer below — a stream rather than a seekable body,
         * which is the safe reading of a Content-Length we could not understand. */
        long long len = 0;
        if (get_header(env, conn, "Content-Length", clen, sizeof(clen))) {
            char* cl_end = NULL;
            errno = 0;
            long long v = strtoll(clen, &cl_end, 10);
            /* Recorded before the skip, exactly as the portable source does it and
             * for the same reason: strtoll skips leading whitespace itself, so on a
             * value of just a tab the skip below would move cl_end off clen and the
             * emptiness test would pass on a field with no digits. Harmless here,
             * because len stays 0 and that is already the unknown-length answer —
             * but the two parsers are supposed to agree, and a reader checking that
             * should not have to work out that one of them is accidentally safe. */
            int had_digits = (cl_end != clen);
            while (*cl_end == ' ' || *cl_end == '\t') ++cl_end;
            if (had_digits && !*cl_end && errno != ERANGE && v >= 0) len = v;
        }
        h->seekable = (rangeable && len > 0) ? 1 : 0;
        h->content_length = len > 0 ? len : -1;
    }

    jobject is = (*env)->CallObjectMethod(env, conn, g_ids.conn_get_is);
    if ((*env)->ExceptionCheck(env) || !is) {
        log_and_clear_pending(env, "getInputStream");
        if ((*env)->IsInstanceOf(env, conn, g_ids.http_conn_cls))
            (*env)->CallVoidMethod(env, conn, g_ids.http_disconnect);
        log_and_clear_pending(env, "disconnect");
        (*env)->DeleteLocalRef(env, conn);
        https_ctx_free(h); jenv_release(&L); return NULL;
    }

    h->conn = (*env)->NewGlobalRef(env, conn);
    h->is   = (*env)->NewGlobalRef(env, is);
    /* Same reason the reseek path checks: a NULL stream with the eof flag clear
     * is reported by the reader as a permanent -1 rather than an end of stream,
     * and the connection would sit open until close. Fail the open instead. */
    if (!h->conn || !h->is) {
        if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
        if (h->conn) (*env)->DeleteGlobalRef(env, h->conn);
        if (h->is)   (*env)->DeleteGlobalRef(env, h->is);
        h->conn = NULL; h->is = NULL;
        if ((*env)->IsInstanceOf(env, conn, g_ids.http_conn_cls))
            (*env)->CallVoidMethod(env, conn, g_ids.http_disconnect);
        log_and_clear_pending(env, "disconnect");
        (*env)->DeleteLocalRef(env, is);
        (*env)->DeleteLocalRef(env, conn);
        https_ctx_free(h); jenv_release(&L); return NULL;
    }
    h->url  = strdup(url);
    /* Without the URL every reseek refuses at its own guard, so an allocation
     * failure would surface as a source that cannot seek — a different fault,
     * reported at a different time, from the one that actually happened. The
     * disconnect matters for the same reason it does above: dropping the refs
     * alone leaves the connection open until the JVM finalises it. */
    if (!h->url) {
        (*env)->DeleteGlobalRef(env, h->conn);
        (*env)->DeleteGlobalRef(env, h->is);
        h->conn = NULL; h->is = NULL;
        if ((*env)->IsInstanceOf(env, conn, g_ids.http_conn_cls))
            (*env)->CallVoidMethod(env, conn, g_ids.http_disconnect);
        log_and_clear_pending(env, "disconnect");
        (*env)->DeleteLocalRef(env, is);
        (*env)->DeleteLocalRef(env, conn);
        https_ctx_free(h); jenv_release(&L); return NULL;
    }
    h->timeout_ms = timeout_ms;

    (*env)->DeleteLocalRef(env, is);
    (*env)->DeleteLocalRef(env, conn);

    {
        char okhost[256];
        url_host_for_log(url, okhost, sizeof(okhost));
        LOGI("basis_jni_https: open ok for %s", okhost);
    }
    jenv_release(&L);
    return h;
}

static int ensure_scratch(JNIEnv* env, https_ctx* h, int want) {
    if (h->scratch && h->scratch_cap >= want) return 0;
    if (h->scratch) {
        (*env)->DeleteGlobalRef(env, h->scratch);
        h->scratch = NULL;
        h->scratch_cap = 0;
    }
    int cap = want < 16384 ? 16384 : want;
    jbyteArray local = (*env)->NewByteArray(env, cap);
    if (!local) return -1;
    h->scratch = (jbyteArray)(*env)->NewGlobalRef(env, local);
    (*env)->DeleteLocalRef(env, local);
    h->scratch_cap = cap;
    return h->scratch ? 0 : -1;
}

int basis_jni_https_is_seekable(void* ctx) {
    https_ctx* h = (https_ctx*)ctx;
    return h ? h->seekable : 0;
}

long long basis_jni_https_content_length(void* ctx) {
    https_ctx* h = (https_ctx*)ctx;
    return h ? h->content_length : -1;
}

int basis_jni_https_can_reseek(void* ctx) {
    https_ctx* h = (https_ctx*)ctx;
    return h ? (h->seekable && h->range_ok) : 0;
}

void basis_jni_https_abort(void* ctx) {
    https_ctx* h = (https_ctx*)ctx;
    if (!h) return;
    jenv_lease L; if (jenv_acquire(&L) != 0) return;
    /* Waits the reader out as well as unblocking it: the caller is free to reseek
     * the moment this returns, and reseek deletes the refs the reader holds.
     * reseek clears eof when it installs the new stream. */
    https_quiesce(h, L.env);
    jenv_release(&L);
}

int basis_jni_https_reseek(void* ctx, long long offset) {
    https_ctx* h = (https_ctx*)ctx;
    if (!h || !h->seekable || !h->range_ok || !h->url || offset < 0) return -1;

    jenv_lease L; if (jenv_acquire(&L) != 0) return -1;
    JNIEnv* env = L.env;

    /* Tear down the old response. The quiesce is what makes the deletes below
     * safe — http_reseek aborts first, but this must not depend on its caller
     * having done so. */
    https_quiesce(h, env);

    pthread_mutex_lock(&h->lock);
    jobject old_is = h->is, old_conn = h->conn;
    h->is = NULL;
    h->conn = NULL;
    pthread_mutex_unlock(&h->lock);

    if (old_is) {
        (*env)->CallVoidMethod(env, old_is, g_ids.is_close);
        if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
        (*env)->DeleteGlobalRef(env, old_is);
    }
    if (old_conn) {
        if ((*env)->IsInstanceOf(env, old_conn, g_ids.http_conn_cls))
            (*env)->CallVoidMethod(env, old_conn, g_ids.http_disconnect);
        if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
        (*env)->DeleteGlobalRef(env, old_conn);
    }

    char range[64];
    snprintf(range, sizeof(range), "bytes=%lld-", offset);
    jint code = 0;
    jobject conn = https_connect(env, h->url, h->timeout_ms, range, &code);
    if (!conn) { jenv_release(&L); return -1; }   /* quiesce already set eof */

    /* 206 = ranged body starting at offset. A 200 means the server ignored the
     * Range and restarted at byte 0 — the bytes would be silently misaligned. */
    if (code != 206 && !(code == 200 && offset == 0)) {
        (*env)->CallVoidMethod(env, conn, g_ids.http_disconnect);
        if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
        (*env)->DeleteLocalRef(env, conn);
        jenv_release(&L); return -1;   /* quiesce already set eof */
    }
    /* 206 status alone doesn't say where the part starts — a range-rewriting proxy
     * or a multipart/byteranges answer is also a 206. Confirm the full
     * "bytes first-last/total" grammar and that first is the offset we asked for,
     * or the bytes land at the wrong stream position. */
    if (code == 206) {
        char cr[128];
        long long first;
        if (!get_header(env, conn, "Content-Range", cr, sizeof(cr)) ||
            parse_content_range_first(cr, &first) != 0 || first != offset) {
            (*env)->CallVoidMethod(env, conn, g_ids.http_disconnect);
            if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
            (*env)->DeleteLocalRef(env, conn);
            jenv_release(&L); return -1;
        }
    }

    jobject is = (*env)->CallObjectMethod(env, conn, g_ids.conn_get_is);
    if ((*env)->ExceptionCheck(env) || !is) {
        log_and_clear_pending(env, "reseek getInputStream");
        (*env)->CallVoidMethod(env, conn, g_ids.http_disconnect);
        if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
        (*env)->DeleteLocalRef(env, conn);
        jenv_release(&L); return -1;   /* quiesce already set eof */
    }

    jobject new_conn = (*env)->NewGlobalRef(env, conn);
    jobject new_is   = (*env)->NewGlobalRef(env, is);
    /* A global ref is NULL when the JVM cannot allocate. Installing one would
     * leave `is` NULL with `eof` clear, which the reader reports as a permanent
     * -1 rather than an end of stream, and would strand the connection until
     * close. Fail the reseek instead, the same as every other path here. */
    if (!new_conn || !new_is) {
        if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
        if (new_conn) (*env)->DeleteGlobalRef(env, new_conn);
        if (new_is) (*env)->DeleteGlobalRef(env, new_is);
        (*env)->CallVoidMethod(env, conn, g_ids.http_disconnect);
        if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
        (*env)->DeleteLocalRef(env, is);
        (*env)->DeleteLocalRef(env, conn);
        jenv_release(&L); return -1;   /* quiesce already set eof */
    }
    pthread_mutex_lock(&h->lock);
    h->conn = new_conn;
    h->is   = new_is;
    h->eof  = 0;
    h->total_bytes = offset;
    pthread_mutex_unlock(&h->lock);
    (*env)->DeleteLocalRef(env, is);
    (*env)->DeleteLocalRef(env, conn);
    jenv_release(&L);
    return 0;
}

int basis_jni_https_read(void* ctx, uint8_t* buf, int len) {
    https_ctx* h = (https_ctx*)ctx;
    if (!h || !buf || len <= 0) return 0;

    /* Claim the stream before using it, so a reseek or teardown cannot delete
     * the global ref underneath this call. `is` is a copy: the fields may be
     * replaced while the read blocks, which the staleness check below catches. */
    pthread_mutex_lock(&h->lock);
    jobject is = h->is;
    if (h->eof || !is) {
        int done = h->eof;
        pthread_mutex_unlock(&h->lock);
        return done ? 0 : -1;
    }
    h->inflight++;
    pthread_mutex_unlock(&h->lock);

    jenv_lease L;
    if (jenv_acquire(&L) != 0) { https_release_inflight(h, is, 1); return -1; }
    JNIEnv* env = L.env;

    if (ensure_scratch(env, h, len) != 0) {
        jenv_release(&L); https_release_inflight(h, is, 1); return -1;
    }

    int want = len < h->scratch_cap ? len : h->scratch_cap;
    jint n = 0;
    int zero_reads = 0;
    for (;;) {
        n = (*env)->CallIntMethod(env, is, g_ids.is_read, h->scratch, 0, want);
        if ((*env)->ExceptionCheck(env)) {
            log_and_clear_pending(env, "InputStream.read");
            LOGE("basis_jni_https: read exception after %lld bytes", h->total_bytes);
            jenv_release(&L);
            https_release_inflight(h, is, 1);
            return -1;
        }
        if (n != 0) break;
        /* Java's read(byte[], 0, len>0) contract is to block until data, EOF or
         * error — a 0 return is a stack bug. The byte-source contract has no
         * retry signal (0 means EOF and would end the stream), so absorb the
         * anomaly here and read again — bounded, because a stream that keeps
         * returning 0 without blocking would otherwise spin this thread
         * forever; past the bound it is broken, and a terminal error routes it
         * to the engine's error path rather than a fake clean EOF. */
        if (++zero_reads >= 1000) {
            LOGE("basis_jni_https: persistent zero-byte reads after %lld bytes", h->total_bytes);
            jenv_release(&L);
            https_release_inflight(h, is, 1);
            return -1;
        }
    }
    if (n < 0) {
        LOGI("basis_jni_https: clean EOF after %lld bytes", h->total_bytes);
        jenv_release(&L);
        https_release_inflight(h, is, 1);
        return 0;
    }
    (*env)->GetByteArrayRegion(env, h->scratch, 0, n, (jbyte*)buf);
    jenv_release(&L);

    /* Bytes from a response that has since been replaced belong to the pre-seek
     * stream and must not reach the ring. The cursor advances under the same
     * lock, so a reseek waiting to install a new stream cannot have its position
     * overwritten afterwards. */
    if (https_release_inflight_advance(h, is, 0, (int)n) != 0) return -1;
    return (int)n;
}

void basis_jni_https_close(void* ctx) {
    https_ctx* h = (https_ctx*)ctx;
    if (!h) return;

    jenv_lease L;
    if (jenv_acquire(&L) == 0) {
        JNIEnv* env = L.env;
        /* The core joins the reader before it gets here, so this is normally a
         * no-op — but it is the only thing between a reader that outlived its
         * join and the frees below. */
        https_quiesce(h, env);
        /* Detach under the lock before deleting, as reseek does: a quiesce racing
         * this must find NULL rather than a reference that is about to go. */
        pthread_mutex_lock(&h->lock);
        jobject old_is = h->is, old_conn = h->conn;
        h->is = NULL;
        h->conn = NULL;
        pthread_mutex_unlock(&h->lock);
        if (old_is) {
            (*env)->CallVoidMethod(env, old_is, g_ids.is_close);
            if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
            (*env)->DeleteGlobalRef(env, old_is);
        }
        if (old_conn) {
            if ((*env)->IsInstanceOf(env, old_conn, g_ids.http_conn_cls))
                (*env)->CallVoidMethod(env, old_conn, g_ids.http_disconnect);
            if ((*env)->ExceptionCheck(env)) (*env)->ExceptionClear(env);
            (*env)->DeleteGlobalRef(env, old_conn);
        }
        if (h->scratch) (*env)->DeleteGlobalRef(env, h->scratch);
        jenv_release(&L);
    }
    https_ctx_free(h);
}
