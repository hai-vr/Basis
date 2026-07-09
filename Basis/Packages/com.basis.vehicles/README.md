# Basis Vehicles

A general-purpose vehicle system for Basis — drivable / pilotable vehicles with networked
physics: bodies, wheels, thrusters, hover thrusters, steering, gimbaled parts, pilot seats,
and engine audio. Includes example land + space scenes.

Extracted from the Basis monorepo so it can be versioned and installed on its own.

## Requirements

Requires a Basis project. Depends on:

- `com.basis.common`
- `com.basis.framework` (+ `com.basis.framework.editor`)
- `com.basis.sdk`
- `com.basis.server`

## Installation

### Basis Package Manager (recommended)

Search for **Basis Vehicles** in the
[Basis Package Manager](https://github.com/BasisVR/BasisPackageManager) and install.

### Unity Package Manager (git URL)

In Unity: **Window → Package Manager → + → Add package from git URL…**, then paste:

```
https://github.com/BasisVR/BasisVehicles.git
```

### Manual (`Packages/manifest.json`)

```json
"com.basis.vehicles": "https://github.com/BasisVR/BasisVehicles.git"
```

## Samples

The `examples/` folder contains land + space demo scenes and low-poly models
(see `examples/copyrights.json` for asset attribution).

## License

[MIT](LICENSE) © BasisVR
