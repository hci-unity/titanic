using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class FirstPersonCameraRig : MonoBehaviour
{
    [Tooltip("The character root transform. Mouse-X yaws this.")]
    public Transform characterRoot;

    [Tooltip("The camera transform under the CameraRig. Mouse-Y pitches this (local X axis).")]
    public Transform cameraTransform;

    [Tooltip("Pitch clamp in degrees.")]
    public float pitchLimit = 85f;

    [Tooltip("Degrees per pixel of mouse delta.")]
    public float sensitivity = 0.12f;

    [Tooltip("Lock the cursor when this script is active.")]
    public bool lockCursor = true;

    float pitch;

    void OnEnable()
    {
        if (lockCursor && Application.isPlaying)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void OnDisable()
    {
        if (lockCursor && Application.isPlaying)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void Update()
    {
        if (characterRoot == null || cameraTransform == null) return;
        var mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 d = mouse.delta.ReadValue();
        float mx = d.x * sensitivity;
        float my = d.y * sensitivity;

        characterRoot.Rotate(0f, mx, 0f, Space.Self);
        pitch = Mathf.Clamp(pitch - my, -pitchLimit, pitchLimit);
        cameraTransform.localEulerAngles = new Vector3(pitch, 0f, 0f);
    }
}
