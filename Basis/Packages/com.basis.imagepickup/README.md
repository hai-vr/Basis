# Basis Image Pickup

Drag and drop a PNG onto the window to spawn a networked, grabbable image pickup. The image
bytes are relayed peer-to-peer (or via the server as a pure relay) and **never stored
server-side** — an image lives only while its spawner is connected.

Extracted from the Basis monorepo so it can be versioned and installed on its own.

## Requirements

Requires a Basis project. Depends on:

- `com.basis.framework`
- `com.basis.sdk`
- `com.basis.common`
- `com.basis.server`
- `com.basis.textmeshpro`

## Installation

### Basis Package Manager (recommended)

Search for **Basis Image Pickup** in the
[Basis Package Manager](https://github.com/BasisVR/BasisPackageManager) and install.

### Unity Package Manager (git URL)

In Unity: **Window → Package Manager → + → Add package from git URL…**, then paste:

```
https://github.com/BasisVR/BasisImagePickup.git
```

### Manual (`Packages/manifest.json`)

```json
"com.basis.imagepickup": "https://github.com/BasisVR/BasisImagePickup.git"
```

## License

[MIT](LICENSE) © BasisVR
