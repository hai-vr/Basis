# TODO

Fixes to do:
- [ ] Reduce the size of the initialization packets, possibly by reusing the data stored within RequireNetworked.
  - An alternative is to map all addresses to SHA-1, to get consistent addresses.
- [ ] Duplicating an object group does not duplicate the references to the Property instances.
- [ ] Investigate the "Illegal Sender" error in the logs.

Things that remain to be done in Vixxy:
- [ ] Auto-upgrade addresses to use the server reduction system when a different value **is sent** too many times per second.
  - We do not want to auto-upgrade addresses based on how many times OnAddressUpdated is called, as it may be called multiple times with the same value.
- [ ] Allow receiving values from external programs even when Face Tracking is not present on the avatar.
- [ ] Support more property types:
  - [ ] Add transform position, rotation, and scale.
  - [ ] Add materials slot swaps.
  - [ ] Add array swaps in general.
  - [ ] Add effect triggers, such as audio and particle systems.
- [ ] Add interpolation setting at the network level (for sliders and some hardware addresses).
- [ ] Remember and restore values when loading an avatar with the same name or the same user-defined identifier or tag.
- [ ] Add aggregators (conditionals) and filters (change over-time, lerp, smoothing).
- [ ] Integrate Vixxy with future non-settings menu systems.

Things that remain to be done in Comms:
- [ ] Migrate Face Tracking and Eye Tracking to use the underlying facilities of this system (OnAddressUpdated becomes the only input,
    removing OnInterpolationDataChanged).
- [ ] Add measurement component.
- [ ] Add zipper component.
