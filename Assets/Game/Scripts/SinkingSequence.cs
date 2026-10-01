using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// The experience timeline: calm -> iceberg crash -> sinking into total darkness.
// Waves play throughout (and keep going in the dark). At the crash: crash SFX, screams fade in, onCrash
// fires (shake / falling lamps / mirror hook in there), each lamp flickers on its own and dies one by one.
// The hellish track fades in while every light, the ambient light and glowing materials fade to black.
[DisallowMultipleComponent]
public class SinkingSequence : MonoBehaviour
{
    [Header("Timing (seconds)")]
    public float calmDuration = 25f;
    public float sinkDuration = 40f;
    public float flickerDuration = 3f;
    [Tooltip("Calm music fades out over this long, starting when the crash clip starts.")]
    public float calmMusicFadeOut = 6.5f;
    [Tooltip("Hell music fades in over this long, after the flicker.")]
    public float musicCrossfade = 10f;
    public float screamsFadeIn = 4f;
    public float audioFadeOutAtEnd = 4f;

    [Header("Audio")]
    public AudioSource calmMusic;
    public AudioSource hellMusic;
    public AudioSource waves;
    public AudioSource crash;
    [Tooltip("Seconds into the crash clip where the impact hits (the clip starts this early).")]
    public float crashImpactTime = 7.5f;
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

    [Tooltip("Fires at the moment of impact.")]
    public UnityEvent onCrash;

    readonly List<(Light light, float intensity)> lights = new();
    readonly List<(Material mat, Color emission)> glows = new();
    readonly Dictionary<Light, int> lampIndex = new();
    Material[] lampMats;
    Color[] lampEmission;
    float[] lampOn;
    Color ambient;
    float reflections;
    float level = 1f;

    void Start()
    {
        foreach (var l in FindObjectsByType<Light>()) lights.Add((l, l.intensity));
        ambient = RenderSettings.ambientLight;
        reflections = RenderSettings.reflectionIntensity;
        CloneGlowMaterials();

        lampOn = new float[lampLights.Length];
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
        // Start the crash clip early so its build-up ends exactly on the impact.
        float lead = crash && crash.clip ? Mathf.Min(crashImpactTime, calmDuration) : 0f;
        yield return new WaitForSeconds(calmDuration - lead);
        if (crash) crash.Play();
        if (calmMusic) StartCoroutine(Fade(calmMusic, calmMusic.volume, 0f, calmMusicFadeOut));
        yield return new WaitForSeconds(lead);

        if (screams) StartCoroutine(Fade(screams, 0f, screams.volume, screamsFadeIn, play: true));
        onCrash.Invoke();

        float sink = Mathf.Max(0.01f, sinkDuration - flickerDuration);
        for (int i = 0; i < lampOn.Length; i++)
            StartCoroutine(FlickerLamp(i, flickerDuration + sink * Random.Range(lampDeathWindow.x, lampDeathWindow.y)));

        // Main lights dip (not black) while the lamps do the hard flickering.
        for (float end = Time.time + flickerDuration; Time.time < end;)
        {
            level = Random.Range(0.3f, 0.5f);
            yield return new WaitForSeconds(Random.Range(0.05f, 0.15f));
            level = 1f;
            yield return new WaitForSeconds(Random.Range(0.15f, 0.4f));
        }

        if (hellMusic) StartCoroutine(Fade(hellMusic, 0f, hellMusic.volume, musicCrossfade, play: true));

        bool fadingOut = false;
        float dipUntil = 0f;
        for (float t = 0; t < sink; t += Time.deltaTime)
        {
            float k = 1f - t / sink;
            // Occasional short brownouts (~one every 4s) as the power fails.
            if (Random.value < Time.deltaTime / 4f) dipUntil = Time.time + Random.Range(0.08f, 0.15f);
            level = k * k * (Time.time < dipUntil ? 0.3f : 1f);

            if (!fadingOut && sink - t <= audioFadeOutAtEnd)
            {
                fadingOut = true;
                foreach (var a in new[] { hellMusic, screams }) // waves keep going in the dark
                    if (a) StartCoroutine(Fade(a, a.volume, 0f, audioFadeOutAtEnd));
            }
            yield return null;
        }
        level = 0f;
        Debug.Log("SinkingSequence: reached total darkness.");
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
            if (l) l.intensity = i * level * (lampIndex.TryGetValue(l, out int j) ? lampOn[j] : 1f);
        foreach (var (m, e) in glows) m.SetColor("_EmissionColor", e * level);
        for (int j = 0; j < lampMats.Length; j++)
            if (lampMats[j]) lampMats[j].SetColor("_EmissionColor", lampEmission[j] * level * lampOn[j]);
        RenderSettings.ambientLight = ambient * level;
        RenderSettings.reflectionIntensity = reflections * level;
    }

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
