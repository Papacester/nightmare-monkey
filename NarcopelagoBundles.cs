using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Money;
using MelonLoader;
using System;
using System.Collections.Concurrent;

namespace Narcopelago
{
    /// <summary>
    /// Handles Cash and XP bundle items received from Archipelago.
    /// 
    /// Bundle amounts are interpolated based on the number of bundles:
    /// - The first bundle received gives the minimum amount
    /// - The last bundle received gives the maximum amount
    /// - Intermediate bundles are evenly distributed between min and max
    /// 
    /// If only 1 bundle is configured, only the minimum amount is given.
    /// </summary>
    public static class NarcopelagoBundles
    {
        /// <summary>
        /// Tracks how many cash bundles have been received so far.
        /// </summary>
        private static int _cashBundlesReceived = 0;

        /// <summary>
        /// Tracks how many XP bundles have been received so far.
        /// </summary>
        private static int _xpBundlesReceived = 0;

        /// <summary>
        /// Queue of cash amounts to add on the main thread.
        /// </summary>
        private static ConcurrentQueue<int> _pendingCashAmounts = new ConcurrentQueue<int>();

        /// <summary>
        /// Queue of XP amounts to add on the main thread.
        /// </summary>
        private static ConcurrentQueue<int> _pendingXPAmounts = new ConcurrentQueue<int>();

        /// <summary>
        /// Tracks if we're in a game scene.
        /// </summary>
        private static bool _inGameScene = false;

        /// <summary>
        /// Sets whether we're in a game scene.
        /// </summary>
        public static void SetInGameScene(bool inGame)
        {
            _inGameScene = inGame;
            if (inGame)
            {
                MelonLogger.Msg("[Bundles] Entered game scene");
            }
        }

        /// <summary>
        /// Called when a Cash Bundle item is received from Archipelago.
        /// Calculates the interpolated amount based on how many bundles have been received.
        /// </summary>
        public static void OnCashBundleReceived()
        {
            try
            {
                int numberOfBundles = NarcopelagoOptions.Number_of_cash_bundles;
                int minAmount = NarcopelagoOptions.Amount_of_cash_per_bundle_min;
                int maxAmount = NarcopelagoOptions.Amount_of_cash_per_bundle_max;

                if (numberOfBundles <= 0)
                {
                    MelonLogger.Warning("[Bundles] Received cash bundle but number_of_cash_bundles is 0");
                    return;
                }

                // Calculate the amount for this bundle using interpolation
                int amount = CalculateBundleAmount(_cashBundlesReceived, numberOfBundles, minAmount, maxAmount);

                MelonLogger.Msg($"[Bundles] Cash Bundle #{_cashBundlesReceived + 1}/{numberOfBundles}: ${amount}");
                MelonLogger.Msg($"[Bundles]   (Min: ${minAmount}, Max: ${maxAmount})");

                // Increment the count AFTER calculating (so first bundle uses index 0)
                _cashBundlesReceived++;

                // Queue the cash to be added on the main thread
                _pendingCashAmounts.Enqueue(amount);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Bundles] Error processing cash bundle: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when an XP Bundle item is received from Archipelago.
        /// Calculates the interpolated amount based on how many bundles have been received.
        /// </summary>
        public static void OnXPBundleReceived()
        {
            try
            {
                int numberOfBundles = NarcopelagoOptions.Number_of_xp_bundles;
                int minAmount = NarcopelagoOptions.Amount_of_xp_per_bundle_min;
                int maxAmount = NarcopelagoOptions.Amount_of_xp_per_bundle_max;

                if (numberOfBundles <= 0)
                {
                    MelonLogger.Warning("[Bundles] Received XP bundle but number_of_xp_bundles is 0");
                    return;
                }

                // Calculate the amount for this bundle using interpolation
                int amount = CalculateBundleAmount(_xpBundlesReceived, numberOfBundles, minAmount, maxAmount);

                MelonLogger.Msg($"[Bundles] XP Bundle #{_xpBundlesReceived + 1}/{numberOfBundles}: {amount} XP");
                MelonLogger.Msg($"[Bundles]   (Min: {minAmount}, Max: {maxAmount})");

                // Increment the count AFTER calculating (so first bundle uses index 0)
                _xpBundlesReceived++;

                // Queue the XP to be added on the main thread
                _pendingXPAmounts.Enqueue(amount);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Bundles] Error processing XP bundle: {ex.Message}");
            }
        }

        /// <summary>
        /// Calculates the interpolated bundle amount.
        /// 
        /// Formula: min + (bundleIndex * interpolationConstant)
        /// Where interpolationConstant = (max - min) / (numberOfBundles - 1)
        /// 
        /// Special case: If numberOfBundles is 1, returns min.
        /// </summary>
        /// <param name="bundleIndex">The 0-based index of the current bundle (how many have been received before this one).</param>
        /// <param name="numberOfBundles">Total number of bundles configured.</param>
        /// <param name="minAmount">Minimum amount (first bundle).</param>
        /// <param name="maxAmount">Maximum amount (last bundle).</param>
        /// <returns>The calculated amount for this bundle.</returns>
        private static int CalculateBundleAmount(int bundleIndex, int numberOfBundles, int minAmount, int maxAmount)
        {
            // If only 1 bundle, always return min
            if (numberOfBundles <= 1)
            {
                return minAmount;
            }

            // If we've received more bundles than configured, cap at the last bundle's value (max)
            if (bundleIndex >= numberOfBundles - 1)
            {
                return maxAmount;
            }

            // Calculate interpolation constant: (max - min) / (numberOfBundles - 1)
            // This gives us the step size between each bundle
            float interpolationConstant = (float)(maxAmount - minAmount) / (numberOfBundles - 1);

            // Calculate the amount: min + (bundleIndex * interpolationConstant)
            float amount = minAmount + (bundleIndex * interpolationConstant);

            // Round to nearest integer
            return (int)Math.Round(amount);
        }

        /// <summary>
        /// Process queued bundle rewards on the main thread.
        /// Call this from Core.OnUpdate().
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            if (!_inGameScene)
                return;

            // Process pending cash
            while (_pendingCashAmounts.TryDequeue(out int cashAmount))
            {
                try
                {
                    AddCashToGame(cashAmount);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Bundles] Error adding cash: {ex.Message}");
                }
            }

            // Process pending XP
            while (_pendingXPAmounts.TryDequeue(out int xpAmount))
            {
                try
                {
                    AddXPToGame(xpAmount);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Bundles] Error adding XP: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Adds cash to the player's online bank account.
        /// </summary>
        private static void AddCashToGame(int amount)
        {
            if (!NetworkSingleton<MoneyManager>.InstanceExists)
            {
                MelonLogger.Warning("[Bundles] MoneyManager not available - cannot add cash");
                // Re-queue for later
                _pendingCashAmounts.Enqueue(amount);
                return;
            }

            var moneyManager = NetworkSingleton<MoneyManager>.Instance;
            if (moneyManager == null)
            {
                MelonLogger.Warning("[Bundles] MoneyManager instance is null - cannot add cash");
                _pendingCashAmounts.Enqueue(amount);
                return;
            }

            // Create an online transaction to add the cash
            // Parameters: transaction name, unit amount, quantity, note
            moneyManager.CreateOnlineTransaction("Archipelago Cash Bundle", (float)amount, 1f, "Cash bundle from Archipelago");
            
            MelonLogger.Msg($"[Bundles] Added ${amount} to online balance");
        }

        /// <summary>
        /// Adds XP to the player's rank progress.
        /// </summary>
        private static void AddXPToGame(int amount)
        {
            if (!NetworkSingleton<LevelManager>.InstanceExists)
            {
                MelonLogger.Warning("[Bundles] LevelManager not available - cannot add XP");
                // Re-queue for later
                _pendingXPAmounts.Enqueue(amount);
                return;
            }

            var levelManager = NetworkSingleton<LevelManager>.Instance;
            if (levelManager == null)
            {
                MelonLogger.Warning("[Bundles] LevelManager instance is null - cannot add XP");
                _pendingXPAmounts.Enqueue(amount);
                return;
            }

            // Add XP to the player's rank progress
            levelManager.AddXP(amount);
            
            MelonLogger.Msg($"[Bundles] Added {amount} XP");
        }

        /// <summary>
        /// Syncs the bundle counts from the current Archipelago session.
        /// Call this when entering a game scene to restore state.
        /// 
        /// IMPORTANT: This only counts how many bundles have been received previously.
        /// It does NOT grant any cash or XP - that was already done when the items
        /// were originally received. This ensures future bundles are calculated
        /// correctly using the interpolation formula.
        /// </summary>
        public static void SyncFromSession()
        {
            var session = ConnectionHandler.CurrentSession;
            if (session?.Items?.AllItemsReceived == null)
            {
                MelonLogger.Msg("[Bundles] Cannot sync - no session or items");
                return;
            }

            int cashCount = 0;
            int xpCount = 0;

            // Count all bundles that have already been received
            // We don't grant rewards here - just restore the count so future
            // bundles use the correct interpolation index
            foreach (var item in session.Items.AllItemsReceived)
            {
                string itemName = item.ItemName;
                
                if (IsCashBundleItem(itemName))
                {
                    cashCount++;
                }
                else if (IsXPBundleItem(itemName))
                {
                    xpCount++;
                }
            }

            // Update the counters to match what we've already received
            _cashBundlesReceived = cashCount;
            _xpBundlesReceived = xpCount;

            MelonLogger.Msg($"[Bundles] Synced from session: {cashCount} cash bundles, {xpCount} XP bundles already received");
            MelonLogger.Msg($"[Bundles] Future bundles will be calculated starting from these counts");
        }

        /// <summary>
        /// Checks if an item name is a Cash Bundle item.
        /// </summary>
        public static bool IsCashBundleItem(string itemName)
        {
            return string.Equals(itemName, "Cash Bundle", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if an item name is an XP Bundle item.
        /// </summary>
        public static bool IsXPBundleItem(string itemName)
        {
            return string.Equals(itemName, "XP Bundle", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the number of cash bundles received so far.
        /// </summary>
        public static int GetCashBundlesReceived() => _cashBundlesReceived;

        /// <summary>
        /// Gets the number of XP bundles received so far.
        /// </summary>
        public static int GetXPBundlesReceived() => _xpBundlesReceived;

        /// <summary>
        /// Resets the bundle tracking state.
        /// </summary>
        public static void Reset()
        {
            _cashBundlesReceived = 0;
            _xpBundlesReceived = 0;
            _inGameScene = false;
            
            // Clear queues
            while (_pendingCashAmounts.TryDequeue(out _)) { }
            while (_pendingXPAmounts.TryDequeue(out _)) { }
            
            MelonLogger.Msg("[Bundles] Reset bundle tracking");
        }
    }
}
