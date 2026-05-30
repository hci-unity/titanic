using UnityEngine;
using UnityEngine.InputSystem;

// Feeds the player's movement-stick input into the avatar Animator's locomotion parameters,
// so the mirror reflection walks/idles with the player. Hands + head stay IK-driven on top
// (IK pass + AvatarRigDriver.LateUpdate). Root motion stays OFF, so the walk plays in place
// while AvatarRigDriver positions the body under the HMD.
//
// Why stick input and not CharacterController.velocity: on this XR rig the capsule velocity
// reads ~1.3 m/s even while standing still (the rig continuously repositions the capsule under
// the HMD), so it can't distinguish idle from walking. The locomotion stick magnitude is the
// reliable "intent to move" signal and cleanly returns to zero when released.
[DisallowMultipleComponent]
public class LocomotionAnimatorDriver : MonoBehaviour
{
    [Tooltip("Input System binding for the movement stick (Vector2). Magnitude drives the walk.")]
    public string moveBinding = "<XRController>{LeftHand}/thumbstick";

    [Tooltip("Animator with a locomotion blend tree. Auto-found in children if null.")]
    public Animator animator;

    [Tooltip("Float parameter driven by movement magnitude (StarterAssetsThirdPerson uses 'Speed').")]
    public string speedParameter = "Speed";

    [Tooltip("Clip-playback-speed parameter; gates the walk clip so idle doesn't animate (StarterAssets 'MotionSpeed').")]
    public string motionSpeedParameter = "MotionSpeed";

    [Tooltip("Speed value at full stick deflection (tune to the blend tree; ~2 = walk, higher leans into run).")]
    public float maxSpeed = 2.0f;

    [Tooltip("Stick magnitude below this counts as idle (snaps Speed + MotionSpeed to 0).")]
    public float deadzone = 0.15f;

    [Tooltip("Smoothing time for the speed value (seconds).")]
    public float damp = 0.12f;

    [Tooltip("Legacy/optional; not used to derive speed (kept so existing scene wiring stays valid).")]
    public CharacterController source;

    InputAction moveAction;
    int speedHash, motionHash;
    float speed;

    // Pure, testable: horizontal magnitude of a velocity (retained for assertions / external callers).
    public static float HorizontalSpeed(Vector3 velocity)
    {
        velocity.y = 0f;
        return velocity.magnitude;
    }

    void Awake()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>();
        speedHash = Animator.StringToHash(speedParameter);
        motionHash = Animator.StringToHash(motionSpeedParameter);
        moveAction = new InputAction("Move", InputActionType.Value, moveBinding, expectedControlType: "Vector2");
    }

    void OnEnable() { moveAction?.Enable(); }
    void OnDisable() { moveAction?.Disable(); }

    void Update()
    {
        if (animator == null) return;
        float mag = moveAction != null ? Mathf.Clamp01(moveAction.ReadValue<Vector2>().magnitude) : 0f;
        if (mag < deadzone) mag = 0f;

        float target = mag * maxSpeed;
        speed = Mathf.Lerp(speed, target, damp > 0f ? Time.deltaTime / damp : 1f);
        animator.SetFloat(speedHash, speed);
        animator.SetFloat(motionHash, mag);   // 0 when idle -> walk clip freezes to idle pose
    }
}
