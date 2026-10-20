using System;
using Unity.Netcode;
using UnityEngine;

namespace SliceIt.Enemies
{
    public enum EnemyState : byte
    {
        Active = 0,
        DefeatedPending = 1,
        MergedDespawned = 2
    }

    /// <summary>
    /// Network-synchronized enemy entity representing a member of a progression pair.
    /// Manages core piercing, defeated visual state during pair window, and timeout restoration.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class EnemyEntity : NetworkBehaviour
    {
        [Header("Identity & Progression")]
        [SerializeField] private int enemyId;
        [SerializeField] private int pairId;
        [SerializeField] private int rank = 1;

        [Header("Components")]
        [SerializeField] private EnemyCore core;
        [SerializeField] private Collider bodyCollider;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Rigidbody rb;

        public NetworkVariable<EnemyState> State { get; } = new NetworkVariable<EnemyState>(
            EnemyState.Active,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<int> NetworkPairId { get; } = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<int> NetworkRank { get; } = new NetworkVariable<int>(
            1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public int EnemyId => enemyId;
        public int PairId => NetworkPairId.Value > 0 ? NetworkPairId.Value : pairId;
        public int Rank => NetworkRank.Value > 0 ? NetworkRank.Value : rank;
        public EnemyCore Core => core;
        public bool IsActive => State.Value == EnemyState.Active;
        public Vector3 DefeatedPosition { get; private set; }

        public event Action<EnemyEntity> OnDefeatedPending;
        public event Action<EnemyEntity> OnRestored;

        private float stunRemaining = 0f;

        private void Awake()
        {
            if (core == null) core = GetComponentInChildren<EnemyCore>();
            if (bodyCollider == null) bodyCollider = GetComponent<Collider>();
            if (visualRoot == null) visualRoot = transform.Find("Visuals") ?? transform;
            if (rb == null) rb = GetComponent<Rigidbody>();
        }

        public override void OnNetworkSpawn()
        {
            State.OnValueChanged += HandleStateChanged;
            ApplyStateVisuals(State.Value);

            if (IsServer)
            {
                NetworkPairId.Value = pairId;
                NetworkRank.Value = rank;

                if (SliceIt.Progression.PairManager.Instance != null)
                {
                    SliceIt.Progression.PairManager.Instance.RegisterEnemy(this);
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            State.OnValueChanged -= HandleStateChanged;
        }

        private void Update()
        {
            if (stunRemaining > 0f)
            {
                stunRemaining -= Time.deltaTime;
            }
        }

        public void InitializePairInfo(int id, int pair, int rnk, Color pairColor)
        {
            enemyId = id;
            pairId = pair;
            rank = rnk;

            if (core != null)
            {
                core.SetCoreColor(pairColor);
            }
        }

        #region Core Hit & State Transitions (Server Authoritative)

        /// <summary>
        /// Called when the enemy's core is pierced by a player's blade.
        /// </summary>
        public void PierceCore(ulong attackerClientId)
        {
            if (!IsServer || State.Value != EnemyState.Active) return;

            DefeatedPosition = transform.position;
            State.Value = EnemyState.DefeatedPending;
            ApplyStateVisuals(EnemyState.DefeatedPending);
            OnDefeatedPending?.Invoke(this);

            if (SliceIt.Progression.PairManager.Instance != null)
            {
                SliceIt.Progression.PairManager.Instance.NotifyEnemyDefeated(this);
            }

            NotifyCorePiercedClientRpc(attackerClientId, DefeatedPosition);
        }

        /// <summary>
        /// Restores enemy to active state upon pair window timeout.
        /// </summary>
        public void RestoreAfterTimeout(Vector3 position)
        {
            if (!IsServer) return;

            transform.position = position;
            State.Value = EnemyState.Active;
            ApplyStateVisuals(EnemyState.Active);
            OnRestored?.Invoke(this);

            NotifyRestoredClientRpc(position);
        }

        /// <summary>
        /// Despawns enemy when successfully merged into the next rank.
        /// </summary>
        public void CompleteMerge()
        {
            if (!IsServer) return;

            State.Value = EnemyState.MergedDespawned;
            if (NetworkObject != null && NetworkObject.IsSpawned)
            {
                NetworkObject.Despawn(true);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        #endregion

        #region Near-Miss Reactions

        /// <summary>
        /// Fixed reaction when blade passes near core without piercing it (knockback / stun).
        /// </summary>
        public void ApplyNearMissReaction(Vector3 hitPoint, Vector3 slashDirection)
        {
            if (!IsServer || State.Value != EnemyState.Active) return;

            // Apply stun and slight knockback
            stunRemaining = 0.8f;
            Vector3 pushForce = (slashDirection.normalized + Vector3.up * 0.2f) * 6.0f;

            if (rb != null)
            {
                rb.AddForce(pushForce, ForceMode.Impulse);
            }

            NotifyNearMissClientRpc(pushForce, 0.8f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyNearMissClientRpc(Vector3 knockback, float stunDuration)
        {
            stunRemaining = stunDuration;
            Debug.Log($"[EnemyEntity] Enemy {enemyId} suffered near-miss! Stunned for {stunDuration}s.");
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyCorePiercedClientRpc(ulong attackerId, Vector3 pos)
        {
            ApplyStateVisuals(EnemyState.DefeatedPending);
            Debug.Log($"[EnemyEntity] Enemy {enemyId} core pierced by Client {attackerId} at {pos}!");
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyRestoredClientRpc(Vector3 pos)
        {
            transform.position = pos;
            ApplyStateVisuals(EnemyState.Active);
            Debug.Log($"[EnemyEntity] Enemy {enemyId} restored to full health at {pos} after pair timeout.");
        }

        #endregion

        #region Visual State Updates

        private void HandleStateChanged(EnemyState previous, EnemyState current)
        {
            ApplyStateVisuals(current);
        }

        private void ApplyStateVisuals(EnemyState state)
        {
            switch (state)
            {
                case EnemyState.Active:
                    if (visualRoot != null) visualRoot.localRotation = Quaternion.identity;
                    if (core != null) core.SetColliderActive(true);
                    if (bodyCollider != null) bodyCollider.enabled = true;
                    gameObject.SetActive(true);
                    break;

                case EnemyState.DefeatedPending:
                    // Knocked down on ground as visual body during the 10s pair window
                    if (visualRoot != null) visualRoot.localRotation = Quaternion.Euler(85f, 0f, 0f);
                    if (core != null) core.SetColliderActive(false);
                    if (bodyCollider != null) bodyCollider.enabled = false;
                    break;

                case EnemyState.MergedDespawned:
                    gameObject.SetActive(false);
                    break;
            }
        }

        #endregion
    }
}
