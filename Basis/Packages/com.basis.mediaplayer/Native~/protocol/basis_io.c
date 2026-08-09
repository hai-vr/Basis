#include "basis_io.h"

#include <string.h>
#include <stdlib.h>
#include <stdio.h>
#include <time.h>

#if defined(_WIN32)
  #include <winsock2.h>
  #include <ws2tcpip.h>
  typedef SOCKET sock_t;
  #define BASIS_INVALID_SOCK INVALID_SOCKET
  #define closesock closesocket
  #define sock_errno WSAGetLastError()
#else
  #include <sys/types.h>
  #include <sys/socket.h>
  #include <netinet/in.h>
  #include <netinet/tcp.h>
  #include <netdb.h>
  #include <unistd.h>
  #include <fcntl.h>
  #include <errno.h>
  #include <sys/time.h>
  #include <poll.h>
  typedef int sock_t;
  #define BASIS_INVALID_SOCK (-1)
  #define closesock close
  #define sock_errno errno
#endif

struct basis_io {
    sock_t fd;
    int read_timeout_ms;   /* last SO_RCVTIMEO set, 0 = untimed; bounds the EINTR retry */
    int send_timeout_ms;   /* last SO_SNDTIMEO set; bounds the EINTR retry in write_full */
};

void basis_io_global_init(void) {
#if defined(_WIN32)
    WSADATA wsa;
    WSAStartup(MAKEWORD(2, 2), &wsa);
#endif
}

void basis_io_global_shutdown(void) {
#if defined(_WIN32)
    WSACleanup();
#endif
}

/* Per-socket settings that must hold from the moment the descriptor exists:
 * close-on-exec, and SIGPIPE suppression on the platforms that want it as a socket
 * option rather than a send flag. Named for both, since it does both.
 *
 * Close-on-exec keeps the socket out of any child process. The client spawns
 * helpers (the URL resolver among them), and a descriptor open at that moment is
 * inherited and stays open for the child's whole life — an in-flight stream fetch
 * or an RTSP control connection held by a process that has no idea it owns one.
 * Set right after creation; SOCK_CLOEXEC would close the window between the two
 * calls, but it is not portable, and nothing here forks. */
/* Windows: WSASocketW sets non-inheritance at creation, closing the window the
 * post-hoc SetHandleInformation in configure_new_socket leaves and the LSP-owned
 * case it can fail on. WSA_FLAG_OVERLAPPED must be passed explicitly — socket()
 * implies it, WSASocketW does not. */
static sock_t create_socket(int family, int type, int proto) {
#if defined(_WIN32)
    sock_t fd = WSASocketW(family, type, proto, NULL, 0,
                           WSA_FLAG_OVERLAPPED | WSA_FLAG_NO_HANDLE_INHERIT);
    if (fd != BASIS_INVALID_SOCK) return fd;
    return socket(family, type, proto);   /* flag rejected; configure_new_socket clears inheritance instead */
#else
    return socket(family, type, proto);
#endif
}

static void configure_new_socket(sock_t fd) {
#if defined(_WIN32)
    SetHandleInformation((HANDLE)fd, HANDLE_FLAG_INHERIT, 0);
#else
    int flags = fcntl(fd, F_GETFD, 0);
    if (flags >= 0) fcntl(fd, F_SETFD, flags | FD_CLOEXEC);
#endif
    /* Apple has no MSG_NOSIGNAL, so the SIGPIPE suppression the send paths get from
     * that flag has to be a socket option here instead. Set alongside close-on-exec
     * because both want to be true from the moment the descriptor exists. */
#if defined(SO_NOSIGPIPE)
    int on = 1;
    setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, (const char*)&on, sizeof(on));
#endif
}

static void set_blocking(sock_t fd, int blocking) {
#if defined(_WIN32)
    u_long mode = blocking ? 0 : 1;
    ioctlsocket(fd, FIONBIO, &mode);
#else
    int flags = fcntl(fd, F_GETFL, 0);
    if (flags < 0) return;
    fcntl(fd, F_SETFL, blocking ? (flags & ~O_NONBLOCK) : (flags | O_NONBLOCK));
#endif
}

void basis_io_set_read_timeout(basis_io_t* io, int timeout_ms) {
    if (!io || io->fd == BASIS_INVALID_SOCK) return;
    io->read_timeout_ms = timeout_ms;
#if defined(_WIN32)
    DWORD tv = (DWORD)timeout_ms;
    setsockopt(io->fd, SOL_SOCKET, SO_RCVTIMEO, (const char*)&tv, sizeof(tv));
#else
    struct timeval tv;
    tv.tv_sec = timeout_ms / 1000;
    tv.tv_usec = (timeout_ms % 1000) * 1000;
    setsockopt(io->fd, SOL_SOCKET, SO_RCVTIMEO, &tv, sizeof(tv));
#endif
}

/* Bound how long a write may block. With no send timeout, a peer that stops reading
 * fills the send buffer and send() then waits for the connection to die — which a
 * peer advertising a zero window and still answering keepalives never lets happen.
 * That wait is on the demux thread, and close joins that thread with no timeout of
 * its own, so it is a client freeze rather than a stalled stream.
 *
 * Ten seconds matches the send timeout the WinHTTP source already sets. Everything
 * written through here is a small request, so there is no legitimate reason to wait
 * longer; the read timeout is the one that has to tolerate a live source going quiet
 * between segments, which is why the two are not the same number. */
#define BASIS_SEND_TIMEOUT_MS 10000

static void set_send_timeout(sock_t fd, int timeout_ms) {
#if defined(_WIN32)
    DWORD tv = (DWORD)timeout_ms;
    setsockopt(fd, SOL_SOCKET, SO_SNDTIMEO, (const char*)&tv, sizeof(tv));
#else
    struct timeval tv;
    tv.tv_sec = timeout_ms / 1000;
    tv.tv_usec = (timeout_ms % 1000) * 1000;
    setsockopt(fd, SOL_SOCKET, SO_SNDTIMEO, &tv, sizeof(tv));
#endif
}

static int local_allowed(void) {
    const char* v = getenv("BASIS_MEDIA_ALLOW_LOCAL");
    return v && v[0];
}

/* The IANA IPv4 Special-Purpose Address Registry entries that are "Globally
 * Reachable: False" — i.e. everything that is not a public unicast destination.
 * Kept in lockstep with the managed guard (BasisMediaPlayerSecurity.IsBlockedAddress)
 * — change both together. See
 * https://www.iana.org/assignments/iana-ipv4-special-registry/ */
static int ipv4_octets_blocked(uint8_t b0, uint8_t b1, uint8_t b2) {
    if (b0 == 0) return 1;                              /* 0/8 "this network" */
    if (b0 == 10) return 1;                             /* 10/8 private */
    if (b0 == 127) return 1;                            /* 127/8 loopback */
    if (b0 == 100 && (b1 & 0xC0) == 64) return 1;       /* 100.64/10 CGNAT */
    if (b0 == 169 && b1 == 254) return 1;               /* 169.254/16 link-local (metadata) */
    if (b0 == 172 && b1 >= 16 && b1 <= 31) return 1;    /* 172.16/12 private */
    if (b0 == 192 && b1 == 0 && b2 == 0) return 1;      /* 192.0.0/24 IETF protocol assignments */
    if (b0 == 192 && b1 == 0 && b2 == 2) return 1;      /* 192.0.2/24 TEST-NET-1 */
    if (b0 == 192 && b1 == 88 && b2 == 99) return 1;    /* 192.88.99/24 6to4 relay anycast (deprecated) */
    if (b0 == 192 && b1 == 168) return 1;               /* 192.168/16 private */
    if (b0 == 198 && (b1 & 0xFE) == 18) return 1;       /* 198.18/15 benchmarking */
    if (b0 == 198 && b1 == 51 && b2 == 100) return 1;   /* 198.51.100/24 TEST-NET-2 */
    if (b0 == 203 && b1 == 0 && b2 == 113) return 1;    /* 203.0.113/24 TEST-NET-3 */
    if (b0 >= 224) return 1;                            /* 224/4 multicast + 240/4 reserved (incl. 255.255.255.255) */
    return 0;
}

/* SSRF guard: reject connecting to a non-global-unicast target. Checks the ACTUAL
 * resolved address, so a public name pointed at a private IP (and DNS rebinding) is
 * caught here at connect time, not just at the URL string. */
static int sockaddr_is_blocked(const struct sockaddr* sa) {
    if (!sa) return 1;
    if (sa->sa_family == AF_INET) {
        const struct sockaddr_in* s = (const struct sockaddr_in*)sa;
        const uint8_t* b = (const uint8_t*)&s->sin_addr.s_addr; /* network order = octets in order */
        return ipv4_octets_blocked(b[0], b[1], b[2]);
    }
    if (sa->sa_family == AF_INET6) {
        const struct sockaddr_in6* s = (const struct sockaddr_in6*)sa;
        const uint8_t* b = (const uint8_t*)s->sin6_addr.s6_addr;
        int i, nz = 0, loop = (b[15] == 1);
        for (i = 0; i < 16; i++) if (b[i]) { nz = 1; break; }
        if (!nz) return 1;                                 /* :: unspecified */
        for (i = 0; i < 15; i++) if (b[i]) { loop = 0; break; }
        if (loop) return 1;                                /* ::1 loopback */
        if ((b[0] & 0xFE) == 0xFC) return 1;               /* fc00::/7 ULA */
        if (b[0] == 0xFE && (b[1] & 0xC0) == 0x80) return 1; /* fe80::/10 link-local */
        if (b[0] == 0xFF) return 1;                        /* ff00::/8 multicast */
        {
            int mapped = 1;
            for (i = 0; i < 10; i++) if (b[i]) { mapped = 0; break; }
            if (mapped && b[10] == 0xFF && b[11] == 0xFF)  /* ::ffff:a.b.c.d */
                return ipv4_octets_blocked(b[12], b[13], b[14]);
        }
        if (b[0] == 0x20 && b[1] == 0x02)                  /* 2002::/16 6to4 */
            return ipv4_octets_blocked(b[2], b[3], b[4]);
        return 0;
    }
    return 1;
}

int basis_io_host_is_blocked(const char* host) {
    if (!host || !host[0]) return 1;
    if (local_allowed()) return 0;

    /* Callers hand over whatever the URL's authority carried, and an IPv6 literal
     * is written there in brackets. getaddrinfo wants the bare address, so a
     * bracketed host would otherwise fail to resolve and be refused as unknown —
     * safe, but it would make every IPv6-literal URL unplayable. */
    char bare[256];
    size_t hl = strlen(host);
    if (host[0] == '[') {
        if (hl < 3 || host[hl - 1] != ']' || hl - 2 >= sizeof(bare)) return 1;
        memcpy(bare, host + 1, hl - 2);
        bare[hl - 2] = 0;
        host = bare;
    }

    struct addrinfo hints, *res = NULL, *ai;
    memset(&hints, 0, sizeof(hints));
    hints.ai_family = AF_UNSPEC;
    hints.ai_socktype = SOCK_STREAM;
    /* Fail closed: a name we can't resolve here could resolve to a private
     * address at the platform stack's own lookup a moment later. */
    if (getaddrinfo(host, NULL, &hints, &res) != 0 || !res) return 1;

    /* Block if ANY resolved address is non-global — a host with both a public
     * and a private record must not be usable to reach the private one. */
    int blocked = 0;
    for (ai = res; ai; ai = ai->ai_next)
        if (sockaddr_is_blocked(ai->ai_addr)) { blocked = 1; break; }
    freeaddrinfo(res);
    return blocked;
}

int basis_io_resolve_checked(const char* host, char* out_ip, int out_cap, int* out_family) {
    if (!host || !host[0] || !out_ip || out_cap <= 0) return -1;

    /* Same bracket handling as basis_io_host_is_blocked: a URL authority hands an
     * IPv6 literal over in brackets, but getaddrinfo wants it bare. */
    char bare[256];
    size_t hl = strlen(host);
    if (host[0] == '[') {
        if (hl < 3 || host[hl - 1] != ']' || hl - 2 >= sizeof(bare)) return -1;
        memcpy(bare, host + 1, hl - 2);
        bare[hl - 2] = 0;
        host = bare;
    }

    struct addrinfo hints, *res = NULL, *ai;
    memset(&hints, 0, sizeof(hints));
    hints.ai_family = AF_UNSPEC;
    hints.ai_socktype = SOCK_DGRAM;
    if (getaddrinfo(host, NULL, &hints, &res) != 0 || !res) return -1;

    /* Pick the first allowed address, exactly as basis_io_connect does: skip a
     * non-global one rather than blocking the whole name, since pinning to the
     * chosen literal means the skipped address can never be reached. */
    int allow_local = local_allowed();
    int rc = -1;
    for (ai = res; ai; ai = ai->ai_next) {
        if (!allow_local && sockaddr_is_blocked(ai->ai_addr)) continue;
        if (getnameinfo(ai->ai_addr, (socklen_t)ai->ai_addrlen, out_ip, (socklen_t)out_cap,
                        NULL, 0, NI_NUMERICHOST) != 0) continue;
        if (out_family) *out_family = ai->ai_family;
        rc = 0;
        break;
    }
    freeaddrinfo(res);
    return rc;
}

basis_io_t* basis_io_connect(const char* host, int port, int timeout_ms) {
    if (!host || port <= 0) return NULL;

    char portstr[16];
    snprintf(portstr, sizeof(portstr), "%d", port);

    struct addrinfo hints, *res = NULL, *ai;
    memset(&hints, 0, sizeof(hints));
    hints.ai_family = AF_UNSPEC;
    hints.ai_socktype = SOCK_STREAM;
    hints.ai_protocol = IPPROTO_TCP;
    if (getaddrinfo(host, portstr, &hints, &res) != 0 || !res) return NULL;

    sock_t fd = BASIS_INVALID_SOCK;
    int allow_local = local_allowed();
    for (ai = res; ai; ai = ai->ai_next) {
        if (!allow_local && sockaddr_is_blocked(ai->ai_addr)) continue;
        fd = create_socket(ai->ai_family, ai->ai_socktype, ai->ai_protocol);
        if (fd == BASIS_INVALID_SOCK) continue;
        configure_new_socket(fd);

        /* non-blocking connect with select() timeout */
        set_blocking(fd, 0);
        int rc = connect(fd, ai->ai_addr, (int)ai->ai_addrlen);
        int inprogress = 0;
#if defined(_WIN32)
        inprogress = (rc != 0 && sock_errno == WSAEWOULDBLOCK);
#else
        inprogress = (rc != 0 && (errno == EINPROGRESS || errno == EWOULDBLOCK));
#endif
        if (rc == 0) {
            set_blocking(fd, 1);
            break;
        }
        if (inprogress) {
            fd_set wf;
            FD_ZERO(&wf);
            FD_SET(fd, &wf);
            struct timeval tv;
            tv.tv_sec = timeout_ms / 1000;
            tv.tv_usec = (timeout_ms % 1000) * 1000;
            int sel = select((int)fd + 1, NULL, &wf, NULL, timeout_ms > 0 ? &tv : NULL);
            if (sel > 0) {
                int err = 0;
                socklen_t elen = sizeof(err);
                /* The return matters as much as the value it writes: err is
                 * pre-zeroed, so a failed call would otherwise read as a
                 * successful connect and hand back a socket that never came up. */
                int got = getsockopt(fd, SOL_SOCKET, SO_ERROR, (char*)&err, &elen);
                if (got == 0 && err == 0) {
                    set_blocking(fd, 1);
                    break;
                }
            }
        }
        closesock(fd);
        fd = BASIS_INVALID_SOCK;
    }
    freeaddrinfo(res);

    if (fd == BASIS_INVALID_SOCK) return NULL;

    int one = 1;
    setsockopt(fd, IPPROTO_TCP, TCP_NODELAY, (const char*)&one, sizeof(one));
    set_send_timeout(fd, BASIS_SEND_TIMEOUT_MS);

    basis_io_t* io = (basis_io_t*)calloc(1, sizeof(*io));
    if (!io) { closesock(fd); return NULL; }
    io->fd = fd;
    io->send_timeout_ms = BASIS_SEND_TIMEOUT_MS;   /* matches the option set above */
    basis_io_set_read_timeout(io, timeout_ms > 0 ? timeout_ms : 15000);
    return io;
}

int basis_io_read(basis_io_t* io, uint8_t* buf, int len) {
    if (!io || io->fd == BASIS_INVALID_SOCK || !buf || len <= 0) return -1;
#if defined(_WIN32)
    return (int)recv(io->fd, (char*)buf, len, 0);   /* Winsock has no EINTR */
#else
    /* A signal delivered without SA_RESTART interrupts the wait and returns
     * -1/EINTR on a socket that is perfectly healthy, which callers read as a
     * dead peer and abandon the stream over. So retry — but bounded, because
     * each retry re-arms SO_RCVTIMEO and a repeating signal would otherwise turn
     * a read the caller asked to be timed into an untimed one. Same rule as
     * basis_io_poll_read, and giving up looks exactly like the timeout that was
     * asked for. An untimed socket has no deadline to preserve, so it retries.
     *
     * The deadline is tested after recv returns, and the retry gets a fresh
     * SO_RCVTIMEO rather than the remainder, so a signal arriving just short of
     * the deadline can take the total to ~2x read_timeout_ms. Callers sizing
     * their own deadlines off this value should treat it as the bound. Re-arming
     * with the remainder would tighten it at the cost of a setsockopt per
     * signal, which is the wrong trade for a path that exists to absorb a rare
     * interruption. */
    struct timespec t0;
    int clock_ok = (clock_gettime(CLOCK_MONOTONIC, &t0) == 0);
    for (;;) {
        int n = (int)recv(io->fd, (char*)buf, len, 0);
        if (n >= 0 || errno != EINTR) return n;
        if (io->read_timeout_ms > 0) {
            struct timespec t1;
            if (!clock_ok || clock_gettime(CLOCK_MONOTONIC, &t1) != 0) return -1;
            long elapsed_ms = (long)((t1.tv_sec - t0.tv_sec) * 1000 +
                                     (t1.tv_nsec - t0.tv_nsec) / 1000000);
            if (elapsed_ms >= io->read_timeout_ms) { errno = EAGAIN; return -1; }
        }
    }
#endif
}

int basis_io_read_full(basis_io_t* io, uint8_t* buf, int len) {
    int got = 0;
    while (got < len) {
        int n = basis_io_read(io, buf + got, len - got);
        if (n <= 0) return got;
        got += n;
    }
    return got;
}

/* A write must not be able to raise SIGPIPE. The default disposition terminates the
 * process, and a peer that half-closes or resets the connection chooses when that
 * happens — so on the RTSP and RTMP control connections it is a remote kill of the
 * whole client, not just the stream. Linux and Android take a per-send flag; Apple
 * has no such flag and uses SO_NOSIGPIPE on the socket instead (set at connect);
 * Winsock has no SIGPIPE at all. */
#if defined(MSG_NOSIGNAL)
#define BASIS_SEND_FLAGS MSG_NOSIGNAL
#else
#define BASIS_SEND_FLAGS 0
#endif

int basis_io_write_full(basis_io_t* io, const uint8_t* buf, int len) {
    if (!io || io->fd == BASIS_INVALID_SOCK || !buf) return -1;
    int sent = 0;
#if !defined(_WIN32)
    /* Same rule as basis_io_read, on the other direction. An interrupted send moved
     * nothing, so reissuing it is the correct response — and every caller here
     * treats a short write as a dead connection, so not retrying tears down a
     * working session over a signal. Bounded against the send deadline so a
     * repeating signal cannot turn a timed write into an untimed one. */
    struct timespec t0;
    int clock_ok = (clock_gettime(CLOCK_MONOTONIC, &t0) == 0);
#endif
    while (sent < len) {
        int n = (int)send(io->fd, (const char*)buf + sent, len - sent, BASIS_SEND_FLAGS);
        if (n > 0) { sent += n; continue; }
#if !defined(_WIN32)
        if (n < 0 && errno == EINTR) {
            struct timespec t1;
            if (!clock_ok || clock_gettime(CLOCK_MONOTONIC, &t1) != 0) return -1;
            long elapsed_ms = (long)((t1.tv_sec - t0.tv_sec) * 1000 +
                                     (t1.tv_nsec - t0.tv_nsec) / 1000000);
            /* The recorded deadline, not the default: a caller that shortens it
             * means the whole write, and this retry loop is part of the write. */
            int budget = io->send_timeout_ms > 0 ? io->send_timeout_ms
                                                 : BASIS_SEND_TIMEOUT_MS;
            if (elapsed_ms >= budget) return -1;
            continue;
        }
#endif
        return -1;
    }
    return sent;
}

void basis_io_close(basis_io_t* io) {
    if (!io) return;
    if (io->fd != BASIS_INVALID_SOCK) closesock(io->fd);
    free(io);
}

void basis_io_set_send_timeout(basis_io_t* io, int timeout_ms) {
    if (!io || io->fd == BASIS_INVALID_SOCK) return;
    /* Recorded as well as applied: SO_SNDTIMEO bounds one send() call, while the
     * EINTR retry in basis_io_write_full bounds the whole write, and a caller that
     * shortens the deadline means both. */
    io->send_timeout_ms = timeout_ms;
    set_send_timeout(io->fd, timeout_ms);
}

void basis_io_shutdown(basis_io_t* io) {
    if (!io || io->fd == BASIS_INVALID_SOCK) return;
    /* Deliberately not a close: the reader is about to be joined and would then be
     * holding a freed descriptor, or a recycled one. A shutdown makes the parked
     * read return immediately and leaves the descriptor valid until its owner
     * closes it on the normal path. */
#if defined(_WIN32)
    shutdown(io->fd, SD_BOTH);
#else
    shutdown(io->fd, SHUT_RDWR);
#endif
}

int basis_io_peer_addr(basis_io_t* io, char* buf, int cap) {
    if (!io || io->fd == BASIS_INVALID_SOCK || !buf || cap <= 0) return -1;
    struct sockaddr_storage ss;
    socklen_t slen = sizeof(ss);
    if (getpeername(io->fd, (struct sockaddr*)&ss, &slen) != 0) return -1;
    return getnameinfo((struct sockaddr*)&ss, slen, buf, (socklen_t)cap,
                       NULL, 0, NI_NUMERICHOST) == 0 ? 0 : -1;
}

/* One UDP socket bound to local_port in the given family. Fails (returns
 * invalid) on bind conflicts so the caller can try another local pair. */
static sock_t udp_bind_one(int family, int local_port) {
    sock_t fd = create_socket(family, SOCK_DGRAM, IPPROTO_UDP);
    if (fd == BASIS_INVALID_SOCK) return BASIS_INVALID_SOCK;
    configure_new_socket(fd);

    struct sockaddr_storage local;
    memset(&local, 0, sizeof(local));
    socklen_t llen;
    if (family == AF_INET) {
        struct sockaddr_in* l4 = (struct sockaddr_in*)&local;
        l4->sin_family = AF_INET;
        l4->sin_port = htons((unsigned short)local_port);
        llen = sizeof(*l4);
    } else {
        struct sockaddr_in6* l6 = (struct sockaddr_in6*)&local;
        l6->sin6_family = AF_INET6;
        l6->sin6_port = htons((unsigned short)local_port);
        llen = sizeof(*l6);
    }
    if (bind(fd, (const struct sockaddr*)&local, llen) != 0) { closesock(fd); return BASIS_INVALID_SOCK; }
    return fd;
}

int basis_io_udp_open_pair(const char* host, basis_io_t** rtp, basis_io_t** rtcp,
                           int* local_rtp_port) {
    if (!host || !rtp || !rtcp) return -1;
    *rtp = *rtcp = NULL;

    /* Resolve only to pick the address family the connect target will need. */
    struct addrinfo hints, *res = NULL;
    memset(&hints, 0, sizeof(hints));
    hints.ai_family = AF_UNSPEC;
    hints.ai_socktype = SOCK_DGRAM;
    hints.ai_protocol = IPPROTO_UDP;
    if (getaddrinfo(host, NULL, &hints, &res) != 0 || !res) return -1;
    int family = res->ai_family;
    freeaddrinfo(res);
    if (family != AF_INET && family != AF_INET6) return -1;

    sock_t frtp = BASIS_INVALID_SOCK, frtcp = BASIS_INVALID_SOCK;
    int chosen_port = 0;

    /* Even/odd local pair from a 1024-pair window, probed from a randomised
     * start so concurrent sessions and processes don't all contend from the
     * same base; a failed bind steps to the next pair (UDP has no TIME_WAIT —
     * a conflict means the port is genuinely in use). SO_REUSEADDR is
     * deliberately absent: sharing a bound port would route another session's
     * datagrams here. */
    enum { PORT_BASE = 46000, PORT_PAIRS = 1024 };
    /* The salt is shared across demux threads (split-stream sessions negotiate
     * on two); increment atomically so the shared counter stays defined. */
#if defined(_WIN32)
    static volatile LONG probe_salt;
    unsigned salt = (unsigned)InterlockedIncrement(&probe_salt);
#else
    static unsigned probe_salt;
    unsigned salt = __atomic_add_fetch(&probe_salt, 1u, __ATOMIC_RELAXED);
#endif
    unsigned start = ((unsigned)(uintptr_t)&hints ^ (unsigned)time(NULL) ^ salt) % PORT_PAIRS;
    for (int i = 0; i < PORT_PAIRS; ++i) {
        int base = PORT_BASE + 2 * (int)((start + (unsigned)i) % PORT_PAIRS);
        frtp = udp_bind_one(family, base);
        if (frtp == BASIS_INVALID_SOCK) continue;
        frtcp = udp_bind_one(family, base + 1);
        if (frtcp == BASIS_INVALID_SOCK) { closesock(frtp); frtp = BASIS_INVALID_SOCK; continue; }
        chosen_port = base;
        break;
    }
    if (frtp == BASIS_INVALID_SOCK) return -1;

    basis_io_t* a = (basis_io_t*)calloc(1, sizeof(*a));
    basis_io_t* b = (basis_io_t*)calloc(1, sizeof(*b));
    if (!a || !b) { free(a); free(b); closesock(frtp); closesock(frtcp); return -1; }
    a->fd = frtp; b->fd = frtcp;
    *rtp = a; *rtcp = b;
    if (local_rtp_port) *local_rtp_port = chosen_port;
    return 0;
}

int basis_io_udp_connect(basis_io_t* io, const char* host, int port) {
    if (!io || io->fd == BASIS_INVALID_SOCK || !host || port <= 0) return -1;

    char portstr[16];
    snprintf(portstr, sizeof(portstr), "%d", port);

    struct addrinfo hints, *res = NULL, *ai;
    memset(&hints, 0, sizeof(hints));
    hints.ai_family = AF_UNSPEC;
    hints.ai_socktype = SOCK_DGRAM;
    hints.ai_protocol = IPPROTO_UDP;
    if (getaddrinfo(host, portstr, &hints, &res) != 0 || !res) return -1;

    int allow_local = local_allowed();
    int rc = -1;
    for (ai = res; ai; ai = ai->ai_next) {
        if (!allow_local && sockaddr_is_blocked(ai->ai_addr)) continue;
        if (connect(io->fd, ai->ai_addr, (int)ai->ai_addrlen) == 0) { rc = 0; break; }
    }
    freeaddrinfo(res);
    return rc;
}

int basis_io_send(basis_io_t* io, const uint8_t* buf, int len) {
    if (!io || io->fd == BASIS_INVALID_SOCK || !buf || len < 0) return -1;
    return (int)send(io->fd, (const char*)buf, len, BASIS_SEND_FLAGS);
}

int basis_io_poll_read(basis_io_t** ios, int n, int timeout_ms) {
    if (!ios || n <= 0 || n > 8) return -1;
#if defined(_WIN32)
    /* Winsock reports a pending socket error (e.g. ICMP port-unreachable on a
     * connected UDP socket) in the exception set, not readfds — fold it into
     * the readable mask so the next read surfaces the error promptly, matching
     * the POSIX POLLERR handling below and the contract in basis_io.h. */
    fd_set rf, ef;
    FD_ZERO(&rf);
    FD_ZERO(&ef);
    sock_t maxfd = 0;
    for (int i = 0; i < n; ++i) {
        if (!ios[i] || ios[i]->fd == BASIS_INVALID_SOCK) continue;
        FD_SET(ios[i]->fd, &rf);
        FD_SET(ios[i]->fd, &ef);
        if (ios[i]->fd > maxfd) maxfd = ios[i]->fd;
    }
    struct timeval tv;
    tv.tv_sec = timeout_ms / 1000;
    tv.tv_usec = (timeout_ms % 1000) * 1000;
    int rc = select((int)maxfd + 1, &rf, NULL, &ef, &tv);
    if (rc < 0) return -1;
    if (rc == 0) return 0;
    int mask = 0;
    for (int i = 0; i < n; ++i)
        if (ios[i] && ios[i]->fd != BASIS_INVALID_SOCK &&
            (FD_ISSET(ios[i]->fd, &rf) || FD_ISSET(ios[i]->fd, &ef))) mask |= 1 << i;
    return mask;
#else
    struct pollfd pf[8];
    int map[8];
    int np = 0;
    for (int i = 0; i < n; ++i) {
        if (!ios[i] || ios[i]->fd == BASIS_INVALID_SOCK) continue;
        pf[np].fd = ios[i]->fd;
        pf[np].events = POLLIN;
        pf[np].revents = 0;
        map[np] = i;
        np++;
    }
    if (np == 0) return 0;
    /* An interrupted wait is not a failure, and here it costs more than a lost
     * read: the RTSP session loop reads -1 from this as a socket error and gives
     * up on UDP for the whole host. Retry on what is left of the caller's
     * deadline, so repeated signals cannot extend the wait either. */
    struct timespec t0;
    int clock_ok = (clock_gettime(CLOCK_MONOTONIC, &t0) == 0);
    int remaining = timeout_ms;
    int rc;
    for (;;) {
        rc = poll(pf, (nfds_t)np, remaining);
        if (rc >= 0) break;
        if (errno != EINTR) return -1;
        if (timeout_ms > 0) {
            /* Without a clock there is no way to retry inside the caller's
             * deadline, and re-arming the full timeout on every signal would
             * extend a wait that is meant to be bounded. Report the interruption
             * as an expiry instead: callers poll again, so nothing is lost but
             * one round trip. */
            struct timespec t1;
            if (!clock_ok || clock_gettime(CLOCK_MONOTONIC, &t1) != 0) return 0;
            long elapsed_ms = (long)((t1.tv_sec - t0.tv_sec) * 1000 +
                                     (t1.tv_nsec - t0.tv_nsec) / 1000000);
            remaining = timeout_ms - (int)elapsed_ms;
            if (remaining <= 0) return 0;
        }
        /* timeout_ms <= 0 is an unbounded wait, so there is no deadline to keep. */
    }
    if (rc == 0) return 0;
    int mask = 0;
    for (int i = 0; i < np; ++i)
        if (pf[i].revents & (POLLIN | POLLERR | POLLHUP)) mask |= 1 << map[i];
    return mask;
#endif
}
