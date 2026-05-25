# TODO

🟨 = Being worked on
✅ = Implemented
🟪 = Partially implemented, postponed

Topics being discussed:
- 🟨 Remember and restore values when loading an avatar.
- ⬜ Discuss avatar-wide throughput limits.
- ⬜ Integrate Vixxy with future non-settings menu systems. (check with dooly/vowgan)

Fixes to do:
- ✅ Duplicating an object group does not duplicate the references to the Property instances.
- ✅ HDR colors are using the wrong type of property.
- ⬜ Reduce the size of the initialization packets, possibly by reusing the data stored within RequireNetworked.
  - An alternative is to map all addresses to SHA-1 or another function.
  - ⬜ Consider generating a hash of all the unique addresses to serve as verification and transmit only the values, not the keys.
- ⬜ Investigate the "Illegal Sender" error in the logs.
- ⬜ Investigate how messages from VRCFaceTracking seem to arrive with more irregularity when the Basis window is focused / when the VRCFaceTracking window is not focused.
- 🟨 Remote should ignore any incoming NewNet_WearerSubmitsUpdatedHighFrequencyVariables packets until it has received all new variables.
- ⬜ HVRBasisBuiltInAddresses may not handle Dictionary with destroyed Comms components.

Optimizations:
- ⬜ Actuate the scene objects based on a change in the (choiceA, choiceB, lerp value) tuple, rather than a change in the clamped input value.

Things that remain to be done in Vixxy:
- ⬜ Improve the heuristic for upgrading the address.
  - ⬜ Consider not auto-upgrading addresses if the only value that is set is 1.0 or 0.0.
- ⬜ Auto-downgrade addresses that used the server reduction system when the same value has stalled for way too long.
- ⬜ Allow receiving values from external programs even when Face Tracking is not present on the avatar / Let the user specify that an address is driven by an external program / Each control should have a component that depends on it so that we can build avatar optimizers.
- ⬜ Support more property types:
  - ⬜ Add materials slot swaps.
  - ⬜ Add array swaps in general.
  - ⬜ Add effect triggers, such as audio and particle systems.
- ⬜ Add a configuration or heuristic or UI warning to choose the interpolation setting of a control.
- ⬜ Add aggregators (conditionals).
- ⬜ Show in the UI that there's special networking on control which address is a Measure (use the same symbol as System).
- ⬜ Allow Controls to directly reference Measure components for prefabbing.

Things that remain to be done in Comms:
- ⬜ Make a fix for when the user closes both eyes, EyeTrackingActive sets to off, because there's effectively no data coming in.
  - ⬜ Toys suggests that we ought to expose eye tracking as a toggle in the avatar customization menu. This sounds good
  - ⬜ Make sure that both BlendshapeActuation and EyeTrackingBoneActuation still work even if Vixxy is not present in the avatar.
- ⬜ Add renderer visibility component (if any renderer of that list is visible -> enable Control).
  - For use with conflict prevention blendshapes / JiggleRig disablers.
- 🟨 Add measurement component.
  - 🟨 Add local measurements.
    - 🟪 Add Raycast.
    - 🟪 Add Speed.
    - ⬜ Add Unity Collider (Trigger).
    - ⬜ Add Unity Collider Physics.
    - ⬜ Add Particle Collision.
  - 🟨 Add measurements derived from other systems.
    - ⬜ Add finger curls.
    - ⬜ Add networked measurements.
      - ⬜ Add controller trigger.
      - ⬜ Add controller grip.
- ⬜ If a control triggers a particle system or a sound, then:
  - it must happen even if OSC controls happen sub-frame, or
  - we have to change the networking to only take into consideration applied values for variables each frame; ignoring sub-frame changes.
- ⬜ Specify whether transitions should go across choices.

Long-term objectives:
- ⬜ Migrate HVRVariableNetworking.Update() and HVRVixxyOrchestrator.Update() to use BasisEventDriver functions.
- ⬜ Slow down interpolation delay when server reduction kicks in.
  - ⬜ If the queue is starving, slow down the playback.
- ⬜ Make Vixxy usable in Props.
  - ⬜ Make Vixxy usable in Scene.
- ⬜ Add zipper component.
- ⬜ Send messages at a faster rate for direct connections.
- ⬜ Add more abuse limitations.

-----

# Interpolator

- When unpacking, add (time + [(address, value)] to interpolator).
- When playing the interpolator:
  - Check if we need to advance the tape.
  - Advance the tape.
    - Previous gets the value of the previous shapshot, and the previous shapshot alone.
    - If the new tape shapshot needs a new address, get the value for that address as previous.
    - If the new tape does not use an address that the previous shapshot has, do not interpolate, but remember that previous snapshot value, once, for that frame.
  - Check again if we need to advance the tape, repeat if needed.
  - If the tape is empty, then all values to be applied are "previous value" for that frame.
  - Calculate the lerped values of the addresses to apply for that frame.
  - Apply the "previous shapshot value" for that frame.

-----

# Vixxy Protocol WIP

🚧 = This doesn't exist yet.
❗ = Added, not tested.
✅ = Added

Initialization:
- When Wearer loads:
  - Wearer chooses a network ID for each address ID.
  - Wearer sends initialization packets to Everyone else, including the initial value.
- When Remote loads:
  - Remote asks Wearer for initialization.
  - Wearer sends initialization packets to that Remote, including the current value (which may not be the initial value).
- When the Remote receives an initialization packet:
  - Remote binds the network ID to the address ID.
  - Remote sets the value for that address.

❗ Initialization packet extension:
- Before initialization packets are sent, if the number of networked addresses is strictly greater than 256:
  - Wearer sends a packet that signals network IDs use ushort instead of byte.

Runtime, event-driven approach:
- Every 1/10 of a second, after a networked address has at least changed to a different value once:
  - Wearer records the actual delta time since the last evaluated 1/10 of a second (even if a packet was not sent).
  - Wearer collects the largest delta of each changed address.
  - Wearer treats the value of 0.0 and 1.0 specially and puts them into buckets.
  - Wearer sends a data packet of type Zero/One/Zeroes and Ones/Mixed, based on the contents of the buckets.
- When the Remote receives a data packet:
  - If the address is interpolated:
    - Remote puts the values the addresses referenced by those network IDs into a tape, with the timing information associated with that packet.
  - Otherwise:
    - Remote sets the values for the addresses referenced by those network IDs.

Runtime, add high-frequency:
- When Wearer wants to signal that an address is high frequency:
  - Wearer records the network ID, minimum, and maximum value for an address.
  - Wearer sends upgrade packets. Upgrade packets must be sent in the order the network IDs will be in the Server Reduction system.

🚧 Runtime, remove high-frequency:
- TODO. It is easier to add high-frequency addresses than remove them, because removal changes the schema, it doesn't just get appended at the end.
- Consider adding a byte to encode the schema number.

Runtime, Server Reduction:
- Every 1/10 of a second, after a networked address has at least changed to a different value once:
  - Wearer records the actual delta time since the last evaluated 1/10 of a second (even if a packet was not sent).
  - Wearer collects the largest delta of each changed address.
  - Wearer quantizes floats to byte from the min to max value.
    - If -min == max, only 255 out of the 256 possible values are used for quantization so that the value of 0 can be encoded.
  - Data is packed in the order that the network IDs were last upgraded to high frequency.
