/* Minimal plaintext HTTP/1.1 GET byte source over basis_io (handles chunked
 * transfer-encoding). Used for http:// live streams on every platform. https://
 * uses the platform TLS stacks instead (WinHTTP on Windows, JNI HttpsURLConnection on
 * Android), so this module is plaintext-only. */
#ifndef BASIS_HTTP_H
#define BASIS_HTTP_H

#include <stdint.h>
#include "basis_url.h"

#ifdef __cplusplus
extern "C" {
#endif

void* basis_http_open(const basis_url_t* url, int timeout_ms);
int   basis_http_read(void* ctx, uint8_t* buf, int len);   /* basis_read_fn-compatible */
void  basis_http_close(void* ctx);

/* Interrupts a read parked on another thread so it returns at once, matching what
 * the WinHTTP and JNI sources offer. Without it a teardown that joins the reader
 * waits out the socket's read timeout instead of returning promptly. Does not free
 * the context; the caller still closes it. Serialise against basis_http_close:
 * abort first, join the reader, then close. Calling abort during or after close
 * touches a freed context — a use-after-free. */
void  basis_http_abort(void* ctx);

#ifdef __cplusplus
}
#endif
#endif
