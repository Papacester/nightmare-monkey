using HarmonyLib;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Property;
using MelonLoader;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Narcopelago
{
    /// <summary>
    /// Handles property and business purchases from the Realtor (Ray Hoffmen).
    /// 
    /// When purchasing any property or business:
    /// - Sends a location check named "Realtor Purchase, {PropertyName}"
    /// - If Randomize_business_properties is true and it's a business, blocks the unlock
    /// - If Randomize_drug_making_properties is true and it's a property, blocks the unlock
    /// 
    /// When receiving items with tags "Drug Making Property" or "Business Property":
    /// - Unlocks the corresponding property/business in-game
    /// </summary>
    public static class NarcopelagoRealtor
    {
        #region State Tracking

        /// <summary>
        /// Tracks which properties have had their purchase location checked.
        /// </summary>
        private static HashSet<string> _purchaseLocationChecked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tracks which properties have been unlocked via AP items.
        /// </summary>
        private static HashSet<string> _unlockedViaAP = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Queue of properties to unlock on the main thread.
        /// </summary>
        private static ConcurrentQueue<string> _pendingUnlocks = new ConcurrentQueue<string>();

        /// <summary>
        /// Tracks which owned properties we temporarily added back to UnownedProperties
        /// so the game's dialogue system handles them normally.
        /// </summary>
        private static HashSet<string> _spoofedUnownedProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Tracks which owned businesses we temporarily added back to UnownedBusinesses.
        /// </summary>
        private static HashSet<string> _spoofedUnownedBusinesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
        /// Tracks whether realtor location hints have been scouted this session.
        /// </summary>
        private static bool _hintsScouted = false;

        #endregion

        #region Public Methods

        /// <summary>
        /// Sets whether we're in a game scene.
        /// </summary>
        public static void SetInGameScene(bool inGame)
        {
            _inGameScene = inGame;
            if (inGame)
            {
                MelonLogger.Msg("[Realtor] Entered game scene");
            }
        }

        /// <summary>
        /// Syncs from session on load.
        /// </summary>
        public static void SyncFromSession()
        {
            _syncPending = true;
            _syncDelayFrames = 120; // ~2 seconds at 60fps
            MelonLogger.Msg("[Realtor] Queued sync from session");
        }

        /// <summary>
        /// Process queued operations on the main thread.
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            if (!_inGameScene)
                return;

            // Process pending sync
            if (_syncPending)
            {
                if (_syncDelayFrames > 0)
                {
                    _syncDelayFrames--;
                }
                else
                {
                    _syncPending = false;
                    SyncFromSessionInternal();
                }
            }

            // Process pending unlocks
            while (_pendingUnlocks.TryDequeue(out string propertyName))
            {
                try
                {
                    UnlockPropertyInternal(propertyName);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Realtor] Error unlocking '{propertyName}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Called when a property/business purchase is initiated.
        /// Sends the location check and determines if unlock should proceed.
        /// </summary>
        /// <param name="propertyName">The name of the property being purchased.</param>
        /// <param name="isBusiness">True if this is a business property.</param>
        /// <returns>True if the unlock should proceed, false to block it.</returns>
        public static bool OnPropertyPurchase(string propertyName, bool isBusiness)
        {
            MelonLogger.Msg($"[Realtor] Processing purchase: {propertyName} (Business: {isBusiness})");

            // Send the location check
            SendPurchaseLocationCheck(propertyName);

            // Mark as purchased
            _purchaseLocationChecked.Add(propertyName);

            // Check if this property even exists as an AP location - if not, allow it through
            string locationName = $"Realtor Purchase, {propertyName}";
            int locationId = Data_Locations.GetLocationId(locationName);
            if (locationId <= 0)
            {
                MelonLogger.Msg($"[Realtor] Allowing unlock for '{propertyName}' - not an Archipelago location");
                return true;
            }

            // Check if we should block the unlock based on randomization settings
            if (isBusiness && NarcopelagoOptions.Randomize_business_properties)
            {
                // Check if we have the AP item
                if (IsUnlockedViaAP(propertyName))
                {
                    MelonLogger.Msg($"[Realtor] Allowing business unlock for '{propertyName}' - AP item received");
                    return true;
                }
                else
                {
                    MelonLogger.Msg($"[Realtor] Blocking business unlock for '{propertyName}' - no AP item (location check sent)");
                    return false;
                }
            }
            else if (!isBusiness && NarcopelagoOptions.Randomize_drug_making_properties)
            {
                // Check if we have the AP item
                if (IsUnlockedViaAP(propertyName))
                {
                    MelonLogger.Msg($"[Realtor] Allowing property unlock for '{propertyName}' - AP item received");
                    return true;
                }
                else
                {
                    MelonLogger.Msg($"[Realtor] Blocking property unlock for '{propertyName}' - no AP item (location check sent)");
                    return false;
                }
            }

            // Randomization is off for this type - allow unlock
            MelonLogger.Msg($"[Realtor] Allowing unlock for '{propertyName}' - randomization disabled");
            return true;
        }

        /// <summary>
        /// Called when a property/business AP item is received.
        /// Always unlocks the property immediately.
        /// </summary>
        public static void OnPropertyItemReceived(string propertyName)
        {
            MelonLogger.Msg($"[Realtor] AP item received for: {propertyName}");
            _unlockedViaAP.Add(propertyName);

            // Always unlock immediately when receiving an AP item
            MelonLogger.Msg($"[Realtor] Queueing unlock for: {propertyName}");
            _pendingUnlocks.Enqueue(propertyName);
        }

        /// <summary>
        /// Checks if the purchase location for a property has been completed.
        /// </summary>
        public static bool HasCompletedPurchaseLocation(string propertyName)
        {
            // First check our local tracking
            if (_purchaseLocationChecked.Contains(propertyName))
            {
                return true;
            }

            // Then check Archipelago
            string locationName = $"Realtor Purchase, {propertyName}";
            int locationId = Data_Locations.GetLocationId(locationName);
            
            if (locationId <= 0)
            {
                // Location doesn't exist in data - no location check needed
                return true;
            }
            
            return NarcopelagoLocations.IsLocationChecked(locationId);
        }

        /// <summary>
        /// Checks if a property has been unlocked via AP item.
        /// </summary>
        public static bool IsUnlockedViaAP(string propertyName)
        {
            return _unlockedViaAP.Contains(propertyName);
        }

        /// <summary>
        /// Checks if a property purchase location has been checked.
        /// </summary>
        public static bool HasPurchasedProperty(string propertyName)
        {
            return _purchaseLocationChecked.Contains(propertyName);
        }

        /// <summary>
        /// Checks if a property/business should be shown in the purchase dialogue.
        /// </summary>
        public static bool ShouldShowPurchaseOption(string propertyName, bool isBusiness)
        {
            // If already purchased (location checked) but not unlocked, hide the option
            if (HasPurchasedProperty(propertyName))
            {
                // Only show if we have the AP item and it's not already owned
                // The actual ownership check is done by the game
                return false;
            }

            // Not purchased yet - show the option
            return true;
        }

        /// <summary>
        /// Resets the realtor tracking state.
        /// </summary>
        public static void Reset()
        {
            _purchaseLocationChecked.Clear();
            _unlockedViaAP.Clear();
            _inGameScene = false;
            _syncPending = false;
            _hintsScouted = false;
            
            while (_pendingUnlocks.TryDequeue(out _)) { }
            
            MelonLogger.Msg("[Realtor] Reset tracking state");
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Public method to send location check (used by patches for already-owned properties).
        /// </summary>
        public static void SendPurchaseLocationCheckPublic(string propertyName)
        {
            SendPurchaseLocationCheck(propertyName);
        }

        /// <summary>
        /// Public method to mark a property as purchased (used by patches for already-owned properties).
        /// </summary>
        public static void MarkAsPurchased(string propertyName)
        {
            _purchaseLocationChecked.Add(propertyName);
        }

        /// <summary>
        /// Sends the location check for a property purchase.
        /// </summary>
        private static void SendPurchaseLocationCheck(string propertyName)
        {
            string locationName = $"Realtor Purchase, {propertyName}";
            int locationId = Data_Locations.GetLocationId(locationName);

            if (locationId > 0)
            {
                if (!NarcopelagoLocations.IsLocationChecked(locationId))
                {
                    MelonLogger.Msg($"[Realtor] Sending location check: {locationName} (ID: {locationId})");
                    NarcopelagoLocations.CompleteLocation(locationId);
                }
                else
                {
                    MelonLogger.Msg($"[Realtor] Location already checked: {locationName}");
                }
            }
            else
            {
                MelonLogger.Warning($"[Realtor] Could not find location ID for: {locationName}");
            }
        }

        /// <summary>
        /// Internal sync from session.
        /// </summary>
        private static void SyncFromSessionInternal()
        {
            var session = ConnectionHandler.CurrentSession;
            if (session?.Items?.AllItemsReceived == null)
            {
                MelonLogger.Msg("[Realtor] Cannot sync - no session or items");
                return;
            }

            MelonLogger.Msg("[Realtor] Syncing from session...");

            int propertyCount = 0;

            foreach (var item in session.Items.AllItemsReceived)
            {
                string itemName = item.ItemName;

                // Check if it's a property item
                if (Data_Items.HasTag(itemName, "Drug Making Property") || Data_Items.HasTag(itemName, "Business Property"))
                {
                    if (!_unlockedViaAP.Contains(itemName))
                    {
                        _unlockedViaAP.Add(itemName);
                        propertyCount++;
                        MelonLogger.Msg($"[Realtor] Synced AP unlock for: {itemName}");
                    }

                    // Always unlock when we have the AP item
                    _pendingUnlocks.Enqueue(itemName);
                }
            }

            // Sync completed purchase locations from Archipelago
            SyncCompletedPurchasesFromSession();

            MelonLogger.Msg($"[Realtor] Synced {propertyCount} property unlocks from session");
        }

        /// <summary>
        /// Syncs completed purchase locations from Archipelago.
        /// </summary>
        private static void SyncCompletedPurchasesFromSession()
        {
            if (!NarcopelagoLocations.IsAvailable)
                return;

            var checkedLocations = NarcopelagoLocations.AllLocationsChecked;
            if (checkedLocations == null)
                return;

            // Check all property purchase locations
            var propertyLocations = Data_Locations.GetLocationsByTag("Drug Making Property");
            var businessLocations = Data_Locations.GetLocationsByTag("Business Property");

            foreach (var locationName in propertyLocations)
            {
                int locationId = Data_Locations.GetLocationId(locationName);
                if (locationId > 0 && checkedLocations.Contains(locationId))
                {
                    string propertyName = locationName.Replace("Realtor Purchase, ", "");
                    if (!_purchaseLocationChecked.Contains(propertyName))
                    {
                        _purchaseLocationChecked.Add(propertyName);
                        MelonLogger.Msg($"[Realtor] Marked '{propertyName}' as purchased (from Archipelago)");
                    }
                }
            }

            foreach (var locationName in businessLocations)
            {
                int locationId = Data_Locations.GetLocationId(locationName);
                if (locationId > 0 && checkedLocations.Contains(locationId))
                {
                    string propertyName = locationName.Replace("Realtor Purchase, ", "");
                    if (!_purchaseLocationChecked.Contains(propertyName))
                    {
                        _purchaseLocationChecked.Add(propertyName);
                        MelonLogger.Msg($"[Realtor] Marked '{propertyName}' as purchased (from Archipelago)");
                    }
                }
            }
        }

        /// <summary>
        /// Unlocks a property in-game.
        /// </summary>
        private static void UnlockPropertyInternal(string propertyName)
        {
            try
            {
                // Sewer Office has special conditions — handled by NarcopelagoSewer
                if (string.Equals(propertyName, "Sewer Office", StringComparison.OrdinalIgnoreCase))
                {
                    MelonLogger.Msg($"[Realtor] Skipping Sewer Office — delegating to NarcopelagoSewer");
                    NarcopelagoSewer.OnSewerOfficeItemReceived();
                    return;
                }

                // Try to find the property in the game
                Property property = FindPropertyByName(propertyName);
                if (property != null)
                {
                    if (!property.IsOwned)
                    {
                        property.SetOwned();
                        MelonLogger.Msg($"[Realtor] Unlocked property: {propertyName}");
                    }
                    else
                    {
                        MelonLogger.Msg($"[Realtor] Property already owned: {propertyName}");
                    }
                    return;
                }

                // Try to find the business
                Business business = FindBusinessByName(propertyName);
                if (business != null)
                {
                    if (!business.IsOwned)
                    {
                        business.SetOwned();
                        MelonLogger.Msg($"[Realtor] Unlocked business: {propertyName}");
                    }
                    else
                    {
                        MelonLogger.Msg($"[Realtor] Business already owned: {propertyName}");
                    }
                    return;
                }

                MelonLogger.Warning($"[Realtor] Could not find property or business: {propertyName}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Realtor] Error in UnlockPropertyInternal: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds a property by name.
        /// </summary>
        private static Property FindPropertyByName(string propertyName)
        {
            foreach (var prop in Property.Properties)
            {
                if (prop == null) continue;
                
                if (string.Equals(prop.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return prop;
                }
            }
            return null;
        }

        /// <summary>
        /// Finds a business by name.
        /// </summary>
        private static Business FindBusinessByName(string propertyName)
        {
            foreach (var biz in Business.Businesses)
            {
                if (biz == null) continue;

                if (string.Equals(biz.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return biz;
                }
            }
            return null;
        }

        /// <summary>
        /// Scouts all realtor purchase locations as hints on the AP server.
        /// Called once per session when the player first talks to the realtor.
        /// </summary>
        public static void ScoutRealtorHints()
        {
            if (_hintsScouted) return;
            _hintsScouted = true;

            var locationIds = new System.Collections.Generic.List<long>();

            // Gather Drug Making Property locations if randomization is enabled
            if (NarcopelagoOptions.Randomize_drug_making_properties)
            {
                foreach (var locName in Data_Locations.GetLocationsByTag("Drug Making Property"))
                {
                    int id = Data_Locations.GetLocationId(locName);
                    if (id > 0 && !NarcopelagoLocations.IsLocationChecked(id))
                        locationIds.Add(id);
                }
            }

            // Gather Business Property locations if randomization is enabled
            if (NarcopelagoOptions.Randomize_business_properties)
            {
                foreach (var locName in Data_Locations.GetLocationsByTag("Business Property"))
                {
                    int id = Data_Locations.GetLocationId(locName);
                    if (id > 0 && !NarcopelagoLocations.IsLocationChecked(id))
                        locationIds.Add(id);
                }
            }

            if (locationIds.Count > 0)
            {
                MelonLogger.Msg($"[Realtor] Scouting {locationIds.Count} realtor locations as hints");
                _ = NarcopelagoLocations.ScoutLocationsAsHintsAsync(locationIds.ToArray());
            }
        }

        /// <summary>
        /// Ensures an owned property is temporarily in the UnownedProperties list so
        /// the game's entire dialogue flow (price display, fund checks, etc.) works.
        /// </summary>
        public static void EnsurePropertyInUnownedList(Property property)
        {
            if (property == null || !property.IsOwned) return;
            if (_spoofedUnownedProperties.Contains(property.PropertyName)) return;

            // Check it's not already in the list
            var unowned = Property.UnownedProperties;
            for (int i = 0; i < unowned.Count; i++)
            {
                if (unowned[i] != null && string.Equals(unowned[i].PropertyName, property.PropertyName, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            unowned.Add(property);
            _spoofedUnownedProperties.Add(property.PropertyName);
            MelonLogger.Msg($"[Realtor] Temporarily added '{property.PropertyName}' to UnownedProperties for dialogue");
        }

        /// <summary>
        /// Ensures an owned business is temporarily in the UnownedBusinesses list.
        /// </summary>
        public static void EnsureBusinessInUnownedList(Business business)
        {
            if (business == null || !business.IsOwned) return;
            if (_spoofedUnownedBusinesses.Contains(business.PropertyName)) return;

            var unowned = Business.UnownedBusinesses;
            for (int i = 0; i < unowned.Count; i++)
            {
                if (unowned[i] != null && string.Equals(unowned[i].PropertyName, business.PropertyName, StringComparison.OrdinalIgnoreCase))
                    return;
            }

            unowned.Add(business);
            _spoofedUnownedBusinesses.Add(business.PropertyName);
            MelonLogger.Msg($"[Realtor] Temporarily added '{business.PropertyName}' to UnownedBusinesses for dialogue");
        }

        /// <summary>
        /// Removes a property from UnownedProperties if we spoofed it in.
        /// </summary>
        public static void RemovePropertyFromUnownedList(string propertyName)
        {
            if (!_spoofedUnownedProperties.Remove(propertyName)) return;

            var unowned = Property.UnownedProperties;
            for (int i = unowned.Count - 1; i >= 0; i--)
            {
                if (unowned[i] != null && string.Equals(unowned[i].PropertyName, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    unowned.RemoveAt(i);
                    MelonLogger.Msg($"[Realtor] Removed '{propertyName}' from UnownedProperties after purchase");
                    return;
                }
            }
        }

        /// <summary>
        /// Removes a business from UnownedBusinesses if we spoofed it in.
        /// </summary>
        public static void RemoveBusinessFromUnownedList(string businessName)
        {
            if (!_spoofedUnownedBusinesses.Remove(businessName)) return;

            var unowned = Business.UnownedBusinesses;
            for (int i = unowned.Count - 1; i >= 0; i--)
            {
                if (unowned[i] != null && string.Equals(unowned[i].PropertyName, businessName, StringComparison.OrdinalIgnoreCase))
                {
                    unowned.RemoveAt(i);
                    MelonLogger.Msg($"[Realtor] Removed '{businessName}' from UnownedBusinesses after purchase");
                    return;
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Harmony patch for DialogueHandler_EstateAgent.DialogueCallback
    /// Intercepts property/business purchases to send location checks and conditionally block unlocks.
    /// Also handles the case where properties are already owned (from AP) but need location checks.
    /// </summary>
    [HarmonyPatch(typeof(DialogueHandler_EstateAgent), "DialogueCallback")]
    public class DialogueHandler_EstateAgent_DialogueCallback_Patch
    {
        // Track the selected property/business for the current transaction
        private static Property _pendingProperty = null;
        private static Business _pendingBusiness = null;

        public static void SetPendingProperty(Property property)
        {
            _pendingProperty = property;
            _pendingBusiness = null;
        }

        public static void SetPendingBusiness(Business business)
        {
            _pendingProperty = null;
            _pendingBusiness = business;
        }

        public static void ClearPending()
        {
            _pendingProperty = null;
            _pendingBusiness = null;
        }

        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] DialogueHandler_EstateAgent.DialogueCallback patch is being prepared");
            return true;
        }

        static bool Prefix(DialogueHandler_EstateAgent __instance, string choiceLabel)
        {
            try
            {
                if (choiceLabel == "CONFIRM_BUY" && _pendingProperty != null)
                {
                    string propertyName = _pendingProperty.PropertyName;
                    bool isAlreadyOwned = _pendingProperty.IsOwned;
                    
                    MelonLogger.Msg($"[PATCH] CONFIRM_BUY for '{propertyName}' (AlreadyOwned: {isAlreadyOwned})");

                    if (isAlreadyOwned)
                    {
                        // Validate funds before charging
                        float balance = Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Money.MoneyManager>.Instance.onlineBalance;
                        if (balance < _pendingProperty.Price)
                        {
                            MelonLogger.Msg($"[PATCH] CONFIRM_BUY blocked for '{propertyName}' - insufficient funds (have: {balance}, need: {_pendingProperty.Price})");
                            _pendingProperty = null;
                            return false;
                        }

                        // Property already owned via AP - charge the player and send the location check
                        Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Money.MoneyManager>.Instance.CreateOnlineTransaction(
                            propertyName + " purchase", 
                            0f - _pendingProperty.Price, 
                            1f, 
                            string.Empty);

                        NarcopelagoRealtor.SendPurchaseLocationCheckPublic(propertyName);
                        NarcopelagoRealtor.MarkAsPurchased(propertyName);
                        NarcopelagoRealtor.RemovePropertyFromUnownedList(propertyName);
                        MelonLogger.Msg($"[PATCH] Property '{propertyName}' already owned - payment processed, location check sent");
                        _pendingProperty = null;
                        return false; // Skip original - don't try to buy what we already own
                    }

                    bool shouldUnlock = NarcopelagoRealtor.OnPropertyPurchase(propertyName, isBusiness: false);

                    if (!shouldUnlock)
                    {
                        // Still charge the player but don't unlock
                        Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Money.MoneyManager>.Instance.CreateOnlineTransaction(
                            propertyName + " purchase", 
                            0f - _pendingProperty.Price, 
                            1f, 
                            string.Empty);
                        
                        MelonLogger.Msg($"[PATCH] Blocked property unlock for '{propertyName}' - payment processed, awaiting AP item");
                        _pendingProperty = null;
                        return false; // Skip original method
                    }
                    
                    _pendingProperty = null;
                }
                else if (choiceLabel == "CONFIRM_BUY_BUSINESS" && _pendingBusiness != null)
                {
                    string businessName = _pendingBusiness.PropertyName;
                    bool isAlreadyOwned = _pendingBusiness.IsOwned;
                    
                    MelonLogger.Msg($"[PATCH] CONFIRM_BUY_BUSINESS for '{businessName}' (AlreadyOwned: {isAlreadyOwned})");

                    if (isAlreadyOwned)
                    {
                        // Validate funds before charging
                        float balance = Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Money.MoneyManager>.Instance.onlineBalance;
                        if (balance < _pendingBusiness.Price)
                        {
                            MelonLogger.Msg($"[PATCH] CONFIRM_BUY_BUSINESS blocked for '{businessName}' - insufficient funds (have: {balance}, need: {_pendingBusiness.Price})");
                            _pendingBusiness = null;
                            return false;
                        }

                        // Business already owned via AP - charge the player and send the location check
                        Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Money.MoneyManager>.Instance.CreateOnlineTransaction(
                            businessName + " purchase", 
                            0f - _pendingBusiness.Price, 
                            1f, 
                            string.Empty);

                        NarcopelagoRealtor.SendPurchaseLocationCheckPublic(businessName);
                        NarcopelagoRealtor.MarkAsPurchased(businessName);
                        NarcopelagoRealtor.RemoveBusinessFromUnownedList(businessName);
                        MelonLogger.Msg($"[PATCH] Business '{businessName}' already owned - payment processed, location check sent");
                        _pendingBusiness = null;
                        return false; // Skip original - don't try to buy what we already own
                    }

                    bool shouldUnlock = NarcopelagoRealtor.OnPropertyPurchase(businessName, isBusiness: true);

                    if (!shouldUnlock)
                    {
                        // Still charge the player but don't unlock
                        Il2CppScheduleOne.DevUtilities.NetworkSingleton<Il2CppScheduleOne.Money.MoneyManager>.Instance.CreateOnlineTransaction(
                            businessName + " purchase", 
                            0f - _pendingBusiness.Price, 
                            1f, 
                            string.Empty);
                        
                        MelonLogger.Msg($"[PATCH] Blocked business unlock for '{businessName}' - payment processed, awaiting AP item");
                        _pendingBusiness = null;
                        return false; // Skip original method
                    }
                    
                    _pendingBusiness = null;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in DialogueCallback Prefix: {ex.Message}");
            }

            return true; // Continue to original method
        }
    }

    /// <summary>
    /// Harmony patch for DialogueHandler_EstateAgent.ChoiceCallback
    /// Captures the selected property/business before purchase confirmation.
    /// The game's own code now finds owned AP properties in UnownedProperties/UnownedBusinesses
    /// (added there by ShouldChoiceBeShown), so we just need to track the pending selection.
    /// </summary>
    [HarmonyPatch(typeof(DialogueHandler_EstateAgent), "ChoiceCallback")]
    public class DialogueHandler_EstateAgent_ChoiceCallback_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] DialogueHandler_EstateAgent.ChoiceCallback patch is being prepared");
            return true;
        }

        static void Postfix(DialogueHandler_EstateAgent __instance, string choiceLabel)
        {
            try
            {
                // IMPORTANT: Check businesses FIRST since Business extends Property
                var business = Business.Businesses.Find(
                    new Func<Business, bool>(x => string.Equals(x.PropertyCode, choiceLabel, StringComparison.OrdinalIgnoreCase)));

                if (business != null)
                {
                    MelonLogger.Msg($"[PATCH] Selected business: {business.PropertyName} (Owned: {business.IsOwned})");
                    DialogueHandler_EstateAgent_DialogueCallback_Patch.SetPendingBusiness(business);
                    return;
                }

                var property = Property.Properties.Find(
                    new Func<Property, bool>(x => string.Equals(x.PropertyCode, choiceLabel, StringComparison.OrdinalIgnoreCase)));

                if (property != null)
                {
                    MelonLogger.Msg($"[PATCH] Selected property: {property.PropertyName} (Owned: {property.IsOwned})");
                    DialogueHandler_EstateAgent_DialogueCallback_Patch.SetPendingProperty(property);
                    return;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in ChoiceCallback Postfix: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Harmony patch for DialogueHandler_EstateAgent.ShouldChoiceBeShown
    /// Controls visibility of purchase options based on location check status.
    /// Shows options for owned properties if their location check is not complete.
    /// Hides options for properties whose location check is already complete.
    /// Properties are shown regardless of player funds - the game handles the money check at purchase time.
    /// </summary>
    [HarmonyPatch(typeof(DialogueHandler_EstateAgent), "ShouldChoiceBeShown")]
    public class DialogueHandler_EstateAgent_ShouldChoiceBeShown_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] DialogueHandler_EstateAgent.ShouldChoiceBeShown patch is being prepared");
            return true;
        }

        /// <summary>
        /// Postfix to control visibility. We need to handle:
        /// 1. Force SHOW owned properties if location check not complete (override game hiding them)
        /// 2. Force HIDE any properties if location check IS complete
        /// 3. Temporarily add owned AP properties to UnownedProperties/UnownedBusinesses so the
        ///    game's entire dialogue flow (price display, fund checks) works naturally
        /// 4. Let the game handle everything else
        /// </summary>
        static void Postfix(string choiceLabel, ref bool __result)
        {
            try
            {
                // Scout realtor locations as hints the first time the player talks to Ray
                NarcopelagoRealtor.ScoutRealtorHints();

                // Check if this is a property
                var property = Property.Properties.Find(
                    new Func<Property, bool>(x => string.Equals(x.PropertyCode, choiceLabel, StringComparison.OrdinalIgnoreCase)));

                if (property != null)
                {
                    // Only apply our logic when randomization is enabled and this property has an AP location.
                    // Properties with no AP location (e.g. Hyland Manor) are left entirely to the game.
                    if (!NarcopelagoOptions.Randomize_drug_making_properties)
                        return;

                    string locationName = $"Realtor Purchase, {property.PropertyName}";
                    if (Data_Locations.GetLocationId(locationName) <= 0)
                        return;

                    bool locationComplete = NarcopelagoRealtor.HasCompletedPurchaseLocation(property.PropertyName);
                    if (locationComplete)
                    {
                        __result = false;
                        return;
                    }

                    // Show the property and ensure it's in UnownedProperties so the
                    // game's dialogue code can find it for price display + fund checks
                    if (property.IsOwned)
                    {
                        NarcopelagoRealtor.EnsurePropertyInUnownedList(property);
                    }
                    __result = true;
                    return;
                }

                // Check if this is a business
                var business = Business.Businesses.Find(
                    new Func<Business, bool>(x => string.Equals(x.PropertyCode, choiceLabel, StringComparison.OrdinalIgnoreCase)));

                if (business != null)
                {
                    // Only apply our logic when randomization is enabled and this business has an AP location.
                    if (!NarcopelagoOptions.Randomize_business_properties)
                        return;

                    string locationName = $"Realtor Purchase, {business.PropertyName}";
                    if (Data_Locations.GetLocationId(locationName) <= 0)
                        return;

                    bool locationComplete = NarcopelagoRealtor.HasCompletedPurchaseLocation(business.PropertyName);
                    if (locationComplete)
                    {
                        __result = false;
                        return;
                    }

                    // Show the business and ensure it's in UnownedBusinesses
                    if (business.IsOwned)
                    {
                        NarcopelagoRealtor.EnsureBusinessInUnownedList(business);
                    }
                    __result = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in ShouldChoiceBeShown Postfix: {ex.Message}");
            }

            // Not a property/business we recognize - keep original result
        }
    }

    }
