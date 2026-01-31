using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Quests;
using MelonLoader;
using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace Narcopelago
{
    /// <summary>
    /// Handles filler items from Archipelago by creating dead drop quests.
    /// 
    /// When receiving an item with a tag containing "Filler":
    /// - Creates a dead drop quest at a random empty dead drop
    /// - Fills the dead drop with the specified quantity of items based on filler tier:
    ///   - "Bad Filler": 5 items
    ///   - "Basic Filler": 3 items
    ///   - "Better Filler": 2 items
    ///   - "Amazing Filler": 1 item
    /// </summary>
    public static class NarcopelagoFillers
    {
        #region Filler Quantities

        private const int BAD_FILLER_QUANTITY = 5;
        private const int BASIC_FILLER_QUANTITY = 3;
        private const int BETTER_FILLER_QUANTITY = 2;
        private const int AMAZING_FILLER_QUANTITY = 1;

        #endregion

        #region State Tracking

        /// <summary>
        /// Tracks if we're in a game scene.
        /// </summary>
        private static bool _inGameScene = false;

        /// <summary>
        /// Queue of filler items to process on the main thread.
        /// Each entry is (itemName, quantity).
        /// </summary>
        private static ConcurrentQueue<(string itemName, int quantity)> _pendingFillers = new ConcurrentQueue<(string, int)>();

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
                MelonLogger.Msg("[Fillers] Entered game scene");
            }
        }

        /// <summary>
        /// Called when a filler item is received from Archipelago.
        /// </summary>
        public static void OnFillerItemReceived(string itemName)
        {
            int quantity = GetFillerQuantity(itemName);
            
            if (quantity <= 0)
            {
                MelonLogger.Warning($"[Fillers] Could not determine quantity for filler item: {itemName}");
                return;
            }

            MelonLogger.Msg($"[Fillers] Received filler item: {itemName} x{quantity}");
            _pendingFillers.Enqueue((itemName, quantity));
        }

        /// <summary>
        /// Process queued filler items on the main thread.
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            if (!_inGameScene)
                return;

            while (_pendingFillers.TryDequeue(out var filler))
            {
                try
                {
                    CreateFillerDeadDrop(filler.itemName, filler.quantity);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Fillers] Error creating dead drop for '{filler.itemName}': {ex.Message}");
                    // Re-queue if we fail due to no available dead drops
                    if (ex.Message.Contains("No empty dead drop"))
                    {
                        _pendingFillers.Enqueue(filler);
                        break; // Stop processing, wait for next frame
                    }
                }
            }
        }

        /// <summary>
        /// Checks if an item is a filler item.
        /// </summary>
        public static bool IsFillerItem(string itemName)
        {
            return Data_Items.HasTag(itemName, "Bad Filler") ||
                   Data_Items.HasTag(itemName, "Basic Filler") ||
                   Data_Items.HasTag(itemName, "Better Filler") ||
                   Data_Items.HasTag(itemName, "Amazing Filler");
        }

        /// <summary>
        /// Resets the filler tracking state.
        /// </summary>
        public static void Reset()
        {
            _inGameScene = false;
            while (_pendingFillers.TryDequeue(out _)) { }
            MelonLogger.Msg("[Fillers] Reset tracking state");
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Gets the quantity of items to give based on the filler tier.
        /// </summary>
        private static int GetFillerQuantity(string itemName)
        {
            if (Data_Items.HasTag(itemName, "Bad Filler"))
                return BAD_FILLER_QUANTITY;
            if (Data_Items.HasTag(itemName, "Basic Filler"))
                return BASIC_FILLER_QUANTITY;
            if (Data_Items.HasTag(itemName, "Better Filler"))
                return BETTER_FILLER_QUANTITY;
            if (Data_Items.HasTag(itemName, "Amazing Filler"))
                return AMAZING_FILLER_QUANTITY;

            return 0;
        }

        /// <summary>
        /// Creates a dead drop with the specified filler items.
        /// </summary>
        private static void CreateFillerDeadDrop(string itemName, int quantity)
        {
            // Get the item definition from the game registry
            ItemDefinition itemDef = Registry.GetItem(itemName);
            if (itemDef == null)
            {
                // Try with different casing or ID format
                itemDef = TryFindItemDefinition(itemName);
            }

            if (itemDef == null)
            {
                MelonLogger.Warning($"[Fillers] Could not find item definition for: {itemName}");
                return;
            }

            // Find a random empty dead drop
            DeadDrop deadDrop = FindEmptyDeadDrop();
            if (deadDrop == null)
            {
                throw new Exception("No empty dead drop available");
            }

            // Create item instance
            ItemInstance itemInstance = itemDef.GetDefaultInstance(quantity);
            if (itemInstance == null)
            {
                MelonLogger.Error($"[Fillers] Failed to create item instance for: {itemName}");
                return;
            }

            // Insert items into the dead drop storage
            if (!deadDrop.Storage.CanItemFit(itemInstance, quantity))
            {
                MelonLogger.Warning($"[Fillers] Dead drop cannot fit {quantity}x {itemName}");
                throw new Exception("No empty dead drop available");
            }

            deadDrop.Storage.InsertItem(itemInstance, true);

            MelonLogger.Msg($"[Fillers] Added {quantity}x {itemName} to dead drop: {deadDrop.DeadDropName}");

            // Create the quest to collect from the dead drop
            CreateDeadDropQuest(deadDrop, itemName, quantity);
        }

        /// <summary>
        /// Tries to find an item definition using various name formats.
        /// </summary>
        private static ItemDefinition TryFindItemDefinition(string itemName)
        {
            // Try exact name
            if (Registry.ItemExists(itemName))
                return Registry.GetItem(itemName);

            // Try lowercase
            if (Registry.ItemExists(itemName.ToLower()))
                return Registry.GetItem(itemName.ToLower());

            // Try replacing spaces with underscores
            string underscored = itemName.Replace(" ", "_");
            if (Registry.ItemExists(underscored))
                return Registry.GetItem(underscored);

            // Try lowercase with underscores
            if (Registry.ItemExists(underscored.ToLower()))
                return Registry.GetItem(underscored.ToLower());

            // Try removing spaces
            string noSpaces = itemName.Replace(" ", "");
            if (Registry.ItemExists(noSpaces))
                return Registry.GetItem(noSpaces);

            // Try lowercase without spaces
            if (Registry.ItemExists(noSpaces.ToLower()))
                return Registry.GetItem(noSpaces.ToLower());

            return null;
        }

        /// <summary>
        /// Finds a random empty dead drop.
        /// </summary>
        private static DeadDrop FindEmptyDeadDrop()
        {
            try
            {
                // Get player position for distance-based selection
                Vector3 playerPos = Vector3.zero;
                var localPlayer = Il2CppScheduleOne.PlayerScripts.Player.Local;
                if (localPlayer != null)
                {
                    playerPos = ((Component)localPlayer).transform.position;
                }

                // Use the game's built-in method to find a random empty drop
                return DeadDrop.GetRandomEmptyDrop(playerPos);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Fillers] Error finding empty dead drop: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Creates a quest to collect items from the dead drop.
        /// </summary>
        private static void CreateDeadDropQuest(DeadDrop deadDrop, string itemName, int quantity)
        {
            try
            {
                if (!NetworkSingleton<QuestManager>.InstanceExists)
                {
                    MelonLogger.Warning("[Fillers] QuestManager not available - cannot create quest");
                    return;
                }

                var questManager = NetworkSingleton<QuestManager>.Instance;
                if (questManager == null)
                {
                    MelonLogger.Warning("[Fillers] QuestManager instance is null");
                    return;
                }

                // Create the dead drop collection quest
                string dropGUID = deadDrop.GUID.ToString();
                DeaddropQuest quest = questManager.CreateDeaddropCollectionQuest(dropGUID, "");

                if (quest != null)
                {
                    MelonLogger.Msg($"[Fillers] Created dead drop quest for {quantity}x {itemName} at {deadDrop.DeadDropName}");
                }
                else
                {
                    MelonLogger.Warning($"[Fillers] Failed to create dead drop quest");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Fillers] Error creating dead drop quest: {ex.Message}");
            }
        }

        #endregion
    }
}
