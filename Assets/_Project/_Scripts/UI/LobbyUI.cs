using System;
using SliceIt.Networking;
using SliceIt.Player;
using SliceIt.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SliceIt.UI
{
    public enum MenuScreen
    {
        MainMenu = 0,
        MultiplayerLobby = 1
    }

    /// <summary>
    /// Manages the full menu flow for Slice It!:
    /// - Main Menu (Play, Exit)
    /// - Multiplayer Lobby (Local Host/Client, Relay Host/Client, Navigation)
    /// - In-Game Escape Menu (Esc key toggle, Resume, Restart, Leave to Main Menu, Exit Game)
    /// </summary>
    public class LobbyUI : MonoBehaviour
    {
        public static LobbyUI Instance { get; private set; }

        [Header("Local Connection")]
        [SerializeField] private string targetIp = "127.0.0.1";
        [SerializeField] private string targetPort = "7777";

        [Header("Relay Connection")]
        [SerializeField] private string relayJoinCode = string.Empty;

        public bool IsEscapeMenuOpen { get; private set; } = false;

        private MenuScreen currentScreen = MenuScreen.MainMenu;
        private NetworkSessionManager sessionManager;
        private string statusMessage = "Ready";
        private int pingCount = 0;
        private string lastPinger = "None";
        private Texture2D bgTex;
        private GUIStyle titleStyle;
        private GUIStyle subTitleStyle;
        private GUIStyle buttonStyle;
        private GUIStyle headerStyle;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            bgTex = new Texture2D(1, 1);
            bgTex.SetPixel(0, 0, new Color(0.08f, 0.08f, 0.12f, 0.94f));
            bgTex.Apply();
        }

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

            if (bgTex != null) Destroy(bgTex);
        }

        private void Update()
        {
            bool isConnected = NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer);

            var kb = Keyboard.current;
            if (isConnected && kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                ToggleEscapeMenu();
            }
        }

        public void ToggleEscapeMenu()
        {
            IsEscapeMenuOpen = !IsEscapeMenuOpen;
            if (IsEscapeMenuOpen)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        public void CloseEscapeMenu()
        {
            IsEscapeMenuOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void HandleStatusChanged(string status) => statusMessage = status;
        private void HandleRelayCodeGenerated(string code) => relayJoinCode = code;
        private void HandleDisconnected()
        {
            statusMessage = "Disconnected from session.";
            currentScreen = MenuScreen.MainMenu;
            IsEscapeMenuOpen = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void HandlePingUpdated(int count, string pinger)
        {
            pingCount = count;
            lastPinger = pinger;
        }

        private void InitStyles()
        {
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 28,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            if (subTitleStyle == null)
            {
                subTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 13,
                    fontStyle = FontStyle.Italic,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            if (headerStyle == null)
            {
                headerStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 16,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            if (buttonStyle == null)
            {
                buttonStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 14,
                    fontStyle = FontStyle.Bold
                };
            }
        }

        private void OnGUI()
        {
            InitStyles();

            bool isConnected = NetworkManager.Singleton != null && (NetworkManager.Singleton.IsClient || NetworkManager.Singleton.IsServer);

            if (!isConnected)
            {
                // Unconnected view: Main Menu or Multiplayer Lobby
                if (currentScreen == MenuScreen.MainMenu)
                {
                    DrawMainMenu();
                }
                else
                {
                    DrawLobbyScreen();
                }
            }
            else
            {
                // In-Game view: Escape menu when pressed
                if (IsEscapeMenuOpen)
                {
                    DrawInGameEscapeMenu();
                }
            }
        }

        #region Screen 1: Main Menu

        private void DrawMainMenu()
        {
            float w = 460f;
            float h = 330f;
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            GUI.DrawTexture(new Rect(x, y, w, h), bgTex);
            GUI.Box(new Rect(x, y, w, h), string.Empty);

            GUILayout.BeginArea(new Rect(x + 20, y + 25, w - 40, h - 50));

            // Title
            GUI.color = new Color(0.2f, 0.9f, 1f);
            GUILayout.Label("SLICE IT!", titleStyle);
            GUI.color = Color.white;
            GUILayout.Label("2-Player Co-op Low-Poly Slasher", subTitleStyle);

            GUILayout.Space(25);

            // Play Button
            GUI.color = new Color(0.2f, 0.85f, 0.35f);
            if (GUILayout.Button("PLAY (MULTIPLAYER)", buttonStyle, GUILayout.Height(48)))
            {
                currentScreen = MenuScreen.MultiplayerLobby;
            }
            GUI.color = Color.white;

            GUILayout.Space(12);

            // Exit Button
            GUI.color = new Color(0.85f, 0.3f, 0.3f);
            if (GUILayout.Button("EXIT GAME", buttonStyle, GUILayout.Height(42)))
            {
                ExitGame();
            }
            GUI.color = Color.white;

            GUILayout.EndArea();
        }

        #endregion

        #region Screen 2: Multiplayer Lobby

        private void DrawLobbyScreen()
        {
            float w = 480f;
            float h = 470f;
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            GUI.DrawTexture(new Rect(x, y, w, h), bgTex);
            GUI.Box(new Rect(x, y, w, h), string.Empty);

            GUILayout.BeginArea(new Rect(x + 20, y + 20, w - 40, h - 35));

            GUI.color = Color.cyan;
            GUILayout.Label("MULTIPLAYER LOBBY", headerStyle);
            GUI.color = Color.white;

            // Status message
            GUI.color = Color.yellow;
            GUILayout.Label($"Status: {statusMessage}", subTitleStyle);
            GUI.color = Color.white;

            GUILayout.Space(12);

            // Local Connection Group
            GUILayout.Label("<b>Local / LAN Setup (Zero Setup)</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label("IP:", GUILayout.Width(25));
            targetIp = GUILayout.TextField(targetIp, GUILayout.Width(170));
            GUILayout.Label("Port:", GUILayout.Width(35));
            targetPort = GUILayout.TextField(targetPort, GUILayout.Width(70));
            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host Local (127.0.0.1)", GUILayout.Height(34)))
            {
                ushort.TryParse(targetPort, out ushort port);
                sessionManager.StartLocalHost(port);
            }
            if (GUILayout.Button("Join Local", GUILayout.Height(34)))
            {
                ushort.TryParse(targetPort, out ushort port);
                sessionManager.StartLocalClient(targetIp, port);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(16);

            // Unity Relay Group
            GUILayout.Label("<b>Unity Relay (Cross-Country / No VPN)</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Join Code:", GUILayout.Width(75));
            relayJoinCode = GUILayout.TextField(relayJoinCode.ToUpper());
            GUILayout.EndHorizontal();

            GUILayout.Space(5);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Host via Relay", GUILayout.Height(34)))
            {
                _ = sessionManager.StartRelayHostAsync(1);
            }
            if (GUILayout.Button("Join via Relay", GUILayout.Height(34)))
            {
                _ = sessionManager.StartRelayClientAsync(relayJoinCode);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(25);

            // Navigation: Back to Main Menu
            GUI.color = new Color(0.7f, 0.7f, 0.8f);
            if (GUILayout.Button("< Back to Main Menu", buttonStyle, GUILayout.Height(36)))
            {
                currentScreen = MenuScreen.MainMenu;
            }
            GUI.color = Color.white;

            GUILayout.EndArea();
        }

        #endregion

        #region Screen 3: In-Game Escape Menu

        private void DrawInGameEscapeMenu()
        {
            float w = 420f;
            float h = 330f;
            float x = (Screen.width - w) * 0.5f;
            float y = (Screen.height - h) * 0.5f;

            GUI.DrawTexture(new Rect(x, y, w, h), bgTex);
            GUI.Box(new Rect(x, y, w, h), string.Empty);

            GUILayout.BeginArea(new Rect(x + 25, y + 25, w - 50, h - 45));

            GUI.color = Color.white;
            GUILayout.Label("GAME MENU", titleStyle);
            GUI.color = Color.gray;
            GUILayout.Label("(Multiplayer active — game is not paused)", subTitleStyle);
            GUI.color = Color.white;

            GUILayout.Space(15);

            // Resume Button
            GUI.color = new Color(0.2f, 0.85f, 0.35f);
            if (GUILayout.Button("Resume Game", buttonStyle, GUILayout.Height(38)))
            {
                CloseEscapeMenu();
            }
            GUI.color = Color.white;

            GUILayout.Space(8);

            // Restart Level Button
            GUI.color = new Color(0.95f, 0.75f, 0.2f);
            if (GUILayout.Button("Restart Level", buttonStyle, GUILayout.Height(38)))
            {
                CloseEscapeMenu();
                var localHealth = FindLocalPlayerHealth();
                if (localHealth != null)
                {
                    localHealth.RequestRestartServerRpc();
                }
                else if (ArenaProgressionManager.Instance != null && NetworkManager.Singleton.IsServer)
                {
                    ArenaProgressionManager.Instance.RestartMatchServer();
                }
            }
            GUI.color = Color.white;

            GUILayout.Space(8);

            // Leave to Main Menu Button
            GUI.color = new Color(0.85f, 0.35f, 0.35f);
            if (GUILayout.Button("Leave to Main Menu", buttonStyle, GUILayout.Height(38)))
            {
                sessionManager?.DisconnectSession();
                currentScreen = MenuScreen.MainMenu;
                CloseEscapeMenu();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            GUI.color = Color.white;

            GUILayout.Space(8);

            // Exit Game Button
            if (GUILayout.Button("Exit Game", buttonStyle, GUILayout.Height(34)))
            {
                ExitGame();
            }

            GUILayout.EndArea();
        }

        private PlayerHealth FindLocalPlayerHealth()
        {
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.LocalClient == null) return null;
            var playerObj = NetworkManager.Singleton.LocalClient.PlayerObject;
            if (playerObj != null)
            {
                return playerObj.GetComponent<PlayerHealth>();
            }
            return null;
        }

        #endregion

        private void ExitGame()
        {
            Debug.Log("[LobbyUI] Exiting application...");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
