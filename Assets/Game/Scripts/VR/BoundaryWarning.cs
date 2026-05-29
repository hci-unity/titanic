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
