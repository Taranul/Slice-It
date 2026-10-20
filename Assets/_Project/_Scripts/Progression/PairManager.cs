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
    public class PairManager : NetworkBehaviour
    {
        public static PairManager Instance { get; private set; }

        [Header("Configuration")]
        [Tooltip("The time window in seconds to defeat the second enemy of a pair.")]
        [SerializeField] private float defaultPairWindowDuration = 10.0f;

        [SerializeField] private List<EnemyPair> configuredPairs = new List<EnemyPair>();

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
            ActivePairId.OnValueChanged += HandleActivePairChanged;
            PairWindowRemaining.OnValueChanged += HandleTimerChanged;

            if (IsServer)
            {
                InitializePairs();
            }
        }

        public override void OnNetworkDespawn()
        {
            ActivePairId.OnValueChanged -= HandleActivePairChanged;
            PairWindowRemaining.OnValueChanged -= HandleTimerChanged;

            if (IsServer)
            {
                UnsubscribePairs();
            }
        }

        public void RegisterPair(EnemyPair pair)
        {
            if (!configuredPairs.Contains(pair))
            {
                configuredPairs.Add(pair);
            }

            if (IsServer)
            {
                pair.Initialize();
                SubscribePairEvents(pair);
            }
        }

        private void InitializePairs()
        {
            for (int i = 0; i < configuredPairs.Count; i++)
            {
                var pair = configuredPairs[i];
                pair.Initialize();
                SubscribePairEvents(pair);
            }
        }

        private void SubscribePairEvents(EnemyPair pair)
        {
            if (pair.EnemyA != null) pair.EnemyA.OnDefeatedPending += HandleEnemyDefeated;
            if (pair.EnemyB != null) pair.EnemyB.OnDefeatedPending += HandleEnemyDefeated;
        }

        private void UnsubscribePairs()
        {
            for (int i = 0; i < configuredPairs.Count; i++)
            {
                var pair = configuredPairs[i];
                if (pair.EnemyA != null) pair.EnemyA.OnDefeatedPending -= HandleEnemyDefeated;
                if (pair.EnemyB != null) pair.EnemyB.OnDefeatedPending -= HandleEnemyDefeated;
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
                    // Timeout!
                    ResolvePairTimeout(pair);
                }
            }

            if (!anyActive && ActivePairId.Value != 0)
            {
                ActivePairId.Value = 0;
                PairWindowRemaining.Value = 0f;
            }
        }

        private void HandleEnemyDefeated(EnemyEntity defeatedEnemy)
        {
            if (!IsServer) return;

            var pair = configuredPairs.Find(p => p.PairId == defeatedEnemy.PairId);
            if (pair == null) return;

            if (pair.ResolutionState == PairResolutionState.BothActive)
            {
                // First enemy defeated: start 10s pair window!
                pair.ResolutionState = PairResolutionState.WindowRunning;
                pair.WindowTimer = defaultPairWindowDuration;
                pair.FirstDefeated = defeatedEnemy;

                ActivePairId.Value = pair.PairId;
                PairWindowRemaining.Value = defaultPairWindowDuration;

                NotifyPairWindowStartedClientRpc(pair.PairId, defaultPairWindowDuration);
            }
            else if (pair.ResolutionState == PairResolutionState.WindowRunning)
            {
                // Second enemy defeated within window: SUCCESSFUL MERGE!
                ResolvePairMerge(pair);
            }
        }

        private void ResolvePairMerge(EnemyPair pair)
        {
            pair.ResolutionState = PairResolutionState.Merged;

            // Despawn both defeated participants
            if (pair.EnemyA != null) pair.EnemyA.CompleteMerge();
            if (pair.EnemyB != null) pair.EnemyB.CompleteMerge();

            // Spawn next rank enemy at designated spawn point
            Vector3 spawnPos = pair.MergeSpawnPoint != null ? pair.MergeSpawnPoint.position : (pair.EnemyA.transform.position + pair.EnemyB.transform.position) * 0.5f;
            Quaternion spawnRot = pair.MergeSpawnPoint != null ? pair.MergeSpawnPoint.rotation : Quaternion.identity;

            if (pair.NextRankPrefab != null)
            {
                var nextRankObj = Instantiate(pair.NextRankPrefab, spawnPos, spawnRot);
                var netObj = nextRankObj.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    netObj.Spawn();
                }
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
            Debug.Log($"[PairManager] Pair {pairId} window STARTED! {duration} seconds remaining to kill partner.");
            OnPairWindowUpdated?.Invoke(pairId, duration);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyMergeCompletedClientRpc(int pairId, Vector3 mergePoint)
        {
            Debug.Log($"[PairManager] Pair {pairId} successfully MERGED at {mergePoint} into next rank!");
            OnMergeCompleted?.Invoke(pairId);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyPairTimeoutClientRpc(int pairId)
        {
            Debug.Log($"[PairManager] Pair {pairId} TIMED OUT! Enemies restored to full health.");
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
