using System;
using System.Collections.Generic;
using SliceIt.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SliceIt.Combat
{
    /// <summary>
    /// Records arbitrary 2D mouse slash gestures while LMB is held.
    /// Freezes camera look rotation while keeping locomotion active.
    /// Cancels or dispatches slash on LMB release or timeout (default 1.0s).
    /// </summary>
    public class BladeInputRecorder : MonoBehaviour
    {
        [Header("Timing & Sensitivity")]
        [Tooltip("Maximum allowed duration for drawing a single blade slash.")]
        [SerializeField] private float maxRecordDuration = 1.0f;

        [Tooltip("Minimum pixel distance between recorded samples to avoid redundant data.")]
        [SerializeField] private float minPixelStep = 8.0f;

        public event Action<IReadOnlyList<TrajectoryPoint2D>> OnSlashRecorded;
        public event Action OnRecordingStarted;
        public event Action OnRecordingCancelled;

        public bool IsRecording { get; private set; } = false;
        public float RecordProgress => IsRecording ? Mathf.Clamp01((Time.time - recordStartTime) / maxRecordDuration) : 0f;
        public IReadOnlyList<TrajectoryPoint2D> RecordedPoints => points;
        public Vector2 VirtualCursorPosition => virtualCursor;

        private PlayerController playerController;
        private readonly List<TrajectoryPoint2D> points = new List<TrajectoryPoint2D>(64);
        private float recordStartTime;
        private Vector2 virtualCursor;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
        }

        private void Update()
        {
            if (playerController != null && !playerController.IsOwner) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            if (!IsRecording)
            {
                // Can only initiate attack when grounded and LMB pressed
                if (mouse.leftButton.wasPressedThisFrame)
                {
                    if (playerController != null && !playerController.IsGrounded)
                    {
                        // Cannot attack while in mid-air (jumping/falling)
                        return;
                    }

                    StartRecording();
                }
            }
            else
            {
                UpdateRecording(mouse);

                // Finish on LMB release or timeout
                bool released = !mouse.leftButton.isPressed;
                bool timedOut = (Time.time - recordStartTime) >= maxRecordDuration;

                if (released || timedOut)
                {
                    FinishRecording();
                }
            }
        }

        private void StartRecording()
        {
            IsRecording = true;
            recordStartTime = Time.time;
            points.Clear();

            // Center virtual cursor on screen
            virtualCursor = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            points.Add(new TrajectoryPoint2D(virtualCursor, Time.time));

            // Lock camera look so mouse movement traces blade path
            if (playerController != null)
            {
                playerController.SetCameraLookLocked(true);
            }

            OnRecordingStarted?.Invoke();
        }

        private void UpdateRecording(Mouse mouse)
        {
            Vector2 delta = mouse.delta.ReadValue();
            virtualCursor += delta;
            virtualCursor.x = Mathf.Clamp(virtualCursor.x, 0f, Screen.width);
            virtualCursor.y = Mathf.Clamp(virtualCursor.y, 0f, Screen.height);

            // Record point if moved enough from previous
            if (points.Count > 0)
            {
                Vector2 lastPos = points[points.Count - 1].ScreenPosition;
                if (Vector2.Distance(lastPos, virtualCursor) >= minPixelStep)
                {
                    points.Add(new TrajectoryPoint2D(virtualCursor, Time.time));
                }
            }
        }

        private void FinishRecording()
        {
            IsRecording = false;

            // Unlock camera look
            if (playerController != null)
            {
                playerController.SetCameraLookLocked(false);
            }

            if (points.Count >= 2)
            {
                OnSlashRecorded?.Invoke(points);
            }
            else
            {
                OnRecordingCancelled?.Invoke();
            }
        }

        private void OnDisable()
        {
            if (IsRecording)
            {
                FinishRecording();
            }
        }
    }
}
