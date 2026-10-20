using System;
using System.Collections.Generic;
using SliceIt.Enemies;
using SliceIt.Player;
using Unity.Netcode;
using UnityEngine;

namespace SliceIt.Combat
{
    /// <summary>
    /// Executes the 3D blade slash along the converted trajectory.
    /// Performs intersection checks against enemy cores and bodies.
    /// Enforces the rule: One completed swipe hits at most one core.
    /// Sends authoritative ServerRpc for validated hits.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(BladeInputRecorder))]
    public class BladeSlashExecutor : NetworkBehaviour
    {
        [Header("Blade Reach & Geometry")]
        [SerializeField] private float bladeReach = 2.8f;
        [SerializeField] private float nearOffset = 0.3f;
        [SerializeField] private float slashThickness = 0.25f;

        public event Action<Vector3, Vector3> OnSlashExecuted; // Start & End world positions for visual trail

        private PlayerController playerController;
        private BladeInputRecorder inputRecorder;
        private ITrajectoryConverter3D trajectoryConverter;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            inputRecorder = GetComponent<BladeInputRecorder>();
            trajectoryConverter = new CameraRibbonTrajectoryConverter();
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner && inputRecorder != null)
            {
                inputRecorder.OnSlashRecorded += HandleSlashRecorded;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner && inputRecorder != null)
            {
                inputRecorder.OnSlashRecorded -= HandleSlashRecorded;
            }
        }

        private void HandleSlashRecorded(IReadOnlyList<TrajectoryPoint2D> points)
        {
            if (!IsOwner || playerController == null || playerController.PlayerCamera == null) return;

            var segments = trajectoryConverter.ConvertTo3D(
                points,
                playerController.PlayerCamera,
                bladeReach,
                nearOffset
            );

            if (segments == null || segments.Count == 0) return;

            OnSlashExecuted?.Invoke(segments[0].BladeBase, segments[segments.Count - 1].BladeTip);
            ProcessTrajectoryIntersections(segments);
        }

        private void ProcessTrajectoryIntersections(List<TrajectorySegment3D> segments)
        {
            EnemyCore hitCore = null;
            EnemyEntity nearMissEnemy = null;
            Vector3 nearMissPoint = Vector3.zero;
            Vector3 nearMissDirection = Vector3.zero;

            // Rule: One swipe hits at most ONE core
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                Vector3 center = (seg.BladeBase + seg.BladeTip) * 0.5f;
                float halfHeight = Vector3.Distance(seg.BladeBase, seg.BladeTip) * 0.5f;

                Collider[] overlaps = Physics.OverlapCapsule(
                    seg.BladeBase,
                    seg.BladeTip,
                    slashThickness,
                    ~0,
                    QueryTriggerInteraction.Collide
                );

                for (int j = 0; j < overlaps.Length; j++)
                {
                    var col = overlaps[j];

                    // Check for enemy core
                    var core = col.GetComponent<EnemyCore>();
                    if (core != null && core.OwnerEnemy != null && core.OwnerEnemy.IsActive)
                    {
                        hitCore = core;
                        Debug.Log($"[BladeSlashExecutor] Detected core collision on enemy {core.OwnerEnemy.EnemyId}!");
                        break; // Stop immediately: max 1 core hit!
                    }

                    // Check for near miss on enemy body
                    if (hitCore == null)
                    {
                        var enemy = col.GetComponentInParent<EnemyEntity>();
                        if (enemy != null && enemy.IsActive)
                        {
                            nearMissEnemy = enemy;
                            nearMissPoint = col.ClosestPoint(center);
                            nearMissDirection = (seg.EndPoint - seg.StartPoint).normalized;
                        }
                    }
                }

                if (hitCore != null) break;
            }

            // Authoritative dispatch
            if (hitCore != null)
            {
                var netObj = hitCore.OwnerEnemy.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    RequestCoreHitServerRpc(netObj.NetworkObjectId);
                }
            }
            else if (nearMissEnemy != null)
            {
                var netObj = nearMissEnemy.GetComponent<NetworkObject>();
                if (netObj != null)
                {
                    RequestNearMissServerRpc(netObj.NetworkObjectId, nearMissPoint, nearMissDirection);
                }
            }
        }

        [Rpc(SendTo.Server)]
        private void RequestCoreHitServerRpc(ulong enemyNetworkObjectId, RpcParams rpcParams = default)
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null) return;

            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(enemyNetworkObjectId, out var netObj))
            {
                Debug.LogWarning($"[BladeSlashExecutor] ServerRpc: Enemy NetworkObject {enemyNetworkObjectId} not found in SpawnedObjects.");
                return;
            }

            var enemy = netObj.GetComponent<EnemyEntity>();
            if (enemy == null || !enemy.IsActive)
            {
                Debug.LogWarning($"[BladeSlashExecutor] ServerRpc: Enemy is null or not active.");
                return;
            }

            // Distance sanity check: within reach + player movement margin
            float dist = Vector3.Distance(transform.position, enemy.transform.position);
            if (dist > bladeReach + 5.0f)
            {
                Debug.LogWarning($"[BladeSlashExecutor] ServerRpc: Enemy distance too far ({dist:F1}m > {bladeReach + 5.0f}m).");
                return;
            }

            ulong attackerId = rpcParams.Receive.SenderClientId;
            Debug.Log($"[BladeSlashExecutor] Server successfully verified core hit on Enemy {enemy.EnemyId} (Pair {enemy.PairId}) from Client {attackerId}.");
            enemy.PierceCore(attackerId);
        }

        [Rpc(SendTo.Server)]
        private void RequestNearMissServerRpc(ulong enemyNetworkObjectId, Vector3 hitPoint, Vector3 slashDir, RpcParams rpcParams = default)
        {
            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(enemyNetworkObjectId, out var netObj))
                return;

            var enemy = netObj.GetComponent<EnemyEntity>();
            if (enemy == null || !enemy.IsActive) return;

            enemy.ApplyNearMissReaction(hitPoint, slashDir);
        }
    }
}
