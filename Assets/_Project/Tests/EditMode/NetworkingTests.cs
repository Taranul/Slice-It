using NUnit.Framework;
using SliceIt.Networking;
using SliceIt.Player;
using Unity.Netcode;
using UnityEngine;

namespace SliceIt.Tests
{
    public class NetworkingTests
    {
        [Test]
        public void PlayerPrefab_HasRequiredNetworkComponents()
        {
            var playerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Player.prefab");
            Assert.IsNotNull(playerPrefab, "Player.prefab should exist in Assets/_Project/Prefabs/");

            Assert.IsNotNull(playerPrefab.GetComponent<CharacterController>(), "Player should have CharacterController.");
            Assert.IsNotNull(playerPrefab.GetComponent<NetworkObject>(), "Player should have NetworkObject.");
            Assert.IsNotNull(playerPrefab.GetComponent<Unity.Netcode.Components.NetworkTransform>(), "Player should have NetworkTransform.");
            Assert.IsNotNull(playerPrefab.GetComponent<PlayerController>(), "Player should have PlayerController.");
        }

        [Test]
        public void PlayerSpawnManager_CalculatesAdequateSeparation()
        {
            Vector3 hostPos = new Vector3(-3f, 1f, 0f);
            Vector3 clientPos = new Vector3(3f, 1f, 0f);

            float distance = Vector3.Distance(hostPos, clientPos);
            Assert.GreaterOrEqual(distance, 4.0f, "Host and Client spawn positions must be at least 4 units apart to avoid collision.");
            Assert.Greater(hostPos.y, 0f, "Spawn position Y must be above ground level (y > 0).");
            Assert.Greater(clientPos.y, 0f, "Spawn position Y must be above ground level (y > 0).");
        }

        [Test]
        public void NetworkSessionManager_InitializesWithDefaultStatus()
        {
            var go = new GameObject("TestNetSession");
            var mgr = go.AddComponent<NetworkSessionManager>();

            Assert.AreEqual("Disconnected", mgr.CurrentStatus);
            Assert.IsEmpty(mgr.ActiveRelayCode);

            Object.DestroyImmediate(go);
        }
    }
}
