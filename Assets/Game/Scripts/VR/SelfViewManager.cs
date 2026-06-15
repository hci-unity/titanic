using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// First-person self-view via the production-standard SPLIT-RENDERER + per-camera culling-mask
// pattern (how VRChat "Head Chop" and VRM FirstPerson do it). It does NOT touch the mirror avatar
// or the mirror material, and uses NO render-event hooks or per-camera material swaps -- so it is
// immune to the nested-RenderSingleCamera fragility that broke earlier attempts.
//
// Mechanism:
//   - The mirror avatar stays exactly as-is on layer 8 (mirror-only, full body + head, original
//     URP/Lit material).
//   - For the ACTIVE avatar this builds a SECOND, HEADLESS SkinnedMeshRenderer on a dedicated
//     "first-person body" layer. The headless mesh is a runtime CLONE of the avatar's own mesh
//     with the head-bone-weighted triangles removed (so bindposes/bone order match natively -- no
//     fragile skeleton remapping), and it SHARES the avatar's live bone Transforms, so it deforms
//     identically from the same IK + finger driver with zero extra animation/sync.
//   - Visibility is pure per-camera culling: the HMD/Main camera already renders the FP-body layer
//     and culls layer 8 (so it sees the headless body, not the mirror avatar); the mirror's
//     reflectLayers EXCLUDES the FP-body layer (so the reflection shows only the full mirror avatar).
//
// Generic across characters: rebuilds when the switcher's active character changes. No per-character
// authoring, no Blender, no custom shader.
[DisallowMultipleComponent]
public class SelfViewManager : MonoBehaviour
{
    [Tooltip("Master switch. When off, the first-person body is torn down (head shows nowhere extra).")]
    public bool selfViewEnabled = true;

    [Tooltip("Switcher tracking the active avatar. The active character gets a headless FP body.")]
    public MirrorCharacterSwitcher switcher;

    [Tooltip("Layer for the first-person body: must be RENDERED by the Main/HMD camera and EXCLUDED " +
             "from the mirror's reflectLayers. Default 10.")]
    public int firstPersonLayer = 10;

    [Tooltip("A mesh vertex counts as 'head' when its summed weight to the head bone (and its bone " +
             "descendants) is >= this. Triangles with 2+ head vertices are dropped (clean neck cut).")]
    [Range(0f, 1f)] public float headVertexThreshold = 0.5f;

    [Tooltip("Let the first-person body cast shadows. Off by default to avoid doubling the mirror " +
             "avatar's shadow at the same spot.")]
    public bool castShadows = false;

    [Tooltip("Controller-visual GameObjects to hide while self-view is active (the avatar's own hands " +
             "replace them). Found by name if left empty.")]
    public string[] controllerVisualNames = { "Left Controller Visual", "Right Controller Visual" };

    int builtIndex = -1;
    GameObject fpBodyGO;
    SkinnedMeshRenderer fpSmr;
    Mesh fpMesh;
    readonly List<GameObject> hiddenControllerVisuals = new List<GameObject>();

    void OnDisable() { Teardown(); }
    void OnDestroy() { Teardown(); }

    void LateUpdate()
    {
        if (switcher == null) return;

        if (!selfViewEnabled)
        {
            if (fpBodyGO != null) Teardown();
            return;
        }

        if (switcher.CurrentIndex != builtIndex || fpBodyGO == null) Rebuild();
    }

    void Rebuild()
    {
        Teardown();
        builtIndex = switcher.CurrentIndex;

        GameObject avatar = (switcher.characters != null && builtIndex >= 0 && builtIndex < switcher.characters.Count)
            ? switcher.characters[builtIndex] : null;
        if (avatar == null) return;

        var src = avatar.GetComponentInChildren<SkinnedMeshRenderer>(true);
        var anim = avatar.GetComponentInChildren<Animator>(true);
        if (src == null || src.sharedMesh == null || anim == null || !anim.isHuman) return;
        var headBone = anim.GetBoneTransform(HumanBodyBones.Head);
        if (headBone == null) return;

        fpMesh = BuildHeadlessMesh(src, headBone, headVertexThreshold);
        if (fpMesh == null) return;

        fpBodyGO = new GameObject("FirstPersonBody");
        fpBodyGO.transform.SetParent(src.transform.parent, false);
        fpBodyGO.transform.localPosition = src.transform.localPosition;
        fpBodyGO.transform.localRotation = src.transform.localRotation;
        fpBodyGO.transform.localScale = src.transform.localScale;
        fpBodyGO.layer = firstPersonLayer;

        fpSmr = fpBodyGO.AddComponent<SkinnedMeshRenderer>();
        fpSmr.sharedMesh = fpMesh;
        fpSmr.bones = src.bones;                 // share the avatar's LIVE bone Transforms
        fpSmr.rootBone = src.rootBone;
        fpSmr.sharedMaterials = src.sharedMaterials; // identical URP/Lit material -> identical lighting
        fpSmr.quality = src.quality;
        fpSmr.updateWhenOffscreen = true;        // bounds follow shared bones safely
        fpSmr.shadowCastingMode = castShadows
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.Off;

        HideControllerVisuals(true);
    }

    // Clone the source mesh and drop triangles that belong to the head (2+ head-weighted vertices),
    // leaving a clean neck cut. Vertices/bindposes/boneweights are untouched (it's a clone sharing the
    // same bones), so deformation matches the avatar exactly minus the head.
    Mesh BuildHeadlessMesh(SkinnedMeshRenderer src, Transform headBone, float threshold)
    {
        var mesh = src.sharedMesh;
        var bones = src.bones;
        if (bones == null || bones.Length == 0) return null;

        // Head bone set = the head bone plus any bone that is its transform-descendant (eyes, jaw, hair).
        var headBones = new HashSet<int>();
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] != null && (bones[i] == headBone || bones[i].IsChildOf(headBone)))
                headBones.Add(i);

        var bw = mesh.boneWeights;
        if (bw == null || bw.Length != mesh.vertexCount) return null; // not skinned as expected
        var isHead = new bool[mesh.vertexCount];
        for (int v = 0; v < mesh.vertexCount; v++)
        {
            float w = 0f;
            var b = bw[v];
            if (headBones.Contains(b.boneIndex0)) w += b.weight0;
            if (headBones.Contains(b.boneIndex1)) w += b.weight1;
            if (headBones.Contains(b.boneIndex2)) w += b.weight2;
            if (headBones.Contains(b.boneIndex3)) w += b.weight3;
            isHead[v] = w >= threshold;
        }

        var clone = Instantiate(mesh);
        clone.name = mesh.name + "_Headless";
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var tris = mesh.GetTriangles(s);
            var keep = new List<int>(tris.Length);
            for (int t = 0; t < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                int headCount = (isHead[a] ? 1 : 0) + (isHead[b] ? 1 : 0) + (isHead[c] ? 1 : 0);
                if (headCount >= 2) continue; // drop head triangle
                keep.Add(a); keep.Add(b); keep.Add(c);
            }
            clone.SetTriangles(keep, s);
        }
        clone.RecalculateBounds();
        return clone;
    }

    void HideControllerVisuals(bool hide)
    {
        if (hide)
        {
            hiddenControllerVisuals.Clear();
            if (controllerVisualNames == null) return;
            var all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var t in all)
            {
                if (t == null) continue;
                foreach (var n in controllerVisualNames)
                    if (t.name == n && t.gameObject.activeSelf)
                    {
                        t.gameObject.SetActive(false);
                        hiddenControllerVisuals.Add(t.gameObject);
                    }
            }
        }
        else
        {
            foreach (var go in hiddenControllerVisuals) if (go != null) go.SetActive(true);
            hiddenControllerVisuals.Clear();
        }
    }

    void Teardown()
    {
        HideControllerVisuals(false);
        if (fpBodyGO != null) { Destroy(fpBodyGO); fpBodyGO = null; fpSmr = null; }
        if (fpMesh != null) { Destroy(fpMesh); fpMesh = null; }
        builtIndex = -1;
    }
}
