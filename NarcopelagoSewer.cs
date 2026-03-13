using HarmonyLib;
using HarmonyLib;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Tools;
using MelonLoader;
using System;
using System.Collections.Concurrent;

namespace Narcopelago
{
    /// <summary>
    /// Handles sewer-related Archipelago logic:
    /// - Location 403: "Jen Heard Sewer Key Purchase" — sent when the player buys the sewer key from Jen Heard
    /// - Location 404: "Sewer Office Key Pad" — sent when the player completes the keypad puzzle
    /// 
    /// When Randomize_sewer_key is true:
    ///   - Block the sewer key from being awarded when purchased from Jen Heard
    ///   - The "Sewer Key" item must come from Archipelago instead
    /// 
    /// When Randomize_drug_making_properties is true:
    ///   - Block the Sewer Office from becoming owned unless BOTH:
    ///     1. "Sewer Office" item has been received from Archipelago
    ///     2. Location 404 ("Sewer Office Key Pad") has been completed
    ///   - When either condition becomes newly satisfied, attempt to unlock the office
    /// </summary>
    public static class NarcopelagoSewer
    {
        private const int SEWER_KEY_PURCHASE_LOCATION_ID = 403;
        private const int SEWER_KEYPAD_LOCATION_ID = 404;
        private const string SEWER_OFFICE_ITEM_NAME = "Sewer Office";

        /// <summary>
        /// Tracks whether the "Sewer Office" item has been received from Archipelago.
        /// </summary>
        private static bool _sewerOfficeItemReceived = false;

        /// <summary>
        /// Tracks whether the "Sewer Key" item has been received from Archipelago.
        /// </summary>
        private static bool _sewerKeyItemReceived = false;

        /// <summary>
        /// Tracks whether the keypad location (404) has been completed.
        /// </summary>
        private static bool _keypadLocationCompleted = false;

        /// <summary>
        /// Tracks whether we've already sent location 403.
        /// </summary>
        private static bool _keyPurchaseLocationSent = false;

        /// <summary>
        /// Tracks whether we've already sent location 404.
        /// </summary>
        private static bool _keypadLocationSent = false;

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
        /// Queue for main-thread operations.
        /// </summary>
        private static ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

        /// <summary>
        /// Sets whether we're in a game scene.
        /// </summary>
        public static void SetInGameScene(bool inGame)
        {
            _inGameScene = inGame;
            if (inGame)
            {
                MelonLogger.Msg("[Sewer] Entered game scene");
            }
        }

        /// <summary>
        /// Syncs from session on load.
        /// </summary>
        public static void SyncFromSession()
        {
            _syncPending = true;
            _syncDelayFrames = 120;
            MelonLogger.Msg("[Sewer] Queued sync from session");
        }

        /// <summary>
        /// Process main thread queue.
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            if (!_inGameScene) return;

            ProcessPendingSync();

            int processed = 0;
            while (processed < 5 && _mainThreadQueue.TryDequeue(out var action))
            {
                try
                {
                    action();
                    processed++;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Sewer] Error processing queued action: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Called when the "Sewer Key" item is received from Archipelago.
        /// </summary>
        public static void OnSewerKeyItemReceived()
        {
            _sewerKeyItemReceived = true;
            MelonLogger.Msg("[Sewer] Sewer Key item received from Archipelago");
        }

        /// <summary>
        /// Called when the "Sewer Office" item is received from Archipelago.
        /// </summary>
        public static void OnSewerOfficeItemReceived()
        {
            _sewerOfficeItemReceived = true;
            MelonLogger.Msg("[Sewer] Sewer Office item received from Archipelago");

            // Try to unlock now in case keypad was already completed
            _mainThreadQueue.Enqueue(TryUnlockSewerOffice);
        }

        /// <summary>
        /// Called when the player buys the sewer key from Jen Heard's dialogue.
        /// Sends location check 403.
        /// </summary>
        public static void OnSewerKeyPurchased()
        {
            if (_keyPurchaseLocationSent) return;

            _keyPurchaseLocationSent = true;
            MelonLogger.Msg("[Sewer] Sewer key purchased from Jen Heard - sending location check 403");
            NarcopelagoLocations.CompleteLocation(SEWER_KEY_PURCHASE_LOCATION_ID);
        }

        /// <summary>
        /// Called when the player completes the sewer office keypad puzzle.
        /// Sends location check 404.
        /// </summary>
        public static void OnKeypadCompleted()
        {
            if (_keypadLocationSent) return;

            _keypadLocationSent = true;
            _keypadLocationCompleted = true;
            MelonLogger.Msg("[Sewer] Sewer office keypad completed - sending location check 404");
            NarcopelagoLocations.CompleteLocation(SEWER_KEYPAD_LOCATION_ID);

            // Try to unlock the office now in case the item was already received
            TryUnlockSewerOffice();
        }

        /// <summary>
        /// Checks if the sewer key has been received from Archipelago.
        /// Used by the dialogue patch to decide whether to award the key in-game.
        /// </summary>
        public static bool HasSewerKeyItem()
        {
            return _sewerKeyItemReceived;
        }

        /// <summary>
        /// Checks if the "Sewer Office" AP item has been received.
        /// </summary>
        public static bool HasSewerOfficeItem()
        {
            return _sewerOfficeItemReceived;
        }

        /// <summary>
        /// Checks if the keypad location (404) has been completed.
        /// </summary>
        public static bool IsKeypadCompleted()
        {
            return _keypadLocationCompleted;
        }

        /// <summary>
        /// Checks if the Sewer Office should be allowed to become owned.
        /// When Randomize_drug_making_properties is true, requires both the AP item AND keypad completion.
        /// </summary>
        public static bool CanOwnSewerOffice()
        {
            if (!NarcopelagoOptions.Randomize_drug_making_properties && _keypadLocationCompleted)
                return true;
            return _sewerOfficeItemReceived && _keypadLocationCompleted;
        }

        /// <summary>
        /// Attempts to unlock the sewer office if both conditions are met.
        /// </summary>
        private static void TryUnlockSewerOffice()
        {
            if (!_inGameScene) return;
            if (!CanOwnSewerOffice()) return;

            try
            {
                // Find the Sewer Office property
                foreach (var prop in Property.Properties)
                {
                    if (prop == null) continue;

                    var sewerOffice = prop.TryCast<SewerOffice>();
                    if (sewerOffice == null) continue;

                    if (!sewerOffice.IsOwned)
                    {
                        sewerOffice.SetOwned();
                        MelonLogger.Msg("[Sewer] Sewer Office unlocked!");
                    }
                    else
                    {
                        MelonLogger.Msg("[Sewer] Sewer Office already owned");
                    }
                    return;
                }

                MelonLogger.Warning("[Sewer] Could not find Sewer Office property");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Sewer] Error unlocking Sewer Office: {ex.Message}");
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
                MelonLogger.Error($"[Sewer] Error in sync: {ex.Message}");
            }
        }

        /// <summary>
        /// Internal sync from Archipelago session.
        /// </summary>
        private static void SyncFromSessionInternal()
        {
            var session = ConnectionHandler.CurrentSession;
            if (session?.Items?.AllItemsReceived == null)
            {
                MelonLogger.Msg("[Sewer] Cannot sync - no session or items");
                return;
            }

            MelonLogger.Msg("[Sewer] Syncing from session...");

            // Check received items for Sewer Key and Sewer Office
            foreach (var item in session.Items.AllItemsReceived)
            {
                string itemName = item.ItemName;

                if (string.Equals(itemName, "Sewer Key", StringComparison.OrdinalIgnoreCase))
                {
                    _sewerKeyItemReceived = true;
                    MelonLogger.Msg("[Sewer] Synced: Sewer Key received");
                }
                else if (string.Equals(itemName, SEWER_OFFICE_ITEM_NAME, StringComparison.OrdinalIgnoreCase))
                {
                    _sewerOfficeItemReceived = true;
                    MelonLogger.Msg("[Sewer] Synced: Sewer Office received");
                }
            }

            // Check completed locations
            if (NarcopelagoLocations.IsAvailable)
            {
                var checkedLocations = NarcopelagoLocations.AllLocationsChecked;
                if (checkedLocations != null)
                {
                    if (checkedLocations.Contains(SEWER_KEY_PURCHASE_LOCATION_ID))
                    {
                        _keyPurchaseLocationSent = true;
                        MelonLogger.Msg("[Sewer] Synced: Key purchase location already checked");
                    }

                    if (checkedLocations.Contains(SEWER_KEYPAD_LOCATION_ID))
                    {
                        _keypadLocationSent = true;
                        _keypadLocationCompleted = true;
                        MelonLogger.Msg("[Sewer] Synced: Keypad location already checked");
                    }
                }
            }

            // Try to unlock the sewer office if conditions are met
            TryUnlockSewerOffice();

            MelonLogger.Msg($"[Sewer] Sync complete (Key: {_sewerKeyItemReceived}, Office: {_sewerOfficeItemReceived}, Keypad: {_keypadLocationCompleted})");
        }

        /// <summary>
        /// Resets all sewer tracking state.
        /// </summary>
        public static void Reset()
        {
            _sewerOfficeItemReceived = false;
            _sewerKeyItemReceived = false;
            _keypadLocationCompleted = false;
            _keyPurchaseLocationSent = false;
            _keypadLocationSent = false;
            _inGameScene = false;
            _syncPending = false;

            while (_mainThreadQueue.TryDequeue(out _)) { }

            MelonLogger.Msg("[Sewer] Reset");
        }
    }

    // =====================================================================
    // Harmony Patches
    // =====================================================================

    /// <summary>
    /// Harmony patch for DialogueController_Jen.ChoiceCallback
    /// Intercepts the sewer key purchase dialogue to:
    /// 1. Always send location check 403 when CHOICE_CONFIRM is selected
    /// 2. Block key award when Randomize_sewer_key is true (skip original method)
    /// </summary>
    [HarmonyPatch(typeof(DialogueController_Jen), "ChoiceCallback")]
    public class DialogueController_Jen_ChoiceCallback_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] DialogueController_Jen.ChoiceCallback patch is being prepared");
            return true;
        }

        static bool Prefix(DialogueController_Jen __instance, string choiceLabel)
        {
            try
            {
                MelonLogger.Msg($"[PATCH] Jen Heard ChoiceCallback: '{choiceLabel}'");

                // CHOICE_CONFIRM is the confirmation to buy the sewer key
                if (string.Equals(choiceLabel, "CHOICE_CONFIRM", StringComparison.OrdinalIgnoreCase))
                {
                    MelonLogger.Msg("[PATCH] Detected sewer key purchase confirmation");
                    NarcopelagoSewer.OnSewerKeyPurchased();

                    if (NarcopelagoOptions.Randomize_sewer_key)
                    {
                        // Block the original method so the key is NOT awarded in-game.
                        // The key will come from Archipelago as a claimable item instead.
                        MelonLogger.Msg("[PATCH] Blocking sewer key award (Randomize_sewer_key is true)");
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in Jen Heard ChoiceCallback: {ex.Message}");
            }

            return true;
        }
    }

    /// <summary>
    /// Harmony patch for SewerOffice.OnPasscodeCorrect
    /// Intercepts keypad puzzle completion to send location check 404.
    /// </summary>
    [HarmonyPatch(typeof(SewerOffice), "OnPasscodeCorrect")]
    public class SewerOffice_OnPasscodeCorrect_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] SewerOffice.OnPasscodeCorrect patch is being prepared");
            return true;
        }

        static void Postfix()
        {
            try
            {
                MelonLogger.Msg("[PATCH] Sewer office keypad puzzle completed");
                NarcopelagoSewer.OnKeypadCompleted();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in SewerOffice.OnPasscodeCorrect: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Harmony patch for Property.RpcLogic___SetOwned_Server_2166136261
    /// When Randomize_drug_making_properties is true, blocks the Sewer Office from
    /// becoming owned unless BOTH:
    ///   1. "Sewer Office" AP item has been received
    ///   2. Location 404 ("Sewer Office Key Pad") has been completed
    /// </summary>
    [HarmonyPatch(typeof(Property), "RpcLogic___SetOwned_Server_2166136261")]
    public class Property_SetOwned_SewerOffice_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] Property.SetOwned_Server (Sewer Office check) patch is being prepared");
            return true;
        }

        static bool Prefix(Property __instance)
        {
            try
            {
                // Only care about SewerOffice
                var sewerOffice = __instance.TryCast<SewerOffice>();
                if (sewerOffice == null)
                    return true; // Not a sewer office - let other patches/original handle it

                // Check if both conditions are met
                if (NarcopelagoSewer.CanOwnSewerOffice())
                {
                    MelonLogger.Msg("[PATCH] Allowing Sewer Office ownership (AP item + keypad complete)");
                    return true;
                }

                MelonLogger.Msg("[PATCH] Blocking Sewer Office ownership (conditions not met: " +
                    $"Office item={NarcopelagoSewer.HasSewerOfficeItem()}, Keypad={NarcopelagoSewer.IsKeypadCompleted()})");
                return false;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in Property.SetOwned Sewer check: {ex.Message}");
                return true;
            }
        }
    }
}
