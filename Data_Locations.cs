using Newtonsoft.Json;
using System.Collections.Generic;

namespace Narcopelago
{
    /// <summary>
    /// Represents a single location entry from locations.json
    /// </summary>
    public class LocationData
    {
        [JsonProperty("region")]
        public string Region { get; set; }

        [JsonProperty("requirements")]
        public Dictionary<string, int> Requirements { get; set; }

        [JsonProperty("requirements_type")]
        public string RequirementsType { get; set; }

        [JsonProperty("requirements_alt")]
        public Dictionary<string, int> RequirementsAlt { get; set; }

        [JsonProperty("requirements_alt_type")]
        public string RequirementsAltType { get; set; }

        [JsonProperty("tags")]
        public List<string> Tags { get; set; }

        [JsonProperty("modern_id")]
        public int ModernId { get; set; }
    }

    /// <summary>
    /// Static class to hold all location data from locations.json
    /// </summary>
    public static class Data_Locations
    {
        /// <summary>
        /// Dictionary mapping location name to LocationData
        /// </summary>
        public static Dictionary<string, LocationData> Locations { get; private set; }

        /// <summary>
        /// Dictionary mapping modern_id to location name for quick lookup
        /// </summary>
        public static Dictionary<int, string> IdToName { get; private set; }

        /// <summary>
        /// Dictionary mapping location name to modern_id for quick lookup
        /// </summary>
        public static Dictionary<string, int> NameToId { get; private set; }

        /// <summary>
        /// Loads locations from JSON string
        /// </summary>
        public static void LoadFromJson(string json)
        {
            Locations = JsonConvert.DeserializeObject<Dictionary<string, LocationData>>(json);
            
            IdToName = new Dictionary<int, string>();
            NameToId = new Dictionary<string, int>();

            if (Locations != null)
            {
                foreach (var kvp in Locations)
                {
                    IdToName[kvp.Value.ModernId] = kvp.Key;
                    NameToId[kvp.Key] = kvp.Value.ModernId;
                }
            }
        }

        /// <summary>
        /// Gets a location by name
        /// </summary>
        public static LocationData GetLocation(string name)
        {
            if (Locations != null && Locations.TryGetValue(name, out var location))
                return location;
            return null;
        }

        /// <summary>
        /// Gets a location name by modern_id
        /// </summary>
        public static string GetLocationName(int modernId)
        {
            if (IdToName != null && IdToName.TryGetValue(modernId, out var name))
                return name;
            return null;
        }

        /// <summary>
        /// Gets a modern_id by location name
        /// </summary>
        public static int GetLocationId(string name)
        {
            if (NameToId != null && NameToId.TryGetValue(name, out var id))
                return id;
            return -1;
        }
    }
}
