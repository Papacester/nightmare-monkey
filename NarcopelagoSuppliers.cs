using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Relation;
using MelonLoader;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace Narcopelago
{
    /// <summary>
    /// Tracks supplier states and handles supplier unlock logic for Archipelago.
    /// </summary>
    public static class NarcopelagoSuppliers
    {
        /// <summary>
        /// Tracks which suppliers have completed their befriend location.
        /// Key: Supplier name (normalized), Value: true if befriend location was checked
        /// </summary>
        private static Dictionary<string, bool> _supplierBefriendStatus = new Dictionary<string, bool>(NormalizedStringComparer.Instance);

        /// <summary>
        /// Tracks which suppliers have been unlocked via Archipelago items.
        /// Key: Supplier name (normalized), Value: true if "SupplierName Unlocked" item received
        /// </summary>
        private static Dictionary<string, bool> _supplierUnlockStatus = new Dictionary<string, bool>(NormalizedStringComparer.Instance);

        /// <summary>
        /// Tracks suppliers that need to be unlocked once the game scene is loaded.
        /// </summary>
        private static HashSet<string> _pendingUnlocks = new HashSet<string>(NormalizedStringComparer.Instance);

        /// <summary>
        /// Thread-safe queue for supplier unlocks that need to be processed on the main thread.
        /// </summary>
        private static ConcurrentQueue<string> _mainThreadUnlockQueue = new ConcurrentQueue<string>();

        /// <summary>
        /// Tracks if we're in a game scene where NPCs should be available.
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
        /// Sets whether we're in a game scene. Call this from Core when scene changes.
        /// </summary>
        public static void SetInGameScene(bool inGame)
        {
            _inGameScene = inGame;
            if (inGame)
            {
                MelonLogger.Msg("[Suppliers] Entered game scene - will process unlocks");
            }
        }

        /// <summary>
        /// Queues a sync from session to be processed after a delay.
        /// </summary>
        public static void QueueSyncFromSession(int delayFrames = 120)
        {
            _syncPending = true;
            _syncDelayFrames = delayFrames;
            MelonLogger.Msg($"[Suppliers] Queued sync from session ({delayFrames} frames delay)");
        }

        /// <summary>
        /// Call this from the main thread to process queued unlocks.
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            if (!_inGameScene) return;

            ProcessPendingSync();

            int processed = 0;
            var requeue = new List<string>();

            while (processed < 5 && _mainThreadUnlockQueue.TryDequeue(out string supplierName))
            {
                try
                {
                    if (!TryUnlockSupplierInGameInternal(supplierName))
                    {
                        requeue.Add(supplierName);
                    }
                    processed++;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Suppliers] Error processing queued unlock for '{supplierName}': {ex.Message}");
                }
            }

            foreach (var name in requeue)
            {
                _mainThreadUnlockQueue.Enqueue(name);
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
                MelonLogger.Error($"[Suppliers] Error in ProcessPendingSync: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a supplier's befriend location has been completed.
        /// </summary>
        public static bool HasCompletedBefriendLocation(string supplierName)
        {
            return _supplierBefriendStatus.TryGetValue(supplierName, out bool status) && status;
        }

        /// <summary>
        /// Marks a supplier's befriend location as completed.
        /// Sends the location check to Archipelago.
        /// </summary>
        public static void SetBefriendCompleted(string supplierName)
        {
            if (HasCompletedBefriendLocation(supplierName))
            {
                MelonLogger.Msg($"[Suppliers] '{supplierName}' already befriended - skipping");
                return;
            }

            _supplierBefriendStatus[supplierName] = true;
            MelonLogger.Msg($"[Suppliers] '{supplierName}' befriend completed");

            SendSupplierBefriendCheck(supplierName);
        }

        /// <summary>
        /// Sends the Archipelago location check for a supplier befriend.
        /// </summary>
        private static void SendSupplierBefriendCheck(string supplierName)
        {
            try
            {
                string locationName = Data_Locations.GetBefriendLocationForSupplier(supplierName);
                if (string.IsNullOrEmpty(locationName))
                {
                    MelonLogger.Msg($"[Suppliers] No location found for supplier '{supplierName}' - skipping check");
                    return;
                }

                int modernId = Data_Locations.GetLocationId(locationName);

                if (modernId > 0)
                {
                    MelonLogger.Msg($"[Suppliers] Sending location check: '{locationName}' (ID: {modernId})");
                    NarcopelagoLocations.CompleteLocation(modernId);
                }
                else
                {
                    MelonLogger.Warning($"[Suppliers] Could not find location ID for: '{locationName}'");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Suppliers] Error in SendSupplierBefriendCheck: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a supplier is unlocked via Archipelago.
        /// </summary>
        public static bool IsSupplierUnlockedViaAP(string supplierName)
        {
            return _supplierUnlockStatus.TryGetValue(supplierName, out bool unlocked) && unlocked;
        }

        /// <summary>
        /// Marks a supplier as unlocked via Archipelago item.
        /// Unlocks the supplier in-game.
        /// </summary>
        public static void SetSupplierUnlocked(string supplierName)
        {
            _supplierUnlockStatus[supplierName] = true;
            MelonLogger.Msg($"[Suppliers] '{supplierName}' unlocked via Archipelago");

            // Unlock the supplier in-game
            if (!TryUnlockSupplierInGame(supplierName))
            {
                _pendingUnlocks.Add(supplierName);
                MelonLogger.Msg($"[Suppliers] '{supplierName}' added to pending (NPCs not loaded yet)");
            }
        }

        /// <summary>
        /// Syncs all unlocked suppliers from the Archipelago session.
        /// </summary>
        public static void SyncFromSession()
        {
            QueueSyncFromSession(120);
        }

        /// <summary>
        /// Internal sync implementation.
        /// </summary>
        private static void SyncFromSessionInternal()
        {
            var session = ConnectionHandler.CurrentSession;
            if (session?.Items?.AllItemsReceived == null)
            {
                MelonLogger.Msg("[Suppliers] Cannot sync - no session or items");
                return;
            }

            MelonLogger.Msg($"[Suppliers] Syncing {session.Items.AllItemsReceived.Count} received items...");

            int supplierCount = 0;
            foreach (var item in session.Items.AllItemsReceived)
            {
                string itemName = item.ItemName;

                // Check if it's a supplier unlock item
                if (itemName.EndsWith(" Unlocked") && Data_Items.HasTag(itemName, "Supplier"))
                {
                    string supplierName = itemName.Replace(" Unlocked", "").Trim();

                    if (!_supplierUnlockStatus.ContainsKey(supplierName) || !_supplierUnlockStatus[supplierName])
                    {
                        _supplierUnlockStatus[supplierName] = true;
                        supplierCount++;
                    }

                    TryUnlockSupplierInGame(supplierName);
                }
            }

            MelonLogger.Msg($"[Suppliers] Synced {supplierCount} supplier unlocks from session");

            SyncCompletedBefriendsFromSession();
        }

        /// <summary>
        /// Syncs completed befriend locations from Archipelago.
        /// </summary>
        public static void SyncCompletedBefriendsFromSession()
        {
            if (!NarcopelagoLocations.IsAvailable)
            {
                MelonLogger.Msg("[Suppliers] Cannot sync befriends - not connected to Archipelago");
                return;
            }

            var checkedLocations = NarcopelagoLocations.AllLocationsChecked;
            if (checkedLocations == null)
            {
                MelonLogger.Msg("[Suppliers] No checked locations available");
                return;
            }

            int befriendsMarked = 0;
            int suppliersUnlocked = 0;
            var befriendLocations = Data_Locations.GetAllSupplierBefriendLocations();

            foreach (var locationName in befriendLocations)
            {
                int locationId = Data_Locations.GetLocationId(locationName);
                if (locationId > 0 && checkedLocations.Contains(locationId))
                {
                    string supplierName = Data_Locations.GetSupplierNameFromBefriendLocation(locationName);
                    if (!string.IsNullOrEmpty(supplierName))
                    {
                        if (!HasCompletedBefriendLocation(supplierName))
                        {
                            _supplierBefriendStatus[supplierName] = true;
                            befriendsMarked++;
                            MelonLogger.Msg($"[Suppliers] Marked '{supplierName}' as already befriended (from Archipelago)");
                        }

                        // When Randomize_suppliers is false, unlock suppliers whose locations are checked
                        if (!NarcopelagoOptions.Randomize_suppliers && !IsSupplierUnlockedInGame(supplierName))
                        {
                            TryUnlockSupplierInGame(supplierName);
                            suppliersUnlocked++;
                            MelonLogger.Msg($"[Suppliers] Queued unlock for '{supplierName}' (location was checked but supplier not unlocked)");
                        }
                    }
                }
            }

            MelonLogger.Msg($"[Suppliers] Synced {befriendsMarked} completed befriends, queued {suppliersUnlocked} unlocks from Archipelago");
        }

        /// <summary>
        /// Queues a supplier unlock for the main thread.
        /// </summary>
        private static bool TryUnlockSupplierInGame(string supplierName)
        {
            _mainThreadUnlockQueue.Enqueue(supplierName);
            MelonLogger.Msg($"[Suppliers] Queued '{supplierName}' for main thread unlock");
            return true;
        }

        /// <summary>
        /// Actually performs the unlock. Must be called from main thread.
        /// </summary>
        private static bool TryUnlockSupplierInGameInternal(string supplierName)
        {
            try
            {
                // Find the supplier in NPCRegistry
                foreach (var npc in NPCManager.NPCRegistry)
                {
                    if (npc == null) continue;

                    // Check if this NPC is a Supplier
                    var supplier = npc.TryCast<Supplier>();
                    if (supplier == null) continue;

                    string name = supplier.fullName ?? supplier.FirstName ?? "";
                    if (StringHelper.EqualsNormalized(name, supplierName))
                    {
                        if (!supplier.RelationData.Unlocked)
                        {
                            Supplier_SetUnlocked_Patch.SetArchipelagoUnlock(true);
                            try
                            {
                                supplier.RelationData.Unlock(NPCRelationData.EUnlockType.Recommendation, true);
                                MelonLogger.Msg($"[Suppliers] '{supplierName}' unlocked in-game");
                            }
                            finally
                            {
                                Supplier_SetUnlocked_Patch.SetArchipelagoUnlock(false);
                            }
                            return true;
                        }
                        else
                        {
                            MelonLogger.Msg($"[Suppliers] '{supplierName}' already unlocked in-game");
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Suppliers] Error unlocking '{supplierName}': {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// Checks if a supplier is unlocked in-game.
        /// </summary>
        public static bool IsSupplierUnlockedInGame(string supplierName)
        {
            try
            {
                foreach (var npc in NPCManager.NPCRegistry)
                {
                    if (npc == null) continue;

                    var supplier = npc.TryCast<Supplier>();
                    if (supplier == null) continue;

                    string name = supplier.fullName ?? supplier.FirstName ?? "";
                    if (StringHelper.EqualsNormalized(name, supplierName))
                    {
                        return supplier.RelationData?.Unlocked ?? false;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Suppliers] Error in IsSupplierUnlockedInGame: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Resets all supplier tracking data.
        /// </summary>
        public static void Reset()
        {
            _supplierBefriendStatus.Clear();
            _supplierUnlockStatus.Clear();
            _pendingUnlocks.Clear();
            _inGameScene = false;
            while (_mainThreadUnlockQueue.TryDequeue(out _)) { }
        }
    }

    /// <summary>
        /// Harmony patch for Customer.RecommendSupplier
        /// This is called when a customer recommends a supplier to the player after a deal.
        /// Controls when supplier unlock is allowed based on AP item status.
        /// </summary>
        [HarmonyPatch(typeof(Customer), "RecommendSupplier")]
        public class Customer_RecommendSupplier_Patch
        {
            static bool Prepare()
            {
                MelonLogger.Msg("[PATCH] Customer.RecommendSupplier patch is being prepared");
                return true;
            }

            static bool Prefix(Supplier supplier)
            {
                try
                {
                    if (supplier == null)
                    {
                        return true; // Let original handle null case
                    }

                    string supplierName = supplier.fullName ?? supplier.FirstName ?? "Unknown";
                    MelonLogger.Msg($"[PATCH] RecommendSupplier called for '{supplierName}'");

                    // Albert Hoover is the default supplier - don't interfere
                    if (supplierName.Equals("Albert Hoover", StringComparison.OrdinalIgnoreCase))
                    {
                        MelonLogger.Msg($"[PATCH] Allowing recommendation for default supplier '{supplierName}'");
                        return true;
                    }

                    // If already unlocked, let original handle it
                    if (supplier.RelationData != null && supplier.RelationData.Unlocked)
                    {
                        MelonLogger.Msg($"[PATCH] Supplier '{supplierName}' already unlocked");
                        return true;
                    }

                    // When Randomize_suppliers is false, send location check and allow unlock
                    if (!NarcopelagoOptions.Randomize_suppliers)
                    {
                        NarcopelagoSuppliers.SetBefriendCompleted(supplierName);
                        MelonLogger.Msg($"[PATCH] Allowing recommendation for supplier '{supplierName}' - Randomize_suppliers is false");
                        return true;
                    }

                    // Randomize_suppliers is true - check if we have the AP item
                    if (NarcopelagoSuppliers.IsSupplierUnlockedViaAP(supplierName))
                    {
                        // Have AP item - allow unlock (no location check sent when Randomize_suppliers is true)
                        MelonLogger.Msg($"[PATCH] Allowing recommendation for supplier '{supplierName}' - has AP item");
                        return true;
                    }

                    // Don't have AP item - block the recommendation entirely
                    MelonLogger.Msg($"[PATCH] Blocking recommendation for supplier '{supplierName}' - no AP item");
                    return false;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[PATCH] Error in RecommendSupplier Prefix: {ex.Message}");
                    return true;
                }
            }
        }

        /// <summary>
        /// Harmony patch for Supplier.RpcLogic___SetUnlocked_2166136261
        /// This is the observer-side unlock. Backup patch in case unlock is triggered another way.
        /// </summary>
        [HarmonyPatch(typeof(Supplier), "RpcLogic___SetUnlocked_2166136261")]
        public class Supplier_SetUnlocked_Patch
        {
            private static bool _isArchipelagoUnlock = false;

            public static void SetArchipelagoUnlock(bool value)
            {
                _isArchipelagoUnlock = value;
            }

            static bool Prepare()
            {
                MelonLogger.Msg("[PATCH] Supplier.SetUnlocked patch is being prepared");
                return true;
            }

            static bool Prefix(Supplier __instance)
            {
                try
                {
                    string supplierName = __instance.fullName ?? __instance.FirstName ?? "Unknown";

                    // Albert Hoover is the default supplier - don't interfere
                    if (supplierName.Equals("Albert Hoover", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    // If this is an Archipelago-initiated unlock, always allow
                    if (_isArchipelagoUnlock)
                    {
                        MelonLogger.Msg($"[PATCH] Allowing Archipelago unlock for supplier '{supplierName}'");
                        return true;
                    }

                    // If already unlocked, let original handle it
                    if (__instance.RelationData.Unlocked)
                    {
                        return true;
                    }

                    // When Randomize_suppliers is false, allow
                    if (!NarcopelagoOptions.Randomize_suppliers)
                    {
                        return true;
                    }

                    // Randomize_suppliers is true - check if we have the AP item
                    if (NarcopelagoSuppliers.IsSupplierUnlockedViaAP(supplierName))
                    {
                        return true;
                    }

                    // Don't have AP item - block unlock
                    MelonLogger.Msg($"[PATCH] Blocking SetUnlocked for supplier '{supplierName}' - no AP item");
                    return false;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[PATCH] Error in Supplier SetUnlocked Prefix: {ex.Message}");
                    return true;
                }
            }
        }
    }
