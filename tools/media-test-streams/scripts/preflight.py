#!/usr/bin/env python3
"""Preflight probe for the test-stream stack.

Run BEFORE any media-player test session (~30s):

    python3 preflight.py                  # probe everything on localhost
    python3 preflight.py --host my.vps    # probe a deployed stack
    python3 preflight.py --host my.vps main captions   # matching lanes only

Catches sick feeders in seconds instead of after a confusing editor session:
a symptom seen in the player only earns player-side investigation time if the
preflight is green.

Requires ffmpeg + ffprobe on PATH. Do NOT use curl.exe on Windows for
throughput checks (it under-reads badly); this script uses urllib.
"""
import argparse
import json
import subprocess
import time
import urllib.request

# (name, kind, path_template, threshold)
#   rtsp: threshold = max seconds to first decoded frame (None = informational;
#         slowjoin waits for an IDR in a ~10s GOP by design)
#   http: threshold = min Mbps over a short sample (catches stale ~0.5Mbps
#         feeder slots, not nominal bitrate — -re paced feeds dip on quiet scenes)
#   vod:  threshold unused; checks for a real 206 Partial Content
LANES = [
    ("main",     "rtsp", "rtsp://{host}:8554/main",          6.0),
    ("silent",   "rtsp", "rtsp://{host}:8554/silent",        6.0),
    ("slowjoin", "rtsp", "rtsp://{host}:8554/slowjoin",      None),
    ("rtmp",     "rtsp", "rtmp://{host}:1935/main",          6.0),
    ("vod",      "vod",  "http://{host}:8080/assets/mezzanine.mp4", None),
    ("hls-vod",  "vod",  "http://{host}:8080/assets/hls/index.m3u8", None),
    ("tslive",   "http", "http://{host}:8081/live.ts",       1.0),
    ("captions", "http", "http://{host}:8082/captions.ts",   1.0),
]


def probe_rtsp(name, url, limit):
    """Time-to-first-decoded-video-frame + stream shape (RTSP/TCP or RTMP)."""
    transport = ["-rtsp_transport", "tcp"] if url.startswith("rtsp") else []
    t0 = time.monotonic()
    try:
        out = subprocess.run(
            ["ffmpeg", "-v", "error"] + transport + ["-i", url,
             "-map", "0:v:0", "-frames:v", "1", "-f", "null", "-"],
            capture_output=True, text=True, timeout=25)
    except subprocess.TimeoutExpired:
        return f"FAIL  {name}: no decodable video frame within 25s"
    ttff = time.monotonic() - t0
    if out.returncode != 0:
        err = [l for l in out.stderr.strip().splitlines()
               if "Missing reference" not in l and "mmco" not in l]
        return f"FAIL  {name}: {err[-1] if err else 'ffmpeg error'}"
    streams = subprocess.run(
        ["ffprobe", "-v", "error"] + transport + ["-show_entries",
         "stream=codec_type,codec_name,channels", "-of", "json", url],
        capture_output=True, text=True, timeout=25)
    shape = ",".join(
        f"{s['codec_name']}({s.get('channels')}ch)" if s["codec_type"] == "audio"
        else s["codec_name"]
        for s in json.loads(streams.stdout or '{"streams":[]}')["streams"])
    verdict = "PASS" if (limit is None or ttff < limit) else "SLOW"
    note = "" if limit else "  (long-GOP adversarial path: slow join is expected)"
    return f"{verdict}  {name}: first frame {ttff:.1f}s, streams [{shape}]{note}"


def probe_http(name, url, min_mbps, seconds=5):
    """First-byte latency + short bitrate sample. Consumes a single-client
    feeder slot; the container respawns a fresh one on disconnect."""
    t0 = time.monotonic()
    try:
        resp = urllib.request.urlopen(url, timeout=10)
        first = resp.read(64 * 1024)
        fb = time.monotonic() - t0
        total = len(first)
        t1 = time.monotonic()
        while time.monotonic() - t1 < seconds:
            chunk = resp.read(256 * 1024)
            if not chunk:
                break
            total += len(chunk)
        resp.close()
    except Exception as ex:
        return f"FAIL  {name}: {ex}"
    mbps = total * 8 / max(time.monotonic() - t0, 0.001) / 1e6
    verdict = "PASS" if mbps >= min_mbps else "FAIL"
    return f"{verdict}  {name}: first byte {fb:.2f}s, {mbps:.1f} Mbps sampled"


def probe_vod(name, url):
    """The player's Delivery=Auto probe needs a real 206 for OnDemand."""
    req = urllib.request.Request(url, headers={"Range": "bytes=0-1023"})
    try:
        resp = urllib.request.urlopen(req, timeout=10)
        code = resp.getcode()
        resp.read(1024)
        resp.close()
    except Exception as ex:
        return f"FAIL  {name}: {ex}"
    if code == 206:
        return f"PASS  {name}: 206 Partial Content (detected as OnDemand)"
    return f"FAIL  {name}: got {code}, not 206 — will be treated as LIVE"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", default="localhost")
    ap.add_argument("filters", nargs="*", help="only lanes whose name contains a filter")
    args = ap.parse_args()

    results = []
    for name, kind, tmpl, threshold in LANES:
        if args.filters and not any(f in name for f in args.filters):
            continue
        url = tmpl.format(host=args.host)
        if kind == "rtsp":
            results.append(probe_rtsp(name, url, threshold))
        elif kind == "http":
            results.append(probe_http(name, url, threshold))
        else:
            results.append(probe_vod(name, url))
        print(results[-1], flush=True)

    fails = sum(1 for r in results if r.startswith("FAIL"))
    print(f"\n{len(results)} lanes probed, {fails} failing")
    raise SystemExit(1 if fails else 0)


if __name__ == "__main__":
    main()
