using UnityEngine;
using Unity.Netcode;
using TMPro;
using NUnit.Framework;

public class PlayerNetworkController : NetworkBehaviour
{
    [Header("Movement Speeds")]
    [SerializeField] float walkSpeed = 6.5f;
    [SerializeField] float runSpeed = 9.5f;
    [SerializeField] float airControlMultiplier = 0.9f;

    [Header("Jump / Gravity")]
    [SerializeField] float jumpForce = 6.5f;
    [SerializeField] float gravity = -18f;

    [Header("Mouse Look")]
    [SerializeField] float mouseSensitivity = 3f;
    [SerializeField] float maxLookAngle = 85f;

    [Header("Jump Tuning")]
    [SerializeField] float groundStickForce = -8f;
    [SerializeField] float coyoteTime = 0.10f;
    [SerializeField] float jumpBufferTime = 0.12f;

    [Header("References")]
    [SerializeField] Transform cameraPivot; // assign CameraPivot child (pitch)
    [SerializeField] Camera playerCamera;   // assign child camera (optional)

    [Header("UI")]
    [SerializeField] TMP_Text speedText;
    [SerializeField] TMP_Text groundedText;
    [SerializeField] TMP_Text debugText;
    public Canvas pauseMenu;
    public TMP_InputField joinCode;

    [Header("Net Input Rate")]
    [Tooltip("How often to send input to the server (Hz). 30 is plenty.")]
    [SerializeField] float inputSendRateHz = 30f;

    CharacterController controller;

    // Server simulation state
    Vector3 velocity;
    float lastGroundedTime;
    float lastJumpPressedTime;

    // Owner-only camera pitch
    float xRotation;

    // Input cache (received on server)
    Vector2 moveInput;
    bool jumpPressed;
    bool sprintHeld;
    bool isPaused;
    float yawDeltaAccum;

    // Owner-side send throttle
    float nextSendTime;

    void Awake()
    {
        isPaused = false;
        controller = GetComponent<CharacterController>();

        if (cameraPivot == null)
            cameraPivot = transform.Find("CameraPivot");

        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>(true);
    }

    public override void OnNetworkSpawn()
    {
        // Only the owning client keeps the camera active
        if (!IsOwner)
        {
            if (playerCamera != null) playerCamera.gameObject.SetActive(false);
            return;
        }

        // Owner UI is OK to keep enabled; if you have world-space UI disable for non-owner elsewhere.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (!IsOwner) return;

        // Cursor behaviors (match what you had)
        if (Input.GetButton("Fire1"))
            Cursor.lockState = CursorLockMode.Locked;

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (!isPaused)
            {
                pauseMenu.enabled = true;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                isPaused = true;
            }
            else
            {
                pauseMenu.enabled = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                isPaused = false;
            }
            
        }
        

        // ----- Local camera pitch (instant) -----
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity * 100f * Time.deltaTime;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity * 100f * Time.deltaTime;

        xRotation -= mouseY;
        xRotation = Mathf.Clamp(xRotation, -maxLookAngle, maxLookAngle);
        if (cameraPivot) cameraPivot.localRotation = Quaternion.Euler(xRotation, 0f, 0f);

        // ----- Gather input for server -----
        Vector2 move = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical")
        );

        bool jumpDown = Input.GetButtonDown("Jump");
        bool sprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        // Throttle sending to reduce spam
        if (Time.unscaledTime >= nextSendTime)
        {
            nextSendTime = Time.unscaledTime + (1f / Mathf.Max(1f, inputSendRateHz));
            SendInputServerRpc(move, jumpDown, sprint, mouseX);
        }

        // ----- Optional UI (owner only) -----
        // We can’t read CharacterController velocity reliably on the client in server-auth mode,
        // so this is “best effort” UI based on input and grounded state.
        if (speedText) speedText.text = sprint ? $"Speed: {runSpeed:0.0}" : $"Speed: {walkSpeed:0.0}";
        if (groundedText) groundedText.text = controller.isGrounded ? "Grounded" : "Airborne";
        if (debugText) debugText.text = $"SendHz: {inputSendRateHz:0}";
    }

    void FixedUpdate()
    {
        if (!IsServer) return;

        // Apply yaw on server so everyone sees correct facing
        if (Mathf.Abs(yawDeltaAccum) > 0.0001f)
        {
            transform.Rotate(Vector3.up * yawDeltaAccum);
            yawDeltaAccum = 0f;
        }

        // Determine target speed
        float speed = sprintHeld ? runSpeed : walkSpeed;

        // World move direction (server authoritative)
        Vector3 moveWorld = (transform.right * moveInput.x + transform.forward * moveInput.y);
        moveWorld = Vector3.ClampMagnitude(moveWorld, 1f);

        // Reduce control in air if desired
        float control = controller.isGrounded ? 1f : airControlMultiplier;

        controller.Move(moveWorld * (speed * control) * Time.fixedDeltaTime);

        // Track grounded time / stick
        if (controller.isGrounded)
        {
            lastGroundedTime = Time.time;
            if (velocity.y < 0f)
                velocity.y = groundStickForce;
        }

        // Buffer jump press (received as a pulse)
        if (jumpPressed)
        {
            lastJumpPressedTime = Time.time;
            jumpPressed = false;
        }

        // Jump conditions (buffer + coyote)
        if (Time.time - lastJumpPressedTime <= jumpBufferTime &&
            Time.time - lastGroundedTime <= coyoteTime)
        {
            velocity.y = jumpForce;
            lastJumpPressedTime = -999f;
            lastGroundedTime = -999f;
        }

        // Gravity
        velocity.y += gravity * Time.fixedDeltaTime;

        // Apply vertical movement
        controller.Move(velocity * Time.fixedDeltaTime);
    }

    [ServerRpc]
    void SendInputServerRpc(Vector2 move, bool jumpDown, bool sprint, float yawDelta)
    {
        moveInput = move;
        sprintHeld = sprint;

        if (jumpDown) jumpPressed = true;

        // Accumulate yaw in case multiple RPCs arrive between FixedUpdates
        yawDeltaAccum += yawDelta;
    }
}
