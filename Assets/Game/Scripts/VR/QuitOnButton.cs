using UnityEngine;
using UnityEngine.InputSystem;

// Quits on a controller button (Y, controller mode) OR the keyboard Escape key (operator). In a build
// this calls Application.Quit(); in the editor it stops Play mode. Hands mode has no in-experience
// quit by design -- the operator uses Escape.
[DisallowMultipleComponent]
public class QuitOnButton : MonoBehaviour
{
    [Tooltip("Input System binding that quits the game. Default: left controller Y button.")]
    public string quitBinding = "<XRController>{LeftHand}/secondaryButton";

    [Tooltip("Operator keyboard binding that also quits. Default: Escape.")]
    public string quitKeyBinding = "<Keyboard>/escape";

    InputAction quitAction;

    void Awake()
    {
        quitAction = new InputAction("QuitGame", InputActionType.Button, quitBinding);
        quitAction.AddBinding(quitKeyBinding);
    }

    void OnEnable() { quitAction?.Enable(); }
    void OnDisable() { quitAction?.Disable(); }

    void Update()
    {
        if (quitAction != null && quitAction.WasPressedThisFrame()) Quit();
    }

    public void Quit()
    {
        Debug.Log("QuitOnButton: quit requested.");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
