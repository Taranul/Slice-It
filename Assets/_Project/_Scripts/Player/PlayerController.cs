using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SliceIt.Player
{
    /// <summary>
    /// Network-synchronized first-person controller with decoupled camera locking for blade recording.
    /// Handles ownership, local camera activation, and remote avatar visibility.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerController : NetworkBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float walkSpeed = 5.5f;
        [SerializeField] private float sprintSpeed = 9.0f;
        [SerializeField] private float jumpHeight = 1.5f;
        [SerializeField] private float gravity = -22.0f;

        [Header("Look & Camera")]
        [SerializeField] private Transform cameraHolder;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private AudioListener audioListener;
        [SerializeField] private float mouseSensitivity = 0.15f;
        [SerializeField] private float pitchClamp = 85.0f;

        [Header("Visuals")]
        [SerializeField] private GameObject remoteAvatarRoot;
        [SerializeField] private GameObject localAvatarRoot;

        private CharacterController characterController;
        private float cameraPitch = 0f;
        private Vector3 verticalVelocity;
        private bool isCameraLookLocked = false;

        public bool IsCameraLookLocked => isCameraLookLocked;
        public bool IsGrounded => CheckGrounded();
        public Vector3 Velocity => characterController != null ? characterController.velocity : Vector3.zero;
        public Camera PlayerCamera => playerCamera;

        private bool CheckGrounded()
        {
            if (characterController != null && characterController.isGrounded) return true;
            // Raycast fallback to handle slight ground unevenness
            return Physics.Raycast(transform.position + Vector3.up * 0.15f, Vector3.down, 0.35f);
        }

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();

            if (cameraHolder == null)
            {
                var cam = GetComponentInChildren<Camera>(true);
                if (cam != null)
                {
                    cameraHolder = cam.transform;
                    playerCamera = cam;
                    audioListener = cam.GetComponent<AudioListener>();
                }
            }
            else if (playerCamera == null)
            {
                playerCamera = cameraHolder.GetComponentInChildren<Camera>(true);
                audioListener = cameraHolder.GetComponentInChildren<AudioListener>(true);
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            if (IsOwner)
            {
                // Position owner at designated starting spawn point
                Vector3 spawnPos = IsServer ? new Vector3(-3f, 1f, 0f) : new Vector3(3f, 1f, 0f);
                Quaternion spawnRot = Quaternion.LookRotation((Vector3.zero - spawnPos).normalized, Vector3.up);

                if (characterController != null) characterController.enabled = false;
                transform.position = spawnPos;
                transform.rotation = spawnRot;
                if (characterController != null) characterController.enabled = true;

                // Local player setup
                if (cameraHolder != null) cameraHolder.gameObject.SetActive(true);
                if (playerCamera != null) playerCamera.enabled = true;
                if (audioListener != null) audioListener.enabled = true;

                if (remoteAvatarRoot != null) remoteAvatarRoot.SetActive(false);
                if (localAvatarRoot != null) localAvatarRoot.SetActive(true);

                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            else
            {
                // Remote player setup: disable local camera, audio, and character controller
                if (characterController != null) characterController.enabled = false;
                if (cameraHolder != null) cameraHolder.gameObject.SetActive(false);
                if (playerCamera != null) playerCamera.enabled = false;
                if (audioListener != null) audioListener.enabled = false;

                if (remoteAvatarRoot != null) remoteAvatarRoot.SetActive(true);
                if (localAvatarRoot != null) localAvatarRoot.SetActive(false);
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();

            if (IsOwner)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void Update()
        {
            if (!IsOwner) return;

            HandleCursorToggle();
            HandleLook();
            HandleLocomotion();
        }

        /// <summary>
        /// Locks or unlocks camera pitch/yaw rotation. Used during blade trajectory drawing.
        /// </summary>
        public void SetCameraLookLocked(bool locked)
        {
            isCameraLookLocked = locked;
        }

        private void HandleCursorToggle()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
        }

        private void HandleLook()
        {
            if (isCameraLookLocked || Cursor.lockState != CursorLockMode.Locked) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 mouseDelta = mouse.delta.ReadValue() * mouseSensitivity;

            // Horizontal yaw rotates player body
            transform.Rotate(Vector3.up * mouseDelta.x);

            // Vertical pitch tilts camera up/down
            cameraPitch -= mouseDelta.y;
            cameraPitch = Mathf.Clamp(cameraPitch, -pitchClamp, pitchClamp);

            if (cameraHolder != null)
            {
                cameraHolder.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
            }
        }

        private void HandleLocomotion()
        {
            bool grounded = characterController.isGrounded;
            if (grounded && verticalVelocity.y < 0f)
            {
                verticalVelocity.y = -2f; // Slight downward force to stay glued to slopes
            }

            // Input reading via InputSystem
            var kb = Keyboard.current;
            Vector2 moveInput = Vector2.zero;
            bool isSprinting = false;
            bool jumpPressed = false;

            if (kb != null)
            {
                if (kb.wKey.isPressed) moveInput.y += 1f;
                if (kb.sKey.isPressed) moveInput.y -= 1f;
                if (kb.aKey.isPressed) moveInput.x -= 1f;
                if (kb.dKey.isPressed) moveInput.x += 1f;
                moveInput.Normalize();

                isSprinting = kb.leftShiftKey.isPressed;
                jumpPressed = kb.spaceKey.wasPressedThisFrame;
            }

            // Convert movement input to world space
            Vector3 moveDirection = (transform.forward * moveInput.y + transform.right * moveInput.x).normalized;
            float currentSpeed = isSprinting ? sprintSpeed : walkSpeed;
            Vector3 horizontalMove = moveDirection * currentSpeed;

            // Jump
            if (jumpPressed && grounded)
            {
                verticalVelocity.y = Mathf.Sqrt(jumpHeight * -2.0f * gravity);
            }

            // Gravity
            verticalVelocity.y += gravity * Time.deltaTime;

            // Apply movement
            Vector3 finalVelocity = horizontalMove + verticalVelocity;
            characterController.Move(finalVelocity * Time.deltaTime);
        }
    }
}
