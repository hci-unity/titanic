# Jack & Rose Swappable Mirror Avatars — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the VR player become Jack or Rose in the mirror — swappable at runtime via a controller button — using the Meshy-made Humanoid characters, driven by the existing IK/mirror rig, and walking while the player moves.

**Architecture:** One self-contained GameObject per character (Default / Jack / Rose), each a layer-8 Humanoid with its own `AvatarRigDriver` (carrying that rig's calibrated wrist offsets). A shared `ThreePointIKPoseSource` (HMD + controllers) and a new `MirrorCharacterSwitcher` live on an always-active parent container; the switcher enables exactly one character and cycles on a button. A new `LocomotionAnimatorDriver` feeds player speed into each character's locomotion blend tree.

**Tech Stack:** Unity 6000.4 / URP, XR Interaction Toolkit (Input System), Humanoid Mecanim + `OnAnimatorIK`, the existing `MirrorReflection` stereo mirror. All Unity edits are applied through the Unity MCP (`Unity_RunCommand`); there is no terminal C# build.

**Testing approach (Unity reality):** This project has no NUnit harness and its behavior is inherently scene/headset-bound. So: pure helper functions are verified by **`Unity_RunCommand` assertion scripts** (compile + run C# + throw on mismatch — a real automated check here); scene/IK/visual behavior is verified by **edit-mode render-to-PNG** and **in-headset play**. Each task states its exact verification.

**Conventions used below:**

- "RunCommand: «title»" means call `mcp__unity-mcp__Unity_RunCommand` with a `CommandScript : IRunCommand` containing the shown body.
- Scene = `Assets/Scenes/GrandStaircase.unity` (already open). Scene edits require **edit mode** (not Play); runtime field reads/calibration happen in Play.
- Git: the user commits manually (strict no-commit rule). "Commit" steps below are **suggested messages to hand the user**, never run by the worker.

---

## File Structure

- Create: `Assets/Game/Scripts/VR/MirrorCharacterSwitcher.cs` — owns "which character is live"; enables one, disables the rest; cycles on a controller button. Pure index math in a static helper.
- Create: `Assets/Game/Scripts/VR/LocomotionAnimatorDriver.cs` — feeds horizontal player speed into an Animator `Speed` parameter. Pure speed math in a static helper.
- Modify (scene, via MCP): `Assets/Scenes/GrandStaircase.unity` — add `MirrorAvatars` container, move the shared pose source up, add Jack (and later Rose) character roots, wire the switcher.
- Use as-is: `Assets/Game/Scripts/VR/AvatarRigDriver.cs`, `Assets/Game/Scripts/VR/ThreePointIKPoseSource.cs`, `Assets/Game/Scripts/VR/IPoseSource.cs` (no code changes — they already work on any Humanoid).
- Assets already present: `Assets/Game/Characters/Jack/Jack.fbx` (+ `Jack_Mat.mat`, 6 PBR PNGs). Rose's rigged FBX is produced in Task 7.
- Reuse: the `StarterAssetsThirdPerson` AnimatorController (locomotion blend tree on `Speed` + **IK Pass enabled**) — assigned to each character's Animator.

---

## Task 1: Create the two runtime scripts

**Files:**

- Create: `Assets/Game/Scripts/VR/MirrorCharacterSwitcher.cs`
- Create: `Assets/Game/Scripts/VR/LocomotionAnimatorDriver.cs`

- [ ] **Step 1: Write `MirrorCharacterSwitcher.cs`**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Enables exactly one mirror-avatar character at a time and cycles on a controller button.
// Each entry in `characters` is a layer-8 Humanoid avatar root with its own AvatarRigDriver,
// pre-wired (in the scene) to the shared ThreePointIKPoseSource. Adding a character = add its
// root to `characters` in the inspector. No other code changes needed.
[DisallowMultipleComponent]
public class MirrorCharacterSwitcher : MonoBehaviour
{
    [Tooltip("Character roots. Index 0 is active on Start.")]
    public List<GameObject> characters = new List<GameObject>();

    [Tooltip("Input System binding that advances to the next character.")]
    public string nextBinding = "<XRController>{RightHand}/primaryButton";

    InputAction nextAction;
    int current = -1;

    // Pure, testable index wrap (handles negative + overflow).
    public static int WrapIndex(int index, int count)
    {
        if (count <= 0) return 0;
        return ((index % count) + count) % count;
    }

    void Awake()
    {
        nextAction = new InputAction("NextCharacter", InputActionType.Button, nextBinding);
    }

    void OnEnable() { nextAction?.Enable(); }
    void OnDisable() { nextAction?.Disable(); }

    void Start() { SetCharacter(0); }

    void Update()
    {
        if (nextAction != null && nextAction.WasPressedThisFrame()) Next();
    }

    public void Next()
    {
        if (characters.Count == 0) return;
        SetCharacter(current + 1);
    }

    public void SetCharacter(int index)
    {
        if (characters.Count == 0) return;
        current = WrapIndex(index, characters.Count);
        for (int i = 0; i < characters.Count; i++)
            if (characters[i] != null) characters[i].SetActive(i == current);
    }

    public int CurrentIndex => current;
}
```

- [ ] **Step 2: Write `LocomotionAnimatorDriver.cs`**

```csharp
using UnityEngine;

// Feeds the player's horizontal movement speed into the avatar Animator's locomotion parameter,
// so the mirror reflection walks/idles with the player. Hands + head stay IK-driven on top
// (IK pass + AvatarRigDriver.LateUpdate). Root motion stays OFF, so the walk plays in place
// while AvatarRigDriver positions the body under the HMD.
[DisallowMultipleComponent]
public class LocomotionAnimatorDriver : MonoBehaviour
{
    [Tooltip("Player movement source (the XR Origin CharacterController). Auto-found on parents if null.")]
    public CharacterController source;
    [Tooltip("Animator with a locomotion blend tree. Auto-found in children if null.")]
    public Animator animator;
    [Tooltip("Float parameter driven by horizontal speed (StarterAssetsThirdPerson uses 'Speed').")]
    public string speedParameter = "Speed";
    [Tooltip("Smoothing time for the speed value (seconds).")]
    public float damp = 0.12f;

    int speedHash;
    float speed;

    // Pure, testable: horizontal magnitude of a velocity.
    public static float HorizontalSpeed(Vector3 velocity)
    {
        velocity.y = 0f;
        return velocity.magnitude;
    }

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (source == null) source = GetComponentInParent<CharacterController>();
        speedHash = Animator.StringToHash(speedParameter);
    }

    void Update()
    {
        if (animator == null || source == null) return;
        float target = HorizontalSpeed(source.velocity);
        speed = Mathf.Lerp(speed, target, damp > 0f ? Time.deltaTime / damp : 1f);
        animator.SetFloat(speedHash, speed);
    }
}
```

- [ ] **Step 3: Verify compile + pure helpers (RunCommand: "Verify switcher/locomotion helpers")**

```csharp
using UnityEngine;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        // WrapIndex
        Check(MirrorCharacterSwitcher.WrapIndex(0, 3) == 0, "wrap 0/3");
        Check(MirrorCharacterSwitcher.WrapIndex(3, 3) == 0, "wrap 3/3");
        Check(MirrorCharacterSwitcher.WrapIndex(-1, 3) == 2, "wrap -1/3");
        Check(MirrorCharacterSwitcher.WrapIndex(4, 3) == 1, "wrap 4/3");
        Check(MirrorCharacterSwitcher.WrapIndex(5, 0) == 0, "wrap count0");
        // HorizontalSpeed
        Check(Mathf.Abs(LocomotionAnimatorDriver.HorizontalSpeed(new Vector3(3, 99, 4)) - 5f) < 1e-4f, "hspeed 3-4-5");
        Check(LocomotionAnimatorDriver.HorizontalSpeed(new Vector3(0, 9, 0)) == 0f, "hspeed vertical-only");
        result.Log("ALL HELPER ASSERTIONS PASSED");
    }
    void Check(bool ok, string n){ if(!ok) throw new System.Exception("FAILED: " + n); }
}
```

Expected: compilation success + log `ALL HELPER ASSERTIONS PASSED`. If compile fails, fix the script named in the error before continuing.

- [ ] **Step 4: Suggested commit (hand to user)**

```
feat(vr): MirrorCharacterSwitcher + LocomotionAnimatorDriver scripts
```

---

## Task 2: Scene restructure — shared pose source + switcher container

Goal: move the shared `ThreePointIKPoseSource` onto an always-active parent so it keeps running when individual characters are disabled, and host the switcher there. The existing `MirrorAvatar` becomes the "Default" character.

**Files:** Scene `GrandStaircase` (via MCP). No script changes.

- [ ] **Step 1: Snapshot current state (RunCommand: "Snapshot avatar wiring")**

```csharp
using UnityEngine; using System.Linq; using System.Text;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var sb = new StringBuilder();
        var ma = GameObject.Find("XR Origin (XR Rig)/MirrorAvatar");
        sb.AppendLine("MirrorAvatar found=" + (ma != null) + " layer=" + (ma ? ma.layer : -1));
        var ps = Object.FindObjectsByType<ThreePointIKPoseSource>(FindObjectsSortMode.None).Where(x=>x!=null).ToArray();
        sb.AppendLine("ThreePointIKPoseSource count=" + ps.Length);
        var rd = Object.FindObjectsByType<AvatarRigDriver>(FindObjectsSortMode.None).Where(x=>x!=null).ToArray();
        sb.AppendLine("AvatarRigDriver count=" + rd.Length);
        result.Log(sb.ToString());
    }
}
```

Expected: MirrorAvatar found=True layer=8; one pose source; one driver.

- [ ] **Step 2: Create `MirrorAvatars` container, relocate the shared pose source, reparent Default (RunCommand: "Build MirrorAvatars container")**

```csharp
using UnityEngine; using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var rig = GameObject.Find("XR Origin (XR Rig)");
        var mirrorAvatar = GameObject.Find("XR Origin (XR Rig)/MirrorAvatar");
        var oldPose = mirrorAvatar.GetComponent<ThreePointIKPoseSource>();

        // 1. Container (always active), parented under the rig at the rig origin.
        var container = new GameObject("MirrorAvatars");
        Undo.RegisterCreatedObjectUndo(container, "create MirrorAvatars");
        container.transform.SetParent(rig.transform, false);

        // 2. Move the shared pose source onto the container (copy field values, then strip the old one).
        var newPose = container.AddComponent<ThreePointIKPoseSource>();
        newPose.hmd = oldPose.hmd;
        newPose.leftController = oldPose.leftController;
        newPose.rightController = oldPose.rightController;
        newPose.handPositionOffset = oldPose.handPositionOffset;
        newPose.handRotationEuler = oldPose.handRotationEuler;
        Object.DestroyImmediate(oldPose);

        // 3. Repoint the Default avatar's driver at the shared pose source and reparent it.
        var driver = mirrorAvatar.GetComponent<AvatarRigDriver>();
        driver.poseSourceBehaviour = newPose;
        mirrorAvatar.transform.SetParent(container.transform, true);
        mirrorAvatar.name = "Default";

        // 4. Add the switcher to the container (characters list filled in Task 6).
        container.AddComponent<MirrorCharacterSwitcher>();

        EditorUtility.SetDirty(container);
        EditorSceneManagerMarkAllDirty();
        result.Log("Container built. Default driver.poseSource=" + (driver.poseSourceBehaviour != null) +
                   " hmd=" + (newPose.hmd ? newPose.hmd.name : "null"));
    }
    static void EditorSceneManagerMarkAllDirty() =>
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
}
```

Expected log: `Default driver.poseSource=True hmd=Main Camera`.

- [ ] **Step 3: Save the scene (RunCommand: "Save scene")**

```csharp
using UnityEditor; using UnityEditor.SceneManagement;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        result.Log("scene saved");
    }
}
```

- [ ] **Step 4: Play-verify the Default avatar still works.** Enter Play (headset or editor), confirm the Default avatar still tracks head/hands in the mirror and no NRE in console (RunCommand: `Unity_GetConsoleLogs`). Expected: reflection unchanged from before the restructure.

- [ ] **Step 5: Suggested commit (hand to user)**

```
refactor(vr): shared pose source + MirrorAvatars container + switcher host
```

---

## Task 3: Add Jack as a layer-8 mirror-avatar character

**Files:** Scene (via MCP); uses `Assets/Game/Characters/Jack/Jack.fbx` + `Jack_Mat.mat`.

- [ ] **Step 1: Instantiate Jack under the container, layer 8, textured, with a driver (RunCommand: "Add Jack character")**

```csharp
using UnityEngine; using UnityEditor;
internal class CommandScript : IRunCommand
{
    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
    }
    public void Execute(ExecutionResult result)
    {
        var container = GameObject.Find("XR Origin (XR Rig)/MirrorAvatars");
        var pose = container.GetComponent<ThreePointIKPoseSource>();
        var fbx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Characters/Jack/Jack.fbx");
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Characters/Jack/Jack_Mat.mat");
        var ctrl = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
            "Assets/StarterAssets/ThirdPersonController/Character/StarterAssetsThirdPerson.controller");

        var jack = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
        Undo.RegisterCreatedObjectUndo(jack, "add Jack");
        jack.name = "Jack";
        jack.transform.SetParent(container.transform, false);
        jack.transform.localPosition = Vector3.zero;
        jack.transform.localScale = Vector3.one * 0.85f;     // ~2.11m -> ~1.79m; tuned in Step 3
        SetLayerRecursive(jack, 8);                          // mirror-only

        foreach (var smr in jack.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            smr.sharedMaterial = mat;
            smr.updateWhenOffscreen = true;                  // avoid frustum-cull in the reflection cam
        }

        var anim = jack.GetComponentInChildren<Animator>();
        anim.runtimeAnimatorController = ctrl;               // locomotion blend tree + IK pass
        anim.applyRootMotion = false;

        var driver = jack.AddComponent<AvatarRigDriver>();
        driver.poseSourceBehaviour = pose;
        driver.handWeight = 1f; driver.handRotationWeight = 1f;
        driver.driveHead = true; driver.followHmdHorizontal = true;
        driver.elbowHintWeight = 1f; driver.elbowOut = 0.45f; driver.elbowBack = 0.30f; driver.elbowDown = 0.20f;
        // Wrist offsets are PER-RIG and unknown yet -> calibrated in Task 5. Leave zero for now.
        driver.leftHandOffsetEuler = Vector3.zero;
        driver.rightHandOffsetEuler = Vector3.zero;

        jack.SetActive(false);                               // switcher will manage activation

        EditorUtility.SetDirty(jack);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(jack.scene);
        result.Log("Jack added. ctrl=" + (anim.runtimeAnimatorController != null) +
                   " isHuman=" + anim.isHuman + " driver.pose=" + (driver.poseSourceBehaviour != null));
    }
}
```

Expected: `Jack added. ctrl=True isHuman=True driver.pose=True`. (If the StarterAssets controller path differs, find it: RunCommand logging `AssetDatabase.FindAssets("StarterAssetsThirdPerson t:AnimatorController")`.)

- [ ] **Step 2: Confirm IK Pass is enabled on the controller (RunCommand: "Check IK pass")**

```csharp
using UnityEngine; using UnityEditor; using UnityEditor.Animations;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(
            "Assets/StarterAssets/ThirdPersonController/Character/StarterAssetsThirdPerson.controller");
        var sb = new System.Text.StringBuilder();
        foreach (var l in c.layers) sb.AppendLine("layer '" + l.name + "' ikPass=" + l.ikPass);
        result.Log(sb.ToString());
    }
}
```

Expected: base layer `ikPass=True`. If any needed layer is False, set `layers[i].ikPass = true` and re-save the controller (note: this affects the Default avatar too, which is fine — it already relies on IK pass).

- [ ] **Step 3: Tune Jack's height (play-verify).** Enter Play; with the switcher about to be wired (Task 6) you can temporarily `jack.SetActive(true)` via RunCommand. Compare Jack's `Head` bone world Y to `Main Camera` world Y. Adjust `jack.transform.localScale` uniformly until they match within ~5 cm. RunCommand to read both:

```csharp
using UnityEngine;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var jack = GameObject.Find("XR Origin (XR Rig)/MirrorAvatars/Jack");
        var anim = jack.GetComponentInChildren<Animator>();
        var head = anim.GetBoneTransform(HumanBodyBones.Head);
        var cam = Camera.main;
        result.Log("headY=" + head.position.y + " camY=" + cam.transform.position.y + " scale=" + jack.transform.localScale.y);
    }
}
```

Expected: tune scale so `headY ≈ camY`. Record the final scale.

- [ ] **Step 4: Suggested commit (hand to user)**

```
feat(vr): add Jack as a layer-8 Humanoid mirror avatar
```

---

## Task 4: Locomotion — make characters walk with the player

**Files:** Scene (via MCP); uses `LocomotionAnimatorDriver` from Task 1.

- [ ] **Step 1: Add `LocomotionAnimatorDriver` to Default and Jack (RunCommand: "Wire locomotion")**

```csharp
using UnityEngine; using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var cc = GameObject.Find("XR Origin (XR Rig)").GetComponent<CharacterController>();
        string[] names = { "Default", "Jack" };
        foreach (var n in names)
        {
            var go = GameObject.Find("XR Origin (XR Rig)/MirrorAvatars/" + n);
            if (go == null) { result.LogWarning("missing " + n); continue; }
            var loco = go.GetComponent<LocomotionAnimatorDriver>();
            if (loco == null) loco = go.AddComponent<LocomotionAnimatorDriver>();
            loco.source = cc;
            loco.animator = go.GetComponentInChildren<Animator>();
            loco.speedParameter = "Speed";
            EditorUtility.SetDirty(go);
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        result.Log("locomotion wired; cc found=" + (cc != null));
    }
}
```

Expected: `cc found=True`. (If the CharacterController is on a different object, find it with `GetComponentInChildren<CharacterController>()` from the rig.)

- [ ] **Step 2: Play-verify walk.** Enter Play; with Jack active, move with the left stick. Expected: the reflection's legs walk while moving and return to idle when still; hands still track the controllers (IK overrides arms); no foot-sliding artifacts beyond normal in-place walk. Confirm `Speed` rises with motion (RunCommand reading `animator.GetFloat("Speed")` during Play).

- [ ] **Step 3: Suggested commit (hand to user)**

```
feat(vr): drive mirror-avatar locomotion from player movement speed
```

---

## Task 5: Calibrate Jack's wrist offsets (per-rig)

The wrist offsets are constants specific to Jack's rig (controller→hand-bone). Use the proven deterministic "flashlight-pose" method (no eyeballing). **Requires the user in-headset.**

**Files:** writes `leftHandOffsetEuler`/`rightHandOffsetEuler` on Jack's `AvatarRigDriver`.

- [ ] **Step 1: Enter Play with Jack active.** Ask the user to hold the calibration pose: arms extended forward, controllers pointing forward, **thumbs up, palms inward**, looking straight ahead. Hold steady.

- [ ] **Step 2: Derive + apply offsets (RunCommand: "Calibrate Jack wrists", run during Play while the user holds the pose)**

```csharp
using UnityEngine;
internal class CommandScript : IRunCommand
{
    static Quaternion AnatFromBone(Animator a, bool left)
    {
        var hand = a.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        var mid  = a.GetBoneTransform(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
        var thumb= a.GetBoneTransform(left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
        Vector3 fingers = (mid ? (mid.position - hand.position) : hand.forward).normalized;
        Vector3 up = (thumb ? (thumb.position - hand.position) : hand.up).normalized;
        return Quaternion.LookRotation(fingers, up);
    }
    public void Execute(ExecutionResult result)
    {
        var jack = GameObject.Find("XR Origin (XR Rig)/MirrorAvatars/Jack");
        var anim = jack.GetComponentInChildren<Animator>();
        var driver = jack.GetComponent<AvatarRigDriver>();
        var cam = Camera.main;
        var lc = GameObject.Find("Left Controller").transform;
        var rc = GameObject.Find("Right Controller").transform;
        Vector3 fwd = cam.transform.forward; fwd.y = 0; fwd.Normalize();
        Quaternion tgt = Quaternion.LookRotation(fwd, Vector3.up);

        Vector3 Calc(bool left, Transform ctrl, Transform handBone)
        {
            Quaternion curAnat = AnatFromBone(anim, left);
            Quaternion desiredBone = tgt * Quaternion.Inverse(curAnat) * handBone.rotation;
            Quaternion offset = Quaternion.Inverse(ctrl.rotation) * desiredBone;
            return offset.eulerAngles;
        }
        var lh = anim.GetBoneTransform(HumanBodyBones.LeftHand);
        var rh = anim.GetBoneTransform(HumanBodyBones.RightHand);
        driver.leftHandOffsetEuler = Calc(true, lc, lh);
        driver.rightHandOffsetEuler = Calc(false, rc, rh);
        result.Log("LEFT=" + driver.leftHandOffsetEuler + "  RIGHT=" + driver.rightHandOffsetEuler);
    }
}
```

Expected: two euler triples logged; in-headset the wrists now follow the controllers naturally. **Record both triples** — Play-mode field sets do not persist.

- [ ] **Step 3: Bake the values in edit mode.** Exit Play. RunCommand setting `driver.leftHandOffsetEuler`/`rightHandOffsetEuler` to the recorded triples, `EditorUtility.SetDirty`, `MarkSceneDirty`, save scene. Verify by re-reading the serialized values (RunCommand) match the recorded triples.

- [ ] **Step 4: Play-verify wrists.** Re-enter Play; confirm wrists track naturally and the numeric check `fingersDir·forward ≈ 1.0` while the user re-holds the flashlight pose (optional sanity RunCommand).

- [ ] **Step 5: Suggested commit (hand to user)**

```
feat(vr): calibrate + bake Jack wrist offsets
```

---

## Task 6: Register characters in the switcher + button cycling

**Files:** Scene (via MCP); `MirrorCharacterSwitcher` from Task 1.

- [ ] **Step 1: Populate the switcher's character list (RunCommand: "Register characters")**

```csharp
using UnityEngine; using UnityEditor; using System.Collections.Generic;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var container = GameObject.Find("XR Origin (XR Rig)/MirrorAvatars");
        var sw = container.GetComponent<MirrorCharacterSwitcher>();
        var list = new List<GameObject>();
        foreach (var n in new[] { "Default", "Jack" })   // Rose appended in Task 7
        {
            var t = container.transform.Find(n);
            if (t != null) list.Add(t.gameObject);
        }
        sw.characters = list;
        sw.nextBinding = "<XRController>{RightHand}/primaryButton";
        EditorUtility.SetDirty(sw);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(container.scene);
        result.Log("registered: " + string.Join(",", list.ConvertAll(g => g.name)));
    }
}
```

Expected: `registered: Default,Jack`. Save the scene.

- [ ] **Step 2: Play-verify swapping.** Enter Play. On Start, only `Default` is active (index 0). Press the right-hand primary button (A) → `Jack` becomes active, `Default` disabled; press again → wraps back to `Default`. Confirm via RunCommand reading `sw.CurrentIndex` and each child's `activeSelf`. Expected: exactly one active at a time; reflection swaps between the original avatar and Jack.

- [ ] **Step 3: Suggested commit (hand to user)**

```
feat(vr): runtime character swap on controller button
```

---

## Task 7: Produce Rose and add her as a character

Repeat the validated Meshy pipeline for Rose, then the same Unity wiring as Jack.

**Files:** `Assets/Game/Characters/Rose/` (rigged FBX + maps); Scene (via MCP).

- [ ] **Step 1: (User, Meshy web UI)** Re-rig Rose to **Humanoid** and download the **rigged** export: _Rigged Character ON, Animation OFF, With Skin, FBX_. Also have the textured export's PBR PNGs. (The earlier `Assets/Game/Characters/Rose/` files were the UNRIGGED mesh — they will be replaced.)

- [ ] **Step 2: Place files.** Remove the old unrigged Rose files; put the rigged FBX as `Assets/Game/Characters/Rose/Rose.fbx` and the 6 PBR PNGs alongside (bash `unzip`/`cp` as in the Jack import; PowerShell is blocked, plain `bash`/`curl`/`unzip` are allowed).

- [ ] **Step 3: Import as Humanoid + verify skeleton (RunCommand: "Import Rose Humanoid").** Same as the Jack import check: set `animationType = Human`, `avatarSetup = CreateFromThisModel`, `importAnimation = false`, leave `optimizeGameObjects` false; reimport; assert `Avatar.isHuman == true` and a SkinnedMeshRenderer with bones > 0. Expected: valid Humanoid (Rose shares the same Meshy bone naming as Jack).

- [ ] **Step 4: Build `Rose_Mat.mat`** (URP/Lit, base + normal, metallic 0, smoothness 0.3) and set the `_normal` PNG to Normal map type — same script as Jack's material step, with Rose paths.

- [ ] **Step 5: Add Rose character** — same as Task 3 Step 1 with `Rose` paths/name, parented under `MirrorAvatars`, layer 8, controller assigned, `AvatarRigDriver` added, `SetActive(false)`. Tune her scale (Task 3 Step 3) — Rose's height may differ from Jack's.

- [ ] **Step 6: Add `LocomotionAnimatorDriver` to Rose** (Task 4 Step 1, name `Rose`).

- [ ] **Step 7: Calibrate Rose's wrists** (Task 5) — her offsets are a different rig's constants; derive + bake separately.

- [ ] **Step 8: Append Rose to the switcher list** — re-run Task 6 Step 1 with `{ "Default", "Jack", "Rose" }`. Save scene.

- [ ] **Step 9: Suggested commit (hand to user)**

```
feat(vr): add Rose mirror avatar + register in switcher
```

---

## Task 8: Final in-headset verification

**Files:** none (verification only).

- [ ] **Step 1: Swap cycle.** In-headset, press the button to cycle Default → Jack → Rose → Default. Exactly one visible in the mirror each time; no flicker; console clean (`Unity_GetConsoleLogs`).

- [ ] **Step 2: Deformation live (criterion 2 in-headset).** As Jack and as Rose: raise arms, rotate wrists, turn head, reach forward. Confirm shoulders/elbows/wrists deform cleanly (matches the scripted pre-check) and the reflection's head/hands track 1:1.

- [ ] **Step 3: Locomotion.** Walk around as each character; the reflection walks, idles when still; loose costume (Rose's coat/skirt) deforms acceptably under leg motion (note any issue for a follow-up weight tweak).

- [ ] **Step 4: Scale/eye-line.** Each character's eye line sits at the player's height (no giant/dwarf reflection).

- [ ] **Step 5: Suggested commit (hand to user)**

```
feat(vr): Jack/Rose swappable mirror avatars — verified in headset
```

---

## Notes for the worker

- **Never run git.** Surface suggested commit messages; the user commits.
- **PowerShell is denied; plain `bash`/`curl`/`unzip` are allowed** for file ops (Downloads → `Assets/...`). Meshy web-UI assets are NOT visible to the Meshy API — they must be downloaded by the user.
- **Edit vs Play:** scene edits + `SaveScene` only in edit mode; calibration + behavior checks in Play. Play-mode field writes do NOT persist — always bake into serialized values in edit mode afterward.
- **`optimizeGameObjects` must stay false** on every character FBX, or `AvatarRigDriver`'s `LateUpdate` head/wrist bone-writes break (bone transforms get stripped).
- **Layers:** every character + all children on **layer 8** (mirror-only). The XR `Main Camera` already excludes layer 8; the mirror's `reflectLayers` already excludes layer 9 (controller visuals).
- If `cam.Render()`-based preview is needed for debugging, use the edit-mode render-to-PNG recipe (temp camera + directional light at the camera + sRGB RT → `EncodeToPNG` → project root → `Read`).
