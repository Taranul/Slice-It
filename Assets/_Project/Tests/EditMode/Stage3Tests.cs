using NUnit.Framework;
using SliceIt.Buffs;
using SliceIt.Enemies;
using SliceIt.Player;
using SliceIt.Progression;
using Unity.Netcode;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SliceIt.Tests
{
    public class Stage3Tests
    {
        [Test]
        public void PlayerHealth_InitializesWithCorrectDefaults()
        {
            var go = new GameObject("TestHealthObj");
            go.AddComponent<PlayerController>();
            var health = go.AddComponent<PlayerHealth>();

            Assert.AreEqual(100f, health.MaxHealth);
            Assert.IsTrue(health.IsAlive);
            Assert.IsFalse(health.IsDowned.Value);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void InRunBuffManager_AppliesEnhancement()
        {
            var go = new GameObject("TestBuffObj");
            go.AddComponent<PlayerController>();
            var mgr = go.AddComponent<InRunBuffManager>();

            Assert.AreEqual(InRunBuffType.None, mgr.ActiveBuff);

            mgr.ApplyBuff(InRunBuffType.ExtendedReach);
            Assert.AreEqual(InRunBuffType.ExtendedReach, mgr.ActiveBuff);
            Assert.IsFalse(mgr.IsBuffMenuOpen);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void AllSevenRankPrefabs_ExistAndHaveRequiredComponents()
        {
            for (int r = 1; r <= 7; r++)
            {
                string path = $"Assets/_Project/Prefabs/Enemy_Rank{r}.prefab";
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.IsNotNull(prefab, $"Enemy_Rank{r}.prefab must exist at {path}");

                var entity = prefab.GetComponent<EnemyEntity>();
                Assert.IsNotNull(entity, $"Enemy_Rank{r} must have EnemyEntity.");
                Assert.AreEqual(r, entity.Rank, $"Enemy_Rank{r} must have rank {r}.");

                var ai = prefab.GetComponent<EnemyAI>();
                Assert.IsNotNull(ai, $"Enemy_Rank{r} must have EnemyAI.");

                var netObj = prefab.GetComponent<NetworkObject>();
                Assert.IsNotNull(netObj, $"Enemy_Rank{r} must have NetworkObject.");
            }
        }

        [Test]
        public void PairManager_HasConfiguredPairs()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/SampleScene.unity");

            var progObj = GameObject.Find("Arena_Progression");
            Assert.IsNotNull(progObj, "Arena_Progression must exist in SampleScene.");

            var pm = progObj.GetComponent<PairManager>();
            Assert.IsNotNull(pm, "PairManager must be attached to Arena_Progression.");

            var so = new UnityEditor.SerializedObject(pm);
            var pairs = so.FindProperty("configuredPairs");
            Assert.GreaterOrEqual(pairs.arraySize, 16, "Arena must have at least 16 configured pairs for 32 Rank 1 enemies.");
        }
    }
}
