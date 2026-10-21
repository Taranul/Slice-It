using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SliceIt.Player
{
    /// <summary>
    /// Handles the player mobility and defense kit:
    /// - Dash (Q / LeftCtrl): Directional impulse with cooldown.
    /// - Grappling Hook (E): Shoots cable, pulls player toward target geometry.
    /// - Block (Hold RMB): Raises shield, negates frontal damage.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerAbilities : NetworkBehaviour
    {
        [Header("Dash Settings")]
        [SerializeField] private float dashSpeed = 22f;
        [SerializeField] private float dashDuration = 0.22f;
        [SerializeField] private float dashCooldown = 2.5f;

        [Header("Grappling Hook Settings")]
        [SerializeField] private float grappleMaxDistance = 35f;
        [SerializeField] private float grapplePullSpeed = 24f;
        [SerializeField] private float grappleCooldown = 3.5f;
        [SerializeField] private LineRenderer ropeRenderer;

        [Header("Block Settings")]
        [SerializeField] private Transform shieldVisual;
        [SerializeField] private Vector3 shieldRestPos = new Vector3(-0.35f, -0.4f, 0.45f);
        [SerializeField] private Vector3 shieldBlockPos = new Vector3(0f, -0.1f, 0.5f);

        // State
        public bool IsDashing { get; private set; } = false;
        public bool IsGrappling { get; private set; } = false;
        public bool IsBlocking { get; private set; } = false;

        public float DashCooldownRemaining => Mathf.Max(0f, dashCooldownEndTime - Time.time);
        public float GrappleCooldownRemaining => Mathf.Max(0f, grappleCooldownEndTime - Time.time);

        public event Action OnDashTriggered;
        public event Action<Vector3> OnGrappleStarted;
        public event Action OnGrappleEnded;

        private PlayerController playerController;
        private CharacterController characterController;
        private PlayerHealth playerHealth;

        private float dashEndTime;
        private float dashCooldownEndTime;
        private Vector3 dashDirection;

        private Vector3 grapplePoint;
        private float grappleCooldownEndTime;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            characterController = GetComponent<CharacterController>();
            playerHealth = GetComponent<PlayerHealth>();

            if (ropeRenderer == null)
            {
                ropeRenderer = GetComponent<LineRenderer>();
                if (ropeRenderer == null)
                {
                    ropeRenderer = gameObject.AddComponent<LineRenderer>();
                    ropeRenderer.startWidth = 0.04f;
                    ropeRenderer.endWidth = 0.04f;
                    ropeRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    ropeRenderer.material.color = new Color(0.9f, 0.85f, 0.6f);
                    ropeRenderer.enabled = false;
                }
            }
        }

        private void Update()
        {
            if (!IsOwner) return;
            if (playerHealth != null && !playerHealth.IsAlive)
            {
                CancelAllAbilities();
                return;
            }

            HandleBlock();
            HandleDash();
            HandleGrapple();
        }

        public Vector3 GetActiveMovementVelocity()
        {
            if (IsDashing)
            {
                return dashDirection * dashSpeed;
            }
            if (IsGrappling)
            {
                Vector3 toAnchor = grapplePoint - transform.position;
                return toAnchor.normalized * grapplePullSpeed;
            }
            return Vector3.zero;
        }

        #region Block

        private void HandleBlock()
        {
            var mouse = Mouse.current;
            bool shouldBlock = false;
            if (mouse != null)
            {
                shouldBlock = mouse.rightButton.isPressed;
            }
            if (!shouldBlock)
            {
                shouldBlock = Input.GetMouseButton(1);
            }

            shouldBlock = shouldBlock && !IsDashing && !IsGrappling;

            if (shouldBlock != IsBlocking)
            {
                IsBlocking = shouldBlock;
                UpdateShieldVisual(IsBlocking);
            }
        }

        private void UpdateShieldVisual(bool blocking)
        {
            if (shieldVisual != null)
            {
                shieldVisual.localPosition = blocking ? shieldBlockPos : shieldRestPos;
                shieldVisual.localRotation = blocking ? Quaternion.Euler(-10f, 0f, 0f) : Quaternion.Euler(0f, -25f, 0f);
            }
        }

        #endregion

        #region Dash

        private void HandleDash()
        {
            var kb = Keyboard.current;
            bool dashPressed = false;
            if (kb != null)
            {
                dashPressed = kb.qKey.wasPressedThisFrame || kb.leftCtrlKey.wasPressedThisFrame;
            }
            if (!dashPressed)
            {
                dashPressed = Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.LeftControl);
            }

            if (dashPressed && !IsDashing && !IsGrappling && Time.time >= dashCooldownEndTime)
            {
                StartDash(kb);
            }

            if (IsDashing && Time.time >= dashEndTime)
            {
                IsDashing = false;
            }
        }

        private void StartDash(Keyboard kb)
        {
            Vector2 input = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed) input.y += 1f;
                if (kb.sKey.isPressed) input.y -= 1f;
                if (kb.aKey.isPressed) input.x -= 1f;
                if (kb.dKey.isPressed) input.x += 1f;
            }

            if (input.sqrMagnitude < 0.01f)
            {
                input.x = Input.GetAxisRaw("Horizontal");
                input.y = Input.GetAxisRaw("Vertical");
            }

            if (input.sqrMagnitude > 0.01f)
            {
                dashDirection = (transform.forward * input.y + transform.right * input.x).normalized;
            }
            else
            {
                dashDirection = transform.forward;
            }

            IsDashing = true;
            dashEndTime = Time.time + dashDuration;
            dashCooldownEndTime = Time.time + dashCooldown;

            Debug.Log("[PlayerAbilities] Dash activated!");
            OnDashTriggered?.Invoke();
        }

        #endregion

        #region Grappling Hook

        private void HandleGrapple()
        {
            var kb = Keyboard.current;
            bool grapplePressed = (kb != null && kb.eKey.wasPressedThisFrame) || Input.GetKeyDown(KeyCode.E);

            if (grapplePressed && !IsGrappling && !IsDashing && Time.time >= grappleCooldownEndTime)
            {
                TryShootGrapple();
            }

            if (IsGrappling)
            {
                UpdateGrappleMovement();
            }
        }

        private void TryShootGrapple()
        {
            if (playerController == null || playerController.PlayerCamera == null) return;

            Ray ray = playerController.PlayerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 startPos = ray.origin + ray.direction * 0.7f;

            // Use RaycastAll to filter out player's own colliders
            RaycastHit[] hits = Physics.RaycastAll(startPos, ray.direction, grappleMaxDistance, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                var hit = hits[i];
                if (hit.collider.transform.root == transform.root) continue; // Skip self colliders

                // Anchor to geometry
                grapplePoint = hit.point;
                IsGrappling = true;
                grappleCooldownEndTime = Time.time + grappleCooldown;

                if (ropeRenderer != null)
                {
                    ropeRenderer.enabled = true;
                    ropeRenderer.SetPosition(0, transform.position + Vector3.up * 0.8f);
                    ropeRenderer.SetPosition(1, grapplePoint);
                }

                Debug.Log($"[PlayerAbilities] Grappling hook attached at {grapplePoint} on {hit.collider.gameObject.name}!");
                OnGrappleStarted?.Invoke(grapplePoint);
                break;
            }
        }

        private void UpdateGrappleMovement()
        {
            Vector3 toAnchor = grapplePoint - transform.position;
            float distance = toAnchor.magnitude;

            var kb = Keyboard.current;
            bool jumpPressed = (kb != null && kb.spaceKey.wasPressedThisFrame) || Input.GetKeyDown(KeyCode.Space);

            // Detach if reached anchor or pressed Jump
            if (distance < 2.2f || jumpPressed)
            {
                EndGrapple();
                return;
            }

            if (ropeRenderer != null && ropeRenderer.enabled)
            {
                ropeRenderer.SetPosition(0, transform.position + Vector3.up * 0.8f);
                ropeRenderer.SetPosition(1, grapplePoint);
            }
        }

        private void EndGrapple()
        {
            IsGrappling = false;
            if (ropeRenderer != null)
            {
                ropeRenderer.enabled = false;
            }
            OnGrappleEnded?.Invoke();
        }

        private void CancelAllAbilities()
        {
            IsDashing = false;
            IsBlocking = false;
            if (IsGrappling) EndGrapple();
            UpdateShieldVisual(false);
        }

        #endregion
    }
}
