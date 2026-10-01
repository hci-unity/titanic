using UnityEngine;

// Ship motion after the iceberg impact (call Impact() from SinkingSequence.onCrash): a jolt that follows
// the crash clip's loudness, a faint rumble while sinking, and a slow tilt as the bow goes down.
// Moves the camera's PARENT (XR "Camera Offset" / desktop "CameraRig"), so head tracking stays 1:1 and
// the player's collider is untouched. Rotation pivots at the head. VR comfort: bigger = more nausea risk.
[DisallowMultipleComponent]
public class ShipShake : MonoBehaviour
{
    [Tooltip("What gets shaken. Empty = the main camera's parent, found at impact.")]
    public Transform target;

    [Header("Impact jolt")]
    public float joltDuration = 14f;
    public float joltPosition = 0.16f;  // meters
    public float joltRotation = 6f;   // degrees
    public float joltFrequency = 9f;
    [Tooltip("If set, jolt strength follows this sound's loudness (the crash clip) instead of the fixed decay.")]
    public AudioSource followAudio;
    [Tooltip("Loudness (dB) mapped to no jolt / full jolt.")]
    public Vector2 loudnessRangeDb = new(-24f, -3f);

    [Header("Rumble while sinking")]
    public float rumblePosition = 0.003f;
    public float rumbleRotation = 0.15f;
    public float rumbleFrequency = 2f;

    [Header("Tilt")]
    [Tooltip("Final tilt in degrees (X = bow down, Z = roll), eased in over tiltDuration.")]
    public Vector3 finalTilt = new(45f, 0f, 10f);
    public float tiltDuration = 40f;

    [Tooltip("Things outside the ship (ocean) that stay level while the ship tilts and shakes.")]
    public Transform[] keepLevel = new Transform[0];

    // World-space rotation that keeps things level with the player's real "down" (identity before impact).
    // Multiply Physics.gravity by it for objects that should fall straight down in view.
    public Quaternion LevelRotation { get; private set; } = Quaternion.identity;

    Transform head;
    Vector3[] levelPos;
    Quaternion[] levelRot;
    Vector3 basePos;
    Quaternion baseRot;
    float impactTime = -1f;
    float phase;
    float envelope;
    readonly float[] samples = new float[256];

    public void Impact()
    {
        if (!target && Camera.main) target = Camera.main.transform.parent;
        if (!target) return;
        head = Camera.main ? Camera.main.transform : null;
        basePos = target.localPosition;
        baseRot = target.localRotation;
        impactTime = Time.time;
        levelPos = new Vector3[keepLevel.Length];
        levelRot = new Quaternion[keepLevel.Length];
        for (int i = 0; i < keepLevel.Length; i++) { levelPos[i] = keepLevel[i].position; levelRot[i] = keepLevel[i].rotation; }
    }

    void LateUpdate()
    {
        if (impactTime < 0f || !target) return;
        float t = Time.time - impactTime;

        float jolt = Mathf.Clamp01(1f - t / joltDuration);
        jolt *= jolt;
        // Audio-driven: full strength for most of joltDuration, then eases off (the clip grinds on for ~50s).
        if (followAudio && followAudio.isPlaying)
            jolt = Loudness() * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(joltDuration * 0.6f, joltDuration, t)));
        phase += Time.deltaTime * Mathf.Lerp(rumbleFrequency, joltFrequency, jolt);
        var n = new Vector3(Noise(0), Noise(1), Noise(2));

        float tilt = Mathf.SmoothStep(0f, 1f, t / tiltDuration);
        // Tilt in WORLD axes (the ship's), not the rig's: the desktop rig yaws with the mouse, and a
        // rig-relative tilt turned with it, swinging the moon and sea around as you looked about.
        var d = Quaternion.Euler(finalTilt * tilt + n * (joltRotation * jolt + rumbleRotation));
        var jitter = n * (joltPosition * jolt + rumblePosition);

        // Pivot around the head (not the rig's floor origin), so big tilts turn the room around the
        // player instead of swinging their viewpoint into walls.
        var parent = target.parent;
        var parentRot = parent ? parent.rotation : Quaternion.identity;
        Vector3 Rest(Vector3 local) => parent ? parent.TransformPoint(local) : local;
        Vector3 c = head ? target.InverseTransformPoint(head.position) : Vector3.zero;
        Vector3 pivot = Rest(basePos + baseRot * c); // where the head is, untilted
        target.SetPositionAndRotation(pivot + d * (Rest(basePos) - pivot) + jitter, d * parentRot * baseRot);

        // Tilting the camera rig tilts everything in view, the sea included. Give outside objects the same
        // world-space move as the camera, so in view the room tilts but the horizon stays level.
        for (int i = 0; i < keepLevel.Length; i++)
            if (keepLevel[i]) keepLevel[i].SetPositionAndRotation(pivot + d * (levelPos[i] - pivot) + jitter, d * levelRot[i]);
        LevelRotation = d;
    }

    // Crash clip loudness -> 0..1, with instant attack and a short release so each hit reads as a jolt.
    float Loudness()
    {
        followAudio.GetOutputData(samples, 0);
        float sum = 0f;
        foreach (var x in samples) sum += x * x;
        float db = 20f * Mathf.Log10(Mathf.Sqrt(sum / samples.Length) + 1e-6f);
        float goal = Mathf.InverseLerp(loudnessRangeDb.x, loudnessRangeDb.y, db);
        envelope = Mathf.Max(goal, Mathf.MoveTowards(envelope, goal, Time.deltaTime / 0.25f));
        return envelope;
    }

    float Noise(int axis) => Mathf.PerlinNoise(phase, axis * 7.3f) * 2f - 1f;
}
