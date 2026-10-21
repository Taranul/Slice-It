using System;
using Unity.Netcode;
using UnityEngine;

namespace SliceIt.Player
{
    /// <summary>
    /// Manages player health, shield damage mitigation, downed state,
    /// and the 15-second revive countdown per GDD specification.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerHealth : NetworkBehaviour
    {
        [Header("Health Settings")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float reviveDuration = 15.0f;
        [SerializeField] private float revivedHealth = 50f;

        public NetworkVariable<float> CurrentHealth { get; } = new NetworkVariable<float>(
            100f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<bool> IsDowned { get; } = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<float> ReviveTimer { get; } = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<bool> IsInvincible { get; } = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<bool> IsTeamDefeated { get; } = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public event Action<float, float> OnHealthChanged;
        public event Action<float> OnReviveTimerUpdated;
        public event Action OnPlayerDowned;
        public event Action OnPlayerRevived;
        public static event Action OnTeamDefeated;

        public float MaxHealth => maxHealth;
        public bool IsAlive => !IsDowned.Value && CurrentHealth.Value > 0f;

        private PlayerController playerController;
        private PlayerAbilities playerAbilities;
        private float invincibilityDuration = 2.0f;
        private float invincibilityEndTime = 0f;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            playerAbilities = GetComponent<PlayerAbilities>();
        }

        public override void OnNetworkSpawn()
        {
            CurrentHealth.OnValueChanged += HandleHealthChanged;
            IsDowned.OnValueChanged += HandleDownedChanged;
            ReviveTimer.OnValueChanged += HandleReviveTimerChanged;

            if (IsServer)
            {
                CurrentHealth.Value = maxHealth;
                IsDowned.Value = false;
                ReviveTimer.Value = 0f;
            }
        }

        public override void OnNetworkDespawn()
        {
            CurrentHealth.OnValueChanged -= HandleHealthChanged;
            IsDowned.OnValueChanged -= HandleDownedChanged;
            ReviveTimer.OnValueChanged -= HandleReviveTimerChanged;
        }

        private void Update()
        {
            if (!IsServer) return;

            // Handle post-respawn invincibility countdown
            if (IsInvincible.Value && Time.time >= invincibilityEndTime)
            {
                IsInvincible.Value = false;
                Debug.Log($"[PlayerHealth] Player {OwnerClientId} invincibility ended.");
            }

            if (IsDowned.Value)
            {
                ReviveTimer.Value -= Time.deltaTime;

                if (ReviveTimer.Value <= 0f)
                {
                    // Check if teammate is alive before reviving
                    if (IsTeammateAlive())
                    {
                        RevivePlayerServer();
                    }
                    else
                    {
                        TriggerTeamDefeatServer();
                    }
                }
            }
        }

        /// <summary>
        /// Applies damage to player with frontal shield block mitigation and invincibility check.
        /// </summary>
        public void TakeDamage(float amount, Vector3 damageSourcePosition)
        {
            if (!IsServer || IsDowned.Value || IsInvincible.Value) return;

            // Check if player is blocking and facing the attack
            if (playerAbilities != null && playerAbilities.IsBlocking)
            {
                Vector3 toSource = (damageSourcePosition - transform.position).normalized;
                toSource.y = 0f;
                float angle = Vector3.Angle(transform.forward, toSource);

                if (angle <= 65f) // Frontal 130-degree shield arc
                {
                    Debug.Log($"[PlayerHealth] Damage blocked by shield! (Angle: {angle:F0}°)");
                    NotifyDamageBlockedClientRpc();
                    return;
                }
            }

            CurrentHealth.Value = Mathf.Max(0f, CurrentHealth.Value - amount);
            NotifyDamageTakenClientRpc(amount, CurrentHealth.Value);

            if (CurrentHealth.Value <= 0f)
            {
                DownPlayerServer();
            }
        }

        private void DownPlayerServer()
        {
            IsDowned.Value = true;
            ReviveTimer.Value = reviveDuration;

            Debug.Log($"[PlayerHealth] Player {OwnerClientId} downed! 15s revive countdown started.");

            // Check if all players are now downed -> Team Defeat
            if (!IsTeammateAlive())
            {
                TriggerTeamDefeatServer();
            }
        }

        private void RevivePlayerServer()
        {
            IsDowned.Value = false;
            CurrentHealth.Value = revivedHealth;
            ReviveTimer.Value = 0f;

            // 2-second invincibility after respawn
            IsInvincible.Value = true;
            invincibilityEndTime = Time.time + invincibilityDuration;

            Debug.Log($"[PlayerHealth] Player {OwnerClientId} revived with {revivedHealth} HP! Invincible for 2s.");
            NotifyRevivedClientRpc();
        }

        private bool IsTeammateAlive()
        {
            if (NetworkManager.Singleton == null) return false;

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                if (client.ClientId == OwnerClientId) continue; // Skip self

                var health = client.PlayerObject.GetComponent<PlayerHealth>();
                if (health != null && health.IsAlive)
                {
                    return true;
                }
            }

            return false;
        }

        private void TriggerTeamDefeatServer()
        {
            Debug.LogWarning("[PlayerHealth] >>> TEAM DEFEATED! Both players are dead! <<<");

            if (SliceIt.Progression.ArenaProgressionManager.Instance != null)
            {
                SliceIt.Progression.ArenaProgressionManager.Instance.NotifyTeamDefeatedServer();
            }

            // Ensure IsTeamDefeated is set to true on all players in the session
            var allHealths = FindObjectsByType<PlayerHealth>(FindObjectsSortMode.None);
            for (int i = 0; i < allHealths.Length; i++)
            {
                if (allHealths[i] != null)
                {
                    allHealths[i].IsTeamDefeated.Value = true;
                }
            }

            NotifyTeamDefeatedClientRpc();
        }

        /// <summary>
        /// Restarts the level for all players upon clicking Restart on Death Screen or Pause Menu.
        /// </summary>
        [Rpc(SendTo.Server)]
        public void RequestRestartServerRpc()
        {
            if (!IsServer) return;

            Debug.Log("[PlayerHealth] Restarting level for all clients...");
            if (SliceIt.Progression.ArenaProgressionManager.Instance != null)
            {
                SliceIt.Progression.ArenaProgressionManager.Instance.RestartMatchServer();
            }
            else if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                NetworkManager.Singleton.SceneManager.LoadScene("SampleScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
            }
        }

        /// <summary>
        /// Server-authoritative reset of player state and teleportation to designated spawn.
        /// </summary>
        public void ResetPlayerToSpawnServer(ulong clientId)
        {
            if (!IsServer) return;

            CurrentHealth.Value = maxHealth;
            IsDowned.Value = false;
            IsTeamDefeated.Value = false;
            ReviveTimer.Value = 0f;
            IsInvincible.Value = true;
            invincibilityEndTime = Time.time + 2.5f;

            Vector3 spawnPos = (clientId == NetworkManager.ServerClientId) ? new Vector3(-3f, 1f, 0f) : new Vector3(3f, 1f, 0f);
            float yaw = (clientId == NetworkManager.ServerClientId) ? 90f : -90f;

            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            transform.position = spawnPos;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (cc != null) cc.enabled = true;

            NotifyPlayerResetClientRpc(spawnPos, yaw);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyPlayerResetClientRpc(Vector3 pos, float yaw)
        {
            var cc = GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            transform.position = pos;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            if (cc != null) cc.enabled = true;

            if (playerController != null)
            {
                playerController.ResetLookOrientation(yaw);
            }

            if (playerAbilities != null)
            {
                playerAbilities.CancelAllAbilities();
            }

            var recorder = GetComponent<SliceIt.Combat.BladeInputRecorder>();
            if (recorder != null)
            {
                recorder.CancelRecording();
            }

            OnHealthChanged?.Invoke(maxHealth, maxHealth);
            OnPlayerRevived?.Invoke();

            if (IsOwner)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        #region RPCs & Callbacks

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyDamageTakenClientRpc(float damage, float remainingHp)
        {
            if (playerController != null)
            {
                playerController.TriggerHitAnimation();
            }
            OnHealthChanged?.Invoke(remainingHp, maxHealth);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyDamageBlockedClientRpc()
        {
            Debug.Log("[PlayerHealth] Shield block sound/VFX triggered.");
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyRevivedClientRpc()
        {
            OnPlayerRevived?.Invoke();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyTeamDefeatedClientRpc()
        {
            OnTeamDefeated?.Invoke();
        }

        private void HandleHealthChanged(float previous, float current)
        {
            OnHealthChanged?.Invoke(current, maxHealth);
        }

        private void HandleDownedChanged(bool previous, bool current)
        {
            if (current)
            {
                OnPlayerDowned?.Invoke();
            }
            else
            {
                OnPlayerRevived?.Invoke();
            }
        }

        private void HandleReviveTimerChanged(float previous, float current)
        {
            OnReviveTimerUpdated?.Invoke(current);
        }

        #endregion
    }
}
