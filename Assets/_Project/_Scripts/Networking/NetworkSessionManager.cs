using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

namespace SliceIt.Networking
{
    /// <summary>
    /// Manages network session lifecycle, supporting both local direct IP (loopback/LAN)
    /// and Unity Relay for cross-country NAT-punchthrough coop without 3rd-party VPNs.
    /// </summary>
    public class NetworkSessionManager : MonoBehaviour
    {
        public static NetworkSessionManager Instance { get; private set; }

        [Header("Local Connection Settings")]
        [SerializeField] private string defaultLocalIp = "127.0.0.1";
        [SerializeField] private ushort defaultPort = 7777;

        public event Action<string> OnStatusChanged;
        public event Action<string> OnRelayCodeGenerated;
        public event Action OnDisconnected;

        public string CurrentStatus { get; private set; } = "Disconnected";
        public string ActiveRelayCode { get; private set; } = string.Empty;
        public bool IsRelayAvailable { get; private set; } = false;

        private UnityTransport unityTransport;
        private bool isInitializingServices = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            EnsureTransport();
            RegisterNetworkCallbacks();
            TryInitializeUnityServices();
        }

        private void OnDestroy()
        {
            UnregisterNetworkCallbacks();
        }

        private void EnsureTransport()
        {
            if (NetworkManager.Singleton != null)
            {
                unityTransport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            }
        }

        private void RegisterNetworkCallbacks()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;
            }
        }

        private void UnregisterNetworkCallbacks()
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        private async void TryInitializeUnityServices()
        {
            if (isInitializingServices) return;
            isInitializingServices = true;

            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    await UnityServices.InitializeAsync();
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                IsRelayAvailable = true;
                UpdateStatus("Unity Services Initialized. Relay is ready.");
            }
            catch (Exception ex)
            {
                IsRelayAvailable = false;
                UpdateStatus($"Unity Cloud Services not linked (Local IP available): {ex.Message}");
            }
            finally
            {
                isInitializingServices = false;
            }
        }

        #region Local Connection (Zero Setup / Rapid Iteration)

        public bool StartLocalHost(ushort port = 0)
        {
            EnsureTransport();
            if (NetworkManager.Singleton == null || unityTransport == null) return false;

            ushort p = port > 0 ? port : defaultPort;
            unityTransport.SetConnectionData(defaultLocalIp, p, "0.0.0.0");

            bool started = NetworkManager.Singleton.StartHost();
            if (started)
            {
                ActiveRelayCode = string.Empty;
                UpdateStatus($"Hosting locally on port {p}. Waiting for player 2...");
            }
            else
            {
                UpdateStatus("Failed to start local host.");
            }

            return started;
        }

        public bool StartLocalClient(string ipAddress = null, ushort port = 0)
        {
            EnsureTransport();
            if (NetworkManager.Singleton == null || unityTransport == null) return false;

            string targetIp = string.IsNullOrWhiteSpace(ipAddress) ? defaultLocalIp : ipAddress.Trim();
            ushort p = port > 0 ? port : defaultPort;

            unityTransport.SetConnectionData(targetIp, p);

            bool started = NetworkManager.Singleton.StartClient();
            if (started)
            {
                ActiveRelayCode = string.Empty;
                UpdateStatus($"Connecting to local host at {targetIp}:{p}...");
            }
            else
            {
                UpdateStatus("Failed to start local client.");
            }

            return started;
        }

        #endregion

        #region Unity Relay Connection (Cross-Country / No VPN)

        public async Task<string> StartRelayHostAsync(int maxConnections = 1)
        {
            EnsureTransport();
            if (NetworkManager.Singleton == null || unityTransport == null)
            {
                UpdateStatus("NetworkManager or UnityTransport missing.");
                return null;
            }

            if (!IsRelayAvailable)
            {
                await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }
                IsRelayAvailable = true;
            }

            try
            {
                UpdateStatus("Creating Relay allocation...");
                Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
                string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

                unityTransport.SetHostRelayData(
                    allocation.RelayServer.IpV4,
                    (ushort)allocation.RelayServer.Port,
                    allocation.AllocationIdBytes,
                    allocation.Key,
                    allocation.ConnectionData
                );

                bool started = NetworkManager.Singleton.StartHost();
                if (started)
                {
                    ActiveRelayCode = joinCode;
                    OnRelayCodeGenerated?.Invoke(joinCode);
                    UpdateStatus($"Relay Host started. Join Code: {joinCode}");
                    return joinCode;
                }
                else
                {
                    UpdateStatus("Failed to start Host with Relay data.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Relay Host error: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> StartRelayClientAsync(string joinCode)
        {
            EnsureTransport();
            if (NetworkManager.Singleton == null || unityTransport == null)
            {
                UpdateStatus("NetworkManager or UnityTransport missing.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(joinCode))
            {
                UpdateStatus("Join Code cannot be empty.");
                return false;
            }

            if (!IsRelayAvailable)
            {
                await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }
                IsRelayAvailable = true;
            }

            try
            {
                string cleanCode = joinCode.Trim();
                UpdateStatus($"Joining Relay allocation with code {cleanCode}...");

                JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(cleanCode);

                unityTransport.SetClientRelayData(
                    joinAllocation.RelayServer.IpV4,
                    (ushort)joinAllocation.RelayServer.Port,
                    joinAllocation.AllocationIdBytes,
                    joinAllocation.Key,
                    joinAllocation.ConnectionData,
                    joinAllocation.HostConnectionData
                );

                bool started = NetworkManager.Singleton.StartClient();
                if (started)
                {
                    ActiveRelayCode = cleanCode;
                    UpdateStatus($"Relay Client connected with code {cleanCode}.");
                    return true;
                }
                else
                {
                    UpdateStatus("Failed to start Client with Relay data.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Relay Join error: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Session Management & Disconnect

        public void DisconnectSession()
        {
            if (NetworkManager.Singleton == null) return;

            if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient)
            {
                NetworkManager.Singleton.Shutdown();
            }

            ActiveRelayCode = string.Empty;
            UpdateStatus("Session disconnected.");
            OnDisconnected?.Invoke();
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (NetworkManager.Singleton.IsServer)
            {
                UpdateStatus($"Client {clientId} connected to session. Connected players: {NetworkManager.Singleton.ConnectedClientsList.Count}/2");
            }
            else
            {
                UpdateStatus("Successfully connected to host!");
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (NetworkManager.Singleton == null) return;

            if (NetworkManager.Singleton.IsServer)
            {
                UpdateStatus($"Client {clientId} disconnected.");
            }
            else
            {
                // If local client disconnected, the host likely shut down
                UpdateStatus("Host disconnected or connection lost.");
                OnDisconnected?.Invoke();
            }
        }

        private void UpdateStatus(string message)
        {
            CurrentStatus = message;
            Debug.Log($"[NetworkSessionManager] {message}");
            OnStatusChanged?.Invoke(message);
        }

        #endregion
    }
}
