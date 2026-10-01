using UnityEngine;

// The ship goes under (call Begin() from SinkingSequence.onCrash): the sea rises relative to the ship,
// climbs the windows, floods the room and finally closes over the player's head. Its swells calm as it
// rises. Underwater: murky fog, the surface seen from below (OceanWaves back faces), muffled audio.
// The sea is a child of a ShipShake.keepLevel root, so it rises "straight up" in the player's view.
[DisallowMultipleComponent]
public class Flooding : MonoBehaviour
{
    public Transform sea;
    [Tooltip("Minimum meters the sea climbs over the whole sinking.")]
    public float riseHeight = 11f;
    [Tooltip("The sea always ends at least this far above the player's head, wherever they stand (stairs, gallery).")]
    public float headClearance = 2f;
    public float duration = 40f;
    [Tooltip(">1 = slow at first, faster as the ship goes under.")]
    public float risePower = 1.5f;
    [Tooltip("How calm the swells get by the end (0 = full waves, 1 = flat).")]
    [Range(0f, 1f)] public float calmAtEnd = 0.65f;

    [Header("Underwater")]
    public Color underwaterColor = new(0.01f, 0.035f, 0.045f);
    public float underwaterFogDensity = 0.35f;
    public float muffleCutoffHz = 1200f;
    [Tooltip("Played louder underwater, since the muffle takes a lot of its energy away (the waves).")]
    public AudioSource loudUnderwater;
    public float underwaterVolumeBoost = 2f;
    [Tooltip("Switch to underwater this far before the surface reaches the eyes (the view starts filling with water first).")]
    public float surfaceMargin = 0.2f;

    static readonly int WaveCalmId = Shader.PropertyToID("_WaveCalm");
    static readonly int UnderwaterId = Shader.PropertyToID("_Underwater");
    static readonly int SkyDarknessId = Shader.PropertyToID("_SkyDarkness"); // set by SinkingSequence
    static readonly int[] WaveIds = { Shader.PropertyToID("_WaveA"), Shader.PropertyToID("_WaveB"), Shader.PropertyToID("_WaveC"), Shader.PropertyToID("_WaveD") };
    Material seaMaterial;

    float startY, startTime = -1f, risen;
    bool underwater;

    public void Begin()
    {
        if (!sea) return;
        startY = sea.localPosition.y;
        startTime = Time.time;
    }

    void Update()
    {
        if (startTime < 0f) return;
        float f = Mathf.Clamp01((Time.time - startTime) / duration);
        var cam = Camera.main;
        float headAboveSea = cam ? Vector3.Dot(cam.transform.position - sea.position, sea.up) : 0f;

        // Rise toward whichever is higher: the default height, or just over the player's head (measured from
        // where the sea started). Never sinks back if they come down again.
        float total = Mathf.Max(riseHeight, headAboveSea + risen + headClearance);
        risen = Mathf.Max(risen, total * Mathf.Pow(f, risePower));
        var p = sea.localPosition;
        p.y = startY + risen;
        sea.localPosition = p;
        Shader.SetGlobalFloat(WaveCalmId, calmAtEnd * f);

        // Head (nearly) below the actual wavy surface: the underwater look switches as a crest reaches the eyes.
        // Latched: once under, a passing trough doesn't pop the view back above water (the ship is going down).
        if (!underwater && cam && headAboveSea < WaveHeightAt(cam.transform.position) + surfaceMargin) GoUnderwater(cam);
        // The murk fades with the night, so the ending reaches true black.
        RenderSettings.fogColor = underwaterColor * (1f - Shader.GetGlobalFloat(SkyDarknessId));
    }

    // Same Gerstner sum as OceanWaves.shader, evaluated on the CPU at one point (sea object space, scale 1).
    // Gerstner also shifts the surface sideways, so a few fixed-point steps find the wave whose crest
    // actually sits above this xz.
    float WaveHeightAt(Vector3 world)
    {
        if (!seaMaterial && sea) seaMaterial = sea.GetComponent<Renderer>().sharedMaterial;
        if (!seaMaterial) return 0f;
        float calm = 1f - Shader.GetGlobalFloat(WaveCalmId);
        float time = Time.timeSinceLevelLoad * seaMaterial.GetFloat("_WaveSpeed"); // shader _Time.y = time since level load
        var local = sea.InverseTransformPoint(world);
        var target = new Vector2(local.x, local.z);
        Vector2 shift = Vector2.zero;
        float height = 0f;
        for (int pass = 0; pass < 3; pass++)
        {
            var q = target - shift;
            shift = Vector2.zero;
            height = 0f;
            foreach (var id in WaveIds)
            {
                var w = seaMaterial.GetVector(id);
                float k = 2f * Mathf.PI / w.w, a = w.z * calm / k;
                var d = new Vector2(w.x, w.y).normalized;
                float f = k * (Vector2.Dot(d, q) - Mathf.Sqrt(9.8f / k) * time);
                shift += d * (a * Mathf.Cos(f));
                height += a * Mathf.Sin(f);
            }
        }
        return height;
    }

    void GoUnderwater(Camera cam)
    {
        underwater = true;
        RenderSettings.fogDensity = underwaterFogDensity; // scene keeps fog ON at density 0 so builds keep the fog shader variants
        Shader.SetGlobalFloat(UnderwaterId, 1f);
        var muffle = cam.gameObject.AddComponent<AudioLowPassFilter>(); // the listener lives on the camera
        muffle.cutoffFrequency = muffleCutoffHz;
        if (loudUnderwater) loudUnderwater.volume = Mathf.Min(1f, loudUnderwater.volume * underwaterVolumeBoost);
    }

    // Globals outlive Play mode in the editor.
    void OnDestroy()
    {
        Shader.SetGlobalFloat(WaveCalmId, 0f);
        Shader.SetGlobalFloat(UnderwaterId, 0f);
    }
}
