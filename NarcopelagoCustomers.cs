using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Relation;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Variables;
using MelonLoader;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

namespace Narcopelago
{
    /// <summary>
    /// Tracks customer states and handles customer unlock logic for Archipelago.
    /// </summary>
    public static class NarcopelagoCustomers
    {
        /// <summary>
        /// Tracks which customers have been given a successful sample.
        /// Key: Customer name (normalized), Value: true if sample was successful
        /// </summary>
        private static Dictionary<string, bool> _customerSampleStatus = new Dictionary<string, bool>(NormalizedStringComparer.Instance);

        /// <summary>
        /// Tracks which customers have been unlocked via Archipelago items.
        /// Key: Customer name (normalized), Value: true if unlock item received
        /// </summary>
        private static Dictionary<string, bool> _customerUnlockStatus = new Dictionary<string, bool>(NormalizedStringComparer.Instance);

        /// <summary>
        /// Tracks customers that need to be unlocked once the game scene is loaded.
        /// </summary>
        private static HashSet<string> _pendingUnlocks = new HashSet<string>(NormalizedStringComparer.Instance);

        /// <summary>
        /// Thread-safe queue for customer unlocks that need to be processed on the main thread.
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
                MelonLogger.Msg("[Customers] Entered game scene - will process unlocks");
            }
        }

        /// <summary>
        /// Queues a sync from session to be processed after a delay.
        /// This allows the game to fully initialize before we start unlocking NPCs.
        /// </summary>
        public static void QueueSyncFromSession(int delayFrames = 120)
        {
            _syncPending = true;
            _syncDelayFrames = delayFrames;
            MelonLogger.Msg($"[Customers] Queued sync from session ({delayFrames} frames delay)");
        }

        /// <summary>
        /// Call this from the main thread (e.g., in MelonMod.OnUpdate) to process queued unlocks.
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            // Only process if we're in a game scene
            if (!_inGameScene) return;

            // Process pending sync first
            ProcessPendingSync();

            // Process up to 5 unlocks per frame to avoid hitching
            int processed = 0;
            var requeue = new List<string>();

            while (processed < 5 && _mainThreadUnlockQueue.TryDequeue(out string customerName))
            {
                try
                {
                    if (!TryUnlockCustomerInGameInternal(customerName))
                    {
                        // NPC not found yet - requeue for later
                        requeue.Add(customerName);
                    }
                    processed++;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Customers] Error processing queued unlock for '{customerName}': {ex.Message}");
                }
            }

            // Requeue any that couldn't be processed
            foreach (var name in requeue)
            {
                _mainThreadUnlockQueue.Enqueue(name);
            }

            // Process pending POI updates
            ProcessPendingPOIUpdate();
        }

        /// <summary>
        /// Processes pending sync from session.
        /// </summary>
        private static void ProcessPendingSync()
        {
            if (!_syncPending) return;

            // Check if we're still waiting
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
                MelonLogger.Error($"[Customers] Error in ProcessPendingSync: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a customer has received a successful sample.
        /// </summary>
        public static bool HasReceivedSample(string customerName)
        {
            return _customerSampleStatus.TryGetValue(customerName, out bool status) && status;
        }

        /// <summary>
        /// Marks a customer as having received a successful sample.
        /// Sends the location check to Archipelago.
        /// </summary>
        public static void SetSampleReceived(string customerName)
        {
            if (HasReceivedSample(customerName))
            {
                MelonLogger.Msg($"[Customers] '{customerName}' already received sample - skipping");
                return;
            }

            _customerSampleStatus[customerName] = true;
            MelonLogger.Msg($"[Customers] '{customerName}' received successful sample");

            SendCustomerSampleCheck(customerName);
        }

        /// <summary>
        /// Sends the Archipelago location check for a successful customer sample.
        /// </summary>
        private static void SendCustomerSampleCheck(string customerName)
        {
            try
            {
                string locationName = $"Successful Sample: {customerName}";
                int modernId = Data_Locations.GetLocationId(locationName);
                
                if (modernId > 0)
                {
                    MelonLogger.Msg($"[Customers] Sending location check: '{locationName}' (ID: {modernId})");
                    NarcopelagoLocations.CompleteLocation(modernId);
                }
                else
                {
                    MelonLogger.Warning($"[Customers] Could not find location ID for: '{locationName}'");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Customers] Error in SendCustomerSampleCheck: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a customer is unlocked via Archipelago.
        /// </summary>
        public static bool IsCustomerUnlockedViaAP(string customerName)
        {
            return _customerUnlockStatus.TryGetValue(customerName, out bool unlocked) && unlocked;
        }

        /// <summary>
        /// Marks a customer as unlocked via Archipelago item.
        /// If NPCs are loaded, unlocks immediately. Otherwise adds to pending.
        /// Also queues a POI update to show new sampleable customers.
        /// </summary>
        public static void SetCustomerUnlocked(string customerName)
        {
            _customerUnlockStatus[customerName] = true;
            MelonLogger.Msg($"[Customers] '{customerName}' unlocked via Archipelago");

            // Try to unlock immediately, if it fails add to pending
            if (!TryUnlockCustomerInGame(customerName))
            {
                _pendingUnlocks.Add(customerName);
                MelonLogger.Msg($"[Customers] '{customerName}' added to pending (NPCs not loaded yet)");
            }

            // Queue a delayed POI update to refresh the map
            // This will show any new customers that can now be sampled
            // (either this customer or others connected to them)
            if (_inGameScene)
            {
                QueueDelayedPOIUpdate(10); // Short delay to allow unlock to process
            }
        }

        /// <summary>
        /// Process all pending customer unlocks.
        /// Call this when the game scene is fully loaded and NPCs are available.
        /// </summary>
        public static void ProcessPendingUnlocks()
        {
            if (_pendingUnlocks.Count == 0)
            {
                MelonLogger.Msg($"[Customers] No pending unlocks to process");
                return;
            }

            MelonLogger.Msg($"[Customers] Processing {_pendingUnlocks.Count} pending unlocks...");

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

            MelonLogger.Msg($"[Customers] Processed {processed.Count} unlocks, {_pendingUnlocks.Count} still pending");
        }

        /// <summary>
        /// Syncs all unlocked customers from the Archipelago session.
        /// This is the public entry point - it queues a delayed sync.
        /// </summary>
        public static void SyncFromSession()
        {
            // Queue the sync with a delay to allow game to fully initialize
            QueueSyncFromSession(120); // ~2 seconds at 60fps
        }

        /// <summary>
        /// Internal sync implementation. Called from the main thread after delay.
        /// Also syncs completed samples and updates POIs.
        /// </summary>
        private static void SyncFromSessionInternal()
        {
            var session = ConnectionHandler.CurrentSession;
            if (session?.Items?.AllItemsReceived == null)
            {
                MelonLogger.Msg("[Customers] Cannot sync - no session or items");
                return;
            }

            MelonLogger.Msg($"[Customers] Syncing {session.Items.AllItemsReceived.Count} received items...");

            int customerCount = 0;
            foreach (var item in session.Items.AllItemsReceived)
            {
                string itemName = item.ItemName;
                
                // Check if it's a customer unlock item using Data_Items tags
                if (itemName.EndsWith(" Unlocked") && Data_Items.HasTag(itemName, "Customer"))
                {
                    string customerName = itemName.Replace(" Unlocked", "").Trim();
                    
                    // Mark as unlocked in our tracking if not already
                    if (!_customerUnlockStatus.ContainsKey(customerName) || !_customerUnlockStatus[customerName])
                    {
                        _customerUnlockStatus[customerName] = true;
                        customerCount++;
                    }
                    
                    // Try to unlock in game
                    TryUnlockCustomerInGame(customerName);
                }
            }

            MelonLogger.Msg($"[Customers] Synced {customerCount} customer unlocks from session");

            // Also sync completed samples from Archipelago location checks
            SyncCompletedSamplesFromSession();

            // Queue delayed POI update to show/hide customer markers
            // Delay allows unlock queue to drain first
            QueueDelayedPOIUpdate(60); // ~1 second at 60fps
        }

        /// <summary>
        /// Attempts to find and unlock a customer in the game.
        /// Queues the unlock for the main thread to avoid IL2CPP/Unity threading issues.
        /// </summary>
        private static bool TryUnlockCustomerInGame(string customerName)
        {
            // Queue the unlock to be processed on the main thread
            // Unity operations and IL2CPP delegate invocations must happen on the main thread
            _mainThreadUnlockQueue.Enqueue(customerName);
            MelonLogger.Msg($"[Customers] Queued '{customerName}' for main thread unlock");
            return true; // Return true to indicate it was queued (not necessarily unlocked yet)
        }

        /// <summary>
        /// Internal method that actually performs the unlock. Must be called from the main thread.
        /// Returns true if the customer was found (and unlocked or already unlocked), false if NPC not found.
        /// </summary>
        private static bool TryUnlockCustomerInGameInternal(string customerName)
        {
            try
            {
                // Check unlocked customers first
                foreach (var customer in Customer.UnlockedCustomers)
                {
                    if (customer == null || customer.NPC == null) continue;
                    
                    string name = customer.NPC.fullName ?? customer.NPC.FirstName ?? "";
                    if (StringHelper.EqualsNormalized(name, customerName))
                    {
                        // Customer is already in UnlockedCustomers list
                        // But ensure RelationData.Unlocked is also true (for dealer assignment)
                        if (customer.NPC.RelationData != null && !customer.NPC.RelationData.Unlocked)
                        {
                            customer.NPC.RelationData.Unlocked = true;
                            MelonLogger.Msg($"[Customers] '{customerName}' was in UnlockedCustomers but RelationData.Unlocked was false - fixed");
                        }
                        else
                        {
                            MelonLogger.Msg($"[Customers] '{customerName}' already unlocked in-game");
                        }
                        return true;
                    }
                }

                // Check locked customers
                foreach (var customer in Customer.LockedCustomers)
                {
                    if (customer == null || customer.NPC == null) continue;
                    
                    string name = customer.NPC.fullName ?? customer.NPC.FirstName ?? "";
                    if (StringHelper.EqualsNormalized(name, customerName))
                    {
                        var relationData = customer.NPC.RelationData;
                        if (relationData != null && !relationData.Unlocked)
                        {
                            // Mark this as an Archipelago-initiated unlock so the patch allows it
                            Customer_OnCustomerUnlocked_Patch.SetArchipelagoUnlock(true);
                            try
                            {
                                relationData.Unlock(Il2CppScheduleOne.NPCs.Relation.NPCRelationData.EUnlockType.DirectApproach, true);
                                MelonLogger.Msg($"[Customers] '{customerName}' unlocked in-game via Unlock() method");
                            }
                            finally
                            {
                                Customer_OnCustomerUnlocked_Patch.SetArchipelagoUnlock(false);
                            }
                            
                            // Verify the customer was moved to UnlockedCustomers
                            if (!Customer.UnlockedCustomers.Contains(customer))
                            {
                                MelonLogger.Warning($"[Customers] '{customerName}' was not added to UnlockedCustomers - manually adding");
                                Customer.UnlockedCustomers.Add(customer);
                            }
                            if (Customer.LockedCustomers.Contains(customer))
                            {
                                MelonLogger.Warning($"[Customers] '{customerName}' still in LockedCustomers - manually removing");
                                Customer.LockedCustomers.Remove(customer);
                            }
                            
                            // Queue POI update after unlock
                            QueueDelayedPOIUpdate(5);
                            return true;
                        }
                        else if (relationData != null && relationData.Unlocked)
                        {
                            // Customer is in LockedCustomers but RelationData says unlocked
                            // This is a stale state - move them to UnlockedCustomers
                            MelonLogger.Warning($"[Customers] '{customerName}' was in LockedCustomers but RelationData.Unlocked was true - fixing list placement");
                            if (!Customer.UnlockedCustomers.Contains(customer))
                            {
                                Customer.UnlockedCustomers.Add(customer);
                            }
                            Customer.LockedCustomers.Remove(customer);
                            return true;
                        }
                    }
                }

                // Customer not found in either list - might not be loaded yet
                return false;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Customers] Error unlocking '{customerName}': {ex.Message}");
                return true; // Return true to prevent infinite requeue on errors
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
            _inGameScene = false;
            // Clear the queue
            while (_mainThreadUnlockQueue.TryDequeue(out _)) { }
        }

        /// <summary>
            /// Gets all unlocked customers (via Archipelago).
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
            /// Checks if a customer can be sampled based on Archipelago requirements.
            /// A customer can be sampled if:
            /// 1. They haven't already received a successful sample (completed AP location)
            /// 2. They are unlocked in-game OR via Archipelago OR have an unlocked connection
            /// </summary>
            public static bool CanCustomerBeSampled(string customerName)
            {
                // Already sampled - cannot sample again (AP location already checked)
                if (HasReceivedSample(customerName))
                {
                    return false;
                }

                // Check if the customer is unlocked in-game (from save data or game logic)
                // This covers both Randomize_customers = true and false
                if (IsCustomerUnlockedInGame(customerName))
                {
                    return true;
                }

                // Check if the customer is unlocked via Archipelago item
                if (IsCustomerUnlockedViaAP(customerName))
                {
                    return true;
                }

                // Check if any of the customer's connections are unlocked
                // This allows sampling locked customers if they have an unlocked connection
                // (This is how the game normally works - you can sample someone if you know their friend)
                string sampleLocation = Data_Locations.GetSampleLocationForCustomer(customerName);
                var requiredUnlocks = Data_Locations.GetRequiredUnlocksForSample(sampleLocation);

                foreach (var requiredCustomer in requiredUnlocks)
                {
                    // Skip self-reference
                    if (StringHelper.EqualsNormalized(requiredCustomer, customerName))
                        continue;
                        
                    // Check if this connection is unlocked (via AP or in-game)
                    if (IsCustomerUnlockedViaAP(requiredCustomer) || IsCustomerUnlockedInGame(requiredCustomer))
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// Checks if a customer is unlocked in the game's data (not just via Archipelago).
            /// Uses cached Customer reference when available for performance.
            /// </summary>
            public static bool IsCustomerUnlockedInGame(string customerName)
            {
                try
                {
                    // Try to find the customer in the static lists first (much faster)
                    foreach (var customer in Customer.UnlockedCustomers)
                    {
                        if (customer == null || customer.NPC == null) continue;
                        string name = customer.NPC.fullName ?? customer.NPC.FirstName ?? "";
                        if (StringHelper.EqualsNormalized(name, customerName))
                        {
                            return true;
                        }
                    }

                    // Not in unlocked list - check if in locked list (means they exist but are locked)
                    foreach (var customer in Customer.LockedCustomers)
                    {
                        if (customer == null || customer.NPC == null) continue;
                        string name = customer.NPC.fullName ?? customer.NPC.FirstName ?? "";
                        if (StringHelper.EqualsNormalized(name, customerName))
                        {
                            // Found in locked list - check actual RelationData in case lists are stale
                            return customer.NPC.RelationData?.Unlocked ?? false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Customers] Error in IsCustomerUnlockedInGame: {ex.Message}");
                }

                return false;
            }

            /// <summary>
            /// Syncs completed sample locations from Archipelago.
            /// Marks customers as already sampled if their location is checked.
            /// When Randomize_customers is false, also unlocks customers whose samples are checked.
            /// </summary>
            public static void SyncCompletedSamplesFromSession()
            {
                if (!NarcopelagoLocations.IsAvailable)
                {
                    MelonLogger.Msg("[Customers] Cannot sync samples - not connected to Archipelago");
                    return;
                }

                var checkedLocations = NarcopelagoLocations.AllLocationsChecked;
                if (checkedLocations == null)
                {
                    MelonLogger.Msg("[Customers] No checked locations available");
                    return;
                }

                int samplesMarked = 0;
                int customersUnlocked = 0;
                var sampleLocations = Data_Locations.GetAllCustomerSampleLocations();

                foreach (var locationName in sampleLocations)
                {
                    int locationId = Data_Locations.GetLocationId(locationName);
                    if (locationId > 0 && checkedLocations.Contains(locationId))
                    {
                        string customerName = Data_Locations.GetCustomerNameFromSampleLocation(locationName);
                        if (!string.IsNullOrEmpty(customerName))
                        {
                            // Mark as sampled if not already
                            if (!HasReceivedSample(customerName))
                            {
                                _customerSampleStatus[customerName] = true;
                                samplesMarked++;
                                MelonLogger.Msg($"[Customers] Marked '{customerName}' as already sampled (from Archipelago)");
                            }

                            // When Randomize_customers is false, unlock customers whose samples are checked
                            // This handles the case where player sampled but didn't save - the unlock should still happen
                            if (!NarcopelagoOptions.Randomize_customers && !IsCustomerUnlockedInGame(customerName))
                            {
                                TryUnlockCustomerInGame(customerName);
                                customersUnlocked++;
                                MelonLogger.Msg($"[Customers] Queued unlock for '{customerName}' (sample was checked but customer not unlocked)");
                            }
                        }
                    }
                }

                MelonLogger.Msg($"[Customers] Synced {samplesMarked} completed samples, queued {customersUnlocked} unlocks from Archipelago");
            }

            /// <summary>
            /// Gets all customers that can currently be sampled.
            /// </summary>
            public static List<string> GetSampleableCustomers()
            {
                var result = new List<string>();
                var sampleLocations = Data_Locations.GetAllCustomerSampleLocations();

                foreach (var locationName in sampleLocations)
                {
                    string customerName = Data_Locations.GetCustomerNameFromSampleLocation(locationName);
                    if (!string.IsNullOrEmpty(customerName) && CanCustomerBeSampled(customerName))
                    {
                        result.Add(customerName);
                    }
                }

                return result;
            }

            /// <summary>
            /// Flag to indicate POI update is pending.
            /// </summary>
            private static bool _poiUpdatePending = false;

            /// <summary>
            /// Counter for delayed POI updates (to allow unlock queue to drain first).
            /// </summary>
            private static int _poiUpdateDelayFrames = 0;

            /// <summary>
            /// Queues a POI update to be processed on the main thread.
            /// </summary>
            public static void QueuePOIUpdate()
            {
                _poiUpdatePending = true;
                _poiUpdateDelayFrames = 0; // Immediate update
            }

            /// <summary>
            /// Queues a POI update with a delay to allow other operations to complete first.
            /// </summary>
            public static void QueueDelayedPOIUpdate(int delayFrames = 30)
            {
                _poiUpdatePending = true;
                _poiUpdateDelayFrames = delayFrames;
                MelonLogger.Msg($"[Customers] Queued delayed POI update ({delayFrames} frames)");
            }

            /// <summary>
            /// Processes pending POI updates. Called from the main thread queue processor.
            /// </summary>
            private static void ProcessPendingPOIUpdate()
            {
                if (!_poiUpdatePending || !_inGameScene) return;
                
                // Check if we're still waiting for delayed update
                if (_poiUpdateDelayFrames > 0)
                {
                    _poiUpdateDelayFrames--;
                    return;
                }

                _poiUpdatePending = false;

                try
                {
                    UpdateCustomerPOIs();
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Customers] Error updating POIs: {ex.Message}");
                }
            }

            /// <summary>
                /// Updates all customer POIs based on current sample eligibility.
                /// Shows POIs for customers who can be sampled, hides for those who cannot.
                /// </summary>
                public static void UpdateCustomerPOIs()
                {
                    int shown = 0;
                    int hidden = 0;
                    int processed = 0;

                    // Process locked customers
                    for (int i = 0; i < Customer.LockedCustomers.Count; i++)
                    {
                        var customer = Customer.LockedCustomers[i];
                        if (customer == null || customer.NPC == null) continue;

                        string customerName = customer.NPC.fullName ?? customer.NPC.FirstName ?? "";
                        if (string.IsNullOrEmpty(customerName)) continue;

                        bool canBeSampled = CanCustomerBeSampled(customerName);
                        customer.SetPotentialCustomerPoIEnabled(canBeSampled);

                        if (canBeSampled) shown++;
                        else hidden++;
                        processed++;
                    }

                    // Process unlocked customers
                    for (int i = 0; i < Customer.UnlockedCustomers.Count; i++)
                    {
                        var customer = Customer.UnlockedCustomers[i];
                        if (customer == null || customer.NPC == null) continue;

                        string customerName = customer.NPC.fullName ?? customer.NPC.FirstName ?? "";
                        if (string.IsNullOrEmpty(customerName)) continue;

                        bool canBeSampled = CanCustomerBeSampled(customerName);
                        customer.SetPotentialCustomerPoIEnabled(canBeSampled);

                        if (canBeSampled) shown++;
                        else hidden++;
                        processed++;
                    }

                    if (processed == 0)
                    {
                        MelonLogger.Msg("[Customers] No customers found for POI update");
                        return;
                    }

                    MelonLogger.Msg($"[Customers] POI update: {shown} shown, {hidden} hidden");
                }
            }

    /// <summary>
    /// Harmony patch for Customer.ShowDirectApproachOption
    /// Allows offering samples to customers based on Archipelago requirements:
    /// - Customer hasn't received a successful sample yet
    /// - Customer is unlocked OR has a connection that is unlocked
    /// </summary>
    [HarmonyPatch(typeof(Il2CppScheduleOne.Economy.Customer), "ShowDirectApproachOption")]
    public class Customer_ShowDirectApproachOption_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] Customer.ShowDirectApproachOption patch is being prepared");
            return true;
        }

        /// <summary>
            /// Postfix to override the result - allow samples based on Archipelago eligibility.
            /// Always allows sampling unlocked customers who haven't completed their location check.
            /// </summary>
            static void Postfix(Il2CppScheduleOne.Economy.Customer __instance, bool enabled, ref bool __result)
            {
                try
                {
                    // If not enabled, keep false
                    if (!enabled) return;

                    // Get customer name
                    string customerName = __instance.NPC?.fullName ?? __instance.NPC?.FirstName ?? "";
                    if (string.IsNullOrEmpty(customerName)) return;

                    // Check if customer has already completed their sample location check
                    bool alreadySampled = NarcopelagoCustomers.HasReceivedSample(customerName);

                    // If already sampled for Archipelago, don't show the option
                    if (alreadySampled)
                    {
                        __result = false;
                        return;
                    }

                    // If the original method returned true, keep it
                    if (__result) return;

                    // Original returned false - check if we should override
                    // This happens when customer is already unlocked (original blocks sampling unlocked customers)
                    
                    // Player pursuit check
                    var player = Player.Local;
                    if (player != null && player.CrimeData != null)
                    {
                        if (player.CrimeData.CurrentPursuitLevel != PlayerCrimeData.EPursuitLevel.None && 
                            player.CrimeData.TimeSinceSighted < 5f)
                        {
                            return; // Keep false - player is being pursued
                        }
                    }

                    // Get the CustomerData to check CanBeDirectlyApproached
                    var customerData = __instance.customerData;
                    if (customerData == null || !customerData.CanBeDirectlyApproached)
                    {
                        return; // Keep false
                    }

                    // Check if awaiting delivery
                    if (__instance.IsAwaitingDelivery)
                    {
                        return; // Keep false
                    }

                    // Check if customer is unlocked in-game (can be sampled for Archipelago location)
                    if (NarcopelagoCustomers.IsCustomerUnlockedInGame(customerName))
                    {
                        // Customer is unlocked but hasn't been sampled for Archipelago - allow sampling
                        __result = true;
                        return;
                    }

                    // If Randomize_customers is enabled, also check AP eligibility
                    if (NarcopelagoOptions.IsLoaded && NarcopelagoOptions.Randomize_customers)
                    {
                        if (NarcopelagoCustomers.CanCustomerBeSampled(customerName))
                        {
                            __result = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[PATCH] Error in ShowDirectApproachOption Postfix: {ex.Message}");
                }
            }
        }

    /// <summary>
    /// Harmony patch for Customer.SampleOptionValid
    /// This method validates if the sample option can be used when clicked.
    /// We override the IsMutuallyKnown() check to use Archipelago eligibility instead.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppScheduleOne.Economy.Customer), "SampleOptionValid")]
    public class Customer_SampleOptionValid_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] Customer.SampleOptionValid patch is being prepared");
            return true;
        }

        /// <summary>
        /// Prefix to override the validation - allow samples based on Archipelago eligibility.
        /// Always allows sampling unlocked customers who haven't completed their location check.
        /// Handles the sampleOfferedToday check ourselves since original method blocks on Unlocked check.
        /// </summary>
        static bool Prefix(Il2CppScheduleOne.Economy.Customer __instance, ref bool __result, ref string invalidReason)
        {
            try
            {
                string customerName = __instance.NPC?.fullName ?? __instance.NPC?.FirstName ?? "";
                if (string.IsNullOrEmpty(customerName))
                {
                    // Let original method handle it
                    return true;
                }

                // Check if already sampled for Archipelago
                if (NarcopelagoCustomers.HasReceivedSample(customerName))
                {
                    invalidReason = "Already provided a successful sample";
                    __result = false;
                    return false; // Skip original
                }

                // Check if customer is unlocked in-game
                bool isUnlockedInGame = NarcopelagoCustomers.IsCustomerUnlockedInGame(customerName);

                // If customer is unlocked in-game, we handle validation ourselves
                // (original would block because it doesn't allow sampling unlocked customers)
                if (isUnlockedInGame)
                {
                    // Check sampleOfferedToday - access the field directly
                    if (__instance.sampleOfferedToday)
                    {
                        invalidReason = "Sample already offered today";
                        __result = false;
                        return false; // Skip original
                    }

                    // Unlocked customers always have 100% success chance, so no need to check
                    
                    // All checks passed - allow the sample
                    __result = true;
                    invalidReason = string.Empty;
                    return false; // Skip original
                }

                // Customer is not unlocked in-game
                // If Randomize_customers is enabled, check AP eligibility
                if (NarcopelagoOptions.IsLoaded && NarcopelagoOptions.Randomize_customers)
                {
                    bool canBeSampled = NarcopelagoCustomers.CanCustomerBeSampled(customerName);
                    
                    if (canBeSampled)
                    {
                        // Check sample success chance
                        float successChance = __instance.GetSampleRequestSuccessChance();
                        if (successChance == 0f)
                        {
                            invalidReason = "Mutual relationship too low";
                            __result = false;
                            return false; // Skip original
                        }

                        // Check sampleOfferedToday
                        if (__instance.sampleOfferedToday)
                        {
                            invalidReason = "Sample already offered today";
                            __result = false;
                            return false; // Skip original
                        }

                        // All checks passed - allow the sample
                        __result = true;
                        invalidReason = string.Empty;
                        return false; // Skip original
                    }
                }

                // Let original method handle it
                return true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in SampleOptionValid Prefix: {ex.Message}");
                // On error, let original method run
                return true;
            }
        }

        /// <summary>
            /// Postfix to override the result if original blocked due to IsMutuallyKnown or region but we allow it.
            /// Only applies when Randomize_customers is enabled.
            /// </summary>
            static void Postfix(Il2CppScheduleOne.Economy.Customer __instance, ref bool __result, ref string invalidReason)
            {
                try
                {
                    // If Randomize_customers is disabled, let original game logic handle it
                    if (!NarcopelagoOptions.IsLoaded || !NarcopelagoOptions.Randomize_customers)
                    {
                        return; // Don't modify the result
                    }

                    // If original returned true, keep it
                    if (__result) return;

                    // If blocked for daily limit, keep the block
                    if (!string.IsNullOrEmpty(invalidReason) && invalidReason.Contains("already offered"))
                    {
                        return; // Keep the original block
                    }

                    // If blocked for relationship too low, keep the block
                    if (!string.IsNullOrEmpty(invalidReason) && invalidReason.Contains("relationship"))
                    {
                        return; // Keep the original block
                    }

                    // Original blocked due to connections or region check - see if we should override
                    string customerName = __instance.NPC?.fullName ?? __instance.NPC?.FirstName ?? "";
                    if (string.IsNullOrEmpty(customerName)) return;

                    // Check Archipelago eligibility
                    if (NarcopelagoCustomers.CanCustomerBeSampled(customerName))
                    {
                        // We allow it - clear the error and return true
                        invalidReason = string.Empty;
                        __result = true;
                        MelonLogger.Msg($"[PATCH] Overriding SampleOptionValid for '{customerName}' - Archipelago allows sampling");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[PATCH] Error in SampleOptionValid Postfix: {ex.Message}");
                }
            }
        }

        /// <summary>
            /// Harmony patch for Customer.SampleConsumed
            /// This is called on the server after a customer consumes a sample.
            /// We use a Prefix to track when it's called, and check the SuccessfulSampleCount variable
            /// after the method runs to detect if the sample was successful.
            /// </summary>
            [HarmonyPatch(typeof(Il2CppScheduleOne.Economy.Customer), "SampleConsumed")]
            public class Customer_SampleConsumed_Patch
            {
                // Store the sample count before the method runs
                private static float _previousSampleCount = 0f;

                static bool Prepare()
                {
                    MelonLogger.Msg("[PATCH] Customer.SampleConsumed patch is being prepared");
                    return true;
                }

                /// <summary>
                /// Prefix to capture the current successful sample count before the method runs.
                /// </summary>
                static void Prefix()
                {
                    try
                    {
                        // Get current successful sample count from the game's variable database
                        // VariableDatabase is a NetworkSingleton
                        if (Il2CppScheduleOne.DevUtilities.NetworkSingleton<VariableDatabase>.InstanceExists)
                        {
                            var variableDb = Il2CppScheduleOne.DevUtilities.NetworkSingleton<VariableDatabase>.Instance;
                            if (variableDb != null)
                            {
                                _previousSampleCount = variableDb.GetValue<float>("SuccessfulSampleCount");
                                MelonLogger.Msg($"[PATCH] SampleConsumed Prefix - Previous sample count: {_previousSampleCount}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Error($"[PATCH] Error in SampleConsumed Prefix: {ex.Message}");
                    }
                }

                /// <summary>
                    /// Postfix to check if the sample was successful by comparing sample counts.
                    /// </summary>
                    static void Postfix(Il2CppScheduleOne.Economy.Customer __instance)
                    {
                        try
                        {
                            string customerName = __instance.NPC?.fullName ?? __instance.NPC?.FirstName ?? "";
                            if (string.IsNullOrEmpty(customerName))
                            {
                                MelonLogger.Warning("[PATCH] SampleConsumed - Could not get customer name");
                                return;
                            }

                            // Check if the successful sample count increased
                            if (Il2CppScheduleOne.DevUtilities.NetworkSingleton<VariableDatabase>.InstanceExists)
                            {
                                var variableDb = Il2CppScheduleOne.DevUtilities.NetworkSingleton<VariableDatabase>.Instance;
                                if (variableDb != null)
                                {
                                    float currentSampleCount = variableDb.GetValue<float>("SuccessfulSampleCount");
                                    MelonLogger.Msg($"[PATCH] SampleConsumed Postfix - Current sample count: {currentSampleCount}");
                        
                                    if (currentSampleCount > _previousSampleCount)
                                    {
                                        // Sample was successful!
                                        MelonLogger.Msg($"[PATCH] Sample was successful for customer: {customerName}");
                                        NarcopelagoCustomers.SetSampleReceived(customerName);
                            
                                        // When Randomize_customers is true, hide this customer's POI immediately
                                        // since they've completed their sample location
                                        if (NarcopelagoOptions.IsLoaded && NarcopelagoOptions.Randomize_customers)
                                        {
                                            __instance.SetPotentialCustomerPoIEnabled(false);
                                            MelonLogger.Msg($"[PATCH] Disabled POI for '{customerName}' after successful sample");
                                        }
                            
                                        // Queue delayed POI update to refresh all customer markers on the map
                                        // Small delay ensures all state updates have completed
                                        NarcopelagoCustomers.QueueDelayedPOIUpdate(5);
                                    }
                                    else
                                    {
                                        MelonLogger.Msg($"[PATCH] Sample was rejected for customer: {customerName}");
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Error($"[PATCH] Error in SampleConsumed Postfix: {ex.Message}");
                        }
                    }
                }

                /// <summary>
                    /// Harmony patch for Customer.OnCustomerUnlocked
                    /// When Randomize_customers is enabled, this blocks the unlock from proceeding
                    /// when it comes from a DirectApproach (successful sample) but NOT from Archipelago.
                    /// </summary>
                    [HarmonyPatch(typeof(Customer), "OnCustomerUnlocked")]
                    public class Customer_OnCustomerUnlocked_Patch
                    {
                        /// <summary>
                        /// Tracks if we're currently processing an Archipelago-initiated unlock.
                        /// When true, the unlock should proceed even if Randomize_customers is enabled.
                        /// </summary>
                        private static bool _isArchipelagoUnlock = false;

                        /// <summary>
                        /// Call this before triggering an Archipelago unlock to allow it through the patch.
                        /// </summary>
                        public static void SetArchipelagoUnlock(bool value)
                        {
                            _isArchipelagoUnlock = value;
                        }

                        static bool Prepare()
                        {
                            MelonLogger.Msg("[PATCH] Customer.OnCustomerUnlocked patch is being prepared");
                            return true;
                        }

                        /// <summary>
                        /// Prefix that blocks the unlock callback when Randomize_customers is enabled
                        /// and the unlock is from DirectApproach (sample), but NOT from Archipelago.
                        /// </summary>
                        static bool Prefix(Customer __instance, NPCRelationData.EUnlockType unlockType, bool notify)
                        {
                            try
                            {
                                string customerName = __instance.NPC?.fullName ?? __instance.NPC?.FirstName ?? "Unknown";

                                // If this is an Archipelago-initiated unlock, always allow it
                                if (_isArchipelagoUnlock)
                                {
                                    MelonLogger.Msg($"[PATCH] Allowing Archipelago unlock for '{customerName}'");
                                    return true;
                                }

                                // If options not loaded, run original
                                if (!NarcopelagoOptions.IsLoaded)
                                {
                                    return true;
                                }

                                // If Randomize_customers is disabled, run original
                                if (!NarcopelagoOptions.Randomize_customers)
                                {
                                    return true;
                                }

                                // If this is a DirectApproach unlock (from sample), block it
                                if (unlockType == NPCRelationData.EUnlockType.DirectApproach)
                                {
                                    MelonLogger.Msg($"[PATCH] Blocking DirectApproach unlock for '{customerName}' - Randomize_customers enabled");

                                    // We need to undo the unlock that already happened
                                    // The Unlock() method sets Unlocked = true before calling this callback
                                    if (__instance.NPC?.RelationData != null)
                                    {
                                        __instance.NPC.RelationData.Unlocked = false;
                                    }

                                    // Make sure the customer stays in LockedCustomers and not in UnlockedCustomers
                                    if (Customer.UnlockedCustomers.Contains(__instance))
                                    {
                                        Customer.UnlockedCustomers.Remove(__instance);
                                    }
                                    if (!Customer.LockedCustomers.Contains(__instance))
                                    {
                                        Customer.LockedCustomers.Add(__instance);
                                    }

                                    // Don't call UpdatePotentialCustomerPoI here - it will be handled by
                                    // the SampleConsumed patch after it marks the sample as received

                                    return false; // Skip the rest of the unlock logic
                                }

                                // Other unlock types should proceed
                                return true;
                            }
                            catch (Exception ex)
                            {
                                MelonLogger.Error($"[PATCH] Error in OnCustomerUnlocked Prefix: {ex.Message}");
                                return true; // On error, run original
                            }
                        }
                    }

                    /// <summary>
                    /// Harmony patch for Customer.UpdatePotentialCustomerPoI
                    /// Overrides the game's POI visibility logic to use Archipelago's sample eligibility.
                    /// Shows POI for any customer who can still be sampled (hasn't completed their AP location).
                    /// </summary>
                    [HarmonyPatch(typeof(Customer), "UpdatePotentialCustomerPoI")]
                    public class Customer_UpdatePotentialCustomerPoI_Patch
                    {
                        static bool Prepare()
                        {
                            MelonLogger.Msg("[PATCH] Customer.UpdatePotentialCustomerPoI patch is being prepared");
                            return true;
                        }

                        /// <summary>
                        /// Prefix that replaces the game's POI logic with Archipelago's sample eligibility check.
                        /// </summary>
                        static bool Prefix(Customer __instance)
                        {
                            try
                            {
                                // Get the POI - if it doesn't exist, let original handle it
                                if (__instance.potentialCustomerPoI == null)
                                {
                                    return true;
                                }

                                string customerName = __instance.NPC?.fullName ?? __instance.NPC?.FirstName ?? "";
                                if (string.IsNullOrEmpty(customerName))
                                {
                                    return true; // Let original handle it
                                }

                                // Use Archipelago's logic to determine if POI should be shown
                                bool canBeSampled = NarcopelagoCustomers.CanCustomerBeSampled(customerName);
                
                                // Set POI visibility based on whether they can be sampled
                                __instance.SetPotentialCustomerPoIEnabled(canBeSampled);

                                return false; // Skip original method
                            }
                            catch (Exception ex)
                            {
                                MelonLogger.Error($"[PATCH] Error in UpdatePotentialCustomerPoI Prefix: {ex.Message}");
                                return true; // On error, run original
                            }
                        }
                    }
                }

