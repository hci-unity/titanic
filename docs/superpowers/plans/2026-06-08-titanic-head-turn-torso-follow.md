# Head-driven turning + smart mirror torso-follow — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove artificial stick-turning (player turns by physically pivoting) and make the mirror avatar's torso follow the head with a hysteresis dead-zone, so a glance keeps the shoulders still and committing to a new direction swings the torso around — no owl neck.

**Architecture:** Two confined changes. (1) Delete the `ContinuousTurnProvider` from the XR Origin; locomotion stays head-relative (`DynamicMoveProvider` unchanged), so physical pivoting sets facing. (2) Extend `AvatarRigDriver` to rotate the avatar root's yaw via a pure, testable hysteresis state machine (`StepYaw`). The change touches only the mirror-only (layer 8) avatar, so it cannot affect comfort, collision, or locomotion. Applies uniformly to Default/Jack/Rose since they share the driver.

**Tech Stack:** Unity 6000.4.0f1, URP, OpenXR + XR Interaction Toolkit 3.5.0. Edits applied as plain C# file edits (Unity recompiles) and Unity-MCP `Unity_RunCommand` for scene/serialized changes and the in-editor logic harness. Verification: in-editor MCP harness for pure math, in-headset (Play-in-Editor over Air Link) for integration.

**Spec:** `docs/superpowers/specs/2026-06-08-titanic-head-turn-torso-follow-design.md`

**⚠️ Git:** The user has a strict no-commit rule. NEVER run `git add`/`git commit`/`git push`. Each task ends by handing back to the user with a suggested commit message; the user commits.

**⚠️ Unity-MCP gotchas (from prior sessions):**

- Scene saves (`MarkSceneDirty`/`SaveScene`) FAIL during Play mode — do scene edits in EDIT mode.
- Guard `GetComponentsInChildren<MonoBehaviour>()` results with `!= null` (missing-script entries cause NRE).
- The MCP bridge drops to "no fresh discovery files" during domain reloads/recompiles — just retry the call once Unity settles.

---

## File Structure

- **Modify:** `Assets/Game/Scripts/VR/AvatarRigDriver.cs` — add the pure `StepYaw` static helper, four serialized tuning fields, one hysteresis-state field, and the yaw-follow call site inside `OnAnimatorIK`. This is the only script change. It keeps its single responsibility (drive the Humanoid avatar from the pose source); the new logic is the body-yaw half of "follow the HMD," replacing today's position-only follow with position + smart yaw.
- **Modify (scene):** `Assets/Scenes/GrandStaircase.unity` — remove the `ContinuousTurnProvider` component from the `XR Origin (XR Rig)` hierarchy. No other scene wiring: the new `AvatarRigDriver` fields take their C# defaults, which are the intended values, so all three avatars get the behavior automatically.

No new files. The pure logic lives as a static method beside the MonoBehaviour, matching the existing `LocomotionAnimatorDriver.HorizontalSpeed` convention in this codebase.

---

### Task 1: Pure hysteresis yaw helper `AvatarRigDriver.StepYaw`

Isolate the glance-vs-commit decision as a deterministic static method so it can be verified in the editor with no headset. TDD via an in-editor MCP harness: stub it first, watch the commit case fail, implement, watch it pass.

**Files:**

- Modify: `Assets/Game/Scripts/VR/AvatarRigDriver.cs`

- [ ] **Step 1: Add a STUB `StepYaw` (no movement) so the harness compiles and fails meaningfully**

In `AvatarRigDriver.cs`, add this static method inside the class (e.g. just above `void Awake()`):

```csharp
// Hysteresis-gated yaw step. Returns the new root yaw (deg). `following` carries the
// dead-zone state across frames: once |offset| >= startAngle the torso follows the head
// until |offset| <= stopAngle, then it locks again (glance = no follow, commit = follow).
// STUB — replaced in Step 3.
public static float StepYaw(float currentYaw, float desiredYaw, float dt,
    float startAngle, float stopAngle, float maxYawSpeed, ref bool following)
{
    return currentYaw; // no movement yet
}
```

- [ ] **Step 2: Run the verification harness to see the commit case FAIL**

Run this via `Unity_RunCommand` (Unity in EDIT mode). It exercises `StepYaw` with fixed inputs and logs PASS/FAIL — no scene/headset needed:

```csharp
bool b;
// Case A — GLANCE: 30 deg < startAngle 40 -> must NOT move, must stay frozen.
b = false;
float a = AvatarRigDriver.StepYaw(0f, 30f, 0.016f, 40f, 5f, 180f, ref b);
bool glanceOk = Mathf.Abs(a - 0f) < 0.001f && b == false;

// Case B — COMMIT: 180 deg >= startAngle -> follow and converge near 180 within ~2s.
b = false;
float y = 0f;
for (int i = 0; i < 130; i++) // ~2.08s at dt=0.016
    y = AvatarRigDriver.StepYaw(y, 180f, 0.016f, 40f, 5f, 180f, ref b);
bool commitOk = Mathf.Abs(Mathf.DeltaAngle(y, 180f)) <= 5f && b == false;

// Case C — HYSTERESIS HOLD: while following, a 20 deg offset (between stop 5 and start 40)
// keeps following until it drops to <= stopAngle.
b = true;             // already following
float z = 0f;
for (int i = 0; i < 130; i++)
    z = AvatarRigDriver.StepYaw(z, 20f, 0.016f, 40f, 5f, 180f, ref b);
bool holdOk = Mathf.Abs(Mathf.DeltaAngle(z, 20f)) <= 5f && b == false;

Debug.Log($"[StepYaw test] glance={glanceOk} commit={commitOk} hold={holdOk}");
```

Expected with the stub: `glance=True commit=False hold=False` (glance trivially passes because the stub never moves; commit/hold fail because nothing converges). This is the red state.

- [ ] **Step 3: Implement the real `StepYaw`**

Replace the stub body with:

```csharp
public static float StepYaw(float currentYaw, float desiredYaw, float dt,
    float startAngle, float stopAngle, float maxYawSpeed, ref bool following)
{
    float offset = Mathf.DeltaAngle(currentYaw, desiredYaw);
    float mag = Mathf.Abs(offset);

    if (!following && mag >= startAngle) following = true;
    else if (following && mag <= stopAngle) following = false;

    if (!following) return currentYaw;

    float step = Mathf.Min(mag, maxYawSpeed * Mathf.Max(dt, 0f));
    return currentYaw + Mathf.Sign(offset) * step;
}
```

- [ ] **Step 4: Re-run the harness from Step 2 to see GREEN**

Run the same `Unity_RunCommand` again.
Expected: `[StepYaw test] glance=True commit=True hold=True`.
If `commit` or `hold` is False, the convergence math is wrong — recheck the sign/`DeltaAngle` usage before proceeding.

- [ ] **Step 5: Hand back for commit (do NOT commit yourself)**

Report to the user that Task 1 is green. Suggested commit message:
`feat(vr): add hysteresis yaw helper for mirror torso-follow`

---

### Task 2: Wire torso-follow into `AvatarRigDriver`

Add the serialized tuning fields and the call site that rotates the avatar root each frame using `StepYaw`. Head/hand bones are untouched (still absolute to HMD/controllers in `LateUpdate`), so the reflection gets a natural neck twist during a glance and the shoulders swing during a commit.

**Files:**

- Modify: `Assets/Game/Scripts/VR/AvatarRigDriver.cs`

- [ ] **Step 1: Add the serialized fields + hysteresis state**

In `AvatarRigDriver.cs`, immediately after the existing `followHmdHorizontal` field:

```csharp
    [Tooltip("Move the avatar root to stand under the HMD (keeps body beneath the head).")]
    public bool followHmdHorizontal = true;
```

insert:

```csharp
    [Header("Torso follow (head-vs-body yaw)")]
    [Tooltip("Rotate the avatar root to follow the head's yaw with a hysteresis dead-zone: brief/small glances keep the shoulders still; sustained/large head turns swing the torso to match. Cosmetic — affects only the mirror reflection.")]
    public bool followYaw = true;

    [Tooltip("Head-vs-torso yaw offset (deg) at which the torso STARTS following.")]
    public float startAngle = 40f;

    [Tooltip("Offset (deg) at which the torso STOPS following (hysteresis; keep < startAngle).")]
    public float stopAngle = 5f;

    [Tooltip("How fast the torso swings to catch up while following (deg/s).")]
    public float maxYawSpeed = 180f;

    bool yawFollowing; // hysteresis state for StepYaw
```

- [ ] **Step 2: Add the yaw-follow call site in `OnAnimatorIK`**

In `OnAnimatorIK`, the current position-follow block reads:

```csharp
        if (followHmdHorizontal)
        {
            Vector3 hp = headTarget.position;
            transform.position = new Vector3(hp.x, transform.position.y, hp.z);
        }
```

Add the yaw block directly after it:

```csharp
        if (followYaw)
        {
            Vector3 f = headTarget.forward; f.y = 0f;
            if (f.sqrMagnitude > 1e-6f)
            {
                float desiredYaw = Quaternion.LookRotation(f).eulerAngles.y;
                float newYaw = StepYaw(transform.eulerAngles.y, desiredYaw, Time.deltaTime,
                    startAngle, stopAngle, maxYawSpeed, ref yawFollowing);
                Vector3 e = transform.eulerAngles;
                transform.eulerAngles = new Vector3(e.x, newYaw, e.z);
            }
        }
```

(`headTarget` is refreshed at the top of `OnAnimatorIK` via `RefreshTargets()`, so its forward is the live HMD direction.)

- [ ] **Step 3: Confirm it compiles cleanly**

Run via `Unity_RunCommand` after Unity recompiles (retry once if the MCP bridge reports "no fresh discovery files" during the reload):

```csharp
var logs = ""; // trivial no-op; the goal is a successful compile + command round-trip
Debug.Log("[torso-follow] AvatarRigDriver compiled OK");
```

Then check `Unity_GetConsoleLogs` — expected: the log line present, and NO compile errors referencing `AvatarRigDriver.cs`.

- [ ] **Step 4: Edit-mode sanity — fields exist on the scene avatars with intended defaults**

Run via `Unity_RunCommand` (EDIT mode):

```csharp
foreach (var d in Object.FindObjectsByType<AvatarRigDriver>(FindObjectsSortMode.None))
    Debug.Log($"[torso-follow] {d.gameObject.name}: followYaw={d.followYaw} start={d.startAngle} stop={d.stopAngle} maxYaw={d.maxYawSpeed}");
```

Expected: one line per avatar (Default, Jack, Rose) showing `followYaw=True start=40 stop=5 maxYaw=180`. New fields use C# defaults because they are not yet serialized in the scene — no per-avatar wiring required.

- [ ] **Step 5: Hand back for commit (do NOT commit yourself)**

Suggested commit message:
`feat(vr): mirror avatar torso follows head yaw (hysteresis dead-zone)`

---

### Task 3: Remove artificial stick-turn from the XR Origin

Delete the `ContinuousTurnProvider` so the right stick no longer rotates the rig; the player turns by physically pivoting. Confirm movement stays head-relative.

**Files:**

- Modify (scene): `Assets/Scenes/GrandStaircase.unity`

- [ ] **Step 1: Confirm Unity is in EDIT mode, then verify the turn provider is present**

Run via `Unity_RunCommand`:

```csharp
if (Application.isPlaying) { Debug.LogError("[turn] EXIT PLAY MODE FIRST — scene save will fail in Play."); }
var rig = GameObject.Find("XR Origin (XR Rig)");
int found = 0;
foreach (var mb in rig.GetComponentsInChildren<MonoBehaviour>(true))
    if (mb != null && mb.GetType().Name == "ContinuousTurnProvider") { found++; Debug.Log($"[turn] found on '{mb.gameObject.name}'"); }
Debug.Log($"[turn] ContinuousTurnProvider count = {found}");
```

Expected: not in Play mode; `count = 1` (note the GameObject name it sits on).

- [ ] **Step 2: Remove it and save the scene (EDIT mode)**

Run via `Unity_RunCommand`:

```csharp
var rig = GameObject.Find("XR Origin (XR Rig)");
int removed = 0;
foreach (var mb in rig.GetComponentsInChildren<MonoBehaviour>(true))
    if (mb != null && mb.GetType().Name == "ContinuousTurnProvider") { Object.DestroyImmediate(mb); removed++; }
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
Debug.Log($"[turn] removed {removed}; scene saved");
```

Expected: `removed 1; scene saved`.

- [ ] **Step 3: Verify removal stuck and find the move provider's forward source**

Run via `Unity_RunCommand`:

```csharp
var rig = GameObject.Find("XR Origin (XR Rig)");
int turns = 0, moves = 0;
foreach (var mb in rig.GetComponentsInChildren<MonoBehaviour>(true))
{
    if (mb == null) continue;
    string n = mb.GetType().Name;
    if (n == "ContinuousTurnProvider" || n == "SnapTurnProvider") turns++;
    if (n == "DynamicMoveProvider" || n == "ContinuousMoveProvider")
    {
        moves++;
        var fwdField = mb.GetType().GetField("m_ForwardSource") ?? mb.GetType().GetField("forwardSource");
        var fwd = fwdField != null ? fwdField.GetValue(mb) : null;
        Debug.Log($"[turn] move provider '{n}' forwardSource = {(fwd == null ? "null (defaults to head camera = head-relative)" : fwd.ToString())}");
    }
}
Debug.Log($"[turn] turn providers remaining = {turns}; move providers = {moves}");
```

Expected: `turn providers remaining = 0`; one move provider; `forwardSource` either null (head-relative by default) or pointing at the head/camera. If `forwardSource` points at something other than the head, flag it to the user — movement direction must track head facing (per spec open point).

- [ ] **Step 4: Hand back for commit (do NOT commit yourself)**

Suggested commit message:
`feat(vr): remove stick-turn — player turns by physically pivoting`

---

### Task 4: In-headset verification (user-driven)

This task requires the headset (Play-in-Editor over Air Link). The agent prepares the run and reports; the user performs the physical checks. Tune `startAngle`/`stopAngle`/`maxYawSpeed` live if needed (they are plain serialized fields — editable in the Inspector during Play, or re-baked via `Unity_RunCommand`).

**Files:** none (verification only).

- [ ] **Step 1: Enter Play mode and confirm a clean start**

User dons the headset and enters Play. Agent checks `Unity_GetConsoleLogs` — expected: no errors, no NREs from `AvatarRigDriver`.

- [ ] **Step 2: Glance test**

User looks left/right at the mirror within ~40°.
Expected: the reflected **shoulders hold still**; the head/neck turns naturally; the torso does not drift. If shoulders twitch on small glances, raise `startAngle`.

- [ ] **Step 3: Commit / 180° test**

User physically turns ~180°.
Expected: the reflected **torso swings smoothly** to face the new direction and ends aligned with the head — no residual owl neck, no jarring snap. If it lags or looks robotic, raise `maxYawSpeed`; if it stops short of aligning, lower `stopAngle`.

- [ ] **Step 4: Locomotion test**

User pushes the move stick while facing several physically-chosen directions.
Expected: they walk in the direction they are physically facing; the right stick does nothing (no artificial turn at any point).

- [ ] **Step 5: All-avatars test**

User cycles Default → Jack → Rose (A-button) and repeats the glance + commit checks on each.
Expected: identical torso-follow behavior on all three; console clean.

- [ ] **Step 6: Bake any tuned values + hand back**

If the user changed `startAngle`/`stopAngle`/`maxYawSpeed` during Play, re-apply them in EDIT mode so they persist (Play-mode changes are discarded on exit). Run via `Unity_RunCommand` (EDIT mode), substituting the agreed values:

```csharp
foreach (var d in Object.FindObjectsByType<AvatarRigDriver>(FindObjectsSortMode.None))
{
    var so = new UnityEditor.SerializedObject(d);
    so.FindProperty("startAngle").floatValue = 40f;   // <- agreed value
    so.FindProperty("stopAngle").floatValue = 5f;     // <- agreed value
    so.FindProperty("maxYawSpeed").floatValue = 180f; // <- agreed value
    so.ApplyModifiedProperties();
}
var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
Debug.Log("[torso-follow] tuned values baked + scene saved");
```

(If the defaults were left unchanged, skip the bake — defaults already apply.) Then report completion. Suggested commit message if values were baked:
`chore(vr): bake tuned torso-follow thresholds`

---

## Self-Review

**Spec coverage:**

- "Delete artificial stick-turn entirely, no fallback" → Task 3. ✅
- "Keep locomotion head-relative" → Task 3 Step 3 verifies the move provider's forward source. ✅
- "Mirror torso: glance = shoulders hold, commit = follows, no owl neck" → Task 1 (logic) + Task 2 (wiring) + Task 4 Steps 2–3 (headset). ✅
- "Hysteresis dead-zone with startAngle/stopAngle/maxYawSpeed serialized for live tuning" → Task 2 Step 1. ✅
- "Applies uniformly to Default/Jack/Rose" → Task 2 Step 4 + Task 4 Step 5. ✅
- "No changes to IPoseSource / move provider / collision / mirror shader" → only `AvatarRigDriver.cs` + one component removal; IPoseSource untouched. ✅
- Spec risk "DynamicMoveProvider forward source" → Task 3 Step 3 explicitly checks and flags. ✅

**Placeholder scan:** No TBD/TODO; every code step shows complete code; every command states expected output. ✅

**Type consistency:** `StepYaw(float, float, float, float, float, float, ref bool)` is defined in Task 1 and called with matching arguments in Task 2 Step 2. Field names `followYaw`/`startAngle`/`stopAngle`/`maxYawSpeed`/`yawFollowing` are consistent across Tasks 2 and 4. ✅
