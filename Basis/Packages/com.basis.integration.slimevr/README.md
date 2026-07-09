# Basis SlimeVR Integration

Connects Basis to a running [SlimeVR server](https://github.com/SlimeVR/SlimeVR-Server) over the
[SolarXR protocol](https://github.com/SlimeVR/SolarXR-Protocol) and uses the data SlimeVR already
has about your body, so you don't have to calibrate your size in Basis at all.

## What it does

- **Auto body size**: pulls SlimeVR's skeleton config (bone lengths + user height) and applies it
  to the Basis height system. `PlayerEyeHeight` comes straight from SlimeVR's user height
  (its internal user height *is* the standing floor-to-eye height), and the arm span is derived
  from the skeleton: `SHOULDERS_WIDTH + 2 x (UPPER_ARM + LOWER_ARM + HAND_Y)` — the distance
  between where the controllers sit, which is what Basis measures during a manual calibration.
  Both are persisted as the saved calibrated body size, so the very first avatar load next
  session is already at the right scale.
- **Stays in sync**: the skeleton config is re-polled, so running SlimeVR's autobone, its height
  calibration, or editing proportions in the SlimeVR GUI flows into Basis within seconds.
- **Tracker telemetry**: a datafeed provides every tracker's role, status, battery level, voltage
  and signal strength (`BasisSlimeVRBridge.Trackers` + `OnTrackersUpdated`) for HUDs and menus.
- **Resets**: SlimeVR's yaw / full / mounting resets can be triggered from inside Basis
  (`BasisSlimeVRBridge.TriggerYawReset()` etc., also exposed in Settings > Tracker Settings).

## How it connects

Transports, tried in order:

1. **Named pipe / unix socket** (server v20.1+): `\\.\pipe\SlimeVRRpc` on Windows,
   `$XDG_RUNTIME_DIR/SlimeVRRpc` on Linux. Framing: 4-byte little-endian length prefix that
   includes itself, then a flatbuffers `MessageBundle`. This is SlimeVR's forward path.
2. **Websocket** `ws://127.0.0.1:21110` (every released server): one `MessageBundle` per binary
   message, no length prefix.

A background thread owns the connection, reconnects every few seconds while the server is absent,
and marshals everything onto the main thread through `BasisDeviceManagement.mainThreadActions`.
When no SlimeVR server is installed the integration idles at zero cost beyond a periodic failed
connect.

**Server pipe bug (as of v20.1.0 / 2026-06 main):** the Windows named pipe bridge accepts
connections and reads requests, but every response it writes is a zero-byte write —
`WindowsNamedPipeRpcConnection.send` builds the length-prefixed buffer `src` and then sends
`bytes.array(), bytes.remaining()`, which is 0 after `src.put(bytes)` consumed the buffer (fix:
send `src.array(), src.remaining()`). Its reader also rejects any message over 1024 bytes. The
client detects the mute pipe (no response within 6 s of the connect-time request) and retries
preferring the websocket, so the integration works on every server version today and will use
the pipe automatically once the server side is fixed.

## Settings

Settings > Tracker Settings > SlimeVR:

- **Connect To SlimeVR** (`slimevr_enable`, default on)
- **Auto Apply Body Measurements** (`slimevr_applybodymeasurements`, default on)

Seated mode is respected: measurements received while seated are held and applied when you stand.
Manual calibration still works and simply overwrites the SlimeVR values until the next config poll.

## Debugging

`Basis > Tests And Debug > SlimeVR Debug` shows the live connection, the raw skeleton parts, the
derived measurements, all trackers with battery/signal, and reset buttons.

## Protocol code

- `Runtime/SolarXR/Generated` — C# generated from the SolarXR schema with flatc **v22.10.26**
  (the version the SolarXR repo pins). Regenerate with:
  `flatc --csharp --gen-all -o Generated -I schema schema/all.fbs`
- `Runtime/SolarXR/FlatBuffers` — the Google FlatBuffers C# runtime, same version.

See `THIRD_PARTY_NOTICES.md` for licenses (SolarXR: MIT/Apache-2.0 dual, FlatBuffers: Apache-2.0).
