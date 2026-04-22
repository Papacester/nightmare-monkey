using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Packets;
using HarmonyLib;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.PlayerScripts.Health;
using Il2CppScheduleOne.UI;
using MelonLoader;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Narcopelago
{
    /// <summary>
    /// Handles DeathLink functionality for Schedule I.
    /// DeathLink allows players to share deaths across different games in the multiworld.
    /// 
    /// When enabled:
    /// - Sends a DeathLink when the player dies or is arrested
    /// - Arrests the player when a DeathLink is received from another player
    /// </summary>
    public static class NarcopelagoDeathLink
    {
        // ============================================================
        // DEBUG SETTINGS - Set to false to disable debug key
        // ============================================================
        /// <summary>
        /// TEMPORARY DEBUG: Enable/disable the debug key for testing DeathLink.
        /// Set this to FALSE before production release.
        /// Press F9 to simulate receiving a DeathLink.
        /// </summary>
        private const bool ENABLE_DEBUG_KEY = false;
        // ============================================================

        private static DeathLinkService _deathLinkService;
        private static bool _isEnabled = false;
        private static bool _isProcessingReceivedDeath = false;
        private static bool _hookedEvents = false;
        private static double _lastSentDeathTimestamp = 0;

        /// <summary>
        /// Queue of deaths to process on the main thread.
        /// </summary>
        private static ConcurrentQueue<(string source, string cause)> _pendingDeaths = new ConcurrentQueue<(string, string)>();

        /// <summary>
        /// Indicates whether DeathLink is currently enabled.
        /// </summary>
        public static bool IsEnabled => _isEnabled;

        /// <summary>
        /// Enables DeathLink for the current session.
        /// This should be called after a successful connection if DeathLink is enabled in options.
        /// </summary>
        /// <param name="session">The connected Archipelago session.</param>
        public static void Enable(ArchipelagoSession session)
        {
            if (session == null)
            {
                MelonLogger.Warning("[DeathLink] Cannot enable - session is null");
                return;
            }

            if (_isEnabled)
            {
                MelonLogger.Msg("[DeathLink] Already enabled");
                return;
            }

            try
            {
                MelonLogger.Msg("[DeathLink] Creating DeathLink service...");
                
                // Get or create the DeathLink service from the session
                _deathLinkService = session.CreateDeathLinkService();
                
                if (_deathLinkService == null)
                {
                    MelonLogger.Error("[DeathLink] Failed to create DeathLink service - returned null");
                    return;
                }
                
                MelonLogger.Msg("[DeathLink] DeathLink service created, subscribing to events...");
                
                // Subscribe to DeathLink received events
                _deathLinkService.OnDeathLinkReceived += OnDeathLinkReceived;
                
                MelonLogger.Msg("[DeathLink] Enabling DeathLink on server...");
                
                // Enable the service - this tells the server we want to participate in DeathLink
                _deathLinkService.EnableDeathLink();
                
                _isEnabled = true;
                MelonLogger.Msg("[DeathLink] Enabled successfully - ready to send and receive deaths");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeathLink] Failed to enable: {ex.Message}");
                MelonLogger.Error($"[DeathLink] Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Disables DeathLink for the current session.
        /// </summary>
        public static void Disable()
        {
            if (!_isEnabled)
            {
                return;
            }

            try
            {
                if (_deathLinkService != null)
                {
                    _deathLinkService.DisableDeathLink();
                    _deathLinkService.OnDeathLinkReceived -= OnDeathLinkReceived;
                    _deathLinkService = null;
                }
                
                _isEnabled = false;
                MelonLogger.Msg("[DeathLink] Disabled");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeathLink] Failed to disable: {ex.Message}");
            }
        }

        /// <summary>
        /// Hooks into player death and arrest events.
        /// Should be called when entering a game scene.
        /// </summary>
        public static void HookPlayerEvents()
        {
            if (_hookedEvents)
            {
                return;
            }

            try
            {
                // We'll use Harmony patches instead of direct event subscription
                // because the Player.Local instance may not exist at the right time
                _hookedEvents = true;
                MelonLogger.Msg("[DeathLink] Player event hooks ready (via Harmony patches)");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeathLink] Failed to hook player events: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when the local player dies (from Harmony patch).
        /// </summary>
        public static void OnLocalPlayerDied()
        {
            if (!_isEnabled || !NarcopelagoOptions.Deathlink)
            {
                return;
            }

            // Don't send a death if we're currently processing a received death
            if (_isProcessingReceivedDeath)
            {
                MelonLogger.Msg("[DeathLink] Ignoring death caused by received DeathLink");
                return;
            }

            string slotName = ConnectionHandler.LastSlotName ?? "Unknown";
            SendDeath(slotName, "died");
        }

        /// <summary>
        /// Called when the local player is arrested (from Harmony patch).
        /// </summary>
        public static void OnLocalPlayerArrested()
        {
            if (!_isEnabled || !NarcopelagoOptions.Deathlink)
            {
                return;
            }

            // Don't send a death if we're currently processing a received death
            if (_isProcessingReceivedDeath)
            {
                MelonLogger.Msg("[DeathLink] Ignoring arrest caused by received DeathLink");
                return;
            }

            string slotName = ConnectionHandler.LastSlotName ?? "Unknown";
            SendDeath(slotName, "was arrested");
        }

        /// <summary>
        /// Sends a death to other players in the multiworld.
        /// </summary>
        /// <param name="playerName">The name of the player who died.</param>
        /// <param name="cause">Description of what caused the death.</param>
        private static void SendDeath(string playerName, string cause)
        {
            if (!_isEnabled || _deathLinkService == null)
            {
                MelonLogger.Warning("[DeathLink] Cannot send death - service not enabled");
                return;
            }

            // Verify session is still connected
            var session = ConnectionHandler.CurrentSession;
            if (session == null || !session.Socket.Connected)
            {
                MelonLogger.Warning("[DeathLink] Cannot send death - session not connected");
                return;
            }

            try
            {
                string deathMessage = $"{playerName} {cause}";
                
                MelonLogger.Msg($"[DeathLink] Sending death via raw Bounce packet...");
                MelonLogger.Msg($"[DeathLink]   Source: '{playerName}'");
                MelonLogger.Msg($"[DeathLink]   Cause: '{deathMessage}'");
                
                // Calculate timestamp and store it so we can filter out our own death when it bounces back
                var timestamp = DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                _lastSentDeathTimestamp = timestamp;
                
                var bouncePacket = new BouncePacket
                {
                    Tags = new List<string> { "DeathLink" },
                    Data = new Dictionary<string, JToken>
                    {
                        { "time", timestamp },
                        { "source", playerName },
                        { "cause", deathMessage }
                    }
                };
                
                session.Socket.SendPacket(bouncePacket);
                
                MelonLogger.Msg($"[DeathLink] Bounce packet sent with timestamp {timestamp}");
                
                // Notify APContacts about the sent deathlink
                NarcopelagoAPContacts.OnDeathLinkSent(playerName, cause);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeathLink] Failed to send death: {ex.Message}");
                MelonLogger.Error($"[DeathLink] Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Called when a DeathLink is received from another player.
        /// Queues the death to be processed on the main thread.
        /// </summary>
        private static void OnDeathLinkReceived(DeathLink deathLink)
        {
            try
            {
                string source = deathLink.Source ?? "Unknown";
                string cause = deathLink.Cause ?? "died";
                
                // Check if this is our own death bouncing back
                string mySlotName = ConnectionHandler.LastSlotName ?? "";
                if (source.Equals(mySlotName, StringComparison.OrdinalIgnoreCase))
                {
                    MelonLogger.Msg($"[DeathLink] Ignoring our own death bounce from {source}");
                    return;
                }
                
                // Also check timestamp to catch edge cases
                double receivedTimestamp = deathLink.Timestamp.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
                if (Math.Abs(receivedTimestamp - _lastSentDeathTimestamp) < 1.0)
                {
                    MelonLogger.Msg($"[DeathLink] Ignoring death with matching timestamp (likely our own)");
                    return;
                }
                
                MelonLogger.Msg($"[DeathLink] Received death from {source}: {cause}");
                
                // Queue the death to be processed on the main thread
                _pendingDeaths.Enqueue((source, cause));
                
                // Notify APContacts about the received deathlink
                NarcopelagoAPContacts.OnDeathLinkReceived(source, cause);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeathLink] Error processing received death: {ex.Message}");
            }
        }

        /// <summary>
        /// Random instance for selecting death options.
        /// </summary>
        private static readonly System.Random _random = new System.Random();

        /// <summary>
        /// The trap types that can be chosen for the "random_trap" DeathLink option.
        /// Matches: Heat, Slippery, Trash, Pan, TimeScale, Sleep
        /// </summary>
        private static readonly string[] RandomTrapPool = new[]
        {
            "Heat Trap", "Slippery Trap", "Trash Trap", "Pan Trap", "TimeScale Trap", "Sleep Trap"
        };

        /// <summary>
        /// Processes pending deaths on the main thread.
        /// Randomly selects from the enabled DeathLink options for each received death.
        /// Call this from OnUpdate.
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            // Debug key handler (F9 to simulate receiving DeathLink)
            if (ENABLE_DEBUG_KEY)
            {
                HandleDebugKey();
            }

            if (!_isEnabled || !NarcopelagoOptions.Deathlink)
            {
                return;
            }

            while (_pendingDeaths.TryDequeue(out var death))
            {
                try
                {
                    ExecuteDeathLinkConsequence(death.source, death.cause);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[DeathLink] Error processing death consequence: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// TEMPORARY DEBUG METHOD: Handles the debug key to simulate receiving a DeathLink.
        /// Press F9 to trigger a test DeathLink.
        /// Set ENABLE_DEBUG_KEY to false to disable this feature.
        /// </summary>
        private static void HandleDebugKey()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                MelonLogger.Warning("[DeathLink DEBUG] F9 pressed - simulating DeathLink received!");

                // Queue a fake death to be processed
                string debugSource = "DEBUG_PLAYER";
                string debugCause = "pressed F9 for testing";

                _pendingDeaths.Enqueue((debugSource, debugCause));

                // Also notify APContacts like a real death would
                NarcopelagoAPContacts.OnDeathLinkReceived(debugSource, debugCause);

                MelonLogger.Warning("[DeathLink DEBUG] Test DeathLink queued!");
            }
        }

        /// <summary>
        /// Picks a random DeathLink option and executes it.
        /// </summary>
        private static void ExecuteDeathLinkConsequence(string source, string cause)
        {
            var options = NarcopelagoOptions.DeathLink_options;
            if (options == null || options.Count == 0)
            {
                MelonLogger.Warning("[DeathLink] No DeathLink options configured - defaulting to death");
                KillPlayer(source, cause);
                return;
            }

            string chosen = options[_random.Next(options.Count)];
            MelonLogger.Msg($"[DeathLink] Chose consequence '{chosen}' from {options.Count} option(s) (source: {source})");

            switch (chosen.ToLowerInvariant())
            {
                case "sleep trap":
                    ExecuteSleepTrap(source, cause);
                    break;
                case "arrested":
                    ArrestPlayer(source, cause);
                    break;
                case "random trap":
                    ExecuteRandomTrap(source, cause);
                    break;
                case "death":
                    KillPlayer(source, cause);
                    break;
                default:
                    MelonLogger.Warning($"[DeathLink] Unknown option '{chosen}' - defaulting to death");
                    KillPlayer(source, cause);
                    break;
            }
        }

        /// <summary>
        /// Arrests the local player in response to a DeathLink.
        /// </summary>
        private static void ArrestPlayer(string source, string cause)
        {
            var localPlayer = Player.Local;
            if (localPlayer == null)
            {
                MelonLogger.Warning("[DeathLink] Cannot arrest - no local player");
                return;
            }

            if (localPlayer.IsArrested)
            {
                MelonLogger.Msg("[DeathLink] Player is already arrested");
                return;
            }

            if (!localPlayer.Health.IsAlive)
            {
                MelonLogger.Msg("[DeathLink] Player is already dead");
                return;
            }

            MelonLogger.Msg($"[DeathLink] Arresting player because {source} {cause}");
            _isProcessingReceivedDeath = true;
            try
            {
                localPlayer.Arrest_Server();
            }
            finally
            {
                _isProcessingReceivedDeath = false;
            }
        }

        /// <summary>
        /// Kills the local player in response to a DeathLink.
        /// Calls SendDie() which triggers the game's built-in death flow.
        /// The DeathScreen_CanRespawn_Patch ensures the Respawn button is always
        /// available, which uses the game's multiplayer hospital respawn logic.
        /// </summary>
        private static void KillPlayer(string source, string cause)
        {
            var localPlayer = Player.Local;
            if (localPlayer == null)
            {
                MelonLogger.Warning("[DeathLink] Cannot kill - no local player");
                return;
            }

            if (!localPlayer.Health.IsAlive)
            {
                MelonLogger.Msg("[DeathLink] Player is already dead");
                return;
            }

            MelonLogger.Msg($"[DeathLink] Killing player because {source} {cause}");
            _isProcessingReceivedDeath = true;
            try
            {
                localPlayer.Health.SendDie();
            }
            finally
            {
                _isProcessingReceivedDeath = false;
            }
        }

        /// <summary>
        /// Applies a random trap (Heat, Slippery, Trash, Pan, TimeScale, Sleep) in response to a DeathLink.
        /// </summary>
        private static void ExecuteRandomTrap(string source, string cause)
        {
            string trapName = RandomTrapPool[_random.Next(RandomTrapPool.Length)];
            MelonLogger.Msg($"[DeathLink] Random trap selected: '{trapName}' because {source} {cause}");
            NarcopelagoTraps.OnTrapItemReceived(trapName);
        }

        /// <summary>
        /// Applies the sleep trap (ends day) in response to a DeathLink.
        /// </summary>
        private static void ExecuteSleepTrap(string source, string cause)
        {
            MelonLogger.Msg($"[DeathLink] Sleep trap (end day) because {source} {cause}");
            NarcopelagoTraps.OnTrapItemReceived("Sleep Trap");
        }

        /// <summary>
        /// Resets the DeathLink state.
        /// Call this when disconnecting from Archipelago.
        /// </summary>
        public static void Reset()
        {
            Disable();
            _hookedEvents = false;
            _isProcessingReceivedDeath = false;
            _lastSentDeathTimestamp = 0;
            while (_pendingDeaths.TryDequeue(out _)) { }
        }
    }

    /// <summary>
    /// Harmony patch for PlayerHealth.Die to detect player death and trigger DeathLink.
    /// The actual hospital redirect is handled by DeathScreen_Open_Patch.
    /// </summary>
    [HarmonyPatch(typeof(PlayerHealth), "RpcLogic___Die_2166136261")]
    public class PlayerHealth_Die_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] PlayerHealth.Die patch is being prepared");
            return true;
        }

        static void Postfix(PlayerHealth __instance)
        {
            try
            {
                if (__instance.Player != null && __instance.Player == Player.Local)
                {
                    MelonLogger.Msg("[PATCH] Local player died - triggering DeathLink");
                    NarcopelagoDeathLink.OnLocalPlayerDied();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in PlayerHealth.Die Postfix: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Harmony patch for DeathScreen.CanRespawn to always return true.
    /// This ensures the respawn button is always shown instead of the load save button,
    /// which uses the game's multiplayer hospital respawn logic.
    /// </summary>
    [HarmonyPatch(typeof(DeathScreen), nameof(DeathScreen.CanRespawn))]
    public class DeathScreen_CanRespawn_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] DeathScreen.CanRespawn patch is being prepared");
            return true;
        }

        static bool Prefix(ref bool __result)
        {
            // Always allow respawn - this shows the respawn button instead of load save
            __result = true;
            MelonLogger.Msg("[PATCH] CanRespawn called - returning true to enable respawn button");
            return false; // Skip original method
        }
    }

    /// <summary>
    /// Harmony patch for DeathScreen.Open to show the respawn button and hide load save button.
    /// The death screen UI is shown but configured for multiplayer-style respawn.
    /// </summary>
    [HarmonyPatch(typeof(DeathScreen), nameof(DeathScreen.Open))]
    public class DeathScreen_Open_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] DeathScreen.Open patch is being prepared");
            return true;
        }

        static void Postfix(DeathScreen __instance)
        {
            try
            {
                MelonLogger.Msg("[PATCH] DeathScreen.Open postfix - ensuring respawn button is visible and load save is hidden");

                // Show the respawn button
                if (__instance.respawnButton != null)
                {
                    __instance.respawnButton.gameObject.SetActive(true);
                    MelonLogger.Msg("[PATCH] Respawn button activated");
                }
                else
                {
                    MelonLogger.Warning("[PATCH] respawnButton is null");
                }

                // Hide the load save button
                if (__instance.loadSaveButton != null)
                {
                    __instance.loadSaveButton.gameObject.SetActive(false);
                    MelonLogger.Msg("[PATCH] Load save button hidden");
                }
                else
                {
                    MelonLogger.Warning("[PATCH] loadSaveButton is null");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in DeathScreen.Open Postfix: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Harmony patch for Player.Arrest_Client to detect player arrest.
    /// </summary>
    [HarmonyPatch(typeof(Player), "RpcLogic___Arrest_Client_2166136261")]
    public class Player_Arrest_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] Player.Arrest patch is being prepared");
            return true;
        }

        static void Postfix(Player __instance)
        {
            try
            {
                if (__instance == Player.Local)
                {
                    MelonLogger.Msg("[PATCH] Local player arrested - triggering DeathLink");
                    NarcopelagoDeathLink.OnLocalPlayerArrested();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in Player.Arrest Postfix: {ex.Message}");
            }
        }
    }
}
