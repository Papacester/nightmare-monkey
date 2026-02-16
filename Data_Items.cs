using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace Narcopelago
{
    /// <summary>
    /// Represents a single item entry from items.json
    /// Classification is now a dictionary where keys are conditions (e.g., "default", "!randomize_customers")
    /// and values are lists of classifications (e.g., ["PROGRESSION", "USEFUL"])
    /// </summary>
    public class ItemData
    {
        [JsonProperty("classification")]
        public Dictionary<string, List<string>> Classification { get; set; }

        [JsonProperty("tags")]
        public List<string> Tags { get; set; }

        [JsonProperty("modern_id")]
        public int ModernId { get; set; }
    }

    /// <summary>
    /// Static class to hold all item data from items.json
    /// </summary>
    public static class Data_Items
    {
        /// <summary>
        /// Dictionary mapping item name to ItemData
        /// </summary>
        public static Dictionary<string, ItemData> Items { get; private set; }

        /// <summary>
        /// Dictionary mapping modern_id to item name for quick lookup
        /// </summary>
        public static Dictionary<int, string> IdToName { get; private set; }

        /// <summary>
        /// Dictionary mapping item name to modern_id for quick lookup
        /// </summary>
        public static Dictionary<string, int> NameToId { get; private set; }

        /// <summary>
        /// Loads items from JSON string
        /// </summary>
        public static void LoadFromJson(string json)
        {
            Items = JsonConvert.DeserializeObject<Dictionary<string, ItemData>>(json);
            
            IdToName = new Dictionary<int, string>();
            NameToId = new Dictionary<string, int>();

            if (Items != null)
            {
                foreach (var kvp in Items)
                {
                    IdToName[kvp.Value.ModernId] = kvp.Key;
                    NameToId[kvp.Key] = kvp.Value.ModernId;
                }
            }
        }

        /// <summary>
        /// Gets an item by name
        /// </summary>
        public static ItemData GetItem(string name)
        {
            if (Items != null && Items.TryGetValue(name, out var item))
                return item;
            return null;
        }

        /// <summary>
        /// Gets an item name by modern_id
        /// </summary>
        public static string GetItemName(int modernId)
        {
            if (IdToName != null && IdToName.TryGetValue(modernId, out var name))
                return name;
            return null;
        }

        /// <summary>
        /// Gets a modern_id by item name
        /// </summary>
        public static int GetItemId(string name)
        {
            if (NameToId != null && NameToId.TryGetValue(name, out var id))
                return id;
            return -1;
        }

        /// <summary>
        /// Checks if an item has a specific classification under any condition.
        /// Searches all classification condition keys (default, !randomize_customers, etc.)
        /// </summary>
        public static bool HasClassification(string itemName, string classification)
        {
            var item = GetItem(itemName);
            if (item?.Classification == null)
                return false;

            // Check all condition keys for the classification
            foreach (var kvp in item.Classification)
            {
                if (kvp.Value?.Contains(classification) == true)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if an item has a specific classification under the "default" condition.
        /// </summary>
        public static bool HasDefaultClassification(string itemName, string classification)
        {
            var item = GetItem(itemName);
            if (item?.Classification == null)
                return false;

            if (item.Classification.TryGetValue("default", out var defaultClassifications))
            {
                return defaultClassifications?.Contains(classification) == true;
            }
            return false;
        }

        /// <summary>
        /// Gets all classifications for an item under a specific condition key.
        /// </summary>
        public static List<string> GetClassifications(string itemName, string conditionKey = "default")
        {
            var item = GetItem(itemName);
            if (item?.Classification == null)
                return new List<string>();

            if (item.Classification.TryGetValue(conditionKey, out var classifications))
            {
                return classifications ?? new List<string>();
            }
            return new List<string>();
        }

        /// <summary>
        /// Checks if an item has a specific tag
        /// </summary>
        public static bool HasTag(string itemName, string tag)
        {
            var item = GetItem(itemName);
            return item?.Tags?.Contains(tag) ?? false;
        }

        /// <summary>
        /// Checks if an item has any of the specified tags
        /// </summary>
        public static bool HasAnyTag(string itemName, params string[] tags)
        {
            var item = GetItem(itemName);
            if (item?.Tags == null)
                return false;

            foreach (var tag in tags)
            {
                if (item.Tags.Contains(tag))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Gets all items with a specific classification (under any condition)
        /// </summary>
        public static List<string> GetItemsByClassification(string classification)
        {
            var result = new List<string>();
            if (Items != null)
            {
                foreach (var kvp in Items)
                {
                    if (HasClassification(kvp.Key, classification))
                        result.Add(kvp.Key);
                }
            }
            return result;
        }

        /// <summary>
        /// Gets all items with a specific tag
        /// </summary>
        public static List<string> GetItemsByTag(string tag)
        {
            var result = new List<string>();
            if (Items != null)
            {
                foreach (var kvp in Items)
                {
                    if (kvp.Value.Tags?.Contains(tag) == true)
                        result.Add(kvp.Key);
                }
            }
            return result;
        }

        /// <summary>
        /// Gets all items that have any of the specified tags
        /// </summary>
        public static List<string> GetItemsByAnyTag(params string[] tags)
        {
            var result = new List<string>();
            if (Items != null)
            {
                foreach (var kvp in Items)
                {
                    if (kvp.Value.Tags != null)
                    {
                        foreach (var tag in tags)
                        {
                            if (kvp.Value.Tags.Contains(tag))
                            {
                                result.Add(kvp.Key);
                                break;
                            }
                        }
                    }
                }
            }
            return result;
        }
    }
}
