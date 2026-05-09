# TODO

Topics being discussed:
- 🟨 Remember and restore values when loading an avatar.
- ⬜ Discuss avatar-wide throughput limits.
- ⬜ Integrate Vixxy with future non-settings menu systems. (check with dooly/vowgan)

Fixes to do:
- ⬜ Reduce the size of the initialization packets, possibly by reusing the data stored within RequireNetworked.
  - An alternative is to map all addresses to SHA-1, to get consistent addresses.
- ⬜ Duplicating an object group does not duplicate the references to the Property instances.
- ⬜ Investigate the "Illegal Sender" error in the logs.
- ⬜ Optimize the size of the networked packets by removing the array length
- ⬜ Optimize the size of the networked addresses using bytes instead of ushort when the total number of addresses is less than 256.

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
    - ⬜ Add OpenLipSync input.
    - ⬜ Add finger curls.
    - ⬜ Add networked measurements.
      - ⬜ Add controller trigger.
      - ⬜ Add controller grip.
- ⬜ Add zipper component.
