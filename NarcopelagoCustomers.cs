using HarmonyLib;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Relation;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Narcopelago
{
    /// <summary>
    /// Tracks customer states and handles customer unlock logic for Archipelago.
    /// When Randomize_customers is enabled, customers are not unlocked until
    /// the corresponding item is received from Archipelago.
    /// </summary>
    public static class NarcopelagoCustomers
    {
        /// <summary>
        /// Tracks which customers have been given a successful sample.
        /// Key: Customer name, Value: true if sample was successful
        /// </summary>
        private static Dictionary<string, bool> _customerSampleStatus = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tracks which customers have been unlocked via Archipelago items.
        /// Only used when Randomize_customers is true.
        /// Key: Customer name, Value: true if unlock item received
        /// </summary>
        private static Dictionary<string, bool> _customerUnlockStatus = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tracks customers that need to be unlocked once the game is ready.
        /// Used for startup items received before NPCs are loaded.
        /// </summary>
        private static HashSet<string> _pendingUnlocks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Flag to indicate we're unlocking from Archipelago (don't intercept with Harmony patch)
        /// </summary>
        private static bool _isUnlockingFromArchipelago = false;

        /// <summary>
        /// Returns true if we're currently unlocking a customer from an Archipelago item.
        /// Used by Harmony patch to skip interception.
        /// </summary>
        public static bool IsUnlockingFromArchipelago => _isUnlockingFromArchipelago;

        /// <summary>
        /// Checks if a customer has received a successful sample.
        /// </summary>
        /// <param name="customerName">The customer's name.</param>
        /// <returns>True if the customer has received a successful sample.</returns>
        public static bool HasReceivedSample(string customerName)
        {
            return _customerSampleStatus.TryGetValue(customerName, out bool status) && status;
        }

        /// <summary>
        /// Marks a customer as having received a successful sample.
        /// </summary>
        /// <param name="customerName">The customer's name.</param>
        public static void SetSampleReceived(string customerName)
        {
            // Avoid duplicate sends
            if (HasReceivedSample(customerName))
            {
                MelonLogger.Msg($"Customer '{customerName}' already received sample - skipping");
                return;
            }

            _customerSampleStatus[customerName] = true;
            MelonLogger.Msg($"Customer '{customerName}' received successful sample");

            // Send the location check for this customer sample (always, regardless of Randomize_customers)
            SendCustomerSampleCheck(customerName);
        }

        /// <summary>
        /// Sends the Archipelago location check for a customer sample.
        /// Location search is sync, server call is async.
        /// </summary>
        /// <param name="customerName">The customer's name.</param>
        private static void SendCustomerSampleCheck(string customerName)
        {
            try
            {
                // Search for location containing "Customer: {customerName}"
                // Format is like "Unlock Northtown Customer: Peter File"
                // Do this synchronously since we need to access session data
                long locationId = -1;
                
                var session = ConnectionHandler.CurrentSession;
                if (session?.Locations?.AllLocations != null)
                {
                    foreach (var locId in session.Locations.AllLocations)
                    {
                        string locName = NarcopelagoLocations.GetLocationName(locId);
                        if (locName != null && locName.Contains($"Customer: {customerName}"))
                        {
                            locationId = locId;
                            MelonLogger.Msg($"Found location by search: {locName}");
                            break;
                        }
                    }
                }
                
                if (locationId > 0)
                {
                    MelonLogger.Msg($"Sending customer unlock check for '{customerName}' (ID: {locationId})");
                    // CompleteLocation already runs async internally
                    NarcopelagoLocations.CompleteLocation(locationId);
                }
                else
                {
                    MelonLogger.Warning($"Could not find location for customer: {customerName}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in SendCustomerSampleCheck: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a customer is unlocked.
        /// When Randomize_customers is true, this checks if the unlock item was received.
        /// When false, customers unlock normally through gameplay.
        /// </summary>
        /// <param name="customerName">The customer's name.</param>
        /// <returns>True if the customer is unlocked.</returns>
        public static bool IsCustomerUnlocked(string customerName)
        {
            if (!NarcopelagoOptions.Randomize_customers)
            {
                // Not randomizing customers - they unlock normally
                return true;
            }

            return _customerUnlockStatus.TryGetValue(customerName, out bool unlocked) && unlocked;
        }

        /// <summary>
        /// Marks a customer as unlocked and attempts to unlock them in-game.
        /// Called when receiving the unlock item from Archipelago.
        /// </summary>
        /// <param name="customerName">The customer's name.</param>
        public static void SetCustomerUnlocked(string customerName)
        {
            // Mark as unlocked in our tracking
            _customerUnlockStatus[customerName] = true;
            MelonLogger.Msg($"Customer '{customerName}' unlocked via Archipelago");

            // Try to actually unlock the customer in-game
            if (!TryUnlockCustomerInGame(customerName))
            {
                // If we couldn't unlock now (NPCs not loaded yet), add to pending
                _pendingUnlocks.Add(customerName);
                MelonLogger.Msg($"Customer '{customerName}' added to pending unlocks");
            }
        }

        /// <summary>
        /// Marks a customer as unlocked from history (previous session items).
        /// Only updates tracking, does not try to unlock in-game.
        /// </summary>
        /// <param name="customerName">The customer's name.</param>
        public static void SetCustomerUnlockedFromHistory(string customerName)
        {
            _customerUnlockStatus[customerName] = true;
            _pendingUnlocks.Add(customerName);
            MelonLogger.Msg($"Customer '{customerName}' marked as unlocked from history");
        }

        /// <summary>
        /// Attempts to find and unlock a customer in the game.
        /// </summary>
        /// <param name="customerName">The customer's name.</param>
        /// <returns>True if the customer was found and unlocked.</returns>
        private static bool TryUnlockCustomerInGame(string customerName)
        {
            try
            {
                // Find all NPCs in the scene
                var npcs = GameObject.FindObjectsOfType<NPC>();
                
                if (npcs == null || npcs.Length == 0)
                {
                    MelonLogger.Msg($"[DEBUG] No NPCs found in scene - game may not be loaded yet");
                    return false;
                }

                foreach (var npc in npcs)
                {
                    string npcName = npc.fullName ?? npc.FirstName ?? "";
                    
                    if (string.Equals(npcName, customerName, StringComparison.OrdinalIgnoreCase))
                    {
                        MelonLogger.Msg($"[DEBUG] Found NPC '{customerName}', attempting unlock...");
                        
                        // Get the relation data and call Unlock
                        var relationData = npc.RelationData;
                        if (relationData != null)
                        {
                            // Set flag to prevent Harmony patch from intercepting this unlock
                            _isUnlockingFromArchipelago = true;
                            try
                            {
                                // Unlock with type 1 (Sample) and network=true
                                relationData.Unlock((NPCRelationData.EUnlockType)1, true);
                                MelonLogger.Msg($"Customer '{customerName}' unlocked in-game!");
                            }
                            finally
                            {
                                _isUnlockingFromArchipelago = false;
                            }
                            return true;
                        }
                        else
                        {
                            MelonLogger.Warning($"NPC '{customerName}' has no RelationData");
                        }
                    }
                }

                MelonLogger.Msg($"[DEBUG] Could not find NPC '{customerName}' in scene");
                return false;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error unlocking customer '{customerName}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Process any pending unlocks. Call this when the game scene is fully loaded.
        /// </summary>
        public static void ProcessPendingUnlocks()
        {
            if (_pendingUnlocks.Count == 0) return;

            MelonLogger.Msg($"[DEBUG] Processing {_pendingUnlocks.Count} pending customer unlocks...");

            var processed = new List<string>();
            
            foreach (var customerName in _pendingUnlocks)
            {
                if (TryUnlockCustomerInGame(customerName))
                {
                    processed.Add(customerName);
                }
            }

            foreach (var name in processed)
            {
                _pendingUnlocks.Remove(name);
            }

            if (_pendingUnlocks.Count > 0)
            {
                MelonLogger.Msg($"[DEBUG] {_pendingUnlocks.Count} customers still pending unlock");
            }
        }

        /// <summary>
        /// Resets all customer tracking data.
        /// </summary>
        public static void Reset()
        {
            _customerSampleStatus.Clear();
            _customerUnlockStatus.Clear();
            _pendingUnlocks.Clear();
        }

        /// <summary>
        /// Gets all customers who have received samples.
        /// </summary>
        public static IEnumerable<string> GetCustomersWithSamples()
        {
            foreach (var kvp in _customerSampleStatus)
            {
                if (kvp.Value)
                    yield return kvp.Key;
            }
        }

        /// <summary>
        /// Gets all unlocked customers (when Randomize_customers is enabled).
        /// </summary>
        public static IEnumerable<string> GetUnlockedCustomers()
        {
            foreach (var kvp in _customerUnlockStatus)
            {
                if (kvp.Value)
                    yield return kvp.Key;
            }
        }

        /// <summary>
        /// Logs current customer status for debugging.
        /// </summary>
        public static void LogStatus()
        {
            MelonLogger.Msg("=== Customer Status ===");
            MelonLogger.Msg($"Randomize Customers: {NarcopelagoOptions.Randomize_customers}");
            MelonLogger.Msg($"Customers with samples: {_customerSampleStatus.Count}");
            MelonLogger.Msg($"Unlocked customers: {_customerUnlockStatus.Count}");
            MelonLogger.Msg($"Pending unlocks: {_pendingUnlocks.Count}");
        }
    }

    /// <summary>
    /// Harmony patch to intercept NPCRelationData.Unlock
    /// TEMPORARILY DISABLED to diagnose crash
    /// </summary>
    [HarmonyPatch(typeof(Il2CppScheduleOne.NPCs.Relation.NPCRelationData), "Unlock")]
    public class NPCRelationData_Unlock_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] NPCRelationData.Unlock patch is DISABLED");
            return false; // Disabled
        }
    }
}
