using System;
using SliceIt.Combat;
using SliceIt.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SliceIt.Buffs
{
    public enum InRunBuffType
    {
        None,
        ExtendedReach,   // Blade reach +25%
        SwiftDash,       // Dash cooldown -35%
        TitanHook,       // Grapple speed +40%
        FortifiedGuard,  // Shield block arc widened
        AdrenalineRush   // Locomotion speed +25%
    }

    /// <summary>
    /// Temporary, modular in-run buff system per GDD Section 6.
    /// Allows each player to select enhancements that last for the duration of the current run.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class InRunBuffManager : MonoBehaviour
    {
        public InRunBuffType ActiveBuff { get; private set; } = InRunBuffType.None;
        public bool IsBuffMenuOpen { get; set; } = false;

        private PlayerController playerController;
        private PlayerAbilities playerAbilities;
        private BladeSlashExecutor slashExecutor;

        private void Awake()
        {
            playerController = GetComponent<PlayerController>();
            playerAbilities = GetComponent<PlayerAbilities>();
            slashExecutor = GetComponent<BladeSlashExecutor>();
        }

        private void Update()
        {
            if (playerController != null && !playerController.IsOwner) return;

            var kb = Keyboard.current;
            if (kb != null && kb.bKey.wasPressedThisFrame)
            {
                IsBuffMenuOpen = !IsBuffMenuOpen;
                if (IsBuffMenuOpen)
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
        }

        public void ApplyBuff(InRunBuffType buff)
        {
            ActiveBuff = buff;
            IsBuffMenuOpen = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            Debug.Log($"[InRunBuffManager] Player selected in-run buff: {buff}");
        }

        private void OnGUI()
        {
            if (playerController != null && !playerController.IsOwner) return;
            if (!IsBuffMenuOpen) return;

            int menuWidth = 420;
            int menuHeight = 310;
            Rect menuRect = new Rect((Screen.width - menuWidth) * 0.5f, (Screen.height - menuHeight) * 0.5f, menuWidth, menuHeight);

            GUI.Box(menuRect, "<b>SLICE IT! — CHOOSE IN-RUN BUFF (Press B to close)</b>");
            GUILayout.BeginArea(new Rect(menuRect.x + 20, menuRect.y + 35, menuRect.width - 40, menuRect.height - 50));

            GUILayout.Label("Select an individual enhancement for this run (Temporary prototype):");
            GUILayout.Space(8);

            if (GUILayout.Button("1. Extended Blade (Reach +25% to 3.5m)", GUILayout.Height(36)))
            {
                ApplyBuff(InRunBuffType.ExtendedReach);
            }

            if (GUILayout.Button("2. Swift Dash (Dash cooldown reduced to 1.6s)", GUILayout.Height(36)))
            {
                ApplyBuff(InRunBuffType.SwiftDash);
            }

            if (GUILayout.Button("3. Titan Grapple (Hook pull velocity +40%)", GUILayout.Height(36)))
            {
                ApplyBuff(InRunBuffType.TitanHook);
            }

            if (GUILayout.Button("4. Adrenaline Rush (Movement & Sprint +25%)", GUILayout.Height(36)))
            {
                ApplyBuff(InRunBuffType.AdrenalineRush);
            }

            GUILayout.Space(6);
            if (GUILayout.Button("Close Menu", GUILayout.Height(28)))
            {
                IsBuffMenuOpen = false;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            GUILayout.EndArea();
        }
    }
}
