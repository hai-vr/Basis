# Mocap corpus — third-party data

Ground truth for `BasisMocapAccuracyTests`. Not shipped in player builds and not imported by Unity: the
trailing `~` on the folder name keeps it out of the asset database entirely.

## CMU Graphics Lab Motion Capture Database

- Source: http://mocap.cs.cmu.edu/
- BVH conversion: Bruce Hahne (cgspeed), via https://github.com/una-dinosauria/cmu-mocap
- Files: `02_01.bvh` (general motion), `07_01.bvh` (walk), `09_01.bvh` (run)

CMU places no restrictions on the use of this data, including commercial use. From the database's own terms:

> This data is free for use in research and commercial projects worldwide. You may include this data in
> commercially-released products.

The database was created with funding from NSF EIA-0196217.

## Adding more clips

Drop any `.bvh` into this folder and the tests pick it up automatically. The loader expects the standard
Biovision hierarchy with CMU/Biovision joint names (`LeftArm`, `LeftForeArm`, `LeftUpLeg`, …); see the name map
in `BasisBvhLoader`. Every clip is checked for anatomical sanity (left hand on the left, knees bending forward)
before any measurement is taken from it, so a bad file fails loudly rather than quietly skewing the numbers.
