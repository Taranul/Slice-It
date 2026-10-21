using SliceIt.Buffs;
using SliceIt.Networking;
using SliceIt.Player;
using SliceIt.Progression;
using Unity.Netcode;
using UnityEngine;

namespace SliceIt.UI
{
    /// <summary>
    /// Comprehensive Match HUD displaying player health, abilities cooldowns (Dash, Grapple, Block),
    /// 15-second revive countdowns, pair progression status, and Victory/Defeat overlays.
    /// </summary>
    public class MatchHUD : MonoBehaviour
    {
        private PlayerController playerController;
        private PlayerHealth playerHealth;
        private PlayerAbilities playerAbilities;
        private InRunBuffManager buffManager;
        private Texture2D whiteTex;

        private GUIStyle boldStyle;
        private GUIStyle titleStyle;
        private GUIStyle alertStyle;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            playerHealth = GetComponent<PlayerHealth>();
            playerAbilities = GetComponent<PlayerAbilities>();
            buffManager = GetComponent<InRunBuffManager>();

            whiteTex = new Texture2D(1, 1);
            whiteTex.SetPixel(0, 0, Color.white);
            whiteTex.Apply();
        }

        private void OnDestroy()
        {
            if (whiteTex != null) Destroy(whiteTex);
        }

        private void OnGUI()
        {
            if (playerController != null && !playerController.IsOwner) return;

            InitStyles();

            DrawHealthBar();
            DrawAbilitiesPanel();
            DrawProgressionTracker();
            DrawDownedReviveOverlay();
            DrawMatchResultOverlay();
        }

        private void InitStyles()
        {
            if (boldStyle == null)
            {
                boldStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Bold
                };
            }

            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 28,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            if (alertStyle == null)
            {
                alertStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 20,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }
        }

        private void DrawHealthBar()
        {
            if (playerHealth == null) return;

            float hp = playerHealth.CurrentHealth.Value;
            float maxHp = playerHealth.MaxHealth;
            float progress = Mathf.Clamp01(hp / maxHp);

            float width = 240f;
            float height = 24f;
            float x = 25f;
            float y = Screen.height - 50f;

            // Background
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.85f);
            GUI.DrawTexture(new Rect(x, y, width, height), whiteTex);

            // Health Fill
            Color hpColor = progress > 0.5f ? Color.green : (progress > 0.25f ? Color.yellow : Color.red);
            GUI.color = hpColor;
            GUI.DrawTexture(new Rect(x + 2, y + 2, (width - 4) * progress, height - 4), whiteTex);

            // Text
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 10, y + 2, width, height), $"HP: {hp:F0} / {maxHp:F0}", boldStyle);

            if (playerHealth.IsInvincible.Value)
            {
                GUI.color = Color.yellow;
                GUI.Label(new Rect(x + width + 12, y + 2, 140, height), "[INVINCIBLE]", boldStyle);
                GUI.color = Color.white;
            }
        }

        private void DrawAbilitiesPanel()
        {
            if (playerAbilities == null) return;

            float x = 280f;
            float y = Screen.height - 50f;

            string dashText = playerAbilities.DashCooldownRemaining > 0f
                ? $"[Q] Dash: {playerAbilities.DashCooldownRemaining:F1}s"
                : "[Q] Dash: READY";

            string grappleText = playerAbilities.GrappleCooldownRemaining > 0f
                ? $"[E] Hook: {playerAbilities.GrappleCooldownRemaining:F1}s"
                : "[E] Hook: READY";

            string blockText = playerAbilities.IsBlocking ? "[RMB] BLOCKING" : "[RMB] Block: Hold";

            string buffText = buffManager != null && buffManager.ActiveBuff != InRunBuffType.None
                ? $"Buff: {buffManager.ActiveBuff}"
                : "[B] Select Buff";

            GUI.color = new Color(0.15f, 0.15f, 0.15f, 0.8f);
            GUI.DrawTexture(new Rect(x, y, 480f, 26f), whiteTex);

            GUI.color = Color.white;
            GUI.Label(new Rect(x + 10, y + 3, 120f, 20f), dashText, boldStyle);
            GUI.Label(new Rect(x + 130, y + 3, 120f, 20f), grappleText, boldStyle);
            GUI.Label(new Rect(x + 250, y + 3, 110f, 20f), blockText, boldStyle);
            GUI.Label(new Rect(x + 365, y + 3, 110f, 20f), buffText, boldStyle);
        }

        private void DrawProgressionTracker()
        {
            if (ArenaProgressionManager.Instance == null) return;

            int remaining = ArenaProgressionManager.Instance.EnemiesRemaining.Value;
            int rank = ArenaProgressionManager.Instance.HighestRankActive.Value;

            float width = 210f;
            float height = 55f;
            float x = Screen.width - width - 20f;
            float y = 20f;

            GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            GUI.DrawTexture(new Rect(x, y, width, height), whiteTex);

            GUI.color = Color.cyan;
            GUI.Label(new Rect(x + 10, y + 6, width - 20, 20), $"ENEMIES: {remaining} / 32", boldStyle);
            GUI.color = Color.yellow;
            GUI.Label(new Rect(x + 10, y + 28, width - 20, 20), $"HIGHEST RANK: {rank} / 7", boldStyle);
            GUI.color = Color.white;
        }

        private void DrawDownedReviveOverlay()
        {
            if (playerHealth == null) return;

            if (playerHealth.IsDowned.Value)
            {
                float remaining = playerHealth.ReviveTimer.Value;
                float boxWidth = 500f;
                float boxHeight = 90f;
                float cx = (Screen.width - boxWidth) * 0.5f;
                float cy = (Screen.height - boxHeight) * 0.5f;

                GUI.color = new Color(0.8f, 0.1f, 0.1f, 0.85f);
                GUI.DrawTexture(new Rect(cx, cy, boxWidth, boxHeight), whiteTex);

                GUI.color = Color.white;
                GUI.Label(new Rect(cx, cy + 12, boxWidth, 32), "YOU ARE DOWNED!", alertStyle);
                GUI.Label(new Rect(cx, cy + 45, boxWidth, 26), $"Reviving in: {remaining:F1}s (Teammate must survive!)", boldStyle);
            }
        }

        private void DrawMatchResultOverlay()
        {
            bool victory = ArenaProgressionManager.Instance != null && ArenaProgressionManager.Instance.IsVictory.Value;
            bool isDefeated = (ArenaProgressionManager.Instance != null && ArenaProgressionManager.Instance.IsDefeat.Value)
                           || (playerHealth != null && playerHealth.IsTeamDefeated.Value);

            if (victory)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                float w = 550f;
                float h = 180f;
                float x = (Screen.width - w) * 0.5f;
                float y = (Screen.height - h) * 0.5f;

                GUI.color = new Color(0.05f, 0.5f, 0.15f, 0.95f);
                GUI.DrawTexture(new Rect(x, y, w, h), whiteTex);

                GUI.color = Color.white;
                GUI.Label(new Rect(x, y + 25, w, 40), "VICTORY!", titleStyle);
                GUI.Label(new Rect(x, y + 75, w, 30), "Final Boss Defeated! Match Won!", boldStyle);

                if (GUI.Button(new Rect(x + 175, y + 120, 200, 38), "Leave to Main Menu"))
                {
                    NetworkSessionManager.Instance?.DisconnectSession();
                }
            }
            else if (isDefeated)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                float w = 550f;
                float h = 210f;
                float x = (Screen.width - w) * 0.5f;
                float y = (Screen.height - h) * 0.5f;

                GUI.color = new Color(0.12f, 0.02f, 0.02f, 0.95f);
                GUI.DrawTexture(new Rect(x, y, w, h), whiteTex);

                GUI.color = new Color(1f, 0.25f, 0.25f);
                GUI.Label(new Rect(x, y + 20, w, 40), "TEAM DEFEATED", titleStyle);

                GUI.color = Color.white;
                GUI.Label(new Rect(x, y + 68, w, 30), "Both players have fallen in battle.", boldStyle);

                // Restart Level button
                GUI.color = new Color(0.2f, 0.85f, 0.35f);
                if (GUI.Button(new Rect(x + 80, y + 120, 180, 44), "Restart Level / Respawn"))
                {
                    playerHealth.RequestRestartServerRpc();
                }

                // Leave to Menu button
                GUI.color = new Color(0.85f, 0.3f, 0.3f);
                if (GUI.Button(new Rect(x + 290, y + 120, 180, 44), "Leave to Menu"))
                {
                    NetworkSessionManager.Instance?.DisconnectSession();
                }
                GUI.color = Color.white;
            }
        }
    }
}
