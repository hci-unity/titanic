using UnityEngine;

// Drives the mirror avatar's locomotion blend tree from the player's PHYSICAL movement (the HMD's
// horizontal speed in world space), so the reflection walks when the player walks around their room.
// Works for bare hands AND controllers, and also covers stick/smooth locomotion (which translates the
// HMD in world space too). Root motion stays OFF; the walk plays in place while AvatarRigDriver keeps
// the body under the HMD.
//
// Why HMD displacement and not CharacterController.velocity: the XR rig continuously repositions the
// capsule under the HMD, so capsule velocity reads ~1.3 m/s even while standing still. The HMD's
// world-space horizontal displacement is the reliable "actually moving" signal and is zero when still.
[DisallowMultipleComponent]
public class LocomotionAnimatorDriver : MonoBehaviour
{
    [Tooltip("HMD transform; physical movement is measured from its horizontal displacement. Auto-finds Camera.main if null.")]
    public Transform hmd;

    [Tooltip("Animator with a locomotion blend tree. Auto-found in children if null.")]
    public Animator animator;

    [Tooltip("Float parameter driven by movement magnitude (StarterAssetsThirdPerson uses 'Speed').")]
    public string speedParameter = "Speed";

    [Tooltip("Clip-playback-speed parameter; gates the walk clip so idle doesn't animate ('MotionSpeed').")]
    public string motionSpeedParameter = "MotionSpeed";

    [Tooltip("Physical speed (m/s) that maps to a full walk in the blend tree. LOW = animation reacts " +
             "strongly to small real movement.")]
    public float walkSpeed = 0.5f;

    [Tooltip("Speed value sent at full movement (tune to the blend tree; ~2 = walk, higher leans to jog/run).")]
    public float maxSpeed = 2.5f;

    [Tooltip("Clip playback speed (MotionSpeed) when BARELY moving — floor so the legs never play in " +
             "slow-motion. 1 = normal clip speed.")]
    public float motionSpeedMin = 1.0f;

    [Tooltip("Clip playback speed at full movement — >1 makes the stride faster / more aggressive.")]
    public float motionSpeedMax = 1.6f;

    [Tooltip("Physical speed (m/s) below this counts as standing still (snaps to idle).")]
    public float deadzone = 0.1f;

    [Tooltip("Speeds above this (m/s) are treated as a recenter/teleport glitch and ignored.")]
    public float glitchSpeed = 6.0f;

    [Tooltip("Smoothing time for the speed value (seconds).")]
    public float damp = 0.12f;

    int speedHash, motionHash;
    float speed;
    Vector3 lastHmdPos;
    bool hasLast;

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (hmd == null && Camera.main != null) hmd = Camera.main.transform;
        speedHash = Animator.StringToHash(speedParameter);
        motionHash = Animator.StringToHash(motionSpeedParameter);
    }

    void Update()
    {
        if (animator == null) return;
        if (hmd == null && Camera.main != null) hmd = Camera.main.transform;

        float dt = Time.deltaTime;
        float physSpeed = 0f;
        if (hmd != null && dt > 0f)
        {
            Vector3 p = hmd.position;
            if (hasLast)
            {
                Vector3 d = p - lastHmdPos; d.y = 0f;
                physSpeed = d.magnitude / dt;
            }
            lastHmdPos = p;
            hasLast = true;
        }
        if (physSpeed < deadzone || physSpeed > glitchSpeed) physSpeed = 0f;

        float mag = walkSpeed > 0f ? Mathf.Clamp01(physSpeed / walkSpeed) : 0f;
        float target = mag * maxSpeed;
        speed = Mathf.Lerp(speed, target, damp > 0f ? dt / damp : 1f);
        animator.SetFloat(speedHash, speed);

        // MotionSpeed gates clip playback: 0 freezes to idle, but when moving keep it at full speed
        // (>= motionSpeedMin) so even gentle movement produces a proper, non-slow-motion stride.
        float motion = mag > 0f ? Mathf.Lerp(motionSpeedMin, motionSpeedMax, mag) : 0f;
        animator.SetFloat(motionHash, motion);
    }
}
