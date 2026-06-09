# HRTF / Spatial Voice Profiles

Basis spatializes player voice with Steam Audio's binaural renderer (HRTF). This
covers how elevation (above/below) cues work, and how to add a better HRTF profile.

## Background

Above/below localization comes almost entirely from pinna spectral notches in the
~5–10 kHz band, and those notches are highly individual. Steam Audio's **built-in
default HRTF is a generic, averaged dataset**, so it smears exactly those cues — voices
above and below the listener sound flatter than they should. This is a property of
generic HRTFs, not a bug in the audio pipeline; the full chain (spatializer plugin,
`AudioSource.spatialize`, `SteamAudioSource.directBinaural`, the listener) is wired and
active.

The lever is the HRTF dataset itself. Steam Audio can load custom HRTFs from `.sofa`
files; swapping in a measured dummy-head profile gives cleaner, stronger elevation
response than the generic default.

## What ships by default

- **HRTF interpolation defaults to Bilinear** (`General > Networking/Remote Audio >
  HRTF`). Bilinear blends adjacent measured responses instead of snapping to the
  nearest one, which smooths directional/elevation transitions. The binding key was
  bumped (`ra_interpolation_v2`) so existing installs pick up the new default.
- An **HRTF Profile** dropdown in the same group. With no `.sofa` files imported it
  shows only **Default** (the built-in generic profile).

## Adding a custom HRTF profile

You supply the `.sofa` file — we don't bundle one (no single HRTF suits everyone, and
the binary asset is large). Steps:

1. **Get a permissively-licensed SOFA HRTF.** Recommended:
   - **SADIE II** (University of York) — Neumann KU100 / KEMAR dummy heads, high spatial
     resolution, excellent elevation. CC BY 4.0. https://www.york.ac.uk/sadie-project/database.html
   - **MIT KEMAR** (Gardner & Martin) — the classic dummy-head set; clean and
     "less aggressive." Free to use.

   Use a `SimpleFreeFieldHRIR` convention file (the standard for these databases).
   Avoid research-only sets (e.g. CIPIC) if you ship commercially.

2. **Import it.** Drop the `.sofa` file anywhere under `Assets/`. The Steam Audio
   `ScriptedImporter` turns it into a `SOFAFile` asset automatically. Its profile name
   is the file name (e.g. `D2_48K_24bit_256tap_FIR_SOFA.sofa`).

3. **Register it.** Open `Assets/Basis/Settings/Resources/SteamAudioSettings.asset`
   and add the imported `SOFAFile` to the **SOFA Files** list. (Optional per-file
   `volume` gain in dB and `normType` if it's too quiet/loud relative to Default.)

4. **Select it.** In-app: `Settings > Remote Audio > HRTF > HRTF Profile` →
   pick the file by name. The choice persists (`ra_hrtfprofile`) and applies live to
   all spatialized audio.

## Notes

- The HRTF is **global** — one binaural renderer drives all spatialized sources, so the
  profile is the listener's, not per-speaker. The setting lives under Remote Audio
  because that's where voice spatialization is configured.
- Selection is by **name**, so reordering the SOFA list won't break a saved choice; if a
  saved profile is missing it falls back to Default.
- **Verify on Android / Quest / Steam Frame.** SOFA loading depends on the native
  Steam Audio plugin including the SOFA reader on that platform; confirm a custom
  profile actually loads there before relying on it.

## Code touch points

- `SteamAudioManager.GetHRTFIndexByName` / `SetActiveHRTF` — runtime profile switch
  (`Packages/com.steam.steamaudio/Runtime/SteamAudioManager.cs`).
- `SettingsProviderRemoteAudio.ApplyHrtfProfile` / `GetHrtfProfileEntries` and the
  `RAHrtfProfile` / `RAInterpolation` bindings — settings + UI wiring.
