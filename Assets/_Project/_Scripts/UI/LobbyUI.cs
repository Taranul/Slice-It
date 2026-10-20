using System;
using SliceIt.Networking;
using Unity.Netcode;
using UnityEngine;

namespace SliceIt.UI
{
    /// <summary>
    /// Interactive Lobby and Network Verification HUD.
    /// Works immediately out of the box for testing 2-player host-client loop,
    /// Relay join codes, local IP, authoritative pings, and disconnects.
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        [Header("Local Connection")]
        [SerializeField] private string targetIp = "127.0.0.1";
        [SerializeField] private string targetPort = "7777";

        [Header("Relay Connection")]
        [SerializeField] private string relayJoinCode = string.Empty;

        private NetworkSessionManager sessionManager;
        private string statusMessage = "Ready";
        private int pingCount = 0;
        private string lastPinger = "None";
        private Vector2 scrollPos;

        private void Start()
        {
            sessionManager = NetworkSessionManager.Instance;
            if (sessionManager != null)
            {
                sessionManager.OnStatusChanged += HandleStatusChanged;
                sessionManager.OnRelayCodeGenerated += HandleRelayCodeGenerated;
                sessionManager.OnDisconnected += HandleDisconnected;
                statusMessage = sessionManager.CurrentStatus;
            }

            if (NetworkAuthorityPingTest.Instance != null)
            {
                NetworkAuthorityPingTest.Instance.OnPingUpdated += HandlePingUpdated;
            }
        }

        private void OnDestroy()
        {
            if (sessionManager != null)
            {
                sessionManager.OnStatusChanged -= HandleStatusChanged;
                sessionManager.OnRelayCodeGenerated -= HandleRelayCodeGenerated;
                sessionManager.OnDisconnected -= HandleDisconnected;
            }

            if (NetworkAuthorityPingTest.Instance != null)
            {
                NetworkAuthorityPingTest.Instance.OnPingUpdated -= HandlePingUpdated;
            }
        }

        private void HandleStatusChanged(string status) => statusMessage = status;
        private void HandleRelayCodeGenerated(string code) => relayJoinCode = code;
        private void HandleDisconnected() => statusMessage = "Disconnected from session.";
        private void HandlePingUpdated(int count, string pinger)
        {
            pingCount = count;
            lastPinger = pinger;
        }

        private void OnGUI()
        {
            // Subscribe lazily to ping test once spawned
            if (NetworkAuthorityPingTest.Instance != null)
            {
                NetworkAuthorityPingTest.Instance.OnPingUpdated -= HandlePingUpdated;
                NetworkAuthorityPingTest.Instance.OnPingUpdated += HandlePingUpdated;
            }

            bool isConnected = NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer);

            // Styling
            GUI.skin.box.fontSize = 13;
            GUI.skin.label.fontSize = 13;
            GUI.skin.button.fontSize = 13;
            GUI.skin.textField.fontSize = 13;

            int panelWidth = 360;
            int panelHeight = isConnected ? 340 : 420;
            Rect panelRect = new Rect(20, 20, panelWidth, panelHeight);

            GUI.Box(panelRect, isConnected ? "SLICE IT! — ACTIVE SESSION" : "SLICE IT! — MULTIPLAYER LOBBY");
            GUILayout.BeginArea(new Rect(panelRect.x + 15, panelRect.y + 30, panelRect.width - 30, panelRect.height - 40));

            // Status banner
            GUI.color = isConnected ? Color.green : Color.yellow;
            GUILayout.Label($"Status: {statusMessage}");
            GUI.color = Color.white;

            // Active Pair Window Status
            if (SliceIt.Progression.PairManager.Instance != null && SliceIt.Progression.PairManager.Instance.PairWindowRemaining.Value > 0f)
            {
                float rem = SliceIt.Progression.PairManager.Instance.PairWindowRemaining.Value;
                int pid = SliceIt.Progression.PairManager.Instance.ActivePairId.Value;
                GUILayout.Space(5);
                GUI.color = new Color(1f, 0.3f, 0.3f);
                GUILayout.Box($"<b>>>> PAIR {pid} WINDOW: {rem:F1}s <<<</b>");
                GUI.color = Color.white;
            }

            GUILayout.Space(10);

            if (!isConnected)
            {
                // Local Connection Group
                GUILayout.Label("<b>--- Local / LAN (Zero Setup) ---</b>");
                GUILayout.BeginHorizontal();
                GUILayout.Label("IP:", GUILayout.Width(25));
                targetIp = GUILayout.TextField(targetIp, GUILayout.Width(130));
                GUILayout.Label("Port:", GUILayout.Width(35));
                targetPort = GUILayout.TextField(targetPort, GUILayout.Width(60));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Host Local (127.0.0.1)", GUILayout.Height(32)))
                {
                    ushort.TryParse(targetPort, out ushort port);
                    sessionManager.StartLocalHost(port);
                }
                if (GUILayout.Button("Join Local", GUILayout.Height(32)))
                {
                    ushort.TryParse(targetPort, out ushort port);
                    sessionManager.StartLocalClient(targetIp, port);
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(15);

                // Relay Group
                GUILayout.Label("<b>--- Unity Relay (Cross-Country / No VPN) ---</b>");
                GUILayout.BeginHorizontal();
                GUILayout.Label("Join Code:", GUILayout.Width(75));
                relayJoinCode = GUILayout.TextField(relayJoinCode.ToUpper());
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Host via Relay", GUILayout.Height(32)))
                {
                    _ = sessionManager.StartRelayHostAsync(1);
                }
                if (GUILayout.Button("Join via Relay", GUILayout.Height(32)))
                {
                    _ = sessionManager.StartRelayClientAsync(relayJoinCode);
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                // Connected state
                string role = NetworkManager.Singleton.IsHost ? "HOST (Player 1)" : "CLIENT (Player 2)";
                GUILayout.Label($"Role: <b>{role}</b>");
                GUILayout.Label($"Players Connected: <b>{NetworkManager.Singleton.ConnectedClientsList.Count}/2</b>");

                if (!string.IsNullOrEmpty(sessionManager.ActiveRelayCode))
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"Relay Code: <b>{sessionManager.ActiveRelayCode}</b>");
                    if (GUILayout.Button("Copy", GUILayout.Width(60)))
                    {
                        GUIUtility.systemCopyBuffer = sessionManager.ActiveRelayCode;
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(10);

                // Authoritative Event Test
                GUILayout.Label("<b>--- Authoritative Network Event Test ---</b>");
                GUILayout.Label($"Total Pings: <b>{pingCount}</b> (Last: {lastPinger})");
                if (GUILayout.Button("Send Authoritative Ping", GUILayout.Height(32)))
                {
                    if (NetworkAuthorityPingTest.Instance != null)
                    {
                        NetworkAuthorityPingTest.Instance.SendAuthoritativePing();
                    }
                }

                GUILayout.Space(15);

                // Disconnect
                GUI.color = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("Disconnect / Leave Session", GUILayout.Height(32)))
                {
                    sessionManager.DisconnectSession();
                }
                GUI.color = Color.white;
            }

            GUILayout.EndArea();
        }
    }
}
