using UnityEngine;

// The iceberg (call Impact() from SinkingSequence.onCrash): drifts in from ahead during the calm, strikes the
// bow corner at the impact (bursting the front windows in that corner), then scrapes back along that side (the
// long grinding in the crash audio), bursting each side window in turn as its leading edge reaches it (glass +
// ice chunks fly in), and falls astern.
// Positions are in the Sea's local space: as a child of the Sea it stays level with the water while the ship
// tilts, and rises with it as the ship goes down.
public class IcebergDrift : MonoBehaviour
{
    [Tooltip("Far out, almost dead ahead (visible through the front windows the whole way in).")]
    public Vector3 startLocal = new(18.41f, 0f, -171.51f);
    [Tooltip("At the impact: its front tip touches the bow corner, ~0.8 m off the hull (measured on the mesh).")]
    public Vector3 impactLocal = new(18.41f, 0f, -31.51f);
    [Tooltip("Its tail has passed the stern end of the room.")]
    public Vector3 endLocal = new(18.41f, 0f, 34.12f);
    [Tooltip("Seconds from the start to reach the ship (match SinkingSequence.calmDuration).")]
    public float approachTime = 25f;
    [Tooltip("Seconds the run in from the horizon takes. Fixed, so the ship's speed is the same in every game length; "
           + "in a longer game the iceberg stays hidden until it's this long before the impact.")]
    public float approachDuration = 25f;
    public float scrapeTime = 30f;

    [Header("Windows it smashes on the way past")]
    [Tooltip("Glass panes on the iceberg's side. Each bursts when the iceberg's leading edge reaches it.")]
    public Renderer[] windowGlass = new Renderer[0];
    public AudioClip breakClip;
    public Material glassShardMaterial;
    public Material iceMaterial;
    [Tooltip("Shape for the ice lumps that burst in (keep it low-poly: dozens get spawned).")]
    public Mesh lumpMesh;
    public ShipShake ship;

    float impactTime = -1f;
    Vector3 impactFrom;
    Renderer ice;
    bool[] broken;

    // The ship's speed (m/s), shared with the IcebergField so the whole sea passes at one speed.
    public float ShipSpeed => Vector3.Distance(startLocal, impactLocal) / approachDuration;

    void Awake() => ice = GetComponentInChildren<Renderer>();

    public void Impact()
    {
        impactTime = Time.time;
        impactFrom = transform.localPosition;
        ice.enabled = true;
        IcebergField.SetFade(ice, 1f);
        broken = new bool[windowGlass.Length];
    }

    void Update()
    {
        if (impactTime < 0f)
        {
            // Steady approach; if the impact lands a moment late (it waits on the audio), it just waits alongside.
            float t = (Time.timeSinceLevelLoad - (approachTime - approachDuration)) / approachDuration;
            ice.enabled = t > 0f; // appears at the horizon, emerging from the haze over its first ~8 s
            IcebergField.SetFade(ice, t * approachDuration / 8f);
            transform.localPosition = Vector3.Lerp(startLocal, impactLocal, t);
            return;
        }
        float p = Mathf.Clamp01((Time.time - impactTime) / scrapeTime);
        transform.localPosition = Vector3.Lerp(impactFrom, endLocal, 1f - (1f - p) * (1f - p)); // slows as the ship loses way

        // It moves toward +z (back along the ship), so its +z end is the leading edge.
        float leading = ice.bounds.max.z;
        for (int i = 0; i < windowGlass.Length; i++)
            if (!broken[i] && windowGlass[i] && leading >= windowGlass[i].bounds.center.z) Smash(i);
    }

    void Smash(int i)
    {
        broken[i] = true;
        var glass = windowGlass[i];
        var window = glass.transform.parent;
        foreach (var r in window.GetComponentsInChildren<Renderer>())
            if (r.name == "Glass" || r.name == "Muntin") r.enabled = false; // the glass's collider stays, so nobody walks out to sea
        var b = glass.bounds;
        // The pane's thin axis is its normal (x on the side wall, z on the front); point it away from the iceberg, into the room.
        var normal = b.size.x < b.size.z ? Vector3.right : Vector3.forward;
        var inward = -Mathf.Sign(Vector3.Dot(transform.position - b.center, normal)) * normal;
        var spot = b.center + inward * 0.35f; // just inside, clear of the pane's collider
        var spread = Vector3.Scale(b.extents, Vector3.one - normal) + normal * 0.05f;
        Shatter.Sound(breakClip, b.center, 0.8f, 6f);
        Shatter.Burst(spot, spread, 25, new Vector2(0.04f, 0.12f), glassShardMaterial, inward * 2.5f, ship);
        Shatter.Burst(spot, spread * 0.7f, 6, new Vector2(0.25f, 0.55f), iceMaterial, inward * 3f + Vector3.up, ship, lumpMesh);
    }
}
