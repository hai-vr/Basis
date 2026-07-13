# Mocap corpus — third-party data

Ground truth for `BasisMocapAccuracyTests`. Not shipped in player builds and not imported by Unity: the
trailing `~` on the folder name keeps it out of the asset database entirely.

## CMU Graphics Lab Motion Capture Database

- Source: http://mocap.cs.cmu.edu/
- BVH conversion: Bruce Hahne (cgspeed), via https://github.com/una-dinosauria/cmu-mocap
- Files: `02_01.bvh` (general motion), `07_01.bvh` (walk), `09_01.bvh` (run)
- Files, added: a filename is the CMU `subject_trial` — `141_20.bvh` is subject 141, trial 20, and the CMU
  index describes every trial. Picked for the poses a VR user actually holds, idle first:
  - `77_02.bvh` (standing still), `113_21.bvh` (standing still, long quiet hold), `141_20.bvh` (waiting —
    restless idle: weight shifts and small steps in place, no net travel)
  - `141_15.bvh` (range of motion — a full sweep of every joint through its limits)
  - `143_11.bvh` (walk up, bend, pick up box), `143_26.bvh` (washing a window — sustained arm reach),
    `26_09.bvh` (bend over, pick up), `69_70.bvh` (walk to object, squat, pick up, set down)
  - `69_16.bvh` (turn in place), `77_05.bvh` (look around with a flashlight)
  - `07_04.bvh` (slow walk), `132_18.bvh` (fast walk), `16_11.bvh` (walk, veering left — curved path)
  - `143_18.bvh` (sit down and get up), `141_17.bvh` (sit on a stool)
  - `143_25.bvh` (waving)
  - `143_17.bvh` (walk up stairs and step over)

CMU places no restrictions on the use of this data, including commercial use. From the database's own terms:

> This data is free for use in research and commercial projects worldwide. You may include this data in
> commercially-released products.

The database was created with funding from NSF EIA-0196217.

## Adding more clips

Drop any `.bvh` into this folder and the tests pick it up automatically. The loader expects the standard
Biovision hierarchy with CMU/Biovision joint names (`LeftArm`, `LeftForeArm`, `LeftUpLeg`, …); see the name map
in `BasisBvhLoader`. Every clip is checked for anatomical sanity (left hand on the left, knees bending forward)
before any measurement is taken from it, so a bad file fails loudly rather than quietly skewing the numbers.
