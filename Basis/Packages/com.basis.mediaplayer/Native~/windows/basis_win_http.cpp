/* WinHTTP byte source — OS TLS/HTTP, no third-party deps. */
#include "basis_win_http.h"
#include "../protocol/basis_io.h"

#include <windows.h>
#include <winhttp.h>
#include <shlwapi.h>
#include <stdio.h>
#include <string.h>
#include <stdlib.h>
#include <wchar.h>

#pragma comment(lib, "winhttp.lib")
#pragma comment(lib, "shlwapi.lib")

/* `request` is shared between the read-ahead reader thread and the demux thread,
 * which aborts and re-issues it. Nulling it around a close is not enough on its
 * own: a reader that has already copied the value keeps reading through it, and
 * the caller reopens the moment abort returns, so WinHTTP can hand the same
 * HINTERNET straight back and the reader drains the new response into the
 * pre-seek ring. `lock` covers the field, `inflight` counts readers currently
 * using a copy of it, and `idle` lets a detaching thread wait them out before
 * anything is reallocated. */
typedef struct {
    HINTERNET session;
    HINTERNET connect;
    HINTERNET request;
    CRITICAL_SECTION lock;
    CONDITION_VARIABLE idle;
    int inflight;
    int response_complete;
    int seekable;            /* finite Content-Length + Accept-Ranges: bytes (VOD) */
    int range_ok;            /* server answered the bytes=0- probe with a 206 */
    long long content_length;/* body size, or -1 when unknown/chunked/live */
    wchar_t* url;            /* URL as opened, kept for ranged re-requests */
} win_http_t;

/* Detach the current request under the lock, close it (the documented way to
 * cancel a synchronous WinHttpReadData), then wait for every reader holding a
 * copy to return. Returning before that is what lets the next open reuse the
 * handle value out from under them. The wait terminates because the close above
 * fails the pending read; the alternative to waiting is the use-after-free.
 *
 * The close has to precede the drain, since it is what makes the pending read
 * return, so a reader that has claimed but not yet entered WinHttpReadData can
 * still call it on a closed handle.
 *
 * That read cannot be mistaken for a current one, and the ordering here is what
 * guarantees it, so it is worth spelling out. `request` is cleared under the lock
 * above, before the close. Any reopen happens only after this function returns,
 * which is after the drain, which is after the reader has taken the lock again for
 * its own staleness check. At that check `request` is therefore NULL and cannot
 * equal the handle the reader copied, whatever value WinHTTP has since recycled.
 * A generation counter beside `request` would be comparing a field that already
 * cannot match.
 *
 * What the ordering does not fix is such a read consuming bytes from whichever
 * request WinHTTP handed the value to next — the loss lands on that other request,
 * which no bookkeeping in this struct can see. Closing that needs asynchronous
 * WinHTTP, where cancellation is defined. */
static void detach_request_and_drain(win_http_t* h) {
    EnterCriticalSection(&h->lock);
    HINTERNET req = h->request;
    h->request = NULL;
    h->response_complete = 1;
    LeaveCriticalSection(&h->lock);

    if (req) WinHttpCloseHandle(req);

    EnterCriticalSection(&h->lock);
    while (h->inflight > 0) SleepConditionVariableCS(&h->idle, &h->lock, INFINITE);
    LeaveCriticalSection(&h->lock);
}

/* Whole-field unsigned parse, or -1 for anything that isn't one. _wcstoi64 would
 * take a numeric prefix ("123junk" -> 123) and saturate to the signed maximum on
 * overflow, either of which hands a bogus finite length to the seek and pacing
 * logic. Header values are remote input, so the field must be digits and nothing
 * else, and must fit. */
static long long parse_u64_exact(const wchar_t* s, const wchar_t* end) {
    if (!s || s >= end) return -1;
    unsigned long long v = 0;
    for (; s < end; s++) {
        if (*s < L'0' || *s > L'9') return -1;
        unsigned d = (unsigned)(*s - L'0');
        if (v > (0x7FFFFFFFFFFFFFFFULL - d) / 10ULL) return -1;
        v = v * 10ULL + d;
    }
    return (long long)v;
}

/* A whole field value, with the optional padding a header value may carry trimmed
 * off. That padding is only ever legal around the value, never inside it. */
static long long parse_u64_field(const wchar_t* s) {
    if (!s) return -1;
    const wchar_t* end = s + wcslen(s);
    while (s < end && (*s == L' ' || *s == L'\t')) s++;
    while (end > s && (end[-1] == L' ' || end[-1] == L'\t')) end--;
    return parse_u64_exact(s, end);
}

/* Complete length out of a "bytes <first>-<last>/<complete>" Content-Range, read for
 * the bytes=0- probe specifically. The grammar carries no whitespace of its own, so
 * only the outer field padding is tolerated and the delimiters must be exact — a value
 * like "not-a-range/123" must not pass on the strength of its tail. The bounds have to
 * be coherent, and first must be 0, because that is what the probe asked for: a body
 * starting anywhere else is not the one the caller believes it is reading. Either "*"
 * form reports unknown. */
static int parse_content_range(const wchar_t* s, long long* first, long long* last, long long* total) {
    if (!s) return -1;
    const wchar_t* end = s + wcslen(s);
    while (s < end && (*s == L' ' || *s == L'\t')) s++;
    while (end > s && (end[-1] == L' ' || end[-1] == L'\t')) end--;

    const size_t unitLen = 6;   /* "bytes" and the single SP the grammar allows */
    if ((size_t)(end - s) <= unitLen || _wcsnicmp(s, L"bytes ", unitLen) != 0) return -1;
    s += unitLen;

    const wchar_t* dash = wcschr(s, L'-');
    const wchar_t* slash = wcschr(s, L'/');
    if (!dash || !slash || dash >= slash || slash >= end) return -1;

    long long f = parse_u64_exact(s, dash);
    long long l = parse_u64_exact(dash + 1, slash);
    long long t = parse_u64_exact(slash + 1, end);
    if (f < 0 || l < f || t <= 0 || l >= t) return -1;
    *first = f; *last = l; *total = t;
    return 0;
}

static long long parse_content_range_total(const wchar_t* s) {
    long long first, last, total;
    if (parse_content_range(s, &first, &last, &total) != 0) return -1;
    if (first != 0) return -1;   /* the initial probe asked for bytes=0- */
    return total;
}

static wchar_t* to_w(const char* s) {
    int n = MultiByteToWideChar(CP_UTF8, 0, s, -1, NULL, 0);
    wchar_t* w = (wchar_t*)malloc((size_t)n * sizeof(wchar_t));
    if (w) MultiByteToWideChar(CP_UTF8, 0, s, -1, w, n);
    return w;
}

enum {
    URL_MAX = 4096,       /* wide chars, including the terminator */
    MAX_REDIRECTS = 10    /* WinHTTP's own default cap when it follows them itself */
};

/* WinHttpCrackUrl keeps an IPv6 literal's brackets and hands back UTF-16; the
 * address guard is C and takes UTF-8. Fail closed on a host that won't convert. */
static int host_is_blocked_w(const wchar_t* host) {
    char utf8[1024];
    if (!WideCharToMultiByte(CP_UTF8, 0, host, -1, utf8, (int)sizeof(utf8), NULL, NULL)) return 1;
    return basis_io_host_is_blocked(utf8);
}

static int is_redirect_status(DWORD code) {
    return code == 301 || code == 302 || code == 303 || code == 307 || code == 308;
}

typedef struct {
    wchar_t cur[URL_MAX];    /* the URL this hop is fetching   */
    wchar_t next[URL_MAX];   /* Location resolved against cur  */
    wchar_t location[URL_MAX];
    wchar_t path[URL_MAX];
    wchar_t host[256];
} follow_bufs;

/* GETs `url` with `range_header`, following redirects by hand, and hands back the
 * connect + request handles for the response that ends the chain.
 *
 * Redirects have to be followed here rather than by WinHTTP because the address
 * policy is only ever applied to a URL we can see. WinHTTP's default policy,
 * DISALLOW_HTTPS_TO_HTTP, covers the downgrade hop and nothing else, so an origin
 * that passes the entry check can answer 302 Location: https://127.0.0.1/… and
 * the connection is made before anything gets a chance to look at the target.
 * REDIRECT_POLICY_NEVER surfaces the 3xx instead, and each hop is then cracked,
 * held to http(s), and put through the same guard as the entry URL.
 *
 * A relative Location is resolved with UrlCombineW, which also normalises the
 * result and collapses dot segments; it will happily produce a file: or
 * javascript: URL from a Location that carries its own scheme, which is why the
 * scheme check sits after the combine rather than before it.
 *
 * Returns 0 with *out_connect / *out_request / *out_code set, or -1 having
 * released everything it opened. */
static int http_request_follow(HINTERNET session, const wchar_t* url,
                               const wchar_t* range_header,
                               HINTERNET* out_connect, HINTERNET* out_request,
                               DWORD* out_code) {
    *out_connect = NULL; *out_request = NULL; *out_code = 0;
    if (!session || !url || wcslen(url) >= URL_MAX) return -1;

    follow_bufs* b = (follow_bufs*)malloc(sizeof(follow_bufs));
    if (!b) return -1;
    wcscpy_s(b->cur, URL_MAX, url);

    int rc = -1;
    /* Taking the redirect loop off WinHTTP means taking on the one thing its
     * default policy did cover: DISALLOW_HTTPS_TO_HTTP refuses a hop that leaves
     * TLS behind. Nothing else here would catch it — a plaintext target can be a
     * perfectly ordinary public host, so the address guard passes it — and the
     * body would then travel readable and rewritable by anyone on the path. An
     * http entry URL stays allowed; it is the downgrade that is refused. */
    int entry_secure = -1;
    for (int hop = 0; ; hop++) {
        URL_COMPONENTS uc;
        memset(&uc, 0, sizeof(uc));
        uc.dwStructSize = sizeof(uc);
        b->host[0] = 0; b->path[0] = 0;
        uc.lpszHostName = b->host; uc.dwHostNameLength = (DWORD)(_countof(b->host) - 1);
        uc.lpszUrlPath = b->path; uc.dwUrlPathLength = URL_MAX - 1;
        /* No ExtraInfo buffer on purpose: WinHTTP then leaves the query in the path,
         * which is exactly what the request object wants. Splitting it out would
         * drop the query from every signed CDN URL. */
        if (!WinHttpCrackUrl(b->cur, 0, 0, &uc)) break;
        if (uc.nScheme != INTERNET_SCHEME_HTTP && uc.nScheme != INTERNET_SCHEME_HTTPS) break;
        int secure = (uc.nScheme == INTERNET_SCHEME_HTTPS);
        if (entry_secure < 0) entry_secure = secure;
        else if (entry_secure && !secure) break;   /* https -> http downgrade */
        if (host_is_blocked_w(b->host)) break;

        HINTERNET conn = WinHttpConnect(session, b->host, uc.nPort, 0);
        if (!conn) break;

        DWORD flags = (uc.nScheme == INTERNET_SCHEME_HTTPS) ? WINHTTP_FLAG_SECURE : 0;
        HINTERNET req = WinHttpOpenRequest(conn, L"GET", b->path[0] ? b->path : L"/", NULL,
                                           WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, flags);
        if (!req) { WinHttpCloseHandle(conn); break; }

        DWORD redirectPolicy = WINHTTP_OPTION_REDIRECT_POLICY_NEVER;
        /* Caps the wait for response headers separately from the data-read timeout, so
         * a host that accepts and then goes quiet can be bounded without shortening the
         * gap a live stream is allowed between segments. WinHTTP evaluates this as data
         * arrives rather than as an absolute deadline, so a server that sends nothing at
         * all can still fall through to the receive timeout — untested here. */
        DWORD responseTimeout = 10000;
        if (!WinHttpSetOption(req, WINHTTP_OPTION_REDIRECT_POLICY,
                              &redirectPolicy, sizeof(redirectPolicy)) ||
            !WinHttpSetOption(req, WINHTTP_OPTION_RECEIVE_RESPONSE_TIMEOUT,
                              &responseTimeout, sizeof(responseTimeout)) ||
            !WinHttpSendRequest(req, range_header, (DWORD)-1L,
                                WINHTTP_NO_REQUEST_DATA, 0, 0, 0) ||
            !WinHttpReceiveResponse(req, NULL)) {
            WinHttpCloseHandle(req); WinHttpCloseHandle(conn); break;
        }

        DWORD code = 0, sz = sizeof(code);
        if (!WinHttpQueryHeaders(req, WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                                 WINHTTP_HEADER_NAME_BY_INDEX, &code, &sz, WINHTTP_NO_HEADER_INDEX)) {
            WinHttpCloseHandle(req); WinHttpCloseHandle(conn); break;
        }

        if (!is_redirect_status(code)) {
            *out_connect = conn; *out_request = req; *out_code = code;
            rc = 0;
            break;
        }

        /* Past the cap the chain is either hostile or broken — a self-redirect
         * loops here forever otherwise. */
        int followed = 0;
        if (hop < MAX_REDIRECTS) {
            DWORD lsz = sizeof(b->location);
            DWORD nsz = URL_MAX;
            b->location[0] = 0;
            if (WinHttpQueryHeaders(req, WINHTTP_QUERY_LOCATION, WINHTTP_HEADER_NAME_BY_INDEX,
                                    b->location, &lsz, WINHTTP_NO_HEADER_INDEX) &&
                b->location[0] &&
                SUCCEEDED(UrlCombineW(b->cur, b->location, b->next, &nsz, 0))) {
                wcscpy_s(b->cur, URL_MAX, b->next);
                followed = 1;
            }
        }
        WinHttpCloseHandle(req);
        WinHttpCloseHandle(conn);
        if (!followed) break;
    }

    free(b);
    return rc;
}

extern "C" void* basis_win_http_open(const char* url) {
    if (!url) return NULL;
    win_http_t* h = (win_http_t*)calloc(1, sizeof(win_http_t));
    if (!h) return NULL;

    /* Before any path that can reach basis_win_http_close, which deletes it. */
    InitializeCriticalSection(&h->lock);
    InitializeConditionVariable(&h->idle);

    h->url = to_w(url);
    if (!h->url) { DeleteCriticalSection(&h->lock); free(h); return NULL; }

    h->session = WinHttpOpen(L"BasisMediaPlayer/1.0",
                             WINHTTP_ACCESS_TYPE_AUTOMATIC_PROXY,
                             WINHTTP_NO_PROXY_NAME, WINHTTP_NO_PROXY_BYPASS, 0);
    if (!h->session) { basis_win_http_close(h); return NULL; }

    /* Bound every phase of the open. basis_media_close joins this thread from the
     * Unity main thread, so an open that stalls freezes the client for as long as
     * it takes — and WinHTTP's own defaults give an attacker-chosen URL a very long
     * lever: the resolve timeout defaults to 0, meaning no timeout at all, and a
     * blackholed SYN parks in WinHttpSendRequest for the TCP stack's ~21 s. Every
     * other transport here already bounds its connect (basis_io.c uses a
     * non-blocking connect with select(); the Android path sets connect/read
     * timeouts), so this only brings Windows into line. Data reads keep the 30 s
     * default — a live stream may legitimately go quiet between segments.
     *
     * These bound WinHTTP's own waits, not the TCP stack's: the connect value does
     * not override SYN/ACK retransmission, so treat it as the usual case rather
     * than a guarantee. Measured against a blackholed port it does hold — teardown
     * tracks the connect value exactly, plus a fixed ~2.9 s of unrelated teardown.
     *
     * Fail closed: running on the default timeouts is the exact condition being
     * guarded against, so failing to set them is a failure to open. */
    if (!WinHttpSetTimeouts(h->session, 5000 /*resolve*/, 5000 /*connect*/,
                                        10000 /*send*/, 30000 /*receive data*/)) {
        basis_win_http_close(h); return NULL;
    }

    /* A protocol floor. Without one the set follows whatever the host's WinHTTP
     * policy still permits, which on an unmanaged machine can include TLS 1.0.
     * TLS 1.3's flag is newer than some SDK/OS pairs, so ask for both and fall
     * back to 1.2 alone rather than let an unknown bit fail the whole option. */
    {
        DWORD protocols = WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_2;
        int set_ok = 0;
#if defined(WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_3)
        /* Kept as its own attempt rather than a conditional arm of the one below,
         * so no brace pair straddles the #if and the block reads the same before
         * and after preprocessing. */
        DWORD with13 = protocols | WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_3;
        set_ok = WinHttpSetOption(h->session, WINHTTP_OPTION_SECURE_PROTOCOLS,
                                  &with13, sizeof(with13)) ? 1 : 0;
#endif
        if (!set_ok &&
            !WinHttpSetOption(h->session, WINHTTP_OPTION_SECURE_PROTOCOLS,
                              &protocols, sizeof(protocols))) {
            basis_win_http_close(h); return NULL;
        }
    }

    /* Revocation checking is off unless asked for, so a revoked certificate is
     * accepted by default. Not fail-closed like the two above: this one can refuse
     * a legitimate stream when the revocation endpoint is unreachable rather than
     * when the certificate is bad, and a client that cannot reach OCSP should still
     * play. Enabling it is the improvement; requiring the enable to succeed is not. */
    {
        DWORD feature = WINHTTP_ENABLE_SSL_REVOCATION;
        WinHttpSetOption(h->session, WINHTTP_OPTION_ENABLE_FEATURE, &feature, sizeof(feature));
    }

    /* The bytes=0- probe: identical body, but a server that really implements
     * ranges answers 206. Only that proves a later ranged re-request will be
     * honoured — Accept-Ranges alone is advertisement (Python's SimpleHTTP
     * handler, for one, advertises it and then serves 200 + the whole file). */
    DWORD code = 0;
    if (http_request_follow(h->session, h->url, L"Range: bytes=0-",
                            &h->connect, &h->request, &code) != 0) {
        basis_win_http_close(h);
        return NULL;
    }

    /* 2xx only. A 3xx surviving the hop loop is one that was refused or ran past
     * the cap, and its body is a redirect page rather than media. */
    if (code < 200 || code >= 300) { basis_win_http_close(h); return NULL; }
    h->range_ok = (code == 206);

    /* Seekability (for live-vs-VOD auto-detection): a finite, range-fetchable
     * body — on-demand content. Range support is proven either by the probe
     * answering 206 (nginx omits Accept-Ranges on 206 responses, so the status
     * is the only signal there) or by an Accept-Ranges: bytes advertisement.
     * Finiteness has to come from somewhere too, so that a chunked / open-ended
     * live stream is never mistaken for VOD (which would mis-pace it) — either a
     * Content-Length for the body that arrived, or a Content-Range stating the
     * representation total. Those are different quantities and only the second is
     * a length for the whole source; see the split below. Advertised-only range
     * support still counts as on-demand for pacing, but never for seeking:
     * can_reseek and reseek both additionally require the probe's 206. */
    {
        wchar_t field[128] = {0}; DWORD fsz = sizeof(field);
        long long bodyLen = -1;   /* what this response carries */
        long long total = -1;     /* the whole representation, where it can be proven */

        /* Content-Length is read as a string and parsed, not queried with
         * WINHTTP_QUERY_FLAG_NUMBER64. Wine's winhttp omits NUMBER64 from its
         * QUERY_MODIFIER_MASK, so the flag is left in the attribute index, the header
         * lookup misses and the query fails outright — every source then looks
         * non-seekable under Proton. The 32-bit WINHTTP_QUERY_FLAG_NUMBER that Wine does
         * support truncates past 4GB, so it isn't the answer either. */
        if (WinHttpQueryHeaders(h->request, WINHTTP_QUERY_CONTENT_LENGTH,
                WINHTTP_HEADER_NAME_BY_INDEX, field, &fsz, WINHTTP_NO_HEADER_INDEX)) {
            bodyLen = parse_u64_field(field);
        }

        if (h->range_ok) {
            /* On a 206 the Content-Length covers the returned part, which a range-capping
             * proxy can make far smaller than the file, so it can never stand in for the
             * total. Content-Range is the only thing that can. */
            field[0] = 0; fsz = sizeof(field);
            if (WinHttpQueryHeaders(h->request, WINHTTP_QUERY_CONTENT_RANGE,
                    WINHTTP_HEADER_NAME_BY_INDEX, field, &fsz, WINHTTP_NO_HEADER_INDEX)) {
                total = parse_content_range_total(field);
            }
        } else {
            total = bodyLen;   /* 200: the body is the whole representation */
        }

        wchar_t ranges[64] = {0}; DWORD rsz = sizeof(ranges);
        BOOL haveRanges = WinHttpQueryHeaders(h->request, WINHTTP_QUERY_ACCEPT_RANGES,
            WINHTTP_HEADER_NAME_BY_INDEX, ranges, &rsz, WINHTTP_NO_HEADER_INDEX);
        int rangeable = h->range_ok || (haveRanges && _wcsicmp(ranges, L"bytes") == 0);

        /* Finite and rangeable is what makes this on-demand rather than live, and that
         * is all the delivery pacing needs. Knowing the complete length is a separate
         * question: a 206 that won't state one still paces correctly, it just reports an
         * unknown size rather than passing the part length off as the whole. */
        h->seekable = ((bodyLen > 0 || total > 0) && rangeable) ? 1 : 0;
        h->content_length = (total > 0) ? total : -1;
    }
    return h;
}

extern "C" int basis_win_http_is_seekable(void* ctx) {
    win_http_t* h = (win_http_t*)ctx;
    return h ? h->seekable : 0;
}

extern "C" long long basis_win_http_content_length(void* ctx) {
    win_http_t* h = (win_http_t*)ctx;
    return h ? h->content_length : -1;
}

extern "C" int basis_win_http_can_reseek(void* ctx) {
    win_http_t* h = (win_http_t*)ctx;
    return h ? (h->seekable && h->range_ok) : 0;
}

extern "C" int basis_win_http_read(void* ctx, uint8_t* buf, int len) {
    win_http_t* h = (win_http_t*)ctx;
    /* `buf` is checked as well as the rest: it goes straight to WinHttpReadData,
     * and the JNI source guards the same three. Both are installed through the same
     * provider table, so they should refuse the same arguments. */
    if (!h || !buf || len <= 0) return 0;

    EnterCriticalSection(&h->lock);
    HINTERNET req = h->request;
    if (h->response_complete || !req) {
        int done = h->response_complete;
        LeaveCriticalSection(&h->lock);
        return done ? 0 : -1;
    }
    h->inflight++;
    LeaveCriticalSection(&h->lock);

    DWORD read = 0;
    BOOL ok = WinHttpReadData(req, buf, (DWORD)len, &read);

    EnterCriticalSection(&h->lock);
    /* A reseek that landed while this read was in flight has installed a new
     * response; these bytes belong to the old one and must not reach the ring,
     * nor may they mark the new response complete. */
    int stale = (h->request != req);
    if (!stale && ok && read == 0) h->response_complete = 1;
    if (--h->inflight == 0) WakeAllConditionVariable(&h->idle);
    LeaveCriticalSection(&h->lock);

    if (stale || !ok) return -1;
    return (int)read;
}

extern "C" void basis_win_http_abort(void* ctx) {
    win_http_t* h = (win_http_t*)ctx;
    if (!h) return;
    detach_request_and_drain(h);
}

/* Replaces the current response with a ranged GET on the same connection so the
 * stream continues from `offset`. Only valid on a seekable body. The caller must
 * guarantee no concurrent basis_win_http_read is in flight (park or abort the
 * reading thread first — a prior basis_win_http_abort is fine, this re-opens).
 * Returns 0 on success; on failure the source is left request-less and reads
 * report EOF. */
extern "C" int basis_win_http_reseek(void* ctx, long long offset) {
    win_http_t* h = (win_http_t*)ctx;
    if (!h || !h->seekable || !h->range_ok || !h->url || offset < 0) return -1;

    /* Re-issued against the URL the caller opened, not against whatever the first
     * response's chain ended on, so a redirector handing out short-lived signed
     * targets still works after a seek — and every hop is re-validated. The
     * connect handle goes with it because the chain may land somewhere else this
     * time; WinHTTP pools the underlying connection per session, so re-opening
     * one against the same host does not mean a fresh TCP/TLS handshake.
     *
     * The drain has to happen before either handle is released, not just before
     * the reopen: closing the connect handle closes the request beneath it, so a
     * reader still inside WinHttpReadData would lose the handle either way. */
    detach_request_and_drain(h);
    EnterCriticalSection(&h->lock);
    HINTERNET old_conn = h->connect;
    h->connect = NULL;
    LeaveCriticalSection(&h->lock);
    if (old_conn) WinHttpCloseHandle(old_conn);

    wchar_t range[64];
    swprintf(range, 64, L"Range: bytes=%lld-", offset);

    HINTERNET conn = NULL, req = NULL;
    DWORD code = 0;
    if (http_request_follow(h->session, h->url, range, &conn, &req, &code) != 0) return -1;

    /* 206 = ranged body starting at offset. A 200 means the server ignored the
     * Range and restarted at byte 0 — the bytes would be silently misaligned. */
    if (code != 206 && !(code == 200 && offset == 0)) {
        WinHttpCloseHandle(req); WinHttpCloseHandle(conn);
        return -1;
    }
    /* 206 status alone doesn't say where the part starts — a range-rewriting proxy
     * or a multipart/byteranges answer is also a 206. Confirm Content-Range begins
     * at the offset we asked for, or the bytes land at the wrong stream position. */
    if (code == 206) {
        wchar_t cr[128] = {0}; DWORD crsz = sizeof(cr);
        long long first, last, total;
        if (!WinHttpQueryHeaders(req, WINHTTP_QUERY_CONTENT_RANGE, WINHTTP_HEADER_NAME_BY_INDEX,
                                 cr, &crsz, WINHTTP_NO_HEADER_INDEX) ||
            parse_content_range(cr, &first, &last, &total) != 0 || first != offset) {
            WinHttpCloseHandle(req); WinHttpCloseHandle(conn);
            return -1;
        }
    }

    EnterCriticalSection(&h->lock);
    h->connect = conn;
    h->request = req;
    h->response_complete = 0;
    LeaveCriticalSection(&h->lock);
    return 0;
}

extern "C" void basis_win_http_close(void* ctx) {
    win_http_t* h = (win_http_t*)ctx;
    if (!h) return;
    /* The core joins the reader thread before it gets here, so this drain is
     * normally a no-op — but it is the only thing standing between a reader that
     * outlived its join and the free() below. */
    detach_request_and_drain(h);
    if (h->connect) WinHttpCloseHandle(h->connect);
    if (h->session) WinHttpCloseHandle(h->session);
    DeleteCriticalSection(&h->lock);
    free(h->url);
    free(h);
}
