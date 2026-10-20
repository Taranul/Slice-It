using Unity.Netcode;
using UnityEngine;

namespace SliceIt.Networking
{
    /// <summary>
    /// Handles initial spawn positions for connecting players so Host and Client
    /// spawn cleanly on the arena floor facing each other without overlapping.
    /// </summary>
    public class PlayerSpawnManager : MonoBehaviour
    {
        [Header("Spawn Offsets")]
        [SerializeField] private Vector3 hostSpawnPosition = new Vector3(-3f, 1f, 0f);
        [SerializeField] private Vector3 clientSpawnPosition = new Vector3(3f, 1f, 0f);

        private void Start()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            }
        }

        private void OnDestroy()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            }
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (!NetworkManager.Singleton.IsServer) return;

            // Find the player object for this client
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var clientData))
            {
                var playerObj = clientData.PlayerObject;
                if (playerObj != null)
                {
                    Vector3 spawnPos = (clientId == NetworkManager.ServerClientId) ? hostSpawnPosition : clientSpawnPosition;
                    Quaternion spawnRot = Quaternion.LookRotation((Vector3.zero - spawnPos).normalized, Vector3.up);

                    var cc = playerObj.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;

                    playerObj.transform.position = spawnPos;
                    playerObj.transform.rotation = spawnRot;

                    if (cc != null) cc.enabled = true;
                }
            }
        }
    }
}
