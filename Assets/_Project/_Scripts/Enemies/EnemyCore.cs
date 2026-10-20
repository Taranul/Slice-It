using UnityEngine;

namespace SliceIt.Enemies
{
    /// <summary>
    /// Glowing core inside the enemy body.
    /// Piercing this core defeats the enemy and starts or completes the pair window.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class EnemyCore : MonoBehaviour
    {
        [SerializeField] private EnemyEntity ownerEnemy;
        [SerializeField] private Renderer coreRenderer;

        private SphereCollider coreCollider;
        private MaterialPropertyBlock propBlock;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public EnemyEntity OwnerEnemy => ownerEnemy;

        private void Awake()
        {
            coreCollider = GetComponent<SphereCollider>();
            coreCollider.isTrigger = true;

            if (ownerEnemy == null)
            {
                ownerEnemy = GetComponentInParent<EnemyEntity>();
            }

            if (coreRenderer == null)
            {
                coreRenderer = GetComponent<Renderer>();
            }

            propBlock = new MaterialPropertyBlock();
        }

        public void SetCoreColor(Color color)
        {
            if (coreRenderer == null) return;

            coreRenderer.GetPropertyBlock(propBlock);
            propBlock.SetColor(BaseColorId, color);
            propBlock.SetColor(EmissionColorId, color * 2.5f); // Emissive glow for URP
            coreRenderer.SetPropertyBlock(propBlock);
        }

        public void SetColliderActive(bool active)
        {
            if (coreCollider != null)
            {
                coreCollider.enabled = active;
            }
        }
    }
}
