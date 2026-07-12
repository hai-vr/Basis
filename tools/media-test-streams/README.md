# Media test-stream stack

Self-hostable test streams for `com.basis.mediaplayer` development — the lanes that have no
reliable public endpoint. The companion guide,
[`Basis/Packages/com.basis.mediaplayer/TESTING.md`](../../Basis/Packages/com.basis.mediaplayer/TESTING.md),
explains what to test against these streams and lists the public always-on endpoints that
cover the common lanes with zero setup.

Runs in two modes:

- **Local (Editor iteration).** The player allows loopback URLs in the Editor, so
  `docker compose up -d` on your dev machine and `rtspt://localhost:8554/main` in the Editor
  is the fastest loop. Builds refuse loopback — local mode is Editor-only by design.
- **Public VPS (builds, Quest, multi-client).** The same stack on any cheap VPS with a public
  IP and a DNS name. Private/LAN addresses are refused on every platform, so there is no
  in-between: it's localhost-in-Editor or properly public.

## Quick start

```bash
# 1. Prepare assets from your own test clip (real footage with visible speech
#    recommended; 6-8 audio channels unlock the multichannel lanes)
scripts/prepare-assets.sh ~/my-test-clip.mp4

# 2. Start the stack
docker compose up -d

# 3. Prove the feeds are healthy before testing the player
python3 scripts/preflight.py                 # local
python3 scripts/preflight.py --host my.vps   # deployed
```

## Lanes

| URL (`<host>` = `localhost` or your VPS) | Lane | Notes |
| --- | --- | --- |
| `rtspt://<host>:8554/main` | RTSPT live, 2 s GOP | The reference live path — joins land a frame within ~2 s |
| `rtspt://<host>:8554/silent` | Video + silent stereo | Regression cover for effectively video-only sources |
| `rtspt://<host>:8554/slowjoin` | **Adversarial** ~10 s GOP | Mid-stream joins wait up to 10 s for an IDR — deliberately; audio-first joins here are expected |
| `rtspt://<host>:8554/captions` | CEA-608 in-band captions | Generated fixture (silent test pattern); cues every ~3 s incl. accents, music note, clear |
| `http://<host>:8081/live.ts` | HTTP-TS live | Single-client feeder: serves one connection, respawns on disconnect |
| `http://<host>:8080/assets/mezzanine.mp4` | Progressive MP4 VOD | nginx answers real 206 → `Delivery=Auto` detects OnDemand |
| `http://<host>:8080/assets/hls/index.m3u8` | HLS VOD | Single-rendition packaging of the mezzanine |
| `http://<host>:8080/assets/lpcm.m2ts` | LPCM multichannel M2TS | Only produced from a ≥6-ch source; the full-7.1 lane |
| `http://<host>:8080/assets/w8.wav` | Multichannel WAV | Audio-only; name varies with source channel count |
| `http://<host>:8080/assets/videoonly.mp4` + `audioonly.mp4` | Split-stream pair | Load the video URL with the audio URL as the separate audio leg |
| `rist://<host>:5000` | RIST plain | `--profile rist`; see below |
| `rist://<host>:5001?secret=…&aes-type=128` | RIST AES-128 | Same PSK on both ends; change the compose file's placeholder |

`http://<host>:8080/assets/` autoindexes, so anything else you drop into `assets/` is served
with range support too.

## RIST lanes

```bash
docker compose --profile rist up -d
```

Two extra requirements:

- **Player side**: the native plugin must be built with `-DBASIS_WITH_RIST=ON` (see the
  package README's build section) — stock builds refuse `rist://`.
- **Sender side**: the ffmpeg in the container must include librist. Verify with
  `docker compose run --rm rist-plain -protocols 2>/dev/null | grep rist`; if it's missing,
  point the two rist services at any ffmpeg image with librist, or run the same commands with
  a host ffmpeg "full" build.

Change the AES lane's pre-shared key in `docker-compose.yml` before deploying anywhere shared.

## Deploying on a VPS

1. Any small VPS works — the publishers `-c copy` pre-encoded assets, so CPU stays near idle.
2. Copy this directory up, run `prepare-assets.sh` there (or copy a prepared `assets/`), then
   `docker compose up -d`.
3. Open the inbound ports at **every** firewall layer (many providers gate ports in a panel
   *in addition to* the OS firewall): `8554/tcp`, `8080/tcp`, `8081/tcp`, and for RIST
   `5000-5001/udp`.
4. Give it a DNS name. Hostnames are DNS-validated by the player, and Quest's cleartext
   policy means the HTTP lanes (`8080`/`8081`) need to sit behind TLS with a certificate
   chain standalone headsets actually trust (serve the full chain including intermediates —
   headset trust stores are sparser than desktop browsers'). `rtspt://` needs no TLS.
5. `python3 scripts/preflight.py --host <name>` before every session.

## Gotchas

- **One client per HTTP-TS slot.** The `tslive` feeder serves a single connection then exits
  and respawns. If it wedges without exiting (stale ~0.5 Mbps trickle — the preflight's
  bitrate floor catches this), `docker compose restart tslive`.
- **Don't benchmark with `curl.exe` on Windows** — it under-reads regardless of server. Use
  `preflight.py`'s sampled throughput.
- **Slow joins on `slowjoin` are the point.** File a bug only if the join exceeds the GOP
  length or A/V never locks after the first IDR.
- **Link capacity is a real variable.** A stream whose bitrate exceeds your path to the VPS
  cannot play live regardless of player behaviour; check the preflight throughput numbers
  against the asset bitrate before chasing "stutter".
- **Asset licensing.** `prepare-assets.sh` derives everything from the clip you supply; use
  content you have the rights to put on a public endpoint.
