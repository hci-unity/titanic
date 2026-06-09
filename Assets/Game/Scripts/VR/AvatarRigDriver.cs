using UnityEngine;

// Drives the Humanoid mirror avatar from an IPoseSource (head + 2 hands), using Unity's
// built-in humanoid IK:
//   - Hand POSITION: OnAnimatorIK sets the LeftHand/RightHand IK position goals to the
//     controller targets (Unity solves the arm/elbow). Requires "IK Pass" enabled on the
//     Animator layer.
//   - Hand ROTATION: the hand bones' world rotation is written DIRECTLY in LateUpdate (not
//     via SetIKRotation, whose goal Unity clamps back toward the natural pose). Each wrist
//     follows its controller through a constant calibrated offset (left/rightHandOffsetEuler).
//   - Head: the head bone's world rotation is matched to the HMD in LateUpdate (after the
//     animator writes the pose), so the reflected head turns exactly with the player's head.
//   - Body: the avatar root follows the HMD horizontally so the body stands under the head
//     (legs/torso stay in the idle pose).
//
// The IPoseSource seam is unchanged, so a mocap-driven source can replace ThreePointIKPoseSource
// later without touching this driver.
[DisallowMultipleComponent]
public class AvatarRigDriver : MonoBehaviour
{
    [Tooltip("Component implementing IPoseSource (e.g. ThreePointIKPoseSource).")]
    public MonoBehaviour poseSourceBehaviour;

    [Tooltip("Position tracking weight for the hands.")]
    [Range(0f, 1f)] public float handWeight = 1f;

    [Tooltip("Rotation tracking weight for the hands. 0 = wrists relax naturally; 1 = wrists follow the controllers using the calibrated offsets below.")]
    [Range(0f, 1f)] public float handRotationWeight = 1f;

    [Header("Hand rotation offsets (calibrated constants)")]
    [Tooltip("Constant controller->hand-bone rotation offset per hand. Baked once for this rig + Touch controllers; the same for every user. Re-derive only if the avatar rig changes.")]
    public Vector3 leftHandOffsetEuler = Vector3.zero;
    public Vector3 rightHandOffsetEuler = Vector3.zero;

    [Header("Elbow hints (keep elbows from caving into the body)")]
    [Range(0f, 1f)] public float elbowHintWeight = 1f;
    [Tooltip("How far out to the side the elbow hint sits (m).")]
    public float elbowOut = 0.45f;
    [Tooltip("How far behind the arm the elbow hint sits (m).")]
    public float elbowBack = 0.30f;
    [Tooltip("How far below the arm the elbow hint sits (m).")]
    public float elbowDown = 0.20f;

    [Tooltip("Match the avatar's head bone to the HMD orientation.")]
    public bool driveHead = true;

    [Tooltip("Constant HMD->head-bone rotation offset for this rig. Baked once (per rig) when the head-bone rest orientation differs from the HMD convention (e.g. a Meshy head that pitches up). Re-derive only if the avatar rig changes.")]
    public Vector3 headOffsetEuler = Vector3.zero;

    [Tooltip("Move the avatar root to stand under the HMD (keeps body beneath the head).")]
    public bool followHmdHorizontal = true;

    [Header("Hand reach (head-relative mapping)")]
    [Tooltip("Place hand IK goals relative to the AVATAR's head instead of absolute world space, so a " +
             "shorter/differently-proportioned avatar reaches naturally. Applies to both controllers and hand tracking.")]
    public bool headRelativeHands = true;

    [Tooltip("Auto-adapt reach to the CURRENT wearer by their MEASURED reach (running max hand-to-head). " +
             "Scales reachScale by (referenceReach / current user's reach) so a shorter/taller tester's " +
             "elbows straighten the same as the calibrating user's. Decays for tester handovers.")]
    public bool autoReach = true;

    [Tooltip("Reach scale tuned for the CALIBRATING user (per character). 1 = hands map 1:1 from head; " +
             ">1 lengthens reach. With autoReach on, this is the value at the reference user's reach.")]
    public float reachScale = 1f;

    [Tooltip("The calibrating user's measured reach (m, hand-to-head at full extension). Same on all " +
             "characters. Used by autoReach to rescale for other users. Baked once in-headset.")]
    public float referenceReach = 0.75f;

    [Tooltip("How fast the measured-reach estimate shrinks toward smaller reaches (m/s). Lets a shorter " +
             "tester's reach take over within a few seconds after a handover; grows instantly.")]
    public float reachDecaySpeed = 0.08f;

    [Header("Torso follow (head-vs-body yaw)")]
    [Tooltip("Rotate the avatar root to follow the head's yaw with a hysteresis dead-zone: brief/small glances keep the shoulders still; sustained/large head turns swing the torso to match. Cosmetic — affects only the mirror reflection.")]
    public bool followYaw = true;

    [Tooltip("Head-vs-torso yaw offset (deg) at which the torso STARTS following.")]
    public float startAngle = 40f;

    [Tooltip("Offset (deg) at which the torso STOPS following (hysteresis; keep < startAngle).")]
    public float stopAngle = 5f;

    [Tooltip("How fast the torso swings to catch up while following (deg/s).")]
    public float maxYawSpeed = 180f;

    bool yawFollowing; // hysteresis state for StepYaw
    float userReach;   // measured-reach estimate for autoReach (seeded to referenceReach)

    IPoseSource poseSource;
    Animator animator;
    Transform headTarget, leftHandTarget, rightHandTarget;
    Transform headBone, leftHandBone, rightHandBone;

    // Hysteresis-gated yaw step. Returns the new root yaw (deg). `following` carries the
    // dead-zone state across frames: once |offset| >= startAngle the torso follows the head
    // until |offset| <= stopAngle, then it locks again (glance = no follow, commit = follow).
    public static float StepYaw(float currentYaw, float desiredYaw, float dt,
        float startAngle, float stopAngle, float maxYawSpeed, ref bool following)
    {
        float offset = Mathf.DeltaAngle(currentYaw, desiredYaw);
        float mag = Mathf.Abs(offset);

        if (!following && mag >= startAngle) following = true;
        else if (following && mag <= stopAngle) following = false;

        if (!following) return currentYaw;

        float step = Mathf.Min(mag, maxYawSpeed * Mathf.Max(dt, 0f));
        return currentYaw + Mathf.Sign(offset) * step;
    }

    // Pure: remap a hand target from the user's head frame onto the avatar's head frame, scaling reach.
    // Makes a differently-proportioned avatar place its hands naturally relative to its own head.
    public static Vector3 HeadRelative(Vector3 handPos, Vector3 userHeadPos, Vector3 avatarHeadPos, float reachScale)
        => avatarHeadPos + (handPos - userHeadPos) * reachScale;

    // Pure: effective reach scale for the current wearer. When auto, rescale the tuned reachScale by
    // (referenceReach / current user's measured reach) so any arm length straightens the same as the
    // calibrating user. Clamped so a bad reading can't explode the reach.
    public static float EffectiveReachScale(bool auto, float reachScale, float referenceReach, float userReach)
    {
        if (!auto || userReach <= 0.05f) return reachScale;
        return Mathf.Clamp(reachScale * referenceReach / userReach, 0.3f, 2.5f);
    }

    // Pure: track the user's reach as a running max that grows instantly but shrinks slowly (so a brief
    // retraction doesn't drop it, but a shorter new wearer is adopted within a few seconds). observed is
    // the clamped hand-to-head distance this frame.
    public static float StepUserReach(float current, float observed, float dt, float decaySpeed)
    {
        if (observed >= current) return observed;
        return Mathf.MoveTowards(current, observed, Mathf.Max(decaySpeed, 0f) * Mathf.Max(dt, 0f));
    }

    void Awake()
    {
        poseSource = poseSourceBehaviour as IPoseSource;
        animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false; // we drive the root ourselves
        userReach = referenceReach;

        headTarget = NewChild("_HeadTarget");
        leftHandTarget = NewChild("_LeftHandTarget");
        rightHandTarget = NewChild("_RightHandTarget");

        if (animator != null && animator.isHuman)
        {
            headBone = animator.GetBoneTransform(HumanBodyBones.Head);
            leftHandBone = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            rightHandBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }

        if (poseSource == null)
            Debug.LogError("AvatarRigDriver: poseSourceBehaviour does not implement IPoseSource.");
    }

    Transform NewChild(string n)
    {
        var t = new GameObject(n).transform;
        t.SetParent(transform, false);
        return t;
    }

    bool Ready => poseSource != null && poseSource.IsActive && animator != null;

    void RefreshTargets() => poseSource.UpdateTargets(headTarget, leftHandTarget, rightHandTarget);

    // Hands via Unity's humanoid IK solver (needs IK Pass enabled on the Animator layer).
    void OnAnimatorIK(int layerIndex)
    {
        if (!Ready) return;
        RefreshTargets();

        if (followHmdHorizontal)
        {
            Vector3 hp = headTarget.position;
            transform.position = new Vector3(hp.x, transform.position.y, hp.z);
        }

        if (followYaw)
        {
            Vector3 f = headTarget.forward; f.y = 0f;
            if (f.sqrMagnitude > 1e-6f)
            {
                float desiredYaw = Quaternion.LookRotation(f).eulerAngles.y;
                float newYaw = StepYaw(transform.eulerAngles.y, desiredYaw, Time.deltaTime,
                    startAngle, stopAngle, maxYawSpeed, ref yawFollowing);
                Vector3 e = transform.eulerAngles;
                transform.eulerAngles = new Vector3(e.x, newYaw, e.z);
            }
        }

        // Position only. The wrist ROTATION is driven directly in LateUpdate (see below) —
        // Unity's humanoid hand-rotation IK goal gets clamped back toward the natural pose,
        // so SetIKRotation can't actually orient the wrist.
        //
        // Hand POSITIONS are optionally remapped into the avatar's head frame (head-relative reach) so a
        // shorter/differently-proportioned avatar reaches naturally instead of straining toward the
        // player's absolute hand positions. headTarget.position is the HMD (user head); headBone is the
        // avatar head. Applies to whichever pose source is active (controllers or hand tracking).
        Vector3 leftPos = leftHandTarget.position;
        Vector3 rightPos = rightHandTarget.position;
        if (headRelativeHands && headBone != null)
        {
            Vector3 userHead = headTarget.position;
            Vector3 avatarHead = headBone.position;
            if (autoReach)
            {
                float obs = Mathf.Clamp(Mathf.Max(
                    Vector3.Distance(leftPos, userHead), Vector3.Distance(rightPos, userHead)), 0.2f, 1.0f);
                userReach = StepUserReach(userReach, obs, Time.deltaTime, reachDecaySpeed);
            }
            float scale = EffectiveReachScale(autoReach, reachScale, referenceReach, userReach);
            leftPos = HeadRelative(leftPos, userHead, avatarHead, scale);
            rightPos = HeadRelative(rightPos, userHead, avatarHead, scale);
        }

        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, handWeight);
        animator.SetIKPosition(AvatarIKGoal.LeftHand, leftPos);

        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, handWeight);
        animator.SetIKPosition(AvatarIKGoal.RightHand, rightPos);

        SetElbowHint(AvatarIKHint.LeftElbow, HumanBodyBones.LeftLowerArm, -1f);
        SetElbowHint(AvatarIKHint.RightElbow, HumanBodyBones.RightLowerArm, 1f);
    }

    // Bias the elbow outward/back/down so the arm bends naturally instead of caving inward.
    void SetElbowHint(AvatarIKHint hint, HumanBodyBones lowerArm, float side)
    {
        var lower = animator.GetBoneTransform(lowerArm);
        if (lower == null) return;
        Vector3 p = lower.position
                  + transform.right * (side * elbowOut)
                  - transform.forward * elbowBack
                  - transform.up * elbowDown;
        animator.SetIKHintPositionWeight(hint, elbowHintWeight);
        animator.SetIKHintPosition(hint, p);
    }

    // Head + wrist rotation, after the animator has written the body pose.
    void LateUpdate()
    {
        if (!Ready) return;
        RefreshTargets();

        if (driveHead && headBone != null)
            headBone.rotation = headTarget.rotation * Quaternion.Euler(headOffsetEuler);

        // Wrist rotation: write the hand bones' world rotation directly here, after the
        // animator/IK pass — the same proven technique used for the head above. Each hand
        // follows its controller through the calibrated constant offset. Blended by weight.
        if (handRotationWeight > 0f)
        {
            if (leftHandBone != null)
                leftHandBone.rotation = Quaternion.Slerp(leftHandBone.rotation,
                    leftHandTarget.rotation * Quaternion.Euler(leftHandOffsetEuler), handRotationWeight);
            if (rightHandBone != null)
                rightHandBone.rotation = Quaternion.Slerp(rightHandBone.rotation,
                    rightHandTarget.rotation * Quaternion.Euler(rightHandOffsetEuler), handRotationWeight);
        }
    }
}
