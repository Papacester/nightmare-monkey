using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Variables;
using MelonLoader;
using System;
using System.Collections.Concurrent;

namespace Narcopelago
{
    /// <summary>
    /// Tracks cash-for-trash progress and sends location checks to Archipelago.
    /// 
    /// The game tracks how much trash the player has recycled via the "TrashRecycled" variable.
    /// Every 10 pieces of trash recycled corresponds to one location check.
    /// Location names are: "Cash for Trash {i}, Collect {i * 10} pieces of trash"
    /// where i goes from 1 to NarcopelagoOptions.Cash_for_trash
    /// 
    /// Location IDs start at 600 and increment by 1 for each tier.
    /// (Tier 1 = ID 600, Tier 2 = ID 601, etc.)
    /// 
    /// This implementation polls the TrashRecycled variable periodically to detect changes,
    /// which is more reliable than patching game methods in IL2CPP.
    /// </summary>
    public static class NarcopelagoCashForTrash
    {
        /// <summary>
        /// The base location ID for Cash for Trash locations.
        /// Tier 1 = 600, Tier 2 = 601, etc.
        /// </summary>
        private const int BASE_LOCATION_ID = 600;
        /// <summary>
        /// Tracks if we're in a game scene.
        /// </summary>
        private static bool _inGameScene = false;

        /// <summary>
        /// Queue of location checks to send on the main thread.
        /// </summary>
        private static ConcurrentQueue<int> _pendingLocationChecks = new ConcurrentQueue<int>();

        /// <summary>
        /// The last known trash count we've processed.
        /// Used to detect when new trash has been recycled.
        /// </summary>
        private static int _lastKnownTrashCount = 0;

        /// <summary>
        /// Frame counter for polling interval.
        /// </summary>
        private static int _pollFrameCounter = 0;

        /// <summary>
        /// How often to poll the trash count (in frames). ~1 second at 60fps.
        /// </summary>
        private const int POLL_INTERVAL_FRAMES = 60;

        /// <summary>
        /// Tracks if initial sync has been done.
        /// </summary>
        private static bool _initialSyncDone = false;

        /// <summary>
        /// Sets whether we're in a game scene.
        /// </summary>
        public static void SetInGameScene(bool inGame)
        {
            _inGameScene = inGame;
            if (inGame)
            {
                MelonLogger.Msg("[CashForTrash] Entered game scene");
                _initialSyncDone = false;
                _pollFrameCounter = 0;
            }
            else
            {
                _lastKnownTrashCount = 0;
                _initialSyncDone = false;
            }
        }

        /// <summary>
        /// Syncs from session on load - checks if any locations should be marked complete
        /// based on the current TrashRecycled count.
        /// </summary>
        public static void SyncFromSession()
        {
            if (!NarcopelagoLocations.IsAvailable)
            {
                MelonLogger.Msg("[CashForTrash] Cannot sync - not connected to Archipelago");
                return;
            }

            int maxLocations = NarcopelagoOptions.Cash_for_trash;
            if (maxLocations <= 0)
            {
                MelonLogger.Msg("[CashForTrash] Cash_for_trash is 0 - no locations to check");
                return;
            }

            // Get current trash count from game
            int currentTrashCount = GetCurrentTrashCount();
            _lastKnownTrashCount = currentTrashCount;

            MelonLogger.Msg($"[CashForTrash] Syncing from session - current trash recycled: {currentTrashCount}");

            // Check each location tier and send any that should be complete
            int locationsToCheck = currentTrashCount / 10;
            int locationsSent = 0;

            for (int i = 1; i <= Math.Min(locationsToCheck, maxLocations); i++)
            {
                string locationName = GetLocationName(i);
                int locationId = GetLocationId(i);

                // Check if already completed
                if (NarcopelagoLocations.IsLocationChecked(locationId))
                {
                    continue; // Already done
                }

                // Queue this location to be sent
                _pendingLocationChecks.Enqueue(locationId);
                locationsSent++;
                MelonLogger.Msg($"[CashForTrash] Queued location: {locationName} (ID: {locationId}, trash count: {currentTrashCount})");
            }

            if (locationsSent > 0)
            {
                MelonLogger.Msg($"[CashForTrash] Queued {locationsSent} locations for completion on load");
            }
            else
            {
                MelonLogger.Msg($"[CashForTrash] No new locations to send (completed: {GetCompletedLocationCount()}/{maxLocations})");
            }
        }

        /// <summary>
        /// Process queued checks on the main thread.
        /// Call this from Core.OnUpdate().
        /// Polls the TrashRecycled variable periodically to detect changes.
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            if (!_inGameScene)
                return;

            // Poll the trash count periodically
            _pollFrameCounter++;
            if (_pollFrameCounter >= POLL_INTERVAL_FRAMES)
            {
                _pollFrameCounter = 0;
                PollAndCheckTrashCount();
            }

            // Process pending location checks
            while (_pendingLocationChecks.TryDequeue(out int locationId))
            {
                try
                {
                    // Double-check it's not already completed
                    if (!NarcopelagoLocations.IsLocationChecked(locationId))
                    {
                        NarcopelagoLocations.CompleteLocation(locationId);
                        string locationName = Data_Locations.GetLocationName(locationId) ?? $"ID:{locationId}";
                        MelonLogger.Msg($"[CashForTrash] Completed location: {locationName}");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[CashForTrash] Error completing location {locationId}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Polls the current trash count and checks if any new locations should be sent.
        /// </summary>
        private static void PollAndCheckTrashCount()
        {
            if (!NarcopelagoLocations.IsAvailable)
                return;

            int maxLocations = NarcopelagoOptions.Cash_for_trash;
            if (maxLocations <= 0)
                return;

            int currentTrashCount = GetCurrentTrashCount();

            // Skip if we can't get the count yet
            if (currentTrashCount < 0)
                return;

            // First poll after entering scene - just record the value
            if (!_initialSyncDone)
            {
                _lastKnownTrashCount = currentTrashCount;
                _initialSyncDone = true;
                MelonLogger.Msg($"[CashForTrash] Initial trash count: {currentTrashCount}");
                return;
            }

            // Check if trash count has increased
            if (currentTrashCount > _lastKnownTrashCount)
            {
                MelonLogger.Msg($"[CashForTrash] Trash count increased: {_lastKnownTrashCount} -> {currentTrashCount}");
                CheckAndSendLocations(_lastKnownTrashCount, currentTrashCount);
                _lastKnownTrashCount = currentTrashCount;
            }
            else if (currentTrashCount != _lastKnownTrashCount)
            {
                // Count decreased (reset?) - just update our tracking
                _lastKnownTrashCount = currentTrashCount;
            }
        }

        /// <summary>
        /// Checks and sends locations based on trash count change.
        /// </summary>
        private static void CheckAndSendLocations(int previousCount, int currentCount)
        {
            int maxLocations = NarcopelagoOptions.Cash_for_trash;
            if (maxLocations <= 0)
                return;

            // Calculate which location tiers we've now reached
            int previousTier = previousCount / 10;
            int currentTier = currentCount / 10;

            // Check each new tier we've reached
            for (int i = previousTier + 1; i <= Math.Min(currentTier, maxLocations); i++)
            {
                string locationName = GetLocationName(i);
                int locationId = GetLocationId(i);

                // Check if already completed
                if (NarcopelagoLocations.IsLocationChecked(locationId))
                {
                    MelonLogger.Msg($"[CashForTrash] Location already completed: {locationName}");
                    continue;
                }

                // Queue this location to be sent
                _pendingLocationChecks.Enqueue(locationId);
                MelonLogger.Msg($"[CashForTrash] Earned location: {locationName} (ID: {locationId}, trash count: {currentCount})");
            }
        }

        /// <summary>
        /// Gets the current trash recycled count from the game's VariableDatabase.
        /// </summary>
        private static int GetCurrentTrashCount()
        {
            try
            {
                if (!NetworkSingleton<VariableDatabase>.InstanceExists)
                    return 0;

                var variableDb = NetworkSingleton<VariableDatabase>.Instance;
                if (variableDb == null)
                    return 0;

                float trashRecycled = variableDb.GetValue<float>("TrashRecycled");
                return (int)trashRecycled;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[CashForTrash] Error getting trash count: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Gets the location name for a given tier.
        /// </summary>
        /// <param name="tier">The tier number (1-based).</param>
        /// <returns>The location name.</returns>
        public static string GetLocationName(int tier)
        {
            int trashRequired = tier * 10;
            return $"Cash for Trash {tier}, Collect {trashRequired} pieces of trash";
        }

        /// <summary>
        /// Gets the location ID for a given tier.
        /// Location IDs start at 600 (tier 1) and increment by 1.
        /// </summary>
        /// <param name="tier">The tier number (1-based).</param>
        /// <returns>The location ID.</returns>
        public static int GetLocationId(int tier)
        {
            // Tier 1 = ID 600, Tier 2 = ID 601, etc.
            return BASE_LOCATION_ID + (tier - 1);
        }

        /// <summary>
        /// Gets how many cash-for-trash locations have been completed.
        /// </summary>
        public static int GetCompletedLocationCount()
        {
            if (!NarcopelagoLocations.IsAvailable)
                return 0;

            int maxLocations = NarcopelagoOptions.Cash_for_trash;
            int completedCount = 0;

            for (int i = 1; i <= maxLocations; i++)
            {
                int locationId = GetLocationId(i);

                if (NarcopelagoLocations.IsLocationChecked(locationId))
                {
                    completedCount++;
                }
            }

            return completedCount;
        }

        /// <summary>
        /// Resets the cash-for-trash tracking state.
        /// </summary>
        public static void Reset()
        {
            _inGameScene = false;
            _lastKnownTrashCount = 0;
            _pollFrameCounter = 0;
            _initialSyncDone = false;
            
            // Clear queue
            while (_pendingLocationChecks.TryDequeue(out _)) { }
            
            MelonLogger.Msg("[CashForTrash] Reset tracking state");
        }
    }
}
