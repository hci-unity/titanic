using UnityEngine;
using UnityEngine.InputSystem;

// Simple first-person CharacterController-driven movement, New Input System.
// WASD / arrows for horizontal movement, Space for jump, LeftShift for run, gravity applied.
// Pairs with FirstPersonCameraRig (mouse-look on character yaw + camera pitch).
[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Tooltip("Walk speed in m/s.")]
    public float walkSpeed = 4.0f;

    [Tooltip("Run speed in m/s (LeftShift).")]
    public float runSpeed = 7.0f;

    [Tooltip("Initial upward velocity on jump.")]
    public float jumpSpeed = 5.5f;

    [Tooltip("Gravity (m/s^2). Negative.")]
    public float gravity = -20f;

    CharacterController controller;
    Vector3 velocity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        float h = (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? -1f : 0f)
                + (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f);
        float v = (kb.sKey.isPressed || kb.downArrowKey.isPressed ? -1f : 0f)
                + (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f);

        Vector3 input = new Vector3(h, 0f, v);
        if (input.sqrMagnitude > 1f) input.Normalize();

        float speed = kb.leftShiftKey.isPressed ? runSpeed : walkSpeed;
        Vector3 horizontal = transform.TransformDirection(input) * speed;

        if (controller.isGrounded)
        {
            if (velocity.y < 0f) velocity.y = -2f;
            if (kb.spaceKey.wasPressedThisFrame)
            {
                velocity.y = jumpSpeed;
            }
        }
        velocity.y += gravity * Time.deltaTime;

        Vector3 move = horizontal + Vector3.up * velocity.y;
        controller.Move(move * Time.deltaTime);
    }
}
