# First-Person Self-View for Mirror Avatars — Design

**Date:** 2026-06-15
**Project:** Titanic VR (Meta Quest 3 / PCVR) — mirror avatar system
**Status:** Approved; head-hide mechanism REVISED 2026-06-15 after the spike (see "UPDATE" below) — the shared-manager architecture (Approach B) stands, but the head is hidden by a **per-camera material swap to a head-clipping shader**, NOT by bone-scaling.
**Related:** `titanic-vr-conversion`, `titanic-jack-rose-characters`, `titanic-jack-fullbody-regen` memories

> **═══ UPDATE 2026-06-15 — Spike result supersedes the bone-scale mechanism ═══**
>
> The spike (run before building) proved the originally-specced **head-bone-scale** mechanism
> **does NOT work**: Unity skins a mesh **once per frame** (after `LateUpdate`, before any camera)
> and reuses that single result for every camera, so changing the head bone in a per-camera
> `beginCameraRendering` callback is too late — both the mirror cam and the HMD cam see the same
> head-full mesh. Verified twice (edit-mode dual render + Play-mode dual render → identical),
> and `BakeMesh` confirmed the bone-scale collapses geometry fine; it just can't be made
> per-camera.
>
> **Revised mechanism (Approach C′ — validated by a second spike):** hide the head with a
> **per-camera MATERIAL SWAP**, which DOES run per camera (the fragment/material draw re-runs per
> camera even though skinning is shared — confirmed: same frame, same skinned mesh rendered red to
> one camera and normal to another). Concretely:
>
> - The **mirror keeps its original URP/Lit material, untouched** (crown jewel unaffected).
> - A shared **head-clipping material** (custom URP shader that `discard`s fragments inside a
>   world-space sphere around the head bone — center+radius pushed as global shader props each
>   frame) is swapped onto the active avatar's body `SkinnedMeshRenderer` **only for the HMD
>   camera's draw**, then restored. Same order-independent begin/end-event timing as before
>   (mirror renders nested in the main cam's begin; `end(MirrorCam)` swaps to the clip material
>   before the main draw).
> - Still a single shared `SelfViewManager` reading the active avatar from the switcher → zero
>   per-character authoring (the clip material is built at runtime from the avatar's own material,
>   preserving its textures).
>
> Everything below is unchanged EXCEPT: the "per-camera head toggle" table now swaps **materials**
> (original ⇄ clip) instead of bone scale, and the Files list adds the clip shader. The
> bone-scale text below is retained for history but is **superseded**.

## Goal

When the player looks down through the HMD they should see their **own body** — torso, legs,
and hands — belonging to the currently-selected mirror character (Default / Rose / JackV2).
Today the body exists and is correctly positioned at the player, but it is **invisible to the
HMD camera** (it lives on a mirror-only layer); only the mirror reflection shows it.

The catch: the body is one skinned mesh whose **head bone is pinned to the HMD**, so if we
simply make it HMD-visible the head mesh wraps the camera and fills/clips the near-plane. So
the body must be visible to the HMD camera **with the head suppressed for that camera only**,
while the head still appears in the **mirror**.

**Always on** — the player always has a body; no toggle, no input binding. **Full body**,
including legs (legs stay locomotion-driven; the slight mismatch during stick-walking is
accepted). **Works for every character with zero per-character setup**, now and for any future
character.

### Non-goals

- **No leg/foot tracking.** Legs follow the locomotion blend tree (idle when standing, walk
  when the stick is pushed). Real leg tracking is a separate future mission (mocap seam).
- **No comfort/vignette, no UI, no new input.**
- **No per-character calibration.** Unlike head/wrist offsets, head-hiding needs no baked
  constants — it is purely geometric (collapse the head bone).

## Context (current system, verified 2026-06-15)

- **Main (HMD) camera** `XR Origin (XR Rig)/Camera Offset/Main Camera`, `cullingMask = -257`
  → culls **exactly layer 8 ("PlayerBody")** and nothing else. The avatar is invisible to the
  HMD today.
- **Mirror reflection camera** is spawned at runtime by `MirrorReflection.cs` (named
  `MirrorCam_<mirror>`), `reflectLayers = -513` → excludes **exactly layer 9** (controller
  visuals) and renders everything else, including layer 8. This is why the avatar shows **only**
  as a reflection.
- **Avatars:** `XR Origin (XR Rig)/MirrorAvatars` holds `Default`, `Rose`, `JackV2` (one active
  at a time via `MirrorCharacterSwitcher`), plus the shared `HandTrackingPoseSource` and
  `InputModeRouter`. Each avatar is a single Humanoid `SkinnedMeshRenderer` on layer 8, root
  follows the HMD horizontally, head bone matched to the HMD, hands IK'd, fingers articulated
  (JackV2). It already stands exactly where a first-person body belongs.
- **Layer 9** is purely the controller **visual meshes** under `Left/Right Controller Visual/
UniversalController/*` (9 `MeshRenderer`s each). Tracking (`TrackedPoseDriver` on the parent
  `Left/Right Controller`) and all interactors (Near-Far, Poke, Teleport, Gaze) are on layer 0
  and are independent of those visual GameObjects.
- It is **one** `SkinnedMeshRenderer`, so the head cannot be hidden by disabling a separate
  "head renderer" — the head must be suppressed by collapsing its **bone**.

## Approach (chosen: B — single shared `SelfViewManager`)

One shared component reads the **active** avatar from `MirrorCharacterSwitcher` and hides its
head per-camera. Because the switcher already tracks the active character, this covers all
current and future characters with **no per-character component to add** — the genuinely
"works-for-all" option.

**Why B over the alternatives:**

- **Zero per-character work, now and future.** Register a character in the switcher (already
  required) and self-view's head-hide applies automatically. No component to attach per avatar,
  no constants to bake.
- **Single subscriber** to the render-pipeline events (vs. one per avatar), and a single place
  that owns the camera/layer configuration.
- Head-hiding needs **no per-rig data**, so the per-avatar pattern (used by `FingerPoseDriver`
  because it needs per-rig wiring) buys nothing here.

**Rejected:** **A (per-avatar `SelfViewHeadHider`)** — matches the `FingerPoseDriver` convention
but requires attaching the component to every avatar (a forgettable manual step) for no
functional gain, since the head bone is found generically. **C (shader per-camera head clip)** —
immune to the skinning-timing risk (below) but needs a custom body shader + a baked head mask,
far more work; kept only as the **fallback** if the spike fails.

## Architecture

### 1. Static scene configuration (one-time, no runtime code)

1. **Main Camera `cullingMask`:** add layer 8 → mask becomes `-1` (Everything). The active
   avatar (only one is enabled) becomes directly HMD-visible. Other layer-8 content does not
   exist, so nothing else is revealed.
2. **Disable the controller visuals:** set `Left Controller Visual` and `Right Controller
Visual` GameObjects inactive. This removes the "double hands" (avatar IK hands + floating
   controller models) so the avatar's own hands are the hands you see. Tracking and interactors
   are unaffected (they live on the parent controller / layer 0). Self-view is always on, so
   these visuals are never wanted.

### 2. New component: `SelfViewManager` (on the `MirrorAvatars` container)

- **References:** the `MirrorCharacterSwitcher` (to find the active avatar) and the main HMD
  `Camera` (the XR Origin camera). The mirror camera is identified at event time by name
  (`MirrorCam_*`) / by being neither the main cam nor a non-Game camera — no hard reference to
  `MirrorReflection` required.
- **Active-avatar resolution:** caches the active avatar's `Animator` → **Head** bone and its
  original `localScale`. Re-resolves whenever `switcher.CurrentIndex` changes (cheap poll in
  `LateUpdate`, or hook a switch event). If the active avatar has no valid Humanoid head bone,
  the manager no-ops for it.
- **Subscribes** to `RenderPipelineManager.beginCameraRendering` / `endCameraRendering`.
- **Per-camera head toggle** (collapse = head `localScale ≈ 1e-4`; restore = captured original):

  | Event                 | Action        | Why                                                        |
  | --------------------- | ------------- | ---------------------------------------------------------- |
  | `begin(mirror cam)`   | head **full** | the reflection must include the head                       |
  | `end(mirror cam)`     | head **~0**   | authoritative: runs after the reflection draw, before main |
  | `begin(main HMD cam)` | head **~0**   | belt-and-suspenders for the main draw                      |
  | `end(main HMD cam)`   | head **full** | restore so scene-view / next frame start clean             |
  | any other camera      | ignore        | scene view, preview, etc. see the full head                |

- **Inspector tunables:** `enabled` master bool (dev kill-switch while tuning), `headHideScale`
  (default `1e-4`). No player-facing controls.

### Why the ordering is correct and order-independent

`MirrorReflection` renders the reflection **nested inside the main camera's
`beginCameraRendering`** (it calls `RenderSingleCamera(reflectionCamera)` there, which fires the
reflection camera's own begin/end events synchronously). So per frame:

```
begin(MainCam)                      ← subscribers run in registration order:
   └─ MirrorReflection renders reflection:
        begin(MirrorCam) → head FULL
        [reflection draws WITH head]   ✓ mirror shows head
        end(MirrorCam)   → head ~0
   └─ SelfViewManager begin(MainCam) → head ~0   (redundant; harmless)
[Main camera draws WITHOUT head]       ✓ HMD shows no head
end(MainCam) → head FULL (restore)
```

`end(MirrorCam)` always runs **after** the reflection draw and **before** the main draw, and it
sets the head to ~0 — so the head is hidden for the main draw regardless of the order in which
the two `begin(MainCam)` subscribers fire. It also works when **no mirror is in view** (the
mirror events simply don't fire; `begin/end(MainCam)` still hide/restore the head).

The toggle changes only the head bone's **scale**; `AvatarRigDriver` writes the head bone's
**rotation** in `LateUpdate` (before any render event) and `FingerPoseDriver` touches only
finger bones — so there is no conflict.

## Spike gate (do this FIRST, before the full build)

The entire approach rests on the assumption that **Unity re-skins the mesh per camera**, so a
bone-scale change between the reflection draw and the main draw actually moves vertices in each.
If skinning is baked once per frame, the toggle would hide the head from neither or both.

**Spike:** a throwaway version that hides the active avatar's head on the main cam and restores
it on the mirror cam, validated by:

- **Dual-camera render-to-PNG** (the proven temp-camera capture method from
  `titanic-vr-conversion`): one capture with a culling mask **including** layer 8 from the HMD
  pose (should show **no** head) and the mirror RT (should show a head).
- **In-headset:** look down (body present, no head blob / near-plane clip) and check the mirror
  (head present).

**If the spike fails** → fall back to **Approach C** (custom shader that discards head fragments
when a per-camera "is-HMD" keyword is set) and re-spec that part. The scene configuration and the
`SelfViewManager` shell (camera classification, active-avatar resolution) carry over unchanged.

## Verification

Per the project's established model (no test harness in the project):

- **Pure-static logic check** via Unity-MCP `RunCommand`: the per-camera decision (given a
  camera classified as main / mirror / other and a begin/end phase, produce the correct head
  state) extracted as a small pure function and exercised on synthetic inputs — the same pattern
  as `AvatarRigDriver.StepYaw` and `MirrorCharacterSwitcher.WrapIndex`.
- **In-headset, Editor Play over Link:** look down and confirm full body (torso + legs + hands)
  with **no** head artifact; confirm the **mirror** still shows the complete body **including the
  head**; **cycle through Default / Rose / JackV2** and confirm self-view works on each with no
  per-character setup; confirm the controller-visual hide left tracking + the quit/poke
  interaction working; console clean.

## Risks / watch-items

- **Skinning timing** (the spike gate). Highest-risk assumption; verified before full build.
- **Head-bone children.** Eyes / hair parented to the head bone collapse with it (good). A
  separate hair `SkinnedMeshRenderer` weighted to the head bone also collapses (good). Verify per
  character — **JackV2 first** — that nothing of the head lingers and the neck stub isn't
  objectionable from the HMD looking down.
- **Controller-visual hide.** Confirm disabling `Left/Right Controller Visual` does not disturb
  `TrackedPoseDriver` / interactors (they are on the parent / layer 0). Keep the change reversible
  (GameObject active toggle), not a deletion.
- **Character switch mid-session.** The manager must re-resolve the head bone when the active
  avatar changes, and restore the **previous** avatar's head scale so a disabled avatar isn't
  left collapsed (matters only if a disabled avatar is ever re-enabled — restore on switch to be
  safe).
- **Build parity.** Validate in Unity Play first; the technique is render-pipeline-event based
  (no Editor-only code), so it should carry to the standalone `.exe`; verify there too.

## Files

- **New:** `Assets/Game/Scripts/VR/SelfViewManager.cs`.
- **Scene edits (`GrandStaircase.unity`):** Main Camera `cullingMask` → Everything; disable
  `Left Controller Visual` + `Right Controller Visual`; add `SelfViewManager` to `MirrorAvatars`
  wired to the switcher + main camera.
- No change to `MirrorReflection`, `AvatarRigDriver`, `FingerPoseDriver`, or the switcher.
