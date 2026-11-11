---
name: hz-meta-xr-operator-uiinteraction-scene
description: "The UIInteraction sample scene for Meta XR Operator (Core-SDK-only world-space UI). How to drive it with gaze+pinch, controllers, and hand ray, the aiming gotchas, and the intentional seeded bugs. Read before testing or building on this scene."
allowed-tools:
  - Bash(metavr:*)
  - Bash(hzdb:*)
---

# UIInteraction Sample Scene

## Goal

Showcase Meta XR Operator driving **world-space Unity UI**. Uses the **Core-SDK OVR**
interaction stack (`OVRRaycaster`, `OVRInputModule`, `OVRHand`/`OVRControllerHelper`,
`OVRGazePinchInputSource`). Two world-space canvases:

- **Left — `ButtonCanvas/Panel/Button` … `Button (3)`** = Red, Blue, Yellow, Green —
  recolor `Environment/Cube`.
- **Right — `InteractableCanvas/Panel/…`**: `Toggle` (Texture Enabled), `Slider`
  (Scale), `TMP_Dropdown` (Shape) — drive `Environment/MeshObject`.

Ships with **intentional seeded bugs** (see below).

## Coordinates

- Tracking origin is **EyeLevel → OpenXR `local`**, so **Unity world = `local` with Z
  negated**. Confirm via `unity_get_world_pose`'s `tracking_origin`.
- Aim at a control by its world position: `unity_get_world_pose` (e.g.
  `ButtonCanvas/Panel/Button (1)` = Blue), negate Z for `local`. Head via
  `openxr_get_head_pose base_space:"local"`. See **hz-meta-xr-operator-coordinates**.

## Input modalities

The scene switches **live** between controllers and hands: a Touch controller that is
**in hand** takes over; set it down (or use hands) and gaze/hand-ray resume — no
pause/resume needed. (Enabled by `OVRManager.controllerDrivenHandPosesType =
ConformingToController`; if you build a new rig, replicate that.)

This scene supports **hands and eye tracking**. To use them, enable them on the **`OVRCameraRig`** (`OVRManager` inspector → **Quest
Features**): **Hand Tracking Support** = *Controllers and Hands* and **Eye Tracking
Support** = *Supported*.

Each modality needs its OpenXR interaction-profile feature enabled in **Project
Settings → XR Plug-in Management → OpenXR (Android)**: **Eye Gaze Interaction** for
gaze+pinch, **Hand Interaction Profile** (+ **Hand Tracking**) for the hand ray, and the
**Oculus Touch Controller Profile** for controllers. A modality silently no-ops if its
profile is off (e.g. remove Eye Gaze Interaction and gaze+pinch stops; the hand ray then
becomes the hand driver).

- **Gaze + pinch** (if available, takes precedence over hand-ray aiming). Gaze aims the
  pointer; a hand pinch selects. Use `openxr_gaze_and_pinch` — **pass `gaze_position`
  in the SAME `base_space` you specify** (mixing `local` with the default
  `local_floor` misses). To **drag** (e.g. the Scale slider): gaze the handle, pinch,
  then move your **hand** — after pinch the pointer follows the hand, not the eyes, so
  gaze may look away. `gaze_and_pinch` activates gaze+hands mode (suppresses a held
  controller), so it also works while controllers are held.
- **Controllers** — the ray rides the **grip pose, NOT the OpenXR aim pose**. Here it's
  the `OVRControllerPrefab` transform's forward
  (`OVRCameraRig/…/RightControllerAnchor/OVRControllerPrefab`, ~60° below grip-forward;
  reticle `…/OVRControllerPrefab/OVRRayHelper/Cursor`, which deactivates off-surface). Aim it with the generic
  measure-and-correct grip method — **"Aiming a Controller Ray at UI Elements" in
  hz-meta-xr-operator-coordinates** (read the ray-origin forward → rotate the grip →
  validate the reticle) — then pull `Trigger`.
- **Hand ray** — fallback far-pointer only when no controller is in hand **and** eye
  gaze is unavailable (`HandRayFallbackController`). Uses the hand's pointer pose (FB
  aim, no grip offset): `openxr_hand_gesture(aim, target)` then `openxr_hand_gesture(pinch)`.
  When simulating hands over held controllers, inject **both** hands — replacing only one
  leaves the other controller active and keeps the scene in controller mode.

## Right panel notes

- **Slider (Scale)** needs a **drag**, not a tap (see the gaze+pinch drag above; with a
  controller, hold `Trigger` and sweep).
- **Dropdown (Shape)**: click to open, then click an option; confirm
  `Environment/MeshObject` changes shape. When open, target the runtime-created option
  list, not the disabled `Template`.

## Intentional seeded bugs (detect & fix)

At least one on the **left** (a color button that never recolors the cube) and one on
the **right** (a dropdown option that doesn't change the shape).

## Setup

Needs Meta XR Operator running (the `openxr_*` MCP tools). If unavailable, see Setup in
**hz-meta-xr-operator**.
