# Titanic VR Conversion (Meta Quest 3 / PCVR) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **▶ STATUS (2026-05-30) — read the `titanic-vr-conversion` memory "RESUME POINT" for the authoritative current state; it overrides this plan where they differ.** DONE & headset-verified: Tasks 1–3 + 6 (packages/OpenXR, VR rig, smooth locomotion + smooth/snap/none turn, stereo-correct mirror). Task 5 avatar: head + hand-**position** tracking done — implemented with Unity built-in **`OnAnimatorIK`**, NOT the Animation Rigging constraint graph this plan describes (chosen for script-reliability); wrist **rotation** deferred → FinalIK. LEFT: Task 4 in-VR comfort-menu panel, Task 7 boundary-cage geometry, Task 8 polish/cleanup.

**Goal:** Convert the existing desktop first-person Titanic grand-staircase scene into a room-scale **PCVR** experience for the Meta Quest 3, with headset head-look, controller-driven hands, smooth stick locomotion, a toggleable turn-comfort mode, a warn-only ~1 m physical-movement boundary, and a full-body IK avatar the player sees in the existing mirror.

**Architecture:** Replace the desktop input/camera scripts with an XRI `XR Origin` rig (OpenXR provider, Windows Standalone). Keep the scene, lighting, walls, and the real-time planar mirror. Drive a Humanoid avatar through a swappable `PoseSource` abstraction (default: Animation Rigging 3-point IK) so it reflects in the mirror; preserve the layer-8 "mirror-only body" culling. Add two small systems — boundary warning and comfort settings. Validate the mirror under VR stereo early.

**Tech Stack:** Unity 6000.4.0f1, URP 17.4.0, OpenXR, XR Plugin Management, XR Interaction Toolkit (XRI 3.x), Animation Rigging, Input System 1.19, Unity MCP (editor automation), Quest 3 over Link/Air Link.

**Reference spec:** `docs/superpowers/specs/2026-05-29-titanic-vr-quest3-conversion-design.md`

**User rule:** Never run `git commit` on the user's behalf. Every "Suggest commit" step lists a message — the user runs the actual commit.

**API-version note:** XRI 3.x unified several component names (e.g. `ContinuousMoveProvider`, `SnapTurnProvider`, `ContinuousTurnProvider`) and namespaces under `UnityEngine.XR.Interaction.Toolkit.Locomotion.*`. Exact names depend on the resolved package version. Where a step references an XRI type, **verify the type compiles via `Unity_GetConsoleLogs` and adjust the namespace/name to the installed version if needed** — this is called out inline.

---

## File structure

**New C# scripts (`Assets/Game/Scripts/`):**

- `VR/ComfortSettings.cs` — runtime toggle between snap and smooth turn providers; exposes turn speed/angle.
- `VR/ComfortSettingsUI.cs` — wires world-space UI buttons to `ComfortSettings`.
- `VR/IPoseSource.cs` — interface: a pose source applies head + two hand targets to the avatar rig each frame.
- `VR/ThreePointIKPoseSource.cs` — default `IPoseSource`: maps HMD + 2 controllers to the avatar's IK targets (Animation Rigging).
- `VR/AvatarRigDriver.cs` — owns the active `IPoseSource`, holds references to rig target transforms, runs per-frame.
- `VR/RecenterAnchor.cs` — captures/stores the play-space center; provides recenter.
- `VR/BoundaryWarning.cs` — measures HMD horizontal distance from the anchor; fades the cage in/out.

**New shader/material:**

- `Assets/Game/Materials/BoundaryCage.mat` (+ optional `BoundaryCage.shader`) — transparent grid wall.

**Modified:**

- `Assets/Game/Scripts/MirrorReflection.cs` — stereo-correct the reflection render.
- `Packages/manifest.json` — add XR + Animation Rigging packages.
- `Assets/Scenes/GrandStaircase.unity` — XR rig replaces desktop rig; add avatar IK, boundary, comfort UI.
- `Assets/Game/Prefabs/` — new `VRPlayerRig.prefab`; the desktop `FirstPersonRig.prefab` is removed from the scene (kept on disk).

**Removed from the VR player (kept in repo):**

- `FirstPersonController.cs`, `FirstPersonCameraRig.cs` — not on the VR rig.

---

## Pre-flight (do once)

- [ ] Confirm Unity Editor is open with the `titanic` project (Unity MCP needs it running).
- [ ] Confirm the **Android Build Support** module is _not_ required for this phase (PCVR only). The Quest is reached over **Link/Air Link** and tested via **Play in Editor**.
- [ ] Confirm a Quest 3 is connected via Link (Quest Link app running on PC, headset in Link) so play-mode verification steps work. If no headset is available at authoring time, mark play-mode steps as "deferred — verify on lab PC."
- [ ] Confirm working tree is clean enough that new changes are reviewable (the user commits; do not commit for them).

---

## Task 1: Install XR + Animation Rigging packages

**Files:**

- Modify: `Packages/manifest.json`

- [ ] **Step 1: Add the packages to the manifest**

Edit `Packages/manifest.json` and add these entries inside `"dependencies"` (alphabetical placement is fine; keep valid JSON — watch trailing commas):

```json
"com.unity.animation.rigging": "1.3.0",
"com.unity.xr.interaction.toolkit": "3.0.8",
"com.unity.xr.management": "4.5.1",
"com.unity.xr.openxr": "1.14.3",
```

These pull `com.unity.xr.core-utils` automatically. If Unity reports a specific version is unavailable for this editor, **do not guess** — open `Window > Package Manager`, choose "Add package by name", enter the package id with no version, and let Unity resolve the compatible version. Accept whatever it resolves.

- [ ] **Step 2: Let Unity import, then check for errors**

Switch focus to Unity so it imports the new packages (or run `mcp__unity-mcp__Unity_RunCommand` with `UnityEditor.AssetDatabase.Refresh();`). Then run `mcp__unity-mcp__Unity_GetConsoleLogs`.

Expected: packages import; **no compile errors**. Warnings about XRI samples/new input are fine. If you see "package version not found", apply the Package Manager fallback from Step 1.

- [ ] **Step 3: Import the XRI Starter Assets sample (for default input actions)**

Manual editor step (the sample is not addressable via script reliably): `Window > Package Manager > XR Interaction Toolkit > Samples > import "Starter Assets"`. This gives a ready-made `XRI Default Input Actions` asset we reuse for controller bindings.

Verify by running `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;
var guids = AssetDatabase.FindAssets("XRI Default Input Actions");
Debug.Log(guids.Length > 0
    ? "XRI Default Input Actions found: " + AssetDatabase.GUIDToAssetPath(guids[0])
    : "XRI Starter Assets NOT imported — import via Package Manager samples.");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: the asset path is logged.

- [ ] **Step 4: Suggest commit**

```
chore: add OpenXR, XR Interaction Toolkit, and Animation Rigging packages
```

---

## Task 2: Configure XR Plugin Management + OpenXR (PCVR, Windows Standalone)

**Files:**

- Modify: project settings (XR) — created by the editor under `ProjectSettings/` and `Assets/XR/`.

- [ ] **Step 1: Enable OpenXR on the Windows Standalone platform**

Manual editor step (XR settings UI is the reliable path): `Edit > Project Settings > XR Plug-in Management`. On the **PC, Mac & Linux Standalone** tab (the monitor icon — _not_ Android), check **OpenXR**. Let it install the OpenXR feature sets.

- [ ] **Step 2: Add the Meta Quest interaction profile + feature group**

In `Project Settings > XR Plug-in Management > OpenXR` (Standalone tab):

- Under **Interaction Profiles**, add **Oculus Touch Controller Profile**.
- Under **OpenXR Feature Groups / features**, enable the **Meta Quest** support feature if listed.

- [ ] **Step 3: Set rendering mode for stereo**

Still in the OpenXR Standalone settings, set **Render Mode** to **Single Pass Instanced** (the default; note it explicitly because the mirror fix in Task 7 depends on knowing the stereo path). Record the chosen mode in the task notes.

- [ ] **Step 4: Verify the runtime is active**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEngine;
using UnityEngine.XR.Management;
var settings = XRGeneralSettings.Instance;
Debug.Log(settings != null && settings.Manager != null
    ? "XR Manager present. Loaders: " + settings.Manager.activeLoaders.Count
    : "XR settings missing — re-check XR Plug-in Management.");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: "XR Manager present" with at least one loader. (Loader count may read 0 outside play mode on some versions — the definitive check is play mode in Step 5.)

- [ ] **Step 5: Smoke-test the headset over Link (manual)**

With the Quest connected via Link, create a throwaway empty scene with just the default Main Camera converted to an XR rig (`right-click hierarchy > XR > Convert Main Camera To XR Rig`, or add an `XR Origin`), press Play, and confirm the headset view tracks head movement. Then discard the throwaway scene.

Expected: head tracking works in the headset. If black/no tracking: confirm Quest Link is running and OpenXR is the active OpenXR runtime (Meta sets this via the Oculus app; or set Windows OpenXR runtime to "Oculus").

- [ ] **Step 6: Suggest commit**

```
chore: enable OpenXR (Windows Standalone) with Oculus Touch profile
```

---

## Task 3: Build the VR player rig (replaces desktop rig)

**Files:**

- Create: `Assets/Game/Prefabs/VRPlayerRig.prefab`
- Modify: `Assets/Scenes/GrandStaircase.unity`

- [ ] **Step 1: Create an `XR Origin (VR)` rig in the scene**

Manual editor step (XRI provides a menu that wires the camera offset + tracked pose drivers correctly): open `Assets/Scenes/GrandStaircase.unity`, then `GameObject > XR > XR Origin (VR)`. This creates `XR Origin` → `Camera Offset` → `Main Camera` (with `TrackedPoseDriver`), and `LeftHand Controller` / `RightHand Controller` objects.

- [ ] **Step 2: Add locomotion components**

Run `mcp__unity-mcp__Unity_RunCommand`. **Verify the type names against the installed XRI version** (see API-version note); adjust namespaces if the console reports "type not found":

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.XR.CoreUtils;

var scene = EditorSceneManager.OpenScene("Assets/Scenes/GrandStaircase.unity");
var origin = Object.FindAnyObjectByType<XROrigin>();
if (origin == null) { Debug.LogError("No XR Origin in scene — run Step 1 first."); }
else
{
    var go = origin.gameObject;
    // CharacterController for wall collision
    var cc = go.GetComponent<CharacterController>();
    if (cc == null) cc = go.AddComponent<CharacterController>();
    cc.height = 1.7f; cc.radius = 0.3f; cc.center = new Vector3(0, 0.9f, 0);
    EditorSceneManager.SaveScene(scene);
    Debug.Log("CharacterController added to XR Origin.");
}
```

Then add, **via the editor Inspector on the XR Origin** (these XRI components are most reliably added in-editor so their serialized Input Action references can be wired): a `LocomotionMediator` (or `Locomotion System` per version), a `Continuous Move Provider`, a `Snap Turn Provider`, and a `Continuous Turn Provider`. On each provider, assign the move/turn actions from the imported `XRI Default Input Actions`. Set `Continuous Move Provider` "Forward Source" to the Main Camera so movement is head-relative.

- [ ] **Step 3: Disable continuous turn initially (snap is the comfort default)**

In the Inspector, **uncheck** the `Continuous Turn Provider` component so only snap turn is active at start. `ComfortSettings` (Task 4) toggles between them at runtime.

- [ ] **Step 4: Position the rig at the desktop spawn point**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.XR.CoreUtils;

var scene = EditorSceneManager.GetActiveScene();
var origin = Object.FindAnyObjectByType<XROrigin>();
var stair = GameObject.Find("Staircase");
if (origin != null && stair != null)
{
    var rends = stair.GetComponentsInChildren<Renderer>();
    var b = rends[0].bounds;
    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
    origin.transform.position = new Vector3(b.center.x, b.min.y, b.min.z - 1.5f);
    origin.transform.rotation = Quaternion.LookRotation(
        new Vector3(b.center.x, b.min.y, b.center.z) - origin.transform.position, Vector3.up);
    EditorSceneManager.SaveScene(scene);
    Debug.Log($"XR Origin placed at {origin.transform.position}");
}
else Debug.LogError("Missing XR Origin or Staircase.");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`.

- [ ] **Step 5: Remove the old desktop rig and any extra cameras from the scene**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var scene = EditorSceneManager.GetActiveScene();
var old = GameObject.Find("FirstPersonRig");
if (old != null) { Object.DestroyImmediate(old); Debug.Log("Removed FirstPersonRig from scene."); }
// Ensure exactly one enabled AudioListener (the XR camera). Disable extras.
var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
for (int i = 1; i < listeners.Length; i++) listeners[i].enabled = false;
EditorSceneManager.SaveScene(scene);
Debug.Log($"AudioListeners in scene: {listeners.Length} (kept 1 enabled).");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: old rig removed, one listener.

- [ ] **Step 6: Save the rig as a prefab**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;
using Unity.XR.CoreUtils;

var origin = Object.FindAnyObjectByType<XROrigin>();
PrefabUtility.SaveAsPrefabAssetAndConnect(origin.gameObject,
    "Assets/Game/Prefabs/VRPlayerRig.prefab", InteractionMode.AutomatedAction);
Debug.Log("VRPlayerRig prefab saved.");
```

- [ ] **Step 7: Play-mode verify (manual, over Link)**

Press Play with the Quest connected. Verify: head-look tracks; left stick moves smoothly head-relative; right stick snap-turns; walking into a wall is blocked by the CharacterController.

Expected: all four behaviors work. If movement is missing, the move/turn actions aren't assigned (Step 2) or the Input Action asset isn't enabled — check `Unity_GetConsoleLogs` for input warnings.

- [ ] **Step 8: Suggest commit**

```
feat: VR player rig (XR Origin + smooth move + snap turn), replacing desktop rig
```

---

## Task 4: Comfort settings — turn-style toggle + world-space UI

**Files:**

- Create: `Assets/Game/Scripts/VR/ComfortSettings.cs`
- Create: `Assets/Game/Scripts/VR/ComfortSettingsUI.cs`
- Modify: `Assets/Scenes/GrandStaircase.unity`

- [ ] **Step 1: Write `ComfortSettings.cs`**

Create `Assets/Game/Scripts/VR/ComfortSettings.cs`:

```csharp
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning; // adjust per installed XRI version

// Switches the active turn style at runtime by enabling exactly one turn provider.
// Snap is the comfort-safe default; smooth/continuous is opt-in for testers who prefer it.
[DisallowMultipleComponent]
public class ComfortSettings : MonoBehaviour
{
    public enum TurnStyle { Snap, Smooth }

    [Tooltip("Snap turn provider on the XR Origin.")]
    public SnapTurnProvider snapTurn;

    [Tooltip("Continuous (smooth) turn provider on the XR Origin.")]
    public ContinuousTurnProvider smoothTurn;

    [Tooltip("Turn style active on start.")]
    public TurnStyle startStyle = TurnStyle.Snap;

    public TurnStyle Current { get; private set; }

    void Start() => SetTurnStyle(startStyle);

    public void SetTurnStyle(TurnStyle style)
    {
        Current = style;
        if (snapTurn != null) snapTurn.enabled = (style == TurnStyle.Snap);
        if (smoothTurn != null) smoothTurn.enabled = (style == TurnStyle.Smooth);
    }

    // Convenience hooks for UI buttons.
    public void UseSnap() => SetTurnStyle(TurnStyle.Snap);
    public void UseSmooth() => SetTurnStyle(TurnStyle.Smooth);
    public void Toggle() => SetTurnStyle(Current == TurnStyle.Snap ? TurnStyle.Smooth : TurnStyle.Snap);
}
```

If the `Turning` namespace differs in the installed XRI, fix the `using` and the two provider type names to match (verify in Step 3).

- [ ] **Step 2: Write `ComfortSettingsUI.cs`**

Create `Assets/Game/Scripts/VR/ComfortSettingsUI.cs`:

```csharp
using UnityEngine;
using UnityEngine.UI;

// Binds world-space UI controls to ComfortSettings. Attach to the settings panel root.
[DisallowMultipleComponent]
public class ComfortSettingsUI : MonoBehaviour
{
    public ComfortSettings comfort;
    public Button snapButton;
    public Button smoothButton;
    public Text statusLabel;

    void Start()
    {
        if (snapButton != null) snapButton.onClick.AddListener(OnSnap);
        if (smoothButton != null) smoothButton.onClick.AddListener(OnSmooth);
        Refresh();
    }

    void OnSnap()   { if (comfort != null) comfort.UseSnap();   Refresh(); }
    void OnSmooth() { if (comfort != null) comfort.UseSmooth(); Refresh(); }

    void Refresh()
    {
        if (statusLabel != null && comfort != null)
            statusLabel.text = "Turn: " + comfort.Current;
    }
}
```

- [ ] **Step 3: Compile and check**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
AssetDatabase.Refresh();
UnityEngine.Debug.Log("ComfortSettings compile requested.");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: **no compile errors**. If the XRI turn-provider types/namespace don't resolve, fix the `using`/type names in `ComfortSettings.cs` to the installed version and recompile.

- [ ] **Step 4: Attach `ComfortSettings` to the XR Origin and wire the providers**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning; // adjust per version

var scene = EditorSceneManager.OpenScene("Assets/Scenes/GrandStaircase.unity");
var origin = Object.FindAnyObjectByType<XROrigin>().gameObject;
var comfort = origin.GetComponent<ComfortSettings>() ?? origin.AddComponent<ComfortSettings>();
comfort.snapTurn = origin.GetComponentInChildren<SnapTurnProvider>(true);
comfort.smoothTurn = origin.GetComponentInChildren<ContinuousTurnProvider>(true);
EditorUtility.SetDirty(comfort);
EditorSceneManager.SaveScene(scene);
Debug.Log($"ComfortSettings wired: snap={comfort.snapTurn!=null}, smooth={comfort.smoothTurn!=null}");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: both providers found (`snap=True, smooth=True`).

- [ ] **Step 5: Build the world-space settings panel (manual editor step)**

Create a world-space Canvas near the spawn (e.g. on a wall): `GameObject > UI > Canvas`, set Render Mode = **World Space**, scale ~0.001, size it to ~0.5 m. Add two `Button`s ("Snap", "Smooth") and a `Text` status label. Add an `ComfortSettingsUI` component to the canvas root and assign `comfort` (the XR Origin's `ComfortSettings`), the two buttons, and the label.

For the controller to click it, add a UI ray interactor: on the right-hand controller add XRI's `XR Ray Interactor` (+ line visual) configured to hit UI, and ensure the scene has an `XR UI Input Module` / `EventSystem` (XRI provides one in Starter Assets). Add a Tracked Device Graphic Raycaster to the canvas.

- [ ] **Step 6: Play-mode verify (manual)**

Press Play. Point the right controller at the panel, click "Smooth" — confirm right-stick turning becomes continuous; click "Snap" — confirm it returns to stepped turning. The status label updates.

Expected: toggle works live; only one turn style active at a time.

- [ ] **Step 7: Suggest commit**

```
feat: comfort settings — runtime snap/smooth turn toggle with world-space UI
```

---

## Task 5: Full-body IK avatar with swappable PoseSource

**Files:**

- Create: `Assets/Game/Scripts/VR/IPoseSource.cs`
- Create: `Assets/Game/Scripts/VR/ThreePointIKPoseSource.cs`
- Create: `Assets/Game/Scripts/VR/AvatarRigDriver.cs`
- Modify: `Assets/Scenes/GrandStaircase.unity` (avatar under the rig)

- [ ] **Step 1: Write `IPoseSource.cs`**

Create `Assets/Game/Scripts/VR/IPoseSource.cs`:

```csharp
using UnityEngine;

// A pose source positions the avatar's IK targets each frame from some tracking input.
// Default impl: ThreePointIKPoseSource (HMD + 2 controllers). Future: a mocap-driven source.
// The seam that lets mocap drop in later without touching the rig setup.
public interface IPoseSource
{
    // Called every frame by AvatarRigDriver. Implementations write world-space
    // target transforms for head and both hands.
    void UpdateTargets(
        Transform headTarget, Transform leftHandTarget, Transform rightHandTarget);

    bool IsActive { get; }
}
```

- [ ] **Step 2: Write `ThreePointIKPoseSource.cs`**

Create `Assets/Game/Scripts/VR/ThreePointIKPoseSource.cs`:

```csharp
using UnityEngine;

// Maps the headset and two controllers onto the avatar's head/hand IK targets.
// Optional per-axis offsets correct for grip pose vs. palm/bone alignment.
[DisallowMultipleComponent]
public class ThreePointIKPoseSource : MonoBehaviour, IPoseSource
{
    [Header("Tracked sources (from the XR rig)")]
    public Transform hmd;
    public Transform leftController;
    public Transform rightController;

    [Header("Offsets (local, applied to hand targets)")]
    public Vector3 handPositionOffset = Vector3.zero;
    public Vector3 handRotationEuler = Vector3.zero;

    public bool IsActive => hmd != null && leftController != null && rightController != null;

    public void UpdateTargets(Transform headTarget, Transform leftHandTarget, Transform rightHandTarget)
    {
        if (!IsActive) return;

        headTarget.SetPositionAndRotation(hmd.position, hmd.rotation);

        var rot = Quaternion.Euler(handRotationEuler);
        leftHandTarget.SetPositionAndRotation(
            leftController.TransformPoint(handPositionOffset), leftController.rotation * rot);
        rightHandTarget.SetPositionAndRotation(
            rightController.TransformPoint(handPositionOffset), rightController.rotation * rot);
    }
}
```

- [ ] **Step 3: Write `AvatarRigDriver.cs`**

Create `Assets/Game/Scripts/VR/AvatarRigDriver.cs`:

```csharp
using UnityEngine;

// Owns the active IPoseSource and the rig target transforms that Animation Rigging
// constraints follow. Runs after tracking has updated. Swap `poseSource` (e.g. to a
// mocap source) without changing the rig.
[DisallowMultipleComponent]
public class AvatarRigDriver : MonoBehaviour
{
    [Tooltip("Component implementing IPoseSource (e.g. ThreePointIKPoseSource).")]
    public MonoBehaviour poseSourceBehaviour;

    [Header("Rig targets (driven each frame)")]
    public Transform headTarget;
    public Transform leftHandTarget;
    public Transform rightHandTarget;

    IPoseSource poseSource;

    void Awake()
    {
        poseSource = poseSourceBehaviour as IPoseSource;
        if (poseSource == null)
            Debug.LogError("AvatarRigDriver: poseSourceBehaviour does not implement IPoseSource.");
    }

    // LateUpdate so controller/HMD poses for this frame are already applied.
    void LateUpdate()
    {
        if (poseSource == null || !poseSource.IsActive) return;
        if (headTarget == null || leftHandTarget == null || rightHandTarget == null) return;
        poseSource.UpdateTargets(headTarget, leftHandTarget, rightHandTarget);
    }
}
```

- [ ] **Step 4: Compile and check**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
AssetDatabase.Refresh();
UnityEngine.Debug.Log("Avatar IK scripts compile requested.");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: no compile errors.

- [ ] **Step 5: Place the Humanoid avatar under the rig**

The desktop body mesh comes from the StarterAssets `PlayerArmature` (Humanoid). Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.XR.CoreUtils;

var scene = EditorSceneManager.OpenScene("Assets/Scenes/GrandStaircase.unity");
var origin = Object.FindAnyObjectByType<XROrigin>();
var armature = AssetDatabase.LoadAssetAtPath<GameObject>(
    "Assets/StarterAssets/ThirdPersonController/Prefabs/PlayerArmature.prefab");
if (origin == null || armature == null) { Debug.LogError("Missing XR Origin or PlayerArmature."); }
else
{
    var avatar = (GameObject)PrefabUtility.InstantiatePrefab(armature);
    avatar.name = "MirrorAvatar";
    avatar.transform.SetParent(origin.transform, false);
    avatar.transform.localPosition = Vector3.zero;
    // Strip desktop gameplay components that would fight the IK/rig.
    foreach (var c in avatar.GetComponentsInChildren<MonoBehaviour>(true))
    {
        var n = c.GetType().Name;
        if (n == "ThirdPersonController" || n == "BasicRigidBodyPush" || n.Contains("StarterAssetsInputs") || n.Contains("PlayerInput"))
            Object.DestroyImmediate(c);
    }
    var cc = avatar.GetComponent<CharacterController>(); if (cc != null) Object.DestroyImmediate(cc);
    var anim = avatar.GetComponentInChildren<Animator>();
    Debug.Log($"MirrorAvatar placed. Animator humanoid: {(anim!=null && anim.isHuman)}");
    EditorSceneManager.SaveScene(scene);
}
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: "Animator humanoid: True" (Humanoid rig confirmed — required for IK + future Jack/Rose swap).

- [ ] **Step 6: Put the avatar's renderers on the PlayerBody layer (layer 8) so it's mirror-only**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var scene = EditorSceneManager.GetActiveScene();
var avatar = GameObject.Find("MirrorAvatar");
int layer = 8; // PlayerBody (excluded from the player camera, included in the reflection cam)
foreach (var t in avatar.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
EditorSceneManager.SaveScene(scene);
Debug.Log("MirrorAvatar set to PlayerBody layer 8.");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Also confirm in the editor that the **XR Main Camera's Culling Mask excludes layer 8** (so the player never sees their own body directly), matching the predecessor rig's behavior.

- [ ] **Step 7: Add IK target transforms + Animation Rigging constraints (manual editor step)**

Animation Rigging constraints are far more reliable to author in the editor than via script. On `MirrorAvatar`:

1. Add a `Rig Builder` to the avatar root; create a child `Rig` GameObject and add it to the Rig Builder's layers.
2. Under `Rig`, create empty target transforms: `HeadTarget`, `LeftHandTarget`, `RightHandTarget`, plus `LeftElbowHint`, `RightElbowHint`.
3. Add a `Multi-Parent Constraint` (or `Multi-Aim` + `Multi-Position` for the head) so the **Head** bone follows `HeadTarget`.
4. Add a `Two Bone IK Constraint` for each arm: Root=upper arm, Mid=forearm, Tip=hand; Target=the hand target; Hint=the elbow hint. Repeat for both arms.
5. Optionally add a `Multi-Aim`/spine constraint so the chest leans toward the head for a natural torso.
6. Place the elbow hints slightly behind/outward from each elbow so elbows bend naturally.

- [ ] **Step 8: Add `ThreePointIKPoseSource` + `AvatarRigDriver` and wire everything**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.XR.CoreUtils;

var scene = EditorSceneManager.GetActiveScene();
var origin = Object.FindAnyObjectByType<XROrigin>();
var avatar = GameObject.Find("MirrorAvatar");

var pose = avatar.GetComponent<ThreePointIKPoseSource>() ?? avatar.AddComponent<ThreePointIKPoseSource>();
pose.hmd = origin.Camera != null ? origin.Camera.transform : null;
// Controllers: find by name created by XR Origin (VR).
Transform Find(string n) { var t = origin.transform.Find(n); return t; }
var left  = GameObject.Find("LeftHand Controller");
var right = GameObject.Find("RightHand Controller");
pose.leftController  = left  != null ? left.transform  : null;
pose.rightController = right != null ? right.transform : null;

var driver = avatar.GetComponent<AvatarRigDriver>() ?? avatar.AddComponent<AvatarRigDriver>();
driver.poseSourceBehaviour = pose;
// Assign the rig targets created in Step 7 by name.
Transform FindDeep(Transform root, string name){
    foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name==name) return t; return null; }
driver.headTarget      = FindDeep(avatar.transform, "HeadTarget");
driver.leftHandTarget  = FindDeep(avatar.transform, "LeftHandTarget");
driver.rightHandTarget = FindDeep(avatar.transform, "RightHandTarget");

EditorUtility.SetDirty(pose); EditorUtility.SetDirty(driver);
EditorSceneManager.SaveScene(scene);
Debug.Log($"Wired pose source. hmd={pose.hmd!=null} L={pose.leftController!=null} R={pose.rightController!=null} " +
          $"targets head={driver.headTarget!=null} L={driver.leftHandTarget!=null} R={driver.rightHandTarget!=null}");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: all fields `True`. Any `False` → fix the referenced object name to match what XR Origin created / what you named in Step 7.

- [ ] **Step 9: Play-mode verify against the mirror (manual)**

Press Play, stand in front of the mirror. Verify in the reflection: the avatar's **head turns with your head**, **hands follow your controllers**, elbows bend naturally, and the standing body reads believably. You should **not** see the body directly in first person (layer-8 culling), only in the mirror.

Expected: head + hands track in the reflection. Tune `handPositionOffset`/`handRotationEuler` if hands are rotated/offset. If legs/torso look uncanny beyond acceptable, note it for the FinalIK upgrade path (spec Risk 2) — do not block here.

- [ ] **Step 10: Suggest commit**

```
feat: full-body IK mirror avatar (3-point PoseSource + Animation Rigging), mirror-only via layer 8
```

---

## Task 6: Stereo-correct the mirror reflection

**Files:**

- Modify: `Assets/Game/Scripts/MirrorReflection.cs`

> **This is the spec's top risk. Do it as its own task and verify in-headset before moving on.**

- [ ] **Step 1: Read the current mirror script and confirm the render path**

Read `Assets/Game/Scripts/MirrorReflection.cs` in full. Identify how it picks the reflection viewpoint (it uses `Camera.current` / the player camera position to build the reflection matrix and render the RT once per frame). Note: under Single Pass Instanced stereo, `OnWillRenderObject` fires once with a camera whose position is the **mono/center** eye, which yields a single mono reflection shared by both eyes.

- [ ] **Step 2: Verify the baseline in-headset (capture the failure mode)**

Press Play in the headset and look at the mirror. Determine whether the reflection looks **mono** (flat, no stereo depth; identical to both eyes) or correct. Record the observation. A mono reflection at the mirror's viewing distance may be acceptable — confirm with the user before investing in per-eye rendering.

- [ ] **Step 3a (if mono is acceptable): pin the reflection to the head midpoint**

Minimal change — make the reflection deterministically use the HMD center eye (already effectively the case) and document it. In `MirrorReflection.cs`, where it reads the source camera transform, prefer `Camera.current` but guard against null and skip rendering for non-game cameras (scene view) to avoid wasted passes:

```csharp
// near the top of the per-frame reflection method, after obtaining `cam`:
if (cam == null) return;
if (cam.cameraType != CameraType.Game && cam.cameraType != CameraType.VR) return;
```

Then re-test in headset; confirm the reflection is stable and correct in tone (the predecessor's `GL.invertCulling` washout fix must remain — do not reintroduce it).

- [ ] **Step 3b (if per-eye is required): render the reflection per eye**

If the user wants true stereo depth in the mirror, render the reflection twice — once from each eye's world position — into a stereo (or two) RT and sample per `unity_StereoEyeIndex` in the shader. Concretely:

1. In the reflection method, when `cam.stereoEnabled`, compute eye world positions via `cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left/Right)` (invert for world position) and build the reflection matrix per eye.
2. Render into a `RenderTexture` created with `vrUsage = VRTextureUsage.TwoEyes` (or two separate RTs), once per eye, setting `reflectionCamera.stereoTargetEye = None` and rendering each eye explicitly.
3. In `MirrorReflection.shader`, sample the correct slice using `UNITY_DECLARE_SCREENSPACE_TEXTURE` / `unity_StereoEyeIndex`.

Keep the existing U-axis flip and oblique near-clip — they are load-bearing (see the predecessor mirror memory). Do not reintroduce `GL.invertCulling = true`.

- [ ] **Step 4: Verify layer-8 culling still holds on the reflection camera**

Run `mcp__unity-mcp__Unity_RunCommand` while not in play mode to confirm the reflection camera's culling mask still **includes** layer 8 (so the avatar appears in the mirror) and the main XR camera **excludes** it. If the mirror creates its reflection camera by copying the source camera's mask, the XR camera's mask (excluding 8) would wrongly propagate — ensure the script explicitly re-includes layer 8 for the reflection render. Patch `MirrorReflection.cs` if needed so the reflection camera mask is `playerMask | (1 << 8)`.

```csharp
// where the reflection camera's cullingMask is set:
reflectionCamera.cullingMask = (cam.cullingMask | (1 << 8)) & ~(1 << 4); // ensure body shown, water skipped
```

- [ ] **Step 5: Re-test in headset (manual)**

Confirm: reflection shows the avatar (Task 5) at correct tone/contrast in both eyes, no washout, no missing body, mirror orientation correct under head yaw/pitch.

Expected: a correct self-reflection in VR. This closes the project's top technical risk.

- [ ] **Step 6: Suggest commit**

```
fix: stereo-correct mirror reflection for VR (per-eye or pinned head-midpoint)
```

---

## Task 7: Boundary warning (warn-only ~1 m cage)

**Files:**

- Create: `Assets/Game/Scripts/VR/RecenterAnchor.cs`
- Create: `Assets/Game/Scripts/VR/BoundaryWarning.cs`
- Create: `Assets/Game/Materials/BoundaryCage.mat`
- Modify: `Assets/Scenes/GrandStaircase.unity`

- [ ] **Step 1: Write `RecenterAnchor.cs`**

Create `Assets/Game/Scripts/VR/RecenterAnchor.cs`:

```csharp
using UnityEngine;

// Stores the play-space center (the spot the physical-movement boundary is measured from).
// Captured at the HMD's horizontal position on start; can be re-captured (recenter).
[DisallowMultipleComponent]
public class RecenterAnchor : MonoBehaviour
{
    public Transform hmd;
    public Vector3 Center { get; private set; }
    public bool HasCenter { get; private set; }

    void Start() { if (hmd != null) Recenter(); }

    public void Recenter()
    {
        if (hmd == null) return;
        var p = hmd.position;
        Center = new Vector3(p.x, 0f, p.z); // horizontal only
        HasCenter = true;
    }

    // Horizontal distance of the HMD from the captured center.
    public float HorizontalDistance()
    {
        if (!HasCenter || hmd == null) return 0f;
        var p = hmd.position;
        return Vector2.Distance(new Vector2(p.x, p.z), new Vector2(Center.x, Center.z));
    }
}
```

- [ ] **Step 2: Write `BoundaryWarning.cs`**

Create `Assets/Game/Scripts/VR/BoundaryWarning.cs`:

```csharp
using UnityEngine;

// Warn-only physical-movement boundary. Fades a cage in as the HMD nears `radius`
// horizontal metres from the RecenterAnchor; fades out on return. Never moves the player.
[DisallowMultipleComponent]
public class BoundaryWarning : MonoBehaviour
{
    public RecenterAnchor anchor;

    [Tooltip("Warn radius in metres from the play-space center.")]
    public float radius = 1.0f;

    [Tooltip("Distance before the radius at which the cage starts fading in.")]
    public float fadeBand = 0.3f;

    [Tooltip("Renderer(s) of the cage geometry to fade (uses material alpha on _BaseColor).")]
    public Renderer[] cageRenderers;

    [Tooltip("Optional cage root to follow the player horizontally so it surrounds them.")]
    public Transform cageRoot;

    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    MaterialPropertyBlock mpb;

    void Awake() { mpb = new MaterialPropertyBlock(); }

    void LateUpdate()
    {
        if (anchor == null || !anchor.HasCenter || cageRenderers == null) return;

        if (cageRoot != null && anchor.hmd != null)
        {
            var p = anchor.hmd.position;
            cageRoot.position = new Vector3(p.x, cageRoot.position.y, p.z);
        }

        float d = anchor.HorizontalDistance();
        float start = Mathf.Max(0f, radius - fadeBand);
        float alpha = Mathf.Clamp01((d - start) / Mathf.Max(0.0001f, radius - start));

        foreach (var r in cageRenderers)
        {
            if (r == null) continue;
            r.enabled = alpha > 0.001f;
            if (!r.enabled) continue;
            r.GetPropertyBlock(mpb);
            var c = mpb.GetColor(BaseColorId);
            if (c == default) c = Color.cyan;
            c.a = alpha;
            mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(mpb);
        }
    }
}
```

- [ ] **Step 3: Compile and check**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
AssetDatabase.Refresh();
UnityEngine.Debug.Log("Boundary scripts compile requested.");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: no compile errors.

- [ ] **Step 4: Create the transparent cage material**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;

var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
mat.name = "BoundaryCage";
// Transparent surface so alpha fade works.
mat.SetFloat("_Surface", 1f);          // 0=Opaque,1=Transparent
mat.SetFloat("_Blend", 0f);            // alpha blend
mat.renderQueue = 3000;
mat.SetColor("_BaseColor", new Color(0.2f, 0.8f, 1f, 0f));
mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
AssetDatabase.CreateAsset(mat, "Assets/Game/Materials/BoundaryCage.mat");
Debug.Log("BoundaryCage material created.");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`.

- [ ] **Step 5: Build the cage geometry + wire the components (manual + script)**

Manual: create a `BoundaryCage` GameObject (child of the scene, not the rig) with 4 vertical quad "walls" forming a ~1 m-radius box centered on the player, plus optional grid texture, all using `BoundaryCage.mat`. Set those quads' renderers disabled by default.

Then wire via `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.XR.CoreUtils;

var scene = EditorSceneManager.GetActiveScene();
var origin = Object.FindAnyObjectByType<XROrigin>();
var hmd = origin.Camera != null ? origin.Camera.transform : null;

var sys = GameObject.Find("BoundarySystem") ?? new GameObject("BoundarySystem");
var anchor = sys.GetComponent<RecenterAnchor>() ?? sys.AddComponent<RecenterAnchor>();
anchor.hmd = hmd;
var warn = sys.GetComponent<BoundaryWarning>() ?? sys.AddComponent<BoundaryWarning>();
warn.anchor = anchor;
warn.radius = 1.0f;

var cageRoot = GameObject.Find("BoundaryCage");
warn.cageRoot = cageRoot != null ? cageRoot.transform : null;
warn.cageRenderers = cageRoot != null ? cageRoot.GetComponentsInChildren<Renderer>(true) : new Renderer[0];

EditorUtility.SetDirty(anchor); EditorUtility.SetDirty(warn);
EditorSceneManager.SaveScene(scene);
Debug.Log($"Boundary wired. hmd={anchor.hmd!=null} cageRenderers={warn.cageRenderers.Length}");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: hmd True, cageRenderers > 0.

- [ ] **Step 6: Play-mode verify (manual)**

Press Play. Physically step ~1 m from your start position — confirm the cage fades in and surrounds you, and **does not** stop or move your in-game position (warn-only). Step back — confirm it fades out. Confirm it triggers _before_ the Quest's own Guardian.

Expected: smooth fade in/out at ~1 m, no positional clamping.

- [ ] **Step 7: Suggest commit**

```
feat: warn-only ~1m physical-movement boundary cage
```

---

## Task 8: Final integration & success criteria

**Files:** none new — verification and tuning.

- [ ] **Step 1: Full play-mode walkthrough (manual, in-headset)**

Verify each spec success criterion:

- [ ] Head turn → view turns **and** reflected avatar's head turns.
- [ ] Controllers → avatar hands track **and** reflect.
- [ ] Smooth move works; snap↔smooth turn toggle works via the panel; walls block movement.
- [ ] Boundary cage fades in ~1 m from center, fades out on return, never clamps.
- [ ] Mirror shows a correct full-body reflection in both eyes at full quality (no washout, correct orientation).
- [ ] Comfortable framerate over Link; no critical nausea issues.

- [ ] **Step 2: Console check**

Run `mcp__unity-mcp__Unity_GetConsoleLogs`. Resolve any errors/persistent warnings. Common: missing Input Action assignment (re-check Task 3 Step 2); MaterialPropertyBlock color default (cage starts invisible — expected).

- [ ] **Step 3: Save scene + capture record**

```csharp
using UnityEditor.SceneManagement;
EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
```

Then `mcp__unity-mcp__Unity_SceneView_CaptureMultiAngleSceneView` for a visual record. (Headset stereo can't be captured via MCP; rely on the manual in-headset walkthrough for the mirror.)

- [ ] **Step 4: Suggest final commit**

```
feat: Titanic VR conversion — PCVR rig, IK mirror avatar, boundary, comfort settings
```

---

## Notes on robustness / fallbacks

- **XRI API drift:** if any XRI type/namespace fails to compile, check the installed package version's API and adjust `using` directives and component names. The custom scripts (`ComfortSettings`, `PoseSource`, `AvatarRigDriver`, boundary) only depend on XRI for the two turn-provider types in `ComfortSettings`.
- **Mirror still mono after Task 6:** if per-eye rendering is too costly or buggy, the head-midpoint mono reflection (Task 6 Step 3a) is an acceptable fallback at the mirror's viewing distance — confirm with the user.
- **IK uncanny:** the FinalIK (VRIK) upgrade is a contained swap of the `IPoseSource` implementation + rig — the rest of the architecture is unaffected.
- **No headset at authoring time:** all play-mode steps are explicitly manual; defer them to the lab PC and verify the non-play-mode (compile + console + scene) steps meanwhile.
- **Course/provider:** if the instructor requires the Oculus provider instead of OpenXR, swap it in XR Plug-in Management (Task 2 Step 1) — the rest of the plan is unaffected.

---

## Self-review

**Spec coverage:**

- Deployment PCVR / OpenXR + XRI → Tasks 1–2 ✓
- Smooth move + snap/smooth turn toggle, no teleport/jump → Tasks 3, 4 ✓
- Warn-only ~1 m boundary → Task 7 ✓
- Full-body IK avatar, Humanoid, swappable PoseSource → Task 5 ✓
- Animation Rigging default, FinalIK upgrade path → Task 5 + fallbacks ✓
- Mirror kept + stereo validation (top risk) → Task 6 ✓
- Layer-8 mirror-only body → Task 5 Step 6 + Task 6 Step 4 ✓
- Comfort vignette (optional tuning lever) → noted in spec; not a required task (YAGNI unless nausea observed) ✓
- Out of scope (mocap, Jack/Rose, grabbing, jump, passthrough, Android) → respected; mocap seam preserved via `IPoseSource` ✓

**Placeholder scan:** No "TBD"/"implement later". Manual editor steps (XR settings, Animation Rigging constraints, world-space UI, cage geometry) are spelled out because those Unity workflows are not reliably scriptable; each is followed by a scripted verification.

**Type consistency:** `IPoseSource.UpdateTargets(head,left,right)` defined in Task 5 Step 1 and called identically in `ThreePointIKPoseSource` (Step 2) and `AvatarRigDriver` (Step 3). `ComfortSettings.TurnStyle`/`UseSnap`/`UseSmooth` defined in Task 4 Step 1 and used in `ComfortSettingsUI` (Step 2). `RecenterAnchor.Center/HasCenter/HorizontalDistance()` defined in Task 7 Step 1 and consumed in `BoundaryWarning` (Step 2). ✓
