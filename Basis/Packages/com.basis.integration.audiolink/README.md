# Basis AudioLink Integration

Basis-side [AudioLink](https://github.com/llealloo/audiolink) integration: CPU-readback
reactive components that drive `Light` intensity/color and renderer materials from AudioLink
bands, plus a republisher that keeps AudioLink globals resolving across Desktop/VR mode switches.

Optional bolt-on — compiles only when both `com.basis.framework` and `com.llealloo.audiolink`
are present.

Extracted from the Basis monorepo so it can be versioned and installed on its own.

## Requirements

Requires a Basis project **and** AudioLink. Depends on:

- `com.basis.framework`
- `com.llealloo.audiolink`

## Installation

### Basis Package Manager (recommended)

Search for **Basis AudioLink Integration** in the
[Basis Package Manager](https://github.com/BasisVR/BasisPackageManager) and install.

### Unity Package Manager (git URL)

In Unity: **Window → Package Manager → + → Add package from git URL…**, then paste:

```
https://github.com/BasisVR/BasisAudioLinkIntegration.git
```

### Manual (`Packages/manifest.json`)

```json
"com.basis.integration.audiolink": "https://github.com/BasisVR/BasisAudioLinkIntegration.git"
```

## License

[MIT](LICENSE) © BasisVR
