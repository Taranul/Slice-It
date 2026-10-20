using System.Collections.Generic;
using NUnit.Framework;
using SliceIt.Combat;
using SliceIt.Enemies;
using SliceIt.Player;
using SliceIt.Progression;
using Unity.Netcode;
using UnityEngine;

namespace SliceIt.Tests
{
    public class CombatAndProgressionTests
    {
        [Test]
        public void CameraRibbonTrajectoryConverter_ConvertsPointsToSegments()
        {
            var converter = new CameraRibbonTrajectoryConverter();

            var camObj = new GameObject("TestCam");
            var cam = camObj.AddComponent<Camera>();
            cam.pixelRect = new Rect(0, 0, 1920, 1080);

            var inputPoints = new List<TrajectoryPoint2D>
            {
                new TrajectoryPoint2D(new Vector2(960, 540), 0.0f),
                new TrajectoryPoint2D(new Vector2(1000, 550), 0.1f),
                new TrajectoryPoint2D(new Vector2(1050, 560), 0.2f)
            };

            float reach = 2.6f;
            float near = 0.4f;

            var segments = converter.ConvertTo3D(inputPoints, cam, reach, near);

            Assert.IsNotNull(segments);
            Assert.AreEqual(2, segments.Count, "3 input points must yield 2 trajectory segments.");

            foreach (var seg in segments)
            {
                float tipToBaseDist = Vector3.Distance(seg.BladeBase, seg.BladeTip);
                Assert.AreEqual(reach - near, tipToBaseDist, 0.01f, "Segment blade span must equal reachDistance - nearOffset.");
            }

            Object.DestroyImmediate(camObj);
        }

        [Test]
        public void EnemyPrefabs_HaveRequiredComponentsAndSetup()
        {
            string[] prefabPaths = {
                "Assets/_Project/Prefabs/Enemy_Rank1.prefab",
                "Assets/_Project/Prefabs/Enemy_Rank2.prefab"
            };

            for (int i = 0; i < prefabPaths.Length; i++)
            {
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPaths[i]);
                Assert.IsNotNull(prefab, $"Prefab should exist at {prefabPaths[i]}");

                Assert.IsNotNull(prefab.GetComponent<NetworkObject>(), $"{prefab.name} must have NetworkObject.");
                Assert.IsNotNull(prefab.GetComponent<CapsuleCollider>(), $"{prefab.name} must have CapsuleCollider.");
                Assert.IsNotNull(prefab.GetComponent<EnemyEntity>(), $"{prefab.name} must have EnemyEntity.");

                var core = prefab.GetComponentInChildren<EnemyCore>();
                Assert.IsNotNull(core, $"{prefab.name} must have child EnemyCore.");

                var sphereCol = core.GetComponent<SphereCollider>();
                Assert.IsNotNull(sphereCol, "Core must have SphereCollider.");
                Assert.IsTrue(sphereCol.isTrigger, "Core SphereCollider must be a trigger.");
            }
        }

        [Test]
        public void EnemyPair_InitializesCorrectly()
        {
            var pair = new EnemyPair
            {
                PairId = 42,
                PairColor = Color.magenta
            };

            pair.Initialize();

            Assert.AreEqual(PairResolutionState.BothActive, pair.ResolutionState);
            Assert.AreEqual(0f, pair.WindowTimer);
            Assert.IsNull(pair.FirstDefeated);
            Assert.IsFalse(pair.IsWindowActive);
        }

        [Test]
        public void ClientNetworkTransform_IsOwnerAuthoritative()
        {
            var go = new GameObject("TestClientTransform");
            var cnt = go.AddComponent<ClientNetworkTransform>();

            // Reflection to check protected OnIsServerAuthoritative returns false
            var method = typeof(ClientNetworkTransform).GetMethod(
                "OnIsServerAuthoritative",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            );
            Assert.IsNotNull(method, "OnIsServerAuthoritative method must exist.");

            bool isServerAuth = (bool)method.Invoke(cnt, null);
            Assert.IsFalse(isServerAuth, "ClientNetworkTransform must NOT be server-authoritative (must return false for owner authority).");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void PlayerPrefab_UsesClientNetworkTransform()
        {
            var playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            Assert.IsNotNull(playerPrefab);

            var cnt = playerPrefab.GetComponent<ClientNetworkTransform>();
            Assert.IsNotNull(cnt, "Player.prefab must have ClientNetworkTransform to allow non-host client locomotion and rotation.");
        }
    }
}
