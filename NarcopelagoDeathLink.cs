using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using MelonLoader;
using System;

namespace Narcopelago
{
    /// <summary>
    /// Handles DeathLink functionality for Schedule I.
    /// DeathLink allows players to share deaths across different games in the multiworld.
    /// </summary>
    public static class NarcopelagoDeathLink
    {
        private static DeathLinkService _deathLinkService;
        private static bool _isEnabled = false;

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
                // Get or create the DeathLink service from the session
                _deathLinkService = session.CreateDeathLinkService();
                
                // Subscribe to DeathLink received events
                _deathLinkService.OnDeathLinkReceived += OnDeathLinkReceived;
                
                // Enable the service
                _deathLinkService.EnableDeathLink();
                
                _isEnabled = true;
                MelonLogger.Msg("[DeathLink] Enabled successfully");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeathLink] Failed to enable: {ex.Message}");
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
        /// Sends a death to other players in the multiworld.
        /// Call this when the local player dies.
        /// </summary>
        /// <param name="playerName">The name of the player who died (usually the slot name).</param>
        /// <param name="cause">Optional description of what caused the death.</param>
        public static void SendDeath(string playerName, string cause = null)
        {
            if (!_isEnabled || _deathLinkService == null)
            {
                MelonLogger.Msg("[DeathLink] Not enabled, skipping death send");
                return;
            }

            try
            {
                string deathMessage = string.IsNullOrEmpty(cause) 
                    ? $"{playerName} died" 
                    : $"{playerName} {cause}";
                
                _deathLinkService.SendDeathLink(new DeathLink(playerName, deathMessage));
                MelonLogger.Msg($"[DeathLink] Sent death: {deathMessage}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeathLink] Failed to send death: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when a DeathLink is received from another player.
        /// This should kill the local player.
        /// </summary>
        private static void OnDeathLinkReceived(DeathLink deathLink)
        {
            try
            {
                string source = deathLink.Source ?? "Unknown";
                string cause = deathLink.Cause ?? "died";
                
                MelonLogger.Msg($"[DeathLink] Received death from {source}: {cause}");
                
                // TODO: Implement actual player death logic here
                // This will depend on how Schedule I handles player death
                // For now, just log it
                MelonLogger.Warning($"[DeathLink] You died because {source} {cause}");
                
                // Example of what you might do:
                // - Find the player character GameObject
                // - Call a method to kill the player (e.g., TakeDamage, Die, etc.)
                // - Show a notification to the player about the deathlink
                
                // Placeholder for actual implementation:
                KillPlayer(source, cause);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeathLink] Error processing received death: {ex.Message}");
            }
        }

        /// <summary>
        /// Kills the local player in response to a DeathLink.
        /// TODO: Implement this based on Schedule I's player death system.
        /// </summary>
        private static void KillPlayer(string source, string cause)
        {
            // TODO: Find the appropriate way to kill the player in Schedule I
            // This might involve:
            // 1. Finding the player GameObject (e.g., GameObject.Find("Player"))
            // 2. Accessing a health/death component
            // 3. Calling a death method
            
            // Example pseudocode:
            // var player = GameObject.FindObjectOfType<PlayerController>();
            // if (player != null)
            // {
            //     player.Die();
            //     // Or: player.TakeDamage(999999);
            //     // Or: player.Health = 0;
            // }
            
            MelonLogger.Warning($"[DeathLink] TODO: Implement player death - killed by {source}: {cause}");
        }

        /// <summary>
        /// Resets the DeathLink state.
        /// Call this when disconnecting from Archipelago.
        /// </summary>
        public static void Reset()
        {
            Disable();
        }
    }
}
