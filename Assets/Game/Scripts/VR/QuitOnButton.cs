using UnityEngine;
using UnityEngine.InputSystem;

// Quits the game when a controller button is pressed.
// Default binding is the left controller's Y button (X = primaryButton, Y = secondaryButton).
// In a build this calls Application.Quit(); in the editor it stops Play mode.
[DisallowMultipleComponent]
public class QuitOnButton : MonoBehaviour
{
    [Tooltip("Input System binding that quits the game. Default: left controller Y button.")]
    public string quitBinding = "<XRController>{LeftHand}/secondaryButton";

    InputAction quitAction;

    void Awake()
    {
        quitAction = new InputAction("QuitGame", InputActionType.Button, quitBinding);
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
