using System;
using System.Collections.Generic;
using SliceIt.Enemies;
using Unity.Netcode;
using UnityEngine;

namespace SliceIt.Progression
{
    /// <summary>
    /// Server-authoritative manager of enemy pairs, 10-second pair windows,
    /// timeout restorations, and next-rank merges.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PairManager : NetworkBehaviour
    {
        public static PairManager Instance { get; private set; }

        [Header("Configuration")]
        [Tooltip("The time window in seconds to defeat the second enemy of a pair.")]
        [SerializeField] private float defaultPairWindowDuration = 10.0f;

        [SerializeField] private List<EnemyPair> configuredPairs = new List<EnemyPair>();

        [Header("Prefabs for Merging")]
        [SerializeField] private GameObject rank2Prefab;

        // Synchronized network state for HUD
        public NetworkVariable<int> ActivePairId { get; } = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<float> PairWindowRemaining { get; } = new NetworkVariable<float>(
            0f,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public event Action<int, float> OnPairWindowUpdated;
        public event Action<int> OnMergeCompleted;
        public event Action<int> OnPairTimedOut;

        public float WindowDuration => defaultPairWindowDuration;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Debug.Log($"[PairManager] Spawned on network. IsServer={IsServer}, IsClient={IsClient}");

            ActivePairId.OnValueChanged += HandleActivePairChanged;
            PairWindowRemaining.OnValueChanged += HandleTimerChanged;

            if (IsServer)
            {
                AutoDiscoverScenePairs();
                InitializePairs();
            }
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            ActivePairId.OnValueChanged -= HandleActivePairChanged;
            PairWindowRemaining.OnValueChanged -= HandleTimerChanged;
        }

        public void RegisterEnemy(EnemyEntity enemy)
        {
            if (!IsServer || enemy == null) return;

            int pid = enemy.PairId;
            var pair = configuredPairs.Find(p => p.PairId == pid);
            if (pair == null)
            {
                pair = new EnemyPair
                {
                    PairId = pid,
                    EnemyA = enemy,
                    NextRankPrefab = rank2Prefab,
                    PairColor = Color.cyan
                };
                configuredPairs.Add(pair);
                pair.Initialize();
            }
            else
            {
                if (pair.EnemyA == null) pair.EnemyA = enemy;
                else if (pair.EnemyB == null && pair.EnemyA != enemy) pair.EnemyB = enemy;
                pair.Initialize();
            }

            Debug.Log($"[PairManager] Registered enemy {enemy.EnemyId} to Pair {pid}. A={(pair.EnemyA != null ? pair.EnemyA.name : "null")}, B={(pair.EnemyB != null ? pair.EnemyB.name : "null")}");
        }

        private void AutoDiscoverScenePairs()
        {
            var enemies = FindObjectsByType<EnemyEntity>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                RegisterEnemy(enemies[i]);
            }

            var spawnPoint = GameObject.Find("MergeSpawnPoint_Pair1");
            var pair1 = configuredPairs.Find(p => p.PairId == 1);
            if (pair1 != null)
            {
                if (spawnPoint != null) pair1.MergeSpawnPoint = spawnPoint.transform;
                if (pair1.NextRankPrefab == null) pair1.NextRankPrefab = rank2Prefab;
            }
        }

        private void InitializePairs()
        {
            for (int i = 0; i < configuredPairs.Count; i++)
            {
                configuredPairs[i].Initialize();
            }
        }

        private void Update()
        {
            if (!IsServer) return;

            UpdateActivePairTimers();
        }

        private void UpdateActivePairTimers()
        {
            bool anyActive = false;

            for (int i = 0; i < configuredPairs.Count; i++)
            {
                var pair = configuredPairs[i];
                if (pair.ResolutionState != PairResolutionState.WindowRunning) continue;

                pair.WindowTimer -= Time.deltaTime;

                if (pair.PairId == ActivePairId.Value)
                {
                    PairWindowRemaining.Value = Mathf.Max(0f, pair.WindowTimer);
                    anyActive = true;
                }

                if (pair.WindowTimer <= 0f)
                {
                    ResolvePairTimeout(pair);
                }
            }

            if (!anyActive && ActivePairId.Value != 0)
            {
                ActivePairId.Value = 0;
                PairWindowRemaining.Value = 0f;
            }
        }

        /// <summary>
        /// Direct entry point called by EnemyEntity when its core is pierced.
        /// </summary>
        public void NotifyEnemyDefeated(EnemyEntity defeatedEnemy)
        {
            if (!IsServer || defeatedEnemy == null) return;

            int pid = defeatedEnemy.PairId;
            var pair = configuredPairs.Find(p => p.PairId == pid);
            if (pair == null)
            {
                Debug.LogWarning($"[PairManager] No pair found for defeated enemy with pairId={pid}");
                return;
            }

            Debug.Log($"[PairManager] NotifyEnemyDefeated for Enemy {defeatedEnemy.EnemyId} (Pair {pid}). Current resolution state: {pair.ResolutionState}");

            if (pair.ResolutionState == PairResolutionState.BothActive)
            {
                // First enemy defeated: start 10s pair window!
                pair.ResolutionState = PairResolutionState.WindowRunning;
                pair.WindowTimer = defaultPairWindowDuration;
                pair.FirstDefeated = defeatedEnemy;

                ActivePairId.Value = pair.PairId;
                PairWindowRemaining.Value = defaultPairWindowDuration;

                Debug.Log($"[PairManager] >>> PAIR {pair.PairId} WINDOW STARTED: {defaultPairWindowDuration}s remaining! <<<");
                NotifyPairWindowStartedClientRpc(pair.PairId, defaultPairWindowDuration);
            }
            else if (pair.ResolutionState == PairResolutionState.WindowRunning)
            {
                // If it's the other enemy of the pair: SUCCESSFUL MERGE!
                if (defeatedEnemy != pair.FirstDefeated)
                {
                    Debug.Log($"[PairManager] >>> PAIR {pair.PairId} SECOND ENEMY DEFEATED IN TIME! EXECUTING MERGE! <<<");
                    ResolvePairMerge(pair);
                }
            }
        }

        private void ResolvePairMerge(EnemyPair pair)
        {
            pair.ResolutionState = PairResolutionState.Merged;

            // Despawn both defeated participants
            if (pair.EnemyA != null) pair.EnemyA.CompleteMerge();
            if (pair.EnemyB != null) pair.EnemyB.CompleteMerge();

            // Spawn next rank enemy at designated spawn point
            Vector3 spawnPos = pair.MergeSpawnPoint != null ? pair.MergeSpawnPoint.position : Vector3.zero;
            Quaternion spawnRot = pair.MergeSpawnPoint != null ? pair.MergeSpawnPoint.rotation : Quaternion.identity;

            GameObject prefabToSpawn = pair.NextRankPrefab != null ? pair.NextRankPrefab : rank2Prefab;
            if (prefabToSpawn != null)
            {
                var nextRankObj = Instantiate(prefabToSpawn, spawnPos, spawnRot);
                var netObj = nextRankObj.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    netObj.Spawn();
                    Debug.Log($"[PairManager] Spawned next rank enemy ({nextRankObj.name}) on network at {spawnPos}.");
                }
            }
            else
            {
                Debug.LogError("[PairManager] Cannot spawn next rank enemy: NextRankPrefab is null!");
            }

            if (ActivePairId.Value == pair.PairId)
            {
                ActivePairId.Value = 0;
                PairWindowRemaining.Value = 0f;
            }

            NotifyMergeCompletedClientRpc(pair.PairId, spawnPos);
        }

        private void ResolvePairTimeout(EnemyPair pair)
        {
            Debug.Log($"[PairManager] >>> PAIR {pair.PairId} TIMED OUT! Restoring enemies to full health... <<<");
            pair.ResolutionState = PairResolutionState.Restored;

            // Restore first defeated enemy at the position where it fell
            if (pair.FirstDefeated != null)
            {
                pair.FirstDefeated.RestoreAfterTimeout(pair.FirstDefeated.DefeatedPosition);
            }

            // Restore other enemy at its current position
            var survivingEnemy = (pair.FirstDefeated == pair.EnemyA) ? pair.EnemyB : pair.EnemyA;
            if (survivingEnemy != null)
            {
                survivingEnemy.RestoreAfterTimeout(survivingEnemy.transform.position);
            }

            pair.ResolutionState = PairResolutionState.BothActive;
            pair.FirstDefeated = null;

            if (ActivePairId.Value == pair.PairId)
            {
                ActivePairId.Value = 0;
                PairWindowRemaining.Value = 0f;
            }

            NotifyPairTimeoutClientRpc(pair.PairId);
        }

        #region RPCs & Client Events

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyPairWindowStartedClientRpc(int pairId, float duration)
        {
            Debug.Log($"[PairManager] (ClientRpc) Pair {pairId} window STARTED! {duration:F1}s remaining.");
            OnPairWindowUpdated?.Invoke(pairId, duration);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyMergeCompletedClientRpc(int pairId, Vector3 mergePoint)
        {
            Debug.Log($"[PairManager] (ClientRpc) Pair {pairId} successfully MERGED at {mergePoint}!");
            OnMergeCompleted?.Invoke(pairId);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyPairTimeoutClientRpc(int pairId)
        {
            Debug.Log($"[PairManager] (ClientRpc) Pair {pairId} TIMED OUT! Enemies restored.");
            OnPairTimedOut?.Invoke(pairId);
        }

        private void HandleActivePairChanged(int previous, int current)
        {
            OnPairWindowUpdated?.Invoke(current, PairWindowRemaining.Value);
        }

        private void HandleTimerChanged(float previous, float current)
        {
            OnPairWindowUpdated?.Invoke(ActivePairId.Value, current);
        }

        #endregion
    }
}
