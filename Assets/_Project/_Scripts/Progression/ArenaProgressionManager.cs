using System;
using System.Collections.Generic;
using SliceIt.Enemies;
using Unity.Netcode;
using UnityEngine;

namespace SliceIt.Progression
{
    /// <summary>
    /// Manages the full arena concentric progression tree per GDD v0.7:
    /// 64 Rank 1 enemies (32 pairs) -> 32 -> 16 -> 8 -> 4 -> 2 Semi-Final Bosses -> 1 Final Boss.
    /// Spawns the initial 64 enemies at concentric designer spawn points and tracks match victory.
    /// </summary>
    [RequireComponent(typeof(PairManager))]
    [RequireComponent(typeof(NetworkObject))]
    public class ArenaProgressionManager : NetworkBehaviour
    {
        public static ArenaProgressionManager Instance { get; private set; }

        [Header("Prefabs")]
        [SerializeField] private GameObject[] rankPrefabs = new GameObject[7]; // Rank 1 to Rank 7

        [Header("Progression State")]
        public NetworkVariable<int> EnemiesRemaining { get; } = new NetworkVariable<int>(
            32,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<int> HighestRankActive { get; } = new NetworkVariable<int>(
            1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<bool> IsVictory { get; } = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<bool> IsDefeat { get; } = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public event Action OnVictoryAchieved;

        private PairManager pairManager;

        public GameObject GetRankPrefab(int rank)
        {
            int index = rank - 1;
            if (rankPrefabs != null && index >= 0 && index < rankPrefabs.Length)
            {
                return rankPrefabs[index];
            }
            return null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            pairManager = GetComponent<PairManager>();
        }

        private struct InitialEnemyData
        {
            public EnemyEntity entity;
            public Vector3 position;
            public Quaternion rotation;
        }

        private readonly List<InitialEnemyData> initialEnemies = new List<InitialEnemyData>();

        public override void OnNetworkSpawn()
        {
            IsVictory.OnValueChanged += HandleVictoryChanged;

            if (IsServer)
            {
                pairManager.OnMergeCompleted += HandleMergeCompleted;
                CacheInitialEnemies();

                // When SampleScene spawns on server, ensure all existing players are reset and revived
                ResetAllPlayersServer();

                if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
                {
                    NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += HandleSceneLoadCompleted;
                }
            }
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void HandleSceneLoadCompleted(string sceneName, UnityEngine.SceneManagement.LoadSceneMode loadSceneMode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
        {
            if (!IsServer) return;
            Debug.Log($"[ArenaProgressionManager] NGO SceneManager OnLoadEventCompleted ({sceneName}). Resetting all players to spawn.");
            ResetAllPlayersServer();
        }

        private void CacheInitialEnemies()
        {
            initialEnemies.Clear();
            var enemies = UnityEngine.Object.FindObjectsByType<EnemyEntity>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                if (enemies[i].Rank == 1)
                {
                    initialEnemies.Add(new InitialEnemyData
                    {
                        entity = enemies[i],
                        position = enemies[i].transform.position,
                        rotation = enemies[i].transform.rotation
                    });
                }
            }
            Debug.Log($"[ArenaProgressionManager] Cached {initialEnemies.Count} initial Rank 1 enemies for match restarts.");
        }

        /// <summary>
        /// Server-authoritative match restart.
        /// Immediately revives and resets all players, then reloads the arena scene (or falls back to in-place reset).
        /// </summary>
        public void RestartMatchServer()
        {
            if (!IsServer) return;

            Debug.Log("[ArenaProgressionManager] RestartMatchServer requested!");

            // 1. Immediately reset all players so state is alive and unblocked
            ResetAllPlayersServer();

            // 2. Try NGO SceneManager reload
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
            {
                var status = NetworkManager.Singleton.SceneManager.LoadScene("SampleScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
                if (status == Unity.Netcode.SceneEventProgressStatus.Started)
                {
                    Debug.Log("[ArenaProgressionManager] NGO SceneManager.LoadScene started successfully.");
                    return;
                }
                Debug.LogWarning($"[ArenaProgressionManager] NGO SceneManager.LoadScene status: {status}. Performing robust in-place reset.");
            }

            // 3. Fallback in-place robust reset
            PerformInPlaceResetServer();
        }

        public void ResetAllPlayersServer()
        {
            if (!IsServer) return;

            Debug.Log("[ArenaProgressionManager] ResetAllPlayersServer: reviving all players and clearing defeat state.");

            IsVictory.Value = false;
            IsDefeat.Value = false;

            // Search all PlayerHealth components in scene
            var allHealths = UnityEngine.Object.FindObjectsByType<SliceIt.Player.PlayerHealth>(FindObjectsSortMode.None);
            for (int i = 0; i < allHealths.Length; i++)
            {
                var h = allHealths[i];
                if (h != null && h.NetworkObject != null)
                {
                    h.ResetPlayerToSpawnServer(h.NetworkObject.OwnerClientId);
                }
            }

            // Also check ConnectedClientsList
            if (NetworkManager.Singleton != null)
            {
                foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
                {
                    if (client.PlayerObject == null) continue;
                    var health = client.PlayerObject.GetComponent<SliceIt.Player.PlayerHealth>();
                    if (health != null)
                    {
                        health.ResetPlayerToSpawnServer(client.ClientId);
                    }
                }
            }

            NotifyMatchRestartedClientRpc();
        }

        private void PerformInPlaceResetServer()
        {
            // Despawn higher-rank enemies
            var currentEnemies = UnityEngine.Object.FindObjectsByType<EnemyEntity>(FindObjectsSortMode.None);
            for (int i = 0; i < currentEnemies.Length; i++)
            {
                if (currentEnemies[i].Rank > 1)
                {
                    if (currentEnemies[i].NetworkObject != null && currentEnemies[i].NetworkObject.IsSpawned)
                    {
                        currentEnemies[i].NetworkObject.Despawn(true);
                    }
                }
            }

            // Restore initial Rank 1 enemies to their original spawn positions
            for (int i = 0; i < initialEnemies.Count; i++)
            {
                var data = initialEnemies[i];
                if (data.entity != null)
                {
                    data.entity.transform.position = data.position;
                    data.entity.transform.rotation = data.rotation;
                    data.entity.RestoreAfterTimeout(data.position);
                }
            }

            // Reset progression variables
            EnemiesRemaining.Value = initialEnemies.Count > 0 ? initialEnemies.Count : 32;
            HighestRankActive.Value = 1;

            // Reset PairManager
            if (pairManager != null)
            {
                pairManager.ResetAllPairsServer();
            }

            ResetAllPlayersServer();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyMatchRestartedClientRpc()
        {
            Debug.Log("[ArenaProgressionManager] Match restarted for all players!");
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public override void OnNetworkDespawn()
        {
            IsVictory.OnValueChanged -= HandleVictoryChanged;

            if (IsServer)
            {
                if (pairManager != null)
                {
                    pairManager.OnMergeCompleted -= HandleMergeCompleted;
                }

                if (NetworkManager.Singleton != null && NetworkManager.Singleton.SceneManager != null)
                {
                    NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleSceneLoadCompleted;
                }
            }
        }

        private void HandleMergeCompleted(int pairId)
        {
            if (!IsServer) return;

            // Recalculate remaining enemies and highest active rank
            var activeEnemies = FindObjectsByType<EnemyEntity>(FindObjectsSortMode.None);
            int count = 0;
            int maxRank = 1;

            for (int i = 0; i < activeEnemies.Length; i++)
            {
                if (activeEnemies[i].IsActive)
                {
                    count++;
                    if (activeEnemies[i].Rank > maxRank)
                    {
                        maxRank = activeEnemies[i].Rank;
                    }
                }
            }

            EnemiesRemaining.Value = count;
            HighestRankActive.Value = maxRank;

            Debug.Log($"[ArenaProgressionManager] Merge completed for pair {pairId}. Active enemies: {count}, Highest Rank: {maxRank}");
        }

        /// <summary>
        /// Called when both players are defeated. Triggers team defeat!
        /// </summary>
        public void NotifyTeamDefeatedServer()
        {
            if (!IsServer || IsVictory.Value || IsDefeat.Value) return;

            IsDefeat.Value = true;
            Debug.LogWarning("[ArenaProgressionManager] >>> ALL PLAYERS DOWNED! MATCH DEFEAT! <<<");
        }

        /// <summary>
        /// Called when the Rank 7 Final Boss is defeated. Triggers team victory!
        /// </summary>
        public void NotifyFinalBossDefeated()
        {
            if (!IsServer || IsVictory.Value) return;

            IsVictory.Value = true;
            Debug.LogWarning("[ArenaProgressionManager] >>> FINAL BOSS DEFEATED! MATCH VICTORY! <<<");
            NotifyVictoryClientRpc();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyVictoryClientRpc()
        {
            OnVictoryAchieved?.Invoke();
        }

        private void HandleVictoryChanged(bool previous, bool current)
        {
            if (current)
            {
                OnVictoryAchieved?.Invoke();
            }
        }
    }
}
