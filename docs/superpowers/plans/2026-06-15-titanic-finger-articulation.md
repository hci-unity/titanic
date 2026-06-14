# Finger Articulation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Drive JackV2's (and any fingered avatar's) mirror finger bones from Quest hand-tracking joints — full curl + splay + thumb, hand-tracking only.

**Architecture:** Extend `HandTrackingPoseSource` (the sole XR Hands reader) to expose per-hand finger joint world poses. Add a new per-avatar component `FingerPoseDriver` that, in `LateUpdate` (ordered after `AvatarRigDriver`'s wrist write), aims each humanoid finger bone so its segment points along the matching XR Hand segment (`Quaternion.FromToRotation`). No per-rig calibration; direction-copy only.

**Tech Stack:** Unity 6, C#, `com.unity.xr.hands` 1.8.0, OpenXR/XRI. Verification via Unity-MCP `Unity_RunCommand` (no test framework in project).

**Spec:** `docs/superpowers/specs/2026-06-15-titanic-finger-articulation-design.md`

---

## Project conventions (read before starting)

- **No unit-test framework.** "Tests" are `Unity_RunCommand` C# scripts that assert pure-static logic and `result.Log("PASS"/"FAIL")`. This matches the established model (see `titanic-vr-conversion` memory). The Unity TDD "fail-first" is limited because a missing method fails _compilation_ rather than asserting — so the pattern here is: implement the pure static, then run an assertion script that must print all-PASS.
- **`Unity_RunCommand` rules** (from memory): class MUST be `internal class CommandScript : IRunCommand`; use `FindFirstObjectByType<T>(FindObjectsInactive.Include)` or `Resources.FindObjectsOfTypeAll<T>()` for inactive objects; keep output small; `result.Log` does **not** support format specifiers like `{0:F2}` — pass plain `{0}`. Scene saves only work in **edit mode**.
- **Git:** the user has a strict no-auto-commit rule. Commit steps below are **checkpoints** — present the diff and let the user commit. Do **not** run `git commit`.
- **Execution order:** `AvatarRigDriver` writes head + wrist world rotation in its `LateUpdate` (no `[DefaultExecutionOrder]`, so 0). `FingerPoseDriver` must run **after** it.
- **Coordinate space:** XR Hands joint poses are relative to the **Camera Offset** object (the `trackingSpace` on `HandTrackingPoseSource`), not the XR Origin root.

## File structure

- **Modify** `Assets/Game/Scripts/VR/HandTrackingPoseSource.cs` — add `TryGetJointWorld(...)`.
- **Create** `Assets/Game/Scripts/VR/FingerPoseDriver.cs` — new component + pure static helpers (`Aim`, `ShouldDrive`, `Chains`, `Mirror`) + `FingerBoneSpec` struct.
- **Modify** `Assets/Scenes/GrandStaircase.unity` — add `FingerPoseDriver` to the `JackV2` avatar, wire `router` + `handSource`.

---

## Task 1: Expose finger joint world poses on `HandTrackingPoseSource`

**Files:**

- Modify: `Assets/Game/Scripts/VR/HandTrackingPoseSource.cs`

- [ ] **Step 1: Add the world-space joint accessor**

Add this method to the `HandTrackingPoseSource` class (e.g. just after `ReadHand`). It reuses the already-resolved `subsystem` and `trackingSpace`; returns false safely when the subsystem is absent or the joint pose is invalid.

```csharp
// Public read-only accessor: world-space pose of ANY hand joint, for the finger driver.
// Returns false if the subsystem is missing or that joint has no valid pose this frame.
// Same Camera-Offset -> world conversion the wrist uses.
public bool TryGetJointWorld(Handedness handedness, XRHandJointID id, out Pose worldPose)
{
    worldPose = default;
    if (subsystem == null) return false;
    Transform space = trackingSpace != null ? trackingSpace : transform;
    XRHand hand = handedness == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;
    if (hand.GetJoint(id).TryGetPose(out Pose p))
    {
        worldPose = new Pose(space.TransformPoint(p.position), space.rotation * p.rotation);
        return true;
    }
    return false;
}
```

- [ ] **Step 2: Verify it compiles and is null-safe in edit mode**

Run this via `Unity_RunCommand` (edit mode, not playing):

```csharp
using UnityEngine;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var src = Object.FindFirstObjectByType<HandTrackingPoseSource>(FindObjectsInactive.Include);
        if (src == null) { result.LogError("FAIL: no HandTrackingPoseSource in scene"); return; }
        bool ok = src.TryGetJointWorld(UnityEngine.XR.Hands.Handedness.Left,
            UnityEngine.XR.Hands.XRHandJointID.IndexProximal, out var pose);
        // Not playing -> subsystem null -> must return false, no exception.
        result.Log("returned={0} (expected False in edit mode). PASS if no exception.", ok);
    }
}
```

Expected: logs `returned=False`, no exception, compilation success.

- [ ] **Step 3: Checkpoint** — present the one-method diff; let the user commit (suggested message: `feat(vr): expose XR hand joint world poses on HandTrackingPoseSource`).

---

## Task 2: `FingerPoseDriver` — struct, mapping table, and pure static helpers

**Files:**

- Create: `Assets/Game/Scripts/VR/FingerPoseDriver.cs`

- [ ] **Step 1: Create the file with the struct, mapping table, and pure statics (no runtime yet)**

```csharp
using UnityEngine;
using UnityEngine.XR.Hands;

// Drives a Humanoid avatar's finger bones from Quest hand-tracking joints (hand-tracking only).
// Method: aim each finger bone so its segment (joint->child) points along the matching XR Hand
// segment's world direction -- the same direct-world-rotation idiom AvatarRigDriver uses for the
// wrist/head, extended down the finger chain. Direction-copy only, so NO per-rig calibration and
// immune to finger-length differences. Roll is left free (fingers are hinges).
//
// Runs in LateUpdate AFTER AvatarRigDriver (execution order 100 > its 0) so the hand bone -- the
// parent of these finger bones -- is already oriented.
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public partial class FingerPoseDriver : MonoBehaviour
{
    // One bone to aim. currentDir = pos(SegTo) - pos(SegFrom); targetDir = world(XrTo) - world(XrFrom).
    public readonly struct FingerBoneSpec
    {
        public readonly HumanBodyBones Aim;
        public readonly HumanBodyBones SegFrom;
        public readonly HumanBodyBones SegTo;
        public readonly XRHandJointID XrFrom;
        public readonly XRHandJointID XrTo;
        public FingerBoneSpec(HumanBodyBones aim, HumanBodyBones segFrom, HumanBodyBones segTo,
                              XRHandJointID xrFrom, XRHandJointID xrTo)
        { Aim = aim; SegFrom = segFrom; SegTo = segTo; XrFrom = xrFrom; XrTo = xrTo; }
    }

    // Left-hand chain (15 bones, root->tip per finger). Right hand reuses these via Mirror() (the
    // Humanoid finger bone enums are laid out Left 24..38, Right 39..53 -> a constant +15 offset).
    static readonly FingerBoneSpec[] LeftBase =
    {
        // Thumb (XR thumb joints: Metacarpal, Proximal, Distal, Tip -- no Intermediate)
        new FingerBoneSpec(HumanBodyBones.LeftThumbProximal,     HumanBodyBones.LeftThumbProximal,     HumanBodyBones.LeftThumbIntermediate, XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal),
        new FingerBoneSpec(HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,       XRHandJointID.ThumbProximal,   XRHandJointID.ThumbDistal),
        new FingerBoneSpec(HumanBodyBones.LeftThumbDistal,       HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,       XRHandJointID.ThumbDistal,     XRHandJointID.ThumbTip),
        // Index
        new FingerBoneSpec(HumanBodyBones.LeftIndexProximal,     HumanBodyBones.LeftIndexProximal,     HumanBodyBones.LeftIndexIntermediate, XRHandJointID.IndexProximal,     XRHandJointID.IndexIntermediate),
        new FingerBoneSpec(HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,       XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal),
        new FingerBoneSpec(HumanBodyBones.LeftIndexDistal,       HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,       XRHandJointID.IndexDistal,       XRHandJointID.IndexTip),
        // Middle
        new FingerBoneSpec(HumanBodyBones.LeftMiddleProximal,     HumanBodyBones.LeftMiddleProximal,     HumanBodyBones.LeftMiddleIntermediate, XRHandJointID.MiddleProximal,     XRHandJointID.MiddleIntermediate),
        new FingerBoneSpec(HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,       XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal),
        new FingerBoneSpec(HumanBodyBones.LeftMiddleDistal,       HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,       XRHandJointID.MiddleDistal,       XRHandJointID.MiddleTip),
        // Ring
        new FingerBoneSpec(HumanBodyBones.LeftRingProximal,     HumanBodyBones.LeftRingProximal,     HumanBodyBones.LeftRingIntermediate, XRHandJointID.RingProximal,     XRHandJointID.RingIntermediate),
        new FingerBoneSpec(HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,       XRHandJointID.RingIntermediate, XRHandJointID.RingDistal),
        new FingerBoneSpec(HumanBodyBones.LeftRingDistal,       HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,       XRHandJointID.RingDistal,       XRHandJointID.RingTip),
        // Little
        new FingerBoneSpec(HumanBodyBones.LeftLittleProximal,     HumanBodyBones.LeftLittleProximal,     HumanBodyBones.LeftLittleIntermediate, XRHandJointID.LittleProximal,     XRHandJointID.LittleIntermediate),
        new FingerBoneSpec(HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,       XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal),
        new FingerBoneSpec(HumanBodyBones.LeftLittleDistal,       HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,       XRHandJointID.LittleDistal,       XRHandJointID.LittleTip),
    };

    // Map a LEFT-hand humanoid finger bone to its RIGHT-hand equivalent (+15 in the enum).
    public static HumanBodyBones Mirror(HumanBodyBones leftFingerBone)
        => (HumanBodyBones)((int)leftFingerBone + 15);

    // The 15 specs for one hand. Left = base; Right = base with every humanoid bone mirrored
    // (XR joint IDs are hand-agnostic -- handedness is chosen by which XRHand is read).
    public static FingerBoneSpec[] Chains(bool left)
    {
        if (left) return (FingerBoneSpec[])LeftBase.Clone();
        var r = new FingerBoneSpec[LeftBase.Length];
        for (int i = 0; i < LeftBase.Length; i++)
        {
            var s = LeftBase[i];
            r[i] = new FingerBoneSpec(Mirror(s.Aim), Mirror(s.SegFrom), Mirror(s.SegTo), s.XrFrom, s.XrTo);
        }
        return r;
    }

    // Aim: rotate currentRot so the world vector currentDir is mapped onto targetDir. Degenerate
    // (near-zero) inputs leave the rotation unchanged.
    public static Quaternion Aim(Vector3 currentDir, Vector3 targetDir, Quaternion currentRot)
    {
        if (currentDir.sqrMagnitude < 1e-10f || targetDir.sqrMagnitude < 1e-10f) return currentRot;
        return Quaternion.FromToRotation(currentDir, targetDir) * currentRot;
    }

    // Gate: drive fingers only in Hands mode with this hand tracked.
    public static bool ShouldDrive(InputModeRouter.InputMode mode, bool handTracked)
        => mode == InputModeRouter.InputMode.Hands && handTracked;
}
```

Note: `public partial class` — the runtime half is added in Task 3 in the same file (kept `partial` only so this plan can present the two halves separately; the executor may instead write both halves into one non-partial class).

- [ ] **Step 2: Assert the pure statics via `Unity_RunCommand` (edit mode)**

```csharp
using UnityEngine;
using UnityEngine.XR.Hands;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        int fails = 0;
        // Aim: (1,0,0) -> (0,1,0) from identity should map +X onto +Y.
        var q = FingerPoseDriver.Aim(Vector3.right, Vector3.up, Quaternion.identity);
        float dot = Vector3.Dot(q * Vector3.right, Vector3.up);
        if (dot < 0.999f) { result.LogError("FAIL Aim dot={0}", dot); fails++; }
        // Aim degenerate: zero currentDir returns input rotation.
        var q2 = FingerPoseDriver.Aim(Vector3.zero, Vector3.up, Quaternion.identity);
        if (q2 != Quaternion.identity) { result.LogError("FAIL Aim degenerate"); fails++; }
        // Mirror: LeftIndexProximal -> RightIndexProximal.
        if (FingerPoseDriver.Mirror(HumanBodyBones.LeftIndexProximal) != HumanBodyBones.RightIndexProximal)
            { result.LogError("FAIL Mirror index"); fails++; }
        if (FingerPoseDriver.Mirror(HumanBodyBones.LeftLittleDistal) != HumanBodyBones.RightLittleDistal)
            { result.LogError("FAIL Mirror little"); fails++; }
        // Chains: 15 per hand; right chain bones are all Right*.
        var L = FingerPoseDriver.Chains(true);
        var R = FingerPoseDriver.Chains(false);
        if (L.Length != 15 || R.Length != 15) { result.LogError("FAIL chain length L={0} R={1}", L.Length, R.Length); fails++; }
        foreach (var s in R) if ((int)s.Aim < (int)HumanBodyBones.RightThumbProximal) { result.LogError("FAIL right chain has non-right bone {0}", s.Aim); fails++; break; }
        // ShouldDrive truth table.
        if (!FingerPoseDriver.ShouldDrive(InputModeRouter.InputMode.Hands, true)) { result.LogError("FAIL ShouldDrive hands+tracked"); fails++; }
        if (FingerPoseDriver.ShouldDrive(InputModeRouter.InputMode.Hands, false)) { result.LogError("FAIL ShouldDrive hands+untracked"); fails++; }
        if (FingerPoseDriver.ShouldDrive(InputModeRouter.InputMode.Controllers, true)) { result.LogError("FAIL ShouldDrive controllers"); fails++; }
        result.Log(fails == 0 ? "ALL PASS" : ("FAILS=" + fails));
    }
}
```

Expected: `ALL PASS`.

- [ ] **Step 3: Assert every chain bone + XR joint resolves on JackV2**

```csharp
using UnityEngine;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        Animator a = null;
        foreach (var d in Resources.FindObjectsOfTypeAll<AvatarRigDriver>())
            if (d != null && d.gameObject.name == "JackV2") { a = d.GetComponentInChildren<Animator>(true); break; }
        if (a == null) { result.LogError("FAIL: JackV2 animator not found"); return; }
        int missing = 0;
        foreach (var left in new[]{true,false})
            foreach (var s in FingerPoseDriver.Chains(left))
            {
                if (a.GetBoneTransform(s.Aim) == null) { result.LogError("missing Aim {0}", s.Aim); missing++; }
                if (a.GetBoneTransform(s.SegFrom) == null) { result.LogError("missing SegFrom {0}", s.SegFrom); missing++; }
                if (a.GetBoneTransform(s.SegTo) == null) { result.LogError("missing SegTo {0}", s.SegTo); missing++; }
            }
        result.Log(missing == 0 ? "ALL 30 BONES RESOLVE - PASS" : ("MISSING=" + missing));
    }
}
```

Expected: `ALL 30 BONES RESOLVE - PASS`.

- [ ] **Step 4: Checkpoint** — present `FingerPoseDriver.cs`; let the user commit (suggested: `feat(vr): finger retarget mapping + aiming math (pure statics)`).

---

## Task 3: `FingerPoseDriver` runtime — cache bones, gate, and drive in LateUpdate

**Files:**

- Modify: `Assets/Game/Scripts/VR/FingerPoseDriver.cs`

- [ ] **Step 1: Add the serialized fields, caches, lifecycle, and driving loop**

Add these members to the `FingerPoseDriver` class (the runtime half):

```csharp
    [Header("Sources")]
    [Tooltip("Input-mode router (selects controllers vs hands). Fingers only drive in Hands mode.")]
    public InputModeRouter router;
    [Tooltip("The single XR Hands reader that owns finger joint data.")]
    public HandTrackingPoseSource handSource;

    [Header("Tuning")]
    [Tooltip("0 = fingers stay in the idle animation pose; 1 = fully follow the tracked hand.")]
    [Range(0f, 1f)] public float fingerWeight = 1f;
    [Tooltip("Low-pass on finger motion. 0 = snap to tracked pose (no smoothing); higher = smoother but laggier.")]
    [Range(0f, 0.95f)] public float smoothing = 0f;

    Animator animator;
    FingerBoneSpec[] leftSpecs, rightSpecs;
    Transform[] lAim, lFrom, lTo, rAim, rFrom, rTo;   // cached bone transforms, parallel to specs
    Quaternion[] lPrev, rPrev;                        // last applied world rotations (for smoothing + freeze)

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) { enabled = false; return; }
        leftSpecs = Chains(true);
        rightSpecs = Chains(false);
        Cache(leftSpecs, out lAim, out lFrom, out lTo, out lPrev);
        Cache(rightSpecs, out rAim, out rFrom, out rTo, out rPrev);
    }

    void Cache(FingerBoneSpec[] specs, out Transform[] aim, out Transform[] from, out Transform[] to, out Quaternion[] prev)
    {
        aim = new Transform[specs.Length];
        from = new Transform[specs.Length];
        to = new Transform[specs.Length];
        prev = new Quaternion[specs.Length];   // default(Quaternion) == (0,0,0,0) -> treated as "no prev"
        for (int i = 0; i < specs.Length; i++)
        {
            aim[i]  = animator.GetBoneTransform(specs[i].Aim);
            from[i] = animator.GetBoneTransform(specs[i].SegFrom);
            to[i]   = animator.GetBoneTransform(specs[i].SegTo);
        }
    }

    void LateUpdate()
    {
        if (animator == null || router == null || handSource == null) return;
        DriveHand(Handedness.Left,  handSource.LeftTracked,  leftSpecs,  lAim, lFrom, lTo, lPrev);
        DriveHand(Handedness.Right, handSource.RightTracked, rightSpecs, rAim, rFrom, rTo, rPrev);
    }

    void DriveHand(Handedness h, bool tracked, FingerBoneSpec[] specs,
                   Transform[] aim, Transform[] from, Transform[] to, Quaternion[] prev)
    {
        // Controller mode -> leave fingers in the idle animation pose; clear prev so we don't
        // snap from a stale tracked pose when hands resume.
        if (router.CurrentMode != InputModeRouter.InputMode.Hands)
        {
            for (int i = 0; i < prev.Length; i++) prev[i] = default;
            return;
        }

        // Hands mode but this hand dropped out -> freeze the last good pose by re-applying it.
        if (!ShouldDrive(router.CurrentMode, tracked))
        {
            for (int i = 0; i < specs.Length; i++)
                if (aim[i] != null && prev[i] != default) aim[i].rotation = prev[i];
            return;
        }

        // Tracked: aim each bone root->tip (array is ordered proximal->intermediate->distal per finger).
        for (int i = 0; i < specs.Length; i++)
        {
            if (aim[i] == null || from[i] == null || to[i] == null) continue;
            var s = specs[i];
            if (!handSource.TryGetJointWorld(h, s.XrFrom, out Pose pf)) continue;
            if (!handSource.TryGetJointWorld(h, s.XrTo, out Pose pt)) continue;

            Vector3 currentDir = to[i].position - from[i].position;
            Vector3 targetDir  = pt.position - pf.position;
            Quaternion aimed   = Aim(currentDir, targetDir, aim[i].rotation);
            Quaternion target  = Quaternion.Slerp(aim[i].rotation, aimed, fingerWeight);
            if (smoothing > 0f && prev[i] != default)
                target = Quaternion.Slerp(prev[i], target, 1f - smoothing);

            aim[i].rotation = target;
            prev[i] = target;
        }
    }
```

If the executor wrote Task 2 as a non-partial class, paste these members into that same class instead of a second `partial` block.

- [ ] **Step 2: Verify compilation + Awake null-safety in edit mode**

```csharp
using UnityEngine;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var go = new GameObject("_fpdTest");
        var fpd = go.AddComponent<FingerPoseDriver>();
        // No animator child + no refs: Awake should disable it, LateUpdate must not throw.
        result.Log("enabled after Awake (expected False, no animator)={0}", fpd.enabled);
        Object.DestroyImmediate(go);
        result.Log("PASS (no exception)");
    }
}
```

Expected: `enabled after Awake ...=False`, `PASS (no exception)`.

- [ ] **Step 3: Checkpoint** — present the runtime diff; let the user commit (suggested: `feat(vr): FingerPoseDriver runtime — drive finger bones from XR Hands`).

---

## Task 4: Wire `FingerPoseDriver` onto JackV2 and verify in-headset

**Files:**

- Modify: `Assets/Scenes/GrandStaircase.unity`

- [ ] **Step 1: Add the component to JackV2 and wire refs (edit mode)**

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        if (Application.isPlaying) { result.LogError("Exit Play first"); return; }
        GameObject jack = null;
        foreach (var d in Resources.FindObjectsOfTypeAll<AvatarRigDriver>())
            if (d != null && d.gameObject.name == "JackV2") { jack = d.gameObject; break; }
        if (jack == null) { result.LogError("JackV2 not found"); return; }
        var router = Object.FindFirstObjectByType<InputModeRouter>(FindObjectsInactive.Include);
        var hands  = Object.FindFirstObjectByType<HandTrackingPoseSource>(FindObjectsInactive.Include);
        if (router == null || hands == null) { result.LogError("router/handSource missing in scene"); return; }

        var fpd = jack.GetComponent<FingerPoseDriver>();
        if (fpd == null) fpd = Undo.AddComponent<FingerPoseDriver>(jack);
        result.RegisterObjectModification(fpd);
        fpd.router = router;
        fpd.handSource = hands;
        fpd.fingerWeight = 1f;
        fpd.smoothing = 0f;
        EditorUtility.SetDirty(fpd);
        EditorSceneManager.MarkSceneDirty(jack.scene);
        EditorSceneManager.SaveScene(jack.scene);
        result.Log("FingerPoseDriver on JackV2: router={0} hands={1} weight={2}", router != null, hands != null, fpd.fingerWeight);
    }
}
```

Expected: logs all-true; scene saved.

- [ ] **Step 2: In-headset live check (Editor Play over Link)**

Ask the user to: don the headset, **put the controllers down and use bare hands** (so `InputModeRouter` switches to Hands), enter Play, switch the active mirror character to **JackV2** (A-button cycle, or activate it directly via a RunCommand as in the calibration session), face the mirror. Then have them perform, holding each ~2s: **open flat hand → slow fist → spread fingers wide → thumbs-up → point with index**.

- [ ] **Step 3: Capture layer-8 hand renders for each pose and read them**

For each held pose, run the render script (the proven temp-camera method: temp `Camera` with `cullingMask = 1<<8`, solid bg, flat white ambient, `cam.Render()` → `ReadPixels` → `EncodeToPNG` to the project root, then `Read` the PNG). Capture front + top of the hands. Compare the rendered JackV2 fingers against the named pose. Re-render in a **separate** RunCommand at least one frame after any change (LateUpdate writes bones next frame — the stale-frame gotcha).

Expected: fingers curl on the fist, separate on the spread, thumb lifts on thumbs-up, index extends on point. Confirm with the user live in the mirror too.

- [ ] **Step 4: If fingers look wrong**, diagnose before tweaking (don't guess):
  - **All fingers rigid / not moving:** confirm `router.CurrentMode == Hands` and `handSource.LeftTracked/RightTracked` are true in a RunCommand; confirm `TryGetJointWorld` returns true for `IndexProximal`.
  - **Fingers move but curl the wrong way / sideways:** log `currentDir` vs `targetDir` for one bone; this is a segment-mapping error — re-check the Task 2 table against the spec.
  - **Jitter:** raise `smoothing` (e.g. 0.3–0.6) live, then bake the chosen value.
  - **Thumb off but fingers fine:** expected risk area — verify the thumb's shifted mapping (Metacarpal→Proximal etc.).

- [ ] **Step 5: Bake any tuned `fingerWeight`/`smoothing`** in edit mode (set field, `SetDirty`, `MarkSceneDirty`, `SaveScene`) — Play-mode values do not persist. Clean up any temp PNGs from the project root (`bash rm`, PowerShell is denied).

- [ ] **Step 6: Checkpoint** — present the scene change; let the user commit (suggested: `feat(vr): enable finger articulation on JackV2 mirror avatar`).

---

## Self-review (completed by plan author)

**Spec coverage:**

- Full articulation (curl + splay + thumb) → Task 2 mapping table + Task 3 `Aim` loop. ✓
- Hand-tracking only → `ShouldDrive` gate on `InputMode.Hands` (Task 2/3). ✓
- Extend `HandTrackingPoseSource` as sole reader → Task 1. ✓
- New per-avatar `FingerPoseDriver`, execution order after `AvatarRigDriver` → `[DefaultExecutionOrder(100)]` (Task 2). ✓
- Approach A aiming, root→tip, distal-leaf via intermediate→distal segment → Task 2 table (distal `SegFrom`=Intermediate) + Task 3 loop. ✓
- Behavior table (controller=idle, tracked=drive, dropout=freeze) → `DriveHand` branches (Task 3). ✓
- `fingerWeight` + smoothing → Task 3 fields. ✓
- Reusability (Rose = add component) → generic `Chains`/`Mirror`; no JackV2 hardcoding in the component. ✓
- Verification (pure-static RunCommand + in-headset layer-8 renders) → Task 2 Steps 2–3, Task 4 Steps 2–3. ✓

**Placeholder scan:** none — all steps contain full code/commands and expected output.

**Type consistency:** `TryGetJointWorld(Handedness, XRHandJointID, out Pose)`, `Aim(Vector3,Vector3,Quaternion)`, `ShouldDrive(InputModeRouter.InputMode,bool)`, `Chains(bool)`, `Mirror(HumanBodyBones)`, `FingerBoneSpec(Aim,SegFrom,SegTo,XrFrom,XrTo)` — names/signatures match across Tasks 1–4. `InputModeRouter.InputMode` enum (`Controllers`/`Hands`) and `HandTrackingPoseSource.LeftTracked/RightTracked` match the existing source files.

**Note for Rose (future step 4 of the larger mission):** repeat Task 4 on the Rose avatar (add `FingerPoseDriver`, wire `router`+`handSource`). No new code. Covered by the `photo-to-fingered-vr-avatar` skill's pipeline.
