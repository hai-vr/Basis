Changelog
=====

## 2026-04-24

New additions:
- Add a toggle system for avatars using HVRVixxyControl, HVRVixxyMenuItem, and HVRVixxyAggregator.
- The toggles are accessible in-game through "Settings > My Avatar" if at least once VixxyMenuItem component is present on the avatar.

Modifications in Basis SDK:
- The following types are now allowed in avatars: HVR.Vixxy.HVRVixxyControl, HVR.Vixxy.HVRVixxyMenuItem, and HVR.Vixxy.HVRVixxyAggregator.

Modifications in existing HVR.Basis systems:
- Acquisition Service now keeps track of values that were sent to it, rather than just emitting events.
- The SettingsProvider avatar tab override now integrates with Vixxy menu items.
- HVRAvatarComms now uses the networking carrier at index 1 (this means the second one) for networking Vixxy-specific messages.
- Remove "Avatar" from the display name of the package, so that is now "HVR Basis Comms".
- Renamed HVRAddress to HVRAddressRegistry, so that the name HVRAddress can be used for a new addition.

Editor modifications in existing HVR.Basis systems:
- Most HVR components now show additional information in Play Mode about their internal state.
    - HVRAvatarComms now shows all mutualized addresses and the corresponding values.
    - AcquisitionService now shows all registered addresses, the number of event listeners for each, and the last value.
    - OSCAcquisitionServer now shows all received addresses (OSC only) and how many messages have been received for each.
- Add icons to user-facing components.
