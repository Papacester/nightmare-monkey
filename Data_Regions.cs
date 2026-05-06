using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Narcopelago
{
    /// <summary>
    /// Represents a single region entry from regions.json
    /// </summary>
    public class RegionData
    {
        [JsonProperty("connections")]
        public Dictionary<string, object> Connections { get; set; }
    }

    /// <summary>
    /// Static class to hold all region data from regions.json.
    /// Used to determine if a customer's region is reachable based on received items.
    /// </summary>
    public static class Data_Regions
    {
        /// <summary>
        /// Dictionary mapping region name to RegionData
        /// </summary>
        public static Dictionary<string, RegionData> Regions { get; private set; }

        /// <summary>
        /// Loads regions from JSON string
        /// </summary>
        public static void LoadFromJson(string json)
        {
            Regions = JsonConvert.DeserializeObject<Dictionary<string, RegionData>>(json);
        }

        /// <summary>
        /// Gets a region by name
        /// </summary>
        public static RegionData GetRegion(string name)
        {
            if (Regions != null && Regions.TryGetValue(name, out var region))
                return region;
            return null;
        }

        /// <summary>
        /// Checks if a customer's sample location is "in logic" for randomize_level_unlocks.
        /// Checks both the location's requirements AND the region access requirements.
        /// When Randomize_level_unlocks is false, all customers are considered in logic.
        /// </summary>
        /// <param name="customerName">The customer's full name</param>
        /// <returns>True if the customer is in logic (requirements met or option disabled)</returns>
        public static bool IsCustomerInLevelUnlockLogic(string customerName)
        {
            // If the option is disabled, everything is in logic
            if (!NarcopelagoOptions.IsLoaded || !NarcopelagoOptions.Randomize_level_unlocks)
                return true;

            // Find the sample location for this customer
            string sampleLocation = Data_Locations.GetSampleLocationForCustomer(customerName);
            if (string.IsNullOrEmpty(sampleLocation))
                return true; // No location found, assume in logic

            var location = Data_Locations.GetLocationNormalized(sampleLocation);
            if (location == null)
                return true;

            // Check the randomize_level_unlocks requirements on the location itself
            var reqDict = location.GetRequirementsDict();
            if (reqDict != null && reqDict.TryGetValue("randomize_level_unlocks", out var levelUnlockReqs))
            {
                if (!CheckLevelUnlockRequirements(levelUnlockReqs))
                    return false; // Location requirements not met
            }

            // Also check if the customer's region is accessible
            // This is important for regions like Suburbia that don't have level unlock requirements on locations
            // but require going through Docks (which requires Fertilizer Unlock)
            string regionName = location.Region;
            if (!string.IsNullOrEmpty(regionName))
            {
                bool regionAccessible = IsRegionAccessibleForLevelUnlocks(regionName);
                if (!regionAccessible)
                {
                    MelonLoader.MelonLogger.Msg($"[Regions] Customer '{customerName}' is out of logic - region '{regionName}' not accessible");
                    return false; // Region not accessible
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if a region is accessible based on randomize_level_unlocks requirements.
        /// Uses BFS to traverse from Overworld to the target region, checking level unlock requirements on connections.
        /// </summary>
        /// <param name="targetRegion">The region to check accessibility for</param>
        /// <returns>True if the region is accessible with current level unlocks</returns>
        public static bool IsRegionAccessibleForLevelUnlocks(string targetRegion)
        {
            if (Regions == null)
                return true;

            // Special case: Overworld and starting regions are always accessible
            if (string.Equals(targetRegion, "Overworld", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetRegion, "Welcome to Hyland Point", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetRegion, "Northtown", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Start from Overworld and traverse using BFS
            var queue = new Queue<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var blockedConnections = new List<string>();

            queue.Enqueue("Overworld");
            visited.Add("Overworld");

            while (queue.Count > 0)
            {
                string currentRegion = queue.Dequeue();

                // Found the target region
                if (string.Equals(currentRegion, targetRegion, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                // Get connections from this region
                var region = GetRegion(currentRegion);
                if (region?.Connections == null)
                    continue;

                foreach (var kvp in region.Connections)
                {
                    string connectedRegion = kvp.Key;

                    // Skip if already visited
                    if (visited.Contains(connectedRegion))
                        continue;

                    // Check if this connection requires level unlocks
                    bool connectionAccessible = true;
                    if (kvp.Value is JObject connectionReqs)
                    {
                        // Check if there's a randomize_level_unlocks requirement for this connection
                        if (connectionReqs.TryGetValue("randomize_level_unlocks", out var levelUnlockReqs))
                        {
                            connectionAccessible = CheckLevelUnlockRequirements(levelUnlockReqs);
                            if (!connectionAccessible)
                            {
                                blockedConnections.Add($"{currentRegion} -> {connectedRegion}");
                            }
                        }
                    }
                    else if (kvp.Value is bool boolValue)
                    {
                        connectionAccessible = boolValue;
                    }

                    // Only traverse if connection is accessible
                    if (connectionAccessible)
                    {
                        queue.Enqueue(connectedRegion);
                        visited.Add(connectedRegion);
                    }
                }
            }

            // Target region not found in traversal - not accessible
            if (blockedConnections.Count > 0)
            {
                MelonLoader.MelonLogger.Msg($"[Regions] Region '{targetRegion}' not accessible - blocked connections: {string.Join(", ", blockedConnections)}");
            }
            return false;
        }

        /// <summary>
        /// Checks if the randomize_level_unlocks requirements are satisfied by received items.
        /// Supports "has", "has_all", and "has_any" requirement types.
        /// </summary>
        private static bool CheckLevelUnlockRequirements(object requirements)
        {
            JObject reqObj = null;
            if (requirements is JObject jo)
                reqObj = jo;
            else if (requirements is Dictionary<string, object> dict)
                reqObj = JObject.FromObject(dict);

            if (reqObj == null)
                return true;

            // Check "has" - single item required
            if (reqObj.TryGetValue("has", out var hasValue))
            {
                string requiredItem = hasValue.ToString();
                if (!NarcopelagoItems.HasReceivedItem(requiredItem))
                    return false;
            }

            // Check "has_all" - all items required
            if (reqObj.TryGetValue("has_all", out var hasAllValue))
            {
                if (hasAllValue is JArray allArray)
                {
                    foreach (var item in allArray)
                    {
                        if (!NarcopelagoItems.HasReceivedItem(item.ToString()))
                            return false;
                    }
                }
            }

            // Check "has_any" - at least one item from any inner array required
            if (reqObj.TryGetValue("has_any", out var hasAnyValue))
            {
                if (hasAnyValue is JArray outerArray && outerArray.Count > 0)
                {
                    foreach (var innerItem in outerArray)
                    {
                        if (innerItem is JArray innerArray)
                        {
                            bool anyFound = false;
                            foreach (var item in innerArray)
                            {
                                if (NarcopelagoItems.HasReceivedItem(item.ToString()))
                                {
                                    anyFound = true;
                                    break;
                                }
                            }
                            if (!anyFound)
                                return false;
                        }
                        else
                        {
                            // Single item in has_any
                            if (!NarcopelagoItems.HasReceivedItem(innerItem.ToString()))
                                return false;
                        }
                    }
                }
            }

            return true;
        }
    }
}
