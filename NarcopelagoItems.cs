using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using MelonLoader;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Narcopelago
{
    /// <summary>
    /// Handles receiving and processing items from Archipelago.
    /// Only processes items that come through the ItemReceived event.
    /// </summary>
    public static class NarcopelagoItems
    {
        /// <summary>
        /// Indicates whether we have subscribed to item events.
        /// </summary>
        public static bool IsInitialized { get; private set; } = false;

        /// <summary>
        /// The tutorial location that indicates a new game vs returning player.
        /// If this location is already checked when loading in, we skip consumable items
        /// (Cash, XP, Fillers) since they were already granted in a previous session.
        /// </summary>
        public const string TUTORIAL_LOCATION = "Welcome to Hyland Point|Open your phone and read your messages";

        /// <summary>
        /// If true, skip all consumable items (Cash, XP, Fillers) permanently.
        /// Set to true if the tutorial location was already completed when we initialized.
        /// This means we're a returning player and consumables were already granted.
        /// </summary>
        private static bool _skipConsumables = false;

        /// <summary>
        /// If true, hold consumable items until the tutorial is completed.
        /// Set to true if the tutorial location was NOT completed when we initialized.
        /// This means we're starting a new game and need to wait for the game to be ready.
        /// </summary>
        private static bool _holdConsumablesUntilTutorial = false;

        /// <summary>
        /// Queue of held consumable items waiting for tutorial completion.
        /// </summary>
        private static ConcurrentQueue<(string type, string itemName)> _heldConsumables = new ConcurrentQueue<(string, string)>();

        /// <summary>
        /// Tracks if the tutorial has been completed this session (consumables released).
        /// </summary>
        private static bool _tutorialCompletedThisSession = false;

        /// <summary>
        /// Called after successful connection to set up item receiving.
        /// Only subscribes to the ItemReceived event - items are processed as they arrive.
        /// </summary>
        /// <param name="session">The connected Archipelago session.</param>
        public static void Initialize(ArchipelagoSession session)
        {
            if (session == null)
            {
                MelonLogger.Warning("[Items] Cannot initialize - session is null");
                return;
            }

            if (IsInitialized)
            {
                MelonLogger.Msg("[Items] Already initialized");
                return;
            }

            try
            {
                // Check if the tutorial location has already been completed
                bool tutorialAlreadyCompleted = IsTutorialLocationChecked();
                
                if (tutorialAlreadyCompleted)
                {
                    // Returning player - skip all consumables since they were already granted
                    _skipConsumables = true;
                    _holdConsumablesUntilTutorial = false;
                    MelonLogger.Msg("[Items] Tutorial location already completed - skipping consumable items (Cash, XP, Fillers)");
                }
                else
                {
                    // New game - hold consumables until tutorial is completed
                    _skipConsumables = false;
                    _holdConsumablesUntilTutorial = true;
                    MelonLogger.Msg("[Items] New game detected - consumable items will be held until tutorial completion");
                }

                // Subscribe to item received events
                // The Archipelago client handles tracking which items are new
                session.Items.ItemReceived += OnItemReceived;
                
                MelonLogger.Msg("[Items] Subscribed to item received events");
                IsInitialized = true;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Items] Failed to initialize: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if the tutorial location has been completed.
        /// </summary>
        private static bool IsTutorialLocationChecked()
        {
            if (!NarcopelagoLocations.IsAvailable)
            {
                return false;
            }

            long locationId = NarcopelagoLocations.GetLocationId(TUTORIAL_LOCATION);
            if (locationId <= 0)
            {
                MelonLogger.Warning($"[Items] Could not find tutorial location ID for: {TUTORIAL_LOCATION}");
                return false;
            }

            return NarcopelagoLocations.IsLocationChecked(locationId);
        }

        /// <summary>
        /// Event handler for when an item is received from Archipelago.
        /// This is called for each new item the server sends us.
        /// </summary>
        private static void OnItemReceived(ReceivedItemsHelper helper)
        {
            try
            {
                while (helper.Any())
                {
                    var item = helper.DequeueItem();
                    ProcessItem(item);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Items] Error in OnItemReceived: {ex.Message}");
            }
        }

        /// <summary>
        /// Process a single received item and apply its effects.
        /// </summary>
        /// <param name="item">The item info.</param>
        private static void ProcessItem(ItemInfo item)
        {
            string itemName = item.ItemName;
            long itemId = item.ItemId;
            var flags = item.Flags;

            MelonLogger.Msg($"[Items] Received: {itemName} (ID: {itemId}, Flags: {flags})");

            // Only handle customer unlocks for now - other handlers are not implemented
            if (IsCustomerUnlockItem(itemName))
            {
                HandleCustomerUnlock(itemName);
            }
            else if (IsDealerRecruitItem(itemName))
            {
                HandleDealerRecruit(itemName);
            }
            else if (IsSupplierUnlockItem(itemName))
            {
                HandleSupplierUnlock(itemName);
            }
            else if (IsCartelInfluenceItem(itemName))
            {
                HandleCartelInfluence(itemName);
            }
            else if (IsLevelUpRewardItem(itemName))
            {
                HandleLevelUpReward(itemName);
            }
            else if (NarcopelagoBundles.IsCashBundleItem(itemName))
            {
                if (_skipConsumables)
                {
                    MelonLogger.Msg($"[Items] Skipping cash bundle - returning player");
                    return;
                }
                if (_holdConsumablesUntilTutorial && !_tutorialCompletedThisSession)
                {
                    MelonLogger.Msg($"[Items] Holding cash bundle until tutorial completion");
                    _heldConsumables.Enqueue(("CashBundle", itemName));
                    return;
                }
                HandleCashBundle(itemName);
            }
            else if (NarcopelagoBundles.IsXPBundleItem(itemName))
            {
                if (_skipConsumables)
                {
                    MelonLogger.Msg($"[Items] Skipping XP bundle - returning player");
                    return;
                }
                if (_holdConsumablesUntilTutorial && !_tutorialCompletedThisSession)
                {
                    MelonLogger.Msg($"[Items] Holding XP bundle until tutorial completion");
                    _heldConsumables.Enqueue(("XPBundle", itemName));
                    return;
                }
                HandleXPBundle(itemName);
            }
            else if (IsPropertyItem(itemName))
            {
                HandlePropertyItem(itemName);
            }
            else if (NarcopelagoFillers.IsFillerItem(itemName))
            {
                if (_skipConsumables)
                {
                    MelonLogger.Msg($"[Items] Skipping filler item '{itemName}' - returning player");
                    return;
                }
                if (_holdConsumablesUntilTutorial && !_tutorialCompletedThisSession)
                {
                    MelonLogger.Msg($"[Items] Holding filler item '{itemName}' until tutorial completion");
                    _heldConsumables.Enqueue(("Filler", itemName));
                    return;
                }
                HandleFillerItem(itemName);
            }
            // Log other types but don't process them yet
            else if (IsDealerUnlockItem(itemName))
            {
                MelonLogger.Msg($"[Items] Dealer unlock (not implemented): {itemName}");
            }
            else
            {
                MelonLogger.Msg($"[Items] Other item (not implemented): {itemName}");
            }
        }

        /// <summary>
        /// Called when the tutorial location is completed.
        /// Releases all held consumable items.
        /// </summary>
        public static void OnTutorialCompleted()
        {
            if (_tutorialCompletedThisSession)
            {
                return; // Already processed
            }

            _tutorialCompletedThisSession = true;
            _holdConsumablesUntilTutorial = false;

            int count = _heldConsumables.Count;
            if (count == 0)
            {
                MelonLogger.Msg("[Items] Tutorial completed - no held consumables to release");
                return;
            }

            MelonLogger.Msg($"[Items] Tutorial completed - releasing {count} held consumable items");

            while (_heldConsumables.TryDequeue(out var held))
            {
                try
                {
                    switch (held.type)
                    {
                        case "CashBundle":
                            HandleCashBundle(held.itemName);
                            break;
                        case "XPBundle":
                            HandleXPBundle(held.itemName);
                            break;
                        case "Filler":
                            HandleFillerItem(held.itemName);
                            break;
                        default:
                            MelonLogger.Warning($"[Items] Unknown held consumable type: {held.type}");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Items] Error releasing held consumable '{held.itemName}': {ex.Message}");
                }
            }

            MelonLogger.Msg("[Items] Finished releasing held consumables");
        }

        #region Item Type Checks

        /// <summary>
        /// Checks if an item is a customer unlock item.
        /// Uses Data_Items tags to verify it's actually a customer.
        /// </summary>
        private static bool IsCustomerUnlockItem(string itemName)
        {
            // First check the basic format
            if (!itemName.EndsWith(" Unlocked"))
                return false;

            // Use Data_Items to check if this item has the "Customer" tag
            if (Data_Items.HasTag(itemName, "Customer"))
                return true;

            // Fallback: if Data_Items isn't loaded, use basic filtering
            // Exclude known non-customer patterns
            if (itemName.Contains("Dealer") || itemName.Contains("Recruited"))
                return false;

            // Check if it's a supplier
            if (Data_Items.HasTag(itemName, "Supplier"))
                return false;

            return false; // Default to false if we can't verify it's a customer
        }

        /// <summary>
        /// Checks if an item is a dealer unlock item.
        /// </summary>
        private static bool IsDealerUnlockItem(string itemName)
        {
            return itemName.Contains("Dealer") && itemName.EndsWith(" Unlocked");
        }

        /// <summary>
        /// Checks if an item is a dealer recruitment item (e.g., "Molly Presley Recruited").
        /// </summary>
        private static bool IsDealerRecruitItem(string itemName)
        {
            if (!itemName.EndsWith(" Recruited"))
                return false;

            return Data_Items.HasTag(itemName, "Dealer");
        }

        /// <summary>
        /// Checks if an item is a supplier unlock item.
        /// </summary>
        private static bool IsSupplierUnlockItem(string itemName)
        {
            if (!itemName.EndsWith(" Unlocked"))
                return false;

            return Data_Items.HasTag(itemName, "Supplier");
        }

        /// <summary>
        /// Checks if an item is a cartel influence item (e.g., "Cartel Influence, Westville").
        /// </summary>
        private static bool IsCartelInfluenceItem(string itemName)
        {
            if (!itemName.StartsWith("Cartel Influence, "))
                return false;

            return Data_Items.HasTag(itemName, "Cartel Influence");
        }

        /// <summary>
        /// Checks if an item is a level up reward unlock item.
        /// These include shop item unlocks, warehouse access, region unlocks, etc.
        /// </summary>
        private static bool IsLevelUpRewardItem(string itemName)
        {
            return Data_Items.HasTag(itemName, "Level Up Reward");
        }

        /// <summary>
        /// Checks if an item is a property or business item.
        /// </summary>
        private static bool IsPropertyItem(string itemName)
        {
            return Data_Items.HasTag(itemName, "Drug Making Property") || 
                   Data_Items.HasTag(itemName, "Business Property");
        }

        #endregion

        #region Item Handlers

        /// <summary>
        /// Handle receiving a customer unlock item.
        /// </summary>
        private static void HandleCustomerUnlock(string itemName)
        {
            string customerName = itemName.Replace(" Unlocked", "").Trim();
            MelonLogger.Msg($"[Items] Unlocking customer: {customerName}");
            NarcopelagoCustomers.SetCustomerUnlocked(customerName);
        }

        /// <summary>
        /// Handle receiving a dealer recruitment item (e.g., "Molly Presley Recruited").
        /// </summary>
        private static void HandleDealerRecruit(string itemName)
        {
            string dealerName = itemName.Replace(" Recruited", "").Trim();
            MelonLogger.Msg($"[Items] Recruiting dealer: {dealerName}");
            NarcopelagoDealers.SetDealerRecruited(dealerName);
        }

        /// <summary>
        /// Handle receiving a supplier unlock item (e.g., "Shirley Watts Unlocked").
        /// </summary>
        private static void HandleSupplierUnlock(string itemName)
        {
            string supplierName = itemName.Replace(" Unlocked", "").Trim();
            MelonLogger.Msg($"[Items] Unlocking supplier: {supplierName}");
            NarcopelagoSuppliers.SetSupplierUnlocked(supplierName);
        }

        /// <summary>
        /// Handle receiving a cartel influence item (e.g., "Cartel Influence, Westville").
        /// </summary>
        private static void HandleCartelInfluence(string itemName)
        {
            string region = itemName.Replace("Cartel Influence, ", "").Trim();
            MelonLogger.Msg($"[Items] Reducing cartel influence in: {region}");
            NarcopelagoCartelInfluence.OnInfluenceItemReceived(region);
        }

        /// <summary>
        /// Handle receiving a level up reward unlock item.
        /// This includes shop item unlocks, warehouse access, region unlocks, etc.
        /// </summary>
        private static void HandleLevelUpReward(string itemName)
        {
            MelonLogger.Msg($"[Items] Processing level up reward: {itemName}");
            NarcopelagoLevels.OnUnlockItemReceived(itemName);
        }

        /// <summary>
        /// Handle receiving a Cash Bundle item.
        /// </summary>
        private static void HandleCashBundle(string itemName)
        {
            MelonLogger.Msg($"[Items] Processing cash bundle: {itemName}");
            NarcopelagoBundles.OnCashBundleReceived();
        }

        /// <summary>
        /// Handle receiving an XP Bundle item.
        /// </summary>
        private static void HandleXPBundle(string itemName)
        {
            MelonLogger.Msg($"[Items] Processing XP bundle: {itemName}");
            NarcopelagoBundles.OnXPBundleReceived();
        }

        /// <summary>
        /// Handle receiving a property or business item.
        /// </summary>
        private static void HandlePropertyItem(string itemName)
        {
            MelonLogger.Msg($"[Items] Processing property item: {itemName}");
            NarcopelagoRealtor.OnPropertyItemReceived(itemName);
        }

        /// <summary>
        /// Handle receiving a filler item.
        /// Creates a dead drop with the item for the player to collect.
        /// </summary>
        private static void HandleFillerItem(string itemName)
        {
            MelonLogger.Msg($"[Items] Processing filler item: {itemName}");
            NarcopelagoFillers.OnFillerItemReceived(itemName);
        }

        #endregion

        /// <summary>
        /// Resets the item processor state.
        /// </summary>
        public static void Reset()
        {
            IsInitialized = false;
            _skipConsumables = false;
            _holdConsumablesUntilTutorial = false;
            _tutorialCompletedThisSession = false;
            while (_heldConsumables.TryDequeue(out _)) { }
            MelonLogger.Msg("[Items] Reset item processor");
        }

        /// <summary>
        /// Gets the count of items received according to the session.
        /// </summary>
        public static int GetReceivedItemCount()
        {
            var session = ConnectionHandler.CurrentSession;
            return session?.Items?.AllItemsReceived?.Count ?? 0;
        }

        /// <summary>
        /// Checks if we have received a specific item by name.
        /// </summary>
        public static bool HasReceivedItem(string itemName)
        {
            var session = ConnectionHandler.CurrentSession;
            if (session?.Items?.AllItemsReceived == null) return false;

            return session.Items.AllItemsReceived.Any(item => 
                string.Equals(item.ItemName, itemName, StringComparison.OrdinalIgnoreCase));
        }
    }
}
