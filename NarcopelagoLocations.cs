using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace Narcopelago
{
    /// <summary>
    /// Manages Archipelago location data for Schedule I.
    /// Access location info via ConnectionHandler.CurrentSession.Locations or through this helper class.
    /// </summary>
    public static class NarcopelagoLocations
    {
        /// <summary>
        /// Queue of location IDs to be sent in the next batch.
        /// </summary>
        private static List<long> _pendingLocationChecks = new List<long>();
        private static readonly object _pendingLock = new object();
        private static bool _isSending = false;

        /// <summary>
        /// Gets the LocationCheckHelper from the current session.
        /// Returns null if not connected.
        /// </summary>
        public static ILocationCheckHelper Locations => ConnectionHandler.CurrentSession?.Locations;

        /// <summary>
        /// Returns true if we have an active connection with location data available.
        /// </summary>
        public static bool IsAvailable => Locations != null;

        /// <summary>
        /// Gets all location IDs that belong to this player's world.
        /// </summary>
        public static ReadOnlyCollection<long> AllLocations => Locations?.AllLocations;

        /// <summary>
        /// Gets all location IDs that have already been checked/completed.
        /// </summary>
        public static ReadOnlyCollection<long> AllLocationsChecked => Locations?.AllLocationsChecked;

        /// <summary>
        /// Gets all location IDs that have not yet been checked.
        /// </summary>
        public static ReadOnlyCollection<long> AllMissingLocations => Locations?.AllMissingLocations;

        /// <summary>
        /// Checks if a specific location has been completed.
        /// </summary>
        /// <param name="locationId">The location ID to check.</param>
        /// <returns>True if the location has been checked, false otherwise.</returns>
        public static bool IsLocationChecked(long locationId)
        {
            if (!IsAvailable) return false;
            return AllLocationsChecked?.Contains(locationId) ?? false;
        }

        /// <summary>
        /// Queues a location to be completed and sends it asynchronously.
        /// Uses CompleteLocationChecksAsync to avoid blocking.
        /// </summary>
        /// <param name="locationId">The location ID to complete.</param>
        public static void CompleteLocation(long locationId)
        {
            if (!IsAvailable)
            {
                MelonLogger.Warning("[Locations] Cannot complete location - not connected to Archipelago");
                return;
            }

            MelonLogger.Msg($"[Locations] Queueing location {locationId} for completion");
            
            lock (_pendingLock)
            {
                if (!_pendingLocationChecks.Contains(locationId))
                {
                    _pendingLocationChecks.Add(locationId);
                }
            }

            // Trigger send if not already sending
            SendPendingLocationsAsync();
        }

        /// <summary>
        /// Queues multiple locations to be completed and sends them asynchronously.
        /// </summary>
        /// <param name="locationIds">The location IDs to complete.</param>
        public static void CompleteLocations(params long[] locationIds)
        {
            if (!IsAvailable)
            {
                MelonLogger.Warning("[Locations] Cannot complete locations - not connected to Archipelago");
                return;
            }

            if (locationIds == null || locationIds.Length == 0) return;

            MelonLogger.Msg($"[Locations] Queueing {locationIds.Length} locations for completion");

            lock (_pendingLock)
            {
                foreach (var id in locationIds)
                {
                    if (!_pendingLocationChecks.Contains(id))
                    {
                        _pendingLocationChecks.Add(id);
                    }
                }
            }

            // Trigger send if not already sending
            SendPendingLocationsAsync();
        }

        /// <summary>
        /// Sends all pending location checks asynchronously.
        /// </summary>
        private static async void SendPendingLocationsAsync()
        {
            // Prevent multiple simultaneous sends
            lock (_pendingLock)
            {
                if (_isSending || _pendingLocationChecks.Count == 0)
                {
                    return;
                }
                _isSending = true;
            }

            // Get the locations to send
            long[] locationsToSend;
            lock (_pendingLock)
            {
                locationsToSend = _pendingLocationChecks.ToArray();
                _pendingLocationChecks.Clear();
            }

            MelonLogger.Msg($"[Locations] Sending {locationsToSend.Length} location checks...");

            try
            {
                // Use Task.Run to send on a background thread
                await Task.Run(() =>
                {
                    try
                    {
                        Locations.CompleteLocationChecks(locationsToSend);
                        MelonLogger.Msg($"[Locations] Successfully completed {locationsToSend.Length} locations");
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Error($"[Locations] Error in background send: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Locations] Error sending location checks: {ex.Message}");
            }
            finally
            {
                lock (_pendingLock)
                {
                    _isSending = false;
                }

                // Check if more locations were queued while we were sending
                if (_pendingLocationChecks.Count > 0)
                {
                    SendPendingLocationsAsync();
                }
            }
        }

        /// <summary>
        /// Scouts a location to see what item is there without completing it.
        /// </summary>
        /// <param name="locationId">The location ID to scout.</param>
        /// <param name="createAsHint">If true, creates a hint for this location.</param>
        /// <returns>Dictionary mapping location IDs to ScoutedItemInfo.</returns>
        public static async Task<Dictionary<long, ScoutedItemInfo>> ScoutLocationAsync(long locationId, bool createAsHint = false)
        {
            if (!IsAvailable)
            {
                MelonLogger.Warning("[Locations] Cannot scout location - not connected to Archipelago");
                return null;
            }

            try
            {
                var result = await Locations.ScoutLocationsAsync(createAsHint, locationId);
                return result;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Locations] Failed to scout location {locationId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the name of a location by its ID.
        /// </summary>
        /// <param name="locationId">The location ID.</param>
        /// <returns>The location name, or null if not found.</returns>
        public static string GetLocationName(long locationId)
        {
            if (!IsAvailable) return null;
            return ConnectionHandler.CurrentSession?.Locations.GetLocationNameFromId(locationId);
        }

        /// <summary>
        /// Gets the ID of a location by its name.
        /// </summary>
        /// <param name="locationName">The location name.</param>
        /// <returns>The location ID, or -1 if not found.</returns>
        public static long GetLocationId(string locationName)
        {
            if (!IsAvailable) return -1;
            return ConnectionHandler.CurrentSession?.Locations.GetLocationIdFromName(
                ConnectionHandler.CurrentSession.ConnectionInfo.Game, 
                locationName) ?? -1;
        }

        /// <summary>
        /// Resets the pending location checks.
        /// Call this when disconnecting.
        /// </summary>
        public static void Reset()
        {
            lock (_pendingLock)
            {
                _pendingLocationChecks.Clear();
                _isSending = false;
            }
        }

        /// <summary>
        /// Logs all location information for debugging purposes.
        /// </summary>
        public static void LogAllLocations()
        {
            if (!IsAvailable)
            {
                MelonLogger.Msg("[Locations] Cannot log locations - not connected");
                return;
            }

            MelonLogger.Msg($"=== Archipelago Locations ===");
            MelonLogger.Msg($"Total locations: {AllLocations?.Count ?? 0}");
            MelonLogger.Msg($"Checked locations: {AllLocationsChecked?.Count ?? 0}");
            MelonLogger.Msg($"Missing locations: {AllMissingLocations?.Count ?? 0}");

            if (AllMissingLocations != null)
            {
                MelonLogger.Msg("Missing location IDs:");
                foreach (var locId in AllMissingLocations.Take(20)) // Limit to first 20
                {
                    string name = GetLocationName(locId) ?? "Unknown";
                    MelonLogger.Msg($"  - {locId}: {name}");
                }
                if (AllMissingLocations.Count > 20)
                {
                    MelonLogger.Msg($"  ... and {AllMissingLocations.Count - 20} more");
                }
            }
        }
    }
}
