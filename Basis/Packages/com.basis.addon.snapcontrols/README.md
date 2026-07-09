# Basis Snap Controls

Add-on snap-detent controls for [Basis](https://github.com/BasisVR/Basis) worlds:
rotary knobs, levers, switches, and per-detent activation targets driven by
snap-point markers.

This package was extracted from the Basis monorepo so it can be versioned and
installed on its own. It is an **optional add-on** — install it only in projects
that use it.

## Requirements

Requires a Basis project. Depends on:

- `com.basis.common`
- `com.basis.framework`
- `com.basis.sdk`

## Installation

### Basis Package Manager (recommended)

Search for **Basis Snap Controls** in the
[Basis Package Manager](https://github.com/BasisVR/BasisPackageManager) and install.

### Unity Package Manager (git URL)

In Unity: **Window → Package Manager → + → Add package from git URL…**, then paste:

```
https://github.com/BasisVR/BasisSnapControls.git
```

### Manual (`Packages/manifest.json`)

```json
"com.basis.addon.snapcontrols": "https://github.com/BasisVR/BasisSnapControls.git"
```

## Samples

A **Rotational Snap Example** scene is included as a UPM sample — import it from the
package's page in the Unity Package Manager.

## License

[MIT](LICENSE) © BasisVR
