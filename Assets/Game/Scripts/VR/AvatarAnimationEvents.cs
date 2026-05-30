using UnityEngine;

// Silent receiver for the StarterAssets locomotion clips' animation events (OnFootstep / OnLand).
// Those clips fire these events expecting the StarterAssets player controller; we don't use it,
// so without a receiver Unity logs a "has no receiver" error every footstep. This no-op sink lives
// on the same GameObject as the Animator and absorbs them. Hook real footstep SFX here later.
[DisallowMultipleComponent]
public class AvatarAnimationEvents : MonoBehaviour
{
    public void OnFootstep(AnimationEvent _) { }
    public void OnLand(AnimationEvent _) { }
}
