using Basis.Scripts.Networking;
using Basis.Scripts.Networking.Receivers;
using SteamAudio;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.BasisUI
{
    /// <summary>
    /// Settings tab for remote player audio configuration.
    /// Exposes AudioSource and Steam Audio settings that apply to all remote players.
    /// </summary>
    public static class SettingsProviderRemoteAudio
    {
        [RuntimeInitializeOnLoadMethod]
        static void Init()
        {
            BasisSettingsSystem.OnSettingsFinishedChanges += ApplyRemoteAudioToAll;
        }

        public static PanelTabPage RemoteAudioTab(PanelTabGroup tabGroup)
        {
            PanelTabPage tab = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
            PanelElementDescriptor descriptor = tab.Descriptor;

            descriptor.SetTitle("Remote Player Audio");
            descriptor.SetDescription("Controls how you hear other players' voices.");

            RectTransform container = descriptor.ContentParent;

            // ─────────────── AUDIO SOURCE GROUP ───────────────
            PanelElementDescriptor audioSourceGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            audioSourceGroup.SetTitle("Audio Source");
            audioSourceGroup.SetDescription("Unity AudioSource spatial settings for remote voice playback.");

            PanelSlider sliderMinDistance = PanelSlider.CreateEntryAndBind(
                audioSourceGroup,
                PanelSlider.SliderSettings.Advanced("Min Distance", 0.1f, 10f, false, 2, ValueDisplayMode.Meters),
                BasisSettingsDefaults.RAMinDistance);

            PanelSlider sliderSpread = PanelSlider.CreateEntryAndBind(
                audioSourceGroup,
                PanelSlider.SliderSettings.Degrees("Spread", 0f, 360f, true, 0),
                BasisSettingsDefaults.RASpread);

            PanelSlider sliderDoppler = PanelSlider.CreateEntryAndBind(
                audioSourceGroup,
                PanelSlider.SliderSettings.Advanced("Doppler Level", 0f, 5f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.RADopplerLevel);

            PanelSlider sliderSpatialBlend = PanelSlider.CreateEntryAndBind(
                audioSourceGroup,
                PanelSlider.SliderSettings.Advanced("Spatial Blend", 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.RASpatialBlend);

            // ─────────────── STEAM AUDIO - HRTF GROUP ───────────────
            PanelElementDescriptor hrtfGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            hrtfGroup.SetTitle("HRTF / Binaural");
            hrtfGroup.SetDescription("Head-related transfer function settings for 3D audio.");

            PanelToggle toggleDirectBinaural = PanelToggle.CreateNewEntry(hrtfGroup);
            toggleDirectBinaural.Descriptor.SetTitle("Direct Binaural (HRTF)");
            toggleDirectBinaural.AssignBinding(BasisSettingsDefaults.RADirectBinaural);

            PanelToggle togglePerspectiveCorrection = PanelToggle.CreateNewEntry(hrtfGroup);
            togglePerspectiveCorrection.Descriptor.SetTitle("Perspective Correction");
            togglePerspectiveCorrection.AssignBinding(BasisSettingsDefaults.RAPerspectiveCorrection);

            PanelDropdown dropdownInterpolation = PanelDropdown.CreateNewEntry(hrtfGroup);
            dropdownInterpolation.Descriptor.SetTitle("HRTF Interpolation");
            dropdownInterpolation.AssignEntries(new List<string> { "Nearest", "Bilinear" });
            dropdownInterpolation.AssignBinding(BasisSettingsDefaults.RAInterpolation);

            // ─────────────── STEAM AUDIO - PROPAGATION GROUP ───────────────
            PanelElementDescriptor propagationGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            propagationGroup.SetTitle("Sound Propagation");
            propagationGroup.SetDescription("Distance attenuation and air absorption simulation.");

            PanelToggle toggleDistanceAttenuation = PanelToggle.CreateNewEntry(propagationGroup);
            toggleDistanceAttenuation.Descriptor.SetTitle("Distance Attenuation");
            toggleDistanceAttenuation.AssignBinding(BasisSettingsDefaults.RADistanceAttenuation);

            PanelToggle toggleAirAbsorption = PanelToggle.CreateNewEntry(propagationGroup);
            toggleAirAbsorption.Descriptor.SetTitle("Air Absorption");
            toggleAirAbsorption.AssignBinding(BasisSettingsDefaults.RAAirAbsorption);

            // ─────────────── STEAM AUDIO - DIRECTIVITY GROUP ───────────────
            PanelElementDescriptor directivityGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            directivityGroup.SetTitle("Directivity");
            directivityGroup.SetDescription("Controls how directional voice sources sound (dipole pattern).");

            PanelToggle toggleDirectivity = PanelToggle.CreateNewEntry(directivityGroup);
            toggleDirectivity.Descriptor.SetTitle("Directivity");
            toggleDirectivity.AssignBinding(BasisSettingsDefaults.RADirectivity);

            PanelSlider sliderDipoleWeight = PanelSlider.CreateEntryAndBind(
                directivityGroup,
                PanelSlider.SliderSettings.Advanced("Dipole Weight", 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.RADipoleWeight);

            PanelSlider sliderDipolePower = PanelSlider.CreateEntryAndBind(
                directivityGroup,
                PanelSlider.SliderSettings.Advanced("Dipole Power", 0f, 4f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.RADipolePower);

            // ─────────────── STEAM AUDIO - OCCLUSION GROUP ───────────────
            PanelElementDescriptor occlusionGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            occlusionGroup.SetTitle("Occlusion");
            occlusionGroup.SetDescription("Controls how walls and objects block sound.");

            PanelToggle toggleOcclusion = PanelToggle.CreateNewEntry(occlusionGroup);
            toggleOcclusion.Descriptor.SetTitle("Occlusion");
            toggleOcclusion.AssignBinding(BasisSettingsDefaults.RAOcclusion);

            PanelDropdown dropdownOcclusionType = PanelDropdown.CreateNewEntry(occlusionGroup);
            dropdownOcclusionType.Descriptor.SetTitle("Occlusion Type");
            dropdownOcclusionType.AssignEntries(new List<string> { "Raycast", "Volumetric" });
            dropdownOcclusionType.AssignBinding(BasisSettingsDefaults.RAOcclusionType);

            PanelSlider sliderOcclusionRadius = PanelSlider.CreateEntryAndBind(
                occlusionGroup,
                PanelSlider.SliderSettings.Advanced("Occlusion Radius", 0f, 4f, false, 2, ValueDisplayMode.Meters),
                BasisSettingsDefaults.RAOcclusionRadius);

            PanelSlider sliderOcclusionSamples = PanelSlider.CreateEntryAndBind(
                occlusionGroup,
                PanelSlider.SliderSettings.Advanced("Occlusion Samples", 1f, 128f, true, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.RAOcclusionSamples);

            // ─────────────── STEAM AUDIO - TRANSMISSION GROUP ───────────────
            PanelElementDescriptor transmissionGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            transmissionGroup.SetTitle("Transmission");
            transmissionGroup.SetDescription("Controls sound passing through walls and surfaces.");

            PanelToggle toggleTransmission = PanelToggle.CreateNewEntry(transmissionGroup);
            toggleTransmission.Descriptor.SetTitle("Transmission");
            toggleTransmission.AssignBinding(BasisSettingsDefaults.RATransmission);

            PanelDropdown dropdownTransmissionType = PanelDropdown.CreateNewEntry(transmissionGroup);
            dropdownTransmissionType.Descriptor.SetTitle("Transmission Type");
            dropdownTransmissionType.AssignEntries(new List<string> { "Frequency Independent", "Frequency Dependent" });
            dropdownTransmissionType.AssignBinding(BasisSettingsDefaults.RATransmissionType);

            PanelSlider sliderMaxTransmissionSurfaces = PanelSlider.CreateEntryAndBind(
                transmissionGroup,
                PanelSlider.SliderSettings.Advanced("Max Transmission Surfaces", 1f, 8f, true, 0, ValueDisplayMode.Raw),
                BasisSettingsDefaults.RAMaxTransmissionSurfaces);

            // ─────────────── STEAM AUDIO - MIX GROUP ───────────────
            PanelElementDescriptor mixGroup =
                PanelElementDescriptor.CreateNew(PanelElementDescriptor.ElementStyles.Group, container);
            mixGroup.SetTitle("Mix");
            mixGroup.SetDescription("Direct sound mix level.");

            PanelSlider sliderDirectMixLevel = PanelSlider.CreateEntryAndBind(
                mixGroup,
                PanelSlider.SliderSettings.Advanced("Direct Mix Level", 0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.RADirectMixLevel);

            // ─────────────── RESET BUTTON ───────────────
            SettingsProvider.AddResetPageButton(container, "Remote Audio", ResetRemoteAudioDefaults);

            descriptor.ForceRebuild();
            return tab;
        }

        private static void ResetRemoteAudioDefaults()
        {
            // AudioSource
            BasisSettingsDefaults.RAMinDistance.ResetToDefault();
            BasisSettingsDefaults.RASpread.ResetToDefault();
            BasisSettingsDefaults.RADopplerLevel.ResetToDefault();
            BasisSettingsDefaults.RASpatialBlend.ResetToDefault();

            // HRTF
            BasisSettingsDefaults.RADirectBinaural.ResetToDefault();
            BasisSettingsDefaults.RAPerspectiveCorrection.ResetToDefault();
            BasisSettingsDefaults.RAInterpolation.ResetToDefault();

            // Propagation
            BasisSettingsDefaults.RADistanceAttenuation.ResetToDefault();
            BasisSettingsDefaults.RAAirAbsorption.ResetToDefault();

            // Directivity
            BasisSettingsDefaults.RADirectivity.ResetToDefault();
            BasisSettingsDefaults.RADipoleWeight.ResetToDefault();
            BasisSettingsDefaults.RADipolePower.ResetToDefault();

            // Occlusion
            BasisSettingsDefaults.RAOcclusion.ResetToDefault();
            BasisSettingsDefaults.RAOcclusionType.ResetToDefault();
            BasisSettingsDefaults.RAOcclusionRadius.ResetToDefault();
            BasisSettingsDefaults.RAOcclusionSamples.ResetToDefault();

            // Transmission
            BasisSettingsDefaults.RATransmission.ResetToDefault();
            BasisSettingsDefaults.RATransmissionType.ResetToDefault();
            BasisSettingsDefaults.RAMaxTransmissionSurfaces.ResetToDefault();

            // Mix
            BasisSettingsDefaults.RADirectMixLevel.ResetToDefault();

            ApplyRemoteAudioToAll();
        }

        /// <summary>
        /// Applies current remote audio settings to all active remote players.
        /// </summary>
        public static void ApplyRemoteAudioToAll()
        {
            foreach (var kvp in BasisNetworkPlayers.RemotePlayers)
            {
                BasisNetworkReceiver receiver = kvp.Value;
                if (receiver?.AudioReceiverModule != null && receiver.AudioReceiverModule.HasAudioSource)
                {
                    ApplyRemoteAudioTo(receiver.AudioReceiverModule);
                }
            }
        }

        /// <summary>
        /// Applies current remote audio settings to a single audio receiver.
        /// </summary>
        public static void ApplyRemoteAudioTo(BasisAudioReceiver receiver)
        {
            if (receiver == null || receiver.audioSource == null)
                return;

            AudioSource source = receiver.audioSource;

            // AudioSource settings
            source.minDistance = BasisSettingsDefaults.RAMinDistance.RawValue;
            source.spread = BasisSettingsDefaults.RASpread.RawValue;
            source.dopplerLevel = BasisSettingsDefaults.RADopplerLevel.RawValue;
            source.spatialBlend = BasisSettingsDefaults.RASpatialBlend.RawValue;

#if STEAMAUDIO_ENABLED
            // Steam Audio settings
            if (source.TryGetComponent<SteamAudioSource>(out var sa))
            {
                // HRTF
                sa.directBinaural = BasisSettingsDefaults.RADirectBinaural.RawValue;
                sa.perspectiveCorrection = BasisSettingsDefaults.RAPerspectiveCorrection.RawValue;
                sa.interpolation = ParseInterpolation(BasisSettingsDefaults.RAInterpolation.RawValue);

                // Propagation
                sa.distanceAttenuation = BasisSettingsDefaults.RADistanceAttenuation.RawValue;
                sa.airAbsorption = BasisSettingsDefaults.RAAirAbsorption.RawValue;

                // Directivity
                sa.directivity = BasisSettingsDefaults.RADirectivity.RawValue;
                sa.dipoleWeight = BasisSettingsDefaults.RADipoleWeight.RawValue;
                sa.dipolePower = BasisSettingsDefaults.RADipolePower.RawValue;

                // Occlusion
                sa.occlusion = BasisSettingsDefaults.RAOcclusion.RawValue;
                sa.occlusionType = ParseOcclusionType(BasisSettingsDefaults.RAOcclusionType.RawValue);
                sa.occlusionRadius = BasisSettingsDefaults.RAOcclusionRadius.RawValue;
                sa.occlusionSamples = (int)BasisSettingsDefaults.RAOcclusionSamples.RawValue;

                // Transmission
                sa.transmission = BasisSettingsDefaults.RATransmission.RawValue;
                sa.transmissionType = ParseTransmissionType(BasisSettingsDefaults.RATransmissionType.RawValue);
                sa.maxTransmissionSurfaces = (int)BasisSettingsDefaults.RAMaxTransmissionSurfaces.RawValue;

                // Mix
                sa.directMixLevel = BasisSettingsDefaults.RADirectMixLevel.RawValue;

                sa.ForceUpdate();
            }
            else
            {
                BasisDebug.LogError("Missing SteamAudio");
            }
#endif
        }

        private static HRTFInterpolation ParseInterpolation(string value)
        {
            if (string.Equals(value, "bilinear", StringComparison.OrdinalIgnoreCase))
                return HRTFInterpolation.Bilinear;
            return HRTFInterpolation.Nearest;
        }

        private static OcclusionType ParseOcclusionType(string value)
        {
            if (string.Equals(value, "volumetric", StringComparison.OrdinalIgnoreCase))
                return OcclusionType.Volumetric;
            return OcclusionType.Raycast;
        }

        private static TransmissionType ParseTransmissionType(string value)
        {
            if (string.Equals(value, "frequency dependent", StringComparison.OrdinalIgnoreCase))
                return TransmissionType.FrequencyDependent;
            return TransmissionType.FrequencyIndependent;
        }
    }
}
