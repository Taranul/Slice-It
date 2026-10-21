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

        public override void OnNetworkSpawn()
        {
            IsVictory.OnValueChanged += HandleVictoryChanged;

            if (IsServer)
            {
                pairManager.OnMergeCompleted += HandleMergeCompleted;
            }
        }

        public override void OnNetworkDespawn()
        {
            IsVictory.OnValueChanged -= HandleVictoryChanged;

            if (IsServer && pairManager != null)
            {
                pairManager.OnMergeCompleted -= HandleMergeCompleted;
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
