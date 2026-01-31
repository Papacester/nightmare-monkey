using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Product;
using MelonLoader;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Narcopelago
{
    /// <summary>
    /// Tracks recipe/mix discoveries and sends location checks to Archipelago.
    /// 
    /// The game tracks discovered product mixes via ProductManager.DiscoveredProducts.
    /// Each drug type has its own set of recipe check locations:
    /// 
    /// Location IDs and naming:
    /// - Weed: Starting ID 195, "{drug_name} Recipe Check, {i}"
    /// - Meth: Starting ID 210, "{drug_name} Recipe Check, {i}"
    /// - Shrooms: Starting ID 225, "{drug_name} Recipe Check, {i}"
    /// - Cocaine: Starting ID 240, "{drug_name} Recipe Check, {i}"
    /// 
    /// Default mixes that don't count toward checks:
    /// - Weed starts with 4 mixes (checks start after 4th)
    /// - Meth, Shrooms, Cocaine start with 1 mix each (checks start after 1st)
    /// 
    /// This implementation polls the DiscoveredProducts list periodically to detect changes.
    /// </summary>
    public static class NarcopelagoRecipeChecks
    {
        #region Location ID Constants

        /// <summary>
        /// Base location ID for Weed recipe checks.
        /// </summary>
        private const int WEED_BASE_ID = 195;

        /// <summary>
        /// Base location ID for Meth recipe checks.
        /// </summary>
        private const int METH_BASE_ID = 210;

        /// <summary>
        /// Base location ID for Shrooms recipe checks.
        /// </summary>
        private const int SHROOMS_BASE_ID = 225;

        /// <summary>
        /// Base location ID for Cocaine recipe checks.
        /// </summary>
        private const int COCAINE_BASE_ID = 240;

        #endregion

        #region Default Mix Counts (don't count toward checks)

        /// <summary>
        /// Weed starts with 4 default mixes.
        /// </summary>
        private const int WEED_DEFAULT_MIXES = 4;

        /// <summary>
        /// Meth starts with 1 default mix.
        /// </summary>
        private const int METH_DEFAULT_MIXES = 1;

        /// <summary>
        /// Shrooms starts with 1 default mix.
        /// </summary>
        private const int SHROOMS_DEFAULT_MIXES = 1;

        /// <summary>
        /// Cocaine starts with 1 default mix.
        /// </summary>
        private const int COCAINE_DEFAULT_MIXES = 1;

        #endregion

        #region State Tracking

        /// <summary>
        /// Tracks if we're in a game scene.
        /// </summary>
        private static bool _inGameScene = false;

        /// <summary>
        /// Queue of location checks to send on the main thread.
        /// </summary>
        private static ConcurrentQueue<int> _pendingLocationChecks = new ConcurrentQueue<int>();

        /// <summary>
        /// Last known counts for each drug type.
        /// </summary>
        private static Dictionary<EDrugType, int> _lastKnownCounts = new Dictionary<EDrugType, int>
        {
            { EDrugType.Marijuana, 0 },
            { EDrugType.Methamphetamine, 0 },
            { EDrugType.Shrooms, 0 },
            { EDrugType.Cocaine, 0 }
        };

        /// <summary>
        /// Frame counter for polling interval.
        /// </summary>
        private static int _pollFrameCounter = 0;

        /// <summary>
        /// How often to poll the recipe counts (in frames). ~1 second at 60fps.
        /// </summary>
        private const int POLL_INTERVAL_FRAMES = 60;

        /// <summary>
        /// Tracks if initial sync has been done.
        /// </summary>
        private static bool _initialSyncDone = false;

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
                MelonLogger.Msg("[RecipeChecks] Entered game scene");
                _initialSyncDone = false;
                _pollFrameCounter = 0;
            }
            else
            {
                ResetCounts();
            }
        }

        /// <summary>
        /// Syncs from session on load - checks if any locations should be marked complete
        /// based on the current discovered product counts.
        /// </summary>
        public static void SyncFromSession()
        {
            if (!NarcopelagoLocations.IsAvailable)
            {
                MelonLogger.Msg("[RecipeChecks] Cannot sync - not connected to Archipelago");
                return;
            }

            int maxLocations = NarcopelagoOptions.Recipe_checks;
            if (maxLocations <= 0)
            {
                MelonLogger.Msg("[RecipeChecks] Recipe_checks is 0 - no locations to check");
                return;
            }

            MelonLogger.Msg("[RecipeChecks] Syncing from session...");

            // Get current counts for each drug type
            var counts = GetCurrentCounts();
            
            foreach (var kvp in counts)
            {
                _lastKnownCounts[kvp.Key] = kvp.Value;
                int defaultMixes = GetDefaultMixCount(kvp.Key);
                int checksEarned = Math.Max(0, kvp.Value - defaultMixes);
                
                MelonLogger.Msg($"[RecipeChecks] {GetDrugName(kvp.Key)}: {kvp.Value} total mixes, {checksEarned} checks earned (default: {defaultMixes})");
            }

            // Check and queue any locations that should be complete
            int totalQueued = 0;
            foreach (var drugType in new[] { EDrugType.Marijuana, EDrugType.Methamphetamine, EDrugType.Shrooms, EDrugType.Cocaine })
            {
                int count = counts.ContainsKey(drugType) ? counts[drugType] : 0;
                int queued = CheckAndQueueLocations(drugType, 0, count);
                totalQueued += queued;
            }

            if (totalQueued > 0)
            {
                MelonLogger.Msg($"[RecipeChecks] Queued {totalQueued} locations for completion on load");
            }
            else
            {
                MelonLogger.Msg("[RecipeChecks] No new locations to send");
            }
        }

        /// <summary>
        /// Process queued checks on the main thread.
        /// Call this from Core.OnUpdate().
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            if (!_inGameScene)
                return;

            // Poll the recipe counts periodically
            _pollFrameCounter++;
            if (_pollFrameCounter >= POLL_INTERVAL_FRAMES)
            {
                _pollFrameCounter = 0;
                PollAndCheckCounts();
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
                        MelonLogger.Msg($"[RecipeChecks] Completed location ID: {locationId}");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[RecipeChecks] Error completing location {locationId}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Resets the recipe check tracking state.
        /// </summary>
        public static void Reset()
        {
            _inGameScene = false;
            _pollFrameCounter = 0;
            _initialSyncDone = false;
            ResetCounts();
            
            // Clear queue
            while (_pendingLocationChecks.TryDequeue(out _)) { }
            
            MelonLogger.Msg("[RecipeChecks] Reset tracking state");
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Resets the last known counts to zero.
        /// </summary>
        private static void ResetCounts()
        {
            _lastKnownCounts[EDrugType.Marijuana] = 0;
            _lastKnownCounts[EDrugType.Methamphetamine] = 0;
            _lastKnownCounts[EDrugType.Shrooms] = 0;
            _lastKnownCounts[EDrugType.Cocaine] = 0;
        }

        /// <summary>
        /// Polls the current recipe counts and checks if any new locations should be sent.
        /// </summary>
        private static void PollAndCheckCounts()
        {
            if (!NarcopelagoLocations.IsAvailable)
                return;

            int maxLocations = NarcopelagoOptions.Recipe_checks;
            if (maxLocations <= 0)
                return;

            var currentCounts = GetCurrentCounts();

            // First poll after entering scene - just record the values
            if (!_initialSyncDone)
            {
                foreach (var kvp in currentCounts)
                {
                    _lastKnownCounts[kvp.Key] = kvp.Value;
                }
                _initialSyncDone = true;
                MelonLogger.Msg($"[RecipeChecks] Initial counts - Weed: {currentCounts.GetValueOrDefault(EDrugType.Marijuana)}, " +
                               $"Meth: {currentCounts.GetValueOrDefault(EDrugType.Methamphetamine)}, " +
                               $"Shrooms: {currentCounts.GetValueOrDefault(EDrugType.Shrooms)}, " +
                               $"Cocaine: {currentCounts.GetValueOrDefault(EDrugType.Cocaine)}");
                return;
            }

            // Check each drug type for changes
            foreach (var drugType in new[] { EDrugType.Marijuana, EDrugType.Methamphetamine, EDrugType.Shrooms, EDrugType.Cocaine })
            {
                int currentCount = currentCounts.ContainsKey(drugType) ? currentCounts[drugType] : 0;
                int lastCount = _lastKnownCounts.ContainsKey(drugType) ? _lastKnownCounts[drugType] : 0;

                if (currentCount > lastCount)
                {
                    MelonLogger.Msg($"[RecipeChecks] {GetDrugName(drugType)} mix count increased: {lastCount} -> {currentCount}");
                    CheckAndQueueLocations(drugType, lastCount, currentCount);
                    _lastKnownCounts[drugType] = currentCount;
                }
                else if (currentCount != lastCount)
                {
                    // Count changed (decreased?) - just update tracking
                    _lastKnownCounts[drugType] = currentCount;
                }
            }
        }

        /// <summary>
        /// Checks and queues locations for a drug type based on count change.
        /// </summary>
        /// <param name="drugType">The drug type.</param>
        /// <param name="previousCount">Previous total mix count.</param>
        /// <param name="currentCount">Current total mix count.</param>
        /// <returns>Number of locations queued.</returns>
        private static int CheckAndQueueLocations(EDrugType drugType, int previousCount, int currentCount)
        {
            int maxLocations = NarcopelagoOptions.Recipe_checks;
            if (maxLocations <= 0)
                return 0;

            int defaultMixes = GetDefaultMixCount(drugType);
            int baseId = GetBaseLocationId(drugType);
            string drugName = GetDrugName(drugType);

            // Calculate which check tiers we've reached
            // previousChecks = how many checks were earned before
            // currentChecks = how many checks are earned now
            int previousChecks = Math.Max(0, previousCount - defaultMixes);
            int currentChecks = Math.Max(0, currentCount - defaultMixes);

            int queued = 0;

            // Queue each new check we've earned
            for (int i = previousChecks + 1; i <= Math.Min(currentChecks, maxLocations); i++)
            {
                int locationId = GetLocationId(drugType, i);

                // Check if already completed
                if (NarcopelagoLocations.IsLocationChecked(locationId))
                {
                    continue;
                }

                // Queue this location
                _pendingLocationChecks.Enqueue(locationId);
                MelonLogger.Msg($"[RecipeChecks] Earned: {drugName} Recipe Check, {i} (ID: {locationId})");
                queued++;
            }

            return queued;
        }

        /// <summary>
        /// Gets the current discovered product counts for each drug type.
        /// </summary>
        private static Dictionary<EDrugType, int> GetCurrentCounts()
        {
            var counts = new Dictionary<EDrugType, int>
            {
                { EDrugType.Marijuana, 0 },
                { EDrugType.Methamphetamine, 0 },
                { EDrugType.Shrooms, 0 },
                { EDrugType.Cocaine, 0 }
            };

            try
            {
                if (!NetworkSingleton<ProductManager>.InstanceExists)
                    return counts;

                var productManager = NetworkSingleton<ProductManager>.Instance;
                if (productManager == null)
                    return counts;

                // Count products from the static DiscoveredProducts list
                var discoveredProducts = ProductManager.DiscoveredProducts;
                if (discoveredProducts == null)
                    return counts;

                foreach (var product in discoveredProducts)
                {
                    if (product == null)
                        continue;

                    EDrugType drugType = product.DrugType;
                    
                    if (counts.ContainsKey(drugType))
                    {
                        counts[drugType]++;
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[RecipeChecks] Error getting product counts: {ex.Message}");
            }

            return counts;
        }

        /// <summary>
        /// Gets the default mix count for a drug type (mixes that don't count toward checks).
        /// </summary>
        private static int GetDefaultMixCount(EDrugType drugType)
        {
            return drugType switch
            {
                EDrugType.Marijuana => WEED_DEFAULT_MIXES,
                EDrugType.Methamphetamine => METH_DEFAULT_MIXES,
                EDrugType.Shrooms => SHROOMS_DEFAULT_MIXES,
                EDrugType.Cocaine => COCAINE_DEFAULT_MIXES,
                _ => 1
            };
        }

        /// <summary>
        /// Gets the base location ID for a drug type.
        /// </summary>
        private static int GetBaseLocationId(EDrugType drugType)
        {
            return drugType switch
            {
                EDrugType.Marijuana => WEED_BASE_ID,
                EDrugType.Methamphetamine => METH_BASE_ID,
                EDrugType.Shrooms => SHROOMS_BASE_ID,
                EDrugType.Cocaine => COCAINE_BASE_ID,
                _ => 0
            };
        }

        /// <summary>
        /// Gets the location ID for a specific drug type and check number.
        /// </summary>
        /// <param name="drugType">The drug type.</param>
        /// <param name="checkNumber">The check number (1-based).</param>
        /// <returns>The location ID.</returns>
        public static int GetLocationId(EDrugType drugType, int checkNumber)
        {
            int baseId = GetBaseLocationId(drugType);
            // Check 1 = baseId, Check 2 = baseId + 1, etc.
            return baseId + (checkNumber - 1);
        }

        /// <summary>
        /// Gets the display name for a drug type.
        /// </summary>
        private static string GetDrugName(EDrugType drugType)
        {
            return drugType switch
            {
                EDrugType.Marijuana => "Weed",
                EDrugType.Methamphetamine => "Meth",
                EDrugType.Shrooms => "Shrooms",
                EDrugType.Cocaine => "Cocaine",
                _ => drugType.ToString()
            };
        }

        /// <summary>
        /// Gets the location name for a specific drug type and check number.
        /// </summary>
        /// <param name="drugType">The drug type.</param>
        /// <param name="checkNumber">The check number (1-based).</param>
        /// <returns>The location name.</returns>
        public static string GetLocationName(EDrugType drugType, int checkNumber)
        {
            string drugName = GetDrugName(drugType);
            return $"{drugName} Recipe Check, {checkNumber}";
        }

        #endregion
    }
}
