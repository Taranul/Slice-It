using SliceIt.Combat;
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
        private PlayerHealth playerHealth;
        private PlayerAbilities playerAbilities;
        private BladeSlashExecutor bladeExecutor;
        private Animator remoteAnimator;
        private Transform localSwordTransform;
        private Quaternion swordInitialLocalRot;
        private float swordSwingTimer = 0f;
        private Vector3 lastPlayerPosition;

        private float cameraPitch = 0f;
        private float currentYaw = 0f;
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
            playerHealth = GetComponent<PlayerHealth>();
            playerAbilities = GetComponent<PlayerAbilities>();
            bladeExecutor = GetComponent<BladeSlashExecutor>();

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

            if (remoteAvatarRoot == null)
            {
                var rAvatar = transform.Find("RemoteAvatar");
                if (rAvatar != null) remoteAvatarRoot = rAvatar.gameObject;
            }

            if (remoteAvatarRoot != null)
            {
                remoteAnimator = remoteAvatarRoot.GetComponentInChildren<Animator>(true);
            }

            if (localAvatarRoot == null)
            {
                var lAvatar = transform.Find("CameraHolder/LocalAvatar");
                if (lAvatar != null) localAvatarRoot = lAvatar.gameObject;
            }

            if (localAvatarRoot != null)
            {
                var sword = localAvatarRoot.transform.Find("LocalSword");
                if (sword != null)
                {
                    localSwordTransform = sword;
                    swordInitialLocalRot = sword.localRotation;
                }
            }

            lastPlayerPosition = transform.position;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            lastPlayerPosition = transform.position;

            if (IsOwner)
            {
                if (bladeExecutor != null) bladeExecutor.OnSlashExecuted += HandleLocalSlashExecuted;
                if (playerAbilities != null) playerAbilities.OnDashTriggered += HandleLocalDashTriggered;

                // Position owner at designated starting spawn point (strictly horizontal, no pitch/roll)
                Vector3 spawnPos = IsServer ? new Vector3(-3f, 1f, 0f) : new Vector3(3f, 1f, 0f);
                Vector3 lookDir = Vector3.zero - spawnPos;
                lookDir.y = 0f;
                Quaternion spawnRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);

                currentYaw = spawnRot.eulerAngles.y;

                if (characterController != null) characterController.enabled = false;
                transform.position = spawnPos;
                transform.rotation = Quaternion.Euler(0f, currentYaw, 0f);
                if (characterController != null) characterController.enabled = true;

                // Local player setup
                if (cameraHolder != null)
                {
                    cameraHolder.gameObject.SetActive(true);
                    cameraHolder.localRotation = Quaternion.identity;
                }
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
                if (bladeExecutor != null) bladeExecutor.OnSlashExecuted -= HandleLocalSlashExecuted;
                if (playerAbilities != null) playerAbilities.OnDashTriggered -= HandleLocalDashTriggered;

                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void Update()
        {
            UpdateAnimationState();

            if (!IsOwner) return;

            HandleCursorToggle();
            HandleLook();

            // Downed players can ONLY look around with camera (locomotion blocked)
            if (playerHealth != null && playerHealth.IsDowned.Value) return;

            HandleLocomotion();
        }

        private void UpdateAnimationState()
        {
            // Sync remote avatar locomotion & states
            if (remoteAvatarRoot != null && remoteAvatarRoot.activeInHierarchy)
            {
                float dist = Vector3.Distance(transform.position, lastPlayerPosition);
                float speed = dist / Mathf.Max(0.0001f, Time.deltaTime);
                lastPlayerPosition = transform.position;

                if (remoteAnimator != null)
                {
                    remoteAnimator.SetFloat("Speed", speed);
                    if (playerHealth != null)
                    {
                        remoteAnimator.SetBool("IsDowned", playerHealth.IsDowned.Value);
                    }
                    if (playerAbilities != null)
                    {
                        remoteAnimator.SetBool("IsBlocking", playerAbilities.IsBlocking);
                    }
                }
            }
            else
            {
                lastPlayerPosition = transform.position;
            }

            // Local sword swing visual feedback
            if (IsOwner && localSwordTransform != null && swordSwingTimer > 0f)
            {
                swordSwingTimer -= Time.deltaTime;
                float progress = 1f - (swordSwingTimer / 0.22f);
                float angle = Mathf.Sin(progress * Mathf.PI) * 40f;
                localSwordTransform.localRotation = swordInitialLocalRot * Quaternion.Euler(angle, -angle * 0.4f, 0f);
                if (swordSwingTimer <= 0f)
                {
                    localSwordTransform.localRotation = swordInitialLocalRot;
                }
            }
        }

        private void HandleLocalSlashExecuted(Vector3 start, Vector3 end)
        {
            swordSwingTimer = 0.22f;
            NotifyPlayerAttackServerRpc();
        }

        private void HandleLocalDashTriggered()
        {
            NotifyPlayerDashServerRpc();
        }

        public void TriggerHitAnimation()
        {
            if (remoteAnimator != null)
            {
                remoteAnimator.SetTrigger("Hit");
            }
        }

        [Rpc(SendTo.Server)]
        private void NotifyPlayerAttackServerRpc()
        {
            NotifyPlayerAttackClientRpc();
        }

        [Rpc(SendTo.NotServer)]
        private void NotifyPlayerAttackClientRpc()
        {
            if (remoteAnimator != null)
            {
                remoteAnimator.SetTrigger("Attack");
            }
        }

        [Rpc(SendTo.Server)]
        private void NotifyPlayerDashServerRpc()
        {
            NotifyPlayerDashClientRpc();
        }

        [Rpc(SendTo.NotServer)]
        private void NotifyPlayerDashClientRpc()
        {
            if (remoteAnimator != null)
            {
                remoteAnimator.SetTrigger("Dash");
            }
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

            // Strictly horizontal yaw (no body tilt/roll)
            currentYaw += mouseDelta.x;
            transform.rotation = Quaternion.Euler(0f, currentYaw, 0f);

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
            bool grounded = CheckGrounded();
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

            // Combine with active ability movement (Dash / Grapple)
            Vector3 abilityMove = Vector3.zero;
            if (playerAbilities != null)
            {
                abilityMove = playerAbilities.GetActiveMovementVelocity();
            }

            // Apply unified movement
            Vector3 finalVelocity = horizontalMove + verticalVelocity + abilityMove;
            characterController.Move(finalVelocity * Time.deltaTime);
        }
    }
}
