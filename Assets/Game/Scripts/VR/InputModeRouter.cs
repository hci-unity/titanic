using System;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

// Routes the avatar's pose between the controller source and the hand-tracking source, following
// Unity/XRI's STANDARD input-modality signal (XRInputModalityManager) rather than a custom heuristic.
// That manager is the platform-blessed way to know whether the user is on controllers or tracked
// hands, and -- unlike raw subsystem tracking flags -- it switches correctly even over Air Link
// (verified: MotionController while holding controllers, TrackedHand while using bare hands).
//
// Itself an IPoseSource, so AvatarRigDriver points at it and never has to know which input is live.
[DisallowMultipleComponent]
public class InputModeRouter : MonoBehaviour, IPoseSource
{
    public enum InputMode { Controllers, Hands }

    [Tooltip("Controller pose source (ThreePointIKPoseSource).")]
    public MonoBehaviour controllerSourceBehaviour;
    [Tooltip("Hand-tracking pose source.")]
    public HandTrackingPoseSource handSource;

    public InputMode CurrentMode { get; private set; } = InputMode.Controllers;
    public event Action<InputMode> ModeChanged;

    IPoseSource controllerSource;

    // Pure: map XRI's standard input modality onto our pose-source mode. None (no input yet / between
    // modes) keeps the current mode so we never flicker to a dead state.
    public static InputMode MapModality(XRInputModalityManager.InputMode modality, InputMode current)
    {
        switch (modality)
        {
            case XRInputModalityManager.InputMode.MotionController: return InputMode.Controllers;
            case XRInputModalityManager.InputMode.TrackedHand: return InputMode.Hands;
            default: return current;
        }
    }

    void Awake() => controllerSource = controllerSourceBehaviour as IPoseSource;

    public bool IsActive =>
        (controllerSource != null && controllerSource.IsActive) ||
        (handSource != null && handSource.IsActive);

    void Update()
    {
        var next = MapModality(XRInputModalityManager.currentInputMode.Value, CurrentMode);
        if (next != CurrentMode) { CurrentMode = next; ModeChanged?.Invoke(next); }
    }

    public void UpdateTargets(Transform headTarget, Transform leftHandTarget, Transform rightHandTarget)
    {
        IPoseSource active = (CurrentMode == InputMode.Hands && handSource != null)
            ? handSource : controllerSource;
        if (active != null && active.IsActive)
            active.UpdateTargets(headTarget, leftHandTarget, rightHandTarget);
    }
}
