using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Narcopelago
{
    /// <summary>
    /// Represents a single location entry from locations.json
    /// </summary>
    public class LocationData
    {
        [JsonProperty("region")]
        public string Region { get; set; }

        /// <summary>
        /// Requirements can be either:
        /// - true (always accessible)
        /// - Dictionary of requirements
        /// </summary>
        [JsonProperty("requirements")]
        public object Requirements { get; set; }

        [JsonProperty("tags")]
        public List<string> Tags { get; set; }

        [JsonProperty("modern_id")]
        public int ModernId { get; set; }

        /// <summary>
        /// Helper to check if requirements is simply 'true'
        /// </summary>
        public bool HasNoRequirements()
        {
            if (Requirements is bool boolValue)
                return boolValue;
            return false;
        }

        /// <summary>
        /// Helper to get requirements as a dictionary (if not bool)
        /// </summary>
        public Dictionary<string, object> GetRequirementsDict()
        {
            if (Requirements is JObject jobj)
                return jobj.ToObject<Dictionary<string, object>>();
            if (Requirements is Dictionary<string, object> dict)
                return dict;
            return null;
        }
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

                /// <summary>
                /// Gets all customer sample location names.
                /// These are locations with names starting with "Successful Sample: "
                /// </summary>
                public static List<string> GetAllCustomerSampleLocations()
                {
                    var result = new List<string>();
                    if (Locations == null) return result;

                    foreach (var kvp in Locations)
                    {
                        if (kvp.Key.StartsWith("Successful Sample: ", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Add(kvp.Key);
                        }
                    }
                    return result;
                }

                /// <summary>
                /// Gets the customer name from a sample location name.
                /// E.g., "Successful Sample: Beth Penn" -> "Beth Penn"
                /// </summary>
                public static string GetCustomerNameFromSampleLocation(string locationName)
                {
                    const string prefix = "Successful Sample: ";
                    if (locationName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return locationName.Substring(prefix.Length);
                    }
                    return null;
                }

                /// <summary>
                /// Gets the sample location name for a customer.
                /// E.g., "Beth Penn" -> "Successful Sample: Beth Penn"
                /// </summary>
                public static string GetSampleLocationForCustomer(string customerName)
                {
                    return $"Successful Sample: {customerName}";
                }

                /// <summary>
                /// Gets the list of customer unlocks that would make this sample location available.
                /// Parses the requirements.randomize_customers.has_any array.
                /// </summary>
                /// <param name="locationName">The sample location name (e.g., "Successful Sample: Beth Penn")</param>
                /// <returns>List of customer names whose unlock would enable this sample, or empty list if not found</returns>
                public static List<string> GetRequiredUnlocksForSample(string locationName)
                {
                    var result = new List<string>();
            
                    var location = GetLocation(locationName);
                    if (location == null) return result;

                    try
                    {
                        var reqDict = location.GetRequirementsDict();
                        if (reqDict == null) return result;

                        // Navigate: requirements.randomize_customers.has_any
                        if (!reqDict.TryGetValue("randomize_customers", out var randomizeCustomers))
                            return result;

                        JObject randomizeObj = null;
                        if (randomizeCustomers is JObject jo)
                            randomizeObj = jo;
                        else if (randomizeCustomers is Dictionary<string, object> dict)
                            randomizeObj = JObject.FromObject(dict);

                        if (randomizeObj == null) return result;

                        if (!randomizeObj.TryGetValue("has_any", out var hasAny))
                            return result;

                        // has_any is an array of arrays, e.g., [["Kyle Cooley Unlocked", "Jessi Waters Unlocked"]]
                        if (hasAny is JArray outerArray && outerArray.Count > 0)
                        {
                            var innerArray = outerArray[0] as JArray;
                            if (innerArray != null)
                            {
                                foreach (var item in innerArray)
                                {
                                    string unlockItem = item.ToString();
                                    // Convert "Kyle Cooley Unlocked" to "Kyle Cooley"
                                    if (unlockItem.EndsWith(" Unlocked", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string customerName = unlockItem.Substring(0, unlockItem.Length - " Unlocked".Length);
                                        result.Add(customerName);
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore parsing errors, return empty list
                    }

                    return result;
                }

                /// <summary>
                /// Gets all locations with a specific tag.
                /// </summary>
                public static List<string> GetLocationsByTag(string tag)
                {
                    var result = new List<string>();
                    if (Locations == null) return result;

                    foreach (var kvp in Locations)
                    {
                        if (kvp.Value.Tags?.Contains(tag) == true)
                        {
                            result.Add(kvp.Key);
                        }
                    }
                    return result;
                }
            }
        }

