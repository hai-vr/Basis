# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased] - Basis

### Added

- VR support: per-eye previous-frame matrices for stereo instancing, multi-pass XR history per eye, and MRT passes that bind every texture array slice.
- `ScreenSpaceGlobalIlluminationURP.CameraFilter` so a host can limit the effect to specific cameras.
- `ScreenSpaceGlobalIlluminationURP.DebugView` (`_SSGIDebugView`): the combine pass can output the indirect light, the GI contribution, or the GBuffer albedo / normals.
- GBuffer fallback (`GBufferFallback` / `FallbackAlbedo`): surfaces whose shader has no `UniversalGBuffer` pass (Poiyomi, lilToon, ...) receive GI using a normal reconstructed from depth and an albedo implied per pixel by the camera colour and the ambient light at it, capped by `FallbackAlbedo`. Depth reconstruction is deliberate: a depth normals prepass cannot target an MSAA depth attachment under forced depth priming.
- `SSGIGBuffer` LightMode: shaders can provide a GBuffer pass for this effect only, without exposing a `UniversalGBuffer` pass to the Deferred rendering path. The forward GBuffer pass draws both tags.
- Editor tool `Basis/Rendering/SSGI/Add GBuffer Pass To Poiyomi URP Shaders` (`Editor/ScreenSpaceGlobalIlluminationPoiyomiGBufferPass.cs`, dependency free): clones a Poiyomi URP shader's DepthNormals pass into an `SSGIGBuffer` pass writing real albedo and normals. Works on skinned meshes like any other renderer; locked shaders inherit it when generated from a patched master.
- Runtime GBuffer override (`GBufferOverrideShader`, `Shaders/SSGIGBufferOverride.shader`): renderers registered with `RegisterRenderers` whose shader has no GBuffer pass are drawn into the GBuffer with an override shader reading the material's `_MainTex`, `_Color`, `_BumpMap` and `_Cutoff` (Poiyomi cutout presets and URP alpha clip honoured). Marked through a rendering layer bit; drawn before the real GBuffer passes so those always win.
- Edit mode tests (`Tests/Editor`).
- Blue noise sampling (`ScreenSpaceGlobalIlluminationBlueNoise`, `_SSGIBlueNoise`): an embedded 64x64 void-and-cluster texture, stepped through the R2 sequence per ray and per frame, replaces the hashed white noise for the hemisphere directions, the step jitter and the A-Trous dilation, so the sampling error is high frequency and the denoisers remove it instead of smearing it.

### Fixed

- MSAA: the combine no longer overwrites the camera colour with a resolved copy. Pass 0 copies the camera colour and writes the per-pixel ambient lighting, pass 6 multiplies by the ambient-removal factor it derives from them and the new pass 10 adds the bounce, both blended per MSAA sample.
- GBuffer fallback in dim scenes: assuming a constant albedo removed more ambient than the pixel held, and the fixed 0.04-luminance precision gate then zeroed what was left, so in an APV-lit night world every surface without a GBuffer pass went black and came back as flat grey bounce that merged with the volumetric fog. The albedo is now implied per pixel and the gate is relative to the pixel brightness, so a missed ray leaves the pixel untouched and night scenes keep their direct light.
- GBuffer data was read from the wrong surface. The forward GBuffer pass draws only shaders that have a GBuffer pass, so a surface without one never occludes anything there and the pixel keeps the data of whatever lies behind it: the near surface was then shaded with the far one's albedo and normal, and the fallback it was supposed to take never engaged. Worst on alpha-tested foliage, which came out as bright cards lit as though they were the wall behind them. The pass now publishes its own depth as `_GBufferDepthTexture` and the effect only trusts GBuffer data whose depth matches the camera depth.
- The implied albedo was capped at 1 instead of at `FallbackAlbedo`. The ratio of colour to ambient is only an upper bound on albedo - a surface lit directly is brighter than the ambient alone could make it - so every directly lit fallback surface was handed an albedo of 1 and with it the full traced irradiance.
- The bounce added to a surface whose albedo was guessed is now bounded relative to the light that surface already shows (`SSGI_FALLBACK_MAX_GAIN`), so a bad albedo estimate or a bad traced hemisphere can no longer blow a dark surface out. Surfaces with real GBuffer albedo are unaffected.
- Forward+ / Deferred+: the reflection probe atlas fallback now uses URP's own cluster iteration and probe declarations (`_CLUSTER_LIGHT_LOOP`), so rotated reflection probes and the current cluster layout are handled.
- Unlit materials (`UniversalMaterialType` = `Unlit`: URP Unlit, Shader Graph Unlit) are drawn into the forward GBuffer with their albedo masked, so they receive neither ambient removal nor bounce light. Screens, emissive panels and camera viewfinders keep their exact colour. Render Graph path only.
- A camera's first frame (and the first after a resolution or denoise change) reads this frame's colour for ray hits and starts the temporal accumulation from cleared history instead of uninitialised textures.
- `_BackDepthEnabled = 2.0` assigned instead of compared in the back-face colour lookup.
- d3d11 shader compilation: `SSGISampleNormalWS` and `SampleReflectionProbesCubemap` returned early from inside a branch, which FXC rejects with "use of potentially uninitialized variable" (error X4000), so passes 0, 1 and 10 produced no program and the effect silently did nothing. Both are single-exit now. Helper functions called from the passes must not return from inside a conditional.
- `HasGBufferPass` checks every subshader instead of only the active one, which is a pass-less fallback whenever the device lacks the shader model (batch mode), so URP Lit / Unlit renderers were marked for the override shader there.
- Self-intersection: a ray could count the surface it started from as its own light source. The depth buffer cannot separate "the ray left the surface" from "the ray is still inside it" within a pixel, so thin dense geometry — foliage cards, hair, grass — registered a hit on the first step and read back its own colour, which the colour history fed in again every frame until the surface blew out, smeared along motion vectors when the camera moved, and left an aliased fringe where the oversized correction met the MSAA silhouette. A hit is now ignored until the ray has travelled past the shading point's own pixel footprint (`SELF_INTERSECTION_PIXELS`).

### Changed

- History textures are kept per camera in the Render Graph path too, and a new camera never reprojects from another camera's history.
- The forward GBuffer pass clears its targets, and its transient depth buffer when depth priming is unavailable (MSAA).
- The per-pixel ambient texture (`_SSGIAmbientLightingTexture`, formerly `_APVLightingTexture`) is always written, APV or ambient probe, and the ambient probe coefficients are uploaded every frame: the sky ray-miss fallback needs them even with ambient override off.
- The sample scene is not shipped with this embedded copy.
- Ray marching steps in clip space: the ray is projected once and every step is a multiply-add with the clip space `w` as the hit depth, instead of a world-to-clip transform and a depth conversion per step.
- History textures are pairs swapped every frame instead of copied, the history depth is kept at the traced resolution, and the temporal reprojection pass writes the normals at that resolution (`_SSGINormalTexture`). The two spatial denoisers read that depth and those normals with one fetch per tap instead of the full resolution depth and GBuffer (or a depth reconstruction) at every tap.
- The brightness limit scales the colour by its maximum channel instead of an HSV round trip.
- The compatibility (non Render Graph) code paths are removed: URP 17.4+ has no `Execute` / `OnCameraSetup`, so they could not compile here.


## [1.1.5] - 2025-06-01

### Fixed

- Removed an unnecessary duplicate Blitter pass introduced in **v1.1.4**. (no visual impact on SSGI)


## [1.1.4] - 2025-05-31

### Added

- Added support for Deferred+ rendering path in Unity 6.1.
- Added support for orthographic cameras.

### Fixed

- Fixed a flickering issue when using multiple cameras with SSGI enabled.
- Fixed a rendering issue when MSAA is enabled.
- Fixed a potential green artifacts issue when SSAO is enabled.
- Fixed an issue with SSGI adding an empty deferred renderer to builds.


## [1.1.3] - 2024-11-23

### Fixed

- Fixed a shader compilation issue in Unity 2023.


## [1.1.2] - 2024-11-12

### Fixed

- Fixed a compilation issue by disabling rendering layers in Unity 2023.1. The `RenderingLayerMask` was introduced in Unity 2023.3.


## [1.1.1] - 2024-11-07

### Added

- Added a message to the volume override when the rendering debugger window is open.

### Changed

- Improved performance when SSGI falls back to APV.
- Adjusted the rules for keeping deferred shader variants when building the project.

### Fixed

- Fixed an issue with the Deferred rendering path in Unity 6 LTS.
- Fixed an issue with the rendering layers in volume override.
- Fixed an issue where APV fallback is not available in Forward and Deferred rendering paths.
- Fixed a performance issue with a shader graph skybox.
- Fixed a rendering order issue with volumetric clouds.


## [1.1.0] - 2024-09-16

### Added

- Added new fallback options and support for Adaptive Probe Volumes (APV):
  - **Sky**: Falls back to the sky ambient probe or APV.
  - **Reflection Probes and Sky**: Falls back to reflection probes (if any), then to the sky ambient probe or APV.

### Changed

- Changed the default value of ray miss property from **Reflection Probes** to **Reflection Probes and Sky**.
- Improved the quality of SSGI across different resolution scales.


## [1.0.10] - 2024-09-05

### Fixed

- Fixed an issue where the SSGI quality did not match the volume override when denoising was disabled.


## [1.0.9] - 2024-09-02

### Added

- Added **High Quality Upscaling** option to re-enable the previous upscaling method.
- Added **Artistic Overrides** header to the **Indirect Diffuse Lighting Multiplier** property.

### Changed

- Improved SSGI performance by reducing arithmetic and texture sampling operations.

### Fixed

- Fixed an issue with incorrect depth precision on mobile platforms.
- Fixed an issue where a temporary camera texture did not follow URP render scale.


## [1.0.8] - 2024-08-30

### Added

- Added three global shader keywords to indicate the state of resources created by SSGI:
  - `SSGI_RENDER_GBUFFER`
  - `SSGI_RENDER_BACKFACE_DEPTH`
  - `SSGI_RENDER_BACKFACE_COLOR`

### Fixed

- Fixed an issue where the history texture was set before allocation.
- Fixed an issue with **Backface Lighting** when using the Deferred rendering path.


## [1.0.7] - 2024-08-30

### Fixed

- Fixed an issue where the color format of direct lighting texture was incorrect in some cases.
- Fixed a regression with the deferred shader variants in URP 17.


## [1.0.6] - 2024-08-29

### Changed

- Changed the **Indirect Diffuse Lighting Multiplier** property type to always visible.

### Fixed

- Fixed an issue where deferred shader variants were stripped by URP 14 during the build.
- Fixed an issue with SSGI on mobile devices.


## [1.0.5] - 2024-08-01

### Changed

- Changed shader includes from absolute paths to relative paths to avoid potential issues in certain situations.


## [1.0.4] - 2024-07-17

### Fixed

- Fixed an issue where previous camera color texture was not released.


## [1.0.3] - 2024-07-17

### Added

- Implemented infinite bounce indirect lighting using the previous camera color texture.
- Enhanced aggressive denoising mode by adding **Poisson Disk Recurrent Denoising**.

### Changed

- Improved the pre-denoising logic to achieve more stable results.


## [1.0.2] - 2024-07-14

### Fixed

- Fixed an issue from URP where motion vectors in scene view were incorrectly rendered in Unity 2022 & 2023.
- Fixed an issue where previous depth texture was incorrectly set as the current one, causing severe ghosting.
- Fixed an issue where textures may not be set when Render Graph is enabled.
- Fixed an issue with Automatic Thickness Mode.
- Fixed an issue with Indirect Diffuse Rendering Layers.
- Fixed an issue with incorrect surface normals when Accurate G-buffer Normals was enabled.
- Fixed an issue with random sampling direction being broken on platforms using half-precision floats.


## [1.0.1] - 2024-07-12

### Changed

- Increased minimum supported Unity version to 2022.3.35f1 (from 2022.3.0f1).

### Fixed

- Resolved compiling errors in Unity Editor 2022 & 2023.


## [1.0.0] - 2024-07-11

### Added

- Initial release of this package.