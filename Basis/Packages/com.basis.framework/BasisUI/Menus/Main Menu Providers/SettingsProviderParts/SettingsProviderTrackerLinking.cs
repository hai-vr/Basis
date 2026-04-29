using Basis.Scripts.Avatar;
using Basis.Scripts.Device_Management;
using Basis.Scripts.Device_Management.Devices;
using Basis.Scripts.Device_Management.Devices.Pairing;
using Basis.Scripts.TransformBinders.BoneControl;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.BasisUI
{
    /// <summary>
    /// Settings tab that lists every connected full-body-eligible tracker and lets
    /// the user link any two of them. The constellation classifier in
    /// BasisAvatarIKStageCalibration reads the resulting pairings and feeds each
    /// pair to calibration as a single midpoint sample, so e.g. a front + back hip
    /// tracker pair gets bound to the Hips role together.
    ///
    /// Layout: a static intro + tuning section at the top (built once when the tab
    /// is created and bound to settings so values persist), then a dynamic list of
    /// per-tracker pair-pickers below that updates whenever devices come or go or
    /// the pairing graph changes.
    ///
    /// Pairing-graph updates only refresh the *selected value* of each existing
    /// dropdown — they do NOT tear the dropdowns down. Tearing down a dropdown
    /// while the user is mid-click on one of its options destroys the GameObject
    /// the click is being routed to and makes the whole entry list flicker out.
    /// A full rebuild only happens when the set of eligible trackers actually
    /// changes (a tracker connected or disconnected).
    /// </summary>
    public static class SettingsProviderTrackerLinking
    {
        private const string UnlinkedLabel = "(unlinked)";
        private const string TabKey = "settings.tab.trackerlinking";

        // Per-tab-instance state captured in a closure. One instance per Settings
        // open; cleared in OnInstanceReleased so reopening Settings starts fresh.
        private sealed class TabState
        {
            public RectTransform TrackersContainer;
            // The wrapping group whose ContentParent is TrackersContainer.
            // Children are added/removed inside its content area, so this is
            // the descriptor whose own layout needs to settle when the row
            // count changes.
            public PanelElementDescriptor TrackersGroup;
            // Tab page's root descriptor — rebuilt alongside TrackersGroup
            // because Unity's layout cascade doesn't always propagate up
            // through nested LayoutGroups on its own. Same two-step rebuild
            // pattern as the visibility toggles in SettingsProviderIK
            // (advancedToggle / debugToggle handlers).
            public PanelElementDescriptor TabDescriptor;
            public readonly bool[] SuppressDropdownEvents = { false };
            // True after the first build pass has run. Lets the diff logic force
            // a full rebuild on the initial call even when there are zero
            // trackers (so the "no trackers" panel actually gets created).
            public bool HasBuilt;
            // Parallel lists kept in lockstep: Ids[i] is the device shown by
            // EntryDropdowns[i] / EntryDescriptors[i]. Used to detect whether
            // an event needs a full rebuild or just a value refresh.
            public readonly List<string> Ids = new List<string>();
            public readonly List<PanelDropdown> EntryDropdowns = new List<PanelDropdown>();
            public readonly List<PanelElementDescriptor> EntryDescriptors = new List<PanelElementDescriptor>();
        }

        public static PanelTabPage TrackerLinkingTab(PanelTabGroup tabGroup)
        {
            PanelTabPage tabPage = PanelTabPage.CreateVertical(tabGroup.Descriptor.ContentParent);
            PanelElementDescriptor tabDesc = tabPage.Descriptor;
            tabDesc.SetTitle(BasisLocalization.Get(TabKey));
            tabDesc.SetIcon(AddressableAssets.Sprites.Calibrate);

            RectTransform tabRoot = tabDesc.ContentParent;

            // Static intro — explains what pairing does and survives device-list rebuilds.
            PanelElementDescriptor headerGroup = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, tabRoot);
            headerGroup.SetTitle(BasisLocalization.Get("trackerLinking.header.title"));
            headerGroup.SetDescription(BasisLocalization.Get("trackerLinking.header.description"));

            // Static tuning group — bound to BasisSettingsDefaults bindings, so values
            // persist across menu reopens and across sessions. Built once.
            PanelElementDescriptor tuningGroup = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, tabRoot);
            tuningGroup.SetTitle(BasisLocalization.Get("trackerLinking.tuning.title"));
            tuningGroup.SetDescription(BasisLocalization.Get("trackerLinking.tuning.description"));
            BuildTuningSliders(tuningGroup.ContentParent);

            // Reset for the tuning section. The shared helper closes and re-opens
            // Settings on the same tab so the sliders re-bind to default values.
            SettingsProvider.AddResetPageButton(tuningGroup.ContentParent, TabKey, ResetTrackerLinkingDefaults);

            // Dynamic per-tracker section — updates on device/pair changes. Lives
            // in its own container so the tuning sliders above aren't touched.
            PanelElementDescriptor trackersGroup = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, tabRoot);
            trackersGroup.SetTitle(BasisLocalization.Get("trackerLinking.trackers.title"));

            TabState state = new TabState
            {
                TrackersContainer = trackersGroup.ContentParent,
                TrackersGroup = trackersGroup,
                TabDescriptor = tabDesc,
            };

            // Single change handler routes both list and pairing events. Decides
            // whether a full rebuild is necessary by diffing the eligible-id set,
            // and otherwise just refreshes the existing dropdowns' selected values
            // and per-entry role descriptions in place.
            Action handleChange = () => HandleChange(state);

            BasisObservableList<BasisInput> devices = BasisDeviceManagement.Instance?.AllInputDevices;
            if (devices != null)
            {
                devices.OnListChanged += handleChange;
            }
            BasisTrackerPairing.OnPairingsChanged += handleChange;

            tabPage.OnInstanceReleased += () =>
            {
                BasisObservableList<BasisInput> currentDevices = BasisDeviceManagement.Instance?.AllInputDevices;
                if (currentDevices != null)
                {
                    currentDevices.OnListChanged -= handleChange;
                }
                BasisTrackerPairing.OnPairingsChanged -= handleChange;
                state.TrackersContainer = null;
                state.TrackersGroup = null;
                state.TabDescriptor = null;
                state.HasBuilt = false;
                state.Ids.Clear();
                state.EntryDropdowns.Clear();
                state.EntryDescriptors.Clear();
            };

            handleChange();
            tabDesc.ForceRebuild();
            return tabPage;
        }

        private static void HandleChange(TabState state)
        {
            if (state.TrackersContainer == null) return;

            List<BasisInput> trackers = CollectEligibleTrackers();
            List<string> newIds = new List<string>(trackers.Count);
            for (int i = 0; i < trackers.Count; i++)
            {
                newIds.Add(trackers[i].UniqueDeviceIdentifier);
            }

            // Full rebuild on the first call (otherwise the empty list path
            // would skip the "no trackers" message), any time the eligible-
            // tracker set has changed (a tracker connected, disconnected, or
            // shifted position in the device list — every case where the
            // dropdown rows need to be added/removed/reordered), and as a
            // self-healing fallback if the cached dropdown count has somehow
            // gone out of sync with the tracker count.
            //
            // Otherwise — pairing graph changed but eligible trackers are the
            // same set in the same order — we update the existing dropdowns
            // in place. This is the path that protects the user's mid-click
            // dropdown from being torn down underneath them.
            bool needsRebuild = !state.HasBuilt
                || !IdsMatch(state.Ids, newIds)
                || state.EntryDropdowns.Count != trackers.Count;

            if (needsRebuild)
            {
                FullRebuild(state, trackers, newIds);
                state.HasBuilt = true;
                // Two-step layout rebuild, matching the toggle-show/hide pattern
                // in SettingsProviderIK (advancedToggle / debugToggle handlers).
                // Force the inner group's layout first so its ContentSizeFitter
                // re-measures with the new row count, then the outer tab so the
                // scroll view's content height picks up that new size. Skipping
                // either step leaves the rows in the hierarchy but invisible
                // (or stacked) because some parent kept its old measured height.
                if (state.TrackersGroup != null && !state.TrackersGroup.IsReleased)
                {
                    state.TrackersGroup.ForceRebuild();
                }
                if (state.TabDescriptor != null && !state.TabDescriptor.IsReleased)
                {
                    state.TabDescriptor.ForceRebuild();
                }
                return;
            }

            RefreshExistingEntries(state, trackers);
        }

        private static bool IdsMatch(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        private static void RefreshExistingEntries(TabState state, List<BasisInput> trackers)
        {
            state.SuppressDropdownEvents[0] = true;
            for (int i = 0; i < trackers.Count; i++)
            {
                BasisInput input = trackers[i];
                string id = input.UniqueDeviceIdentifier;

                if (i >= state.EntryDropdowns.Count) continue;
                PanelDropdown dropdown = state.EntryDropdowns[i];
                PanelElementDescriptor descriptor = state.EntryDescriptors[i];

                if (descriptor != null && !descriptor.IsReleased)
                {
                    descriptor.SetDescription(BuildEntryDescription(input));
                }
                if (dropdown != null && !dropdown.IsReleased)
                {
                    string current = ResolveCurrentSelection(id, state.Ids);
                    dropdown.SetValueWithoutNotify(current);
                }
            }
            state.SuppressDropdownEvents[0] = false;
        }

        private static void FullRebuild(TabState state, List<BasisInput> trackers, List<string> newIds)
        {
            // Snapshot children before releasing — ReleaseInstance destroys the
            // GameObject and may shift sibling indices before the next iteration.
            int childCount = state.TrackersContainer.childCount;
            Transform[] toRelease = new Transform[childCount];
            for (int i = 0; i < childCount; i++)
            {
                toRelease[i] = state.TrackersContainer.GetChild(i);
            }
            for (int i = 0; i < toRelease.Length; i++)
            {
                Transform child = toRelease[i];
                if (child == null) continue;
                PanelElementDescriptor descriptor = child.GetComponent<PanelElementDescriptor>();
                if (descriptor != null)
                {
                    descriptor.ReleaseInstance();
                }
                else
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }

            state.Ids.Clear();
            state.EntryDropdowns.Clear();
            state.EntryDescriptors.Clear();

            if (trackers.Count == 0)
            {
                PanelElementDescriptor empty = PanelElementDescriptor.CreateNew(
                    PanelElementDescriptor.ElementStyles.Group, state.TrackersContainer);
                empty.SetTitle(BasisLocalization.Get("trackerLinking.noTrackers.title"));
                empty.SetDescription(BasisLocalization.Get("trackerLinking.noTrackers.description"));
                return;
            }

            state.Ids.AddRange(newIds);

            // Suppress dropdown change events while we set initial values —
            // without this, AssignEntries + SetValueWithoutNotify can still fire
            // callbacks depending on prefab configuration, and we'd recursively
            // rebuild as we cascade through dropdowns.
            state.SuppressDropdownEvents[0] = true;
            for (int i = 0; i < trackers.Count; i++)
            {
                BuildEntryFor(state, trackers[i]);
            }
            state.SuppressDropdownEvents[0] = false;
        }

        private static void BuildEntryFor(TabState state, BasisInput input)
        {
            string id = input.UniqueDeviceIdentifier;

            PanelElementDescriptor group = PanelElementDescriptor.CreateNew(
                PanelElementDescriptor.ElementStyles.Group, state.TrackersContainer);
            group.SetTitle(id);
            group.SetDescription(BuildEntryDescription(input));

            PanelDropdown dropdown = PanelDropdown.CreateNewEntry(group.ContentParent);
            dropdown.Descriptor.SetTitle(BasisLocalization.Get("trackerLinking.linkLabel"));
            dropdown.Descriptor.SetDescription(BasisLocalization.Get("trackerLinking.linkDescription"));

            List<string> entries = new List<string>(state.Ids.Count) { UnlinkedLabel };
            for (int i = 0; i < state.Ids.Count; i++)
            {
                if (state.Ids[i] == id) continue;
                entries.Add(state.Ids[i]);
            }
            dropdown.AssignEntries(entries);
            dropdown.SetValueWithoutNotify(ResolveCurrentSelection(id, state.Ids));

            // Capture by value so the closure is stable across rebuilds.
            string capturedId = id;
            bool[] suppressFlag = state.SuppressDropdownEvents;
            dropdown.OnValueChanged += newValue =>
            {
                if (suppressFlag[0]) return;
                if (string.IsNullOrEmpty(newValue) || newValue == UnlinkedLabel)
                {
                    BasisTrackerPairing.Unlink(capturedId);
                }
                else
                {
                    BasisTrackerPairing.Link(capturedId, newValue);
                }
            };

            state.EntryDescriptors.Add(group);
            state.EntryDropdowns.Add(dropdown);
        }

        private static string BuildEntryDescription(BasisInput input)
        {
            if (input.TryGetRole(out BasisBoneTrackedRole role))
            {
                return BasisLocalization.Get("trackerLinking.entry.role", role.ToString());
            }
            return BasisLocalization.Get("trackerLinking.entry.unassigned");
        }

        private static string ResolveCurrentSelection(string id, List<string> eligibleIds)
        {
            if (BasisTrackerPairing.TryGetPartner(id, out string partner) && eligibleIds.Contains(partner))
            {
                return partner;
            }
            return UnlinkedLabel;
        }

        private static void BuildTuningSliders(RectTransform parent)
        {
            // Surprise penalty: how aggressively a glitching half loses authority.
            PanelSlider penalty = PanelSlider.CreateAndBind(parent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("trackerLinking.tuning.surprisePenalty"),
                    0.5f, 10f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.PairingSurprisePenalty);
            if (penalty != null)
            {
                penalty.Descriptor.SetDescription(BasisLocalization.Get("trackerLinking.tuning.surprisePenalty.description"));
            }

            // Surprise clamp: above this, the velocity baseline freezes.
            PanelSlider clamp = PanelSlider.CreateAndBind(parent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("trackerLinking.tuning.surpriseClamp"),
                    1.5f, 10f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.PairingSurpriseClamp);
            if (clamp != null)
            {
                clamp.Descriptor.SetDescription(BasisLocalization.Get("trackerLinking.tuning.surpriseClamp.description"));
            }

            // EMA floor (in meters/frame) — guard against divide-by-zero on a
            // frozen tracker. Fine-grained because typical motion is mm/frame.
            PanelSlider floor = PanelSlider.CreateAndBind(parent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("trackerLinking.tuning.emaFloor"),
                    0.0005f, 0.05f, false, 4, ValueDisplayMode.Meters),
                BasisSettingsDefaults.PairingEmaFloor);
            if (floor != null)
            {
                floor.Descriptor.SetDescription(BasisLocalization.Get("trackerLinking.tuning.emaFloor.description"));
            }

            // Soft-snap correction cap.
            PanelSlider maxCorrection = PanelSlider.CreateAndBind(parent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("trackerLinking.tuning.maxCorrection"),
                    0f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.PairingMaxCorrectionStrength);
            if (maxCorrection != null)
            {
                maxCorrection.Descriptor.SetDescription(BasisLocalization.Get("trackerLinking.tuning.maxCorrection.description"));
            }

            // Soft-snap half-life (in meters of error).
            PanelSlider halfLife = PanelSlider.CreateAndBind(parent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("trackerLinking.tuning.softSnapHalfLife"),
                    0.005f, 0.5f, false, 3, ValueDisplayMode.Meters),
                BasisSettingsDefaults.PairingSoftSnapHalfLife);
            if (halfLife != null)
            {
                halfLife.Descriptor.SetDescription(BasisLocalization.Get("trackerLinking.tuning.softSnapHalfLife.description"));
            }

            // Lockstep tolerance (in meters of error).
            PanelSlider lockstep = PanelSlider.CreateAndBind(parent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("trackerLinking.tuning.lockstepTolerance"),
                    0.005f, 0.5f, false, 3, ValueDisplayMode.Meters),
                BasisSettingsDefaults.PairingLockstepTolerance);
            if (lockstep != null)
            {
                lockstep.Descriptor.SetDescription(BasisLocalization.Get("trackerLinking.tuning.lockstepTolerance.description"));
            }

            // Velocity-baseline EMA alpha.
            PanelSlider emaAlpha = PanelSlider.CreateAndBind(parent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("trackerLinking.tuning.emaAlpha"),
                    0.005f, 0.5f, false, 3, ValueDisplayMode.Raw),
                BasisSettingsDefaults.PairingEmaAlpha);
            if (emaAlpha != null)
            {
                emaAlpha.Descriptor.SetDescription(BasisLocalization.Get("trackerLinking.tuning.emaAlpha.description"));
            }

            // Rest-distance EMA alpha.
            PanelSlider distEmaAlpha = PanelSlider.CreateAndBind(parent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("trackerLinking.tuning.distanceEmaAlpha"),
                    0.005f, 0.5f, false, 3, ValueDisplayMode.Raw),
                BasisSettingsDefaults.PairingDistanceEmaAlpha);
            if (distEmaAlpha != null)
            {
                distEmaAlpha.Descriptor.SetDescription(BasisLocalization.Get("trackerLinking.tuning.distanceEmaAlpha.description"));
            }

            // Weight smoothing — how fast the per-tracker confidence weights
            // respond. This is the main knob for trading reactivity against
            // midpoint stability.
            PanelSlider weightSmoothing = PanelSlider.CreateAndBind(parent,
                PanelSlider.SliderSettings.Advanced(BasisLocalization.Get("trackerLinking.tuning.weightSmoothing"),
                    0.05f, 1f, false, 2, ValueDisplayMode.Raw),
                BasisSettingsDefaults.PairingWeightSmoothing);
            if (weightSmoothing != null)
            {
                weightSmoothing.Descriptor.SetDescription(BasisLocalization.Get("trackerLinking.tuning.weightSmoothing.description"));
            }
        }

        private static void ResetTrackerLinkingDefaults()
        {
            BasisSettingsDefaults.PairingSurprisePenalty.ResetToDefault();
            BasisSettingsDefaults.PairingSurpriseClamp.ResetToDefault();
            BasisSettingsDefaults.PairingEmaFloor.ResetToDefault();
            BasisSettingsDefaults.PairingMaxCorrectionStrength.ResetToDefault();
            BasisSettingsDefaults.PairingSoftSnapHalfLife.ResetToDefault();
            BasisSettingsDefaults.PairingLockstepTolerance.ResetToDefault();
            BasisSettingsDefaults.PairingEmaAlpha.ResetToDefault();
            BasisSettingsDefaults.PairingDistanceEmaAlpha.ResetToDefault();
            BasisSettingsDefaults.PairingWeightSmoothing.ResetToDefault();
        }

        private static List<BasisInput> CollectEligibleTrackers()
        {
            List<BasisInput> result = new List<BasisInput>();
            BasisObservableList<BasisInput> devices = BasisDeviceManagement.Instance?.AllInputDevices;
            if (devices == null) return result;

            for (int i = 0; i < devices.Count; i++)
            {
                BasisInput input = devices[i];
                if (input == null) continue;
                if (string.IsNullOrEmpty(input.UniqueDeviceIdentifier)) continue;

                // The merged midpoint produced by an active pair lives in
                // AllInputDevices alongside its physical halves; hide it so users
                // don't try to link the virtual to anything.
                if (input is BasisVirtualMidpointInput) continue;

                // Pairing only makes sense for free FB-trackable devices. Skip the
                // HMD and any device whose role was pinned by the matcher (named
                // hand controllers, etc.) — those never participate in calibration.
                if (input.DeviceMatchSettings != null && input.DeviceMatchSettings.HasTrackedRole) continue;
                if (input.TryGetRole(out BasisBoneTrackedRole role))
                {
                    if (role == BasisBoneTrackedRole.Head || role == BasisBoneTrackedRole.CenterEye)
                    {
                        continue;
                    }
                }
                result.Add(input);
            }
            return result;
        }
    }
}
