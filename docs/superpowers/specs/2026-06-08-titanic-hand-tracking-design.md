# Titanic VR — Hand Tracking Support (controllers OR bare hands)

**Date:** 2026-06-08
**Status:** Design approved, ready for implementation plan
**Related:** [VR conversion](2026-05-29-titanic-vr-quest3-conversion-design.md) ·
[Jack/Rose characters](2026-05-30-titanic-jack-rose-characters-design.md) ·
[Head-turn / torso-follow](2026-06-08-titanic-head-turn-torso-follow-design.md)

## Summary

Let testers experience the Titanic mirror scene **without holding the Touch controllers**. Quest 3
hand tracking drives the mirror avatar's hands; the player can switch characters and exit using a
world-space poke panel. Controllers remain fully supported, and the experience **auto-switches**
between the two input modes (set the controllers down → hands take over; pick them up → controllers
take over). Target platform is unchanged: **Quest 3 PCVR over wireless Air Link (OpenXR + XRI)**.

## Gating spike — PASSED (2026-06-08)

The one risk that could have killed the feature was whether hand tracking streams reliably over
**Air Link**. It does — verified in Play-in-Editor with a throwaway joint-sphere visualizer
(`HandTrackingSpike.cs` + a scene object), cyan spheres tracked the user's bare hands. Findings:

- `com.unity.xr.hands` **1.8.0** installed; OpenXR **Hand Tracking** + **Meta Hand Tracking Aim**
  features enabled for the **Standalone** (Windows PCVR) target.
- No PC-side "Beta → hand tracking over Air Link" toggle is needed on current Meta Quest Link —
  headset-side hand tracking ON + Link as the active OpenXR runtime is sufficient.
- Hands do **not** work in the Link home/void menu — only inside an OpenXR app that requests the
  extension. That is normal.
- No full build is required to test hand tracking; Play-in-Editor over Air Link is enough.

**The spike artifacts (`HandTrackingSpike.cs` and its `HandTrackingSpike` GameObject in
`GrandStaircase.unity`) are throwaway and MUST be deleted as part of this feature's cleanup.**

## Goals

- Mirror avatar hands follow the player's **tracked hands** when controllers are not held.
- **Auto-switch** between controller mode and hands mode, both directions, with no manual UI.
- **Switch character** works hands-free.
- **Exit** is possible three ways: operator keyboard (**ESC**), controller (**Y**, unchanged), and a
  **guarded poke button** in hands mode.
- Controllers keep working exactly as today; nothing about the current experience regresses.

## Non-goals (YAGNI)

- **No synthetic locomotion in hands mode.** Decided: physical walking only. With no thumbstick the
  existing move provider and walk-animation driver simply read zero and do nothing — the avatar body
  already follows the head horizontally, so it moves when the player physically steps. (Optional polish
  noted below; not in scope unless requested.)
- **No finger articulation on the mirror avatar.** The Meshy rigs have no finger bones, so the
  reflected hand stays a mitt in both modes — same as today.
- **No bare-hand turning gesture.** Turning is physical-pivot (already shipped); unchanged.
- **No per-character hand-tracking calibration.** See Calibration — one rig-independent constant.

## Architecture

The feature is built on the existing `IPoseSource` seam. `AvatarRigDriver` is **not modified
internally**; it just points at a new router instead of the controller source.

```
                          ┌─ ThreePointIKPoseSource     (HMD + 2 controllers)   ← exists, unchanged
AvatarRigDriver ──► InputModeRouter ─┤
  (one per character)   (IPoseSource)└─ HandTrackingPoseSource (HMD + XR Hands wrist joints)  ← NEW
```

### Components

**`HandTrackingPoseSource` (new, `IPoseSource`)**

- _Does:_ Writes the head/hand IK targets each frame. Head from the HMD (identical to
  `ThreePointIKPoseSource`). Each hand from the XR Hands **wrist joint** (`XRHandJointID.Wrist`),
  converted from XR-Origin space to world space.
- _Convention fix:_ Applies a constant `wristRotationOffsetEuler` (per hand) that maps the OpenXR
  tracked-wrist orientation onto the **same convention the Touch controller grip produces**. This is
  what lets the per-character offsets downstream stay unchanged (see Calibration). A
  `handPositionOffset` field handles the wrist-joint→hand-bone position nudge.
- _Untracked hand:_ hold the last valid pose (no snapping to origin); report tracked-state for the
  router.
- _Depends on:_ `XRHandSubsystem` (acquired like the spike did), the XR Origin transform, the HMD
  transform.

**`InputModeRouter` (new, `IPoseSource`)**

- _Does:_ Holds references to both real pose sources. Each frame, decides the active mode and
  forwards `UpdateTargets` to the active source. Exposes `CurrentMode` (enum: Controllers / Hands)
  and a change event for any consumer that cares.
- _Switch rule:_ any hand `isTracked` ⇒ Hands mode; otherwise Controllers mode. (Meta's runtime stops
  hand tracking the moment a controller is grabbed, so this is a clean, single signal.) A short
  **debounce** (≈0.25–0.5 s of a stable signal) prevents flicker during the hand-off. If neither input
  is tracked momentarily, keep the last mode.
- _IsActive:_ true if either source is active.
- _Why a router and not mutating `AvatarRigDriver.poseSourceBehaviour`:_ the driver caches its pose
  source in `Awake`; a router keeps the switch logic in one small, unit-testable place and leaves the
  driver untouched. Every character's `AvatarRigDriver` is re-pointed at the shared router (one-time
  scene rewire, same pattern as the shared `ThreePointIKPoseSource` today).

**Poke panel (new scene content + small script)**

- A world-space panel near the mirror with **"Next Character"** and a guarded **"Exit"** button, built
  on XRI poke interactables. Poke works identically for a controller and a tracked **index fingertip**,
  so it is one UI for both modes.
- "Next Character" → `MirrorCharacterSwitcher.Next()`.
- "Exit" → guarded by **poke-and-hold** (≈1.5 s with a visible fill) → `QuitOnButton.Quit()`. The hold
  guard prevents an accidental brush from ending a supervised test. Placed apart from "Next Character".
- Requires poke interactors on **both** the controllers and the hand fingertips. The XRI + XR Hands
  integration supplies the fingertip poke interactor; controllers already have one or get one added.

**`QuitOnButton` (extended)**

- Add an operator **keyboard ESC** binding alongside the existing controller-Y binding. Both remain
  active. (The poke "Exit" calls the same `Quit()`.)

**Existing scripts — unchanged, still enabled in both modes**

- `MirrorCharacterSwitcher` (A button): harmless in hands mode (no button event); the poke panel is the
  hands path. Kept as a controller shortcut.
- `LocomotionAnimatorDriver` + XRI move provider: read the (absent) thumbstick as zero in hands mode →
  no movement, walk clip stays idle. No change.
- `AvatarRigDriver`: unchanged; per-character head/wrist offsets keep working in both modes.

## Data flow (per frame)

1. `AvatarRigDriver.OnAnimatorIK` / `LateUpdate` calls `router.UpdateTargets(head, left, right)`.
2. `InputModeRouter` picks the active source (debounced) and delegates.
3. Active source writes world-space head + hand target transforms.
4. `AvatarRigDriver` applies them through Unity humanoid IK (positions) + direct bone writes
   (head + wrist rotation) using the **existing** per-character offsets — identical for both modes
   because `HandTrackingPoseSource` already normalized its wrist convention.

## Calibration (the one simplification to confirm in practice)

The orientation difference between the **OpenXR tracked-wrist** pose and the **Touch controller grip**
pose is a **single constant in tracking space — independent of which avatar rig is loaded**. So:

- Calibrate **one** `wristRotationOffsetEuler` per hand on `HandTrackingPoseSource`, once, in-headset,
  using the existing flashlight-pose method (see the `titanic-avatar-calibration-procedure` note).
- After that, `HandTrackingPoseSource` output matches `ThreePointIKPoseSource` output, so **every
  character's existing `leftHandOffsetEuler`/`rightHandOffsetEuler` keeps working with no
  re-calibration.**

If in-headset testing shows the constant-delta assumption is imperfect for a given rig, the fallback is
a small per-rig hand-tracking offset — but we start from the single-constant approach.

## Error handling & edge cases

- **Hand leaves the camera frustum:** that hand reports untracked → freeze its last pose; router stays
  in Hands mode (the other signals/debounce keep it stable). No snap to origin.
- **Both hands and controllers untracked briefly:** router keeps the last mode.
- **Subsystem not yet running at scene start:** sources report `IsActive=false`; `AvatarRigDriver`
  already no-ops until `Ready`. Acquire the subsystem lazily (as the spike does).
- **Accidental poke Exit:** mitigated by poke-and-hold + placement.
- **Mode flicker during hand-off:** debounce window.

## Testing

- **Edit-mode unit tests** (no headset) for the pure logic: `InputModeRouter` mode-selection +
  debounce (feed synthetic tracked-state sequences, assert mode transitions and that
  `UpdateTargets` delegates to the expected source); poke-and-hold timer reaching threshold.
- **In-headset checks** (Play-in-Editor over Air Link): hands drive the mirror avatar's hands; set
  controllers down/pick up and confirm clean auto-switch both directions; poke "Next Character"
  cycles Default/Jack/Rose; poke-and-hold "Exit" quits; ESC quits; Y still quits with controllers;
  controller mode unchanged from today.

## Rollout / cleanup

- **Delete the spike artifacts** (`HandTrackingSpike.cs` + the `HandTrackingSpike` object in
  `GrandStaircase.unity`) once `HandTrackingPoseSource` is in.
- One-time scene rewire: point each character's `AvatarRigDriver` at the shared `InputModeRouter`.
- Add the poke panel to `GrandStaircase.unity` near the mirror.
- Verify the standalone build (mirror shader already in Always-Included; confirm hand-tracking works in
  a real build, not just Play-in-Editor, before user testing) — see `titanic-build-gotchas`.

## Optional polish (out of scope unless requested)

- Drive the mirror's **walk animation from HMD horizontal speed** in hands mode so the legs animate when
  the player physically walks (derive speed from HMD displacement, not capsule velocity — the rig
  repositions the capsule under the HMD, so capsule velocity is unreliable; see
  `LocomotionAnimatorDriver` notes).
