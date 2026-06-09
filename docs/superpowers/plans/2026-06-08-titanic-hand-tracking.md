# Titanic VR Hand Tracking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let testers use the Titanic mirror experience with bare Quest 3 hands instead of the Touch controllers, auto-switching between the two input modes, with hands-free character switching and exit.

**Architecture:** Build on the existing `IPoseSource` seam. A new `HandTrackingPoseSource` reads XR Hands; a new `InputModeRouter` (itself an `IPoseSource`) picks controller-vs-hand mode each frame with a debounce and forwards to the active source; every character's `AvatarRigDriver` points at the router. A lightweight `PokeButton` panel near the mirror provides hands-free "Next Character" and a guarded "Exit"; `QuitOnButton` gains a keyboard ESC binding.

**Tech Stack:** Unity 6000.4.0f1, URP, OpenXR + XR Interaction Toolkit 3.5.0, **XR Hands 1.8.0** (installed during the spike), Unity Input System. Quest 3 PCVR over Air Link.

---

## Conventions for this plan (read first)

- **No Unity test harness exists in this project** (confirmed: no test asmdefs), and the established
  practice across every prior VR spec is _pure static methods + in-headset verification_. We follow
  that. Do **not** restructure `Assembly-CSharp` into asmdefs.
- **Pure logic is verified executably** by running an MCP `Unity_RunCommand` "assertion script" that
  calls the real static methods in the live Editor and logs `PASS`/`FAIL`. That is this plan's
  red/green. Behavior that only exists at runtime (tracking, IK, poke in 3D) is verified **in-headset**
  over Air Link in Play mode.
- **Editor automation is via the Unity MCP** (`Unity_RunCommand`, `Unity_GetConsoleLogs`). After any
  script create/edit, the Editor recompiles and the MCP bridge briefly drops — that is normal; re-poll
  `Unity_GetConsoleLogs` until it responds, then check for compile errors.
- **Git:** the user's standing rule is _no commits without explicit in-turn permission_. Commit steps
  are included as required by the planning format, but when executing, **pause and ask** before each
  `git commit`. Branch off `main` first if not already on a feature branch.
- **Scene file:** `Assets/Scenes/GrandStaircase.unity` (the active scene). Key paths:
  - HMD: `XR Origin (XR Rig)/Camera Offset/Main Camera`
  - XR Origin: `XR Origin (XR Rig)`
  - Pose source + switcher: `XR Origin (XR Rig)/MirrorAvatars` (`ThreePointIKPoseSource`, `MirrorCharacterSwitcher`)
  - Characters (each an `AvatarRigDriver`): `XR Origin (XR Rig)/MirrorAvatars/{Default,Jack,Rose}`
  - Quit: `GameControls` (`QuitOnButton`)

## File structure

**Create:**

- `Assets/Game/Scripts/VR/HandTrackingPoseSource.cs` — XR Hands reader + `IPoseSource`. Owns _all_
  XR Hands access: exposes tracked state + index-fingertip world positions, and writes head/hand IK
  targets (wrist-driven, with a calibrated rig-independent convention offset).
- `Assets/Game/Scripts/VR/InputModeRouter.cs` — `IPoseSource` router; pure `ClassifySignal` + `StepMode`
  debounce statics; exposes `CurrentMode` + `ModeChanged`.
- `Assets/Game/Scripts/VR/PokeButton.cs` — world-space poke button; pure `IsPoking`/`StepHold`/`Activates`
  statics; instant or poke-and-hold; fires a `UnityEvent`.

**Modify:**

- `Assets/Game/Scripts/VR/QuitOnButton.cs` — add a keyboard ESC binding.
- `Assets/Scenes/GrandStaircase.unity` — add router + hand source objects, rewire `AvatarRigDriver`s,
  build the poke panel. (Done via MCP / Inspector, not by hand-editing the YAML.)

**Delete (cleanup):**

- `Assets/Game/Scripts/VR/HandTrackingSpike.cs` (+ `.meta`) and the `HandTrackingSpike` GameObject in
  the scene — throwaway spike artifacts.

---

## Task 1: `HandTrackingPoseSource`

**Files:**

- Create: `Assets/Game/Scripts/VR/HandTrackingPoseSource.cs`

- [ ] **Step 1: Write the script**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

// IPoseSource driven by Quest hand tracking (XR Hands). Head comes from the HMD (identical to the
// controller source); each avatar hand target comes from the player's tracked WRIST joint, with a
// constant calibrated offset that maps the OpenXR wrist convention onto the same convention the Touch
// controller grip produces -- so AvatarRigDriver's per-character offsets keep working unchanged.
//
// Also the single owner of "the player's tracked hands": exposes tracked state + index-fingertip world
// positions for the poke UI, so nothing else has to read the XR Hands subsystem.
[DisallowMultipleComponent]
public class HandTrackingPoseSource : MonoBehaviour, IPoseSource
{
    [Header("Tracked sources")]
    [Tooltip("HMD transform (same one the controller source uses).")]
    public Transform hmd;
    [Tooltip("XR Origin transform; XR Hands joint poses are reported in this space.")]
    public Transform xrOrigin;

    [Header("Wrist convention offset (calibrated constant, rig-independent)")]
    public Vector3 leftWristRotationOffsetEuler = Vector3.zero;
    public Vector3 rightWristRotationOffsetEuler = Vector3.zero;
    [Tooltip("Wrist-joint -> hand-target position nudge, in wrist-local space.")]
    public Vector3 handPositionOffset = Vector3.zero;

    XRHandSubsystem subsystem;
    readonly List<XRHandSubsystem> buffer = new();

    // Exposed for InputModeRouter + PokeButton (single XR Hands reader).
    public bool LeftTracked { get; private set; }
    public bool RightTracked { get; private set; }
    public bool AnyHandTracked => LeftTracked || RightTracked;
    public Vector3 LeftIndexTip { get; private set; }
    public Vector3 RightIndexTip { get; private set; }

    public bool IsActive => hmd != null && subsystem != null && subsystem.running;

    void Update() => EnsureSubsystem();

    void EnsureSubsystem()
    {
        if (subsystem != null && subsystem.running) return;
        SubsystemManager.GetSubsystems(buffer);
        subsystem = null;
        foreach (var s in buffer) if (s.running) { subsystem = s; break; }
    }

    public void UpdateTargets(Transform headTarget, Transform leftHandTarget, Transform rightHandTarget)
    {
        EnsureSubsystem();
        if (hmd != null) headTarget.SetPositionAndRotation(hmd.position, hmd.rotation);
        if (subsystem == null) { LeftTracked = RightTracked = false; return; }

        Transform space = xrOrigin != null ? xrOrigin : transform;
        LeftTracked = ApplyHand(subsystem.leftHand, leftHandTarget, space,
            Quaternion.Euler(leftWristRotationOffsetEuler), v => LeftIndexTip = v);
        RightTracked = ApplyHand(subsystem.rightHand, rightHandTarget, space,
            Quaternion.Euler(rightWristRotationOffsetEuler), v => RightIndexTip = v);
    }

    // Writes the hand target from the wrist joint and caches the index tip. Untracked hand: leave the
    // target as-is (freeze last pose) and report false.
    bool ApplyHand(XRHand hand, Transform target, Transform space, Quaternion wristOffset,
        Action<Vector3> setIndexTip)
    {
        if (!hand.isTracked) return false;
        if (hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wrist))
        {
            Vector3 wPos = space.TransformPoint(wrist.position);
            Quaternion wRot = space.rotation * wrist.rotation;
            target.SetPositionAndRotation(wPos + wRot * handPositionOffset, wRot * wristOffset);
        }
        if (hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out Pose tip))
            setIndexTip(space.TransformPoint(tip.position));
        return true;
    }
}
```

- [ ] **Step 2: Compile and verify no errors**

MCP `Unity_RunCommand`:

```csharp
using UnityEditor;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result) { AssetDatabase.Refresh(); result.Log("refreshed"); }
}
```

Then poll `Unity_GetConsoleLogs` (logTypes "Error") until the bridge responds.
Expected: `errorCount: 0`. The API used here (`XRHandSubsystem`, `subsystem.leftHand`,
`hand.GetJoint(XRHandJointID.Wrist).TryGetPose`) is the exact API proven working in the spike.

- [ ] **Step 3: Commit** (ask first per git rule)

```bash
git add Assets/Game/Scripts/VR/HandTrackingPoseSource.cs Assets/Game/Scripts/VR/HandTrackingPoseSource.cs.meta
git commit -m "feat(vr): HandTrackingPoseSource (XR Hands IPoseSource)"
```

---

## Task 2: `InputModeRouter`

**Files:**

- Create: `Assets/Game/Scripts/VR/InputModeRouter.cs`

- [ ] **Step 1: Write the script**

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

// Routes the avatar's pose between the controller source and the hand-tracking source, choosing the
// active input mode each frame with a debounce so the hand-off doesn't flicker. Itself an IPoseSource,
// so AvatarRigDriver points at it and never has to know which input is live.
[DisallowMultipleComponent]
public class InputModeRouter : MonoBehaviour, IPoseSource
{
    public enum InputMode { Controllers, Hands }
    public enum ModeSignal { None, Controllers, Hands }

    [Tooltip("Controller pose source (ThreePointIKPoseSource).")]
    public MonoBehaviour controllerSourceBehaviour;
    [Tooltip("Hand-tracking pose source.")]
    public HandTrackingPoseSource handSource;

    [Tooltip("Seconds a differing signal must persist before the mode flips.")]
    public float debounceSeconds = 0.3f;

    public InputMode CurrentMode { get; private set; } = InputMode.Controllers;
    public event Action<InputMode> ModeChanged;

    IPoseSource controllerSource;
    float stableTimer;
    readonly List<InputDevice> deviceBuffer = new();

    // Pure: classify the raw signal. Hands win when tracked; else controllers if tracked; else None
    // (no input -> caller keeps the last mode).
    public static ModeSignal ClassifySignal(bool anyHandTracked, bool anyControllerTracked)
    {
        if (anyHandTracked) return ModeSignal.Hands;
        if (anyControllerTracked) return ModeSignal.Controllers;
        return ModeSignal.None;
    }

    // Pure, testable debounce step. None signal, or a signal matching the current mode, resets the
    // timer and holds. A differing signal must persist >= debounceSeconds before the mode flips.
    public static InputMode StepMode(InputMode current, ModeSignal signal, float dt,
        ref float stableTimer, float debounceSeconds)
    {
        if (signal == ModeSignal.None) { stableTimer = 0f; return current; }
        InputMode desired = signal == ModeSignal.Hands ? InputMode.Hands : InputMode.Controllers;
        if (desired == current) { stableTimer = 0f; return current; }
        stableTimer += Mathf.Max(dt, 0f);
        if (stableTimer >= debounceSeconds) { stableTimer = 0f; return desired; }
        return current;
    }

    void Awake() => controllerSource = controllerSourceBehaviour as IPoseSource;

    public bool IsActive =>
        (controllerSource != null && controllerSource.IsActive) ||
        (handSource != null && handSource.IsActive);

    void Update()
    {
        bool hands = handSource != null && handSource.AnyHandTracked;
        var signal = ClassifySignal(hands, AnyControllerTracked());
        var next = StepMode(CurrentMode, signal, Time.deltaTime, ref stableTimer, debounceSeconds);
        if (next != CurrentMode) { CurrentMode = next; ModeChanged?.Invoke(next); }
    }

    bool AnyControllerTracked()
    {
        InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, deviceBuffer);
        foreach (var d in deviceBuffer)
            if (d.TryGetFeatureValue(CommonUsages.isTracked, out bool t) && t) return true;
        return false;
    }

    public void UpdateTargets(Transform headTarget, Transform leftHandTarget, Transform rightHandTarget)
    {
        IPoseSource active = (CurrentMode == InputMode.Hands && handSource != null)
            ? handSource : controllerSource;
        if (active != null && active.IsActive)
            active.UpdateTargets(headTarget, leftHandTarget, rightHandTarget);
    }
}
```

- [ ] **Step 2: Compile and verify no errors** — same refresh + poll pattern as Task 1. Expected `errorCount: 0`.

- [ ] **Step 3: Verify the pure logic executably (this is the red/green test)**

MCP `Unity_RunCommand` — calls the real statics and asserts:

```csharp
using UnityEngine;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        int fails = 0;
        void Check(bool ok, string name) { if (!ok) { fails++; result.LogError("FAIL " + name); } else result.Log("PASS " + name); }

        // ClassifySignal
        Check(InputModeRouter.ClassifySignal(true, true)  == InputModeRouter.ModeSignal.Hands, "hands win when both");
        Check(InputModeRouter.ClassifySignal(false, true) == InputModeRouter.ModeSignal.Controllers, "controllers when only ctrl");
        Check(InputModeRouter.ClassifySignal(false, false)== InputModeRouter.ModeSignal.None, "none when neither");

        // StepMode: None holds current
        float t = 5f;
        Check(InputModeRouter.StepMode(InputModeRouter.InputMode.Controllers, InputModeRouter.ModeSignal.None, 1f, ref t, 0.3f)
              == InputModeRouter.InputMode.Controllers && t == 0f, "None holds + resets timer");

        // StepMode: matching signal holds, resets timer
        t = 0.2f;
        Check(InputModeRouter.StepMode(InputModeRouter.InputMode.Hands, InputModeRouter.ModeSignal.Hands, 0.1f, ref t, 0.3f)
              == InputModeRouter.InputMode.Hands && t == 0f, "matching holds + resets");

        // StepMode: differing signal needs to persist past debounce
        t = 0f;
        var m = InputModeRouter.StepMode(InputModeRouter.InputMode.Controllers, InputModeRouter.ModeSignal.Hands, 0.2f, ref t, 0.3f);
        Check(m == InputModeRouter.InputMode.Controllers, "below debounce: no flip yet");
        m = InputModeRouter.StepMode(m, InputModeRouter.ModeSignal.Hands, 0.2f, ref t, 0.3f);
        Check(m == InputModeRouter.InputMode.Hands && t == 0f, "past debounce: flips + resets");

        result.Log(fails == 0 ? "ALL PASS" : ("FAILURES=" + fails));
    }
}
```

Expected: `ALL PASS`, no `FAIL` lines.

- [ ] **Step 4: Commit** (ask first)

```bash
git add Assets/Game/Scripts/VR/InputModeRouter.cs Assets/Game/Scripts/VR/InputModeRouter.cs.meta
git commit -m "feat(vr): InputModeRouter (controller/hand auto-switch pose router)"
```

---

## Task 3: Wire pose sources in the scene + verify auto-switch in-headset

**Files:**

- Modify: `Assets/Scenes/GrandStaircase.unity` (via MCP / Inspector)

- [ ] **Step 1: Add the hand source + router and wire them**

MCP `Unity_RunCommand` (adjust paths only if they differ from the discovery above):

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene = SceneManager.GetActiveScene();
        Transform Find(string p) { var go = GameObject.Find(p); return go ? go.transform : null; }

        var mirrorAvatars = Find("XR Origin (XR Rig)/MirrorAvatars");
        var hmd = Find("XR Origin (XR Rig)/Camera Offset/Main Camera");
        var origin = Find("XR Origin (XR Rig)");
        var three = mirrorAvatars.GetComponent<ThreePointIKPoseSource>();

        // HandTrackingPoseSource
        var htGo = new GameObject("HandTrackingPoseSource");
        htGo.transform.SetParent(mirrorAvatars, false);
        var ht = (HandTrackingPoseSource)htGo.AddComponent(typeof(HandTrackingPoseSource));
        ht.hmd = hmd; ht.xrOrigin = origin;
        result.RegisterObjectCreation(htGo);

        // InputModeRouter
        var rtGo = new GameObject("InputModeRouter");
        rtGo.transform.SetParent(mirrorAvatars, false);
        var rt = (InputModeRouter)rtGo.AddComponent(typeof(InputModeRouter));
        rt.controllerSourceBehaviour = three;
        rt.handSource = ht;
        result.RegisterObjectCreation(rtGo);

        // Re-point every AvatarRigDriver at the router
        foreach (var drv in Object.FindObjectsByType<AvatarRigDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            result.RegisterObjectModification(drv);
            drv.poseSourceBehaviour = rt;
            EditorUtility.SetDirty(drv);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        result.Log("Wired HandTrackingPoseSource + InputModeRouter; re-pointed AvatarRigDrivers.");
    }
}
```

Expected log: "Wired ... re-pointed AvatarRigDrivers." No errors.

- [ ] **Step 2: In-headset check (Air Link, Play mode)** — _manual, user performs_

1. Press Play with controllers in hand → mirror avatar hands follow controllers (unchanged).
2. Set controllers down, raise hands → after ~0.3 s the avatar hands follow your real hands.
3. Pick controllers back up → switches back.

The wrist _orientation_ will look wrong until Task 4 (offset still zero) — that is expected; only
confirm **position tracking + the mode switch** here.

- [ ] **Step 3: Commit** (ask first)

```bash
git add Assets/Scenes/GrandStaircase.unity
git commit -m "feat(vr): wire hand-tracking pose source + input-mode router into scene"
```

---

## Task 4: Calibrate the wrist convention offset (in-headset)

**Files:**

- Modify: `Assets/Scenes/GrandStaircase.unity` (Inspector values on `HandTrackingPoseSource`)

This finds the single rig-independent constant that aligns the tracked wrist with the avatar wrist so
the existing per-character offsets keep working. Use the flashlight-pose method from the
`titanic-avatar-calibration-procedure` memory.

- [ ] **Step 1:** Enter Play (hands mode). Hold both hands flat, fingers forward ("flashlight" pose),
      watching the mirror.
- [ ] **Step 2:** Adjust `HandTrackingPoseSource.leftWristRotationOffsetEuler` /
      `rightWristRotationOffsetEuler` in the Inspector until the reflected wrists match your real wrist
      orientation. Nudge `handPositionOffset` if the hand sits slightly off the wrist.
- [ ] **Step 3:** Note the values (Play-mode changes are lost on Stop). Stop Play, re-enter the same
      values on the component, and re-verify. Confirm all three characters (Default/Jack/Rose) look right
      with the _same_ offset (the offset is rig-independent; per-character offsets stay on the drivers).
- [ ] **Step 4:** Save the scene. **Commit** (ask first):

```bash
git add Assets/Scenes/GrandStaircase.unity
git commit -m "chore(vr): calibrate hand-tracking wrist convention offset"
```

---

## Task 5: `PokeButton`

**Files:**

- Create: `Assets/Game/Scripts/VR/PokeButton.cs`

- [ ] **Step 1: Write the script**

```csharp
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// A world-space button activated by poking it with a fingertip (hands mode) or a controller poke point
// (controller mode). Instant by default; set holdSeconds > 0 for a guarded poke-and-hold (e.g. Exit).
// Fires onActivated once per completed activation.
[DisallowMultipleComponent]
public class PokeButton : MonoBehaviour
{
    [Tooltip("Center of the poke volume (defaults to this transform).")]
    public Transform target;
    [Tooltip("Poke hit radius (m).")]
    public float radius = 0.04f;
    [Tooltip("0 = instant on touch; >0 = must hold the poke this long to activate (guard).")]
    public float holdSeconds = 0f;

    [Tooltip("Router reporting the active input mode.")]
    public InputModeRouter router;
    [Tooltip("Hand-tracking source supplying fingertip positions in hands mode.")]
    public HandTrackingPoseSource handSource;
    [Tooltip("Controller poke points (a child transform on each controller) for controller mode.")]
    public Transform[] controllerPokers;

    [Tooltip("Invoked once each time the button completes activation.")]
    public UnityEvent onActivated;

    public float HoldProgress { get; private set; }   // 0..1, for a fill affordance
    float held;
    bool wasPoking;
    readonly List<Vector3> pokers = new();

    // Pure: is any poker within radius of center?
    public static bool IsPoking(Vector3 center, float radius, IList<Vector3> pokers)
    {
        for (int i = 0; i < pokers.Count; i++)
            if ((pokers[i] - center).sqrMagnitude <= radius * radius) return true;
        return false;
    }

    // Pure: accumulate hold time while poking, reset to 0 when not.
    public static float StepHold(float held, bool poking, float dt) => poking ? held + Mathf.Max(dt, 0f) : 0f;

    // Pure: did this step trigger activation? Instant (holdSeconds<=0) fires on the rising edge of a
    // poke; hold fires the frame the accumulated time crosses the threshold.
    public static bool Activates(bool wasPoking, bool poking, float prevHeld, float newHeld, float holdSeconds)
    {
        if (holdSeconds <= 0f) return poking && !wasPoking;
        return prevHeld < holdSeconds && newHeld >= holdSeconds;
    }

    void Update()
    {
        Vector3 center = (target != null ? target : transform).position;
        CollectPokers();
        bool poking = IsPoking(center, radius, pokers);
        float prev = held;
        held = StepHold(held, poking, Time.deltaTime);
        HoldProgress = holdSeconds > 0f ? Mathf.Clamp01(held / holdSeconds) : (poking ? 1f : 0f);
        if (Activates(wasPoking, poking, prev, held, holdSeconds))
        {
            onActivated?.Invoke();
            held = 0f; // require release + re-poke before it can fire again
        }
        wasPoking = poking;
    }

    void CollectPokers()
    {
        pokers.Clear();
        bool handsMode = router == null || router.CurrentMode == InputModeRouter.InputMode.Hands;
        bool ctrlMode  = router == null || router.CurrentMode == InputModeRouter.InputMode.Controllers;
        if (handsMode && handSource != null)
        {
            if (handSource.LeftTracked) pokers.Add(handSource.LeftIndexTip);
            if (handSource.RightTracked) pokers.Add(handSource.RightIndexTip);
        }
        if (ctrlMode && controllerPokers != null)
            foreach (var t in controllerPokers) if (t != null) pokers.Add(t.position);
    }
}
```

- [ ] **Step 2: Compile and verify no errors** — refresh + poll. Expected `errorCount: 0`.

- [ ] **Step 3: Verify the pure logic executably (red/green)**

MCP `Unity_RunCommand`:

```csharp
using System.Collections.Generic;
using UnityEngine;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        int fails = 0;
        void Check(bool ok, string n) { if (!ok) { fails++; result.LogError("FAIL " + n); } else result.Log("PASS " + n); }

        var inRange = new List<Vector3> { new Vector3(0.02f, 0, 0) };
        var outRange = new List<Vector3> { new Vector3(0.20f, 0, 0) };
        Check(PokeButton.IsPoking(Vector3.zero, 0.04f, inRange), "poker inside radius");
        Check(!PokeButton.IsPoking(Vector3.zero, 0.04f, outRange), "poker outside radius");
        Check(!PokeButton.IsPoking(Vector3.zero, 0.04f, new List<Vector3>()), "no pokers = not poking");

        Check(PokeButton.StepHold(0.5f, true, 0.1f) == 0.6f, "hold accumulates");
        Check(PokeButton.StepHold(0.5f, false, 0.1f) == 0f, "hold resets when not poking");

        // instant: fires on rising edge only
        Check(PokeButton.Activates(false, true, 0, 0, 0f), "instant fires on rising edge");
        Check(!PokeButton.Activates(true, true, 0, 0, 0f), "instant does not re-fire while held");
        // hold: fires the step the threshold is crossed
        Check(!PokeButton.Activates(true, true, 1.0f, 1.4f, 1.5f), "hold not yet at threshold");
        Check(PokeButton.Activates(true, true, 1.4f, 1.6f, 1.5f), "hold fires crossing threshold");

        result.Log(fails == 0 ? "ALL PASS" : ("FAILURES=" + fails));
    }
}
```

Expected: `ALL PASS`.

- [ ] **Step 4: Commit** (ask first)

```bash
git add Assets/Game/Scripts/VR/PokeButton.cs Assets/Game/Scripts/VR/PokeButton.cs.meta
git commit -m "feat(vr): PokeButton (fingertip/controller poke, instant or hold)"
```

---

## Task 6: Build the poke panel in the scene + wire actions

**Files:**

- Modify: `Assets/Scenes/GrandStaircase.unity` (via MCP / Inspector)

- [ ] **Step 1: Find the controllers' poke points** (for controller-mode poking)

MCP `Unity_RunCommand` to log controller transform paths:

```csharp
using UnityEngine;
using System.Text;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var sb = new StringBuilder();
        foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (t.name.ToLower().Contains("controller") || t.name.ToLower().Contains("hand") && t.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() != null)
            {
                var p = t.name; var x = t; while (x.parent) { x = x.parent; p = x.name + "/" + p; }
                sb.AppendLine(p);
            }
        result.Log(sb.ToString());
    }
}
```

Note the Left/Right controller transform paths for Step 2. (If they have a dedicated "poke point"
child, prefer it; otherwise the controller transform itself is fine — the buttons sit at chest height
so the controller body reaching them is acceptable.)

- [ ] **Step 2: Create the panel with two PokeButtons and wire actions**

MCP `Unity_RunCommand` — creates a panel near the mirror with "Next Character" (instant) and "Exit"
(hold) buttons as small quads, wires `onActivated` via `UnityEventTools`. Replace
`LEFT_CTRL_PATH`/`RIGHT_CTRL_PATH` with the paths from Step 1, and tune `panelPos` to a spot beside the
mirror that the player can reach:

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var scene = SceneManager.GetActiveScene();
        Transform Find(string p) { var go = GameObject.Find(p); return go ? go.transform : null; }

        var mirrorAvatars = Find("XR Origin (XR Rig)/MirrorAvatars");
        var ht = Object.FindAnyObjectByType<HandTrackingPoseSource>();
        var rt = Object.FindAnyObjectByType<InputModeRouter>();
        var switcher = Object.FindAnyObjectByType<MirrorCharacterSwitcher>();
        var quit = Object.FindAnyObjectByType<QuitOnButton>();
        var leftCtrl = Find("LEFT_CTRL_PATH");
        var rightCtrl = Find("RIGHT_CTRL_PATH");
        Transform[] ctrlPokers = { leftCtrl, rightCtrl };

        var panel = new GameObject("PokePanel").transform;
        panel.position = new Vector3(0f, 1.1f, 0.6f); // panelPos: tune beside the mirror

        PokeButton MakeButton(string name, Vector3 localPos, Color c, float hold)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = name;
            q.transform.SetParent(panel, false);
            q.transform.localPosition = localPos;
            q.transform.localScale = new Vector3(0.18f, 0.08f, 1f);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            var mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = c };
            var btn = q.AddComponent<PokeButton>();
            btn.router = rt; btn.handSource = ht; btn.controllerPokers = ctrlPokers;
            btn.radius = 0.05f; btn.holdSeconds = hold;
            return btn;
        }

        var next = MakeButton("Btn_NextCharacter", new Vector3(0f, 0.07f, 0f), Color.white, 0f);
        var exit = MakeButton("Btn_Exit", new Vector3(0f, -0.07f, 0f), new Color(0.7f, 0.2f, 0.2f), 1.5f);

        UnityEventTools.AddPersistentListener(next.onActivated, switcher.Next);
        UnityEventTools.AddPersistentListener(exit.onActivated, quit.Quit);

        result.RegisterObjectCreation(panel.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        result.Log("Created PokePanel with Next + Exit buttons, wired to switcher + quit.");
    }
}
```

Expected: "Created PokePanel ...". (Labels/3D text are polish — add later if wanted. The colored quads
are enough to validate behavior.)

- [ ] **Step 2.5: Verify no errors** — poll `Unity_GetConsoleLogs`.

- [ ] **Step 3: In-headset check** — _manual, user performs_
  - Hands mode: poke "Next Character" → cycles Default → Jack → Rose. Poke-and-hold "Exit" ~1.5 s →
    quits (in Editor, exits Play). A quick brush of Exit does **not** quit.
  - Controller mode: the same buttons respond to the controller tip.

- [ ] **Step 4: Commit** (ask first)

```bash
git add Assets/Scenes/GrandStaircase.unity
git commit -m "feat(vr): hands-free poke panel (next character + guarded exit)"
```

---

## Task 7: Keyboard ESC quit (operator)

**Files:**

- Modify: `Assets/Game/Scripts/VR/QuitOnButton.cs`

- [ ] **Step 1: Replace the file contents**

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

// Quits on a controller button (Y, controller mode) OR the keyboard Escape key (operator). In a build
// this calls Application.Quit(); in the editor it stops Play mode. The poke panel's Exit button also
// calls Quit() (hands mode).
[DisallowMultipleComponent]
public class QuitOnButton : MonoBehaviour
{
    [Tooltip("Input System binding that quits. Default: left controller Y button.")]
    public string quitBinding = "<XRController>{LeftHand}/secondaryButton";

    [Tooltip("Operator keyboard binding that also quits. Default: Escape.")]
    public string quitKeyBinding = "<Keyboard>/escape";

    InputAction quitAction;

    void Awake()
    {
        quitAction = new InputAction("QuitGame", InputActionType.Button, quitBinding);
        quitAction.AddBinding(quitKeyBinding);
    }

    void OnEnable() { quitAction?.Enable(); }
    void OnDisable() { quitAction?.Disable(); }

    void Update()
    {
        if (quitAction != null && quitAction.WasPressedThisFrame()) Quit();
    }

    public void Quit()
    {
        Debug.Log("QuitOnButton: quit requested.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
```

- [ ] **Step 2: Compile and verify no errors** — refresh + poll. Expected `errorCount: 0`.

- [ ] **Step 3: In-headset / Editor check** — _manual_ — In Play mode press ESC → Play stops. With
      controllers, Y still quits.

- [ ] **Step 4: Commit** (ask first)

```bash
git add Assets/Game/Scripts/VR/QuitOnButton.cs
git commit -m "feat(vr): add keyboard ESC quit for operator"
```

---

## Task 8: Remove spike artifacts + verify standalone build

**Files:**

- Delete: `Assets/Game/Scripts/VR/HandTrackingSpike.cs` (+ `.meta`)
- Modify: `Assets/Scenes/GrandStaircase.unity` (remove the `HandTrackingSpike` object)

- [ ] **Step 1: Remove the spike object from the scene** (MCP)

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var go = GameObject.Find("HandTrackingSpike");
        if (go != null) { result.DestroyObject(go); result.Log("Removed HandTrackingSpike object."); }
        else result.Log("No HandTrackingSpike object found.");
        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
```

- [ ] **Step 2: Delete the spike script file**

```bash
git rm Assets/Game/Scripts/VR/HandTrackingSpike.cs Assets/Game/Scripts/VR/HandTrackingSpike.cs.meta
```

Then refresh + poll `Unity_GetConsoleLogs`. Expected `errorCount: 0` (nothing references the spike).

- [ ] **Step 3: Standalone build verification** — _manual, user performs_

Build the Windows standalone (the way prior builds were made) and run it over Air Link. Confirm in a
**real build** (not just Play-in-Editor): hands drive the avatar, auto-switch works both directions,
poke panel cycles characters, poke-hold Exit quits, ESC quits, controller mode unchanged. (See
`titanic-build-gotchas`: scene in Build Settings = GrandStaircase; mirror shader already in
Always-Included.) If hand tracking works in Play but not the build, check the OpenXR feature is enabled
for the active build target.

- [ ] **Step 4: Commit** (ask first)

```bash
git add -A
git commit -m "chore(vr): remove hand-tracking spike artifacts"
```

---

## Self-review — spec coverage

- HandTrackingPoseSource (head from HMD, hands from wrist, convention offset, exposes tracked + index
  tips) → **Task 1**.
- InputModeRouter + auto-switch + debounce → **Task 2** (logic) + **Task 3** (wiring + in-headset switch).
- Calibration as one rig-independent constant → **Task 4**.
- Poke panel; Next → `MirrorCharacterSwitcher.Next()`; Exit poke-and-hold → `QuitOnButton.Quit()` →
  **Task 5** (PokeButton) + **Task 6** (panel/wiring).
- ESC operator quit; Y unchanged → **Task 7**.
- Existing scripts unchanged & harmless in hands mode (no locomotion code needed) → verified across
  in-headset checks in Tasks 3/6/8.
- Delete spike artifacts; standalone build verification → **Task 8**.

All spec requirements map to a task. Static method/property names are consistent across tasks
(`ClassifySignal`, `StepMode`, `ModeSignal`, `InputMode`, `IsPoking`, `StepHold`, `Activates`,
`AnyHandTracked`, `LeftIndexTip`/`RightIndexTip`, `CurrentMode`).
