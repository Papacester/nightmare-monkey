using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Packets;
using HarmonyLib;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.PlayerScripts.Health;
using MelonLoader;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeathLink] Error processing received death: {ex.Message}");
            }
        }

        /// <summary>
        /// Processes pending deaths on the main thread.
        /// Call this from OnUpdate.
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            if (!_isEnabled || !NarcopelagoOptions.Deathlink)
            {
                return;
            }

            while (_pendingDeaths.TryDequeue(out var death))
            {
                try
                {
                    ArrestPlayer(death.source, death.cause);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[DeathLink] Error arresting player: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Arrests the local player in response to a DeathLink.
        /// </summary>
        private static void ArrestPlayer(string source, string cause)
        {
            try
            {
                var localPlayer = Player.Local;
                if (localPlayer == null)
                {
                    MelonLogger.Warning("[DeathLink] Cannot arrest - no local player");
                    return;
                }

                // Check if player is already arrested or dead
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

                // Set flag to prevent sending a death back
                _isProcessingReceivedDeath = true;

                try
                {
                    // Call the arrest method
                    // Using the RPC method that handles both server and client
                    localPlayer.Arrest_Server();
                }
                finally
                {
                    // Reset the flag after a short delay (to ensure the event is processed)
                    _isProcessingReceivedDeath = false;
                }
            }
            catch (Exception ex)
            {
                _isProcessingReceivedDeath = false;
                MelonLogger.Error($"[DeathLink] Error in ArrestPlayer: {ex.Message}");
            }
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
    /// Harmony patch for PlayerHealth.Die to detect player death.
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
                // Only trigger for the local player
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
                // Only trigger for the local player
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
