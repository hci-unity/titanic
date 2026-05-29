using UnityEngine;

// Maps the headset and two controllers onto the avatar's head/hand IK targets.
// Optional per-axis offsets correct for grip pose vs. palm/bone alignment.
[DisallowMultipleComponent]
public class ThreePointIKPoseSource : MonoBehaviour, IPoseSource
{
    [Header("Tracked sources (from the XR rig)")]
    public Transform hmd;
    public Transform leftController;
    public Transform rightController;

    [Header("Offsets (local, applied to hand targets)")]
    public Vector3 handPositionOffset = Vector3.zero;
    public Vector3 handRotationEuler = Vector3.zero;

    public bool IsActive => hmd != null && leftController != null && rightController != null;

    public void UpdateTargets(Transform headTarget, Transform leftHandTarget, Transform rightHandTarget)
    {
        if (!IsActive) return;

        headTarget.SetPositionAndRotation(hmd.position, hmd.rotation);

        var rot = Quaternion.Euler(handRotationEuler);
        leftHandTarget.SetPositionAndRotation(
            leftController.TransformPoint(handPositionOffset), leftController.rotation * rot);
        rightHandTarget.SetPositionAndRotation(
            rightController.TransformPoint(handPositionOffset), rightController.rotation * rot);
    }
}
