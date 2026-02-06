using HarmonyLib;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Persistence;
using MelonLoader;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;

namespace Narcopelago
{
    /// <summary>
    /// Manages persistence of Archipelago claimed items.
    /// 
    /// WORKFLOW:
    /// 1. On connection: Get the seed and use it as the save file name
    /// 2. On game scene enter: Load from seed-specific file, compare AP items received vs claimed counts
    /// 3. When player claims item: Increment claimed count in memory
    /// 4. When game saves: Write claimed counts from memory to disk
    /// 5. When leaving game scene: Reload claimed counts from disk (discard any unsaved claims)
    /// 
    /// SAVE FILE NAMING:
    /// Each Archipelago seed gets its own save file: "archipelago_save_{seed}.json"
    /// This allows multiple Archipelago games to be played without data conflicts.
    /// 
    /// Save file location: Data/save/archipelago_save_{seed}.json
    /// </summary>
    public static class NarcopelagoSave
    {
        /// <summary>
        /// The save data structure.
        /// </summary>
        [Serializable]
        public class ArchipelagoSaveData
        {
            /// <summary>
            /// Dictionary of item names to how many have been claimed.
            /// Example: {"Jar": 2, "Cash Bundle": 5, "XP Bundle": 3}
            /// </summary>
            public Dictionary<string, int> ClaimedItemCounts { get; set; } = new Dictionary<string, int>();
        }

        /// <summary>
        /// The current save data in memory.
        /// </summary>
        private static ArchipelagoSaveData _saveData = new ArchipelagoSaveData();

        /// <summary>
        /// Path to the Archipelago save file.
        /// This is set based on the current seed when connecting.
        /// </summary>
        private static string _saveFilePath = null;

        /// <summary>
        /// The current seed (used for naming the save file).
        /// </summary>
        private static string _currentSeed = null;

        /// <summary>
        /// Tracks if we've loaded from disk at least once.
        /// </summary>
        private static bool _loadedFromDisk = false;

        /// <summary>
        /// Tracks if we're in a game scene.
        /// </summary>
        private static bool _inGameScene = false;

        /// <summary>
        /// Tracks if sync has been done for this game scene session.
        /// </summary>
        private static bool _syncDoneThisSession = false;

        /// <summary>
        /// Returns true if the initial sync has been completed this session.
        /// Used by NarcopelagoItems to know when to start processing new consumables.
        /// </summary>
        public static bool IsSyncComplete => _syncDoneThisSession;

        /// <summary>
        /// Delay frames before syncing (to allow AP items to be replayed).
        /// </summary>
        private static int _syncDelayFrames = 0;

        /// <summary>
        /// Flag indicating sync is pending.
        /// </summary>
        private static bool _syncPending = false;

        #region Public API

        /// <summary>
        /// Called once on mod initialization after successful connection.
        /// Sets up the save file path based on the seed and loads from disk.
        /// </summary>
        public static void Initialize()
        {
            if (_loadedFromDisk)
            {
                MelonLogger.Msg("[Save] Already loaded from disk");
                return;
            }

            try
            {
                // Get the seed from the current session
                _currentSeed = GetCurrentSeed();
                
                if (string.IsNullOrEmpty(_currentSeed))
                {
                    MelonLogger.Warning("[Save] Could not get seed - using default save file");
                    _currentSeed = "default";
                }
                
                _saveFilePath = GetArchipelagoSavePath(_currentSeed);
                MelonLogger.Msg($"[Save] Using save file for seed '{_currentSeed}': {_saveFilePath}");
                
                LoadFromDisk();
                _loadedFromDisk = true;
                MelonLogger.Msg("[Save] Initialized - loaded claimed counts from disk");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Save] Failed to initialize: {ex.Message}");
                _saveData = new ArchipelagoSaveData();
            }
        }

        /// <summary>
        /// Called when entering/leaving a game scene.
        /// On enter: Queue sync after delay
        /// On leave: Reload from disk (discard unsaved changes)
        /// </summary>
        public static void SetInGameScene(bool inGame)
        {
            bool wasInGame = _inGameScene;
            _inGameScene = inGame;

            if (inGame && !wasInGame)
            {
                // Entering game scene - queue sync after delay to allow AP items to replay
                MelonLogger.Msg("[Save] Entering game scene - will sync after delay");
                _syncDoneThisSession = false;
                _syncPending = true;
                _syncDelayFrames = 180; // ~3 seconds at 60fps
            }
            else if (!inGame && wasInGame)
            {
                // Leaving game scene - reload from disk to discard unsaved claims
                MelonLogger.Msg("[Save] Leaving game scene - reloading from disk");
                LoadFromDisk();
                _syncDoneThisSession = false;
                _syncPending = false;
                
                // Also clear the fillers list since we're leaving the game
                NarcopelagoFillers.Reset();
            }
        }

        /// <summary>
        /// Called from Core.OnUpdate() to process pending sync.
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            if (!_inGameScene || !_syncPending)
                return;

            if (_syncDelayFrames > 0)
            {
                _syncDelayFrames--;
                return;
            }

            _syncPending = false;

            if (!_syncDoneThisSession)
            {
                SyncClaimableItemsFromSession();
                _syncDoneThisSession = true;
            }
        }

        /// <summary>
        /// Records that an item has been claimed from the phone app.
        /// Increments the claimed count in memory (saved to disk when game saves).
        /// </summary>
        public static void MarkItemAsClaimed(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return;

            if (_saveData.ClaimedItemCounts.ContainsKey(itemName))
            {
                _saveData.ClaimedItemCounts[itemName]++;
            }
            else
            {
                _saveData.ClaimedItemCounts[itemName] = 1;
            }

            MelonLogger.Msg($"[Save] Marked '{itemName}' as claimed (total: {_saveData.ClaimedItemCounts[itemName]}) - will save when game saves");
        }

        /// <summary>
        /// Gets how many of a specific item have been claimed.
        /// </summary>
        public static int GetClaimedCount(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return 0;

            return _saveData.ClaimedItemCounts.TryGetValue(itemName, out int count) ? count : 0;
        }

        /// <summary>
        /// Saves the current claimed counts to disk.
        /// Called when the game saves (via Harmony patch).
        /// </summary>
        public static void Save()
        {
            if (string.IsNullOrEmpty(_saveFilePath))
            {
                // Ensure we have a seed
                if (string.IsNullOrEmpty(_currentSeed))
                {
                    _currentSeed = GetCurrentSeed() ?? "default";
                }
                _saveFilePath = GetArchipelagoSavePath(_currentSeed);
            }

            try
            {
                string saveDir = Path.GetDirectoryName(_saveFilePath);
                if (!Directory.Exists(saveDir))
                {
                    Directory.CreateDirectory(saveDir);
                }

                string json = JsonConvert.SerializeObject(_saveData, Formatting.Indented);
                File.WriteAllText(_saveFilePath, json);

                MelonLogger.Msg($"[Save] Saved {_saveData.ClaimedItemCounts.Count} item types to disk");
                foreach (var kvp in _saveData.ClaimedItemCounts)
                {
                    MelonLogger.Msg($"[Save]   - {kvp.Key}: {kvp.Value}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Save] Failed to save: {ex.Message}");
            }
        }

        /// <summary>
        /// Deletes the save file. Call when starting a fresh slot.
        /// </summary>
        public static void DeleteSaveFile()
        {
            try
            {
                if (string.IsNullOrEmpty(_saveFilePath))
                {
                    // Ensure we have a seed
                    if (string.IsNullOrEmpty(_currentSeed))
                    {
                        _currentSeed = GetCurrentSeed() ?? "default";
                    }
                    _saveFilePath = GetArchipelagoSavePath(_currentSeed);
                }

                if (File.Exists(_saveFilePath))
                {
                    File.Delete(_saveFilePath);
                    MelonLogger.Msg("[Save] Deleted save file");
                }

                _saveData = new ArchipelagoSaveData();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Save] Failed to delete: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets in-memory state without touching disk.
        /// </summary>
        public static void Reset()
        {
            _saveData = new ArchipelagoSaveData();
            _loadedFromDisk = false;
            _syncDoneThisSession = false;
            _syncPending = false;
            _currentSeed = null;
            _saveFilePath = null;
            MelonLogger.Msg("[Save] Reset in-memory state");
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Loads claimed counts from disk into memory.
        /// </summary>
        private static void LoadFromDisk()
        {
            if (string.IsNullOrEmpty(_saveFilePath))
            {
                // Ensure we have a seed
                if (string.IsNullOrEmpty(_currentSeed))
                {
                    _currentSeed = GetCurrentSeed() ?? "default";
                }
                _saveFilePath = GetArchipelagoSavePath(_currentSeed);
            }

            try
            {
                if (!File.Exists(_saveFilePath))
                {
                    MelonLogger.Msg("[Save] No save file found - starting fresh");
                    _saveData = new ArchipelagoSaveData();
                    return;
                }

                string json = File.ReadAllText(_saveFilePath);
                var loaded = JsonConvert.DeserializeObject<ArchipelagoSaveData>(json);

                if (loaded == null)
                {
                    MelonLogger.Warning("[Save] Failed to deserialize - starting fresh");
                    _saveData = new ArchipelagoSaveData();
                    return;
                }

                _saveData = loaded;
                MelonLogger.Msg($"[Save] Loaded {_saveData.ClaimedItemCounts.Count} item types from disk");
                foreach (var kvp in _saveData.ClaimedItemCounts)
                {
                    MelonLogger.Msg($"[Save]   - {kvp.Key}: {kvp.Value}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Save] Failed to load: {ex.Message}");
                _saveData = new ArchipelagoSaveData();
            }
        }

        /// <summary>
        /// Compares AP items received vs claimed counts and adds unclaimed to phone app.
        /// </summary>
        private static void SyncClaimableItemsFromSession()
        {
            var session = ConnectionHandler.CurrentSession;
            if (session?.Items?.AllItemsReceived == null)
            {
                MelonLogger.Msg("[Save] Cannot sync - no session or items");
                return;
            }

            var allItems = session.Items.AllItemsReceived;
            MelonLogger.Msg($"[Save] Syncing from {allItems.Count} AP items (seed: {_currentSeed})...");

            // Count received consumables
            Dictionary<string, int> receivedCounts = new Dictionary<string, int>();
            Dictionary<string, List<int>> bundleAmounts = new Dictionary<string, List<int>>();

            int cashBundleIndex = 0;
            int xpBundleIndex = 0;

            foreach (var item in allItems)
            {
                string itemName = item.ItemName;

                if (NarcopelagoBundles.IsCashBundleItem(itemName))
                {
                    if (!receivedCounts.ContainsKey(itemName))
                    {
                        receivedCounts[itemName] = 0;
                        bundleAmounts[itemName] = new List<int>();
                    }
                    receivedCounts[itemName]++;
                    bundleAmounts[itemName].Add(CalculateCashBundleAmount(cashBundleIndex));
                    cashBundleIndex++;
                }
                else if (NarcopelagoBundles.IsXPBundleItem(itemName))
                {
                    if (!receivedCounts.ContainsKey(itemName))
                    {
                        receivedCounts[itemName] = 0;
                        bundleAmounts[itemName] = new List<int>();
                    }
                    receivedCounts[itemName]++;
                    bundleAmounts[itemName].Add(CalculateXPBundleAmount(xpBundleIndex));
                    xpBundleIndex++;
                }
                else if (NarcopelagoFillers.IsFillerItem(itemName))
                {
                    if (!receivedCounts.ContainsKey(itemName))
                        receivedCounts[itemName] = 0;
                    receivedCounts[itemName]++;
                }
            }

            // Add unclaimed items to phone app
            int totalAdded = 0;
            int totalSkipped = 0;

            foreach (var kvp in receivedCounts)
            {
                string itemName = kvp.Key;
                int received = kvp.Value;
                int claimed = GetClaimedCount(itemName);
                int unclaimed = received - claimed;

                MelonLogger.Msg($"[Save] {itemName}: received={received}, claimed={claimed}, unclaimed={unclaimed}");

                if (unclaimed <= 0)
                {
                    totalSkipped += received;
                    continue;
                }

                if (NarcopelagoBundles.IsCashBundleItem(itemName))
                {
                    var amounts = bundleAmounts[itemName];
                    for (int i = claimed; i < received && i < amounts.Count; i++)
                    {
                        NarcopelagoFillers.OnCashBundleReceived(amounts[i]);
                        totalAdded++;
                    }
                }
                else if (NarcopelagoBundles.IsXPBundleItem(itemName))
                {
                    var amounts = bundleAmounts[itemName];
                    for (int i = claimed; i < received && i < amounts.Count; i++)
                    {
                        NarcopelagoFillers.OnXPBundleReceived(amounts[i]);
                        totalAdded++;
                    }
                }
                else if (NarcopelagoFillers.IsFillerItem(itemName))
                {
                    for (int i = 0; i < unclaimed; i++)
                    {
                        NarcopelagoFillers.OnFillerItemReceived(itemName);
                        totalAdded++;
                    }
                }

                totalSkipped += claimed;
            }

            MelonLogger.Msg($"[Save] Sync complete: {totalAdded} added to app, {totalSkipped} already claimed");
        }

        private static string GetArchipelagoSavePath(string seed)
        {
            try
            {
                string saveDir = Path.Combine(Core.DataPath, "save");
                if (!Directory.Exists(saveDir))
                {
                    Directory.CreateDirectory(saveDir);
                }
                
                // Sanitize the seed to be a valid filename
                string safeSeed = SanitizeFilename(seed);
                return Path.Combine(saveDir, $"archipelago_save_{safeSeed}.json");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Save] Error getting save path: {ex.Message}");
                return Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, $"archipelago_save_{seed}.json");
            }
        }

        /// <summary>
        /// Sanitizes a string to be safe for use as a filename.
        /// </summary>
        private static string SanitizeFilename(string input)
        {
            if (string.IsNullOrEmpty(input))
                return "default";

            // Replace invalid characters with underscores
            char[] invalidChars = Path.GetInvalidFileNameChars();
            string result = input;
            foreach (char c in invalidChars)
            {
                result = result.Replace(c, '_');
            }
            
            // Limit length to avoid path issues
            if (result.Length > 50)
            {
                result = result.Substring(0, 50);
            }
            
            return result;
        }

        /// <summary>
        /// Gets the current seed from the Archipelago session.
        /// </summary>
        private static string GetCurrentSeed()
        {
            try
            {
                var session = ConnectionHandler.CurrentSession;
                if (session?.RoomState != null)
                {
                    return session.RoomState.Seed;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Save] Error getting seed: {ex.Message}");
            }
            return null;
        }

        private static int CalculateCashBundleAmount(int bundleIndex)
        {
            int numberOfBundles = NarcopelagoOptions.Number_of_cash_bundles;
            int minAmount = NarcopelagoOptions.Amount_of_cash_per_bundle_min;
            int maxAmount = NarcopelagoOptions.Amount_of_cash_per_bundle_max;

            if (numberOfBundles <= 0) return minAmount > 0 ? minAmount : 100;
            if (numberOfBundles <= 1) return minAmount;
            if (bundleIndex >= numberOfBundles - 1) return maxAmount;

            float t = (float)bundleIndex / (numberOfBundles - 1);
            return (int)Math.Round(minAmount + t * (maxAmount - minAmount));
        }

        private static int CalculateXPBundleAmount(int bundleIndex)
        {
            int numberOfBundles = NarcopelagoOptions.Number_of_xp_bundles;
            int minAmount = NarcopelagoOptions.Amount_of_xp_per_bundle_min;
            int maxAmount = NarcopelagoOptions.Amount_of_xp_per_bundle_max;

            if (numberOfBundles <= 0) return minAmount > 0 ? minAmount : 100;
            if (numberOfBundles <= 1) return minAmount;
            if (bundleIndex >= numberOfBundles - 1) return maxAmount;

            float t = (float)bundleIndex / (numberOfBundles - 1);
            return (int)Math.Round(minAmount + t * (maxAmount - minAmount));
        }

        #endregion
    }

    /// <summary>
    /// Harmony patch for SaveManager.Save to save our data when the game saves.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), "Save", new Type[] { })]
    public class SaveManager_Save_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] SaveManager.Save patch is being prepared");
            return true;
        }

        static void Postfix()
        {
            try
            {
                MelonLogger.Msg("[PATCH] Game save triggered - saving Archipelago data");
                NarcopelagoSave.Save();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[PATCH] Error in SaveManager.Save Postfix: {ex.Message}");
            }
        }
    }
}
