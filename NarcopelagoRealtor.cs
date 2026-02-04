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
        /// Always queues an immediate unlock attempt.
        /// </summary>
        public static void OnPropertyItemReceived(string propertyName)
        {
            MelonLogger.Msg($"[Realtor] AP item received for: {propertyName}");
            _unlockedViaAP.Add(propertyName);

            // Always queue unlock immediately when receiving an AP item
            // The AP item grants ownership directly, regardless of whether player has purchased
            MelonLogger.Msg($"[Realtor] Queueing immediate unlock for: {propertyName}");
            _pendingUnlocks.Enqueue(propertyName);
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
            
            while (_pendingUnlocks.TryDequeue(out _)) { }
            
            MelonLogger.Msg("[Realtor] Reset tracking state");
        }

        #endregion

        #region Private Methods

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

                    // Try to unlock if we have the item
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

        #endregion
    }

    /// <summary>
    /// Harmony patch for DialogueHandler_EstateAgent.DialogueCallback
    /// Intercepts property/business purchases to send location checks and conditionally block unlocks.
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
    /// </summary>
    [HarmonyPatch(typeof(DialogueHandler_EstateAgent), "ChoiceCallback")]
    public class DialogueHandler_EstateAgent_ChoiceCallback_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] DialogueHandler_EstateAgent.ChoiceCallback patch is being prepared");
            return true;
        }

        static void Postfix(string choiceLabel)
        {
            try
            {
                // IMPORTANT: Check businesses FIRST since Business extends Property
                // If we check Property.UnownedProperties first, we'll find businesses there too
                
                // Check if this is a business selection
                var business = Business.UnownedBusinesses.Find(
                    new Func<Business, bool>(x => string.Equals(x.PropertyCode, choiceLabel, StringComparison.OrdinalIgnoreCase)));
                
                if (business != null)
                {
                    DialogueHandler_EstateAgent_DialogueCallback_Patch.SetPendingBusiness(business);
                    MelonLogger.Msg($"[PATCH] Selected business: {business.PropertyName}");
                    return;
                }

                // Check if this is a property selection (only if not a business)
                var property = Property.UnownedProperties.Find(
                    new Func<Property, bool>(x => string.Equals(x.PropertyCode, choiceLabel, StringComparison.OrdinalIgnoreCase)));
                
                if (property != null)
                {
                    DialogueHandler_EstateAgent_DialogueCallback_Patch.SetPendingProperty(property);
                    MelonLogger.Msg($"[PATCH] Selected property: {property.PropertyName}");
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
    /// Hides purchase options for properties that have already been purchased but not unlocked.
    /// </summary>
    [HarmonyPatch(typeof(DialogueHandler_EstateAgent), "ShouldChoiceBeShown")]
    public class DialogueHandler_EstateAgent_ShouldChoiceBeShown_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] DialogueHandler_EstateAgent.ShouldChoiceBeShown patch is being prepared");
            return true;
        }

        static void Postfix(string choiceLabel, ref bool __result)
        {
            try
            {
                // If already hidden by original logic, keep it hidden
                if (!__result)
                    return;

                // Check if this is a property
                var property = Property.Properties.Find(
                    new Func<Property, bool>(x => string.Equals(x.PropertyCode, choiceLabel, StringComparison.OrdinalIgnoreCase)));
                
                if (property != null)
                {
                    // If we've purchased but not unlocked, hide the option
                    if (NarcopelagoRealtor.HasPurchasedProperty(property.PropertyName) && !property.IsOwned)
                    {
                        __result = false;
                        return;
                    }
                }

                // Check if this is a business
                var business = Business.Businesses.Find(
                    new Func<Business, bool>(x => string.Equals(x.PropertyCode, choiceLabel, StringComparison.OrdinalIgnoreCase)));
                
                if (business != null)
                {
                    // If we've purchased but not unlocked, hide the option
                    if (NarcopelagoRealtor.HasPurchasedProperty(business.PropertyName) && !business.IsOwned)
                    {
                        __result = false;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in ShouldChoiceBeShown Postfix: {ex.Message}");
            }
        }
    }
}
