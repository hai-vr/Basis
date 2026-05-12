# TODO

Topics being discussed:
- 🟨 Remember and restore values when loading an avatar.
- ⬜ Discuss avatar-wide throughput limits.
- ⬜ Integrate Vixxy with future non-settings menu systems. (check with dooly/vowgan)

Fixes to do:
- ⬜ Reduce the size of the initialization packets, possibly by reusing the data stored within RequireNetworked.
  - An alternative is to map all addresses to SHA-1 or another function.
  - ⬜ Consider generating a hash of all the unique addresses to serve as verification and transmit only the values, not the keys.
- ⬜ Duplicating an object group does not duplicate the references to the Property instances.
- ⬜ Investigate the "Illegal Sender" error in the logs.
- 🟨 HDR colors are using the wrong type of property.

Optimizations:
- ⬜ Optimize the size of the networked packets by removing the array length
- ⬜ Optimize the size of the networked addresses using bytes instead of ushort when the total number of addresses is less than 256.
- ⬜ Actuate the scene objects based on a change in the (choiceA, choiceB, lerp value) tuple, rather than a change in the clamped input value.

Things that remain to be done in Vixxy:
- ⬜ Auto-upgrade addresses to use the server reduction system when a different value **is sent** too many times per second.
  - We do not want to auto-upgrade addresses based on how many times OnAddressUpdated is called, as it may be called multiple times with the same value.
  - ⬜ Auto-downgrade addresses that used the server reduction system when the same value has stalled for way too long.
- ⬜ Allow receiving values from external programs even when Face Tracking is not present on the avatar / Let the user specify that an address is driven by an external program / Each control should have a component that depends on it so that we can build avatar optimizers.
- ⬜ Support more property types:
  - ⬜ Add transform position, rotation, and scale.
  - ⬜ Add materials slot swaps.
  - ⬜ Add array swaps in general.
  - ⬜ Add effect triggers, such as audio and particle systems.
- ⬜ Add interpolation setting at the network level (for sliders and some hardware addresses).
- ⬜ Add aggregators (conditionals).
- ✅ Add filters.
  - ✅ Add Linear move towards value filter.
  - ✅ Add Smooth towards value filter.
  - ✅ Add Curve filter.

Things that remain to be done in Comms:
- ⬜ Migrate Face Tracking and Eye Tracking to use the underlying facilities of this system (OnAddressUpdated becomes the only input, removing OnInterpolationDataChanged).
- ⬜ Migrate HVRVariableNetworking.Update() and HVRVixxyOrchestrator.Update() to use BasisEventDriver functions.
- ⬜ Add renderer visibility component (if renderer is visible -> enable Control).
- ⬜ Add measurement component.
  - ⬜ Add local measurements.
    - ✅ Add Distance.
    - ✅ Add Angle.
    - ✅ Add Raycast.
    - ✅ Add Speed.
    - ⬜ Add Unity Collider (Trigger).
    - ⬜ Add Unity Collider Physics.
    - ⬜ Add Particle Collision.
  - ⬜ Add measurements derived from other systems.
    - 🟨 Add OpenLipSync input.
      - OpenLipSync variables should never be networked.
    - ⬜ Add finger curls.
    - ⬜ Add networked measurements.
      - ⬜ Add controller trigger.
      - ⬜ Add controller grip.
- ⬜ Add zipper component.
- ⬜ Add abuse limitations (max string length, max number of variables).

-----

# Vixxy Protocol WIP

🚧 = This doesn't exist yet.

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

🚧 Initialization packet extension:
- Before initialization packets are sent, if the number of networked addresses is strictly greater than 256:
  - Wearer sends a packet that signals network IDs use ushort instead of byte.

Runtime, event-driven approach:
- Every 1/10 of a second, after a networked address has at least changed to a different value once:
  - 🚧 Wearer records the actual delta time since the last evaluated 1/10 of a second (even if a packet was not sent).
  - 🚧 Wearer collects the largest delta of each changed address.
  - Wearer treats the value of 0.0 and 1.0 specially and puts them into buckets.
  - Wearer sends a data packet of type Zero/One/Zeroes and Ones/Mixed, based on the contents of the buckets.
- When the Remote receives a data packet:
  - 🚧 If the address is interpolated:
    - 🚧 Remote puts the values the addresses referenced by those network IDs into a tape, with the timing information associated with that packet.
  - Otherwise:
    - Remote sets the values for the addresses referenced by those network IDs.

🚧 Runtime, add high-frequency:
- When Wearer wants to signal that an address is high-frequency:
  - Wearer records the network ID, minimum, and maximum value for an address.
  - Wearer sends upgrade packets. Upgrade packets must be sent in the order the network IDs will be in the Server Reduction system.

🚧 Runtime, remove high-frequency:
- TODO. It is easier to add high-frequency addresses than remove them, because removal changes the schema, it doesn't just get appended at the end.
- Consider adding a byte to encode the schema number.

🚧 Runtime, Server Reduction:
- Every 1/10 of a second, after a networked address has at least changed to a different value once:
  - Wearer records the actual delta time since the last evaluated 1/10 of a second (even if a packet was not sent).
  - 🚧 Wearer collects the largest delta of each changed address.
  - Wearer quantizes floats to byte from the min to max value. If -min == max, only 255 out of the 256 possible values are used for quantization.
  - Data is packed in the order that the network IDs were last upgraded to high-frequency.
