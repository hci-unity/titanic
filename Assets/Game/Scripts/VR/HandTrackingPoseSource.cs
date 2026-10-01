using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;

// IPoseSource driven by Quest hand tracking (XR Hands). Head comes from the HMD (identical to the
// controller source); each avatar hand target comes from the player's tracked WRIST joint, with a
// constant calibrated offset that maps the OpenXR wrist convention onto the same convention the Touch
// controller grip produces -- so AvatarRigDriver's per-character offsets keep working unchanged.
//
// Also the single owner of "the player's tracked hands": exposes per-hand tracked state + any joint's
// world pose (TryGetJointWorld, for FingerPoseDriver), so nothing else has to read the XR Hands subsystem.
//
// Tracking note (Air Link): the high-level XRHand.isTracked flag is NOT reliable over Air Link while
// the controllers are still awake -- the runtime delivers valid joint poses but leaves isTracked false.
// So "tracked" here means the WRIST JOINT POSE is valid, which is the actual data-availability signal.
// Detection runs every frame in Update() (not only when this source is active) so the router can decide
// to switch TO hands in the first place.
[DisallowMultipleComponent]
public class HandTrackingPoseSource : MonoBehaviour, IPoseSource
{
    [Header("Tracked sources")]
    [Tooltip("HMD transform (same one the controller source uses).")]
    public Transform hmd;
    [Tooltip("Tracking-space transform that XR Hands joint poses are relative to. On this rig (Device " +
             "tracking + manual camera Y-offset) that is the CAMERA OFFSET object, NOT the XR Origin root.")]
    public Transform trackingSpace;

    [Header("Wrist convention offset (calibrated constant, rig-independent)")]
    public Vector3 leftWristRotationOffsetEuler = Vector3.zero;
    public Vector3 rightWristRotationOffsetEuler = Vector3.zero;
    [Tooltip("Wrist-joint -> hand-target position nudge, in wrist-local space.")]
    public Vector3 handPositionOffset = Vector3.zero;

    XRHandSubsystem subsystem;
    readonly List<XRHandSubsystem> buffer = new();

    // Per-hand "wrist pose valid this frame" (read by FingerPoseDriver).
    public bool LeftTracked { get; private set; }
    public bool RightTracked { get; private set; }

    // Latest wrist poses in world space (valid only when the matching *Tracked flag is true).
    Pose leftWristWorld, rightWristWorld;

    public bool IsActive => hmd != null && subsystem != null && subsystem.running;

    void Update()
    {
        EnsureSubsystem();
        if (subsystem == null) { LeftTracked = RightTracked = false; return; }
        Transform space = trackingSpace != null ? trackingSpace : transform;
        LeftTracked = ReadWrist(subsystem.leftHand, space, ref leftWristWorld);
        RightTracked = ReadWrist(subsystem.rightHand, space, ref rightWristWorld);
    }

    void EnsureSubsystem()
    {
        if (subsystem != null && subsystem.running) return;
        SubsystemManager.GetSubsystems(buffer);
        subsystem = null;
        foreach (var s in buffer) if (s.running) { subsystem = s; break; }
    }

    // Reads one hand's wrist world pose. Returns whether it is valid this frame.
    static bool ReadWrist(XRHand hand, Transform space, ref Pose wristWorld)
    {
        if (!hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out Pose wrist)) return false;
        wristWorld = new Pose(space.TransformPoint(wrist.position), space.rotation * wrist.rotation);
        return true;
    }

    // Public read-only accessor: world-space pose of ANY hand joint, for the finger driver.
    // Returns false if the subsystem is missing or that joint has no valid pose this frame.
    // Same Camera-Offset -> world conversion the wrist uses.
    public bool TryGetJointWorld(Handedness handedness, XRHandJointID id, out Pose worldPose)
    {
        worldPose = default;
        if (subsystem == null) return false;
        Transform space = trackingSpace != null ? trackingSpace : transform;
        XRHand hand = handedness == Handedness.Left ? subsystem.leftHand : subsystem.rightHand;
        if (hand.GetJoint(id).TryGetPose(out Pose p))
        {
            worldPose = new Pose(space.TransformPoint(p.position), space.rotation * p.rotation);
            return true;
        }
        return false;
    }

    public void UpdateTargets(Transform headTarget, Transform leftHandTarget, Transform rightHandTarget)
    {
        if (hmd != null) headTarget.SetPositionAndRotation(hmd.position, hmd.rotation);
        WriteHand(leftHandTarget, LeftTracked, leftWristWorld, Quaternion.Euler(leftWristRotationOffsetEuler));
        WriteHand(rightHandTarget, RightTracked, rightWristWorld, Quaternion.Euler(rightWristRotationOffsetEuler));
    }

    // Untracked hand: leave the target as-is (freeze last pose).
    void WriteHand(Transform target, bool tracked, Pose wristWorld, Quaternion wristOffset)
    {
        if (!tracked) return;
        target.SetPositionAndRotation(
            wristWorld.position + wristWorld.rotation * handPositionOffset,
            wristWorld.rotation * wristOffset);
    }
}
