using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.Models;
using MelonLoader;
using System;
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
        /// Checks if an item is a supplier unlock item.
        /// </summary>
        private static bool IsSupplierUnlockItem(string itemName)
        {
            if (!itemName.EndsWith(" Unlocked"))
                return false;

            return Data_Items.HasTag(itemName, "Supplier");
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

        #endregion

        /// <summary>
        /// Resets the item processor state.
        /// </summary>
        public static void Reset()
        {
            IsInitialized = false;
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
