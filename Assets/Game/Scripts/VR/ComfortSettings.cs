using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

// Switches the active turn style at runtime by enabling at most one turn provider.
// Snap is the comfort-safe default; Smooth (continuous) is opt-in for testers who prefer it;
// None disables stick-turning entirely so the player reorients by physically turning their
// body (suited to wireless/roomscale play where there's no cable to manage).
[DisallowMultipleComponent]
public class ComfortSettings : MonoBehaviour
{
    public enum TurnStyle { Snap, Smooth, None }

    [Tooltip("Snap turn provider on the XR Origin.")]
    public SnapTurnProvider snapTurn;

    [Tooltip("Continuous (smooth) turn provider on the XR Origin.")]
    public ContinuousTurnProvider smoothTurn;

    [Tooltip("Turn style active on start.")]
    public TurnStyle startStyle = TurnStyle.Snap;

    public TurnStyle Current { get; private set; }

    void Start() => SetTurnStyle(startStyle);

    public void SetTurnStyle(TurnStyle style)
    {
        Current = style;
        if (snapTurn != null) snapTurn.enabled = (style == TurnStyle.Snap);
        if (smoothTurn != null) smoothTurn.enabled = (style == TurnStyle.Smooth);
    }

    public void UseSnap() => SetTurnStyle(TurnStyle.Snap);
    public void UseSmooth() => SetTurnStyle(TurnStyle.Smooth);
    public void UseNone() => SetTurnStyle(TurnStyle.None);

    // Cycles Snap -> Smooth -> None -> Snap.
    public void Toggle()
    {
        switch (Current)
        {
            case TurnStyle.Snap:   SetTurnStyle(TurnStyle.Smooth); break;
            case TurnStyle.Smooth: SetTurnStyle(TurnStyle.None);   break;
            default:               SetTurnStyle(TurnStyle.Snap);   break;
        }
    }
}
