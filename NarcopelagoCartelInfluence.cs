using HarmonyLib;
using Il2CppScheduleOne.Cartel;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Graffiti;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs.Relation;
using Il2Cpp;
using MelonLoader;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Narcopelago
{
    /// <summary>
    /// Tracks cartel influence states and handles influence-related logic for Archipelago.
    /// Influence is stored as a float 0-1 in game, where 1 = 100% cartel control.
    /// Each "Cartel Influence, Region" item removes 100 influence points (0.1 or 10%).
    /// 
    /// Activities that reduce cartel influence:
    /// - CartelDealer.DiedOrKnockedOut - Eliminate cartel dealer (-0.1)
    /// - SpraySurfaceInteraction.Reward - Spray painting graffiti (-0.05)  
    /// - Customer.OnCustomerUnlocked - Successful sample when cartel hostile (-0.075)
    /// - Ambush defeated - Defend against cartel ambush (-0.1)
    /// </summary>
    public static class NarcopelagoCartelInfluence
    {
        /// <summary>
        /// Tracks how many cartel influence items have been received per region.
        /// Key: Region name, Value: count of items received
        /// </summary>
        private static Dictionary<string, int> _influenceItemsReceived = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tracks how many cartel influence checks have been completed per region.
        /// Key: Region name, Value: count of checks sent (1-7)
        /// </summary>
        private static Dictionary<string, int> _influenceChecksCompleted = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Thread-safe queue for influence reductions that need to be processed on the main thread.
        /// Tuple: (region name, amount to reduce as float)
        /// </summary>
        private static ConcurrentQueue<(string region, float amount)> _mainThreadInfluenceQueue = new ConcurrentQueue<(string, float)>();

        /// <summary>
        /// Tracks if we're in a game scene where cartel influence should be available.
        /// </summary>
        private static bool _inGameScene = false;

        /// <summary>
        /// Tracks if a sync from session is pending.
        /// </summary>
        private static bool _syncPending = false;

        /// <summary>
        /// Counter for delayed sync (to allow game to fully initialize).
        /// </summary>
        private static int _syncDelayFrames = 0;

        /// <summary>
        /// Valid regions for cartel influence (excludes Northtown which has no cartel)
        /// </summary>
        private static readonly string[] ValidRegions = { "Westville", "Downtown", "Docks", "Suburbia", "Uptown" };

        /// <summary>
        /// Starting influence for each region. Westville starts at 0.5, others at 1.0.
        /// </summary>
        private static float GetStartingInfluenceForRegion(string region)
        {
            if (string.Equals(region, "Westville", StringComparison.OrdinalIgnoreCase))
            {
                return 0.5f;
            }
            return 1.0f;
        }

        /// <summary>
        /// Sets whether we're in a game scene. Call this from Core when scene changes.
        /// </summary>
        public static void SetInGameScene(bool inGame)
        {
            _inGameScene = inGame;
            if (inGame)
            {
                MelonLogger.Msg("[CartelInfluence] Entered game scene - will process influence changes");
            }
        }

        /// <summary>
        /// Queues a sync from session to be processed after a delay.
        /// </summary>
        public static void QueueSyncFromSession(int delayFrames = 120)
        {
            _syncPending = true;
            _syncDelayFrames = delayFrames;
            MelonLogger.Msg($"[CartelInfluence] Queued sync from session ({delayFrames} frames delay)");
        }

        /// <summary>
        /// Call this from the main thread to process queued influence changes.
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            if (!_inGameScene) return;

            ProcessPendingSync();

            // Process influence reductions from AP items
            int processed = 0;
            while (processed < 5 && _mainThreadInfluenceQueue.TryDequeue(out var item))
            {
                try
                {
                    ApplyInfluenceReductionInternal(item.region, item.amount);
                    processed++;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[CartelInfluence] Error processing influence reduction for '{item.region}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Processes pending sync from session.
        /// </summary>
        private static void ProcessPendingSync()
        {
            if (!_syncPending) return;

            if (_syncDelayFrames > 0)
            {
                _syncDelayFrames--;
                return;
            }

            _syncPending = false;

            try
            {
                SyncFromSessionInternal();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[CartelInfluence] Error in ProcessPendingSync: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the number of influence items received for a region.
        /// </summary>
        public static int GetInfluenceItemsReceived(string region)
        {
            return _influenceItemsReceived.TryGetValue(region, out int count) ? count : 0;
        }

        /// <summary>
        /// Gets the number of influence checks completed for a region.
        /// </summary>
        public static int GetInfluenceChecksCompleted(string region)
        {
            return _influenceChecksCompleted.TryGetValue(region, out int count) ? count : 0;
        }

        /// <summary>
        /// Checks if a region is valid for cartel influence (excludes Northtown).
        /// </summary>
        public static bool IsValidCartelRegion(string region)
        {
            foreach (var valid in ValidRegions)
            {
                if (string.Equals(valid, region, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if the cartel is currently hostile toward the player.
        /// </summary>
        public static bool IsCartelHostile()
        {
            try
            {
                if (!NetworkSingleton<Cartel>.InstanceExists)
                    return false;
                return NetworkSingleton<Cartel>.Instance.Status == ECartelStatus.Hostile;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Called when an anti-cartel activity is completed (dealer killed, graffiti, sample, ambush defended).
        /// Sends the next location check for the region (up to 7).
        /// </summary>
        public static void OnAntiCartelActivityCompleted(string region, string activityType)
        {
            // Validate region
            if (!IsValidCartelRegion(region))
            {
                MelonLogger.Msg($"[CartelInfluence] Ignoring activity in '{region}' - not a cartel region");
                return;
            }

            // Check if cartel is hostile
            if (!IsCartelHostile())
            {
                MelonLogger.Msg($"[CartelInfluence] Ignoring activity in '{region}' - cartel not hostile");
                return;
            }

            // Check if we've already sent all 7 checks for this region
            int currentCheckCount = GetInfluenceChecksCompleted(region);
            if (currentCheckCount >= 7)
            {
                MelonLogger.Msg($"[CartelInfluence] Already sent all 7 checks for '{region}' - no more checks to send");
                return;
            }

            // Send the next check
            int nextCheck = currentCheckCount + 1;
            MelonLogger.Msg($"[CartelInfluence] Activity '{activityType}' completed in '{region}' - sending check {nextCheck}");
            SendInfluenceCheck(region, nextCheck);
        }

        /// <summary>
        /// Called when a "Cartel Influence, Region" item is received from Archipelago.
        /// Queues influence reduction for the specified region.
        /// </summary>
        public static void OnInfluenceItemReceived(string region)
        {
            if (!_influenceItemsReceived.ContainsKey(region))
            {
                _influenceItemsReceived[region] = 0;
            }
            _influenceItemsReceived[region]++;

            MelonLogger.Msg($"[CartelInfluence] Received influence item for '{region}' (total: {_influenceItemsReceived[region]})");

            // Queue influence reduction (0.1 = 10%)
            float reductionAmount = -0.1f;
            _mainThreadInfluenceQueue.Enqueue((region, reductionAmount));
        }

        /// <summary>
        /// Sends a cartel influence location check.
        /// Capped at 7 checks per region.
        /// </summary>
        private static void SendInfluenceCheck(string region, int checkNumber)
        {
            // Hard cap at 7 checks per region
            if (checkNumber < 1 || checkNumber > 7)
            {
                MelonLogger.Msg($"[CartelInfluence] Check number {checkNumber} out of range (1-7) for region '{region}'");
                return;
            }

            // Initialize tracking if needed
            if (!_influenceChecksCompleted.ContainsKey(region))
            {
                _influenceChecksCompleted[region] = 0;
            }

            // Don't send if already completed
            if (_influenceChecksCompleted[region] >= checkNumber)
            {
                MelonLogger.Msg($"[CartelInfluence] Check {checkNumber} for '{region}' already completed");
                return;
            }

            _influenceChecksCompleted[region] = checkNumber;

            // Get the location ID and send the check
            int locationId = Data_Locations.GetCartelInfluenceLocationId(region, checkNumber);
            if (locationId > 0)
            {
                MelonLogger.Msg($"[CartelInfluence] Sending check: '{region} cartel influence {checkNumber}' (ID: {locationId})");
                NarcopelagoLocations.CompleteLocation(locationId);
            }
            else
            {
                MelonLogger.Warning($"[CartelInfluence] Could not find location ID for '{region} cartel influence {checkNumber}'");
            }
        }

        /// <summary>
        /// Syncs cartel influence from the Archipelago session.
        /// </summary>
        public static void SyncFromSession()
        {
            QueueSyncFromSession(150);
        }

        /// <summary>
        /// Internal sync implementation.
        /// </summary>
        private static void SyncFromSessionInternal()
        {
            var session = ConnectionHandler.CurrentSession;
            if (session?.Items?.AllItemsReceived == null)
            {
                MelonLogger.Msg("[CartelInfluence] Cannot sync - no session or items");
                return;
            }

            MelonLogger.Msg($"[CartelInfluence] Syncing cartel influence items...");

            // Count items per region
            var itemCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in session.Items.AllItemsReceived)
            {
                string itemName = item.ItemName;

                if (itemName.StartsWith("Cartel Influence, ") && Data_Items.HasTag(itemName, "Cartel Influence"))
                {
                    string region = itemName.Replace("Cartel Influence, ", "").Trim();
                    if (!itemCounts.ContainsKey(region))
                    {
                        itemCounts[region] = 0;
                    }
                    itemCounts[region]++;
                }
            }

            // Sync completed checks from Archipelago
            SyncCompletedChecksFromSession();

            // Apply influence based on item count
            foreach (var kvp in itemCounts)
            {
                string region = kvp.Key;
                int itemCount = kvp.Value;

                // Update our tracking
                _influenceItemsReceived[region] = itemCount;

                // Calculate target influence based on starting influence for this region
                // Westville starts at 0.5, others at 1.0
                float startingInfluence = GetStartingInfluenceForRegion(region);
                float targetInfluence = Math.Max(0f, startingInfluence - (itemCount * 0.1f));

                // Apply the influence
                EnforceInfluenceLevel(region, targetInfluence);
            }

            MelonLogger.Msg($"[CartelInfluence] Synced {itemCounts.Count} regions from session");
        }

        /// <summary>
        /// Syncs completed influence checks from Archipelago.
        /// </summary>
        private static void SyncCompletedChecksFromSession()
        {
            if (!NarcopelagoLocations.IsAvailable)
            {
                return;
            }

            var checkedLocations = NarcopelagoLocations.AllLocationsChecked;
            if (checkedLocations == null)
            {
                return;
            }

            foreach (var region in ValidRegions)
            {
                int completedCount = 0;
                for (int i = 1; i <= 7; i++)
                {
                    int locationId = Data_Locations.GetCartelInfluenceLocationId(region, i);
                    if (locationId > 0 && checkedLocations.Contains(locationId))
                    {
                        completedCount = i;
                    }
                    else
                    {
                        break;
                    }
                }

                if (completedCount > 0)
                {
                    _influenceChecksCompleted[region] = completedCount;
                    MelonLogger.Msg($"[CartelInfluence] Synced {completedCount} completed checks for '{region}'");
                }
            }
        }

        /// <summary>
        /// Applies an influence reduction to a region from an AP item.
        /// </summary>
        private static void ApplyInfluenceReductionInternal(string region, float amount)
        {
            try
            {
                if (!NetworkSingleton<Cartel>.InstanceExists)
                {
                    MelonLogger.Warning($"[CartelInfluence] Cartel instance not available");
                    return;
                }

                var cartel = NetworkSingleton<Cartel>.Instance;
                if (cartel?.Influence == null)
                {
                    MelonLogger.Warning($"[CartelInfluence] Cartel.Influence not available");
                    return;
                }

                if (!Enum.TryParse<EMapRegion>(region, true, out var mapRegion))
                {
                    MelonLogger.Warning($"[CartelInfluence] Invalid region name: '{region}'");
                    return;
                }

                float currentInfluence = cartel.Influence.GetInfluence(mapRegion);
                float targetInfluence = Math.Max(0f, currentInfluence + amount);

                MelonLogger.Msg($"[CartelInfluence] Applying AP influence reduction in '{region}': {currentInfluence} -> {targetInfluence}");
                cartel.Influence.ChangeInfluence(mapRegion, amount);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[CartelInfluence] Error applying influence reduction: {ex.Message}");
            }
        }

        /// <summary>
        /// Enforces a specific influence level for a region.
        /// </summary>
        private static void EnforceInfluenceLevel(string region, float targetInfluence)
        {
            try
            {
                if (!NetworkSingleton<Cartel>.InstanceExists)
                {
                    return;
                }

                var cartel = NetworkSingleton<Cartel>.Instance;
                if (cartel?.Influence == null)
                {
                    return;
                }

                if (!Enum.TryParse<EMapRegion>(region, true, out var mapRegion))
                {
                    return;
                }

                float currentInfluence = cartel.Influence.GetInfluence(mapRegion);

                if (currentInfluence > targetInfluence)
                {
                    float reduction = targetInfluence - currentInfluence;
                    cartel.Influence.ChangeInfluence(mapRegion, reduction);
                    MelonLogger.Msg($"[CartelInfluence] Enforced influence level in '{region}': {currentInfluence} -> {targetInfluence}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[CartelInfluence] Error enforcing influence level: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the minimum allowed influence for a region based on items received.
        /// Westville starts at 0.5, other regions start at 1.0.
        /// Each item allows 0.1 reduction from the starting point.
        /// </summary>
        public static float GetMinimumInfluenceForRegion(string region)
        {
            int itemsReceived = GetInfluenceItemsReceived(region);
            float startingInfluence = GetStartingInfluenceForRegion(region);
            float minInfluence = startingInfluence - (itemsReceived * 0.1f);
            return Math.Max(0f, minInfluence);
        }

        /// <summary>
        /// Resets all cartel influence tracking data.
        /// </summary>
        public static void Reset()
        {
            _influenceItemsReceived.Clear();
            _influenceChecksCompleted.Clear();
            _inGameScene = false;
            while (_mainThreadInfluenceQueue.TryDequeue(out _)) { }
        }
    }

    // ========================================
    // PATCHES FOR ANTI-CARTEL ACTIVITIES
    // ========================================

    /// <summary>
    /// Harmony patch for CartelDealer.DiedOrKnockedOut
    /// Called when a cartel dealer is eliminated.
    /// </summary>
    [HarmonyPatch(typeof(CartelDealer), "DiedOrKnockedOut")]
    public class CartelDealer_DiedOrKnockedOut_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] CartelDealer.DiedOrKnockedOut patch is being prepared");
            return true;
        }

        static void Postfix(CartelDealer __instance)
        {
            try
            {
                string region = __instance.Region.ToString();
                MelonLogger.Msg($"[PATCH] Cartel dealer eliminated in '{region}'");
                NarcopelagoCartelInfluence.OnAntiCartelActivityCompleted(region, "CartelDealerEliminated");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in CartelDealer.DiedOrKnockedOut Postfix: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Harmony patch for SpraySurfaceInteraction.Reward
    /// Called when spray painting is completed on a surface.
    /// </summary>
    [HarmonyPatch(typeof(SpraySurfaceInteraction), "Reward")]
    public class SpraySurfaceInteraction_Reward_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] SpraySurfaceInteraction.Reward patch is being prepared");
            return true;
        }

        static void Postfix(SpraySurfaceInteraction __instance)
        {
            try
            {
                string region = __instance.SpraySurface.Region.ToString();
                MelonLogger.Msg($"[PATCH] Spray painting completed in '{region}'");
                NarcopelagoCartelInfluence.OnAntiCartelActivityCompleted(region, "SprayPainting");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in SpraySurfaceInteraction.Reward Postfix: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Harmony patch for Customer.OnCustomerUnlocked
    /// Called when a customer is unlocked (successful sample).
    /// Only counts when notify=true (actual sample, not loading) and cartel is hostile.
    /// </summary>
    [HarmonyPatch(typeof(Customer), "OnCustomerUnlocked")]
    public class Customer_OnCustomerUnlocked_CartelInfluence_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] Customer.OnCustomerUnlocked (CartelInfluence) patch is being prepared");
            return true;
        }

        static void Postfix(Customer __instance, NPCRelationData.EUnlockType unlockType, bool notify)
        {
            try
            {
                // Only count if notify=true (actual sample, not loading from save)
                if (!notify)
                {
                    return;
                }

                string region = __instance.NPC.Region.ToString();
                MelonLogger.Msg($"[PATCH] Customer unlocked (sample) in '{region}'");
                NarcopelagoCartelInfluence.OnAntiCartelActivityCompleted(region, "SuccessfulSample");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in Customer.OnCustomerUnlocked Postfix: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Harmony patch for CartelInfluence.RpcLogic___ChangeInfluence_1267088319
    /// When Randomize_cartel_influence is true, blocks reductions unless from AP items.
    /// </summary>
    [HarmonyPatch(typeof(CartelInfluence), "RpcLogic___ChangeInfluence_1267088319")]
    public class CartelInfluence_ChangeInfluence_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] CartelInfluence.ChangeInfluence patch is being prepared");
            return true;
        }

        static bool Prefix(EMapRegion region, float oldInfluence, ref float newInfluence)
        {
            try
            {
                string regionName = region.ToString();
                bool isReduction = newInfluence < oldInfluence;

                // Ensure we never go below 0
                if (newInfluence < 0f)
                {
                    newInfluence = 0f;
                }

                // If not a reduction, always allow
                if (!isReduction)
                {
                    return true;
                }

                // If Randomize_cartel_influence is false, allow all reductions
                if (!NarcopelagoOptions.Randomize_cartel_influence)
                {
                    MelonLogger.Msg($"[PATCH] Allowing influence reduction in '{regionName}' - Randomize_cartel_influence is false");
                    return true;
                }

                // Randomize_cartel_influence is true - check if this reduction is allowed by AP items
                float minAllowed = NarcopelagoCartelInfluence.GetMinimumInfluenceForRegion(regionName);

                MelonLogger.Msg($"[PATCH] Cartel influence change in '{regionName}': {oldInfluence} -> {newInfluence} (minAllowed: {minAllowed})");

                if (newInfluence < minAllowed)
                {
                    if (oldInfluence > minAllowed)
                    {
                        // Allow partial reduction to minAllowed
                        newInfluence = minAllowed;
                        MelonLogger.Msg($"[PATCH] Clamping influence reduction in '{regionName}' to minAllowed: {minAllowed}");
                        return true;
                    }
                    else
                    {
                        // Already at or below minimum, block entirely
                        MelonLogger.Msg($"[PATCH] Blocking influence reduction in '{regionName}' - at or below minimum ({oldInfluence} <= {minAllowed})");
                        return false;
                    }
                }

                // New influence is >= minAllowed, allow it
                MelonLogger.Msg($"[PATCH] Allowing influence reduction in '{regionName}' - within AP-allowed range");
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in CartelInfluence ChangeInfluence Prefix: {ex.Message}");
                return true;
            }
        }
    }
}
