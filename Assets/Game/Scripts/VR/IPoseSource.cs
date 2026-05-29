using UnityEngine;

// A pose source positions the avatar's IK targets each frame from some tracking input.
// Default impl: ThreePointIKPoseSource (HMD + 2 controllers). Future: a mocap-driven source.
// The seam that lets mocap drop in later without touching the rig setup.
public interface IPoseSource
{
    // Called every frame by AvatarRigDriver. Implementations write world-space
    // target transforms for head and both hands.
    void UpdateTargets(
        Transform headTarget, Transform leftHandTarget, Transform rightHandTarget);

    bool IsActive { get; }
}
