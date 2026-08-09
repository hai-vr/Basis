#include "basis_http.h"
#include "basis_io.h"

#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <ctype.h>
#include <errno.h>

typedef struct {
    basis_io_t* io;
    int chunked;
    int has_length;
    int te_seen;    /* a Transfer-Encoding header was seen (reject a second) */
    long long remaining;   /* content-length remaining, or current chunk remaining */
    int eof;

    /* leftover bytes already pulled from the socket past the header boundary */
    uint8_t lead[8192];
    int lead_len;
    int lead_pos;
} http_ctx;

static int lead_take(http_ctx* h, uint8_t* dst, int len) {
    int avail = h->lead_len - h->lead_pos;
    if (avail <= 0) return 0;
    int n = len < avail ? len : avail;
    memcpy(dst, h->lead + h->lead_pos, (size_t)n);
    h->lead_pos += n;
    return n;
}

/* Bytes past the caller's buffer that a single line may discard before the line
 * is treated as hostile rather than merely long. */
#define HTTP_LINE_DISCARD_MAX 8192

/* Ceilings on one response's header block. Same values as the RTSP reader uses. */
#define HTTP_MAX_HEADERS      100
#define HTTP_MAX_HEADER_BYTES (16 * 1024)

/* read one line (up to CRLF) using leftover + socket; returns line length w/o CRLF */
static int read_line(http_ctx* h, char* out, int cap) {
    int n = 0, dropped = 0;
    for (;;) {
        uint8_t c;
        int got = lead_take(h, &c, 1);
        if (got == 0) got = basis_io_read(h->io, &c, 1);
        /* No newline was reached, so whatever is buffered is a fragment of a
         * line rather than a line. Returning it would hand the caller a
         * half-received header as though it were complete, which is the case a
         * boundary-aligned check misses. */
        if (got <= 0) return -1;
        if (c == '\n') break;
        if (c == '\r') continue;
        /* Read to the newline whatever the length, and drop the excess, rather
         * than stopping at the buffer end. Stopping there left the newline
         * unconsumed, so the tail of an over-long line was returned as the next
         * header -- one field the server wrote becoming two the parser sees,
         * which is the peer choosing our framing rather than our parser
         * choosing it.
         *
         * Truncating rather than failing keeps a legitimately long header (a
         * signed URL echoed back, a fat CDN trace field) playable, since none of
         * those are fields this parser acts on. The discard is bounded so a
         * single endless line cannot spin here forever. */
        if (n < cap - 1) out[n++] = (char)c;
        else if (++dropped > HTTP_LINE_DISCARD_MAX) return -1;
    }
    out[n] = 0;
    return n;
}

void* basis_http_open(const basis_url_t* url, int timeout_ms) {
    if (!url) return NULL;
    http_ctx* h = (http_ctx*)calloc(1, sizeof(*h));
    if (!h) return NULL;

    h->io = basis_io_connect(url->host, url->port, timeout_ms);
    if (!h->io) { free(h); return NULL; }

    /* Sized to hold the largest request the URL parser can produce: the fixed
     * part is 96 bytes, basis_url_t carries path[2048] and host[256], so 2400 is
     * the ceiling. At 2048 this buffer could not represent a legal request at all,
     * and every over-long one truncated. */
    char req[4096];
    int rl = snprintf(req, sizeof(req),
        "GET %s HTTP/1.1\r\n"
        "Host: %s\r\n"
        "User-Agent: BasisMediaPlayer/1.0\r\n"
        "Accept: */*\r\n"
        "Connection: keep-alive\r\n"
        "\r\n",
        url->path[0] ? url->path : "/", url->host);
    /* snprintf answers the length the output *would* have needed, not what it
     * wrote, so on truncation rl runs past the end of req -- and it was handed
     * straight to the write as a length, sending the adjacent stack to the far
     * end. Both ends are checked: a negative return is an encoding error, and
     * anything at or past the buffer size did not fit. The size above means
     * neither can happen from a parsed URL; they are checked because the cost of
     * being wrong is a remote memory disclosure. */
    if (rl < 0 || rl >= (int)sizeof(req)) {
        basis_io_close(h->io); free(h); return NULL;
    }
    if (basis_io_write_full(h->io, (const uint8_t*)req, rl) != rl) {
        basis_io_close(h->io); free(h); return NULL;
    }

    /* status line */
    char line[1024];
    if (read_line(h, line, sizeof(line)) < 0) { basis_io_close(h->io); free(h); return NULL; }
    int code = 0;
    { const char* sp = strchr(line, ' '); if (sp) code = atoi(sp + 1); }
    if (code < 200 || code >= 400) { basis_io_close(h->io); free(h); return NULL; }

    /* headers
     *
     * Bounded on both count and volume. The loop used to end only on a blank line
     * or a socket failure, and SO_RCVTIMEO is a per-read idle timeout that every
     * arriving byte resets -- so a server answering with an endless run of
     * "X: y\r\n" kept it turning forever, with no memory growth to show for it and
     * nothing to make it stop. That is on the demux thread, which close joins with
     * no timeout of its own.
     *
     * Same numbers as the RTSP reader, deliberately: the two are the same defect on
     * two protocols and a reader who has seen one should recognise the other. The
     * byte count is of stored header text; read_line separately bounds what one
     * over-long line may discard, so the two caps together bound the total read.
     *
     * No is_running check, unlike RTSP: this entry point has no sink to ask. The
     * caps alone end the loop, which is what closes the hang; cooperative
     * cancellation here would mean changing the byte-source signature. */
    h->has_length = 0; h->chunked = 0; h->te_seen = 0; h->remaining = -1;
    int nheaders = 0, hbytes = 0;
    for (;;) {
        int ll = read_line(h, line, sizeof(line));
        /* read_line answers -1 for a dead socket and 0 for the blank line that
         * ends the block. Folding them together accepted a response that was cut
         * off mid-headers as a complete one, and the body read below then ran
         * against whatever Content-Length had been seen so far. */
        if (ll < 0) { basis_io_close(h->io); free(h); return NULL; }
        if (ll == 0) break;
        if (++nheaders > HTTP_MAX_HEADERS) { basis_io_close(h->io); free(h); return NULL; }
        hbytes += ll;
        if (hbytes > HTTP_MAX_HEADER_BYTES) { basis_io_close(h->io); free(h); return NULL; }
        /* lowercase the header name for comparison */
        char low[256]; int i = 0;
        for (; line[i] && line[i] != ':' && i < (int)sizeof(low) - 1; ++i) low[i] = (char)tolower((unsigned char)line[i]);
        low[i] = 0;
        const char* val = strchr(line, ':');
        if (val) { val++; while (*val == ' ') val++; }
        if (strcmp(low, "content-length") == 0 && val) {
            /* A second Content-Length is a framing conflict (RFC 7230 §3.3.3),
             * not a value to overwrite: a proxy and this client can then frame
             * two different bodies from one response. Refuse rather than pick. */
            if (h->has_length) { basis_io_close(h->io); free(h); return NULL; }
            /* Same strictness as the chunk size below, and for the same reason.
             * atoll takes a numeric prefix, answers 0 for anything unparseable,
             * and is undefined past LLONG_MAX — so a malformed value set the
             * length flag with nothing behind it and the body read reported a
             * clean end of stream for a response the server never terminated. */
            char* cl_end = NULL;
            errno = 0;
            long long cl = strtoll(val, &cl_end, 10);
            /* Whether anything parsed is recorded BEFORE the trailing-space skip.
             * strtoll leaves cl_end == val when it converts nothing, but it also
             * skips leading whitespace of its own — so for a value of just a tab
             * the skip below would walk cl_end off val and onto the terminator, and
             * the "did anything parse" test would then pass on a field with no
             * digits in it at all. That framed the body at zero and reported a
             * clean end of stream for a response the server said had one. */
            int had_digits = (cl_end != val);
            /* The field is digits and nothing else (RFC 7230 puts 1*DIGIT here),
             * so a numeric prefix is not enough: "5junk" must fail rather than
             * frame the body at 5. Only trailing spacing is tolerated. */
            while (*cl_end == ' ' || *cl_end == '\t') ++cl_end;
            if (!had_digits || *cl_end || errno == ERANGE || cl < 0) { basis_io_close(h->io); free(h); return NULL; }
            h->has_length = 1;
            h->remaining = cl;
        }
        else if (strcmp(low, "transfer-encoding") == 0 && val) {
            /* A repeated Transfer-Encoding is a framing conflict, like a repeated
             * Content-Length. And the whole value must be exactly "chunked": we
             * strip chunk framing but decode no other coding, so "gzip, chunked"
             * (last token chunked, still gzip-compressed underneath) would hand
             * the demuxer a body it can't read. Refuse anything but bare chunked. */
            if (h->te_seen++) { basis_io_close(h->io); free(h); return NULL; }
            const char* s = val;
            while (*s == ' ' || *s == '\t') ++s;
            size_t ln = strlen(s);
            while (ln && (s[ln - 1] == ' ' || s[ln - 1] == '\t' ||
                          s[ln - 1] == '\r' || s[ln - 1] == '\n')) --ln;
            int is_chunked = ln == 7;
            for (size_t i = 0; is_chunked && i < 7; ++i)
                if (tolower((unsigned char)s[i]) != "chunked"[i]) is_chunked = 0;
            if (!is_chunked) { basis_io_close(h->io); free(h); return NULL; }
            h->chunked = 1;
        }
    }

    /* Content-Length with Transfer-Encoding is the same conflict across two
     * fields (RFC 7230 §3.3.3) — refuse rather than let chunking silently win. */
    if (h->chunked && h->has_length) { basis_io_close(h->io); free(h); return NULL; }

    /* For chunked, remaining starts at 0 meaning "read next chunk size". */
    if (h->chunked) h->remaining = 0;
    return h;
}

/* read the next chunk-size line for chunked encoding */
static int next_chunk(http_ctx* h) {
    char line[64];
    /* a CRLF trails each chunk's data; consume any stray leading CRLF */
    int ll = read_line(h, line, sizeof(line));
    if (ll < 0) return -1;
    if (ll == 0) { /* trailing CRLF after previous chunk -> read the size line */
        ll = read_line(h, line, sizeof(line));
        if (ll < 0) return -1;
    }
    /* Only a well-formed size is a size. Unchecked, a line with no hex digits at
     * all parsed as 0 and was taken for the terminal chunk, so a corrupted or
     * truncated response was delivered as a complete one. A chunk-extension
     * (";name=value") legitimately follows the digits, so the parse stops there
     * rather than requiring the line to end.
     *
     * Both ends are tested because strtol is more permissive than the grammar.
     * At base 16 it skips whitespace and accepts a sign and an 0x prefix, so
     * "-0" parses to 0 and would be taken for the terminal chunk while passing
     * the negative test; and it stops at the first character it cannot use, so
     * "5junk" would frame a chunk at 5. The grammar is hex digits, then either a
     * chunk-extension or the end of the line, so require exactly that. */
    if (!isxdigit((unsigned char)line[0])) return -1;
    if (line[0] == '0' && (line[1] == 'x' || line[1] == 'X')) return -1;
    char* end = NULL;
    errno = 0;
    long sz = strtol(line, &end, 16);
    if (end == line || errno == ERANGE || sz < 0) return -1;
    while (*end == ' ' || *end == '\t') ++end;
    if (*end && *end != ';') return -1;
    if (sz == 0) { h->eof = 1; return 0; }
    h->remaining = sz;
    return 1;
}

int basis_http_read(void* ctx, uint8_t* buf, int len) {
    http_ctx* h = (http_ctx*)ctx;
    if (!h || h->eof || len <= 0) return 0;

    if (h->chunked) {
        if (h->remaining <= 0) {
            int r = next_chunk(h);
            if (r <= 0) return r; /* 0 = EOF, -1 = error */
        }
        int want = (long long)len < h->remaining ? len : (int)h->remaining;
        int got = lead_take(h, buf, want);
        if (got == 0) got = basis_io_read(h->io, buf, want);
        if (got <= 0) { h->eof = 1; return got; }
        h->remaining -= got;
        return got;
    }

    if (h->has_length) {
        if (h->remaining <= 0) { h->eof = 1; return 0; }
        int want = (long long)len < h->remaining ? len : (int)h->remaining;
        int got = lead_take(h, buf, want);
        if (got == 0) got = basis_io_read(h->io, buf, want);
        if (got <= 0) { h->eof = 1; return got; }
        h->remaining -= got;
        return got;
    }

    /* no length, not chunked: stream until the server closes */
    int got = lead_take(h, buf, len);
    if (got == 0) got = basis_io_read(h->io, buf, len);
    if (got <= 0) { h->eof = 1; return got; }
    return got;
}

void basis_http_abort(void* ctx) {
    http_ctx* h = (http_ctx*)ctx;
    if (!h) return;
    if (h->io) basis_io_shutdown(h->io);
}

void basis_http_close(void* ctx) {
    http_ctx* h = (http_ctx*)ctx;
    if (!h) return;
    if (h->io) basis_io_close(h->io);
    free(h);
}
