# Finger Articulation for Mirror Avatars — Design

**Date:** 2026-06-15
**Project:** Titanic VR (Meta Quest 3 / PCVR) — mirror avatar system
**Status:** Approved design, ready for implementation plan
**Related:** `titanic-jack-fullbody-regen`, `titanic-hand-tracking-followup`, `titanic-vr-conversion` memories

## Goal

Drive the mirror avatar's **finger bones** from Quest hand-tracking joints so the reflected
hand curls, spreads, and gestures with the player's real hand. **Full articulation** —
every finger joint follows (curl **and** splay), thumb included. **Hand-tracking only**
(controllers cannot sense fingers). First target is **JackV2** (the fingered DiCaprio rig,
30/30 Humanoid finger bones); the component must be generic so **Rose** (and any future
fingered avatar) gets fingers by simply adding the component.

### Non-goals

- No finger input in controller mode (Touch controllers aren't read for finger pose).
- No finger _roll_ fidelity (fingers are treated as hinges — see Approach rationale).

### Platform note (not a limitation we design around)

`FingerPoseDriver` works **anywhere XR Hands delivers joint data** — it reads the same
subsystem the existing hand-tracking source uses, with no Editor-only code paths. Hand tracking
has worked in the **distributed standalone `.exe`** during university user testing, so finger
articulation is expected to work there too, as long as it works in Unity Play. (An earlier
`titanic-hand-tracking-followup` memory note claimed hand tracking was Editor-Play-only over Air
Link; that was contradicted by the university `.exe` sessions and is superseded.) Bottom line:
get it working in Unity Play and it should carry to the build wherever the runtime grants hand
tracking.

## Context (current system)

- `HandTrackingPoseSource` (`Assets/Game/Scripts/VR/`) is the **sole** XR Hands reader. It
  implements `IPoseSource` (head from HMD, hands from the tracked **wrist** joint) and is the
  single owner of tracked-hand data (exposes `LeftTracked`/`RightTracked`, index-tip world
  positions). XR Hands joint poses are in **Camera-Offset** space; it already converts wrist +
  index-tip to world via its `trackingSpace` transform. Package: `com.unity.xr.hands` 1.8.0.
- `InputModeRouter` (`IPoseSource`) picks controller-vs-hand mode each frame from Unity's
  standard `XRInputModalityManager.currentInputMode` and exposes `CurrentMode`
  (`Controllers`/`Hands`) + a `ModeChanged` event. Every `AvatarRigDriver` points at the router.
- `AvatarRigDriver` (per character) drives **head + wrist** rotation by writing the bone's
  **world rotation directly in `LateUpdate`** (after the animator/IK pass) through a calibrated
  constant offset. Hand **position** is solved by Unity humanoid IK in `OnAnimatorIK`. It does
  **not** touch finger bones.
- JackV2: all 30 finger bones mapped (Humanoid Proximal/Intermediate/Distal per finger incl.
  thumb). Distal bones are **leaf** bones (no tip child transform). JackV2 head + wrist offsets
  were calibrated and baked 2026-06-15.

## Approach (chosen: A — world-space directional aiming)

For each humanoid finger bone, rotate it so its bone segment (this joint → its child) points
along the **same world-space direction** as the matching XR Hand segment. This is the exact
idiom already proven for the wrist and head (write bone world rotation in `LateUpdate`),
extended down the finger chain.

**Why A over the alternatives:**

- **Zero per-rig calibration.** It copies _directions_, not orientations, so it is immune to
  the XR-vs-Humanoid rest-pose and axis-convention mismatch that required the flashlight wrist
  calibration. (That mismatch already cost two calibration passes on JackV2's wrist.)
- **Proportion-independent.** Directions are scale-free, so the avatar's finger lengths
  differing from the user's is a non-issue (unlike the hand-_position_ reach problem).
- **Captures curl + splay** because it is a 3D aim, not a 1D flexion angle.
- **Reuses a pattern already working great** in this codebase.

**Cost:** aiming constrains 2 DOF (the bone's pointing direction), leaving _roll_ free — fine
for hinged fingers, where roll carries no meaningful information. Rejected alternatives:
**B (local-rotation retarget with rest-offset calibration)** — fully correct incl. roll but
reintroduces per-rig calibration fragility (esp. the thumb); **C (curl-angle scalar)** — simple
but loses splay, so not "full articulation."

## Architecture

Two components, matching the existing `IPoseSource` seam:

### 1. `HandTrackingPoseSource` — extend to expose finger joints

Add finger joint world poses to the existing sole XR Hands reader. Each `Update()`, alongside
the wrist it already reads, capture the needed joints into per-hand world-space storage and
expose a read-only accessor:

```
bool TryGetJointWorld(Handedness handedness, XRHandJointID id, out Pose worldPose)
```

(or an equivalent per-hand structure). World conversion uses the same `trackingSpace` transform
already used for the wrist. Validity follows the same rule as the wrist: a joint is available
only when its `TryGetPose` succeeds. Nothing else reads the subsystem.

### 2. `FingerPoseDriver` — new per-avatar component (opt-in)

A `MonoBehaviour` placed on each **fingered** avatar (JackV2 now, Rose later). Default/legacy
mitt characters simply don't get it.

- **References:** `InputModeRouter` (mode gate), `HandTrackingPoseSource` (joint data).
  Auto-resolves the `Animator` via `GetComponentInChildren`.
- **Execution order:** `[DefaultExecutionOrder(N)]` with N greater than `AvatarRigDriver`'s
  (which is 0), so `FingerPoseDriver.LateUpdate` runs **after** the wrist world rotation is
  written. Fingers are children of the hand bone, so the hand must be oriented first.
- **Runs only when** `router.CurrentMode == Hands` **and** the matching hand is tracked.
  Otherwise it writes nothing (fingers keep the animator's idle pose).
- **Tunables:** `fingerWeight` (0–1, blends the whole effect) and an optional light
  `smoothing` (Slerp factor toward the target each frame; small default) to tame tracking jitter.

## Joint mapping (XR Hands → Humanoid)

Each humanoid bone is aimed along the matching XR Hand segment:

| Humanoid bone                               | XR segment (this joint → child joint) |
| ------------------------------------------- | ------------------------------------- |
| {Index,Middle,Ring,Little} **Proximal**     | Proximal → Intermediate               |
| {Index,Middle,Ring,Little} **Intermediate** | Intermediate → Distal                 |
| {Index,Middle,Ring,Little} **Distal**       | Distal → Tip                          |
| **Thumb Proximal**                          | ThumbMetacarpal → ThumbProximal       |
| **Thumb Intermediate**                      | ThumbProximal → ThumbDistal           |
| **Thumb Distal**                            | ThumbDistal → ThumbTip                |

The four fingers' XR **Metacarpal** joints are unused (they map into the palm, with no
corresponding humanoid bone). The thumb has no XR _Intermediate_ joint, hence the shifted
mapping above.

## Core aiming algorithm

Per finger, processed **root → tip** so each bone's child positions reflect the parent's new
orientation before the child is aimed:

```
currentDir = (childBone.position - bone.position).normalized
targetDir  = (xrChildJoint.worldPos - xrThisJoint.worldPos).normalized
bone.rotation = Quaternion.FromToRotation(currentDir, targetDir) * bone.rotation
```

- `childBone` is the next humanoid bone in the chain (proximal→intermediate→distal).
- **Distal leaf** (no child transform): use the bone's own rest forward axis — the
  intermediate→distal world direction — as `currentDir`, aimed at the XR distal→tip segment.
- Guard against degenerate/near-zero segments (skip the bone if either direction is ~0).
- With `smoothing` > 0, slerp the bone toward the computed rotation instead of snapping.
- With `fingerWeight` < 1, slerp from the animator's pose toward the computed rotation.

The left avatar hand follows the left tracked hand and right→right (the mirror does the visual
flip; this matches the existing wrist/hand mapping).

## Behavior & fallback

| Situation                  | Finger behavior                                                      |
| -------------------------- | -------------------------------------------------------------------- |
| Controller mode            | Driver idle; fingers rest in StarterAssets idle pose (relaxed hand). |
| Hands mode, hand tracked   | Fingers driven from XR joints.                                       |
| Hands mode, hand drops out | **Freeze last good pose** (consistent with the wrist's freeze).      |

## Reusability

`FingerPoseDriver` is generic for any Humanoid with mapped finger bones. **Step 4 (Rose) is
just: add the component to the Rose avatar** — no new code. The shared finger-joint data lives
on the single `HandTrackingPoseSource`, so adding characters costs nothing extra there.

## Verification

Per the project's established model (no test harness in the project):

- **Pure-static logic checks** via Unity-MCP `RunCommand`: the aiming math on synthetic inputs
  (a known segment direction produces the expected bone rotation) and the joint-mapping table
  (every humanoid finger bone resolves to a valid XR segment).
- **In-headset, Editor Play over Link:** make a fist, open hand, spread fingers, thumbs-up;
  verify against rendered **layer-8** hand close-ups (the proven temp-camera capture method) and
  a live mirror check. Confirm controller mode still shows a clean relaxed hand and that the
  controller→hand switch leaves fingers correct.

## Risks / watch-items

- **Tracking jitter** on fast or partially-occluded hands → mitigated by optional smoothing and
  `fingerWeight`; tune in headset.
- **Thumb** is the hardest joint (different XR joint structure, wide range of motion). The
  direction-aim method needs no special-casing beyond the shifted mapping, but verify the thumb
  specifically in headset.
- **Execution-order regressions** if `AvatarRigDriver`'s ordering changes later — documented via
  the `[DefaultExecutionOrder]` attribute and a code comment.
- **Build parity:** validate in Unity Play first; then confirm in a standalone `.exe` (hand
  tracking has worked there in university testing). If a build ever lacks hand tracking, that's a
  runtime/connection issue, not a code defect — but verify the build to be sure.
