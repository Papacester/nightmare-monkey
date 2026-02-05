using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.UI;
using MelonLoader;
using System;
using UnityEngine.SceneManagement;

namespace Narcopelago
{
    /// <summary>
    /// Handles Archipelago disconnection detection and forces save + exit to main menu.
    /// This prevents players from continuing to play while disconnected, which would cause missed checks.
    /// </summary>
    public static class NarcopelagoDisconnect
    {
        /// <summary>
        /// Tracks if we're in a game scene.
        /// </summary>
        private static bool _inGameScene = false;

        /// <summary>
        /// Tracks if we've confirmed a stable connection while in-game.
        /// Only starts checking for disconnects after this is true.
        /// </summary>
        private static bool _confirmedConnectedInGame = false;

        /// <summary>
        /// Tracks if we're currently handling a disconnect (to prevent multiple triggers).
        /// </summary>
        private static bool _handlingDisconnect = false;

        /// <summary>
        /// Frame counter for periodic connection checks.
        /// </summary>
        private static int _checkFrameCounter = 0;

        /// <summary>
        /// How often to check connection status (in frames). ~1 second at 60fps.
        /// </summary>
        private const int CHECK_INTERVAL_FRAMES = 60;

        /// <summary>
        /// Delay before starting to check for disconnects after entering game scene.
        /// Allows time for the game to fully load.
        /// </summary>
        private static int _initialDelayFrames = 0;

        /// <summary>
        /// Sets whether we're in a game scene.
        /// </summary>
        public static void SetInGameScene(bool inGame)
        {
            bool wasInGame = _inGameScene;
            _inGameScene = inGame;
            
            if (inGame)
            {
                // Entering game scene - reset state and set initial delay
                _handlingDisconnect = false;
                _checkFrameCounter = 0;
                _confirmedConnectedInGame = false;
                _initialDelayFrames = 300; // ~5 seconds delay before checking
                
                MelonLogger.Msg("[Disconnect] Entered game scene - will start monitoring after delay");
            }
            else if (wasInGame)
            {
                // Leaving game scene - reset tracking
                // Don't trigger disconnect when intentionally leaving
                _confirmedConnectedInGame = false;
                MelonLogger.Msg("[Disconnect] Left game scene - stopping disconnect monitoring");
            }
        }

        /// <summary>
        /// Process connection checks on the main thread.
        /// Call this from Core.OnUpdate().
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            // Only check while in a game scene
            if (!_inGameScene)
                return;

            // Don't check if already handling disconnect
            if (_handlingDisconnect)
                return;

            // Wait for initial delay after entering game scene
            if (_initialDelayFrames > 0)
            {
                _initialDelayFrames--;
                return;
            }

            // Only check periodically to avoid performance impact
            _checkFrameCounter++;
            if (_checkFrameCounter < CHECK_INTERVAL_FRAMES)
                return;

            _checkFrameCounter = 0;

            // Check connection status
            bool isConnected = IsConnected();

            // If we haven't confirmed a connection yet, check if we're connected now
            if (!_confirmedConnectedInGame)
            {
                if (isConnected)
                {
                    _confirmedConnectedInGame = true;
                    MelonLogger.Msg("[Disconnect] Confirmed connected while in-game - now monitoring for disconnects");
                }
                // Not connected yet, keep waiting (player might not have connected yet)
                return;
            }

            // We were confirmed connected - check if we've disconnected
            if (!isConnected)
            {
                MelonLogger.Warning("[Disconnect] Connection lost! Forcing save and exit to main menu...");
                HandleDisconnect();
            }
        }

        /// <summary>
        /// Checks if we're currently connected to Archipelago.
        /// </summary>
        private static bool IsConnected()
        {
            var session = ConnectionHandler.CurrentSession;
            if (session == null)
                return false;

            try
            {
                return session.Socket?.Connected ?? false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Handles disconnection by saving and exiting to main menu.
        /// </summary>
        private static void HandleDisconnect()
        {
            _handlingDisconnect = true;

            try
            {
                // Show notification to user
                ShowDisconnectNotification();

                // Save the game
                SaveGame();

                // Exit to main menu after a short delay to allow save to complete
                MelonLoader.MelonCoroutines.Start(ExitToMainMenuCoroutine());
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Disconnect] Error handling disconnect: {ex.Message}");
                // Still try to exit even if something fails
                MelonLoader.MelonCoroutines.Start(ExitToMainMenuCoroutine());
            }
        }

        /// <summary>
        /// Shows a notification to the user about the disconnect.
        /// </summary>
        private static void ShowDisconnectNotification()
        {
            try
            {
                if (Singleton<NotificationsManager>.InstanceExists)
                {
                    var notifManager = Singleton<NotificationsManager>.Instance;
                    notifManager?.SendNotification(
                        "Archipelago Disconnected",
                        "Connection lost! Saving and returning to main menu...",
                        null,
                        10f,
                        true
                    );
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Disconnect] Error showing notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Saves the current game.
        /// </summary>
        private static void SaveGame()
        {
            try
            {
                if (Singleton<SaveManager>.InstanceExists)
                {
                    var saveManager = Singleton<SaveManager>.Instance;
                    if (saveManager != null)
                    {
                        MelonLogger.Msg("[Disconnect] Triggering save...");
                        saveManager.Save();
                        MelonLogger.Msg("[Disconnect] Save triggered");
                    }
                    else
                    {
                        MelonLogger.Warning("[Disconnect] SaveManager instance is null");
                    }
                }
                else
                {
                    MelonLogger.Warning("[Disconnect] SaveManager not available");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Disconnect] Error saving game: {ex.Message}");
            }
        }

        /// <summary>
        /// Coroutine to exit to main menu after a delay.
        /// </summary>
        private static System.Collections.IEnumerator ExitToMainMenuCoroutine()
        {
            // Wait a bit for save to complete
            for (int i = 0; i < 180; i++) // ~3 seconds at 60fps
            {
                yield return null;
            }

            MelonLogger.Msg("[Disconnect] Exiting to main menu...");

            try
            {
                // Try using the game's built-in method to return to menu
                if (Singleton<LoadManager>.InstanceExists)
                {
                    var loadManager = Singleton<LoadManager>.Instance;
                    if (loadManager != null)
                    {
                        loadManager.ExitToMenu();
                        MelonLogger.Msg("[Disconnect] ExitToMenu called");
                        yield break;
                    }
                }

                // Fallback: Load the menu scene directly
                MelonLogger.Msg("[Disconnect] Falling back to direct scene load");
                SceneManager.LoadScene("Menu");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Disconnect] Error exiting to menu: {ex.Message}");
                // Last resort fallback
                try
                {
                    SceneManager.LoadScene("Menu");
                }
                catch
                {
                    MelonLogger.Error("[Disconnect] Failed to exit to menu");
                }
            }
        }

        /// <summary>
        /// Resets the disconnect handler state.
        /// </summary>
        public static void Reset()
        {
            _inGameScene = false;
            _confirmedConnectedInGame = false;
            _handlingDisconnect = false;
            _checkFrameCounter = 0;
            _initialDelayFrames = 0;
        }
    }
}
