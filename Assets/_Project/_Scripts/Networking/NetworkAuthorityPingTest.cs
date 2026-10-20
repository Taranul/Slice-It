using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace SliceIt.Networking
{
    /// <summary>
    /// Verifies server-authoritative state synchronization and network events across host and client.
    /// Fulfills Stage 1 verification requirement in GDD.
    /// </summary>
    public class NetworkAuthorityPingTest : NetworkBehaviour
    {
        public static NetworkAuthorityPingTest Instance { get; private set; }

        public NetworkVariable<int> PingCount { get; } = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public NetworkVariable<FixedString64Bytes> LastPinger { get; } = new NetworkVariable<FixedString64Bytes>(
            "None",
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        public event Action<int, string> OnPingUpdated;

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
            PingCount.OnValueChanged += HandlePingCountChanged;
            LastPinger.OnValueChanged += HandleLastPingerChanged;
        }

        public override void OnNetworkDespawn()
        {
            PingCount.OnValueChanged -= HandlePingCountChanged;
            LastPinger.OnValueChanged -= HandleLastPingerChanged;
        }

        private void HandlePingCountChanged(int previous, int current)
        {
            OnPingUpdated?.Invoke(current, LastPinger.Value.ToString());
        }

        private void HandleLastPingerChanged(FixedString64Bytes previous, FixedString64Bytes current)
        {
            OnPingUpdated?.Invoke(PingCount.Value, current.ToString());
        }

        /// <summary>
        /// Callable by any client or host to request an authoritative event.
        /// </summary>
        public void SendAuthoritativePing()
        {
            if (!IsSpawned) return;

            if (IsServer)
            {
                ExecuteAuthoritativePingServer(NetworkManager.Singleton.LocalClientId);
            }
            else
            {
                RequestPingServerRpc();
            }
        }

        [Rpc(SendTo.Server)]
        private void RequestPingServerRpc(RpcParams rpcParams = default)
        {
            ulong senderId = rpcParams.Receive.SenderClientId;
            ExecuteAuthoritativePingServer(senderId);
        }

        private void ExecuteAuthoritativePingServer(ulong senderId)
        {
            PingCount.Value += 1;
            string senderDesc = senderId == NetworkManager.ServerClientId ? "Host (Player 1)" : $"Client {senderId} (Player 2)";
            LastPinger.Value = new FixedString64Bytes(senderDesc);

            NotifyPingClientRpc(senderId, PingCount.Value);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NotifyPingClientRpc(ulong senderId, int newCount)
        {
            Debug.Log($"[Authoritative Network Event] Ping #{newCount} from sender {senderId} synchronized to local client {NetworkManager.Singleton.LocalClientId}!");
        }
    }
}
