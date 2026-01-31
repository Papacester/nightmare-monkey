using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Phone;
using MelonLoader;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Narcopelago
{
    /// <summary>
    /// Handles level up rewards for Archipelago.
    /// Sends location checks when the player reaches certain ranks.
    /// Location format: "Rank {RankName} {Tier}" (e.g., "Rank Street Rat III", "Rank Hoodlum V")
    /// 
    /// Also handles receiving unlock items from Archipelago:
    /// - "ItemName Unlock" - Unlocks shop items (Gas Mart, Arms Dealer, Warehouse, etc.)
    /// - "Warehouse Access" - Unlocks Dark Market (Oscar's warehouse)
    /// - "Westville Region Unlock" - Unlocks Westville region
    /// - "DrugName Unlock" - Unlocks supplier drugs (Green Crack, Pseudo, etc.)
    /// </summary>
    public static class NarcopelagoLevels
    {
        /// <summary>
        /// Tracks which rank locations have been completed.
        /// Key: Location name (e.g., "Rank Street Rat III"), Value: true if completed
        /// </summary>
        private static Dictionary<string, bool> _completedRanks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tracks which items have been unlocked via Archipelago items.
        /// Key: Item name (e.g., "Gasoline", "Pseudo"), Value: true if unlocked
        /// </summary>
        private static Dictionary<string, bool> _unlockedItems = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tracks if Warehouse Access has been received.
        /// </summary>
        private static bool _warehouseAccessReceived = false;

        /// <summary>
        /// Tracks if Westville Region Unlock has been received.
        /// </summary>
        private static bool _westvilleRegionReceived = false;

        /// <summary>
        /// Queue for unlocks that need to be processed on the main thread.
        /// </summary>
        private static ConcurrentQueue<string> _pendingUnlocks = new ConcurrentQueue<string>();

        /// <summary>
        /// Tracks if we're in a game scene.
        /// </summary>
        private static bool _inGameScene = false;

        /// <summary>
        /// Tracks if a sync from session is pending.
        /// </summary>
        private static bool _syncPending = false;

        /// <summary>
        /// Counter for delayed sync.
        /// </summary>
        private static int _syncDelayFrames = 0;

        /// <summary>
        /// Sets whether we're in a game scene.
        /// </summary>
        public static void SetInGameScene(bool inGame)
        {
            _inGameScene = inGame;
            if (inGame)
            {
                MelonLogger.Msg("[Levels] Entered game scene");
            }
        }

        /// <summary>
        /// Queues a sync from session to be processed after a delay.
        /// </summary>
        public static void QueueSyncFromSession(int delayFrames = 120)
        {
            _syncPending = true;
            _syncDelayFrames = delayFrames;
            MelonLogger.Msg($"[Levels] Queued sync from session ({delayFrames} frames delay)");
        }

        /// <summary>
        /// Call this from the main thread to process pending operations.
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            if (!_inGameScene) return;

            ProcessPendingSync();
            ProcessPendingUnlocks();
        }

        /// <summary>
        /// Processes pending unlocks on the main thread.
        /// </summary>
        private static void ProcessPendingUnlocks()
        {
            int processed = 0;
            while (processed < 5 && _pendingUnlocks.TryDequeue(out string unlockName))
            {
                try
                {
                    ApplyUnlockInternal(unlockName);
                    processed++;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Levels] Error processing unlock '{unlockName}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Applies an unlock on the main thread.
        /// </summary>
        private static void ApplyUnlockInternal(string unlockName)
        {
            if (unlockName == "Warehouse Access")
            {
                // Unlock Dark Market
                if (NetworkSingleton<DarkMarket>.InstanceExists && !NetworkSingleton<DarkMarket>.Instance.Unlocked)
                {
                    MelonLogger.Msg("[Levels] Applying Warehouse Access unlock - unlocking Dark Market");
                    NetworkSingleton<DarkMarket>.Instance.SendUnlocked();
                }
            }
            else if (unlockName == "Westville Region Unlock")
            {
                // Unlock Westville region
                if (Il2CppScheduleOne.Map.Map.Instance != null)
                {
                    var regionData = Il2CppScheduleOne.Map.Map.Instance.GetRegionData(EMapRegion.Westville);
                    if (regionData != null && !regionData.IsUnlocked)
                    {
                        MelonLogger.Msg("[Levels] Applying Westville Region unlock");
                        regionData.SetUnlocked();
                        Singleton<RegionUnlockedCanvas>.Instance?.QueueUnlocked(EMapRegion.Westville);
                    }
                }
            }
            // Other unlocks (shop items) are handled passively through IsItemUnlockedByArchipelago
        }

        /// <summary>
        /// Called when a Level Up Reward unlock item is received from Archipelago.
        /// </summary>
        public static void OnUnlockItemReceived(string itemName)
        {
            MelonLogger.Msg($"[Levels] Received unlock item: {itemName}");

            if (itemName == "Warehouse Access")
            {
                _warehouseAccessReceived = true;
                _pendingUnlocks.Enqueue(itemName);
            }
            else if (itemName == "Westville Region Unlock")
            {
                _westvilleRegionReceived = true;
                _pendingUnlocks.Enqueue(itemName);
            }
            else if (itemName.EndsWith(" Unlock"))
            {
                // Extract item name from "ItemName Unlock"
                string baseItemName = itemName.Substring(0, itemName.Length - " Unlock".Length);
                _unlockedItems[baseItemName] = true;
                MelonLogger.Msg($"[Levels] Item '{baseItemName}' is now unlocked");
            }
        }

        /// <summary>
        /// Checks if an item has been unlocked by receiving an Archipelago item.
        /// </summary>
        public static bool IsItemUnlockedByArchipelago(string itemName)
        {
            return _unlockedItems.TryGetValue(itemName, out bool unlocked) && unlocked;
        }

        /// <summary>
        /// Checks if Warehouse Access has been received from Archipelago.
        /// </summary>
        public static bool IsWarehouseAccessReceived()
        {
            return _warehouseAccessReceived;
        }

        /// <summary>
        /// Checks if Westville Region Unlock has been received from Archipelago.
        /// </summary>
        public static bool IsWestvilleRegionReceived()
        {
            return _westvilleRegionReceived;
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
                MelonLogger.Error($"[Levels] Error in ProcessPendingSync: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when the player ranks up.
        /// Sends checks for the new rank AND any previous ranks that were skipped.
        /// </summary>
        /// <param name="newRank">The new rank achieved.</param>
        public static void OnRankUp(FullRank newRank)
        {
            string rankName = FormatRankName(newRank);
            string locationName = $"Rank {rankName}";

            MelonLogger.Msg($"[Levels] Player ranked up to: {rankName}");

            // Send checks for all ranks up to and including the new rank
            // This handles cases where multiple ranks are gained at once (skipping levels)
            SendMissingRankChecks(newRank);
        }

        /// <summary>
        /// Sends a location check for reaching a rank.
        /// </summary>
        private static void SendRankLocationCheck(string locationName)
        {
            int locationId = Data_Locations.GetLocationId(locationName);
            if (locationId > 0)
            {
                MelonLogger.Msg($"[Levels] Sending check: '{locationName}' (ID: {locationId})");
                NarcopelagoLocations.CompleteLocation(locationId);
            }
            else
            {
                // Not all ranks have location checks (e.g., Street Rat I and II might not)
                MelonLogger.Msg($"[Levels] No location found for rank: '{locationName}'");
            }
        }

        /// <summary>
        /// Formats a FullRank to match the location name format.
        /// E.g., FullRank(Street_Rat, 3) -> "Street Rat III"
        /// </summary>
        private static string FormatRankName(FullRank rank)
        {
            // Convert enum to string and replace underscores with spaces
            string rankName = rank.Rank.ToString().Replace("_", " ");

            // Convert tier to Roman numeral
            string tierNumeral = rank.Tier switch
            {
                1 => "I",
                2 => "II",
                3 => "III",
                4 => "IV",
                5 => "V",
                _ => rank.Tier.ToString()
            };

            return $"{rankName} {tierNumeral}";
        }

        /// <summary>
        /// Syncs from the Archipelago session.
        /// </summary>
        public static void SyncFromSession()
        {
            QueueSyncFromSession(120);
        }

        /// <summary>
        /// Internal sync implementation.
        /// Syncs unlock items and rank checks from the session.
        /// </summary>
        private static void SyncFromSessionInternal()
        {
            // Sync unlock items from Archipelago
            SyncUnlockItemsFromSession();

            // Sync completed ranks from Archipelago to avoid re-sending
            SyncCompletedRanksFromSession();

            // Then check current rank and send any missing checks
            try
            {
                if (!NetworkSingleton<LevelManager>.InstanceExists)
                {
                    MelonLogger.Msg("[Levels] LevelManager not available for sync");
                    return;
                }

                var levelManager = NetworkSingleton<LevelManager>.Instance;
                FullRank currentRank = levelManager.GetFullRank();

                MelonLogger.Msg($"[Levels] Current rank: {FormatRankName(currentRank)} - checking for missing rank checks");

                // Send checks for all ranks up to current rank that haven't been sent
                SendMissingRankChecks(currentRank);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Levels] Error syncing current rank: {ex.Message}");
            }
        }

        /// <summary>
        /// Syncs unlock items from the Archipelago session.
        /// </summary>
        private static void SyncUnlockItemsFromSession()
        {
            var session = ConnectionHandler.CurrentSession;
            if (session?.Items?.AllItemsReceived == null)
            {
                MelonLogger.Msg("[Levels] Cannot sync unlock items - no session or items");
                return;
            }

            MelonLogger.Msg("[Levels] Syncing unlock items from session...");

            foreach (var item in session.Items.AllItemsReceived)
            {
                string itemName = item.ItemName;

                // Check if this is a Level Up Reward item
                if (Data_Items.HasTag(itemName, "Level Up Reward"))
                {
                    if (itemName == "Warehouse Access")
                    {
                        _warehouseAccessReceived = true;
                        // Always queue for re-application on load in case save didn't persist it
                        _pendingUnlocks.Enqueue(itemName);
                        MelonLogger.Msg("[Levels] Synced: Warehouse Access");
                    }
                    else if (itemName == "Westville Region Unlock")
                    {
                        _westvilleRegionReceived = true;
                        // Always queue for re-application on load
                        _pendingUnlocks.Enqueue(itemName);
                        MelonLogger.Msg("[Levels] Synced: Westville Region Unlock");
                    }
                    else if (itemName.EndsWith(" Unlock"))
                    {
                        string baseItemName = itemName.Substring(0, itemName.Length - " Unlock".Length);
                        if (!_unlockedItems.ContainsKey(baseItemName) || !_unlockedItems[baseItemName])
                        {
                            _unlockedItems[baseItemName] = true;
                            MelonLogger.Msg($"[Levels] Synced unlock: {baseItemName}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Sends location checks for all ranks up to the specified rank that haven't been completed.
        /// Called both during sync and during live rank-ups to catch any skipped levels.
        /// </summary>
        private static void SendMissingRankChecks(FullRank upToRank)
        {
            int checksSent = 0;

            // Iterate through all ranks from Street Rat I to the current rank
            foreach (ERank rankEnum in Enum.GetValues(typeof(ERank)))
            {
                for (int tier = 1; tier <= 5; tier++)
                {
                    FullRank rank = new FullRank(rankEnum, tier);

                    // Stop if we've passed the target rank
                    if (rank > upToRank)
                    {
                        if (checksSent > 0)
                        {
                            MelonLogger.Msg($"[Levels] Sent {checksSent} rank check(s) up to {FormatRankName(upToRank)}");
                        }
                        return;
                    }

                    string rankName = FormatRankName(rank);
                    string locationName = $"Rank {rankName}";

                    // Skip if already completed in our local tracking
                    if (_completedRanks.TryGetValue(locationName, out bool completed) && completed)
                    {
                        continue;
                    }

                    // Check if this location exists in the data
                    int locationId = Data_Locations.GetLocationId(locationName);
                    if (locationId > 0)
                    {
                        // Mark as completed locally
                        _completedRanks[locationName] = true;
                        
                        // Send the check
                        MelonLogger.Msg($"[Levels] Sending rank check: '{locationName}' (ID: {locationId})");
                        NarcopelagoLocations.CompleteLocation(locationId);
                        checksSent++;
                    }
                }
            }

            if (checksSent > 0)
            {
                MelonLogger.Msg($"[Levels] Sent {checksSent} rank check(s)");
            }
        }

        /// <summary>
        /// Syncs completed rank locations from Archipelago.
        /// </summary>
        private static void SyncCompletedRanksFromSession()
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

            // Get all Level Up Reward locations
            var levelUpLocations = Data_Locations.GetLocationsByTag("Level Up Reward");

            int synced = 0;
            foreach (var locationName in levelUpLocations)
            {
                int locationId = Data_Locations.GetLocationId(locationName);
                if (locationId > 0 && checkedLocations.Contains(locationId))
                {
                    if (!_completedRanks.ContainsKey(locationName) || !_completedRanks[locationName])
                    {
                        _completedRanks[locationName] = true;
                        synced++;
                    }
                }
            }

            if (synced > 0)
            {
                MelonLogger.Msg($"[Levels] Synced {synced} completed rank locations from Archipelago");
            }
        }

        /// <summary>
        /// Resets all tracking data.
        /// </summary>
        public static void Reset()
        {
            _completedRanks.Clear();
            _unlockedItems.Clear();
            _warehouseAccessReceived = false;
            _westvilleRegionReceived = false;
            while (_pendingUnlocks.TryDequeue(out _)) { }
            _inGameScene = false;
            _syncPending = false;
        }
    }

    /// <summary>
        /// Harmony patch for LevelManager.RpcLogic___IncreaseTierNetworked_3953286437
        /// Called when the player ranks up (network RPC).
        /// </summary>
        [HarmonyPatch(typeof(LevelManager), "RpcLogic___IncreaseTierNetworked_3953286437")]
        public class LevelManager_IncreaseTier_Patch
        {
            static bool Prepare()
            {
                MelonLogger.Msg("[PATCH] LevelManager.IncreaseTier patch is being prepared");
                return true;
            }

            static void Postfix(FullRank before, FullRank after)
            {
                try
                {
                    MelonLogger.Msg($"[PATCH] Player ranked up (RPC): {before} -> {after}");
                    NarcopelagoLevels.OnRankUp(after);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[PATCH] Error in LevelManager IncreaseTier Postfix: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Harmony patch for RankUpCanvas.RankUp
        /// Called when the rank up UI is triggered - this is the reliable local trigger.
        /// </summary>
        [HarmonyPatch(typeof(RankUpCanvas), "RankUp")]
        public class RankUpCanvas_RankUp_Patch
        {
            static bool Prepare()
            {
                MelonLogger.Msg("[PATCH] RankUpCanvas.RankUp patch is being prepared");
                return true;
            }

            static void Postfix(FullRank oldRank, FullRank newRank)
            {
                try
                {
                    MelonLogger.Msg($"[PATCH] RankUpCanvas triggered: {oldRank} -> {newRank}");
                    NarcopelagoLevels.OnRankUp(newRank);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[PATCH] Error in RankUpCanvas.RankUp Postfix: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Harmony patch for StorableItemDefinition.GetIsUnlocked
        /// When Randomize_level_unlocks is true, blocks items that require level to purchase
        /// unless Archipelago has sent the corresponding unlock item.
        /// </summary>
        [HarmonyPatch(typeof(StorableItemDefinition), "GetIsUnlocked")]
        public class StorableItemDefinition_GetIsUnlocked_Patch
        {
            static bool Prepare()
            {
                MelonLogger.Msg("[PATCH] StorableItemDefinition.GetIsUnlocked patch is being prepared");
                return true;
            }

            /// <summary>
            /// Postfix to control level-based unlocks when Randomize_level_unlocks is true.
            /// </summary>
            static void Postfix(StorableItemDefinition __instance, ref bool __result)
            {
                try
                {
                    // Only modify if Randomize_level_unlocks is enabled
                    if (!NarcopelagoOptions.Randomize_level_unlocks)
                    {
                        return;
                    }

                    // Only affect items that require level to purchase
                    if (!__instance.RequiresLevelToPurchase)
                    {
                        return;
                    }

                    // Check if Archipelago has sent the unlock item for this
                    string itemName = __instance.Name;
                    if (NarcopelagoLevels.IsItemUnlockedByArchipelago(itemName))
                    {
                        // Archipelago has unlocked this item
                        __result = true;
                        return;
                    }

                    // Block the unlock if game would allow it
                    if (__result == true)
                    {
                        __result = false;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[PATCH] Error in StorableItemDefinition.GetIsUnlocked Postfix: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Harmony patch for Map.OnRankUp
        /// When Randomize_level_unlocks is true, blocks Westville region unlock from leveling up.
        /// Westville is unlocked via "Westville Region Unlock" item from Archipelago.
        /// </summary>
        [HarmonyPatch(typeof(Map), "OnRankUp")]
        public class Map_OnRankUp_Patch
        {
            static bool Prepare()
            {
                MelonLogger.Msg("[PATCH] Map.OnRankUp patch is being prepared");
                return true;
            }

            /// <summary>
            /// Prefix to block Westville unlock when Randomize_level_unlocks is true.
            /// </summary>
            static bool Prefix(FullRank old, FullRank newRank)
            {
                try
                {
                    // Only block if Randomize_level_unlocks is enabled
                    if (!NarcopelagoOptions.Randomize_level_unlocks)
                    {
                        return true; // Allow normal behavior
                    }

                    // Block the rank-based unlock - Westville should be unlocked via Archipelago item
                    MelonLogger.Msg($"[PATCH] Blocking Westville region unlock from rank up (Randomize_level_unlocks is true)");
                    return false;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[PATCH] Error in Map.OnRankUp Prefix: {ex.Message}");
                    return true; // Allow on error
                }
            }
        }

        /// <summary>
        /// Harmony patch for DarkMarket.RpcLogic___SetUnlocked_328543758
        /// When Randomize_level_unlocks is true, blocks Dark Market unlock unless 
        /// "Warehouse Access" item has been received from Archipelago.
        /// </summary>
        [HarmonyPatch(typeof(DarkMarket), "RpcLogic___SetUnlocked_328543758")]
        public class DarkMarket_SetUnlocked_Patch
        {
            static bool Prepare()
            {
                MelonLogger.Msg("[PATCH] DarkMarket.SetUnlocked patch is being prepared");
                return true;
            }

            /// <summary>
            /// Prefix to control Dark Market unlock based on Archipelago item.
            /// </summary>
            static bool Prefix()
            {
                try
                {
                    // Only block if Randomize_level_unlocks is enabled
                    if (!NarcopelagoOptions.Randomize_level_unlocks)
                    {
                        return true; // Allow normal behavior
                    }

                    // Check if Warehouse Access has been received from Archipelago
                    if (NarcopelagoLevels.IsWarehouseAccessReceived())
                    {
                        MelonLogger.Msg($"[PATCH] Allowing Dark Market unlock (Warehouse Access received from Archipelago)");
                        return true; // Allow - Archipelago sent the unlock
                    }

                    // Block the unlock
                    MelonLogger.Msg($"[PATCH] Blocking Dark Market unlock (Warehouse Access not received)");
                    return false;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[PATCH] Error in DarkMarket.SetUnlocked Prefix: {ex.Message}");
                    return true; // Allow on error
                }
            }
        }

        /// <summary>
        /// Harmony patch for PhoneShopInterface.ChangeListingQuantity
        /// When Randomize_level_unlocks is true, blocks adding items that require level to purchase
        /// unless Archipelago has sent the corresponding unlock item.
        /// </summary>
        [HarmonyPatch(typeof(PhoneShopInterface), "ChangeListingQuantity")]
        public class PhoneShopInterface_ChangeListingQuantity_Patch
        {
            static bool Prepare()
            {
                MelonLogger.Msg("[PATCH] PhoneShopInterface.ChangeListingQuantity patch is being prepared");
                return true;
            }

            /// <summary>
                        /// Prefix to block adding items that require level when not unlocked by Archipelago.
                        /// </summary>
                        static bool Prefix(PhoneShopInterface.Listing listing, int change)
                        {
                            try
                            {
                                // Only block if Randomize_level_unlocks is enabled
                                if (!NarcopelagoOptions.Randomize_level_unlocks)
                                {
                                    return true; // Allow normal behavior
                                }

                                // Only affect items that require level to purchase
                                if (!listing.Item.RequiresLevelToPurchase)
                                {
                                    return true; // Allow - this item doesn't require level
                                }

                                // Check if Archipelago has unlocked this item
                                string itemName = listing.Item.Name;
                                if (NarcopelagoLevels.IsItemUnlockedByArchipelago(itemName))
                                {
                                    return true; // Allow - Archipelago has unlocked this
                                }

                                // Block if trying to add (not remove) items
                                if (change > 0)
                                {
                                    MelonLogger.Msg($"[PATCH] Blocking phone shop purchase of '{itemName}' (not unlocked by Archipelago)");
                                    return false;
                                }

                                return true; // Allow removing items from cart
                            }
                            catch (Exception ex)
                            {
                                MelonLogger.Error($"[PATCH] Error in PhoneShopInterface.ChangeListingQuantity Prefix: {ex.Message}");
                                return true; // Allow on error
                            }
                        }
                    }

                /// <summary>
                    /// Harmony patch for PhoneShopInterface.Open
                    /// When Randomize_level_unlocks is true, fixes up the UI after it's built
                    /// to show Archipelago-unlocked items as available.
                    /// </summary>
                    [HarmonyPatch(typeof(PhoneShopInterface), "Open")]
                    public class PhoneShopInterface_Open_Patch
                    {
                        static bool Prepare()
                        {
                            MelonLogger.Msg("[PATCH] PhoneShopInterface.Open patch is being prepared");
                            return true;
                        }

                        /// <summary>
                        /// Postfix to fix up the UI for items unlocked by Archipelago.
                        /// The original Open method shows items as locked based on level,
                        /// but we need to unlock them if Archipelago has sent the unlock item.
                        /// </summary>
                        static void Postfix(PhoneShopInterface __instance, Il2CppSystem.Collections.Generic.List<PhoneShopInterface.Listing> listings)
                        {
                            try
                            {
                                // Only modify if Randomize_level_unlocks is enabled
                                if (!NarcopelagoOptions.Randomize_level_unlocks)
                                {
                                    return;
                                }

                                // Access the _entries field directly - IL2CPP fields are accessible as properties
                                var entries = __instance._entries;
                                var items = __instance._items;

                                if (entries == null || items == null)
                                {
                                    MelonLogger.Warning("[PATCH] _entries or _items is null");
                                    return;
                                }

                                // Iterate through listings and fix up UI for unlocked items
                                for (int i = 0; i < items.Count && i < entries.Count; i++)
                                {
                                    var listing = items[i];
                                    var entry = entries[i];

                                    // Only process items that require level to purchase
                                    if (!listing.Item.RequiresLevelToPurchase)
                                    {
                                        continue;
                                    }

                                    // Check if this item is unlocked by Archipelago
                                    string itemName = listing.Item.Name;
                                    if (!NarcopelagoLevels.IsItemUnlockedByArchipelago(itemName))
                                    {
                                        continue; // Not unlocked by Archipelago, leave as locked
                                    }

                                    // This item is unlocked by Archipelago - fix up the UI
                                    MelonLogger.Msg($"[PATCH] Fixing up phone shop UI for Archipelago-unlocked item: {itemName}");

                                    // Hide the locked container
                                    var lockedTransform = entry.Find("Locked");
                                    if (lockedTransform != null)
                                    {
                                        lockedTransform.gameObject.SetActive(false);
                                    }

                                    // Add button listeners for quantity changes
                                    var removeButtonTransform = entry.Find("Quantity/Remove");
                                    var addButtonTransform = entry.Find("Quantity/Add");

                                    if (removeButtonTransform != null)
                                    {
                                        var removeButton = removeButtonTransform.GetComponent<Button>();
                                        if (removeButton != null)
                                        {
                                            removeButton.onClick.RemoveAllListeners();
                                            var listingCopy = listing; // Capture for closure
                                            removeButton.onClick.AddListener((UnityAction)(() =>
                                            {
                                                __instance.ChangeListingQuantity(listingCopy, -1);
                                            }));
                                        }
                                    }

                                    if (addButtonTransform != null)
                                    {
                                        var addButton = addButtonTransform.GetComponent<Button>();
                                        if (addButton != null)
                                        {
                                            addButton.onClick.RemoveAllListeners();
                                            var listingCopy = listing; // Capture for closure
                                            addButton.onClick.AddListener((UnityAction)(() =>
                                            {
                                                __instance.ChangeListingQuantity(listingCopy, 1);
                                            }));
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                MelonLogger.Error($"[PATCH] Error in PhoneShopInterface.Open Postfix: {ex.Message}");
                            }
                        }
                    }
                }
