# Titanic Grand Staircase — First-Person Scene Design

**Date:** 2026-05-17
**Project:** `titanic` (Unity 6000.4.0f1, URP 17.4.0)
**Goal:** A first-person walkable scene set inside a Titanic-inspired grand staircase, with a working mirror that reflects the player character.
**Status:** ✅ COMPLETE (2026-05-29) — scene, lighting, first-person rig, and the real-time planar mirror are all done and visually verified. Mirror reflection matches the direct view.

---

## 1. Summary

Pull the Titanic grand staircase and a separate mirror model from Sketchfab via the Blender MCP, export them to Unity-friendly FBX, and assemble a new first-person scene around them. The player's character is the existing `PlayerArmature` (YBot humanoid) from the installed _Starter Assets — ThirdPerson | URP_ package, repurposed for first-person view. Walls enclose the staircase to hide the surrounding void, and warm interior lighting illuminates the space. A real-time planar mirror lets the player see their own character.

A future task (out of scope here) will swap the YBot body for Jack/Rose character meshes on the same rig.

---

## 2. External assets

| Asset                            | Sketchfab UID                      | Notes                                                |
| -------------------------------- | ---------------------------------- | ---------------------------------------------------- |
| Titanic-inspired Grand Staircase | `05290539521c4581a627a47f5d366857` | Static prop, ~10–12 m tall                           |
| Mirror                           | `a1c6daa19b184e6aa7f02ba68dd1d985` | Frame mesh; flat plane will receive a render texture |

The Sketchfab API key is configured inside the Blender MCP add-on preferences, so `mcp__blender-mcp__download_sketchfab_model` works without our code reading any key file.

---

## 3. Asset pipeline (Sketchfab → Blender → Unity)

For each asset:

1. **Download** with `mcp__blender-mcp__download_sketchfab_model(uid, target_size=...)`. Initial values: staircase `target_size=12.0` (Edwardian two-deck atrium), mirror `target_size=2.0`. These are starting points — re-measure `Renderer.bounds` in Unity after import and rescale the prefab transform if the actual mesh extent is off.
2. **Clean up in Blender**:
   - Delete stray Sketchfab decoratives (icospheres, default cameras, demo cubes, helper bezier curves).
   - Apply transforms (`bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)`).
   - Sanity-check `Object.dimensions` and `bpy.data.images` (packed textures).
3. **Export FBX** with these settings (locked-in defaults — these matched what worked in prior Unity-MCP projects):

   ```python
   bpy.ops.export_scene.fbx(
       filepath=...,
       mesh_smooth_type='EDGE',
       path_mode='COPY',
       embed_textures=True,
       axis_forward='-Z',
       axis_up='Y',
       bake_space_transform=True,
       add_leaf_bones=False,
       bake_anim=False,   # static props
   )
   ```

4. **Drop into Unity** under:
   - `Assets/Game/Models/GrandStaircase/GrandStaircase.fbx`
   - `Assets/Game/Models/Mirror/Mirror.fbx`

5. **Post-import in Unity** (via `mcp__unity-mcp__Unity_RunCommand` running C# in the editor):
   - `ModelImporter.ExtractTextures("Assets/Game/Models/<Name>/<Name>_Textures")` → reimport.
   - For each embedded material sub-asset, `AssetDatabase.ExtractAsset(mat, "Assets/Game/Materials/<MatName>.mat")` → write import settings → reimport.
   - **Do NOT** set `importer.materialLocation = ModelImporterMaterialLocation.External` — that property is obsolete in Unity 6 and spams warnings. `ExtractAsset` alone is enough.
   - If `_BaseMap` on any extracted material is empty (Unity's heuristic can drop maps), re-wire it manually from the corresponding extracted texture file.

6. **Verify** with `mcp__unity-mcp__Unity_Camera_Capture` or `Unity_SceneView_Capture2DScene` that bounds, color, and silhouette match the Sketchfab preview.

7. **Wrap in a prefab** at `Assets/Game/Prefabs/Staircase.prefab` and `Assets/Game/Prefabs/Mirror.prefab`.

---

## 4. Scene structure

Create a new scene **`Assets/Scenes/GrandStaircase.unity`** (leaves the existing `SampleScene` untouched). Hierarchy:

```
GrandStaircase (scene)
├── Environment
│   ├── Staircase            (prefab instance)
│   ├── Walls                (ProBuilder box room enclosing the staircase)
│   ├── Floor                (ProBuilder quad)
│   └── Ceiling              (ProBuilder quad)
├── Lighting
│   ├── DirectionalLight     (low intensity, warm tint, ambient direction)
│   ├── Chandelier_Center    (point light, warm 3200 K, high intensity)
│   └── WallSconce_1..4      (4 point lights along walls)
├── Player
│   └── FirstPersonRig       (PlayerArmature-derived, see §5)
└── Mirror
    └── Mirror               (prefab + MirrorReflection.cs + RenderTexture)
```

---

## 5. First-person rig (built from `PlayerArmature`)

We need a real humanoid body in the world (so the mirror has something to reflect) AND a first-person camera. The plan:

**Start from `Assets/StarterAssets/ThirdPersonController/Prefabs/PlayerArmature.prefab`** and copy it into `Assets/Game/Prefabs/FirstPersonRig.prefab`. Modifications (implementation-revised after testing):

1. **Camera placement** — parent `CameraRig` to the **character root** (NOT the Head bone). This avoids the mismatch between Mecanim's head-bone local axes and the character's facing direction.
   - `CameraRig` local position: `(0, ~1.62, 0.10)` — eye height above root, slight forward offset.
   - `CameraRig` local rotation: identity (inherits character's facing).
   - `MainCamera` child: `nearClipPlane=0.30`, `farClipPlane=200`, `fieldOfView=75`, `AudioListener`, plus `UniversalAdditionalCameraData` (required for URP).

2. **Body hiding via layer-based culling** (revised — the originally-planned near-plane clipping wasn't reliable in scene-view rendering and didn't cleanly clip the YBot's body which extends ~0.23 m forward of the root):
   - Create a `PlayerBody` layer.
   - Set every `Renderer` under the rig to that layer.
   - Exclude `PlayerBody` from the player camera's `cullingMask`.
   - The mirror's reflection camera (Task 9) will include `PlayerBody`, so the player still sees themselves in the mirror.

3. **Controller behavior** (`ThirdPersonController.cs` from Starter Assets):
   - Movement, jump, gravity, `CharacterController`, and animator parameters all stay as-is.
   - Clear the `CinemachineCameraTarget` field via reflection so the body-yaw block early-outs. Mouse-X yaws the **character root** directly; mouse-Y pitches the **camera only** (clamped ±85°).
   - **Note:** the Starter Asset uses Unity's Input System for movement; mouse-look uses legacy `Input.GetAxisRaw`. The project's `Active Input Handling` must be set to "Both" (auto-applied during Task 8 implementation via serialized `ProjectSettings.asset` edit).

4. **Mirror visibility**: the mirror's reflection camera views the rig from outside. Its culling mask DOES include `PlayerBody`, so the reflection shows the complete character.

5. **Future Jack/Rose swap**: swap the `SkinnedMeshRenderer` mesh on the same rig — animator and bones stay identical.

---

## 6. Mirror — real-time planar reflection

A classic Unity planar-mirror pattern, implemented as a small `MirrorReflection.cs` on the mirror's reflective surface.

**Behavior:**

- On `OnWillRenderObject`, the script:
  1. Lazily creates one child `Camera` (`MirrorCam`) the first time it's needed; reuses it after.
  2. Computes the mirror plane in world space from the mirror transform.
  3. Builds a view matrix that reflects the rendering camera's position/orientation across that plane.
  4. Applies an oblique projection so the near plane lies on the mirror surface (avoids reflecting geometry behind the mirror).
  5. Renders `MirrorCam` into a `RenderTexture` (1024 × 1024, RGB, depth 16).
  6. Assigns the RenderTexture to the mirror material's `_BaseMap` (URP/Lit) or to a `_ReflectionTex` slot if we use a dedicated mirror shader.

**Quality / cost choices:**

- Resolution 1024 × 1024 — enough sharpness for a single mirror at typical viewing distance, cheap on URP.
- Single reflection per frame, no recursion (the mirror won't reflect itself).
- Culling Mask = everything except other mirrors (only one mirror in this scene, so effectively `Everything`).

**Rejected alternatives:**

- _Realtime Reflection Probe_ — lower fidelity for flat surfaces and wrong parallax unless the viewer stands at the probe.
- _Baked cubemap_ — wouldn't show the live player; defeats the goal.

---

## 7. Walls / floor / ceiling around the staircase

**Revised after import (2026-05-17):** the Sketchfab grand-staircase model already includes its **own walls, floor, ceiling, and glass dome** — it's a complete cutaway atrium, not a free-standing staircase. Original plan was a full ProBuilder room enclosing the staircase; that's no longer needed.

What we still need: **one closing wall on the cutaway side** (the -Z face of the imported model), so the player doesn't see out into the void when they turn around inside the atrium.

Implementation:

- Sample bounds in Unity, identify which cardinal direction is open by raycasting (in our case it was -Z).
- Place a single solid cube on that side, sized slightly larger than the atrium bounds (`size.x + 1`, `size.y + 1`, `0.2 m` thick), positioned just inside `b.min.z`.
- Material: a plain warm-wood URP/Lit (`ClosingWall_Wood.mat`, `_BaseColor` ≈ `(0.40, 0.22, 0.10)`, low smoothness, zero metallic). Do NOT reuse the staircase's UV-mapped material on a flat cube — its texture atlas (clock face, windows, marble tiles) tiles awkwardly across flat geometry.

ProBuilder is still installed but not used. We can revisit if we later need cut doorways or more complex environment geometry.

---

## 8. Lighting (URP)

Goal: _visibly lit_ (no pitch-black corners) with a warm Edwardian interior feel. Realtime only — no baking — to keep iteration fast.

| Light               | Type        | Mode     | Color                | Intensity (starting)   | Notes                                                           |
| ------------------- | ----------- | -------- | -------------------- | ---------------------- | --------------------------------------------------------------- |
| `DirectionalLight`  | Directional | Realtime | Warm white (~5000 K) | 0.3                    | Ambient direction; no shadows from outside since we're indoors. |
| `Chandelier_Center` | Point       | Realtime | Warm (~3200 K)       | High (tuned in editor) | Range covers full staircase height. Soft shadows.               |
| `WallSconce_1..4`   | Point       | Realtime | Warm (~3000 K)       | Medium                 | Placed at corners of the walled room.                           |

**Environment**:

- Skybox: solid dark color (we're indoors).
- Environment lighting intensity ~0.2 so corners aren't black.

If frame rate suffers later, bake the static lights — but that's a follow-up.

---

## 9. Folder layout (new under `Assets/Game/`)

```
Assets/Game/
├── Models/
│   ├── GrandStaircase/
│   │   ├── GrandStaircase.fbx
│   │   └── GrandStaircase_Textures/
│   └── Mirror/
│       ├── Mirror.fbx
│       └── Mirror_Textures/
├── Materials/                       (extracted .mat files)
├── Prefabs/
│   ├── Staircase.prefab
│   ├── Mirror.prefab
│   └── FirstPersonRig.prefab
└── Scripts/
    ├── MirrorReflection.cs
    └── FirstPersonCameraRig.cs      (mouse-look, head-bone follow, layer mask helper)
```

Scene at `Assets/Scenes/GrandStaircase.unity`. `SampleScene` stays untouched.

---

## 10. Tooling

- **Blender MCP** (`mcp__blender-mcp__*`) — Sketchfab download, Blender ops, FBX export.
- **Unity MCP** (`mcp__unity-mcp__*`):
  - `Unity_RunCommand` — all editor-side automation (importer settings, texture extraction, material wiring, scene assembly, prefab creation, lighting placement).
  - `Unity_GetConsoleLogs` — run after every `Unity_RunCommand` to catch compile/import errors.
  - `Unity_SceneView_Capture2DScene` / `Unity_Camera_Capture` — visual verification after each milestone.

Unity Editor must be open and running for the Unity MCP to function.

---

## 11. Success criteria

- [ ] Walking the scene in Play Mode in first-person feels responsive (mouse-look + WASD + jump from `ThirdPersonController.cs`).
- [ ] The grand staircase is visible, textured (no flat-white materials), and roughly the right scale (~10–12 m tall).
- [ ] Walls fully enclose the staircase — no view into the skybox/void from any standing position.
- [ ] The scene is lit; no pitch-black areas at standing height.
- [ ] The mirror, when faced, shows a live reflection of the player character (full body, head included).
- [ ] No errors or persistent warnings in the Unity console after final scene save.

---

## 12. Out of scope (explicitly deferred)

- Jack and Rose character meshes / character selection UI.
- Audio (footsteps, ambient music).
- Interactive doors, NPCs, story beats.
- Performance optimization (baking, occlusion culling).
- Mobile / VR support.
