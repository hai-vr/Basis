# Basis Servers Panel

The optional **Servers** menu panel for Basis: the server directory, connect, and host UI,
built on top of the framework's `BasisConnectionService`. Remove this package to ship a Basis
application with no Servers panel — headless `--connection=` launching keeps working without it.

Extracted from the Basis monorepo so it can be versioned and installed on its own.

## Requirements

Requires a Basis project. Depends on:

- `com.basis.sdk`
- `com.basis.common`
- `com.basis.framework`
- `com.basis.server`

## Installation

### Basis Package Manager (recommended)

Search for **Basis Servers Panel** in the
[Basis Package Manager](https://github.com/BasisVR/BasisPackageManager) and install.

### Unity Package Manager (git URL)

In Unity: **Window → Package Manager → + → Add package from git URL…**, then paste:

```
https://github.com/BasisVR/BasisServersProvider.git
```

### Manual (`Packages/manifest.json`)

```json
"com.basis.provider.servers": "https://github.com/BasisVR/BasisServersProvider.git"
```

## Localization

Ships UI strings in 18 languages under `Localization/Languages/`. Basis auto-registers a
package's language files when it is installed.

## License

[MIT](LICENSE) © BasisVR
