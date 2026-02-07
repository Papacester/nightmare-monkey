using HarmonyLib;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Relation;
using MelonLoader;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Narcopelago
{
    /// <summary>
    /// Tracks dealer states and handles dealer recruitment logic for Archipelago.
    /// </summary>
    public static class NarcopelagoDealers
    {
        /// <summary>
        /// Tracks which dealers have been recruited (location check completed).
        /// Key: Dealer name (normalized), Value: true if recruitment location was checked
        /// </summary>
        private static Dictionary<string, bool> _dealerRecruitStatus = new Dictionary<string, bool>(NormalizedStringComparer.Instance);

        /// <summary>
        /// Tracks which dealers have been unlocked via Archipelago items.
        /// Key: Dealer name (normalized), Value: true if "DealerName Recruited" item received
        /// </summary>
        private static Dictionary<string, bool> _dealerUnlockStatus = new Dictionary<string, bool>(NormalizedStringComparer.Instance);

        /// <summary>
        /// Tracks dealers that need to be recruited once the game scene is loaded.
        /// </summary>
        private static HashSet<string> _pendingRecruits = new HashSet<string>(NormalizedStringComparer.Instance);

        /// <summary>
        /// Thread-safe queue for dealer recruitments that need to be processed on the main thread.
        /// </summary>
        private static ConcurrentQueue<string> _mainThreadRecruitQueue = new ConcurrentQueue<string>();

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
        /// Flag to indicate POI update is pending.
        /// </summary>
        private static bool _poiUpdatePending = false;

        /// <summary>
        /// Counter for delayed POI updates.
        /// </summary>
        private static int _poiUpdateDelayFrames = 0;

        /// <summary>
        /// Sets whether we're in a game scene. Call this from Core when scene changes.
        /// </summary>
        public static void SetInGameScene(bool inGame)
        {
            _inGameScene = inGame;
            if (inGame)
            {
                MelonLogger.Msg("[Dealers] Entered game scene - will process recruitments");
            }
        }

        /// <summary>
        /// Queues a sync from session to be processed after a delay.
        /// </summary>
        public static void QueueSyncFromSession(int delayFrames = 120)
        {
            _syncPending = true;
            _syncDelayFrames = delayFrames;
            MelonLogger.Msg($"[Dealers] Queued sync from session ({delayFrames} frames delay)");
        }

        /// <summary>
        /// Call this from the main thread to process queued recruitments.
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            if (!_inGameScene) return;

            ProcessPendingSync();

            int processed = 0;
            var requeue = new List<string>();

            while (processed < 5 && _mainThreadRecruitQueue.TryDequeue(out string dealerName))
            {
                try
                {
                    if (!TryRecruitDealerInGameInternal(dealerName))
                    {
                        requeue.Add(dealerName);
                    }
                    processed++;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Dealers] Error processing queued recruit for '{dealerName}': {ex.Message}");
                }
            }

            foreach (var name in requeue)
            {
                _mainThreadRecruitQueue.Enqueue(name);
            }

            ProcessPendingPOIUpdate();
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
                MelonLogger.Error($"[Dealers] Error in ProcessPendingSync: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a dealer's recruit location has been completed.
        /// </summary>
        public static bool HasCompletedRecruitLocation(string dealerName)
        {
            return _dealerRecruitStatus.TryGetValue(dealerName, out bool status) && status;
        }

        /// <summary>
        /// Marks a dealer's recruit location as completed.
        /// Sends the location check to Archipelago.
        /// </summary>
        public static void SetRecruitCompleted(string dealerName)
        {
            if (HasCompletedRecruitLocation(dealerName))
            {
                MelonLogger.Msg($"[Dealers] '{dealerName}' already recruited - skipping");
                return;
            }

            _dealerRecruitStatus[dealerName] = true;
            MelonLogger.Msg($"[Dealers] '{dealerName}' recruitment completed");

            SendDealerRecruitCheck(dealerName);
        }

        /// <summary>
        /// Sends the Archipelago location check for a dealer recruitment.
        /// </summary>
        private static void SendDealerRecruitCheck(string dealerName)
        {
            try
            {
                string locationName = Data_Locations.GetRecruitLocationForDealer(dealerName);
                if (string.IsNullOrEmpty(locationName))
                {
                    MelonLogger.Msg($"[Dealers] No location found for dealer '{dealerName}' - skipping check");
                    return;
                }

                int modernId = Data_Locations.GetLocationId(locationName);

                if (modernId > 0)
                {
                    MelonLogger.Msg($"[Dealers] Sending location check: '{locationName}' (ID: {modernId})");
                    NarcopelagoLocations.CompleteLocation(modernId);
                }
                else
                {
                    MelonLogger.Warning($"[Dealers] Could not find location ID for: '{locationName}'");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Dealers] Error in SendDealerRecruitCheck: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a dealer is unlocked via Archipelago.
        /// </summary>
        public static bool IsDealerUnlockedViaAP(string dealerName)
        {
            return _dealerUnlockStatus.TryGetValue(dealerName, out bool unlocked) && unlocked;
        }

        /// <summary>
        /// Marks a dealer as having their AP item received.
        /// This enables the recruitment dialogue but does NOT recruit them in-game
        /// unless the location check is also complete.
        /// 
        /// When Randomize_dealers is true:
        /// - If location NOT complete: Just enable dialogue (don't recruit)
        /// - If location IS complete: Recruit them in-game
        /// 
        /// When Randomize_dealers is false:
        /// - Always recruit them in-game
        /// </summary>
        public static void SetDealerRecruited(string dealerName)
        {
            _dealerUnlockStatus[dealerName] = true;
            MelonLogger.Msg($"[Dealers] '{dealerName}' AP item received - recruitment dialogue now available");

            // Check if we should recruit in-game
            if (!NarcopelagoOptions.Randomize_dealers)
            {
                // Randomize_dealers is false - recruit them immediately
                MelonLogger.Msg($"[Dealers] Recruiting '{dealerName}' - Randomize_dealers is false");
                if (!TryRecruitDealerInGame(dealerName))
                {
                    _pendingRecruits.Add(dealerName);
                }
            }
            else if (HasCompletedRecruitLocation(dealerName))
            {
                // Randomize_dealers is true AND location is complete - recruit them
                MelonLogger.Msg($"[Dealers] Recruiting '{dealerName}' - location already complete");
                if (!TryRecruitDealerInGame(dealerName))
                {
                    _pendingRecruits.Add(dealerName);
                }
            }
            else
            {
                // Randomize_dealers is true but location NOT complete
                // Don't recruit - player needs to use dialogue first
                MelonLogger.Msg($"[Dealers] NOT recruiting '{dealerName}' - location not complete (dialogue available)");
            }

            if (_inGameScene)
            {
                QueueDelayedPOIUpdate(10);
            }
        }

        /// <summary>
        /// Syncs all recruited dealers from the Archipelago session.
        /// </summary>
        public static void SyncFromSession()
        {
            QueueSyncFromSession(120);
        }

        /// <summary>
        /// Internal sync implementation.
        /// When Randomize_dealers is true:
        /// - Only recruits dealers in-game if BOTH the AP item is received AND the location is complete
        /// - If only AP item received (no location complete), just enable the dialogue (don't recruit)
        /// </summary>
        private static void SyncFromSessionInternal()
        {
            var session = ConnectionHandler.CurrentSession;
            if (session?.Items?.AllItemsReceived == null)
            {
                MelonLogger.Msg("[Dealers] Cannot sync - no session or items");
                return;
            }

            MelonLogger.Msg($"[Dealers] Syncing {session.Items.AllItemsReceived.Count} received items...");

            // First, sync completed locations from Archipelago
            // This must happen BEFORE we process items so we know which locations are complete
            SyncCompletedRecruitsFromSession();

            int dealerCount = 0;
            foreach (var item in session.Items.AllItemsReceived)
            {
                string itemName = item.ItemName;

                // Check if it's a dealer recruitment item
                if (itemName.EndsWith(" Recruited") && Data_Items.HasTag(itemName, "Dealer"))
                {
                    string dealerName = itemName.Replace(" Recruited", "").Trim();

                    if (!_dealerUnlockStatus.ContainsKey(dealerName) || !_dealerUnlockStatus[dealerName])
                    {
                        _dealerUnlockStatus[dealerName] = true;
                        dealerCount++;
                        MelonLogger.Msg($"[Dealers] Synced AP item for dealer: {dealerName}");
                    }

                    // Only recruit in-game if BOTH conditions are met:
                    // 1. We have the AP item (which we do if we're here)
                    // 2. The location check is complete OR Randomize_dealers is false
                    if (!NarcopelagoOptions.Randomize_dealers)
                    {
                        // Randomize_dealers is false - just recruit them
                        TryRecruitDealerInGame(dealerName);
                    }
                    else if (HasCompletedRecruitLocation(dealerName))
                    {
                        // Randomize_dealers is true AND location is complete - recruit them
                        MelonLogger.Msg($"[Dealers] Recruiting '{dealerName}' - has AP item AND location complete");
                        TryRecruitDealerInGame(dealerName);
                    }
                    else
                    {
                        // Randomize_dealers is true but location NOT complete
                        // Don't recruit - just let the dialogue be available
                        MelonLogger.Msg($"[Dealers] NOT recruiting '{dealerName}' - has AP item but location NOT complete (dialogue available)");
                    }
                }
            }

            MelonLogger.Msg($"[Dealers] Synced {dealerCount} dealer unlocks from session");

            QueueDelayedPOIUpdate(60);
        }

        /// <summary>
        /// Syncs completed recruitment locations from Archipelago.
        /// </summary>
        public static void SyncCompletedRecruitsFromSession()
        {
            if (!NarcopelagoLocations.IsAvailable)
            {
                MelonLogger.Msg("[Dealers] Cannot sync recruits - not connected to Archipelago");
                return;
            }

            var checkedLocations = NarcopelagoLocations.AllLocationsChecked;
            if (checkedLocations == null)
            {
                MelonLogger.Msg("[Dealers] No checked locations available");
                return;
            }

            int recruitsMarked = 0;
            int dealersUnlocked = 0;
            var recruitLocations = Data_Locations.GetAllDealerRecruitLocations();

            foreach (var locationName in recruitLocations)
            {
                int locationId = Data_Locations.GetLocationId(locationName);
                if (locationId > 0 && checkedLocations.Contains(locationId))
                {
                    string dealerName = Data_Locations.GetDealerNameFromRecruitLocation(locationName);
                    if (!string.IsNullOrEmpty(dealerName))
                    {
                        if (!HasCompletedRecruitLocation(dealerName))
                        {
                            _dealerRecruitStatus[dealerName] = true;
                            recruitsMarked++;
                            MelonLogger.Msg($"[Dealers] Marked '{dealerName}' as already recruited (from Archipelago)");
                        }

                        // When Randomize_dealers is false, unlock dealers whose locations are checked
                        if (!NarcopelagoOptions.Randomize_dealers && !IsDealerRecruitedInGame(dealerName))
                        {
                            TryRecruitDealerInGame(dealerName);
                            dealersUnlocked++;
                            MelonLogger.Msg($"[Dealers] Queued recruit for '{dealerName}' (location was checked but dealer not recruited)");
                        }
                    }
                }
            }

            MelonLogger.Msg($"[Dealers] Synced {recruitsMarked} completed recruits, queued {dealersUnlocked} recruitments from Archipelago");
        }

        /// <summary>
        /// Queues a dealer recruitment for the main thread.
        /// </summary>
        private static bool TryRecruitDealerInGame(string dealerName)
        {
            _mainThreadRecruitQueue.Enqueue(dealerName);
            MelonLogger.Msg($"[Dealers] Queued '{dealerName}' for main thread recruit");
            return true;
        }

        /// <summary>
        /// Actually performs the recruitment. Must be called from main thread.
        /// </summary>
        private static bool TryRecruitDealerInGameInternal(string dealerName)
        {
            try
            {
                // Check all player dealers
                foreach (var dealer in Dealer.AllPlayerDealers)
                {
                    if (dealer == null) continue;

                    string name = dealer.fullName ?? dealer.FirstName ?? "";
                    if (StringHelper.EqualsNormalized(name, dealerName))
                    {
                        if (!dealer.IsRecruited)
                        {
                            Dealer_SetIsRecruited_Patch.SetArchipelagoRecruit(true);
                            try
                            {
                                dealer.RpcLogic___InitialRecruitment_2166136261();
                                MelonLogger.Msg($"[Dealers] '{dealerName}' recruited in-game via InitialRecruitment");
                            }
                            finally
                            {
                                Dealer_SetIsRecruited_Patch.SetArchipelagoRecruit(false);
                            }

                            QueueDelayedPOIUpdate(5);
                            return true;
                        }
                        else
                        {
                            MelonLogger.Msg($"[Dealers] '{dealerName}' already recruited in-game");
                            return true;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Dealers] Error recruiting '{dealerName}': {ex.Message}");
                return true;
            }
        }

        /// <summary>
        /// Checks if a dealer is recruited in-game.
        /// </summary>
        public static bool IsDealerRecruitedInGame(string dealerName)
        {
            try
            {
                foreach (var dealer in Dealer.AllPlayerDealers)
                {
                    if (dealer == null) continue;

                    string name = dealer.fullName ?? dealer.FirstName ?? "";
                    if (StringHelper.EqualsNormalized(name, dealerName))
                    {
                        return dealer.IsRecruited;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Dealers] Error in IsDealerRecruitedInGame: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Checks if a dealer can be recruited based on Archipelago requirements.
        /// A dealer's POI should show if:
        /// 1. They haven't completed their AP recruit location
        /// 2. A location exists for them in Archipelago
        /// 3. They are known/recommended (game's normal requirements)
        /// </summary>
        public static bool CanDealerBeRecruited(string dealerName)
        {
            // Already recruited for AP - hide POI
            if (HasCompletedRecruitLocation(dealerName))
            {
                return false;
            }

            // No location exists for this dealer - use game's default logic
            if (!Data_Locations.HasLocationForDealer(dealerName))
            {
                return false;
            }

            // Check if dealer is already recruited in-game
            if (IsDealerRecruitedInGame(dealerName))
            {
                // Recruited but location not checked - still show POI so player can complete the check
                // Wait, if they're recruited, the location should be checked...
                // Unless Randomize_dealers is true and we blocked the recruitment
                return false;
            }

            // Check if unlocked via AP
            if (IsDealerUnlockedViaAP(dealerName))
            {
                return true;
            }

            // Otherwise, show if they meet game's normal requirements (mutually known, recommended)
            // We'll let the game's logic handle this part
            return true;
        }

        /// <summary>
        /// Resets all dealer tracking data.
        /// </summary>
        public static void Reset()
        {
            _dealerRecruitStatus.Clear();
            _dealerUnlockStatus.Clear();
            _pendingRecruits.Clear();
            _inGameScene = false;
            while (_mainThreadRecruitQueue.TryDequeue(out _)) { }
        }

        /// <summary>
        /// Queues a POI update.
        /// </summary>
        public static void QueuePOIUpdate()
        {
            _poiUpdatePending = true;
            _poiUpdateDelayFrames = 0;
        }

        /// <summary>
        /// Queues a delayed POI update.
        /// </summary>
        public static void QueueDelayedPOIUpdate(int delayFrames = 30)
        {
            _poiUpdatePending = true;
            _poiUpdateDelayFrames = delayFrames;
        }

        /// <summary>
        /// Processes pending POI updates.
        /// </summary>
        private static void ProcessPendingPOIUpdate()
        {
            if (!_poiUpdatePending || !_inGameScene) return;

            if (_poiUpdateDelayFrames > 0)
            {
                _poiUpdateDelayFrames--;
                return;
            }

            _poiUpdatePending = false;

            try
            {
                UpdateDealerPOIs();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Dealers] Error updating POIs: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates all dealer POIs based on current recruitment eligibility.
        /// </summary>
        public static void UpdateDealerPOIs()
        {
            int shown = 0;
            int hidden = 0;

            foreach (var dealer in Dealer.AllPlayerDealers)
            {
                if (dealer == null) continue;

                string dealerName = dealer.fullName ?? dealer.FirstName ?? "";
                if (string.IsNullOrEmpty(dealerName)) continue;

                bool shouldShow = ShouldShowDealerPOI(dealerName, dealer);

                if (dealer.PotentialDealerPoI != null)
                {
                    dealer.PotentialDealerPoI.enabled = shouldShow;

                    if (shouldShow) shown++;
                    else hidden++;
                }
            }

            MelonLogger.Msg($"[Dealers] POI update: {shown} shown, {hidden} hidden");
        }

        /// <summary>
        /// Determines if a dealer's POI should be shown.
        /// POI should show if:
        /// 1. Location check is NOT complete (need to recruit for AP)
        /// 2. AND dealer is NOT already recruited in-game
        /// 3. AND either: has AP item OR meets game's normal requirements
        /// </summary>
        public static bool ShouldShowDealerPOI(string dealerName, Dealer dealer)
        {
            // If no location exists for this dealer, use game's default logic
            if (!Data_Locations.HasLocationForDealer(dealerName))
            {
                // Use game's default: show if mutually known and not unlocked
                return dealer.RelationData.IsMutuallyKnown() && !dealer.RelationData.Unlocked;
            }

            // If recruit location already completed, hide POI (no need to recruit again)
            if (HasCompletedRecruitLocation(dealerName))
            {
                return false;
            }

            // If dealer is already recruited in-game, hide POI
            if (dealer.IsRecruited)
            {
                return false;
            }

            // Location NOT complete and dealer NOT recruited in-game
            // Show POI if we have the AP item (allows re-recruiting after game reset)
            if (IsDealerUnlockedViaAP(dealerName))
            {
                return true;
            }

            // Otherwise, use game's default logic (mutually known requirement)
            return dealer.RelationData.IsMutuallyKnown() && !dealer.RelationData.Unlocked;
        }
    }

    /// <summary>
    /// Harmony patch for Dealer.UpdatePotentialDealerPoI
    /// Overrides the game's POI visibility logic to use Archipelago's recruitment eligibility.
    /// </summary>
    [HarmonyPatch(typeof(Dealer), "UpdatePotentialDealerPoI")]
    public class Dealer_UpdatePotentialDealerPoI_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] Dealer.UpdatePotentialDealerPoI patch is being prepared");
            return true;
        }

        static bool Prefix(Dealer __instance)
        {
            try
            {
                if (__instance.PotentialDealerPoI == null)
                {
                    return true;
                }

                string dealerName = __instance.fullName ?? __instance.FirstName ?? "";
                if (string.IsNullOrEmpty(dealerName))
                {
                    return true;
                }

                // Benji Coleman is the tutorial dealer - don't modify his POI
                if (dealerName.Equals("Benji Coleman", StringComparison.OrdinalIgnoreCase))
                {
                    return true; // Use original game logic
                }

                bool shouldShow = NarcopelagoDealers.ShouldShowDealerPOI(dealerName, __instance);
                __instance.PotentialDealerPoI.enabled = shouldShow;

                return false; // Skip original method
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in UpdatePotentialDealerPoI Prefix: {ex.Message}");
                return true;
            }
        }
    }

    /// <summary>
    /// Harmony patch for Dealer.SetIsRecruited (RpcLogic___SetIsRecruited_328543758)
    /// Controls when recruitment is allowed based on AP item status.
    /// </summary>
    [HarmonyPatch(typeof(Dealer), "RpcLogic___SetIsRecruited_328543758")]
    public class Dealer_SetIsRecruited_Patch
    {
        private static bool _isArchipelagoRecruit = false;

        public static void SetArchipelagoRecruit(bool value)
        {
            _isArchipelagoRecruit = value;
        }

        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] Dealer.SetIsRecruited patch is being prepared");
            return true;
        }

        static bool Prefix(Dealer __instance)
            {
                try
                {
                    string dealerName = __instance.fullName ?? __instance.FirstName ?? "Unknown";

                    // Benji Coleman is the tutorial dealer - don't interfere
                    if (dealerName.Equals("Benji Coleman", StringComparison.OrdinalIgnoreCase))
                    {
                        return true; // Use original game logic
                    }

                    // If this is an Archipelago-initiated recruitment (from sync/load), always allow
                    if (_isArchipelagoRecruit)
                    {
                        MelonLogger.Msg($"[PATCH] Allowing Archipelago sync recruit for '{dealerName}'");
                        return true;
                    }

                    // If already recruited, let original handle it
                    if (__instance.IsRecruited)
                    {
                        return true;
                    }

                    // Send the location check (this is completing the recruitment location)
                    NarcopelagoDealers.SetRecruitCompleted(dealerName);

                    // Hide POI after recruitment dialogue
                    if (__instance.PotentialDealerPoI != null)
                    {
                        __instance.PotentialDealerPoI.enabled = false;
                    }

                    // Check if we have the AP item for this dealer
                    bool hasAPItem = NarcopelagoDealers.IsDealerUnlockedViaAP(dealerName);

                    if (hasAPItem || !NarcopelagoOptions.Randomize_dealers)
                    {
                        // Have AP item or Randomize_dealers is false - allow recruitment to proceed
                        MelonLogger.Msg($"[PATCH] Allowing recruitment for '{dealerName}'");
                        return true;
                    }
                    else
                    {
                        // Don't have AP item - block recruitment
                        MelonLogger.Msg($"[PATCH] Blocking recruitment for '{dealerName}' - no AP item (location check sent)");
                        NarcopelagoDealers.QueueDelayedPOIUpdate(5);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[PATCH] Error in SetIsRecruited Prefix: {ex.Message}");
                    return true;
                }
            }
        }

        /// <summary>
            /// Harmony patch for Dealer.CanOfferRecruitment
            /// Controls when the recruitment dialogue option is available.
            /// </summary>
            [HarmonyPatch(typeof(Dealer), "CanOfferRecruitment")]
            public class Dealer_CanOfferRecruitment_Patch
            {
                static bool Prepare()
                {
                    MelonLogger.Msg("[PATCH] Dealer.CanOfferRecruitment patch is being prepared");
                    return true;
                }

                /// <summary>
                /// Postfix to override recruitment eligibility based on Archipelago state.
                /// </summary>
        static void Postfix(Dealer __instance, ref bool __result, ref string reason)
        {
            try
            {
                string dealerName = __instance.fullName ?? __instance.FirstName ?? "";
                if (string.IsNullOrEmpty(dealerName)) return;

                // Benji Coleman is the tutorial dealer - don't interfere
                if (dealerName.Equals("Benji Coleman", StringComparison.OrdinalIgnoreCase))
                {
                    return; // Use original game logic
                }

                // If already recruited in-game, keep original result (false)
                if (__instance.IsRecruited)
                {
                    return;
                }

                // If location already completed, don't show recruitment dialogue
                if (NarcopelagoDealers.HasCompletedRecruitLocation(dealerName))
                {
                    __result = false;
                    reason = "Already recruited for Archipelago";
                    return;
                }

                // Location NOT complete - we need to allow recruitment to send the check
                
                // If we have the AP item, ALWAYS allow the dialogue
                // This handles the case where game was reset but we still have the AP item
                if (NarcopelagoDealers.IsDealerUnlockedViaAP(dealerName))
                {
                    __result = true;
                    reason = string.Empty;
                    MelonLogger.Msg($"[PATCH] Allowing recruitment dialogue for '{dealerName}' - has AP item, location not complete");
                    return;
                }

                // Don't have AP item - if Randomize_dealers is true, still allow dialogue
                // so player can send the location check (recruitment will be blocked)
                if (NarcopelagoOptions.Randomize_dealers)
                {
                    // Check if player meets game's normal requirements
                    if (__result)
                    {
                        // Game already allows it, keep that
                        return;
                    }
                    
                    // Game doesn't allow it - check if they're mutually known
                    if (__instance.RelationData.IsMutuallyKnown())
                    {
                        __result = true;
                        reason = string.Empty;
                        return;
                    }
                }
                
                // Otherwise keep original result
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in CanOfferRecruitment Postfix: {ex.Message}");
            }
        }
    }
}
