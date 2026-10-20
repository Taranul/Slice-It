using System.Collections.Generic;
using SliceIt.Progression;
using UnityEngine;

namespace SliceIt.Combat
{
    /// <summary>
    /// Immediate visual feedback for blade combat:
    /// - Draws the 2D slash path on screen during LMB hold.
    /// - Renders crosshair and execution indicators.
    /// - Displays the 10-second active pair timer bar on screen.
    /// </summary>
    public class BladeVisualFeedback : MonoBehaviour
    {
        [Header("Blade Path Visuals")]
        [SerializeField] private Color pathColor = new Color(0.2f, 0.9f, 1f, 0.85f);
        [SerializeField] private float lineWidth = 4.0f;

        private BladeInputRecorder recorder;
        private Texture2D lineTex;
        private GUIStyle timerStyle;

        private void Awake()
        {
            recorder = GetComponent<BladeInputRecorder>();

            // Create 1x1 white texture for drawing smooth GUI lines
            lineTex = new Texture2D(1, 1);
            lineTex.SetPixel(0, 0, Color.white);
            lineTex.Apply();
        }

        private void OnDestroy()
        {
            if (lineTex != null)
            {
                Destroy(lineTex);
            }
        }

        private void OnGUI()
        {
            DrawCrosshair();
            DrawActiveSlashGesture();
            DrawPairWindowTimer();
        }

        private void DrawCrosshair()
        {
            if (recorder != null && recorder.IsRecording) return;

            float size = 8f;
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;

            GUI.color = new Color(1f, 1f, 1f, 0.6f);
            GUI.DrawTexture(new Rect(cx - size * 0.5f, cy - 1f, size, 2f), lineTex);
            GUI.DrawTexture(new Rect(cx - 1f, cy - size * 0.5f, 2f, size), lineTex);
            GUI.color = Color.white;
        }

        private void DrawActiveSlashGesture()
        {
            if (recorder == null || !recorder.IsRecording) return;

            var points = recorder.RecordedPoints;
            if (points == null || points.Count < 2) return;

            // Draw line segments between consecutive recorded points
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 p1 = points[i].ScreenPosition;
                Vector2 p2 = points[i + 1].ScreenPosition;

                // Unity GUI Y is inverted relative to standard screen coords
                p1.y = Screen.height - p1.y;
                p2.y = Screen.height - p2.y;

                DrawLine(p1, p2, pathColor, lineWidth);
            }

            // Draw current blade pointer circle/dot
            Vector2 cursor = recorder.VirtualCursorPosition;
            cursor.y = Screen.height - cursor.y;
            float radius = 6f;
            GUI.color = Color.cyan;
            GUI.DrawTexture(new Rect(cursor.x - radius, cursor.y - radius, radius * 2, radius * 2), lineTex);
            GUI.color = Color.white;
        }

        private void DrawPairWindowTimer()
        {
            if (PairManager.Instance == null) return;

            float remaining = PairManager.Instance.PairWindowRemaining.Value;
            int pairId = PairManager.Instance.ActivePairId.Value;

            if (remaining > 0f && pairId > 0)
            {
                float total = PairManager.Instance.WindowDuration;
                float progress = Mathf.Clamp01(remaining / total);

                float barWidth = 320f;
                float barHeight = 22f;
                float barX = (Screen.width - barWidth) * 0.5f;
                float barY = 40f;

                // Background
                GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
                GUI.DrawTexture(new Rect(barX, barY, barWidth, barHeight), lineTex);

                // Progress Fill: Green -> Yellow -> Red as time runs out
                Color barCol = Color.Lerp(Color.red, Color.green, progress);
                GUI.color = barCol;
                GUI.DrawTexture(new Rect(barX + 2, barY + 2, (barWidth - 4) * progress, barHeight - 4), lineTex);

                // Label
                GUI.color = Color.white;
                if (timerStyle == null)
                {
                    timerStyle = new GUIStyle(GUI.skin.label)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        fontSize = 13,
                        fontStyle = FontStyle.Bold
                    };
                }

                GUI.Label(new Rect(barX, barY, barWidth, barHeight), $"PAIR {pairId} WINDOW: {remaining:F1}s", timerStyle);
            }
        }

        private void DrawLine(Vector2 pointA, Vector2 pointB, Color color, float width)
        {
            Matrix4x4 savedMatrix = GUI.matrix;
            Color savedColor = GUI.color;

            GUI.color = color;
            float angle = Vector2.SignedAngle(Vector2.right, pointB - pointA);
            float length = Vector2.Distance(pointA, pointB);

            GUIUtility.RotateAroundPivot(angle, pointA);
            GUI.DrawTexture(new Rect(pointA.x, pointA.y - width * 0.5f, length, width), lineTex);

            GUI.matrix = savedMatrix;
            GUI.color = savedColor;
        }
    }
}
