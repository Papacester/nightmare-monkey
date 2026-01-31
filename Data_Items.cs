using Newtonsoft.Json;
using System.Collections.Generic;

namespace Narcopelago
{
    /// <summary>
    /// Represents a single item entry from items.json
    /// </summary>
    public class ItemData
    {
        [JsonProperty("classification")]
        public List<string> Classification { get; set; }

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
        /// Checks if an item has a specific classification
        /// </summary>
        public static bool HasClassification(string itemName, string classification)
        {
            var item = GetItem(itemName);
            return item?.Classification?.Contains(classification) ?? false;
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
        /// Gets all items with a specific classification
        /// </summary>
        public static List<string> GetItemsByClassification(string classification)
        {
            var result = new List<string>();
            if (Items != null)
            {
                foreach (var kvp in Items)
                {
                    if (kvp.Value.Classification?.Contains(classification) == true)
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
    }
}
