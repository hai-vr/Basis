# Basis Meta Body Tracking

Full-body tracking from the headset itself, with no physical trackers.

The headset's OpenXR runtime already solves a body pose for you. This package takes the joints it
reports and hands them to Basis as ordinary trackers, so hips, chest, elbows, knees and feet drive
the avatar through exactly the same calibration and IK path a lighthouse puck or a SlimeVR tracker
would.

## What it uses

| Extension | What it buys |
| --- | --- |
| `XR_FB_body_tracking` | The body solve itself: hips, spine, chest, arms, hands. Required — without it the package is inert. |
| `XR_META_body_tracking_full_body` | Extends the same skeleton with legs and feet. Without it you get the upper body only. |
| `XR_META_body_tracking_fidelity` | Asks for the camera-driven solve rather than the cheap one inferred from head and controllers. |
| `XR_META_body_tracking_calibration` | Lets Basis hand the runtime your measured height instead of letting it estimate one. |

Each is optional except the first, and each is checked at runtime, so this runs on Quest standalone,
over Link, and does nothing at all on a runtime that offers none of them.

## How it fits together

- `BasisMetaBodyTrackingFeature` — an OpenXR feature that creates the body tracker, keeps the
  predicted display time (by wrapping `xrWaitFrame`), locates every joint once per frame and hands
  them out in Unity space. Enable **Basis Meta Body Tracking** under
  *Project Settings > XR Plug-in Management > OpenXR* for Standalone and Android.
- `BasisMetaBodyTrackerSource` — creates and removes one Basis input device per body part, following
  the source setting.
- `BasisMetaBodyTrackerInput` — a `BasisInput` whose pose is one joint.
- `BasisMetaBodyTrackerRoles` — announces each device's body part through its `metabody://…` serial,
  so the framework binds the roles and runs a full-body calibration pass by itself.
- `SettingsProviderMetaBodyTracking` — the controls, in *Settings > Trackers*.

## Settings

- **Use Headset Body Tracking** — `Off`, `Fill Gaps Only` (default: a body part a physical tracker
  already holds is left alone) or `Always Take Over`.
- **Hips, Chest and Elbows** / **Knees and Feet** — which body parts to source.
- **Bind Trackers Automatically** — bind each part straight to its bone instead of running these
  trackers through manual calibration.
- **High Fidelity Solve** — the camera-driven solve. Costs headset performance.
- **Send Your Height** — pass the height Basis measured to the runtime's body calibration.

## Caveats

- The joints are a *solve*, not a measurement, so the devices classify as
  `BasisTrackingHardware.Estimated` and get filtered accordingly.
- Head and hands are deliberately not sourced; the HMD and controllers own those bones.
- A headset that solves no legs falls back to the upper-body joint set on its own, and the knee and
  foot devices are simply never created.
