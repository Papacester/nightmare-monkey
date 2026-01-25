using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Il2CppScheduleOne.Quests;
namespace Narcopelago
{
    
    /// <summary>
    /// Manages Archipelago location data for Schedule I.
    /// Access location info via ConnectionHandler.CurrentSession.Locations or through this helper class.
    /// </summary>
    public static class NarcopelagoLocations
    {
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
        /// Marks a location as completed and sends it to the server.
        /// Call this when the player completes a check in-game.
        /// This runs asynchronously to avoid blocking the game.
        /// </summary>
        /// <param name="locationId">The location ID to complete.</param>
        public static void CompleteLocation(long locationId)
        {
            if (!IsAvailable)
            {
                MelonLogger.Warning("Cannot complete location - not connected to Archipelago");
                return;
            }

            // Run async to avoid blocking the game
            Task.Run(() =>
            {
                try
                {
                    Locations.CompleteLocationChecks(locationId);
                    MelonLogger.Msg($"Location {locationId} completed!");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Failed to complete location {locationId}: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Marks multiple locations as completed and sends them to the server.
        /// This runs asynchronously to avoid blocking the game.
        /// </summary>
        /// <param name="locationIds">The location IDs to complete.</param>
        public static void CompleteLocations(params long[] locationIds)
        {
            if (!IsAvailable)
            {
                MelonLogger.Warning("Cannot complete locations - not connected to Archipelago");
                return;
            }

            // Run async to avoid blocking the game
            Task.Run(() =>
            {
                try
                {
                    Locations.CompleteLocationChecks(locationIds);
                    MelonLogger.Msg($"Completed {locationIds.Length} locations!");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"Failed to complete locations: {ex.Message}");
                }
            });
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
                MelonLogger.Warning("Cannot scout location - not connected to Archipelago");
                return null;
            }

            try
            {
                var result = await Locations.ScoutLocationsAsync(createAsHint, locationId);
                return result;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to scout location {locationId}: {ex.Message}");
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
        /// Logs all location information for debugging purposes.
        /// </summary>
        public static void LogAllLocations()
        {
            if (!IsAvailable)
            {
                MelonLogger.Msg("Cannot log locations - not connected");
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
