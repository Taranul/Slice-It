using SliceIt.Player;
using Unity.Netcode;
using UnityEngine;

namespace SliceIt.Enemies
{
    /// <summary>
    /// Server-authoritative AI for progression enemies:
    /// Tracks closest player, navigates within aggro radius, and performs melee strikes.
    /// </summary>
    [RequireComponent(typeof(EnemyEntity))]
    public class EnemyAI : NetworkBehaviour
    {
        [Header("AI Parameters")]
        [SerializeField] private float aggroRadius = 28f;
        [SerializeField] private float moveSpeed = 3.2f;
        [SerializeField] private float attackRange = 2.2f;
        [SerializeField] private float attackDamage = 25f;
        [SerializeField] private float attackCooldown = 1.8f;

        private EnemyEntity enemyEntity;
        private Rigidbody rb;
        private float nextAttackTime = 0f;

        private void Awake()
        {
            enemyEntity = GetComponent<EnemyEntity>();
            rb = GetComponent<Rigidbody>();
        }

        public void ConfigureRankStats(int rank)
        {
            moveSpeed = 2.8f + (rank * 0.25f);
            attackRange = 1.8f + (rank * 0.45f);
            attackDamage = 15f + (rank * 10f);
            attackCooldown = Mathf.Max(1.0f, 2.0f - (rank * 0.12f));
            aggroRadius = rank >= 6 ? 999f : (24f + rank * 4f); // Bosses have arena-wide aggro
        }

        private void Update()
        {
            if (!IsServer || enemyEntity == null || !enemyEntity.IsActive) return;

            Transform target = FindClosestLivingPlayer(out float distance);
            if (target == null || distance > aggroRadius) return;

            // Rotate toward player
            Vector3 lookDirection = (target.position - transform.position);
            lookDirection.y = 0f;
            if (lookDirection.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, 180f * Time.deltaTime);
            }

            // Move towards player if outside attack range
            if (distance > attackRange * 0.8f)
            {
                Vector3 step = lookDirection.normalized * moveSpeed * Time.deltaTime;
                transform.position += step;
            }

            // Melee strike
            if (distance <= attackRange && Time.time >= nextAttackTime)
            {
                PerformMeleeAttack(target);
            }
        }

        private void PerformMeleeAttack(Transform target)
        {
            nextAttackTime = Time.time + attackCooldown;

            var playerHealth = target.GetComponent<PlayerHealth>();
            if (playerHealth != null && playerHealth.IsAlive)
            {
                playerHealth.TakeDamage(attackDamage, transform.position);
            }
        }

        private Transform FindClosestLivingPlayer(out float closestDistance)
        {
            closestDistance = float.MaxValue;
            Transform closestPlayer = null;

            if (NetworkManager.Singleton == null) return null;

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;

                var health = client.PlayerObject.GetComponent<PlayerHealth>();
                if (health != null && !health.IsAlive) continue; // Ignore downed players

                float dist = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
                if (dist < closestDistance)
                {
                    closestDistance = dist;
                    closestPlayer = client.PlayerObject.transform;
                }
            }

            return closestPlayer;
        }
    }
}
