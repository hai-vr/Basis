# Cilbox

Sandbox C# CIL code with an interpreter — by [cnlohr](https://github.com/cnlohr).

Vendored into Basis and published as a standalone package so it can be versioned and installed
on its own. Basis uses it in the shim / CIL sandbox layer (`com.basis.shim`).

## Installation

### Unity Package Manager (git URL)

In Unity: **Window → Package Manager → + → Add package from git URL…**, then paste:

```
https://github.com/BasisVR/BasisCilbox.git
```

### Manual (`Packages/manifest.json`)

```json
"com.cnlohr.cilbox": "https://github.com/BasisVR/BasisCilbox.git"
```

## License

See [LICENSE](LICENSE). © cnlohr.
