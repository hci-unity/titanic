using UnityEngine;
using UnityEngine.XR.Hands;

// Drives a Humanoid avatar's finger bones from Quest hand-tracking joints (hand-tracking only).
// Method: aim each finger bone so its segment (joint->child) points along the matching XR Hand
// segment's world direction -- the same direct-world-rotation idiom AvatarRigDriver uses for the
// wrist/head, extended down the finger chain. Direction-copy only, so NO per-rig calibration and
// immune to finger-length differences. Roll is left free (fingers are hinges).
//
// Runs in LateUpdate AFTER AvatarRigDriver (execution order 100 > its 0) so the hand bone -- the
// parent of these finger bones -- is already oriented.
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public class FingerPoseDriver : MonoBehaviour
{
    // One bone to aim. currentDir = pos(SegTo) - pos(SegFrom); targetDir = world(XrTo) - world(XrFrom).
    public readonly struct FingerBoneSpec
    {
        public readonly HumanBodyBones Aim;
        public readonly HumanBodyBones SegFrom;
        public readonly HumanBodyBones SegTo;
        public readonly XRHandJointID XrFrom;
        public readonly XRHandJointID XrTo;
        public FingerBoneSpec(HumanBodyBones aim, HumanBodyBones segFrom, HumanBodyBones segTo,
                              XRHandJointID xrFrom, XRHandJointID xrTo)
        { Aim = aim; SegFrom = segFrom; SegTo = segTo; XrFrom = xrFrom; XrTo = xrTo; }
    }

    // Left-hand chain (15 bones, root->tip per finger). Right hand reuses these via Mirror() (the
    // Humanoid finger bone enums are laid out Left 24..38, Right 39..53 -> a constant +15 offset).
    static readonly FingerBoneSpec[] LeftBase =
    {
        // Thumb (XR thumb joints: Metacarpal, Proximal, Distal, Tip -- no Intermediate)
        new FingerBoneSpec(HumanBodyBones.LeftThumbProximal,     HumanBodyBones.LeftThumbProximal,     HumanBodyBones.LeftThumbIntermediate, XRHandJointID.ThumbMetacarpal, XRHandJointID.ThumbProximal),
        new FingerBoneSpec(HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,       XRHandJointID.ThumbProximal,   XRHandJointID.ThumbDistal),
        new FingerBoneSpec(HumanBodyBones.LeftThumbDistal,       HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,       XRHandJointID.ThumbDistal,     XRHandJointID.ThumbTip),
        // Index
        new FingerBoneSpec(HumanBodyBones.LeftIndexProximal,     HumanBodyBones.LeftIndexProximal,     HumanBodyBones.LeftIndexIntermediate, XRHandJointID.IndexProximal,     XRHandJointID.IndexIntermediate),
        new FingerBoneSpec(HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,       XRHandJointID.IndexIntermediate, XRHandJointID.IndexDistal),
        new FingerBoneSpec(HumanBodyBones.LeftIndexDistal,       HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,       XRHandJointID.IndexDistal,       XRHandJointID.IndexTip),
        // Middle
        new FingerBoneSpec(HumanBodyBones.LeftMiddleProximal,     HumanBodyBones.LeftMiddleProximal,     HumanBodyBones.LeftMiddleIntermediate, XRHandJointID.MiddleProximal,     XRHandJointID.MiddleIntermediate),
        new FingerBoneSpec(HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,       XRHandJointID.MiddleIntermediate, XRHandJointID.MiddleDistal),
        new FingerBoneSpec(HumanBodyBones.LeftMiddleDistal,       HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,       XRHandJointID.MiddleDistal,       XRHandJointID.MiddleTip),
        // Ring
        new FingerBoneSpec(HumanBodyBones.LeftRingProximal,     HumanBodyBones.LeftRingProximal,     HumanBodyBones.LeftRingIntermediate, XRHandJointID.RingProximal,     XRHandJointID.RingIntermediate),
        new FingerBoneSpec(HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,       XRHandJointID.RingIntermediate, XRHandJointID.RingDistal),
        new FingerBoneSpec(HumanBodyBones.LeftRingDistal,       HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,       XRHandJointID.RingDistal,       XRHandJointID.RingTip),
        // Little
        new FingerBoneSpec(HumanBodyBones.LeftLittleProximal,     HumanBodyBones.LeftLittleProximal,     HumanBodyBones.LeftLittleIntermediate, XRHandJointID.LittleProximal,     XRHandJointID.LittleIntermediate),
        new FingerBoneSpec(HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,       XRHandJointID.LittleIntermediate, XRHandJointID.LittleDistal),
        new FingerBoneSpec(HumanBodyBones.LeftLittleDistal,       HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,       XRHandJointID.LittleDistal,       XRHandJointID.LittleTip),
    };

    // Map a LEFT-hand humanoid finger bone to its RIGHT-hand equivalent (+15 in the enum).
    public static HumanBodyBones Mirror(HumanBodyBones leftFingerBone)
        => (HumanBodyBones)((int)leftFingerBone + 15);

    // The 15 specs for one hand. Left = base; Right = base with every humanoid bone mirrored
    // (XR joint IDs are hand-agnostic -- handedness is chosen by which XRHand is read).
    public static FingerBoneSpec[] Chains(bool left)
    {
        if (left) return (FingerBoneSpec[])LeftBase.Clone();
        var r = new FingerBoneSpec[LeftBase.Length];
        for (int i = 0; i < LeftBase.Length; i++)
        {
            var s = LeftBase[i];
            r[i] = new FingerBoneSpec(Mirror(s.Aim), Mirror(s.SegFrom), Mirror(s.SegTo), s.XrFrom, s.XrTo);
        }
        return r;
    }

    // Aim: rotate currentRot so the world vector currentDir is mapped onto targetDir. Degenerate
    // (near-zero) inputs leave the rotation unchanged.
    public static Quaternion Aim(Vector3 currentDir, Vector3 targetDir, Quaternion currentRot)
    {
        if (currentDir.sqrMagnitude < 1e-10f || targetDir.sqrMagnitude < 1e-10f) return currentRot;
        return Quaternion.FromToRotation(currentDir, targetDir) * currentRot;
    }

    // Gate: drive fingers only in Hands mode with this hand tracked.
    public static bool ShouldDrive(InputModeRouter.InputMode mode, bool handTracked)
        => mode == InputModeRouter.InputMode.Hands && handTracked;

    [Header("Sources")]
    [Tooltip("Input-mode router (selects controllers vs hands). Fingers only drive in Hands mode.")]
    public InputModeRouter router;
    [Tooltip("The single XR Hands reader that owns finger joint data.")]
    public HandTrackingPoseSource handSource;

    [Header("Tuning")]
    [Tooltip("0 = fingers stay in the idle animation pose; 1 = fully follow the tracked hand.")]
    [Range(0f, 1f)] public float fingerWeight = 1f;
    [Tooltip("Low-pass on finger motion. 0 = snap to tracked pose (no smoothing); higher = smoother but laggier.")]
    [Range(0f, 0.95f)] public float smoothing = 0f;

    Animator animator;
    FingerBoneSpec[] leftSpecs, rightSpecs;
    Transform[] lAim, lFrom, lTo, rAim, rFrom, rTo;   // cached bone transforms, parallel to specs
    // Last applied world rotations (for smoothing + freeze); null = none yet. Nullable because
    // Quaternion's == is dot-based, so default(Quaternion) never compares equal to itself.
    Quaternion?[] lPrev, rPrev;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) { enabled = false; return; }
        leftSpecs = Chains(true);
        rightSpecs = Chains(false);
        Cache(leftSpecs, out lAim, out lFrom, out lTo, out lPrev);
        Cache(rightSpecs, out rAim, out rFrom, out rTo, out rPrev);
    }

    void Cache(FingerBoneSpec[] specs, out Transform[] aim, out Transform[] from, out Transform[] to, out Quaternion?[] prev)
    {
        aim = new Transform[specs.Length];
        from = new Transform[specs.Length];
        to = new Transform[specs.Length];
        prev = new Quaternion?[specs.Length];
        for (int i = 0; i < specs.Length; i++)
        {
            aim[i]  = animator.GetBoneTransform(specs[i].Aim);
            from[i] = animator.GetBoneTransform(specs[i].SegFrom);
            to[i]   = animator.GetBoneTransform(specs[i].SegTo);
        }
    }

    void LateUpdate()
    {
        if (animator == null || router == null || handSource == null) return;
        DriveHand(Handedness.Left,  handSource.LeftTracked,  leftSpecs,  lAim, lFrom, lTo, lPrev);
        DriveHand(Handedness.Right, handSource.RightTracked, rightSpecs, rAim, rFrom, rTo, rPrev);
    }

    void DriveHand(Handedness h, bool tracked, FingerBoneSpec[] specs,
                   Transform[] aim, Transform[] from, Transform[] to, Quaternion?[] prev)
    {
        // Controller mode -> leave fingers in the idle animation pose; clear prev so we don't
        // snap from a stale tracked pose when hands resume.
        if (router.CurrentMode != InputModeRouter.InputMode.Hands)
        {
            for (int i = 0; i < prev.Length; i++) prev[i] = null;
            return;
        }

        // Hands mode but this hand dropped out -> freeze the last good pose by re-applying it
        // (no pose yet -> leave the idle animation pose).
        if (!ShouldDrive(router.CurrentMode, tracked))
        {
            for (int i = 0; i < specs.Length; i++)
                if (aim[i] != null && prev[i].HasValue) aim[i].rotation = prev[i].Value;
            return;
        }

        // Tracked: aim each bone root->tip (array is ordered proximal->intermediate->distal per finger).
        for (int i = 0; i < specs.Length; i++)
        {
            if (aim[i] == null || from[i] == null || to[i] == null) continue;
            var s = specs[i];
            if (!handSource.TryGetJointWorld(h, s.XrFrom, out Pose pf)) continue;
            if (!handSource.TryGetJointWorld(h, s.XrTo, out Pose pt)) continue;

            Vector3 currentDir = to[i].position - from[i].position;
            Vector3 targetDir  = pt.position - pf.position;
            Quaternion aimed   = Aim(currentDir, targetDir, aim[i].rotation);
            Quaternion target  = Quaternion.Slerp(aim[i].rotation, aimed, fingerWeight);
            if (smoothing > 0f && prev[i].HasValue)
                target = Quaternion.Slerp(prev[i].Value, target, 1f - smoothing);

            aim[i].rotation = target;
            prev[i] = target;
        }
    }
}
