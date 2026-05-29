# Titanic Grand Staircase Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **✅ STATUS: COMPLETE (2026-05-29).** Scene, lighting, first-person rig, and the real-time planar mirror are all done. The mirror's final visual issue (washed-out/low-contrast reflection) was traced to `GL.invertCulling = true` rendering back-faces and fixed by removing it; the reflection now matches the direct view. No outstanding work.

**Goal:** Build a first-person Unity 6 scene set inside a Titanic-inspired grand staircase, walled-in and lit, with a real-time planar mirror that reflects the player's character.

**Architecture:** Pull two Sketchfab models (staircase + mirror) via the Blender MCP, FBX-export with Unity-friendly settings, import to `Assets/Game/Models/`, extract textures + materials. Build the scene in `Assets/Scenes/GrandStaircase.unity`: ProBuilder walls around the staircase, warm interior lighting, a first-person rig derived from the existing `PlayerArmature` (Starter Assets ThirdPerson), and a render-texture-based mirror script. All Unity-side automation runs via `mcp__unity-mcp__Unity_RunCommand`; visual verification via scene/camera capture.

**Tech Stack:** Unity 6000.4.0f1, URP 17.4.0, ProBuilder 6.0.9, Starter Assets — ThirdPerson | URP, Input System 1.19, Blender MCP, Unity MCP.

**Reference spec:** `docs/superpowers/specs/2026-05-17-titanic-grand-staircase-design.md`

**User rule:** Never run `git commit` on the user's behalf. Every "Suggest commit" step lists a suggested message — the user runs the actual commit.

---

## Pre-flight (do once before starting)

- [ ] Confirm Unity Editor is open with the `titanic` project loaded (the Unity MCP needs the editor running).
- [ ] Confirm the Blender MCP add-on has a valid Sketchfab API key configured in its preferences (the user set this).
- [ ] Confirm Blender is open with the MCP server addon active.

---

## Task 1: Folder scaffolding and new scene

**Files:**

- Create: `Assets/Game/Models/GrandStaircase/.keep`
- Create: `Assets/Game/Models/Mirror/.keep`
- Create: `Assets/Game/Materials/.keep`
- Create: `Assets/Game/Prefabs/.keep`
- Create: `Assets/Game/Scripts/.keep`
- Create: `Assets/Scenes/GrandStaircase.unity`

- [ ] **Step 1: Create the Game folder tree in Unity**

Run `mcp__unity-mcp__Unity_RunCommand` with this C# (it creates folders via the AssetDatabase so .meta files are generated correctly):

```csharp
using UnityEditor;
using UnityEngine;

string[] folders = new[] {
    "Assets/Game",
    "Assets/Game/Models",
    "Assets/Game/Models/GrandStaircase",
    "Assets/Game/Models/Mirror",
    "Assets/Game/Materials",
    "Assets/Game/Prefabs",
    "Assets/Game/Scripts",
};

foreach (var f in folders)
{
    var parent = System.IO.Path.GetDirectoryName(f).Replace('\\','/');
    var leaf = System.IO.Path.GetFileName(f);
    if (!AssetDatabase.IsValidFolder(f))
    {
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
AssetDatabase.Refresh();
Debug.Log("Game folder scaffolding created.");
```

- [ ] **Step 2: Verify the folders exist**

Run `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: `"Game folder scaffolding created."` log entry, no errors.

- [ ] **Step 3: Create the new scene `GrandStaircase.unity`**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
// Add a default directional light + skybox so the empty scene isn't pitch-black
var sun = new GameObject("DirectionalLight");
var light = sun.AddComponent<Light>();
light.type = LightType.Directional;
light.intensity = 0.3f;
light.color = new Color(1.0f, 0.95f, 0.85f);
sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

bool ok = EditorSceneManager.SaveScene(scene, "Assets/Scenes/GrandStaircase.unity");
Debug.Log($"GrandStaircase scene saved: {ok}");
```

- [ ] **Step 4: Verify scene was saved**

Run `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: `"GrandStaircase scene saved: True"`, no errors.

- [ ] **Step 5: Suggest commit**

Suggest the user commit. Suggested message:

```
chore: scaffold Game folder tree and create GrandStaircase scene
```

---

## Task 2: Grand Staircase — Blender pipeline (download + clean + export)

**Files:**

- Output: `Assets/Game/Models/GrandStaircase/GrandStaircase.fbx`

- [ ] **Step 1: Download the staircase from Sketchfab into Blender**

Call `mcp__blender-mcp__download_sketchfab_model` with:

```
uid:         05290539521c4581a627a47f5d366857
target_size: 12.0
```

Expected: tool returns success and the Blender scene now contains the staircase mesh hierarchy.

- [ ] **Step 2: Inspect what came in**

Call `mcp__blender-mcp__execute_blender_code` with:

```python
import bpy
print("Objects:", [(o.name, o.type) for o in bpy.data.objects])
for o in bpy.data.objects:
    if o.type == 'MESH':
        print(f"  MESH {o.name}: dims={tuple(round(x,2) for x in o.dimensions)} loc={tuple(round(x,2) for x in o.location)}")
print("Images:", [(i.name, i.size[0], i.size[1], i.packed_file is not None) for i in bpy.data.images])
```

Expected: a list of mesh objects with non-zero dimensions and packed images (`packed_file is not None`). Note the largest mesh's dimensions for the next step's sanity check.

- [ ] **Step 3: Delete stray Sketchfab decoratives, apply transforms, recenter**

Call `mcp__blender-mcp__execute_blender_code`:

```python
import bpy

# Delete Sketchfab demo helpers (cameras, lights, icospheres, etc.)
for o in list(bpy.data.objects):
    if o.type in {'CAMERA', 'LIGHT', 'EMPTY'} and o.name != 'StaircaseRoot':
        bpy.data.objects.remove(o, do_unlink=True)
    elif o.type == 'MESH' and any(x in o.name.lower() for x in ['icosphere','torus','demo','helper']):
        bpy.data.objects.remove(o, do_unlink=True)

# Select all remaining meshes
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects:
    if o.type == 'MESH':
        o.select_set(True)
        bpy.context.view_layer.objects.active = o

# Apply transforms
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
print("Cleanup done. Remaining objects:", [o.name for o in bpy.data.objects if o.type == 'MESH'])
```

- [ ] **Step 4: Export to FBX with Unity-friendly settings**

Call `mcp__blender-mcp__execute_blender_code`:

```python
import bpy, os
project_root = r"C:\Users\Eyal\dev\hci-unity\titanic"
out_path = os.path.join(project_root, "Assets", "Game", "Models", "GrandStaircase", "GrandStaircase.fbx")

# Make sure all meshes are selected
bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects:
    if o.type == 'MESH':
        o.select_set(True)

bpy.ops.export_scene.fbx(
    filepath=out_path,
    use_selection=True,
    mesh_smooth_type='EDGE',
    path_mode='COPY',
    embed_textures=True,
    axis_forward='-Z',
    axis_up='Y',
    bake_space_transform=True,
    add_leaf_bones=False,
    bake_anim=False,
    apply_unit_scale=True,
    global_scale=1.0,
)
size_mb = os.path.getsize(out_path) / (1024*1024)
print(f"Exported {out_path} ({size_mb:.1f} MB)")
```

Expected: log line with a multi-MB file size (indicates textures embedded). If the size is <1 MB, textures likely didn't embed — re-check Step 3 and packed image status.

- [ ] **Step 5: Verify the file is on disk and Unity has noticed it**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;
AssetDatabase.Refresh();
var importer = AssetImporter.GetAtPath("Assets/Game/Models/GrandStaircase/GrandStaircase.fbx") as ModelImporter;
Debug.Log(importer == null ? "FBX NOT FOUND" : $"FBX imported. Materials: {importer.sourceMaterials.Length}");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: `"FBX imported. Materials: N"` with N ≥ 1, no import errors.

- [ ] **Step 6: Suggest commit**

Suggested message:

```
feat: import Titanic grand staircase model (Sketchfab via Blender MCP)
```

---

## Task 3: Grand Staircase — Unity material/texture extraction and prefab

**Files:**

- Create (by extraction): `Assets/Game/Models/GrandStaircase/GrandStaircase_Textures/*.png`
- Create (by extraction): `Assets/Game/Materials/<MaterialName>.mat` (one per embedded material)
- Create: `Assets/Game/Prefabs/Staircase.prefab`

- [ ] **Step 1: Extract embedded textures from the FBX**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;

string fbx = "Assets/Game/Models/GrandStaircase/GrandStaircase.fbx";
string texDir = "Assets/Game/Models/GrandStaircase/GrandStaircase_Textures";
if (!AssetDatabase.IsValidFolder(texDir))
{
    AssetDatabase.CreateFolder("Assets/Game/Models/GrandStaircase", "GrandStaircase_Textures");
}

var importer = (ModelImporter)AssetImporter.GetAtPath(fbx);
importer.ExtractTextures(texDir);
AssetDatabase.WriteImportSettingsIfDirty(fbx);
AssetDatabase.ImportAsset(fbx, ImportAssetOptions.ForceUpdate);
Debug.Log("Textures extracted to: " + texDir);
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: log line and no errors. Some texture maps may not extract (Unity's heuristic) — that's OK for now.

- [ ] **Step 2: Extract each embedded material to `Assets/Game/Materials/`**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;
using System.Linq;

string fbx = "Assets/Game/Models/GrandStaircase/GrandStaircase.fbx";
var subs = AssetDatabase.LoadAllAssetsAtPath(fbx);
int extracted = 0;
foreach (var sub in subs)
{
    if (sub is Material mat && !string.IsNullOrEmpty(mat.name))
    {
        string target = $"Assets/Game/Materials/{mat.name}.mat";
        string err = AssetDatabase.ExtractAsset(mat, target);
        if (string.IsNullOrEmpty(err)) extracted++;
        else Debug.LogWarning($"ExtractAsset failed for {mat.name}: {err}");
    }
}
AssetDatabase.WriteImportSettingsIfDirty(fbx);
AssetDatabase.ImportAsset(fbx, ImportAssetOptions.ForceUpdate);
Debug.Log($"Extracted {extracted} material(s).");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: `"Extracted N material(s)."` with N ≥ 1.

**Important:** do NOT set `importer.materialLocation = ModelImporterMaterialLocation.External` — that property is deprecated in Unity 6 and will spam warnings.

- [ ] **Step 3: Detect materials with missing `_BaseMap` and patch them**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Linq;

string texDir = "Assets/Game/Models/GrandStaircase/GrandStaircase_Textures";
var texPaths = AssetDatabase.FindAssets("t:Texture", new[] { texDir })
    .Select(AssetDatabase.GUIDToAssetPath).ToArray();
Debug.Log("Extracted textures: " + string.Join(", ", texPaths.Select(Path.GetFileName)));

var matPaths = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Game/Materials" })
    .Select(AssetDatabase.GUIDToAssetPath);
foreach (var mp in matPaths)
{
    var m = AssetDatabase.LoadAssetAtPath<Material>(mp);
    if (m == null || !m.HasProperty("_BaseMap")) continue;
    var current = m.GetTexture("_BaseMap");
    if (current != null) continue;
    // try to match by name: look for a texture whose name contains the material name or "BaseColor"/"Albedo"
    var match = texPaths.FirstOrDefault(p =>
        Path.GetFileNameWithoutExtension(p).ToLower().Contains(m.name.ToLower()) ||
        Path.GetFileNameWithoutExtension(p).ToLower().Contains("basecolor") ||
        Path.GetFileNameWithoutExtension(p).ToLower().Contains("albedo"));
    if (match != null)
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture>(match);
        m.SetTexture("_BaseMap", tex);
        EditorUtility.SetDirty(m);
        Debug.Log($"Wired _BaseMap on {m.name} -> {Path.GetFileName(match)}");
    }
    else
    {
        Debug.LogWarning($"No _BaseMap match for {m.name}. May need manual wiring.");
    }
}
AssetDatabase.SaveAssets();
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: at least the "Extracted textures" log + zero or more wiring logs. If any "No \_BaseMap match" warnings appear, note which materials and plan to fix them manually after visual verification.

- [ ] **Step 4: Make a Staircase prefab from the imported model**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;

string fbx = "Assets/Game/Models/GrandStaircase/GrandStaircase.fbx";
var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
var inst = PrefabUtility.InstantiatePrefab(model) as GameObject;
inst.name = "Staircase";
inst.transform.position = Vector3.zero;
inst.transform.rotation = Quaternion.identity;

string prefabPath = "Assets/Game/Prefabs/Staircase.prefab";
PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
Object.DestroyImmediate(inst);
Debug.Log("Staircase prefab saved at " + prefabPath);
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: log line, no errors.

- [ ] **Step 5: Measure the staircase prefab's world bounds**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;
using System.Linq;

var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Staircase.prefab");
var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
var rends = tmp.GetComponentsInChildren<Renderer>();
if (rends.Length == 0) { Debug.LogError("No renderers on Staircase prefab"); }
else
{
    var b = rends[0].bounds;
    for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
    Debug.Log($"Staircase bounds: size={b.size} center={b.center}");
}
Object.DestroyImmediate(tmp);
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: `"Staircase bounds: size=(W,H,D) ..."`. Sanity: H should be ≈ 8–14 m. If H is wildly different from 12 (e.g. 0.5 m or 80 m), the unit scale or target_size needs a fix-up in Blender re-export.

- [ ] **Step 6: Capture a scene-view of the staircase to visually verify**

First add the staircase to the open scene temporarily so the scene view has something to look at:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var scene = EditorSceneManager.OpenScene("Assets/Scenes/GrandStaircase.unity");
var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Staircase.prefab");
var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
inst.transform.position = Vector3.zero;

// Frame in scene view
Selection.activeGameObject = inst;
SceneView.lastActiveSceneView?.FrameSelected();

EditorSceneManager.SaveScene(scene);
Debug.Log("Staircase placed in scene.");
```

Then call `mcp__unity-mcp__Unity_SceneView_Capture2DScene`. Expected: image shows the staircase mesh — textured (not flat white), correct silhouette vs. Sketchfab preview.

- [ ] **Step 7: Suggest commit**

Suggested message:

```
feat: extract staircase materials, build Staircase prefab, place in scene
```

---

## Task 4: Mirror — Blender pipeline (download + clean + export)

**Files:**

- Output: `Assets/Game/Models/Mirror/Mirror.fbx`

- [ ] **Step 1: Download the mirror from Sketchfab into Blender**

Call `mcp__blender-mcp__download_sketchfab_model`:

```
uid:         a1c6daa19b184e6aa7f02ba68dd1d985
target_size: 2.0
```

Expected: success.

- [ ] **Step 2: Inspect**

Call `mcp__blender-mcp__execute_blender_code`:

```python
import bpy
print("Objects:", [(o.name, o.type) for o in bpy.data.objects])
for o in bpy.data.objects:
    if o.type == 'MESH':
        print(f"  MESH {o.name}: dims={tuple(round(x,2) for x in o.dimensions)}")
print("Images:", [(i.name, i.size[0], i.size[1], i.packed_file is not None) for i in bpy.data.images])
```

Expected: mirror frame mesh + likely a flat-plane "glass" mesh. Note the names — we'll need to identify the reflective surface in Unity later.

- [ ] **Step 3: Clean + apply transforms**

Same code pattern as Task 2 Step 3.

- [ ] **Step 4: Export FBX**

Call `mcp__blender-mcp__execute_blender_code`:

```python
import bpy, os
out_path = r"C:\Users\Eyal\dev\hci-unity\titanic\Assets\Game\Models\Mirror\Mirror.fbx"

bpy.ops.object.select_all(action='DESELECT')
for o in bpy.data.objects:
    if o.type == 'MESH':
        o.select_set(True)

bpy.ops.export_scene.fbx(
    filepath=out_path,
    use_selection=True,
    mesh_smooth_type='EDGE',
    path_mode='COPY',
    embed_textures=True,
    axis_forward='-Z',
    axis_up='Y',
    bake_space_transform=True,
    add_leaf_bones=False,
    bake_anim=False,
    apply_unit_scale=True,
    global_scale=1.0,
)
print(f"Exported {out_path} ({os.path.getsize(out_path)/(1024*1024):.1f} MB)")
```

- [ ] **Step 5: Refresh Unity, sanity-check the import**

Same code pattern as Task 2 Step 5, with paths swapped to Mirror.

- [ ] **Step 6: Suggest commit**

```
feat: import mirror model (Sketchfab via Blender MCP)
```

---

## Task 5: Mirror — Unity material/texture extraction, identify reflective surface, build prefab

**Files:**

- Create: `Assets/Game/Models/Mirror/Mirror_Textures/`
- Create: extracted `.mat` files in `Assets/Game/Materials/`
- Create: `Assets/Game/Prefabs/Mirror.prefab`

- [ ] **Step 1: Extract textures**

Same pattern as Task 3 Step 1, swap paths:

```csharp
using UnityEditor;
using UnityEngine;

string fbx = "Assets/Game/Models/Mirror/Mirror.fbx";
string texDir = "Assets/Game/Models/Mirror/Mirror_Textures";
if (!AssetDatabase.IsValidFolder(texDir))
    AssetDatabase.CreateFolder("Assets/Game/Models/Mirror", "Mirror_Textures");
var importer = (ModelImporter)AssetImporter.GetAtPath(fbx);
importer.ExtractTextures(texDir);
AssetDatabase.WriteImportSettingsIfDirty(fbx);
AssetDatabase.ImportAsset(fbx, ImportAssetOptions.ForceUpdate);
Debug.Log("Mirror textures extracted to " + texDir);
```

- [ ] **Step 2: Extract materials**

Same pattern as Task 3 Step 2, swap `fbx` path to `Assets/Game/Models/Mirror/Mirror.fbx`. Verify `Mirror_*` materials now exist under `Assets/Game/Materials/`.

- [ ] **Step 3: Patch missing \_BaseMap on extracted materials**

Same pattern as Task 3 Step 3, swap `texDir` to the Mirror folder.

- [ ] **Step 4: Identify the reflective surface (the "glass plane") in the mirror mesh**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;
using System.Linq;

var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Models/Mirror/Mirror.fbx");
var rends = model.GetComponentsInChildren<MeshRenderer>(true);
foreach (var r in rends)
{
    var f = r.GetComponent<MeshFilter>();
    var m = f != null ? f.sharedMesh : null;
    int tri = m != null ? m.triangles.Length / 3 : -1;
    var b = r.bounds;
    bool flatish = b.size.x > 0.1f && b.size.y > 0.1f && b.size.z < 0.05f;
    Debug.Log($"  {r.transform.name}: tris={tri}, size={b.size}, flat-ish={flatish}");
}
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. The reflective surface is whichever child has the **flattest** bounds (smallest one dimension). Record that GameObject's name — it'll be the target for `MirrorReflection.cs` in Task 9.

- [ ] **Step 5: Build Mirror prefab**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;

string fbx = "Assets/Game/Models/Mirror/Mirror.fbx";
var model = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
inst.name = "Mirror";
inst.transform.position = Vector3.zero;
PrefabUtility.SaveAsPrefabAsset(inst, "Assets/Game/Prefabs/Mirror.prefab");
Object.DestroyImmediate(inst);
Debug.Log("Mirror prefab saved.");
```

- [ ] **Step 6: Suggest commit**

```
feat: extract mirror materials, build Mirror prefab
```

---

## Task 6: ProBuilder room (walls, floor, ceiling) enclosing the staircase

**Files:**

- Modify: `Assets/Scenes/GrandStaircase.unity` (add Walls/Floor/Ceiling GameObjects)

- [ ] **Step 1: Open the scene and measure staircase bounds again**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var scene = EditorSceneManager.OpenScene("Assets/Scenes/GrandStaircase.unity");
var stair = GameObject.Find("Staircase");
if (stair == null) { Debug.LogError("Staircase not in scene"); return; }
var rends = stair.GetComponentsInChildren<Renderer>();
var b = rends[0].bounds;
for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);
Debug.Log($"BOUNDS center={b.center} size={b.size} min={b.min} max={b.max}");
```

Note the values — `b.min.x`, `b.min.z` (room corner), `b.size.x`, `b.size.z` (footprint), `b.max.y` (ceiling base height).

- [ ] **Step 2: Build the box room with ProBuilder, normals flipped inward**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;
using UnityEngine.ProBuilder.MeshOperations;

var scene = EditorSceneManager.OpenScene("Assets/Scenes/GrandStaircase.unity");
var stair = GameObject.Find("Staircase");
var rends = stair.GetComponentsInChildren<Renderer>();
var b = rends[0].bounds;
for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

float pad = 1.5f;        // wall offset from staircase bounds (m)
float ceilLift = 2.0f;   // ceiling above top of staircase (m)
float floorDrop = 0.1f;  // floor below bottom of staircase (m)

Vector3 size = new Vector3(b.size.x + 2*pad, b.size.y + ceilLift + floorDrop, b.size.z + 2*pad);
Vector3 center = new Vector3(b.center.x, b.min.y - floorDrop + size.y * 0.5f, b.center.z);

var room = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
room.gameObject.name = "Walls_Floor_Ceiling";
room.transform.position = center;

// Flip normals inward so we see the walls from the inside
room.faces.ToList().ForEach(f => f.Reverse());
room.ToMesh();
room.Refresh();

EditorSceneManager.SaveScene(scene);
Debug.Log($"Room built: size={size} center={center}");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: room saved, no errors.

- [ ] **Step 3: Assign a wall material**

For v1, reuse the StarterAssets neutral material. Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;

var room = GameObject.Find("Walls_Floor_Ceiling");
var mr = room.GetComponent<MeshRenderer>();

// Try a known StarterAssets material first; fall back to a default URP/Lit if not found
string[] candidates = {
    "Assets/StarterAssets/Environment/Art/Materials/Prototype_512x512_Grey1.mat",
    "Assets/StarterAssets/Environment/Art/Materials/Prototype_512x512_Grey2.mat",
};
Material mat = null;
foreach (var c in candidates)
{
    mat = AssetDatabase.LoadAssetAtPath<Material>(c);
    if (mat != null) { Debug.Log("Using wall material: " + c); break; }
}
if (mat == null)
{
    Debug.LogWarning("No StarterAssets prototype material found; using URP default.");
}
else
{
    mr.sharedMaterial = mat;
}

EditorUtility.SetDirty(room);
UnityEditor.SceneManagement.EditorSceneManager.SaveScene(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
```

Run `mcp__unity-mcp__Unity_GetConsoleLogs`. Note whether a real material was found; if not, manually assign a URP/Lit material in the editor before continuing — or list the available materials in `Assets/StarterAssets/Environment/Art/Materials` and pick one.

If candidates didn't match, list and pick:

```csharp
using System.IO;
foreach (var f in Directory.GetFiles("Assets/StarterAssets/Environment/Art/Materials", "*.mat"))
    Debug.Log(f);
```

- [ ] **Step 4: Capture and verify the walled scene**

Call `mcp__unity-mcp__Unity_SceneView_CaptureMultiAngleSceneView` to get a few angles. Expected: the staircase sits inside an enclosed box; no view to skybox from inside.

- [ ] **Step 5: Suggest commit**

```
feat: enclose staircase with ProBuilder walls/floor/ceiling
```

---

## Task 7: Lighting (directional + chandelier + sconces)

**Files:**

- Modify: `Assets/Scenes/GrandStaircase.unity` (add Lighting/\* GameObjects)

- [ ] **Step 1: Add a Lighting parent and re-organize**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var scene = EditorSceneManager.OpenScene("Assets/Scenes/GrandStaircase.unity");

// Make sure there's a Lighting parent
var lighting = GameObject.Find("Lighting");
if (lighting == null) lighting = new GameObject("Lighting");

// Move the existing DirectionalLight under Lighting if not already
var existingSun = GameObject.Find("DirectionalLight");
if (existingSun != null && existingSun.transform.parent != lighting.transform)
{
    existingSun.transform.SetParent(lighting.transform, true);
    var l = existingSun.GetComponent<Light>();
    if (l != null)
    {
        l.intensity = 0.3f;
        l.color = new Color(1.0f, 0.95f, 0.85f);
        l.shadows = LightShadows.Soft;
    }
}

EditorSceneManager.SaveScene(scene);
Debug.Log("Lighting parent prepared.");
```

- [ ] **Step 2: Add a center chandelier point light**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var scene = EditorSceneManager.GetActiveScene();
var lighting = GameObject.Find("Lighting");
var stair = GameObject.Find("Staircase");
var rends = stair.GetComponentsInChildren<Renderer>();
var b = rends[0].bounds;
for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

var go = new GameObject("Chandelier_Center");
go.transform.SetParent(lighting.transform, false);
go.transform.position = new Vector3(b.center.x, b.max.y + 1.0f, b.center.z);
var l = go.AddComponent<Light>();
l.type = LightType.Point;
l.range = Mathf.Max(b.size.x, b.size.y, b.size.z) * 0.9f;
l.intensity = 50f;
l.color = new Color(1f, 0.88f, 0.7f); // ~3200K
l.shadows = LightShadows.Soft;

EditorSceneManager.SaveScene(scene);
Debug.Log($"Chandelier placed at {go.transform.position}, range={l.range}");
```

- [ ] **Step 3: Add 4 wall sconces at room corners (just above eye height)**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var scene = EditorSceneManager.GetActiveScene();
var lighting = GameObject.Find("Lighting");
var room = GameObject.Find("Walls_Floor_Ceiling");
var bRoom = room.GetComponent<Renderer>().bounds;
float y = bRoom.min.y + 2.5f;
float inset = 0.6f;
Vector3[] corners = new[] {
    new Vector3(bRoom.min.x + inset, y, bRoom.min.z + inset),
    new Vector3(bRoom.max.x - inset, y, bRoom.min.z + inset),
    new Vector3(bRoom.min.x + inset, y, bRoom.max.z - inset),
    new Vector3(bRoom.max.x - inset, y, bRoom.max.z - inset),
};
for (int i = 0; i < corners.Length; i++)
{
    var go = new GameObject($"WallSconce_{i+1}");
    go.transform.SetParent(lighting.transform, false);
    go.transform.position = corners[i];
    var l = go.AddComponent<Light>();
    l.type = LightType.Point;
    l.range = bRoom.size.magnitude * 0.4f;
    l.intensity = 25f;
    l.color = new Color(1f, 0.85f, 0.65f);
    l.shadows = LightShadows.Soft;
}
EditorSceneManager.SaveScene(scene);
Debug.Log("4 wall sconces added.");
```

- [ ] **Step 4: Lower environment ambient so the room reads as indoor**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;

RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
RenderSettings.ambientLight = new Color(0.1f, 0.09f, 0.08f);
RenderSettings.ambientIntensity = 0.2f;
RenderSettings.skybox = null;
Debug.Log("Ambient lowered, skybox cleared.");
```

- [ ] **Step 5: Capture scene from multiple angles to verify lighting**

Call `mcp__unity-mcp__Unity_SceneView_CaptureMultiAngleSceneView`. Expected: room is visibly lit, warm tones, no pitch-black corners. If too dark, raise chandelier intensity by 2× and re-capture; if too bright/washed-out, halve sconce intensity.

- [ ] **Step 6: Suggest commit**

```
feat: light the staircase room (directional + chandelier + sconces)
```

---

## Task 8: FirstPersonCameraRig script + FirstPersonRig prefab

**Files:**

- Create: `Assets/Game/Scripts/FirstPersonCameraRig.cs`
- Create: `Assets/Game/Prefabs/FirstPersonRig.prefab`
- Modify: `Assets/Scenes/GrandStaircase.unity` (place rig)

- [ ] **Step 1: Write `FirstPersonCameraRig.cs`**

Create `Assets/Game/Scripts/FirstPersonCameraRig.cs`:

```csharp
using UnityEngine;

[DisallowMultipleComponent]
public class FirstPersonCameraRig : MonoBehaviour
{
    [Tooltip("The character root (CharacterController). Mouse-X yaws this.")]
    public Transform characterRoot;

    [Tooltip("The camera transform. Mouse-Y pitches this.")]
    public Transform cameraTransform;

    [Tooltip("Pitch clamp in degrees.")]
    public float pitchLimit = 85f;

    [Tooltip("Degrees per pixel of mouse movement.")]
    public float sensitivity = 0.15f;

    [Tooltip("Lock the cursor when this script is active.")]
    public bool lockCursor = true;

    float pitch;

    void OnEnable()
    {
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void OnDisable()
    {
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void Update()
    {
        if (characterRoot == null || cameraTransform == null) return;

        // Read mouse delta from the legacy Input axes (the Starter Assets controller already
        // uses Input System / PlayerInput for movement — mouse-look here is independent).
        float mx = Input.GetAxisRaw("Mouse X") * sensitivity * 60f;
        float my = Input.GetAxisRaw("Mouse Y") * sensitivity * 60f;

        // Yaw the body
        characterRoot.Rotate(0f, mx, 0f, Space.Self);

        // Pitch the camera (clamped)
        pitch = Mathf.Clamp(pitch - my, -pitchLimit, pitchLimit);
        var e = cameraTransform.localEulers();
        cameraTransform.localEulerAngles = new Vector3(pitch, 0f, 0f);
    }
}

static class TransformEulersExt
{
    public static Vector3 localEulers(this Transform t) => t.localEulerAngles;
}
```

**Note:** the Starter Assets ThirdPerson package uses the Input System for movement; we use the legacy `Input.GetAxisRaw` for mouse look here to avoid wiring a new Input Action just for this. If the project has `Active Input Handling = Input System Package (New)` only, switch it to "Both" in `Edit > Project Settings > Player > Active Input Handling` before play-mode test.

- [ ] **Step 2: Force-recompile and check console**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
AssetDatabase.Refresh();
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
UnityEngine.Debug.Log("Compilation requested.");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: no compile errors. If `Input.GetAxisRaw` errors with "module not found", set Active Input Handling to "Both" as noted.

- [ ] **Step 3: Copy `PlayerArmature` to a new FirstPersonRig prefab**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;

string src = "Assets/StarterAssets/ThirdPersonController/Prefabs/PlayerArmature.prefab";
string dst = "Assets/Game/Prefabs/FirstPersonRig.prefab";
if (!AssetDatabase.CopyAsset(src, dst))
{
    Debug.LogError("Failed to copy PlayerArmature -> FirstPersonRig");
    return;
}
Debug.Log("FirstPersonRig prefab created.");
```

- [ ] **Step 4: Reconfigure the FirstPersonRig prefab — camera at head bone, near plane, mouse-look component**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;
using System.Linq;

string prefabPath = "Assets/Game/Prefabs/FirstPersonRig.prefab";
var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
var instRoot = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

// Find the Head bone in the rig (humanoid: look for a Transform named "Head" or "mixamorig:Head").
Transform headBone = null;
foreach (var t in instRoot.GetComponentsInChildren<Transform>())
{
    if (t.name == "Head" || t.name.EndsWith(":Head"))
    {
        headBone = t;
        break;
    }
}
if (headBone == null)
{
    Debug.LogError("Could not find Head bone in PlayerArmature. Hierarchy: " +
        string.Join("\n", instRoot.GetComponentsInChildren<Transform>().Select(t => t.name)));
    Object.DestroyImmediate(instRoot);
    return;
}
Debug.Log("Head bone found: " + headBone.name);

// Create CameraRig under head bone
var camRig = new GameObject("CameraRig");
camRig.transform.SetParent(headBone, false);
camRig.transform.localPosition = new Vector3(0f, 0f, 0.10f); // 10 cm forward of head pivot
camRig.transform.localRotation = Quaternion.identity;

// Add the camera as child of CameraRig
var camGo = new GameObject("MainCamera");
camGo.tag = "MainCamera";
camGo.transform.SetParent(camRig.transform, false);
var cam = camGo.AddComponent<Camera>();
cam.nearClipPlane = 0.30f;  // hides head via near-plane clipping
cam.farClipPlane = 200f;
cam.fieldOfView = 75f;
camGo.AddComponent<AudioListener>();
// Remove any pre-existing AudioListener on the rig root so we don't have two
var oldAL = instRoot.GetComponent<AudioListener>();
if (oldAL != null) Object.DestroyImmediate(oldAL);

// Attach FirstPersonCameraRig component to rig root and wire fields
var fps = instRoot.AddComponent<FirstPersonCameraRig>();
fps.characterRoot = instRoot.transform;
fps.cameraTransform = camGo.transform;

// Save edits back to the prefab
PrefabUtility.SaveAsPrefabAsset(instRoot, prefabPath);
Object.DestroyImmediate(instRoot);
Debug.Log("FirstPersonRig prefab configured.");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: "Head bone found: ..." and "FirstPersonRig prefab configured." with no errors. If "Could not find Head bone" prints, scan the listed hierarchy and patch the bone-name match accordingly.

- [ ] **Step 5: Disable third-person camera-yaw on the rig**

The Starter Assets `ThirdPersonController.cs` rotates the body toward the (third-person) camera's forward. We want mouse-X to drive yaw directly via our new script, so we need to suppress that behavior. Easiest: leave `ThirdPersonController.cs` intact for movement/jump/animator, and remove the reference it holds to its camera target so the rotation block early-outs.

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;
using StarterAssets;

string prefabPath = "Assets/Game/Prefabs/FirstPersonRig.prefab";
var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
var tpc = inst.GetComponent<ThirdPersonController>();
if (tpc != null)
{
    // The script has a public field 'CinemachineCameraTarget' — clearing it makes the body-yaw block a no-op.
    var f = typeof(ThirdPersonController).GetField("CinemachineCameraTarget");
    if (f != null) f.SetValue(tpc, null);
    Debug.Log("CinemachineCameraTarget cleared on FirstPersonRig.");
}
else
{
    Debug.LogWarning("ThirdPersonController not on rig — skip.");
}
PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
Object.DestroyImmediate(inst);
```

If the public field has a different name, list `ThirdPersonController`'s public fields:

```csharp
foreach (var f in typeof(StarterAssets.ThirdPersonController).GetFields()) Debug.Log(f.Name + " : " + f.FieldType.Name);
```

…and patch the field name in the previous step.

- [ ] **Step 6: Drop the FirstPersonRig into the scene**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var scene = EditorSceneManager.OpenScene("Assets/Scenes/GrandStaircase.unity");

// Remove any existing player
var oldPlayer = GameObject.Find("FirstPersonRig");
if (oldPlayer != null) Object.DestroyImmediate(oldPlayer);

// Spawn at a sensible position — in front of the staircase at floor level
var stair = GameObject.Find("Staircase");
var rends = stair.GetComponentsInChildren<Renderer>();
var b = rends[0].bounds;
for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/FirstPersonRig.prefab");
var rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
rig.name = "FirstPersonRig";
rig.transform.position = new Vector3(b.center.x, b.min.y + 1.0f, b.min.z - 1.5f);
rig.transform.rotation = Quaternion.LookRotation(b.center - rig.transform.position, Vector3.up);

// Make sure there's no other camera lying around (the default scene camera, etc.)
foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
{
    if (c.transform.root != rig.transform.root && c.gameObject.name != "Main Camera")
    {
        // leave it; it may be a scene-view utility
    }
}
EditorSceneManager.SaveScene(scene);
Debug.Log($"FirstPersonRig placed at {rig.transform.position}");
```

- [ ] **Step 7: Capture in-game camera view to verify first-person framing**

Call `mcp__unity-mcp__Unity_Camera_Capture` (which renders the active main camera). Expected: scene visible from inside the staircase room, head not visible (near-plane clipping), staircase ahead.

If you see the inside of the character's head (nose/jaw fragments at the bottom of the frame), the head mesh has a piece outside the near plane — go to Task 8 fallback (see spec §5.3) and split the head into a separate sub-mesh or assign a `PlayerHead` layer + cull from the player camera.

- [ ] **Step 8: Enter Play Mode and verify mouse-look + WASD work**

This step is manual — the user enters Play Mode in the editor and verifies:

- Mouse moves the camera (yaw on character, pitch on camera).
- WASD moves the character forward/back/strafe.
- Space jumps.
- Walk/run animations play.
- Walking into walls is blocked by the room.

If `Input.GetAxisRaw` returns 0 always, set **Edit > Project Settings > Player > Active Input Handling = Both** and restart the editor.

- [ ] **Step 9: Suggest commit**

```
feat: first-person rig (PlayerArmature + head-bone camera + mouse-look)
```

---

## Task 9: MirrorReflection script + wire to mirror in the scene

**Files:**

- Create: `Assets/Game/Scripts/MirrorReflection.cs`
- Create: `Assets/Game/Materials/Mirror_Reflective.mat` (the on-glass material with RT)
- Modify: `Assets/Game/Prefabs/Mirror.prefab` (attach script + material)
- Modify: `Assets/Scenes/GrandStaircase.unity` (place mirror)

- [ ] **Step 1: Write `MirrorReflection.cs`**

Create `Assets/Game/Scripts/MirrorReflection.cs`:

```csharp
using UnityEngine;

// Real-time planar mirror via render texture.
// Attach this to the GameObject holding the mirror's reflective surface (a MeshRenderer
// on a flat quad/plane). Assigns a RenderTexture to the material's _BaseMap each frame.
//
// Adapted from the classic Aras Pranckevicius "MirrorReflection" pattern, modernized for
// URP and Unity 6. One reflection per frame, no recursion.
[ExecuteAlways]
[RequireComponent(typeof(MeshRenderer))]
public class MirrorReflection : MonoBehaviour
{
    [Tooltip("Resolution of the reflection RenderTexture (square).")]
    public int textureSize = 1024;

    [Tooltip("Small offset to push the reflection plane away from the mirror surface to avoid z-fighting.")]
    public float clipPlaneOffset = 0.01f;

    [Tooltip("Material slot the RenderTexture is assigned to (defaults to _BaseMap).")]
    public string textureProperty = "_BaseMap";

    Camera reflectionCamera;
    RenderTexture reflectionTexture;
    static bool insideRendering;

    void OnDisable()
    {
        if (reflectionTexture != null) { reflectionTexture.Release(); DestroyImmediate(reflectionTexture); reflectionTexture = null; }
        if (reflectionCamera != null) { DestroyImmediate(reflectionCamera.gameObject); reflectionCamera = null; }
    }

    void OnWillRenderObject()
    {
        if (!enabled || insideRendering) return;

        var cam = Camera.current;
        if (cam == null) return;

        EnsureResources();

        insideRendering = true;
        try
        {
            // Build a plane from this transform: normal = up of the mirror (its flat dimension).
            Vector3 pos = transform.position;
            Vector3 normal = transform.up;

            // Mirrored camera position/rotation across the plane
            float d = -Vector3.Dot(normal, pos) - clipPlaneOffset;
            Vector4 plane = new Vector4(normal.x, normal.y, normal.z, d);

            Matrix4x4 reflection = Matrix4x4.identity;
            CalculateReflectionMatrix(ref reflection, plane);

            reflectionCamera.worldToCameraMatrix = cam.worldToCameraMatrix * reflection;

            // Oblique projection so near plane lies on the mirror
            Vector4 clipPlane = CameraSpacePlane(reflectionCamera, pos, normal, 1.0f);
            reflectionCamera.projectionMatrix = cam.CalculateObliqueMatrix(clipPlane);

            reflectionCamera.cullingMask = ~(1 << 4); // skip "Water" layer if it exists, leave the rest
            reflectionCamera.targetTexture = reflectionTexture;
            GL.invertCulling = true;
            reflectionCamera.Render();
            GL.invertCulling = false;

            var mr = GetComponent<MeshRenderer>();
            // Use materialPropertyBlock so we don't clone the shared material every frame
            var mpb = new MaterialPropertyBlock();
            mr.GetPropertyBlock(mpb);
            mpb.SetTexture(textureProperty, reflectionTexture);
            mr.SetPropertyBlock(mpb);
        }
        finally
        {
            insideRendering = false;
        }
    }

    void EnsureResources()
    {
        if (reflectionTexture == null || reflectionTexture.width != textureSize)
        {
            if (reflectionTexture != null) { reflectionTexture.Release(); DestroyImmediate(reflectionTexture); }
            reflectionTexture = new RenderTexture(textureSize, textureSize, 16);
            reflectionTexture.name = "MirrorReflection_RT";
            reflectionTexture.isPowerOfTwo = true;
            reflectionTexture.antiAliasing = 1;
            reflectionTexture.hideFlags = HideFlags.DontSave;
        }
        if (reflectionCamera == null)
        {
            var go = new GameObject("MirrorCam", typeof(Camera));
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.SetParent(transform, false);
            reflectionCamera = go.GetComponent<Camera>();
            reflectionCamera.enabled = false;
            reflectionCamera.clearFlags = CameraClearFlags.SolidColor;
            reflectionCamera.backgroundColor = Color.black;
        }
    }

    static Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
    {
        var m = cam.worldToCameraMatrix;
        Vector3 cpos = m.MultiplyPoint(pos);
        Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
    }

    static void CalculateReflectionMatrix(ref Matrix4x4 reflectionMat, Vector4 plane)
    {
        reflectionMat.m00 = (1f - 2f * plane[0] * plane[0]);
        reflectionMat.m01 = (-2f * plane[0] * plane[1]);
        reflectionMat.m02 = (-2f * plane[0] * plane[2]);
        reflectionMat.m03 = (-2f * plane[3] * plane[0]);

        reflectionMat.m10 = (-2f * plane[1] * plane[0]);
        reflectionMat.m11 = (1f - 2f * plane[1] * plane[1]);
        reflectionMat.m12 = (-2f * plane[1] * plane[2]);
        reflectionMat.m13 = (-2f * plane[3] * plane[1]);

        reflectionMat.m20 = (-2f * plane[2] * plane[0]);
        reflectionMat.m21 = (-2f * plane[2] * plane[1]);
        reflectionMat.m22 = (1f - 2f * plane[2] * plane[2]);
        reflectionMat.m23 = (-2f * plane[3] * plane[2]);

        reflectionMat.m30 = 0f;
        reflectionMat.m31 = 0f;
        reflectionMat.m32 = 0f;
        reflectionMat.m33 = 1f;
    }
}
```

- [ ] **Step 2: Force compile and check for errors**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
AssetDatabase.Refresh();
UnityEngine.Debug.Log("MirrorReflection compile requested.");
```

Then `mcp__unity-mcp__Unity_GetConsoleLogs`. Expected: no compile errors.

- [ ] **Step 3: Create a dedicated reflective material**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;

var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
mat.name = "Mirror_Reflective";
mat.SetFloat("_Smoothness", 1.0f);
mat.SetFloat("_Metallic", 0.0f);
AssetDatabase.CreateAsset(mat, "Assets/Game/Materials/Mirror_Reflective.mat");
Debug.Log("Mirror_Reflective material created.");
```

- [ ] **Step 4: Open the mirror prefab and attach the script + reflective material to the flat surface**

Recall the flat-surface child name from Task 5 Step 4. Substitute it for `<FLAT_CHILD_NAME>` below.

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEngine;

string prefabPath = "Assets/Game/Prefabs/Mirror.prefab";
var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

// Find the reflective surface — the flattest child. Re-detect at runtime to keep this robust
// to renames.
MeshRenderer best = null;
float bestFlatness = float.MaxValue;
foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
{
    var b = mr.bounds.size;
    float minDim = Mathf.Min(b.x, b.y, b.z);
    float maxDim = Mathf.Max(b.x, b.y, b.z);
    if (maxDim < 0.1f) continue;
    float flatness = minDim / maxDim;
    if (flatness < bestFlatness) { bestFlatness = flatness; best = mr; }
}
if (best == null)
{
    Debug.LogError("Could not find a flat-ish surface on the Mirror prefab.");
    Object.DestroyImmediate(inst);
    return;
}
Debug.Log("Reflective surface: " + best.transform.name);

// Attach the script
if (best.GetComponent<MirrorReflection>() == null) best.gameObject.AddComponent<MirrorReflection>();

// Swap the material to the reflective one
var refl = AssetDatabase.LoadAssetAtPath<Material>("Assets/Game/Materials/Mirror_Reflective.mat");
best.sharedMaterial = refl;

PrefabUtility.SaveAsPrefabAsset(inst, prefabPath);
Object.DestroyImmediate(inst);
Debug.Log("Mirror prefab wired with reflection script.");
```

- [ ] **Step 5: Place the mirror in the scene where the player can walk up to it**

Run `mcp__unity-mcp__Unity_RunCommand`:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var scene = EditorSceneManager.OpenScene("Assets/Scenes/GrandStaircase.unity");
var oldMirror = GameObject.Find("Mirror");
if (oldMirror != null) Object.DestroyImmediate(oldMirror);

var stair = GameObject.Find("Staircase");
var rends = stair.GetComponentsInChildren<Renderer>();
var b = rends[0].bounds;
for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/Prefabs/Mirror.prefab");
var m = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
m.name = "Mirror";
// Place against one of the walls, facing into the room
m.transform.position = new Vector3(b.min.x - 0.2f, b.min.y + 1.5f, b.center.z);
m.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

EditorSceneManager.SaveScene(scene);
Debug.Log($"Mirror placed at {m.transform.position}");
```

- [ ] **Step 6: Capture from the player camera position to verify reflection**

Move the player rig in front of the mirror, then capture:

```csharp
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

var rig = GameObject.Find("FirstPersonRig");
var mirror = GameObject.Find("Mirror");
// Stand the player 2 m away facing the mirror
Vector3 mirrorPos = mirror.transform.position;
Vector3 normal = mirror.transform.up;  // adjust if reflective face uses a different axis
rig.transform.position = mirrorPos + normal * 2.0f - Vector3.up * 0.5f;
rig.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);
EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
Debug.Log("Rig moved in front of mirror.");
```

Then `mcp__unity-mcp__Unity_Camera_Capture`. Expected: the mirror surface shows a reflection of the player character body. If the reflective surface looks white/black instead of a reflection:

- It may not be receiving `OnWillRenderObject` because the surface mesh has no `MeshCollider`/visible bounds → script disabled. Check that the renderer is enabled.
- The reflection plane normal may be wrong (it's `transform.up` by default). Try `transform.forward` or `transform.right` based on the mesh's flat axis from Task 5 Step 4 — adjust the line `Vector3 normal = transform.up;` in `MirrorReflection.cs` accordingly.

- [ ] **Step 7: Suggest commit**

```
feat: real-time planar mirror via render texture
```

---

## Task 10: Final integration and success criteria

**Files:** none new — verification, tuning, polish.

- [ ] **Step 1: Walk-through in Play Mode**

User enters Play Mode and checks each success criterion from spec §11:

- [ ] First-person mouse-look + WASD + jump feel responsive
- [ ] Staircase is visible, textured, ~10–12 m tall
- [ ] Walls fully enclose the staircase (no view of skybox/void from any standing position)
- [ ] The scene is visibly lit; no pitch-black areas at standing height
- [ ] The mirror shows a live reflection of the player character
- [ ] No errors or persistent warnings in the Unity console

- [ ] **Step 2: Pull final console state**

Run `mcp__unity-mcp__Unity_GetConsoleLogs` and check for errors. If any appear, address them before declaring done. Common late-stage issues:

- `MaterialPropertyBlock` warnings if a renderer has multiple sub-materials — switch script to assign a per-instance material instead.
- Texture-import warnings about non-power-of-two — safe to ignore for FBX-extracted PNGs.

- [ ] **Step 3: Save final scene + capture hero shot**

```csharp
using UnityEditor.SceneManagement;
EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
```

Then `mcp__unity-mcp__Unity_SceneView_CaptureMultiAngleSceneView` and `mcp__unity-mcp__Unity_Camera_Capture` for the final visual record.

- [ ] **Step 4: Suggest final commit**

```
feat: titanic grand staircase scene — first-person + mirror + lighting complete
```

---

## Notes on robustness / fallbacks

- **Sketchfab GLBs with non-axis-aligned canonical pose** (per the prior project's lesson): if the staircase or mirror imports with weird rotation, accept "good enough" — set the rotation that puts the floor at Y_min and the obvious "front" facing toward Z+. Don't iterate forever on rotation values.
- **Materials with missing texture maps** after `ExtractTextures`: the heuristic patcher in Task 3/5 Step 3 handles the easy cases. If a specific material remains flat-white, manually drag the matching PNG from the `_Textures` folder into the `_BaseMap` slot in the editor.
- **Head still visible from inside the player camera**: the near-plane clipping (0.30 m) handles 95% of cases. If a piece of head/nose still clips through, switch to the layer-based fallback from spec §5.3.
- **`Input.GetAxisRaw` not working**: set `Edit > Project Settings > Player > Active Input Handling = Both` and restart the editor.

---

## Self-review

**Spec coverage:**

- §3 Asset pipeline → Tasks 2, 3, 4, 5 ✓
- §4 Scene structure → assembled across Tasks 1, 3, 6, 7, 8, 9 ✓
- §5 First-person rig → Task 8 ✓
- §6 Mirror reflection → Task 9 ✓
- §7 Walls/floor/ceiling → Task 6 ✓
- §8 Lighting → Task 7 ✓
- §9 Folder layout → Task 1 ✓
- §10 Tooling → used throughout (Blender MCP for assets, Unity MCP for scene/script) ✓
- §11 Success criteria → Task 10 ✓
- §12 Out of scope → respected (no Jack/Rose, no audio, etc.)

**Placeholder scan:** No "TBD", "implement later", or unspecified steps. Every code block compiles standalone or is wrapped in a clearly-marked fallback.

**Type/method consistency:** `MirrorReflection.cs` properties (`textureSize`, `clipPlaneOffset`, `textureProperty`) are defined in Task 9 Step 1 and not referenced under different names elsewhere. `FirstPersonCameraRig` fields (`characterRoot`, `cameraTransform`, `pitchLimit`, `sensitivity`) are defined in Task 8 Step 1 and wired in Task 8 Step 4 with matching names. ✓
