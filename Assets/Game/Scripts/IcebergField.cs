using System.Collections.Generic;
using UnityEngine;

// Scatters background icebergs on the sea (put this under Outside/Sea, so they rise with the flood and
// stay level with the horizon) and drifts them past the ship (+z, the ship sails toward -z). Each one
// gets a random shape, size, yaw and side. One that drifts out of range re-enters at the far end.
// The crash (SinkingSequence.onCrash) slows the drift to a stop, as the ship loses way.
public class IcebergField : MonoBehaviour
{
    public Mesh[] shapes;
    public Material ice;
    public int count = 12;
    [Tooltip("Icebergs live in -range..+range along z (sea-local); the sea is ~170 m in radius.")]
    public float range = 160f;
    [Tooltip("Closest distance on the port side (-x).")]
    public float portClear = 32f;
    [Tooltip("Closest distance on the starboard side (+x). The main iceberg passes at ~18 m, keep clear of it.")]
    public float starboardClear = 50f;
    public float farX = 150f;
    public Vector2 sizeRange = new(0.35f, 1.15f);
    public float minSpacing = 45f;
    [Tooltip("How fast they pass (m/s) while the ship is under way. Taken from the main iceberg's approach when there is one.")]
    public float passSpeed = 5.6f;
    [Tooltip("Seconds after the crash until the ship has stopped.")]
    public float stopTime = 25f;
    [Tooltip("Metres over which an iceberg emerges from (and sinks back into) the haze at either end of the range.")]
    public float fadeDistance = 40f;

    static MaterialPropertyBlock block;

    // 0 = lost in the haze, 1 = normal. Used by the main iceberg too.
    public static void SetFade(Renderer r, float fade)
    {
        block ??= new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetFloat("_Fade", Mathf.SmoothStep(0f, 1f, fade));
        r.SetPropertyBlock(block);
    }

    readonly List<Transform> bergs = new();
    float crashTime = -1f;

    void Start()
    {
        var seq = FindAnyObjectByType<SinkingSequence>();
        if (seq) seq.onCrash.AddListener(() => crashTime = Time.time);
        var main = FindAnyObjectByType<IcebergDrift>();
        if (main) passSpeed = main.ShipSpeed;

        for (int i = 0; i < count; i++)
        {
            var go = new GameObject("Berg " + i, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = shapes[i % shapes.Length];
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = ice;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localScale = Vector3.one * Random.Range(sizeRange.x, sizeRange.y);
            go.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            // The first two pass close, the rest are spread out to the horizon.
            Place(go.transform, Random.Range(-range, range), i < 2);
            bergs.Add(go.transform);
        }
    }

    void Place(Transform t, float z, bool near)
    {
        for (int attempt = 0; ; attempt++)
        {
            bool port = Random.value < 0.5f;
            float clear = port ? portClear : starboardClear;
            float x = near ? Random.Range(clear, clear + 25f) : Random.Range(clear, farX);
            var p = new Vector3(port ? -x : x, Random.Range(-1f, 0f), z);
            bool crowded = false;
            foreach (var b in bergs)
                if (b != t && Vector2.Distance(new(p.x, p.z), new(b.localPosition.x, b.localPosition.z)) < minSpacing)
                    crowded = true;
            if (!crowded || attempt > 20) { t.localPosition = p; return; }
        }
    }

    void Update()
    {
        float way = crashTime < 0f ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (Time.time - crashTime) / stopTime);
        foreach (var t in bergs)
        {
            var p = t.localPosition;
            p.z += passSpeed * way * Time.deltaTime;
            if (p.z > range) Place(t, -range, false); // re-enter at the far end, faded out
            else t.localPosition = p;
            SetFade(t.GetComponent<Renderer>(), Mathf.Min(range - Mathf.Abs(t.localPosition.z), fadeDistance) / fadeDistance);
        }
    }
}
