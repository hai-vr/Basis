# Basis Addressables

How Basis organizes its Addressable groups, and the editor tooling that maintains them.
The goal is **runtime memory**: keep large or independently-loaded assets in their own
bundles, and never duplicate a shared dependency across bundles.

## Principle: group by dependency, not by category

A group becomes one AssetBundle. Any asset a bundle references that is **not itself
addressable** is *copied into that bundle*. So if two prefabs in different groups share a
non-addressable font/material/shader, that asset is duplicated into both bundles, costing
memory and disk.

Rules of thumb:

1. **Self-contained cluster** (no deps shared with other groups) -> safe to give its own group.
2. **Shared dependency** (referenced from 2+ groups) -> make it addressable in a shared bundle
   so it is referenced once instead of copied. Fonts go to **Basis Fonts**; everything else to
   **Basis Shared**.
3. Models and other large, on-demand assets -> their own groups so they load/unload alone.

All Basis groups use **LZ4** compression and the local build/load paths.

## Groups

These are created and maintained by the tools below; anything not matched by a rule stays in
Foundation (the catch-all).

| Group | Packing | Contents |
|-------|---------|----------|
| Built In Data | - | Unity built-in / local player data |
| Basis Foundation Assets | PackTogether | Catch-all: players, orbs, mirror, scenes, data assets |
| Basis UI Assets | PackTogether | UI prefabs (Panel Elements), icon textures/sprites |
| Basis Fonts | PackTogether | Inter family + TMP fallback fonts (isolated shared dep) |
| Basis Shared | PackTogether | Shared UI material/shaders/sprites (isolated shared deps) |
| Basis Gizmos | PackTogether | com.basis.gizmos debug-draw cluster |
| Basis MediaPipe Models | PackSeparately | *.task.bytes face/hand/pose models (load independently) |
| Basis OpenLipSync | PackTogether | OpenLipSync model + config |
| Basis Localization | PackTogether | Language JSONs (Addressable label `language`) |

## Tools

All live in `com.basis.framework.editor/Editor/` and use the official
`AddressableAssetSettings` API. They categorize by each entry's **resolved asset path**, so
friendly-named entries (e.g. `GizmoMaterial`, `LocalPlayer`) land in the right group. Do not
hand-edit the group YAML under `Assets/AddressableAssetsData/`.

| Menu | File | Does |
|------|------|------|
| Basis > Addressables > Dependency Report | `BasisAddressableDependencyReport.cs` | Writes `BasisAddressableDependencyReport.txt` to the project root: per-group footprint and dependencies shared across groups (duplication hotspots) |
| Basis > Addressables > Organize Groups | `BasisAddressableOrganizer.cs` | Path rules (gizmos, fonts) + isolates every cross-group shared dependency into Basis Fonts / Basis Shared |
| Basis > Addressables > Organize Model Groups | `BasisModelAddressableSetup.cs` | Moves MediaPipe `*.task.bytes` and OpenLipSync model+config into their groups (also runs as an importer) |
| Basis > Localization > Register Languages as Addressable | `BasisLocalizationAddressableSetup.cs` | Registers language JSONs (address `Languages/{code}`, label `language`) into Basis Localization (also an importer) |
| _(helper, no menu)_ | `BasisAddressableGroups.cs` | `GetOrCreate(settings, name, packing)` — LZ4 + local paths |

## Workflow

1. Run **Organize Model Groups**, **Organize Groups**, and **Register Languages as Addressable**
   (the importers also run them automatically when matching assets are imported).
2. Run **Dependency Report** and confirm cross-group shared deps are ~0. TMP
   `Editor Resources/*.psd` icons may appear; they are editor-only and stripped from builds, so
   ignore them.
3. **Build Addressables content** (Window > Asset Management > Addressables > Groups > Build >
   New Build > Default Build Script) for player builds.

Re-running is safe (idempotent).

## Used packages and licenses

| Package | Version | License | Role here |
|---------|---------|---------|-----------|
| com.unity.addressables | 2.9.1 | Unity Companion License | The Addressables system (`Unity.Addressables`, `Unity.ResourceManager`) |
| com.basis.framework / com.basis.framework.editor | embedded | MIT | Tooling + runtime localization loader |
| com.basis.sdk | embedded | MIT | UI prefabs, sprites, materials; Inter fonts (see notes) |
| com.basis.textmeshpro | embedded | Unity Companion License | TMP shaders + LiberationSans fallback (shared deps) |
| com.basis.gizmos | embedded | MIT | Gizmo prefabs / material / shader |
| com.basis.mediapipe | embedded | MIT | MediaPipe `.task.bytes` models (see notes) |
| com.github.homuler.mediapipe | 0.16.3 | Apache-2.0 | MediaPipe inference plugin; bundles MediaPipe + native libs |
| com.basis.openlipsync / com.basisvr.openlipsync | 0.2.0 | Apache-2.0 | OpenLipSync driver + model/config |
| com.basis.tests | embedded | MIT (Basis repo) | Test prefabs (currently in Foundation) |

Asset-level notes:

- **Inter** typeface (`com.basis.sdk/Fonts/`, the bulk of Basis Fonts) is under the
  **SIL Open Font License 1.1**.
- **MediaPipe** face/hand/pose models are Google's, **Apache-2.0**. See
  `com.basis.mediapipe/THIRD_PARTY_NOTICES.md` for the full MediaPipe + homuler notice.
- "embedded" = a local package under `Packages/` (no manifest version); licenses are taken from
  each package's `package.json`/`LICENSE`.