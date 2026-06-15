# First-Person Self-View Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The player looks down through the HMD and sees their own full body (torso/legs/hands) of the active mirror character, with the head hidden from the HMD camera but still visible in the mirror.

**Architecture:** Two static scene changes (un-cull layer 8 on the Main Camera; disable the controller visual meshes) plus one shared `SelfViewManager` on the `MirrorAvatars` container. The manager reads the active avatar from `MirrorCharacterSwitcher`, and collapses that avatar's Humanoid Head bone to ~0 for the HMD camera and restores it for the mirror camera, toggled through URP's `RenderPipelineManager` begin/end camera-render events. Works for all current + future characters with zero per-character setup.

**Tech Stack:** Unity 6, URP (`UniversalRenderPipeline`, `RenderPipelineManager` events), Humanoid `Animator` bone access, Meta Quest 3 / PCVR over (Air) Link. No automated test framework — verification is via Unity-MCP `RunCommand` pure-logic checks, edit-mode render-to-PNG, and in-headset Play.

**Project conventions (read before starting):**

- Verified scene facts: Main Camera `cullingMask = -257` (culls only layer 8 "PlayerBody"); mirror reflection cam `reflectLayers = -513` (excludes only layer 9); `MirrorReflection` spawns a camera named `MirrorCam_<mirrorName>`; switcher list is `[Default, Rose, JackV2]`; layer 9 = the `Left/Right Controller Visual/UniversalController/*` mesh renderers.
- **Git:** never commit on the user's behalf. Each "Checkpoint" step means: stop, summarize the change, and let the **user** commit.
- **Unity-MCP `RunCommand`:** class MUST be `internal class CommandScript : IRunCommand`; use `result.Log/LogError`, `result.RegisterObjectModification(obj)` before edits, `result.DestroyObject(obj)`; avoid `System.Reflection`/`BindingFlags` and LINQ over `HashSet` (assembly-reference errors — use `List`); scene saves work only in **Edit** mode, runtime field sets work in Play; retry once if the bridge reports "no fresh discovery files" during a domain reload.
- **Render-to-PNG (edit mode):** create a temp `Camera` + `RenderTexture` (sRGB), set `cullingMask` to include layer 8, position it, `cam.Render()`, `ReadPixels` → `EncodeToPNG` → `File.WriteAllBytes` to a path **outside `Assets/`** (project root), then `Read` the PNG. Boost `RenderSettings.ambientLight` (save/restore) so the unlit edit-mode avatar is visible.

---

## File Structure

- **Create:** `Assets/Game/Scripts/VR/SelfViewManager.cs` — the entire feature's runtime code. One responsibility: per-camera head visibility for the active avatar. Pure helpers (`Classify`, `ShouldCollapse`) are static + testable; the `MonoBehaviour` owns the camera-event subscription and active-avatar resolution.
- **Modify (scene `Assets/.../GrandStaircase.unity` via `RunCommand`):** Main Camera `cullingMask`; `Left/Right Controller Visual` active state; add + wire the `SelfViewManager` component on `MirrorAvatars`.
- **No change** to `MirrorReflection.cs`, `AvatarRigDriver.cs`, `FingerPoseDriver.cs`, `MirrorCharacterSwitcher.cs`.

---

## Task 1: SPIKE — validate per-camera bone-scale re-skinning (edit mode, throwaway, NO commit)

**Why:** The whole approach assumes Unity re-skins the mesh per render, so changing a bone's scale between two renders in one frame affects each independently. If skinning is cached once per frame, the head would hide from neither or both. Prove this **before** building anything. Headset not required.

**Files:** none created — a single `RunCommand`.

- [ ] **Step 1: Render the active avatar's head twice in one frame — full, then collapsed**

Run this via Unity-MCP `RunCommand` (Edit mode). It finds the active avatar (`Default`), its Head bone, renders a head close-up to `spike_head_full.png` with the head at normal scale, then sets the head bone scale to ~0 and renders again to `spike_head_collapsed.png` — both in the same synchronous call (same frame).

```csharp
using UnityEngine;
using UnityEditor;
using System.IO;
using Unity.XR.CoreUtils;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        // Active avatar = first enabled child of MirrorAvatars with an Animator
        Animator anim = null;
        var allAnim = Object.FindObjectsByType<Animator>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var a in allAnim)
        {
            var p = a.transform; bool underMirror = false;
            while (p != null) { if (p.name == "MirrorAvatars") { underMirror = true; break; } p = p.parent; }
            if (underMirror && a.isHuman) { anim = a; break; }
        }
        if (anim == null) { result.LogError("No active humanoid avatar under MirrorAvatars."); return; }
        var head = anim.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) { result.LogError("No head bone."); return; }
        result.Log("Avatar = {0}, head = {1}", anim.gameObject, head.name);

        // Temp camera looking at the head from the front (avatar faces +Z by convention).
        var camGO = new GameObject("SpikeCam");
        var cam = camGO.AddComponent<Camera>();
        cam.cullingMask = ~0;            // see layer 8
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
        cam.nearClipPlane = 0.01f;
        Vector3 hp = head.position;
        camGO.transform.position = hp + new Vector3(0f, 0.05f, 0.6f);
        camGO.transform.LookAt(hp);

        var savedAmbient = RenderSettings.ambientLight;
        RenderSettings.ambientLight = Color.white;

        var rt = new RenderTexture(512, 512, 16) { name = "SpikeRT" };
        rt.Create();
        cam.targetTexture = rt;

        string root = Directory.GetParent(Application.dataPath).FullName;

        // Render 1: head full
        Vector3 orig = head.localScale;
        RenderToPng(cam, rt, Path.Combine(root, "spike_head_full.png"));

        // Render 2: head collapsed (same frame, after a bone-scale change)
        head.localScale = new Vector3(1e-4f, 1e-4f, 1e-4f);
        RenderToPng(cam, rt, Path.Combine(root, "spike_head_collapsed.png"));

        // Cleanup
        head.localScale = orig;
        RenderSettings.ambientLight = savedAmbient;
        cam.targetTexture = null;
        rt.Release(); Object.DestroyImmediate(rt);
        Object.DestroyImmediate(camGO);
        result.Log("Wrote spike_head_full.png + spike_head_collapsed.png to {0}", root);
    }

    static void RenderToPng(Camera cam, RenderTexture rt, string path)
    {
        cam.Render();
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
```

- [ ] **Step 2: Inspect both PNGs**

`Read` `spike_head_full.png` and `spike_head_collapsed.png` from the project root.
Expected: **full** shows a head; **collapsed** shows the head gone (collapsed to the neck), torso/shoulders unchanged.

- [ ] **Step 3: Decision gate**

- **PASS** (collapsed differs — head gone): per-camera bone scaling works → continue to Task 2.
- **FAIL** (both identical — head present or absent in both): Unity skinned once for the frame. STOP. Switch to Approach C (custom shader head-clip), re-open the spec's fallback, and re-plan. Do **not** proceed with Tasks 2–6 as written.

No commit (throwaway; the command cleans up its temp objects).

---

## Task 2: Create `SelfViewManager.cs` (pure helpers + component)

**Files:**

- Create: `Assets/Game/Scripts/VR/SelfViewManager.cs`

- [ ] **Step 1: Write the component**

```csharp
using UnityEngine;
using UnityEngine.Rendering;

// First-person self-view. Makes the ACTIVE mirror avatar's body visible to the HMD camera while
// hiding ONLY its head for that camera -- the head bone is pinned to the HMD, so its mesh would
// otherwise wrap the near-plane. The head still renders in the MIRROR.
//
// One shared manager (not per-avatar): it reads the active avatar from MirrorCharacterSwitcher,
// so every current and future character is covered with zero per-character setup. Head-hiding
// needs no per-rig calibration -- it collapses the Humanoid Head bone's scale for the HMD camera
// and restores it for the mirror camera, toggled through URP's camera-render events.
//
// ORDERING: MirrorReflection renders the reflection NESTED inside the main camera's
// beginCameraRendering (it calls RenderSingleCamera there). So end(MirrorCam) runs after the
// reflection draw and before the main draw -- the authoritative "collapse before the main draw"
// point, which makes the result independent of subscriber registration order. It also works when
// no mirror is in view (only the main-cam events fire). The toggle changes only the head bone's
// SCALE; AvatarRigDriver writes head ROTATION in LateUpdate and FingerPoseDriver touches only
// finger bones, so there is no conflict.
[DisallowMultipleComponent]
public class SelfViewManager : MonoBehaviour
{
    public enum CamKind { Main, Mirror, Other }
    public enum Phase { Begin, End }

    [Tooltip("Master switch (dev kill-switch). When off, the head is never collapsed.")]
    public bool selfViewEnabled = true;

    [Tooltip("Switcher that tracks the active avatar. The active character's head bone is hidden.")]
    public MirrorCharacterSwitcher switcher;

    [Tooltip("The HMD / main camera (XR Origin camera). The body shows here with the head hidden.")]
    public Camera mainCamera;

    [Tooltip("Head-bone scale used to collapse the head for the HMD camera (near-zero).")]
    public float headHideScale = 1e-4f;

    [Tooltip("Reflection cameras are named with this prefix (MirrorReflection spawns 'MirrorCam_<name>').")]
    public string mirrorCamNamePrefix = "MirrorCam_";

    int resolvedIndex = -1;
    Transform headBone;
    Vector3 originalScale = Vector3.one;

    // Pure: classify a rendering camera. Main = the assigned HMD cam; Mirror = a reflection cam
    // (name prefix); everything else (scene view, previews, non-Game) = Other and is left untouched.
    public static CamKind Classify(Camera cam, Camera mainCam, string mirrorPrefix)
    {
        if (cam == null) return CamKind.Other;
        if (cam.cameraType != CameraType.Game) return CamKind.Other;
        if (mainCam != null && cam == mainCam) return CamKind.Main;
        if (!string.IsNullOrEmpty(mirrorPrefix) && cam.name.StartsWith(mirrorPrefix)) return CamKind.Mirror;
        return CamKind.Other;
    }

    // Pure: should the head be collapsed for this (camera kind, phase)?
    // true = collapse (~0), false = restore (full), null = no change (leave as-is).
    public static bool? ShouldCollapse(CamKind kind, Phase phase)
    {
        switch (kind)
        {
            case CamKind.Mirror: return phase == Phase.End;    // full on begin, collapse on end
            case CamKind.Main:   return phase == Phase.Begin;  // collapse on begin, restore on end
            default:             return null;                  // Other: untouched
        }
    }

    void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += OnBegin;
        RenderPipelineManager.endCameraRendering += OnEnd;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBegin;
        RenderPipelineManager.endCameraRendering -= OnEnd;
        RestoreHead(); // leave the active avatar's head full when disabled
    }

    void LateUpdate()
    {
        if (switcher == null) return;
        if (switcher.CurrentIndex != resolvedIndex)
        {
            RestoreHead();        // restore the previously-collapsed avatar before switching away
            ResolveActiveHead();
        }
    }

    void ResolveActiveHead()
    {
        resolvedIndex = switcher.CurrentIndex;
        headBone = null;
        if (switcher.characters == null || resolvedIndex < 0 || resolvedIndex >= switcher.characters.Count) return;
        var go = switcher.characters[resolvedIndex];
        if (go == null) return;
        var anim = go.GetComponentInChildren<Animator>();
        if (anim == null || !anim.isHuman) return;
        headBone = anim.GetBoneTransform(HumanBodyBones.Head);
        if (headBone != null) originalScale = headBone.localScale;
    }

    void RestoreHead()
    {
        if (headBone != null) headBone.localScale = originalScale;
    }

    void OnBegin(ScriptableRenderContext ctx, Camera cam) => Apply(cam, Phase.Begin);
    void OnEnd(ScriptableRenderContext ctx, Camera cam) => Apply(cam, Phase.End);

    void Apply(Camera cam, Phase phase)
    {
        if (!selfViewEnabled || headBone == null) return;
        bool? collapse = ShouldCollapse(Classify(cam, mainCamera, mirrorCamNamePrefix), phase);
        if (collapse == null) return;
        headBone.localScale = collapse.Value
            ? new Vector3(headHideScale, headHideScale, headHideScale)
            : originalScale;
    }
}
```

- [ ] **Step 2: Verify it compiles + run the pure-logic check**

Run via Unity-MCP `RunCommand` (this compiles the project, so a compile error surfaces here, then exercises the two pure functions on synthetic inputs):

```csharp
using UnityEngine;
using System.Text;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var sb = new StringBuilder();
        int fails = 0;

        // ShouldCollapse truth table
        void Chk(string name, bool? got, bool? want)
        { bool ok = got == want; if (!ok) fails++; sb.AppendLine((ok ? "PASS " : "FAIL ") + name + " got=" + got + " want=" + want); }

        Chk("Mirror.Begin->full",   SelfViewManager.ShouldCollapse(SelfViewManager.CamKind.Mirror, SelfViewManager.Phase.Begin), false);
        Chk("Mirror.End->collapse", SelfViewManager.ShouldCollapse(SelfViewManager.CamKind.Mirror, SelfViewManager.Phase.End),   true);
        Chk("Main.Begin->collapse", SelfViewManager.ShouldCollapse(SelfViewManager.CamKind.Main,   SelfViewManager.Phase.Begin), true);
        Chk("Main.End->full",       SelfViewManager.ShouldCollapse(SelfViewManager.CamKind.Main,   SelfViewManager.Phase.End),   false);
        Chk("Other.Begin->null",    SelfViewManager.ShouldCollapse(SelfViewManager.CamKind.Other,  SelfViewManager.Phase.Begin), null);
        Chk("Other.End->null",      SelfViewManager.ShouldCollapse(SelfViewManager.CamKind.Other,  SelfViewManager.Phase.End),   null);

        // Classify: null camera -> Other
        bool nullOk = SelfViewManager.Classify(null, null, "MirrorCam_") == SelfViewManager.CamKind.Other;
        if (!nullOk) fails++; sb.AppendLine((nullOk ? "PASS " : "FAIL ") + "Classify(null)->Other");

        sb.AppendLine(fails == 0 ? "ALL PASS" : (fails + " FAILED"));
        result.Log(sb.ToString());
    }
}
```

Expected: `ALL PASS`. (Camera-instance classification — Main vs Mirror by name/reference — is covered by the in-headset check in Task 5, since it needs real cameras.)

- [ ] **Step 3: Checkpoint** — summarize the new file; let the user commit.

---

## Task 3: Static scene config — un-cull layer 8 + hide controller visuals

**Files:** Modify `GrandStaircase.unity` (via `RunCommand`, Edit mode).

- [ ] **Step 1: Apply the two scene changes and save**

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Text;
using Unity.XR.CoreUtils;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var sb = new StringBuilder();
        if (Application.isPlaying) { result.LogError("In Play mode - abort. Run in Edit mode."); return; }

        // 1) Main Camera: add layer 8 so the active avatar renders directly to the HMD.
        var origin = Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
        var cam = origin != null ? origin.Camera : null;
        if (cam == null) { result.LogError("No XR Origin camera."); return; }
        result.RegisterObjectModification(cam);
        cam.cullingMask |= (1 << 8);
        sb.AppendLine("Main Camera cullingMask -> " + cam.cullingMask + " (layer8 visible=" + ((cam.cullingMask & (1<<8)) != 0) + ")");

        // 2) Disable the controller visual meshes (kill double-hands). Tracking/interactors are on
        //    the parent controller / layer 0 and are untouched.
        int disabled = 0;
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in all)
        {
            if (t == null) continue;
            if (t.name == "Left Controller Visual" || t.name == "Right Controller Visual")
            {
                result.RegisterObjectModification(t.gameObject);
                t.gameObject.SetActive(false);
                disabled++;
                sb.AppendLine("Disabled: " + t.name);
            }
        }
        if (disabled != 2) sb.AppendLine("WARNING: expected 2 controller-visual objects, disabled " + disabled);

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("Saved=" + EditorSceneManager.SaveScene(scene));
        result.Log(sb.ToString());
    }
}
```

Expected: `cullingMask` now includes layer 8; two `* Controller Visual` objects disabled; `Saved=True`.

- [ ] **Step 2: Checkpoint** — summarize the scene changes; let the user commit.

---

## Task 4: Add + wire `SelfViewManager` on `MirrorAvatars`

**Files:** Modify `GrandStaircase.unity` (via `RunCommand`, Edit mode).

- [ ] **Step 1: Add the component and assign references, then save**

```csharp
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Text;
using Unity.XR.CoreUtils;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        var sb = new StringBuilder();
        if (Application.isPlaying) { result.LogError("In Play mode - abort. Run in Edit mode."); return; }

        // Find MirrorAvatars
        GameObject container = null;
        var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var t in all) if (t != null && t.name == "MirrorAvatars") { container = t.gameObject; break; }
        if (container == null) { result.LogError("No MirrorAvatars container."); return; }

        var mgr = container.GetComponent<SelfViewManager>();
        if (mgr == null) { mgr = container.AddComponent<SelfViewManager>(); result.Log("Added SelfViewManager."); }
        else result.Log("SelfViewManager already present; re-wiring.");
        result.RegisterObjectModification(mgr);

        mgr.switcher = Object.FindFirstObjectByType<MirrorCharacterSwitcher>(FindObjectsInactive.Include);
        var origin = Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
        mgr.mainCamera = origin != null ? origin.Camera : null;
        mgr.selfViewEnabled = true;
        mgr.headHideScale = 1e-4f;
        mgr.mirrorCamNamePrefix = "MirrorCam_";

        sb.AppendLine("switcher=" + (mgr.switcher != null) + " mainCamera=" + (mgr.mainCamera != null ? mgr.mainCamera.name : "NULL"));

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        sb.AppendLine("Saved=" + EditorSceneManager.SaveScene(scene));
        result.Log(sb.ToString());
    }
}
```

Expected: `switcher=True`, `mainCamera=Main Camera`, `Saved=True`.

- [ ] **Step 2: Checkpoint** — summarize the wiring; let the user commit.

---

## Task 5: In-headset verification (Play over Link) — acceptance gate

**Files:** none. This is the human-in-the-loop acceptance test.

- [ ] **Step 1: Enter Play and look down**

Have the user enter Play (Air Link), stand, and look down at their body.
Expected: full body visible — torso, **legs**, and hands — no head blob / near-plane clipping in the lower view. The avatar's own hands are the visible hands (no floating controller models).

- [ ] **Step 2: Check the mirror**

Have the user face the mirror.
Expected: the reflection shows the **complete** body **including the head**, exactly as before this feature.

- [ ] **Step 3: Cycle characters**

Press the character-cycle button through `Default → Rose → JackV2`.
Expected: self-view (body visible, head hidden from HMD, head present in mirror) works on **each** with no per-character setup; switching does not leave any avatar's head collapsed in the mirror.

- [ ] **Step 4: Confirm interaction + console**

Confirm the quit button / any poke interaction still works (controller-visual hide didn't break tracking/interactors), and the Console is clean (no per-frame errors from the camera-event handlers).

- [ ] **Step 5 (fallback capture if no headset available):** Reproduce the mirror-vs-HMD check in Play with a dual render-to-PNG — one capture from the HMD pose with a mask including layer 8 (expect no head), and the mirror RT (expect a head). Use the temp-camera method from the project conventions.

- [ ] **Step 6: Checkpoint** — if all pass, the feature is done; let the user commit any final scene/tuning tweaks.

---

## Task 6: Build parity (standalone `.exe`) — after headset acceptance

**Files:** none (build + run).

- [ ] **Step 1:** Build the standalone `.exe` and run it on the lab-style setup (or locally over Link). Confirm self-view behaves as in Editor Play: body visible to HMD, head hidden, head in mirror, all three characters. The technique is render-pipeline-event based (no Editor-only code), so it should carry. If a build lacks it, capture `Player.log` (`AppData/LocalLow/DefaultCompany/titanic/`) and diagnose — likely a camera-name or culling-mask difference, not a logic defect.

- [ ] **Step 2: Checkpoint** — note build result; let the user commit if anything changed.

---

## Notes for future characters

Adding a new character needs **no self-view work**: register it in `MirrorCharacterSwitcher.characters` (already required) and `SelfViewManager` covers it automatically (it resolves the active avatar's head bone from the switcher). This contrasts with head/wrist calibration, which IS per-character.
