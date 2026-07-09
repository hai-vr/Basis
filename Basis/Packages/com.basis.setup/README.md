# Basis Setup

Some project configuration cannot live in a package — Unity resolves it only from fixed
`Assets/` locations (Addressables data, XR loaders, URP render pipeline defaults, quality
settings, the Input Actions asset, `link.xml`, Android gradle templates, and the SteamVR
binding files under `StreamingAssets/`). As Basis moves its sources into packages, those
files still have to end up in the consuming project — **exactly** as authored.

This package ships the canonical files as verbatim templates and copies them in, so the
result is byte-for-byte identical to the golden project, GUIDs and all. Nothing is
reconstructed from code, so nothing can drift.

## How it works

The canonical assets are stored inside the package under `Templates~/` (the trailing `~`
means Unity never imports them, so the real `.asset`/`.meta` files sit there untouched,
carrying their exact GUIDs and serialized graphs).

- **Bake** *(maintainer, in the golden project)* — snapshots the project's current config
  into `Templates~/`. Run it whenever the canonical defaults change, then commit the package.
- **Install** — copies the templates into a consuming project where they are missing,
  GUID-preserved, then wires the editor-only bits (config objects, graphics defaults).
- **Update** — backs up the existing copy to `BasisSetupBackups/`, then replaces it from the
  template so it matches exactly.

Each module just declares the `Assets/` paths it owns; copying is generic. A small
`Activate()` handles the wiring an asset can't carry (Addressables default object, XR /
OpenVR / Input config objects, default render pipeline).

## Usage

`Basis ▸ Setup ▸ Configuration Window` lists every module with its status. Buttons:

- **Create Missing Assets** — install; only writes paths that don't exist yet.
- **Update All To Latest** — replace managed paths from the templates (backs up first).
- **Bake Templates From Project** *(maintainer)* — project → `Templates~`. Disabled when the
  package is resolved read-only from the PackageCache.

The same actions are on the `Basis ▸ Setup` menu, and applied versions are tracked in
`ProjectSettings/BasisSetup.json`.

### Baking requires a writable package

A git/registry dependency resolves read-only into `Library/PackageCache`, so `Templates~`
can't be written there. To bake, consume the package **embedded** (under `Packages/`) or via
a local `file:` reference in the consuming project's `manifest.json`, e.g.:

```json
"com.basis.setup": "file:../../../BasisSetup"
```

Bake, commit the package, then switch consumers back to the git URL.

## Extending

Derive `BasisSetupModuleBase`, set `Key`/`DisplayName`/`Category`/`Version`, list `OwnedPaths`,
and override `Activate()` if the config needs editor-side registration. It's discovered by
reflection and appears in the window. Vendor integrations live in their own define-gated
assemblies so the package still compiles when the vendor package is absent.

## Modules

| Category | Owns (`Assets/…`) |
|---|---|
| Structure | `Resources/`, `TemporaryStorage/` (empty folders) |
| Addressables | `AddressableAssetsData/` (settings, groups, **entries**, schemas, data builders) |
| XR | `XR/` — `XRGeneralSettingsPerBuildTarget`, OpenXR loader + settings |
| XR (OpenVR) | `XR/…/OpenVRLoader`, `OpenVRSettings` + config object |
| Rendering | `Basis/Settings/Unity Rendering Defaults/` (renderers, volume profiles, URP global, lighting) |
| Quality | `Basis/Settings/Quality Settiings/` (per-platform pipelines) |
| Input | `Basis/Settings/InputActions/` |
| Highlight | `Basis/Settings/Highlight/` |
| Steam Audio | `Basis/Settings/Resources/` (settings + material presets) |
| Linker | `Basis/link.xml` |
| Android | `Plugins/Android/` (manifest + gradle templates) |
| SteamVR | `StreamingAssets/SteamVR/` (action + binding files) |
