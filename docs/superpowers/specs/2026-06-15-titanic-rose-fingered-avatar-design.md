# RoseV2 — Fingered Mirror/Self-View Avatar (Step 4)

**Date:** 2026-06-15
**Status:** Design approved, awaiting reference photo to start P1.
**Predecessor:** This is a re-run of the proven JackV2 pipeline. See
`docs/superpowers/specs/2026-06-15-titanic-finger-articulation-design.md` and the
`photo-to-fingered-vr-avatar` skill. Background: the original Jack/Rose mission spec
`docs/superpowers/specs/2026-05-30-titanic-jack-rose-characters-design.md`.

## Goal

Replace the shipped Rose avatar — currently an old Meshy **24-bone mitt rig with no
finger bones** — with **RoseV2**, a fingered Unity-Humanoid avatar (30 finger bones) that
supports full finger articulation via the existing generic `FingerPoseDriver`, on par with
JackV2. RoseV2 serves both the stereo mirror and (for free) the first-person self-view.

## Decisions (locked)

- **Source:** the user supplies a full-body **A-pose, open separated-hands, plain-background**
  photo (same convention as Jack's reference). The asset is generated from that photo via Meshy.
- **Old Rose is discarded** — no continuity/likeness constraint with the previous Blue Velvet
  gown. Old Rose's GameObject, FBX, material, and source textures are removed once RoseV2 is
  validated (same cleanup we did for old Jack in commit `4fbb593`).
- **Style:** stylized, not photoreal/actor-likeness (uncanny-valley + IP avoidance), consistent
  with JackV2.
- **No new code.** `FingerPoseDriver`, `AvatarRigDriver`, `LocomotionAnimatorDriver`,
  `AvatarAnimationEvents`, and `MirrorCharacterSwitcher` are all generic and unchanged. Step 4 is
  asset generation + scene wiring + calibration only.

## Pipeline (P1 → P3)

### P1 — Generate the fingered asset

1. User-supplied photo → `meshy_image_to_3d` (a-pose mode, separated finger geometry — fused
   hands cannot be fingered). Crop tight with PIL first.
2. **Never Meshy-remesh** (shreds hands). Use **Blender decimate-collapse to ~230k tris**
   (AccuRIG's <300k face limit; 374k crashed the PC last time).
3. **AccuRIG** (free) rigs body + fingers from the open A-pose hands → export FBX **Target=Unity**.
4. AccuRIG's embedded texture won't survive → re-extract albedo from the glb in Blender, build a
   URP/Lit material, remap.
5. Unity: import `animationType=Human`; auto-maps ~29/30 fingers, hand-map the rest via
   `humanDescription`. Height-match by `renderer.bounds` ratio (CC rig unit scale differs).
   `optimizeGameObjects` MUST stay false (we write bone rotations in LateUpdate).

**Decision gate (inside P1):** as soon as the rough mesh exists, render it and review the
**dress/skirt** with the user _before_ investing in rigging. A continuous full-length skirt is a
risk for both (a) AccuRIG cleanly finding/weighting the legs and (b) the leg-driven StarterAssets
walk animation (legs clip through or tear a rigid skirt). Resolve to one of: calf-length/split
skirt (safest for our leg-driven locomotion), accept clipping, or skirt weighted to hips/legs
(highest fidelity, may need Blender cleanup). Lock the choice here.

### P2 — Calibrate head + wrist

- Use the **knuckle-span** method (NOT the Meshy `+Y/+Z` flashlight method — AccuRIG's idle thumb
  points nearly forward and makes `LookRotation(fingers, thumbDir)` degenerate, as burned on
  JackV2). Roll reference = `(IndexProximal − LittleProximal)`; `fingers = (MiddleProximal − Hand)`.
- Calibrate holding **controllers** (router → controller pose source) so the per-character offset
  matches the Touch-grip convention. Bake `headOffsetEuler`, `leftHandOffsetEuler`,
  `rightHandOffsetEuler`, `handRotationWeight=1` onto RoseV2's `AvatarRigDriver`.

### P3 — Wire into the scene (DEFERRED — see sequencing)

- Add RoseV2 under `XR Origin (XR Rig)/MirrorAvatars`, layer 8, localScale height-matched,
  Animator = `StarterAssetsThirdPerson` controller + own avatar (rootMotion off, Base Layer
  iKPass ON, `cullingMode = AlwaysAnimate`).
- Components: `AvatarRigDriver` (poseSource = `InputModeRouter`, baked offsets),
  `LocomotionAnimatorDriver`, `AvatarAnimationEvents`, and `FingerPoseDriver` (wire `router` +
  `handSource`, `fingerWeight=1`, `smoothing=0`).
- Register RoseV2 in `MirrorCharacterSwitcher.characters`; remove old Rose from the list and
  delete its assets. Self-view (`SelfViewManager`, Step 3) reads the active avatar from the
  switcher, so RoseV2 gets first-person self-view for free with zero extra work.

## Sequencing / coordination (important)

A second agent is concurrently doing **Step 3 (first-person self-view)**, which has
`GrandStaircase.unity` modified and uncommitted. **P3 edits the same scene file** — two agents
saving it concurrently will clobber each other.

**Plan:** execute **P1 and P2 now** (they produce only new asset files — fbx, materials,
textures — safe in parallel). **Hold P3's scene wiring until Step 3 is committed/settled.**

## Cost

~100+ Meshy credits, comparable to JackV2. Per the standing rule, every paid Meshy call is quoted
and confirmed with the user before spending; free tools (balance, status, download) run freely.

## Out of scope

- Any change to `FingerPoseDriver` / `AvatarRigDriver` / mirror / self-view code (all generic).
- Self-view tuning for Rose (inherited automatically from the shared `SelfViewManager`).
