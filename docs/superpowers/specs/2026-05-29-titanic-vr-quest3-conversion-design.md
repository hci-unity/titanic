# Titanic Grand Staircase — VR Conversion (Meta Quest 3 / PCVR) Design

> **Status:** Design approved 2026-05-29. Ready for implementation planning.
> **Predecessor:** `docs/superpowers/specs/2026-05-17-titanic-grand-staircase-design.md` (the desktop first-person scene this builds on).

## Goal

Convert the existing desktop first-person Titanic grand-staircase scene into a **room-scale VR experience for the Meta Quest 3**, run as **PCVR** (the game runs on the lab PC; the Quest 3 is the display via Link/Air Link). The headset drives head look, the two Touch controllers drive the player's hands, and the player sees a **believable full-body reflection of themselves in the existing mirror** whose head and hands move with their own. The player moves with smooth stick locomotion, is warned by an in-world cage when they physically wander ~1 m from their start point, and can switch turn-comfort modes.

The scene, lighting, walls, and the real-time planar mirror from the predecessor project are **kept**; the desktop input/camera model is **replaced** by an XR rig.

## Context — current project state (verified 2026-05-29)

- Unity 6000.4.0f1, URP 17.4.0. Scene: `Assets/Scenes/GrandStaircase.unity`.
- **No XR tooling installed yet** — `Packages/manifest.json` has only the built-in `com.unity.modules.xr` / `com.unity.modules.vr` stubs. No XR Plugin Management, no OpenXR, no XR Interaction Toolkit, no Meta XR SDK, no Animation Rigging. This is a greenfield XR setup.
- Desktop locomotion to be replaced:
  - `Assets/Game/Scripts/FirstPersonController.cs` — CharacterController + keyboard movement (New Input System).
  - `Assets/Game/Scripts/FirstPersonCameraRig.cs` — mouse-look (yaw on body, pitch on camera).
- The mirror: `Assets/Game/Scripts/MirrorReflection.cs` (+ `MirrorReflection.shader`) renders a full reflection-camera pass every frame via `OnWillRenderObject`. The avatar (`PlayerArmature`-derived) is on **layer 8 (PlayerBody)**, culled from the player camera, visible to the reflection camera — this is what lets you see yourself in the mirror without seeing your own body in first person.

## Decisions (locked)

| Area                    | Decision                                                                                                                                                                                                                                                                            |
| ----------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Deployment**          | **PCVR** — runs on the lab PC, Quest 3 as display via Link/Air Link. Confirmed: lab connects headsets to (strong) PCs. PCVR is an accepted deliverable.                                                                                                                             |
| **SDK / provider**      | Unity **OpenXR + XR Interaction Toolkit (XRI)**. No Meta XR SDK. (The university course material also does **not** use Meta's SDK — it uses XR Plugin Management with the _Oculus_ provider; OpenXR is the modern equivalent and is concept-identical for what the course teaches.) |
| **Move**                | Smooth continuous locomotion (left stick), head-relative. **No teleport. No jump.**                                                                                                                                                                                                 |
| **Turn**                | Toggleable **snap ↔ smooth/continuous** turn (right stick), exposed as a comfort setting.                                                                                                                                                                                           |
| **Boundary**            | **Warn-only** visual cage/grid wall fading in at ~1 m horizontal distance from the start/recenter point; fades out on return. **No position clamping.** Tuned to trigger _before_ the Quest's own Guardian.                                                                         |
| **Mirror avatar**       | **Full-body IK avatar**. Head + 2 hands directly tracked; torso/hips/legs inferred.                                                                                                                                                                                                 |
| **IK engine**           | Unity **Animation Rigging** (free) as the default. **FinalIK (VRIK)** is the documented upgrade path if the result looks uncanny.                                                                                                                                                   |
| **Avatar swappability** | Built around a **Humanoid rig** so future Jack/Rose characters drop in.                                                                                                                                                                                                             |
| **Avatar pose source**  | Driven through a **swappable `PoseSource` abstraction**: `3-point IK` now → `mocap` later.                                                                                                                                                                                          |
| **Mocap**               | **Future enhancement**, not this phase. The lab has a ceiling mocap system (OptiTrack/Vicon-style) that could later provide real full-body tracking; we leave an architectural seam for it.                                                                                         |

## Section 1 — Overall architecture

Replace the desktop input/camera model with an XR rig while keeping the scene, lighting, walls, and mirror intact.

- **`XR Origin` rig (XRI)** becomes the player: tracked HMD camera + two controller transforms. **Replaces `FirstPersonCameraRig.cs` entirely** — head look comes from the headset, not the mouse.
- **`FirstPersonController.cs` is replaced** by the XRI locomotion stack: `CharacterController` + `ContinuousMoveProvider` + a switchable turn provider. Collision with the existing ProBuilder walls still stops the player.
- **Humanoid avatar parented to the rig**, driven through a **`PoseSource` abstraction** (default: Animation Rigging 3-point IK). This avatar is what the mirror reflects. The existing **layer-8 culling** trick carries over (hidden from player eyes, visible to the reflection camera).
- **The mirror stays as-is**, with one required validation pass for VR stereo rendering (see Section 3, Risk 1).
- **Two new small systems:** the boundary warning and the comfort settings menu.
- **Old desktop scripts** are removed from the VR player (kept in the repo, not on the rig).

Each piece — locomotion, avatar-IK, boundary, comfort menu, mirror — is an independent, individually testable unit.

## Section 2 — Components

### 1. XR Rig & locomotion _(replaces both desktop scripts)_

`XR Origin` with tracked HMD camera + two controllers. `CharacterController` for wall collision. `ContinuousMoveProvider` (smooth move, left stick, head-relative). Turn provider switchable at runtime between `SnapTurnProvider` and `ContinuousTurnProvider` (right stick). No jump.

### 2. Avatar + IK _(the mirror's subject)_

Humanoid-rigged avatar parented to the rig. A **`PoseSource`** interface feeds the avatar rig; the default implementation is Animation Rigging driven by 3 points:

- **Hands** → two `TwoBoneIKConstraint`s targeting controller anchors, with **elbow-hint targets** so elbows bend naturally (matters under mirror scrutiny).
- **Head** → head bone follows the HMD; a spine/chest constraint leans the upper body to follow head tilt.
- **Hips/legs** → standing pose with light foot planting (the stick-locomotion stance keeps the body upright, the case where free IK reads best).

Built on the Humanoid abstraction (Jack/Rose swap in). Pose-source is swappable (mocap drops in later). Retains layer-8 culling so the avatar is mirror-only.

### 3. Mirror — stereo validation _(the one real risk)_

`MirrorReflection.cs` stays. Its per-frame `OnWillRenderObject` reflection must be verified under VR **stereo** rendering and fixed if it renders mono / single-eye. Strong PC GPU means quality and performance are not the concern — **correctness under stereo is.** Prove this early, before building the rest on top.

### 4. Boundary warning

A `RecenterAnchor` captures the play-space center (on start/recenter). A `BoundaryWarning` component measures the HMD's horizontal distance from it; past ~1 m it fades in a cage/grid wall + message and fades out on return. Warn-only, no clamp. Tuned to trigger before the Quest Guardian.

### 5. Comfort settings

A small **world-space UI panel** with a controller pointer ray, exposing the **turn-style toggle** (snap ↔ smooth) and turn speed/angle. This is the only UI interaction; hands do **not** grab world objects this phase (nothing to grab).

## Section 3 — Setup, risks, testing

### Setup & build pipeline (PCVR)

- Install **XR Plugin Management** → enable **OpenXR** on the **PC/Windows Standalone** tab (not Android) → enable the **Meta Quest** feature group + Touch-controller interaction profiles.
- Install **XR Interaction Toolkit** (+ Starter Assets samples) and the **Animation Rigging** package. Input System is already present.
- **Primary workflow:** press **Play in the Editor** with the Quest connected via Link/Air Link — **no Android APK**. Optionally produce a Windows `.exe`.
- **Course-divergence note:** the university doc targets Android _standalone_ (`Switch Platform → Android`, ASTC, `Build and Run`). Those steps do **not** apply to PCVR. If grading ever requires a standalone APK, the deployment decision (and the mirror-performance problem) reopens — confirmed not required for this phase.
- **Provider fallback:** if the instructor specifically requires the legacy _Oculus_ provider instead of OpenXR, it is a one-checkbox swap in XR Plugin Management; the XR Rig / controller / locomotion concepts are identical.

### Risks (most important first)

1. **Mirror under VR stereo (top risk).** `MirrorReflection.cs` uses `OnWillRenderObject` + `Camera.current`; under URP's XR rendering path it may render for only one eye or from a mono viewpoint. **Mitigation:** validate per-eye; render the reflection from each eye's position. **Fallbacks:** render mono from the head midpoint (acceptable at mirror viewing distance), or fall back to a Reflection Probe.
2. **IK uncanniness.** Mitigated by elbow-hint targets, a standing base pose, and the stick-locomotion stance. **Escape hatch:** FinalIK (VRIK).
3. **Comfort / nausea.** Snap-turn default; optional **comfort vignette** during smooth move/turn (cheap, standard) noted as a tuning lever.
4. **Layer-8 culling on the XR camera.** Verify the "hide avatar from player eyes, show to mirror" trick still behaves correctly on the stereo camera.

### Testing / success criteria

- Head turn → view turns **and** the reflected avatar's head turns.
- Controllers → avatar hands track **and** reflect.
- Smooth move works; snap↔smooth turn toggle works; walls block movement.
- Boundary cage fades in ~1 m from center, fades out on return, never clamps.
- **Mirror shows a correct full-body reflection in both eyes** at full quality.
- Comfortable framerate over Link; no critical nausea issues.

## Out of scope (future sessions)

Mocap integration (OptiTrack/Vicon co-location calibration); Jack/Rose character selection; hand grabbing of world objects; jump; passthrough / mixed reality; Android standalone build.

## Open items to confirm (non-blocking)

- Instructor sign-off that **PCVR is an acceptable deliverable** (course materials assume Android standalone). User indicated PCVR is accepted.
- Exact lab PC GPU specs (told "strong"; assumed sufficient for full-quality mirror).
- Whether the instructor requires the **Oculus provider** specifically (otherwise OpenXR).
