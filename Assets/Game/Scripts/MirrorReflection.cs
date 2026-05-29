using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR;

// Stereo-correct planar mirror for URP (VR + desktop).
//
// WHY THIS EXISTS / what was wrong before:
//   The previous version rendered ONE reflection from a single (mono) viewpoint and sampled it
//   with per-eye screen-space UVs. VR draws two eyes from two offset positions, so a mono
//   reflection can't serve both: one eye got wrong parallax, the other sampled off-texture and
//   showed through to the world. A correct VR mirror must render the reflection SEPARATELY per
//   eye and show each eye its own.
//
// METHOD (classic planar reflection, applied per eye):
//   For each eye:  worldToCameraMatrix = eyeViewMatrix * worldReflectionMatrix
//                  projectionMatrix    = oblique( eyeProjectionMatrix )   // near-clip on mirror
//   Rendered into _ReflLeft / _ReflRight; the shader picks the slice by unity_StereoEyeIndex.
//
//   The world reflection matrix flips handedness, so GL.invertCulling = true is CORRECT here
//   (front faces stay front faces -> proper lighting; this is NOT the old desktop washout, which
//   was caused by invertCulling on a NON-flipped LookRotation view). Because the reflection matrix
//   already yields a true mirror image, the shader does NOT flip U.
[RequireComponent(typeof(MeshRenderer))]
public class MirrorReflection : MonoBehaviour
{
    [Tooltip("Layers the reflection camera renders. Default Everything.")]
    public LayerMask reflectLayers = ~0;

    [Tooltip("Push the clip plane forward of the mirror to avoid self-reflection / z-fighting.")]
    public float clipPlaneOffset = 0.01f;

    [Tooltip("Oblique near-clip at the mirror plane. Suspected to break URP shadow cascades at off-center head angles; toggle to test.")]
    public bool obliqueClip = true;

    Camera reflectionCamera;
    RenderTexture rtLeft, rtRight;
    MeshRenderer cachedRenderer;
    Renderer[] mirrorOwnRenderers;
    Material runtimeMirrorMaterial;
    bool insideRendering;

    static readonly int LeftId = Shader.PropertyToID("_ReflLeft");
    static readonly int RightId = Shader.PropertyToID("_ReflRight");

    void OnEnable()
    {
        cachedRenderer = GetComponent<MeshRenderer>();
        var root = transform.root != null ? transform.root : transform;
        mirrorOwnRenderers = root.GetComponentsInChildren<Renderer>(true);
        BuildRuntimeMaterial();
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        Cleanup();
    }

    void Cleanup()
    {
        if (rtLeft != null) { rtLeft.Release(); DestroyImmediate(rtLeft); rtLeft = null; }
        if (rtRight != null) { rtRight.Release(); DestroyImmediate(rtRight); rtRight = null; }
        if (reflectionCamera != null) { DestroyImmediate(reflectionCamera.gameObject); reflectionCamera = null; }
        if (runtimeMirrorMaterial != null) { DestroyImmediate(runtimeMirrorMaterial); runtimeMirrorMaterial = null; }
    }

    void BuildRuntimeMaterial()
    {
        var shader = Shader.Find("Custom/MirrorReflection");
        if (shader == null)
        {
            Debug.LogError("[MR] Custom/MirrorReflection shader not found.");
            return;
        }
        var newMat = new Material(shader) { name = "MirrorReflection (Runtime)" };
        if (newMat.HasProperty("_Tint")) newMat.SetColor("_Tint", Color.white);
        cachedRenderer.material = newMat;
        runtimeMirrorMaterial = cachedRenderer.material;
    }

    void OnBeginCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (!enabled || cam == null || insideRendering) return;
        if (cam == reflectionCamera) return;            // don't reflect our own reflection camera
        if (cam.cameraType != CameraType.Game) return;  // skip scene view / preview cameras
        if (cachedRenderer == null || !cachedRenderer.enabled) return;

        EnsureResources(cam);

        // Hide the mirror's own prop meshes so the reflection doesn't include the mirror's back.
        var states = new bool[mirrorOwnRenderers.Length];
        for (int i = 0; i < mirrorOwnRenderers.Length; i++)
        {
            var r = mirrorOwnRenderers[i]; if (r == null) continue;
            states[i] = r.enabled; if (r.enabled) r.enabled = false;
        }

        insideRendering = true;
        try
        {
            if (cam.stereoEnabled)
            {
                RenderReflection(context,
                    cam.GetStereoViewMatrix(Camera.StereoscopicEye.Left),
                    cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left), rtLeft);
                RenderReflection(context,
                    cam.GetStereoViewMatrix(Camera.StereoscopicEye.Right),
                    cam.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right), rtRight);
            }
            else
            {
                RenderReflection(context, cam.worldToCameraMatrix, cam.projectionMatrix, rtLeft);
            }
        }
        finally
        {
            insideRendering = false;
            for (int i = 0; i < mirrorOwnRenderers.Length; i++)
            {
                var r = mirrorOwnRenderers[i]; if (r == null) continue;
                r.enabled = states[i];
            }
        }

        var liveMat = cachedRenderer.sharedMaterial;
        if (liveMat != null)
        {
            liveMat.SetTexture(LeftId, rtLeft);
            liveMat.SetTexture(RightId, cam.stereoEnabled ? rtRight : rtLeft);
        }
    }

    void RenderReflection(ScriptableRenderContext context, Matrix4x4 eyeView, Matrix4x4 eyeProj, RenderTexture rt)
    {
        Vector3 pos = transform.position;
        Vector3 normal = transform.forward;

        float d = -Vector3.Dot(normal, pos) - clipPlaneOffset;
        Vector4 reflectionPlane = new Vector4(normal.x, normal.y, normal.z, d);
        Matrix4x4 reflection = CalculateReflectionMatrix(reflectionPlane);

        reflectionCamera.worldToCameraMatrix = eyeView * reflection;
        reflectionCamera.projectionMatrix = eyeProj;

        if (obliqueClip)
        {
            // Decouple CULLING from the rendered (oblique) projection. An oblique near plane
            // corrupts URP's shadow-cascade culling (shadows drop out at off-center angles), so
            // we cull with the UNDISTORTED frustum via cullingMatrix and only skew the projection
            // used for the actual draw. This keeps the correct mirror clip AND working shadows.
            reflectionCamera.cullingMatrix = eyeProj * reflectionCamera.worldToCameraMatrix;

            Vector4 clipPlane = CameraSpacePlane(reflectionCamera.worldToCameraMatrix, pos, normal, 1.0f);
            reflectionCamera.projectionMatrix = reflectionCamera.CalculateObliqueMatrix(clipPlane);
        }
        else
        {
            reflectionCamera.ResetCullingMatrix();
        }

        reflectionCamera.cullingMask = reflectLayers;
        reflectionCamera.targetTexture = rt;

        bool oldInvert = GL.invertCulling;
        GL.invertCulling = true; // reflection matrix flips winding; restores correct front-face lighting
        try
        {
#pragma warning disable CS0618
            UniversalRenderPipeline.RenderSingleCamera(context, reflectionCamera);
#pragma warning restore CS0618
        }
        finally { GL.invertCulling = oldInvert; }
    }

    // World-space plane -> reflection camera space (for the oblique near clip).
    Vector4 CameraSpacePlane(Matrix4x4 worldToCam, Vector3 pos, Vector3 normal, float sideSign)
    {
        Vector3 offsetPos = pos + normal * clipPlaneOffset;
        Vector3 cpos = worldToCam.MultiplyPoint(offsetPos);
        Vector3 cnormal = worldToCam.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
    }

    static Matrix4x4 CalculateReflectionMatrix(Vector4 plane)
    {
        Matrix4x4 m = Matrix4x4.identity;
        m.m00 = 1f - 2f * plane.x * plane.x; m.m01 = -2f * plane.x * plane.y; m.m02 = -2f * plane.x * plane.z; m.m03 = -2f * plane.w * plane.x;
        m.m10 = -2f * plane.y * plane.x; m.m11 = 1f - 2f * plane.y * plane.y; m.m12 = -2f * plane.y * plane.z; m.m13 = -2f * plane.w * plane.y;
        m.m20 = -2f * plane.z * plane.x; m.m21 = -2f * plane.z * plane.y; m.m22 = 1f - 2f * plane.z * plane.z; m.m23 = -2f * plane.w * plane.z;
        m.m30 = 0f; m.m31 = 0f; m.m32 = 0f; m.m33 = 1f;
        return m;
    }

    void EnsureResources(Camera referenceCam)
    {
        int w, h;
        if (XRSettings.enabled && XRSettings.eyeTextureWidth > 0)
        { w = XRSettings.eyeTextureWidth; h = XRSettings.eyeTextureHeight; }
        else { w = Mathf.Max(2, Screen.width); h = Mathf.Max(2, Screen.height); }

        EnsureRT(ref rtLeft, "MirrorRT_L", w, h);
        EnsureRT(ref rtRight, "MirrorRT_R", w, h);

        if (reflectionCamera == null)
        {
            var go = new GameObject("MirrorCam_" + name, typeof(Camera));
            go.hideFlags = HideFlags.DontSave;
            go.transform.SetParent(transform, false);
            reflectionCamera = go.GetComponent<Camera>();
            reflectionCamera.enabled = false;
            reflectionCamera.clearFlags = referenceCam.clearFlags;
            reflectionCamera.backgroundColor = referenceCam.backgroundColor;
            var ucd = reflectionCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            ucd.renderType = CameraRenderType.Base;
            ucd.renderShadows = true;
            ucd.requiresDepthTexture = true;
            ucd.requiresColorTexture = false;
            ucd.allowXRRendering = false; // CRITICAL: render mono into each per-eye RT, not stereo
        }
        else
        {
            reflectionCamera.clearFlags = referenceCam.clearFlags;
            reflectionCamera.backgroundColor = referenceCam.backgroundColor;
        }
    }

    void EnsureRT(ref RenderTexture rt, string rtName, int w, int h)
    {
        if (rt != null && (rt.width != w || rt.height != h)) { rt.Release(); DestroyImmediate(rt); rt = null; }
        if (rt == null)
        {
            rt = new RenderTexture(w, h, 16) { name = rtName, antiAliasing = 1, hideFlags = HideFlags.DontSave };
            rt.Create();
        }
    }
}
