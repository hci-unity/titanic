using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Enables exactly one mirror-avatar character at a time and cycles on a controller button.
// Each entry in `characters` is a layer-8 Humanoid avatar root with its own AvatarRigDriver,
// pre-wired (in the scene) to the shared InputModeRouter. Adding a character = add its root to
// `characters` in the inspector (SelfViewManager follows the switch automatically).
[DisallowMultipleComponent]
public class MirrorCharacterSwitcher : MonoBehaviour
{
    [Tooltip("Character roots. Index 0 is active on Start.")]
    public List<GameObject> characters = new List<GameObject>();

    [Tooltip("Input System binding that advances to the next character.")]
    public string nextBinding = "<XRController>{RightHand}/primaryButton";

    InputAction nextAction;
    int current = -1;

    // Pure, testable index wrap (handles negative + overflow).
    public static int WrapIndex(int index, int count)
    {
        if (count <= 0) return 0;
        return ((index % count) + count) % count;
    }

    void Awake()
    {
        nextAction = new InputAction("NextCharacter", InputActionType.Button, nextBinding);
    }

    void OnEnable() { nextAction?.Enable(); }
    void OnDisable() { nextAction?.Disable(); }

    void Start() { SetCharacter(0); }

    void Update()
    {
        if (nextAction != null && nextAction.WasPressedThisFrame()) Next();
    }

    public void Next()
    {
        if (characters.Count == 0) return;
        SetCharacter(current + 1);
    }

    public void SetCharacter(int index)
    {
        if (characters.Count == 0) return;
        current = WrapIndex(index, characters.Count);
        for (int i = 0; i < characters.Count; i++)
            if (characters[i] != null) characters[i].SetActive(i == current);
    }

    public int CurrentIndex => current;
}
