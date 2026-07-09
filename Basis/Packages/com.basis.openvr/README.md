# Basis OpenVR Integration

OpenVR / SteamVR integration for Basis — device input (controllers, spatial, eye), head
tracking, and the OpenVR management layer, bridging Valve's OpenVR into the Basis input and
tracker system.

Extracted from the Basis monorepo so it can be versioned and installed on its own. The
framework activates it automatically when present (via the `BASIS_HAS_OPENVR` version define).

## Requirements

Requires a Basis project **and** the OpenVR/SteamVR runtime packages:

- Basis: `com.basis.common`, `com.basis.framework`, `com.basis.gizmos`, `com.basis.sdk`
- [`com.steam.steamvr`](https://github.com/BasisVR/BasisSteamVR) — SteamVR (trimmed for Basis)
- [`com.valvesoftware.unity.openvr`](https://github.com/BasisVR/BasisOpenVRPlugin) — Valve's OpenVR XR plugin

## Installation

Install all three (the Basis Package Manager can add them together), or add the git URLs to
your project's `Packages/manifest.json`:

```json
"com.basis.openvr": "https://github.com/BasisVR/BasisOpenVR.git",
"com.steam.steamvr": "https://github.com/BasisVR/BasisSteamVR.git",
"com.valvesoftware.unity.openvr": "https://github.com/BasisVR/BasisOpenVRPlugin.git"
```

## License

[MIT](LICENSE) © BasisVR. The SteamVR / OpenVR components are BSD-3-Clause © Valve Corporation.
