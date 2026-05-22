Changelog
=====

## 2025-05-21

*Authored by Haï~*

New additions:
- Add transition duration.
  - The input value of a control can now change over time or be processed by a filter. The following filters are provided: Smooth towards, linear move towards, and curve.
  - This processing is done locally; the value of the address itself does not change over time.
- Lipsync values are available as addresses to be read by Vixxy.
- Effects can be triggered based on the distance, angle, or rotation between objects.
  - Add HVR Measure component to avatar content policy.
- Changing Transform rotation, position, and scale is now permitted.
- In the editor, add a button to capture the current value of a property from the scene.

Major modifications:
- Migrate Face Tracking to use the same system as Vixxy.
    - FaceTrackingActivityRelay only triggers on addresses that start with FT/, as the migration of Face Tracking to the same system as Vixxy was causing a feedback loop.
    - The old systems have been removed.
- Network transmission of an address will now use server reduction if it changes value at least 100 times.

Modifications in existing HVR.Basis systems:
- Functional:
  - Sliders for controls that use multiple choices now fade between the choices. Previously, the slider would snap settings to the nearest whole number.
  - Addresses chosen automatically for controls now account for objects having the same name.
  - Value is clamped per-control between the minimum and the maximum choice value. This is done in preparation for high-frequency networking, where the value is clamped by technical necessity.
  - Handle quaternion properties represented as Euler angles, with Spherical or Euler interpolation.
- Usability:
  - When a Vixxy Control is created, the choices are named by default to "OFF" and "ON"
  - In the Toggle Objects UI, "Enable these when active" has been replaced with "Affect these objects" and now uses the same system as multiple choices.
  - In the Toggle Objects UI, toggles are now shown to the left.
  - In the Change Properties UI, "Inactive" and "Active" now use the same system as multiple choices.
  - In the Control UI, menu creation or the selection of an address is now done through a menu.
  - In the Control UI, choices are now always shown.
  - In the Menu editor UI, the menu settings are always shown.
  - Address and Networking have been moved to their own "Advanced Settings" tab since they don't need to be touched.
- Optimizations:
  - Minimum and maximum values for a control are now baked at runtime.
  - Avoid GetComponent call in Menu Item.
  - Reduce the size of some networking packets, as we can deduce the size of the last list in a packet.
  - Addresses are now transmitted in a byte instead of an ushort if there are 256 network addresses or less.

Fixes:
- Fix editor was unable to switch between object groups or delete one because a key already exists.
- Fix editor must not use the "GetComponent<SkinnedMeshRenderer>() is { } smr" syntax as this can return true despite the object not having a SkinnedMeshRenderer component.
- Fix rejoining or joining multiple servers with the same avatar was incorrectly not submitting the state of the avatar to other users.
- Workaround initialization packet was exceeding the maximum size when a large number of toggles was created.
  - The initialization packet is now chunked into groups of 10 toggles per packet.
- Fix HDR properties was using the wrong type.
- When a user requests initialization, the wearer sends the responses to that user instead of all users.
- Fix Toggle Objects sometimes did not show all objects being toggled.
- Fix adding a new object group should now produce an empty group without stray references of existing objects.

## 2025-05-02

*Authored by Haï~*

Modifications in existing HVR.Basis systems:
- The Basis Framework has changed the "My Avatar" settings tab and its API.
- Re-integrate Vixxy into the new "Avatar Customization" settings tab.

## 2026-05-01

*Authored by Haï~*

New additions:
- Add a toggle system for avatars using HVRVixxyControl, HVRVixxyMenuItem.
- The toggles are accessible in-game through "Settings > My Avatar" if at least one VixxyMenuItem component is present on the avatar.

Modifications in Basis SDK:
- The following types are now allowed in avatars: HVR.Vixxy.HVRVixxyControl, HVR.Vixxy.HVRVixxyMenuItem, and HVR.Vixxy.HVRVixxyAggregator.
- HVR.Vixxy.HVRVixxyAggregator is not implemented and cannot be used.

Modifications in existing HVR.Basis systems:
- The SettingsProvider avatar tab override now integrates with Vixxy menu items.
- Addresses and values can now be networked directly:
  - Add HVRVariableState to handle the networking of avatar state.
  - Add new packet types in AvatarMessageProcessing to support the networking of avatar state.
  - HVRAvatarComms now uses the networking carrier at index 1 (this means the second one) for networking messages related to avatar state.
  - HVRAvatarComms now holds a reference to a variable store which can be subscribed to (instead of AcquisitionService).
  - AcquisitionService now delegates to a variable store.
- Remove "Avatar" from the display name of the package, so that is now "HVR Basis Comms".
- HVRAddress is now a ScriptableObject.
- Calls to HVRLogging are no longer shown in the stack trace.
- Obsolete AcquisitionAssist and RandomAcquisition components have been removed.
- Unused JiggableSkinnedMeshRenderer class has been removed.

Editor modifications in existing HVR.Basis systems:
- Most HVR components now show additional information in Play Mode about their internal state.
    - HVRAvatarComms now shows all mutualized addresses and the corresponding values.
    - AcquisitionService now shows all registered addresses, the number of event listeners for each, and the last value.
    - OSCAcquisitionServer now shows all received addresses (OSC only) and how many messages have been received for each.
- Add icons to user-facing components.
