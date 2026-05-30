# Jack & Rose Mirror Avatars — Design Spec

- **Date:** 2026-05-30
- **Status:** Draft for review
- **Project:** Titanic VR (Unity 6 / URP, Meta Quest 3 PCVR) — see prior specs:
  `2026-05-29-titanic-vr-quest3-conversion-design.md` (VR conversion + IK mirror avatar).
- **Mode:** Proof-of-concept. The goal is to _prove the pipeline and the swap_, not to ship a
  polished selection experience. The "how the player chooses" UX is deferred until both
  characters are confirmed good.

## 1. Goal

Let the player **see themselves as Jack or Rose in the mirror** instead of the current default
avatar. Each character is a stylized (not photoreal, not actor-likeness) early-1900s
Titanic-era figure, driven by the **existing IK/mirror rig** (head + 2 controllers → Humanoid
IK), and **swappable at runtime**. The system must make **adding more characters later trivial**.

### Success criteria

1. At least one generated character imports into Unity and **configures as a Unity Humanoid
   without manual bone remapping**.
2. Driven by the existing `AvatarRigDriver` + mirror, the character shows **no uncanny
   deformation** under the motions the rig stresses: raised arms, rotated wrists, turned head.
3. The reflection **walks while the player moves** (does not slide in an idle pose).
4. Jack and Rose can be **swapped at runtime** (a controller button is sufficient for the POC),
   and the **current default avatar remains selectable** for A/B comparison during user-testing.
5. Adding a further character is a matter of dropping a prefab into a list — no code changes.

## 2. Tool decision

**Tool: Meshy.ai Pro — exclusively.** No paid desktop fallback. Character Creator 5 (CC5) was
researched as the rig-safest option but **rejected on cost** ($299 + a learning curve). A
hands-on bake-off (Phase 0) still gates the work, but on a **pass / iterate** basis (iterate
prompts, settings, or Blender cleanup) rather than a switch-tools basis.

### Rationale

Deep research (103 agents, 21 sources, 25 claims adversarially verified) established that for
this _specific_ use case the make-or-break criterion is **clean Unity-Humanoid mapping + skin
weights that survive IK**, not mesh prettiness.

- **Meshy.ai** is **$20/mo Pro** (~$16/mo annual), web-based, easy, and **generates the period
  costume directly from a prompt/reference image**. It offers in-tool auto-rigging (no Mixamo
  needed) and a documented Unity workflow that sets Animation Type = Humanoid. **Known risk:**
  its strongest "clean rig, no manual fixes" claims were **refuted** in research and its rig/IK
  quality has **no neutral verification** — unproven, not disproven. We accept this risk because
  it is cheap to test (Phase 0) and mitigable in-pipeline (re-generation + Blender cleanup)
  rather than paying for CC5.
- **CC5 + AccuRIG** (rejected): the rig-safest option — ships its own pro humanoid rig + a free
  Unity Auto Setup plugin, corroborated by neutral sources — but **$299 perpetual** and a
  desktop-app learning curve put it out of scope for this POC. Recorded here only so the
  trade-off is not re-litigated.
- **Mixamo round-trip rigging is explicitly avoided** — documented to deform badly at exactly
  the joints this project drives (wrists, shoulders under raised-arm/rotated-wrist poses). We
  use Meshy's _own_ rig instead.

### Licensing

Meshy **paid (Pro) tiers grant private, fully-owned commercial rights** (suitable for company
user-testing). The free tier is CC-BY (requires crediting Meshy), so a paid plan is needed
regardless. (Re-confirm exact live pricing at purchase — figures are 2026-current but volatile.)

### Phase-0 validation — PASSED (2026-05-30)

The bake-off ran on **Jack**, produced entirely in the Meshy web UI (image-to-3D from a reference
image → texture w/ Remove Lighting + PBR → remesh **30K / Quad** → **Humanoid rig**) and brought
into Unity at `Assets/Game/Characters/Jack/Jack.fbx`:

- **Criterion 1 (Humanoid map): PASS.** Imports as a valid Unity Humanoid with **no manual
  remap** (`isHuman=True`, 22 mapped bones). Meshy's bone names are clean/standard (`Hips`,
  `Spine/Spine01/Spine02`, `LeftUpLeg/LeftLeg/LeftFoot/LeftToeBase`,
  `LeftShoulder/LeftArm/LeftForeArm/LeftHand`, `neck/Head`). **No finger bones** (just
  `LeftHand`/`RightHand`) — fine, since the IK drives hand position + wrist rotation, not fingers.
- **Criterion 2 (deformation): PASS.** Posed into an IK-stress pose (arms forward, elbows bent,
  wrists rotated) via Unity's Humanoid muscle system — shoulder/elbow/wrist deform cleanly, no
  pinching or spikes.
- **Textures: PASS.** 6 PBR maps; a URP/Lit material (`Jack_Mat.mat`, base + normal, metallic 0,
  smoothness 0.3) renders correctly in the atrium.

**Key pipeline facts learned (feed the plan):**

- The Meshy **`_texture_fbx` download is the UNRIGGED mesh** (imports as 1 static node). The
  rigged skeleton is a **separate download** (Rig → Download: _Rigged Character ON, Animation OFF,
  With Skin, FBX_).
- The **Meshy web app and the API/MCP are separate stores** — web-UI assets are not visible to the
  API, so characters are **downloaded manually** (the planned DCC Bridge path is moot for this
  workflow).
- Import settings that matter: set the `_normal` texture to **Normal map**; `importAnimation=off`
  (kills an empty-clip warning); **`optimizeGameObjects` MUST stay false** or the `LateUpdate`
  wrist/head bone-writes break (the bone transforms would be stripped).
- **Scale:** the FBX is ~2.11 m tall → must be scaled to player height when placed as the avatar.

## 3. Architecture

**Chosen approach: one self-contained prefab per character** (rejected alternatives: a single
driver that re-targets a swapped avatar — fiddly re-init on each swap; a shared skeleton with
swapped meshes — impossible since generated characters won't share topology).

### Components

- **Character prefab (one per character: Default, Jack, Rose, …).**
  - Root GameObject on **layer 8** (mirror-only: XR camera culls it, reflection camera includes
    it — unchanged from current setup).
  - A **Humanoid** `Animator` (with the locomotion Animator Controller, **IK Pass enabled**).
  - Its **own `AvatarRigDriver`** carrying that rig's **calibrated wrist offsets**
    (`leftHandOffsetEuler` / `rightHandOffsetEuler`) — these are per-rig constants.
  - Responsibility: be a drop-in mirror body. Depends only on a shared `IPoseSource`.

- **`MirrorCharacterSwitcher` (new MonoBehaviour).**
  - Fields: `List<GameObject> characters` (the prefabs/instances), a reference to the shared
    pose-source `MonoBehaviour`, and an input action (controller button).
  - Behaviour: on start, enable index 0 and disable the rest; on button press, advance the
    index, enable the active character, disable the others, and assign the shared pose source to
    the active character's `AvatarRigDriver.poseSourceBehaviour`.
  - Responsibility: own "which character is live." **Adding a character = add it to the list.**
  - Interface: public `SetCharacter(int index)` / `Next()` so a future real selection UI can
    call it without change.

- **Shared `IPoseSource`** (existing `ThreePointIKPoseSource`): unchanged. It only reads
  HMD/controllers, so every character can share one instance.

- **`AvatarRigDriver`** (existing): unchanged in logic. Already resolves its `Animator` and
  Humanoid bones generically, so it works on any Humanoid. Only its serialized wrist-offset
  values differ per character.

### Locomotion (new component / wiring)

The reflection must walk while the player moves.

- A small **`LocomotionAnimatorDriver`** reads the player's horizontal movement speed (from the
  existing XR smooth-move provider / CharacterController velocity) and writes it to a **`Speed`
  parameter** on the avatar's Animator.
- The Animator Controller uses a **locomotion blend tree** (idle ↔ walk ↔ run) on the base
  layer. Unity Humanoid retargeting means **any** walk clip (Unity Starter Assets' existing
  blend tree, or Mixamo, or Meshy's library) drives any Humanoid — no per-character animation
  authoring.
- **Layering is correct by construction:** the base layer plays the walk (legs/hips/torso); the
  **IK Pass overrides the hands** to the controllers; `LateUpdate` continues to drive head +
  wrists. Result: the reflection walks while its hands still mirror the player's.
- This is a Unity-side task and is **independent of the character tool.**

## 4. Pipeline & tooling

- **Meshy MCP server** (`@meshy-ai/meshy-mcp-server` v0.3.0, official; stdio via `npx`)
  connected so generation/rigging/animation can be driven directly (20 tools incl. `text_to_3d`,
  `image_to_3d`, `rig`, `animate`, `remesh`, `retexture`, downloads, and a free `balance` query).
  **An API key requires a Pro plan** (generated at `meshy.ai/settings/api`); **API calls draw on
  the same account credits** as the web app (each MCP call = one REST call at the same rate), so
  Pro credits cover MCP use. Configured in `~/.claude.json` under this project's `mcpServers`
  (env `MESHY_API_KEY`); takes effect after a Claude Code restart. A `balance` call is the
  zero-risk connectivity test.
- **DCC Bridge to Unity** (Pro feature) for one-click transport of the rigged FBX into the
  Unity scene; **DCC Bridge to Blender** available if topology cleanup is needed (paired with
  the existing Blender MCP and the known empty-parent flatten lesson).
- **Unity-side Humanoid characterization** (FBX importer → Animation Type = Humanoid →
  Configure) is expected to remain a Unity step even with the Bridge; verifying it is automatic
  vs manual is part of Phase 0.

### Per-character wrist calibration

Each character's wrist offsets are baked **once** via the existing deterministic
"flashlight-pose" calibration (player holds arms forward, thumbs up, palms inward, looking
ahead; a `Unity_RunCommand` derives `offset = inv(controllerRot)·desiredBone` and verifies
`fingers·forward ≈ 1.0`), then stored in that character's `AvatarRigDriver`. No eyeballing,
no hand-modeling — fits the "minimize manual 3D work" constraint.

## 5. Phased plan (gated)

- **Phase 0 — Bake-off (decision gate).**
  - Verify Meshy Pro credits cover MCP/API; connect the Meshy MCP; add API key.
  - Generate **one** test character (Jack) via Meshy's in-tool auto-rig; bring into Unity
    (Bridge or FBX import); set Humanoid.
  - Put on layer 8, drive with the **existing** `AvatarRigDriver` + mirror; calibrate wrists.
  - **Pass criteria:** (1) Humanoid without manual remap; (2) no ugly deformation under raised
    arms / wrist rotation / head turn in the mirror; (3) wrist calibration converges.
  - **Pass → proceed to Phase 1.** Fail any criterion → iterate (re-generate with a better
    prompt/settings; optionally clean topology/weights via the Blender MCP). If repeatedly
    unworkable, pause and reconsider scope with the user — CC5 is not a fallback here.
- **Phase 1 — Both characters.** Produce Jack + Rose with Meshy; each becomes a
  layer-8 Humanoid prefab with its own baked wrist offsets; scale to ~player height; reassign
  materials to URP if needed.
- **Phase 2 — Swap + locomotion.** Build `MirrorCharacterSwitcher` (button-cycle, including the
  default avatar) and `LocomotionAnimatorDriver` + locomotion blend tree. Verify in headset:
  swap works, both characters look right in the mirror, both walk while moving.

## 6. Out of scope (future missions)

Polished character-selection UX (menu / costume-grab / mirror-toggle), grabbing/costume props,
facial tracking/blendshapes, mocap (the `IPoseSource` seam already exists), additional
characters beyond Jack & Rose (the system supports them; we don't author them now).

## 7. Risks & mitigations

- **Meshy rig fails the Humanoid/IK bar** (its quality is unverified) → Phase 0 catches it
  cheaply; mitigation is re-generation + Blender cleanup, and escalation to the user if
  fundamentally unworkable (no CC5 fallback).
- **Generated topology deforms badly under IK** → topology/weight cleanup in Blender via the DCC
  Bridge + Blender MCP.
- **Per-rig wrist offsets differ per character** → handled by per-character calibration bake;
  expected, not a surprise.
- **MCP/API credits billed separately from web Pro** → verified before relying on the MCP; web
  UI + DCC Bridge is the fallback path.
- **URP material/shader mismatch on import** → reassign to URP/Lit on import (minor, known).
- **Proportion mismatch (character vs player height)** affects arm reach → scale each character
  to ~player height in Phase 1.

## 8. Open questions

- ~~Does the Meshy Pro credit pool cover MCP/API calls?~~ **Resolved:** API key requires Pro;
  API calls consume the same account credits at the same rate. (Confirm exact per-operation
  credit costs as we go.)
- ~~Does the DCC Bridge deliver a pre-configured Unity Humanoid?~~ **Moot:** web-UI assets aren't
  visible to the API/Bridge, so characters are downloaded manually; Unity Humanoid auto-configures
  from the rigged FBX with no manual remap.
- ~~Will Meshy produce period-accurate 1912 costume?~~ **Resolved:** image-to-3D from a curated
  reference image (T-pose, plain background, props removed) gives a clean, on-period result.
- **Open:** in-headset confirmation of deformation while driven live by the IK rig (vs the
  scripted muscle-pose pre-check). To be confirmed during the build when Jack is wired into the
  mirror.
- **Open:** does Meshy's loose period costume (Rose's long open coat/skirt) deform acceptably
  under leg motion, or need a tweak? Evaluate when Rose is wired in.
