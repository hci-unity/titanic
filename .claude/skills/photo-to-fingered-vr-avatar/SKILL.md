---
name: photo-to-fingered-vr-avatar
description: Use when turning a reference photo (e.g. a movie character) into a full-body, finger-articulated, Unity-Humanoid avatar for the Titanic VR mirror/self-view system — building or replacing a mirror character like Jack or Rose with working fingers.
---

# Photo → Fingered VR Avatar (Meshy → Blender → AccuRIG → Unity)

## Overview

Turn one reference photo into a rigged, textured, **finger-boned** Unity Humanoid wired into this project's mirror system (`MirrorAvatars` / `InputModeRouter` / `AvatarRigDriver` / `MirrorCharacterSwitcher`). Core insight: **Meshy gives a great mesh but no finger bones; AccuRIG adds finger bones; Blender bridges them; Unity Humanoid maps them.** Each tool has one job — don't ask a tool to do another's.

**NOT fully autonomous.** Two steps are manual hand-offs you cannot do yourself — **STOP and hand off to the user at Step 4 (AccuRIG desktop app) and Step 8 (in-headset calibration)**, then resume. Also: Meshy calls cost credits — present the cost and wait for the user's OK before any paid generation (Meshy MCP rule). Name outputs `<Name>V2.*` (e.g. `RoseV2.fbx`) and keep the old asset as a fallback, mirroring the JackV2 precedent.

## The non-negotiable rule for fingers

**Finger bones are only possible if the mesh has SEPARATED finger geometry (real gaps between fingers).** Fused mitten hands can never be fingered. You get separated fingers from Meshy ONLY with a **full-body A-pose photo where the hands are open**. Everything else flows from this.

## Pipeline

1. **Source image (on disk).** Need a full-body, roughly A-pose shot with the face clear. A chest-up photo → bust-on-a-pedestal (no legs). If only a face photo exists, get a real full-body still of the character; crop tight around the body with PIL and (optionally) upscale. Save as a file — Meshy MCP needs a disk path.
2. **`meshy_image_to_3d`** (meshy-6/`latest`, ~20cr): `pose_mode:"a-pose"`, `should_texture:true`, `symmetry_mode:"auto"`, `target_formats:["fbx","glb"]`. Download GLB via the EXACT signed URL (curl). Inspect hands in Blender top-down — confirm **separated fingers**.
3. **Decimate in Blender** (NOT Meshy): import GLB, `parent_clear(KEEP_TRANSFORM)` + apply transforms, add a **Decimate (Collapse)** modifier with `ratio` **computed to land at ~230k tris** (`ratio = 230000 / current_tris`; 0.62 was Jack-specific — a different mesh/garment has a different raw count, so compute it). Verify hands stay clean (screenshot). Export FBX with `embed_textures=True`. Also save the base-color image separately (you'll likely need it — see step 6).
4. **AccuRIG** (free Reallusion desktop app, user-run): import the FBX/OBJ (NOT glb). Generate skeleton (auto), then **Rig Hand** — open hands → fingers auto-detect. Calibrate. Export **FBX, Target Application = Unity, Embed Texture ON**. Save into `Assets/Game/Characters/<Name>/`.
5. **Unity import**: set `ModelImporter.animationType = Human`. Auto-maps ~29/30 fingers; hand-map any missing via `humanDescription.human` (e.g. "Right Little Distal" → `CC_Base_R_Pinky3`). Confirm avatar `isValid && isHuman`.
6. **Material**: AccuRIG's embedded texture usually does NOT survive. Re-extract the base-color PNG from the Meshy GLB in Blender (`image.save()`), create a `Universal Render Pipeline/Lit` material with it as `_BaseMap`, and **remap** it onto the FBX material slot via `importer.AddRemap(...)`.
7. **Mirror integration** (match an existing character exactly): instantiate under `MirrorAvatars`, **layer 8** (recursive), localPos 0, **height-match scale via `renderer.bounds` ratio** to an existing avatar (~1.9m). Add `Animator` (`StarterAssetsThirdPerson` controller, rootMotion off), `AvatarRigDriver` (poseSource = the shared `InputModeRouter`, copy structural fields, **zero the head/wrist offsets**), `LocomotionAnimatorDriver`, `AvatarAnimationEvents`. Append to `MirrorCharacterSwitcher.characters`.
8. **Calibrate in-headset** (flashlight-pose) to bake `AvatarRigDriver` head/wrist offsets — the new `CC_Base_` rig orientation differs from old rigs, so positions track but rotations are wrong until calibrated.

## Critical gotchas (hard-won — each cost real time)

| Symptom                                                  | Cause / Fix                                                                                                                                                                                                    |
| -------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Alien spiky "webbing" hands                              | **Meshy remesh shreds hands.** Never Meshy-remesh; decimate in Blender (collapse).                                                                                                                             |
| AccuRIG Calibrate crashes the PC                         | Mesh >300k faces (RAM). Decimate to ~230k. 374k crashes; 157k = AccuRIG mis-detects fingers; **~230k is the sweet spot**.                                                                                      |
| Avatar looks like an exploded "winged spider" in Blender | A **Unity-targeted FBX always looks broken in Blender** (bind-pose mis-read). It's CORRECT in Unity. Don't re-export as Blender target.                                                                        |
| Avatar imports grey / untextured                         | Embedded texture didn't survive AccuRIG; re-extract albedo from the Meshy GLB + build URP material + remap (step 6).                                                                                           |
| Avatar ~6× too big                                       | CC rig bakes a different unit scale than Meshy rigs. Height-match with `renderer.bounds` ratio, NOT `sharedMesh.bounds * lossyScale`.                                                                          |
| Mirror avatar animates but won't track / hands dead      | A pose-source GameObject is **disabled** (`InputModeRouter` and/or `HandTrackingPoseSource` were left inactive as uncommitted WIP). Enable them. `AvatarRigDriver` needs `IK Pass` ON the animator Base Layer. |
| Fingers don't follow the user's fingers                  | EXPECTED until the finger-driving feature is built — adding finger BONES ≠ driving them. `AvatarRigDriver` only does head + 2 wrists.                                                                          |

## Quick reference

- Meshy: ~20cr image-to-3d, ~5cr rig/remesh, ~20cr multi-image. MCP is a **different account** than the user's browser and returns only 3D URLs (never 2D images) — image inputs must be disk files.
- Unity MCP `RunCommand`: keep scripts small; use `FindFirstObjectByType<T>(FindObjectsInactive.Include)` + direct typed field access (avoid reflection `GetField` and LINQ over a scene with missing scripts); `AnimatorControllerLayer.iKPass` (capital K).
- Don't commit Meshy/Blender scratch files (`_meshy_jack/`, `*_for_accurig.*`, ref PNGs).

## Common mistakes

- Trying to bone fused-mitten hands (re-generate from an open-hand A-pose photo instead).
- Judging the model in Blender after a Unity-targeted export (judge it in Unity).
- Skipping in-headset calibration and assuming the rig is broken (positions track, rotations need calibration).

See project memory `titanic-jack-fullbody-regen` for the worked Jack example, and `titanic-avatar-calibration-procedure` for the flashlight calibration recipe.
