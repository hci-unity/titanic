using UnityEngine;

// Drives the Humanoid mirror avatar from an IPoseSource (head + 2 hands), using Unity's
// built-in humanoid IK:
//   - Hands: OnAnimatorIK sets the LeftHand/RightHand IK goals to the controller targets
//     (Unity solves the arm/elbow). Requires "IK Pass" enabled on the Animator layer.
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
    [Range(0f, 1f)] public float handRotationWeight = 0f;

    [Header("Hand rotation calibration")]
    [Tooltip("Set TRUE at runtime while holding a neutral hand pose to auto-compute the controller->hand offsets, then this turns itself off and enables rotation tracking.")]
    public bool calibrateHands = false;
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

    [Tooltip("Move the avatar root to stand under the HMD (keeps body beneath the head).")]
    public bool followHmdHorizontal = true;

    [Tooltip("Temporary: log runtime IK state to the console.")]
    public bool debugLog = false;

    IPoseSource poseSource;
    Animator animator;
    Transform headTarget, leftHandTarget, rightHandTarget;
    Transform headBone, leftHandBone, rightHandBone;
    int _ikCalls;
    int _calibrateState;

    void Awake()
    {
        poseSource = poseSourceBehaviour as IPoseSource;
        animator = GetComponentInChildren<Animator>();
        if (animator != null) animator.applyRootMotion = false; // we drive the root ourselves

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
        _ikCalls++;
        RefreshTargets();

        if (followHmdHorizontal)
        {
            Vector3 hp = headTarget.position;
            transform.position = new Vector3(hp.x, transform.position.y, hp.z);
        }

        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, handWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, handRotationWeight);
        animator.SetIKPosition(AvatarIKGoal.LeftHand, leftHandTarget.position);
        animator.SetIKRotation(AvatarIKGoal.LeftHand, leftHandTarget.rotation * Quaternion.Euler(leftHandOffsetEuler));

        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, handWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, handRotationWeight);
        animator.SetIKPosition(AvatarIKGoal.RightHand, rightHandTarget.position);
        animator.SetIKRotation(AvatarIKGoal.RightHand, rightHandTarget.rotation * Quaternion.Euler(rightHandOffsetEuler));

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

    // Head match + hand-rotation calibration, after the animator has written the body pose.
    void LateUpdate()
    {
        if (!Ready) return;
        RefreshTargets();

        if (driveHead && headBone != null)
            headBone.rotation = headTarget.rotation;

        // Calibration: frame 1 forces relaxed wrists, frame 2 captures the natural pose as the offset.
        if (calibrateHands)
        {
            handRotationWeight = 0f;
            _calibrateState = 1;
            calibrateHands = false;
        }
        else if (_calibrateState == 1)
        {
            if (leftHandBone != null)
                leftHandOffsetEuler = (Quaternion.Inverse(leftHandTarget.rotation) * leftHandBone.rotation).eulerAngles;
            if (rightHandBone != null)
                rightHandOffsetEuler = (Quaternion.Inverse(rightHandTarget.rotation) * rightHandBone.rotation).eulerAngles;
            handRotationWeight = 1f;
            _calibrateState = 0;
            Debug.Log($"[MR] Calibrated hand offsets  L={leftHandOffsetEuler}  R={rightHandOffsetEuler}");
        }
    }
}
