# ZstdSharp - for Basis

Managed port of the Zstandard compression library for .NET. Used by Basis for the Zstd half of the
hybrid avatar-bundle codec: keyframe/full bundles are compressed against a trained 16 KiB
dictionary, delta-only bundles stay on LZ4.

This is [oleg-st/ZstdSharp](https://github.com/oleg-st/ZstdSharp), repackaged as a Unity Package
Manager package so [Basis](https://github.com/BasisVR/Basis) can consume it as a **git dependency**
instead of a vendored local mount. It ships the prebuilt managed assembly only.

A pure-managed port matters here: the same assembly serves the standalone server and the Unity
client on every platform including Android/Quest, with no native binary to build or ship per ABI.

- **Upstream:** https://github.com/oleg-st/ZstdSharp
- **Version:** 0.8.6 (netstandard2.1 build)
- **License:** MIT - see [LICENSE](LICENSE)

## Install

Add to your project's `Packages/manifest.json`:

```json
"org.basisvr.zstdsharp": "https://github.com/BasisVR/BasisZstdSharp.git"
```