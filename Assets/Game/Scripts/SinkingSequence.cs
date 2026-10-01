using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// The experience timeline: calm -> iceberg crash -> sinking into total darkness.
// Waves play throughout (and keep going in the dark). At the crash: crash SFX, screams fade in, onCrash
// fires (shake / falling lamps / mirror hook in there), each lamp flickers on its own and dies one by one.
// Meanwhile every light, the ambient light and glowing materials fade to black.
[DisallowMultipleComponent]
public class SinkingSequence : MonoBehaviour
{
    [Header("Timing (seconds)")]
    public float calmDuration = 25f;
    public float sinkDuration = 40f;
    public float flickerDuration = 3f;
    [Tooltip("Calm music fades out over this long, reaching silence 1s before the impact.")]
    public float calmMusicFadeOut = 6.5f;
    public float screamsFadeIn = 4f;
    public float audioFadeOutAtEnd = 4f;

    [Header("Audio")]
    public AudioSource calmMusic;
    public AudioSource waves;
    public AudioSource crash;
    [Tooltip("Seconds into the crash clip where the impact hits (the clip starts this early).")]
    public float crashImpactTime = 9f;
    public AudioSource screams;

    [Header("Darkness")]
    [Tooltip("Materials whose emission glows (bulbs). Faded via runtime copies, so assets aren't touched.")]
    public Material[] glowMaterials;

    [Header("Lamps (flicker independently after the crash, then die one by one)")]
    public Light[] lampLights;
    [Tooltip("Lamp fixture renderers, same order as lampLights; their glow follows the lamp.")]
    public Renderer[] lampRenderers;
    public Vector2 lampOnTime = new(0.3f, 2.5f);
    public Vector2 lampOffTime = new(0.05f, 0.3f);
    [Tooltip("Each lamp dies at a random point in this fraction of the sinking.")]
    public Vector2 lampDeathWindow = new(0.3f, 0.9f);

    [Header("Falling lamps (a random subset tears loose and shatters)")]
    public int fallingLampCount = 20;
    [Tooltip("Seconds after impact over which the lamps drop, one by one.")]
    public Vector2 fallWindow = new(1f, 25f);
    public AudioClip[] lampBreakClips;
    public Material shardMaterial;
    public ShipShake ship;

    [Tooltip("Fires at the moment of impact.")]
    public UnityEvent onCrash;

    readonly List<(Light light, float intensity)> lights = new();
    readonly List<(Material mat, Color emission)> glows = new();
    readonly Dictionary<Light, int> lampIndex = new();
    Material[] lampMats;
    Color[] lampEmission;
    float[] lampOn;
    bool[] fallen;
    Color ambient;
    float reflections;
    float level = 1f; // ship power: flickers, brownouts, fades
    float night = 1f; // moon / sky / ambient: smooth fade only, never flickers
    static readonly int SkyDarknessId = Shader.PropertyToID("_SkyDarkness");

    void Start()
    {
        foreach (var l in FindObjectsByType<Light>()) lights.Add((l, l.intensity));
        ambient = RenderSettings.ambientLight;
        reflections = RenderSettings.reflectionIntensity;
        CloneGlowMaterials();

        lampOn = new float[lampLights.Length];
        fallen = new bool[lampLights.Length];
        lampMats = new Material[lampLights.Length];
        lampEmission = new Color[lampLights.Length];
        for (int i = 0; i < lampLights.Length; i++)
        {
            lampOn[i] = 1f;
            if (lampLights[i]) lampIndex[lampLights[i]] = i;
            if (i < lampRenderers.Length && lampRenderers[i])
            {
                lampMats[i] = lampRenderers[i].material; // per-lamp copy so each glows on its own
                lampEmission[i] = lampMats[i].GetColor("_EmissionColor");
            }
        }

        if (waves) waves.Play();
        StartCoroutine(Run());
    }

    void CloneGlowMaterials()
    {
        var clones = new Dictionary<Material, Material>();
        foreach (var m in glowMaterials)
            if (m) { var c = new Material(m); clones[m] = c; glows.Add((c, c.GetColor("_EmissionColor"))); }
        if (clones.Count == 0) return;

        foreach (var r in FindObjectsByType<Renderer>())
        {
            var mats = r.sharedMaterials;
            bool hit = false;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] && clones.TryGetValue(mats[i], out var c)) { mats[i] = c; hit = true; }
            if (hit) r.sharedMaterials = mats;
        }
    }

    IEnumerator Run()
    {
        // The crash clip starts early so its big hit lands exactly on the impact; the music is silent 1s before.
        float lead = crash && crash.clip ? Mathf.Min(crashImpactTime, calmDuration) : 0f;
        if (crash) StartCoroutine(After(calmDuration - lead, crash.Play));
        if (calmMusic) StartCoroutine(After(calmDuration - 1f - calmMusicFadeOut,
            () => StartCoroutine(Fade(calmMusic, calmMusic.volume, 0f, calmMusicFadeOut))));
        yield return new WaitForSeconds(calmDuration);
        // Land on the actual audio position, not the clock: a streamed clip can start a little late.
        if (crash && crash.clip)
            yield return new WaitUntil(() => !crash.isPlaying || crash.time >= crashImpactTime);

        if (screams) StartCoroutine(Fade(screams, 0f, screams.volume, screamsFadeIn, play: true));
        onCrash.Invoke();

        float sink = Mathf.Max(0.01f, sinkDuration - flickerDuration);
        for (int i = 0; i < lampOn.Length; i++)
            StartCoroutine(FlickerLamp(i, flickerDuration + sink * Random.Range(lampDeathWindow.x, lampDeathWindow.y)));
        DropLamps();

        // Main lights dip (not black) while the lamps do the hard flickering.
        for (float end = Time.time + flickerDuration; Time.time < end;)
        {
            level = Random.Range(0.3f, 0.5f);
            yield return new WaitForSeconds(Random.Range(0.05f, 0.15f));
            level = 1f;
            yield return new WaitForSeconds(Random.Range(0.15f, 0.4f));
        }

        bool fadingOut = false;
        float dipUntil = 0f;
        for (float t = 0; t < sink; t += Time.deltaTime)
        {
            float k = 1f - t / sink;
            // Occasional short brownouts (~one every 4s) as the power fails.
            if (Random.value < Time.deltaTime / 4f) dipUntil = Time.time + Random.Range(0.08f, 0.15f);
            night = k * k;
            level = night * (Time.time < dipUntil ? 0.3f : 1f);

            if (!fadingOut && sink - t <= audioFadeOutAtEnd)
            {
                fadingOut = true;
                if (screams) StartCoroutine(Fade(screams, screams.volume, 0f, audioFadeOutAtEnd)); // waves keep going
            }
            yield return null;
        }
        level = night = 0f;
        Debug.Log("SinkingSequence: reached total darkness.");
    }

    void DropLamps()
    {
        var order = new List<int>();
        for (int i = 0; i < lampRenderers.Length; i++) if (lampRenderers[i]) order.Add(i);
        for (int n = 0; n < fallingLampCount && order.Count > 0; n++)
        {
            int pick = Random.Range(0, order.Count), i = order[pick];
            order.RemoveAt(pick);
            var lamp = lampRenderers[i].gameObject.AddComponent<FallingLamp>();
            lamp.breakClips = lampBreakClips;
            lamp.shardMaterial = shardMaterial;
            lamp.ship = ship;
            lamp.onDetach = () => fallen[i] = true;
            lamp.Drop(Random.Range(fallWindow.x, fallWindow.y));
        }
    }

    IEnumerator FlickerLamp(int i, float lifetime)
    {
        // Random blinks until the lamp dies.
        for (float end = Time.time + lifetime; Time.time < end;)
        {
            lampOn[i] = Random.Range(0f, 0.15f);
            yield return new WaitForSeconds(Random.Range(lampOffTime.x, lampOffTime.y));
            lampOn[i] = 1f;
            yield return new WaitForSeconds(Random.Range(lampOnTime.x, lampOnTime.y));
        }
        lampOn[i] = 0f;
    }

    void LateUpdate()
    {
        foreach (var (l, i) in lights)
            if (l) l.intensity = l.type == LightType.Directional ? i * night
                               : i * level * (lampIndex.TryGetValue(l, out int j) ? LampOn(j) : 1f);
        foreach (var (m, e) in glows) m.SetColor("_EmissionColor", e * level);
        for (int j = 0; j < lampMats.Length; j++)
            if (lampMats[j]) lampMats[j].SetColor("_EmissionColor", lampEmission[j] * level * LampOn(j));
        RenderSettings.ambientLight = ambient * night;
        RenderSettings.reflectionIntensity = reflections * night;
        Shader.SetGlobalFloat(SkyDarknessId, 1f - night);
    }

    // Globals outlive Play mode in the editor; don't leave the sky black afterwards.
    void OnDestroy() => Shader.SetGlobalFloat(SkyDarknessId, 0f);

    static IEnumerator After(float seconds, System.Action action)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, seconds));
        action();
    }

    float LampOn(int i) => fallen[i] ? 0f : lampOn[i];

    static IEnumerator Fade(AudioSource a, float from, float to, float duration, bool play = false)
    {
        a.volume = from;
        if (play) a.Play();
        for (float t = 0; t < duration; t += Time.deltaTime)
        {
            a.volume = Mathf.Lerp(from, to, t / duration);
            yield return null;
        }
        a.volume = to;
        if (to == 0f) a.Stop();
    }
}
