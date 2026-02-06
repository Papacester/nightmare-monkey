using MelonLoader;
using System;

namespace Narcopelago
{
    /// <summary>
    /// Handles Cash and XP bundle amount calculations.
    /// 
    /// Bundle amounts are interpolated based on the number of bundles:
    /// - The first bundle received gives the minimum amount
    /// - The last bundle received gives the maximum amount
    /// - Intermediate bundles are evenly distributed between min and max
    /// 
    /// This class only calculates amounts and tracks counts.
    /// Actual granting is handled by NarcopelagoFillers (claimable items list).
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
        /// Calculates the cash amount for the next bundle and increments the counter.
        /// Returns the calculated amount.
        /// </summary>
        public static int CalculateAndTrackCashBundle()
        {
            int numberOfBundles = NarcopelagoOptions.Number_of_cash_bundles;
            int minAmount = NarcopelagoOptions.Amount_of_cash_per_bundle_min;
            int maxAmount = NarcopelagoOptions.Amount_of_cash_per_bundle_max;

            if (numberOfBundles <= 0)
            {
                MelonLogger.Warning("[Bundles] number_of_cash_bundles is 0, defaulting to min amount");
                return minAmount > 0 ? minAmount : 100;
            }

            int amount = CalculateBundleAmount(_cashBundlesReceived, numberOfBundles, minAmount, maxAmount);
            MelonLogger.Msg($"[Bundles] Cash Bundle #{_cashBundlesReceived + 1}/{numberOfBundles}: ${amount}");
            _cashBundlesReceived++;
            return amount;
        }

        /// <summary>
        /// Calculates the XP amount for the next bundle and increments the counter.
        /// Returns the calculated amount.
        /// </summary>
        public static int CalculateAndTrackXPBundle()
        {
            int numberOfBundles = NarcopelagoOptions.Number_of_xp_bundles;
            int minAmount = NarcopelagoOptions.Amount_of_xp_per_bundle_min;
            int maxAmount = NarcopelagoOptions.Amount_of_xp_per_bundle_max;

            if (numberOfBundles <= 0)
            {
                MelonLogger.Warning("[Bundles] number_of_xp_bundles is 0, defaulting to min amount");
                return minAmount > 0 ? minAmount : 100;
            }

            int amount = CalculateBundleAmount(_xpBundlesReceived, numberOfBundles, minAmount, maxAmount);
            MelonLogger.Msg($"[Bundles] XP Bundle #{_xpBundlesReceived + 1}/{numberOfBundles}: {amount} XP");
            _xpBundlesReceived++;
            return amount;
        }

        /// <summary>
        /// Calculates the interpolated bundle amount.
        /// </summary>
        private static int CalculateBundleAmount(int bundleIndex, int numberOfBundles, int minAmount, int maxAmount)
        {
            if (numberOfBundles <= 1) return minAmount;
            if (bundleIndex >= numberOfBundles - 1) return maxAmount;

            float interpolationConstant = (float)(maxAmount - minAmount) / (numberOfBundles - 1);
            float amount = minAmount + (bundleIndex * interpolationConstant);
            return (int)Math.Round(amount);
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
        /// Restores bundle counters from save data.
        /// Call this when loading a save to resume correct bundle amount calculations.
        /// </summary>
        public static void RestoreCounters(int cashBundles, int xpBundles)
        {
            _cashBundlesReceived = cashBundles;
            _xpBundlesReceived = xpBundles;
            MelonLogger.Msg($"[Bundles] Restored counters - Cash: {cashBundles}, XP: {xpBundles}");
        }

        /// <summary>
        /// Resets the bundle tracking state.
        /// </summary>
        public static void Reset()
        {
            _cashBundlesReceived = 0;
            _xpBundlesReceived = 0;
            MelonLogger.Msg("[Bundles] Reset bundle tracking");
        }
    }
}
