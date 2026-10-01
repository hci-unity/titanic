using System.Collections;
using UnityEngine;

// The freestanding mirror goes over (call Begin() from SinkingSequence.onCrash): it rattles on its base
// through the grinding, tips over on a hinge, and the glass shatters on the floor (reflection off, shards,
// MirrorShatter sound). Scripted rather than physics: the wide base would just slide, and the fall
// direction is chosen AWAY from the player and clear of walls (a 2.6 m mirror toppling at someone in VR
// makes them leap back into the real room).
public class MirrorFall : MonoBehaviour
{
    public float rattleTime = 3f;
    public float rattleAngle = 1.5f;
    [Tooltip("Distance from the base center to the edge it tips over.")]
    public float baseRadius = 0.45f;
    [Tooltip("Degrees from upright where the top hits the floor.")]
    public float landAngle = 86f;

    [Header("Shatter")]
    public Renderer glass; // the reflective surface (carries MirrorReflection)
    public AudioClip shatterClip;
    [Range(0f, 1f)] public float shatterVolume = 1f;
    public Material shardMaterial;
    public int shardCount = 40;
    public ShipShake ship;

    public void Begin() => StartCoroutine(Run());

    IEnumerator Run()
    {
        var restPos = transform.position;
        var restRot = transform.rotation;
        var bounds = glass.bounds;
        foreach (var r in GetComponentsInChildren<Renderer>()) bounds.Encapsulate(r.bounds);
        var baseCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

        // Rattle on the base, growing as it works loose.
        for (float t = 0; t < rattleTime; t += Time.deltaTime)
        {
            float amp = rattleAngle * t / rattleTime;
            var rattle = Quaternion.Euler(Noise(t, 0) * amp, 0f, Noise(t, 1) * amp);
            transform.SetPositionAndRotation(baseCenter + rattle * (restPos - baseCenter), rattle * restRot);
            yield return null;
        }
        transform.SetPositionAndRotation(restPos, restRot);

        // Its collider is one box around the whole upright mirror; tipped over it would be an invisible slab
        // that shards (and lamps) land on and float above. The fallen frame is decoration from here on.
        foreach (var c in GetComponents<Collider>()) c.enabled = false;

        // Tip over a base edge: a rigid rod pivoting at its foot, theta'' = (3g / 2h) sin(theta).
        var dir = FallDirection(bounds);
        var pivot = baseCenter + dir * baseRadius;
        var axis = Vector3.Cross(Vector3.up, dir);
        float k = 1.5f * 9.81f / bounds.size.y, theta = 2f * Mathf.Deg2Rad, omega = 0.2f;
        while (theta < landAngle * Mathf.Deg2Rad)
        {
            omega += k * Mathf.Sin(theta) * Time.deltaTime;
            theta += omega * Time.deltaTime;
            Pose(pivot, axis, theta * Mathf.Rad2Deg, restPos, restRot);
            yield return null;
        }
        Pose(pivot, axis, landAngle, restPos, restRot);
        Smash();

        // A small bounce as it lands.
        for (float t = 0; t < 0.35f; t += Time.deltaTime)
        {
            Pose(pivot, axis, landAngle - 4f * Mathf.Sin(Mathf.PI * t / 0.35f), restPos, restRot);
            yield return null;
        }
        Pose(pivot, axis, landAngle, restPos, restRot);
    }

    void Pose(Vector3 pivot, Vector3 axis, float degrees, Vector3 restPos, Quaternion restRot)
    {
        var q = Quaternion.AngleAxis(degrees, axis);
        transform.SetPositionAndRotation(pivot + q * (restPos - pivot), q * restRot);
    }

    // A thin mirror tips forward (face down) or backward (face up). Away from the player wins; a wall in the
    // way only counts against a direction, it never makes it fall toward the player.
    Vector3 FallDirection(Bounds bounds)
    {
        var fwd = Vector3.ProjectOnPlane(glass.transform.forward, Vector3.up).normalized;
        var from = new Vector3(bounds.center.x, bounds.min.y + 1f, bounds.center.z);
        var player = Camera.main ? Camera.main.transform.position : from;
        var away = Vector3.ProjectOnPlane(from - player, Vector3.up).normalized;
        Vector3 best = fwd;
        float bestScore = float.MinValue;
        foreach (var d in new[] { fwd, -fwd })
        {
            bool blocked = Physics.Raycast(from, d, bounds.size.y, ~0, QueryTriggerInteraction.Ignore);
            float score = Vector3.Dot(d, away) - (blocked ? 0.5f : 0f);
            if (score > bestScore) { bestScore = score; best = d; }
        }
        return best;
    }

    void Smash()
    {
        var reflection = glass.GetComponent<MirrorReflection>();
        if (reflection) reflection.enabled = false;
        glass.enabled = false;
        var b = glass.bounds;
        Shatter.Sound(shatterClip, b.center, shatterVolume, 8f);
        Shatter.Burst(b.center, new Vector3(b.extents.x, 0.05f, b.extents.z), shardCount, new Vector2(0.05f, 0.15f), shardMaterial, Vector3.up, ship);
    }

    static float Noise(float t, int axis) => Mathf.PerlinNoise(t * 12f, axis * 5.7f) * 2f - 1f;
}
