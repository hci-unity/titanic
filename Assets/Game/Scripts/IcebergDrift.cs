using UnityEngine;

// The iceberg (call Impact() from SinkingSequence.onCrash): drifts in from ahead during the calm, strikes the
// bow corner at the impact, then scrapes back along that side (the long grinding in the crash audio), bursting
// each side window in turn as its leading edge reaches it (glass + ice chunks fly in), and falls astern.
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

    public void Impact()
    {
        impactTime = Time.time;
        impactFrom = transform.localPosition;
        ice = GetComponentInChildren<Renderer>();
        broken = new bool[windowGlass.Length];
    }

    void Update()
    {
        if (impactTime < 0f)
        {
            // Steady approach; if the impact lands a moment late (it waits on the audio), it just waits alongside.
            transform.localPosition = Vector3.Lerp(startLocal, impactLocal, Time.timeSinceLevelLoad / approachTime);
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
        var inward = -Mathf.Sign(transform.position.x - b.center.x) * Vector3.right; // away from the iceberg, into the room
        var spot = b.center + inward * 0.35f; // just inside, clear of the pane's collider
        var spread = new Vector3(0.05f, b.extents.y, b.extents.z);
        Shatter.Sound(breakClip, b.center, 0.8f, 6f);
        Shatter.Burst(spot, spread, 25, new Vector2(0.04f, 0.12f), glassShardMaterial, inward * 2.5f, ship);
        Shatter.Burst(spot, spread * 0.7f, 6, new Vector2(0.25f, 0.55f), iceMaterial, inward * 3f + Vector3.up, ship, lumpMesh);
    }
}
