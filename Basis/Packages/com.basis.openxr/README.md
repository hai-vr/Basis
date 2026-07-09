# Basis OpenXR Integration

OpenXR integration for Basis — head / hand / eye input, device tracking, interaction
profiles, and passthrough — bridging Unity's OpenXR + XR Hands into the Basis input and
tracker system.

Extracted from the Basis monorepo so it can be versioned and installed on its own. The
framework activates it automatically when present (via the `BASIS_HAS_OPENXR` version define).

## Requirements

Requires a Basis project. Depends on:

- Basis: `com.basis.common`, `com.basis.framework`, `com.basis.gizmos`, `com.basis.sdk`
- Unity: `com.unity.xr.openxr`, `com.unity.xr.hands`, `com.unity.xr.management`,
  `com.unity.inputsystem`, `com.unity.addressables`, `com.unity.mathematics`

## Installation

### Basis Package Manager (recommended)

Search for **Basis OpenXR Integration** in the
[Basis Package Manager](https://github.com/BasisVR/BasisPackageManager) and install.

### Unity Package Manager (git URL)

In Unity: **Window → Package Manager → + → Add package from git URL…**, then paste:

```
https://github.com/BasisVR/BasisOpenXR.git
```

### Manual (`Packages/manifest.json`)

```json
"com.basis.openxr": "https://github.com/BasisVR/BasisOpenXR.git"
```

## License

[MIT](LICENSE) © BasisVR — see also `THIRD_PARTY_NOTICES.md`.
