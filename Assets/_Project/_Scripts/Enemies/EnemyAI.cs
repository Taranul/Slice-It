using System;
using SliceIt.Player;
using SliceIt.Progression;
using Unity.Netcode;
using UnityEngine;

namespace SliceIt.Enemies
{
    public enum EnemyAIState : byte
    {
        Chasing = 0,
        ChargingWindup = 1,
        ChargingRush = 2,
        Leaping = 3,
        SlammingWindup = 4
    }

    /// <summary>
    /// Server-authoritative AI for progression enemies with distinct behaviors per rank:
    /// - Rank 1: Standard relentless melee swarmer.
    /// - Rank 2 (Charger): Wind-up sprint charge rushing forward at high speed.
    /// - Rank 3 (Leaper): Airborne pounce leaping toward player from distance.
    /// - Rank 4 (Ground Slammer): Heavy ground slam shockwave hitting in radial AoE.
    /// - Rank 5 (Berserker): Frenzy mode (+40% speed, rapid attacks) when partner is defeated.
    /// - Rank 6/7 (Titan / Final Boss): Arena-wide aggro, cleave strike, and periodic Earthquake Stomp shockwave.
    /// </summary>
    [RequireComponent(typeof(EnemyEntity))]
    public class EnemyAI : NetworkBehaviour
    {
        [Header("AI Parameters")]
        [SerializeField] private float aggroRadius = 26f;
        [SerializeField] private float moveSpeed = 3.2f;
        [SerializeField] private float attackRange = 2.2f;
        [SerializeField] private float attackDamage = 20f;
        [SerializeField] private float attackCooldown = 1.8f;

        private EnemyEntity enemyEntity;
        private Rigidbody rb;
        private Animator animator;

        private EnemyAIState aiState = EnemyAIState.Chasing;
        private float stateTimer = 0f;
        private float nextAttackTime = 0f;
        private float specialAbilityCooldownTimer = 0f;

        // Rank-specific state variables
        private Vector3 chargeDirection = Vector3.forward;
        private Vector3 leapTargetPos;
        private Vector3 leapVelocity;
        private bool isEnraged = false;
        private float nextStompTime = 0f;

        private void Awake()
        {
            enemyEntity = GetComponent<EnemyEntity>();
            rb = GetComponent<Rigidbody>();
            animator = GetComponentInChildren<Animator>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                ConfigureRankStats(enemyEntity.Rank);
            }
        }

        public void ConfigureRankStats(int rank)
        {
            switch (rank)
            {
                case 1: // Swarmer
                    moveSpeed = 3.0f;
                    attackRange = 1.9f;
                    attackDamage = 18f;
                    attackCooldown = 1.6f;
                    aggroRadius = 24f;
                    break;

                case 2: // Charger
                    moveSpeed = 3.3f;
                    attackRange = 2.2f;
                    attackDamage = 22f;
                    attackCooldown = 1.8f;
                    aggroRadius = 28f;
                    specialAbilityCooldownTimer = Time.time + 3.0f;
                    break;

                case 3: // Leaper
                    moveSpeed = 3.5f;
                    attackRange = 2.4f;
                    attackDamage = 26f;
                    attackCooldown = 1.7f;
                    aggroRadius = 30f;
                    specialAbilityCooldownTimer = Time.time + 2.5f;
                    break;

                case 4: // Ground Slammer
                    moveSpeed = 3.1f;
                    attackRange = 2.8f;
                    attackDamage = 32f;
                    attackCooldown = 2.2f;
                    aggroRadius = 32f;
                    specialAbilityCooldownTimer = Time.time + 2.0f;
                    break;

                case 5: // Berserker
                    moveSpeed = 3.8f;
                    attackRange = 2.5f;
                    attackDamage = 30f;
                    attackCooldown = 1.4f;
                    aggroRadius = 36f;
                    break;

                case 6: // Semi-Final Titan
                case 7: // Final Boss
                    moveSpeed = 3.6f;
                    attackRange = 3.8f;
                    attackDamage = 45f;
                    attackCooldown = 2.0f;
                    aggroRadius = 999f; // Arena-wide aggro
                    nextStompTime = Time.time + 5.0f;
                    break;

                default:
                    moveSpeed = 3.0f + (rank * 0.2f);
                    attackRange = 2.0f + (rank * 0.3f);
                    attackDamage = 20f + (rank * 8f);
                    attackCooldown = 1.8f;
                    aggroRadius = 30f;
                    break;
            }
        }

        private void Update()
        {
            if (!IsServer || enemyEntity == null || !enemyEntity.IsActive) return;

            int rank = enemyEntity.Rank;

            // Check Berserk mode for Rank 5
            if (rank == 5 && !isEnraged)
            {
                CheckBerserkTrigger();
            }

            // Check Boss Earthquake Stomp for Rank 6/7
            if (rank >= 6 && Time.time >= nextStompTime)
            {
                PerformEarthquakeStomp();
                nextStompTime = Time.time + 7.0f;
            }

            Transform target = FindClosestLivingPlayer(out float distance);
            if (target == null && aiState == EnemyAIState.Chasing) return;
            if (distance > aggroRadius && aiState == EnemyAIState.Chasing) return;

            switch (aiState)
            {
                case EnemyAIState.Chasing:
                    HandleChasingState(target, distance, rank);
                    break;

                case EnemyAIState.ChargingWindup:
                    HandleChargingWindup(target);
                    break;

                case EnemyAIState.ChargingRush:
                    HandleChargingRush();
                    break;

                case EnemyAIState.Leaping:
                    HandleLeapMovement();
                    break;

                case EnemyAIState.SlammingWindup:
                    HandleSlammingWindup();
                    break;
            }
        }

        #region Behavior State Handlers

        private void HandleChasingState(Transform target, float distance, int rank)
        {
            if (target == null) return;

            // Rank 2: Check for Sprint Charge
            if (rank == 2 && distance >= 4.5f && distance <= 13f && Time.time >= specialAbilityCooldownTimer)
            {
                StartChargeWindup(target);
                return;
            }

            // Rank 3: Check for Airborne Leap
            if (rank == 3 && distance >= 5.0f && distance <= 12f && Time.time >= specialAbilityCooldownTimer)
            {
                StartLeap(target);
                return;
            }

            // Rank 4: Check for Ground Slam AoE
            if (rank == 4 && distance <= 3.2f && Time.time >= specialAbilityCooldownTimer)
            {
                StartGroundSlamWindup();
                return;
            }

            // Face target horizontally
            Vector3 lookDir = (target.position - transform.position);
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, 180f * Time.deltaTime);
            }

            // Movement with separation and collision avoidance
            float effectiveSpeed = isEnraged ? (moveSpeed * 1.4f) : moveSpeed;
            float personalRadius = 0.55f + (rank * 0.15f);

            if (distance > attackRange * 0.85f)
            {
                Vector3 separation = ComputeSeparationVelocity(personalRadius);
                Vector3 moveDir = lookDir.normalized;

                if (separation.sqrMagnitude > 0.01f)
                {
                    // Blend direct path with crowd repulsion force
                    moveDir = (lookDir.normalized * 0.55f + separation.normalized * 0.85f).normalized;
                }

                // Check collision before applying movement to prevent overlap
                Vector3 step = moveDir * effectiveSpeed * Time.deltaTime;
                Vector3 targetPos = transform.position + step;

                if (!IsPositionOccupiedByEnemy(targetPos, personalRadius * 0.85f))
                {
                    transform.position = targetPos;
                }
                else
                {
                    // If blocked ahead, slide along the perpendicular tangent
                    Vector3 tangent = Vector3.Cross(moveDir, Vector3.up).normalized;
                    Vector3 slideStep = tangent * (effectiveSpeed * 0.6f * Time.deltaTime);
                    Vector3 slidePos = transform.position + slideStep;

                    if (!IsPositionOccupiedByEnemy(slidePos, personalRadius * 0.8f))
                    {
                        transform.position = slidePos;
                    }
                }
            }

            // Melee strike
            float effectiveCooldown = isEnraged ? (attackCooldown * 0.6f) : attackCooldown;
            if (distance <= attackRange && Time.time >= nextAttackTime)
            {
                PerformMeleeAttack(target);
                nextAttackTime = Time.time + effectiveCooldown;
            }
        }

        #endregion

        #region Rank 2: Charger Mechanics

        private void StartChargeWindup(Transform target)
        {
            aiState = EnemyAIState.ChargingWindup;
            stateTimer = 0.45f;
            Vector3 dir = (target.position - transform.position);
            dir.y = 0f;
            chargeDirection = dir.normalized;
            transform.rotation = Quaternion.LookRotation(chargeDirection, Vector3.up);
            NotifyAttackClientRpc(1); // Play telegraph visual/anim
        }

        private void HandleChargingWindup(Transform target)
        {
            stateTimer -= Time.deltaTime;
            if (target != null)
            {
                Vector3 dir = (target.position - transform.position);
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                {
                    chargeDirection = dir.normalized;
                    transform.rotation = Quaternion.LookRotation(chargeDirection, Vector3.up);
                }
            }

            if (stateTimer <= 0f)
            {
                aiState = EnemyAIState.ChargingRush;
                stateTimer = 0.75f;
            }
        }

        private void HandleChargingRush()
        {
            stateTimer -= Time.deltaTime;
            float chargeSpeed = moveSpeed * 2.6f;
            transform.position += chargeDirection * chargeSpeed * Time.deltaTime;

            // Check player collision during charge
            Collider[] hits = Physics.OverlapSphere(transform.position + Vector3.up * 0.8f, 1.4f);
            for (int i = 0; i < hits.Length; i++)
            {
                var playerHealth = hits[i].GetComponentInParent<PlayerHealth>();
                if (playerHealth != null && playerHealth.IsAlive)
                {
                    playerHealth.TakeDamage(attackDamage * 1.25f, transform.position);
                    Debug.Log($"[EnemyAI] Charger collided with player {playerHealth.OwnerClientId}!");
                    stateTimer = 0f; // End rush on impact
                    break;
                }
            }

            if (stateTimer <= 0f)
            {
                aiState = EnemyAIState.Chasing;
                specialAbilityCooldownTimer = Time.time + 4.5f;
                nextAttackTime = Time.time + 1.0f;
            }
        }

        #endregion

        #region Rank 3: Leaper Mechanics

        private void StartLeap(Transform target)
        {
            aiState = EnemyAIState.Leaping;
            leapTargetPos = target.position;
            Vector3 diff = (leapTargetPos - transform.position);
            float dist = Mathf.Max(1.0f, diff.magnitude);
            float leapDuration = 0.8f;

            Vector3 horiz = new Vector3(diff.x, 0f, diff.z);
            leapVelocity = horiz / leapDuration;
            leapVelocity.y = 7.0f; // Initial vertical upward impulse

            stateTimer = leapDuration;
            NotifyAttackClientRpc(2);
        }

        private void HandleLeapMovement()
        {
            stateTimer -= Time.deltaTime;
            leapVelocity.y -= 18.0f * Time.deltaTime; // Gravity
            transform.position += leapVelocity * Time.deltaTime;

            if (transform.position.y <= 0.5f || stateTimer <= 0f)
            {
                // Landed on ground
                Vector3 p = transform.position;
                p.y = 0.5f;
                transform.position = p;

                aiState = EnemyAIState.Chasing;
                specialAbilityCooldownTimer = Time.time + 5.0f;

                // Impact damage radius
                Collider[] hits = Physics.OverlapSphere(transform.position, 3.2f);
                for (int i = 0; i < hits.Length; i++)
                {
                    var playerHealth = hits[i].GetComponentInParent<PlayerHealth>();
                    if (playerHealth != null && playerHealth.IsAlive)
                    {
                        playerHealth.TakeDamage(attackDamage * 1.3f, transform.position);
                    }
                }
                NotifyAttackClientRpc(3); // Landing impact shockwave
            }
        }

        #endregion

        #region Rank 4: Ground Slammer Mechanics

        private void StartGroundSlamWindup()
        {
            aiState = EnemyAIState.SlammingWindup;
            stateTimer = 0.55f;
            NotifyAttackClientRpc(4);
        }

        private void HandleSlammingWindup()
        {
            stateTimer -= Time.deltaTime;
            if (stateTimer <= 0f)
            {
                aiState = EnemyAIState.Chasing;
                specialAbilityCooldownTimer = Time.time + 4.5f;
                nextAttackTime = Time.time + 1.2f;

                // Execute Radial Shockwave
                Collider[] hits = Physics.OverlapSphere(transform.position, 5.0f);
                for (int i = 0; i < hits.Length; i++)
                {
                    var playerHealth = hits[i].GetComponentInParent<PlayerHealth>();
                    if (playerHealth != null && playerHealth.IsAlive)
                    {
                        playerHealth.TakeDamage(attackDamage * 1.5f, transform.position);
                    }
                }
                NotifyAttackClientRpc(5); // Shockwave effect
            }
        }

        #endregion

        #region Rank 5: Berserker Frenzy

        private void CheckBerserkTrigger()
        {
            if (PairManager.Instance == null) return;

            // Check if pair partner is defeated
            var partner = FindPairPartner();
            if (partner != null && partner.State.Value == EnemyState.DefeatedPending)
            {
                isEnraged = true;
                Debug.LogWarning($"[EnemyAI] Rank 5 Berserker {enemyEntity.EnemyId} entered FRENZY MODE! Partner is defeated!");
                NotifyEnragedClientRpc();
            }
        }

        private EnemyEntity FindPairPartner()
        {
            int myPairId = enemyEntity.PairId;
            var all = UnityEngine.Object.FindObjectsByType<EnemyEntity>(FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != enemyEntity && all[i].PairId == myPairId)
                {
                    return all[i];
                }
            }
            return null;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyEnragedClientRpc()
        {
            isEnraged = true;
            // Visual feedback: red glow / speed trail
            var core = enemyEntity.Core;
            if (core != null)
            {
                core.SetCoreColor(Color.red);
            }
        }

        #endregion

        #region Rank 6 / 7: Earthquake Stomp

        private void PerformEarthquakeStomp()
        {
            NotifyAttackClientRpc(6);

            Collider[] hits = Physics.OverlapSphere(transform.position, 14f);
            for (int i = 0; i < hits.Length; i++)
            {
                var playerHealth = hits[i].GetComponentInParent<PlayerHealth>();
                if (playerHealth != null && playerHealth.IsAlive)
                {
                    var controller = playerHealth.GetComponent<PlayerController>();
                    // Hits players who are grounded
                    if (controller == null || controller.IsGrounded)
                    {
                        playerHealth.TakeDamage(28f, transform.position);
                        Debug.Log($"[EnemyAI] Titan Stomp hit player {playerHealth.OwnerClientId}!");
                    }
                }
            }
        }

        #endregion

        #region Melee & Targeting

        private void PerformMeleeAttack(Transform target)
        {
            NotifyAttackClientRpc(0);

            var playerHealth = target.GetComponent<PlayerHealth>();
            if (playerHealth != null && playerHealth.IsAlive)
            {
                playerHealth.TakeDamage(attackDamage, transform.position);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyAttackClientRpc(int attackType)
        {
            if (animator != null)
            {
                animator.SetTrigger("Attack");
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

        #region Crowd Separation & Collision Resolution

        private Vector3 ComputeSeparationVelocity(float personalRadius)
        {
            Vector3 separation = Vector3.zero;
            float checkRadius = personalRadius * 2.4f;
            Collider[] neighbors = Physics.OverlapSphere(transform.position + Vector3.up * 0.8f, checkRadius);
            int count = 0;

            for (int i = 0; i < neighbors.Length; i++)
            {
                var neighborCol = neighbors[i];
                if (neighborCol.transform.root == transform.root) continue;

                var neighborEntity = neighborCol.GetComponentInParent<EnemyEntity>();
                if (neighborEntity != null && neighborEntity.IsActive)
                {
                    Vector3 diff = transform.position - neighborEntity.transform.position;
                    diff.y = 0f;
                    float dist = diff.magnitude;
                    float desiredDist = personalRadius * 2.0f;

                    if (dist < desiredDist && dist > 0.001f)
                    {
                        // Repel away with force inversely proportional to distance
                        separation += diff.normalized * ((desiredDist - dist) / desiredDist);
                        count++;
                    }
                    else if (dist <= 0.001f)
                    {
                        // Overlapping exactly: apply random radial nudge
                        Vector2 randomCircle = UnityEngine.Random.insideUnitCircle.normalized;
                        separation += new Vector3(randomCircle.x, 0f, randomCircle.y);
                        count++;
                    }
                }
            }

            if (count > 0)
            {
                separation /= count;
            }

            return separation;
        }

        private bool IsPositionOccupiedByEnemy(Vector3 targetPos, float personalRadius)
        {
            Collider[] overlaps = Physics.OverlapSphere(targetPos + Vector3.up * 0.8f, personalRadius);
            for (int i = 0; i < overlaps.Length; i++)
            {
                if (overlaps[i].transform.root == transform.root) continue;
                var other = overlaps[i].GetComponentInParent<EnemyEntity>();
                if (other != null && other.IsActive)
                {
                    return true;
                }
            }
            return false;
        }

        #endregion

        #endregion
    }
}
