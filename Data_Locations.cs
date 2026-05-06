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
        /// Gets a modern_id by location name.
        /// Uses normalized comparison to handle accented characters.
        /// </summary>
        public static int GetLocationId(string name)
        {
            if (NameToId == null || string.IsNullOrEmpty(name)) return -1;
                    
            // Try exact match first
            if (NameToId.TryGetValue(name, out var id))
                return id;
                    
            // Try normalized match for accented characters
            foreach (var kvp in NameToId)
            {
                if (StringHelper.EqualsNormalized(kvp.Key, name))
                    return kvp.Value;
            }
                    
            return -1;
        }

        /// <summary>
        /// Gets a location by name.
        /// Uses normalized comparison to handle accented characters.
        /// </summary>
        public static LocationData GetLocationNormalized(string name)
        {
            if (Locations == null || string.IsNullOrEmpty(name)) return null;
                    
            // Try exact match first
            if (Locations.TryGetValue(name, out var location))
                return location;
                    
            // Try normalized match for accented characters
            foreach (var kvp in Locations)
            {
                if (StringHelper.EqualsNormalized(kvp.Key, name))
                    return kvp.Value;
            }
                    
            return null;
        }

        /// <summary>
        /// Finds the actual location name in the data that matches the given name.
        /// Useful for getting the correct key when names have accented characters.
        /// </summary>
        public static string FindMatchingLocationName(string name)
        {
            if (Locations == null || string.IsNullOrEmpty(name)) return null;
                    
            // Try exact match first
            if (Locations.ContainsKey(name))
                return name;
                    
            // Try normalized match for accented characters
            foreach (var kvp in Locations)
            {
                if (StringHelper.EqualsNormalized(kvp.Key, name))
                    return kvp.Key;
            }
                    
            return null;
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
                /// Uses normalized comparison to find matching locations with accented characters.
                /// E.g., "Javier Pérez" will find "Successful Sample: Javier PeÌrez" (corrupted encoding)
                /// </summary>
                public static string GetSampleLocationForCustomer(string customerName)
                {
                    if (Locations == null || string.IsNullOrEmpty(customerName)) return null;
                    
                    // Try exact match first
                    string exactName = $"Successful Sample: {customerName}";
                    if (Locations.ContainsKey(exactName))
                        return exactName;
                    
                    // Try normalized match for accented characters
                    string normalizedCustomer = StringHelper.NormalizeForComparison(customerName);
                    foreach (var kvp in Locations)
                    {
                        if (kvp.Key.StartsWith("Successful Sample: ", StringComparison.OrdinalIgnoreCase))
                        {
                            string locationCustomerName = kvp.Key.Substring("Successful Sample: ".Length);
                            if (StringHelper.NormalizeForComparison(locationCustomerName) == normalizedCustomer)
                            {
                                return kvp.Key;
                            }
                        }
                    }
                    
                    return null;
                }

                /// <summary>
                /// Gets the list of customer, dealer, and supplier unlocks that would make this sample location available.
                /// Parses the requirements from randomize_customers.has_any, randomize_dealers.has, and randomize_suppliers.has.
                /// </summary>
                /// <param name="locationName">The sample location name (e.g., "Successful Sample: Beth Penn")</param>
                /// <returns>List of names (customers, dealers, suppliers) whose unlock would enable this sample, or empty list if not found</returns>
                public static List<string> GetRequiredUnlocksForSample(string locationName)
                {
                    var result = new List<string>();

                    var location = GetLocation(locationName);
                    if (location == null) return result;

                    try
                    {
                        var reqDict = location.GetRequirementsDict();
                        if (reqDict == null) return result;

                        // Check randomize_customers.has_any for customer connections
                        if (reqDict.TryGetValue("randomize_customers", out var randomizeCustomers))
                        {
                            JObject customersObj = null;
                            if (randomizeCustomers is JObject jo)
                                customersObj = jo;
                            else if (randomizeCustomers is Dictionary<string, object> dict)
                                customersObj = JObject.FromObject(dict);

                            if (customersObj != null && customersObj.TryGetValue("has_any", out var hasAny))
                            {
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
                        }

                        // Check randomize_dealers.has for dealer connections
                        if (reqDict.TryGetValue("randomize_dealers", out var randomizeDealers))
                        {
                            JObject dealersObj = null;
                            if (randomizeDealers is JObject jo)
                                dealersObj = jo;
                            else if (randomizeDealers is Dictionary<string, object> dict)
                                dealersObj = JObject.FromObject(dict);

                            if (dealersObj != null && dealersObj.TryGetValue("has", out var hasDealer))
                            {
                                string dealerItem = hasDealer.ToString();
                                // Convert "Jane Lucero Recruited" to "Jane Lucero"
                                if (dealerItem.EndsWith(" Recruited", StringComparison.OrdinalIgnoreCase))
                                {
                                    string dealerName = dealerItem.Substring(0, dealerItem.Length - " Recruited".Length);
                                    result.Add(dealerName);
                                }
                            }
                        }

                        // Check randomize_suppliers.has for supplier connections
                        if (reqDict.TryGetValue("randomize_suppliers", out var randomizeSuppliers))
                        {
                            JObject suppliersObj = null;
                            if (randomizeSuppliers is JObject jo)
                                suppliersObj = jo;
                            else if (randomizeSuppliers is Dictionary<string, object> dict)
                                suppliersObj = JObject.FromObject(dict);

                            if (suppliersObj != null && suppliersObj.TryGetValue("has", out var hasSupplier))
                            {
                                string supplierItem = hasSupplier.ToString();
                                // Convert "Salvador Moreno Unlocked" to "Salvador Moreno"
                                if (supplierItem.EndsWith(" Unlocked", StringComparison.OrdinalIgnoreCase))
                                {
                                    string supplierName = supplierItem.Substring(0, supplierItem.Length - " Unlocked".Length);
                                    result.Add(supplierName);
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

                        /// <summary>
                        /// Gets all dealer recruit location names.
                        /// These are locations with names starting with "Recruit " and containing "Dealer:" and tagged as "Dealer"
                        /// </summary>
                        public static List<string> GetAllDealerRecruitLocations()
                        {
                            var result = new List<string>();
                            if (Locations == null) return result;

                            foreach (var kvp in Locations)
                            {
                                if (kvp.Key.StartsWith("Recruit ", StringComparison.OrdinalIgnoreCase) &&
                                    kvp.Key.Contains("Dealer:") &&
                                    kvp.Value.Tags?.Contains("Dealer") == true)
                                {
                                    result.Add(kvp.Key);
                                }
                            }
                            return result;
                        }

                        /// <summary>
                        /// Gets the dealer name from a recruit location name.
                        /// E.g., "Recruit Westville Dealer: Molly Presley" -> "Molly Presley"
                        /// </summary>
                        public static string GetDealerNameFromRecruitLocation(string locationName)
                        {
                            // Format: "Recruit <Region> Dealer: <DealerName>"
                            const string dealerMarker = "Dealer: ";
                            int index = locationName.IndexOf(dealerMarker, StringComparison.OrdinalIgnoreCase);
                            if (index >= 0)
                            {
                                return locationName.Substring(index + dealerMarker.Length).Trim();
                            }
                            return null;
                        }

                        /// <summary>
                        /// Gets the recruit location name for a dealer by searching for a matching location.
                        /// E.g., "Molly Presley" -> "Recruit Westville Dealer: Molly Presley"
                        /// </summary>
                        public static string GetRecruitLocationForDealer(string dealerName)
                        {
                            if (Locations == null) return null;

                            foreach (var kvp in Locations)
                            {
                                if (kvp.Key.Contains($"Dealer: {dealerName}") &&
                                    kvp.Value.Tags?.Contains("Dealer") == true)
                                {
                                    return kvp.Key;
                                }
                            }
                            return null;
                        }

                        /// <summary>
                                /// Checks if a location exists for a given dealer.
                                /// </summary>
                                public static bool HasLocationForDealer(string dealerName)
                                {
                                    return GetRecruitLocationForDealer(dealerName) != null;
                                }

                                /// <summary>
                                /// Gets all supplier befriend location names.
                                /// These are locations with names starting with "Befriend Supplier: " and tagged as "Supplier"
                                /// </summary>
                                public static List<string> GetAllSupplierBefriendLocations()
                                {
                                    var result = new List<string>();
                                    if (Locations == null) return result;

                                    foreach (var kvp in Locations)
                                    {
                                        if (kvp.Key.StartsWith("Befriend Supplier: ", StringComparison.OrdinalIgnoreCase) &&
                                            kvp.Value.Tags?.Contains("Supplier") == true)
                                        {
                                            result.Add(kvp.Key);
                                        }
                                    }
                                    return result;
                                }

                                /// <summary>
                                /// Gets the supplier name from a befriend location name.
                                /// E.g., "Befriend Supplier: Shirley Watts" -> "Shirley Watts"
                                /// </summary>
                                public static string GetSupplierNameFromBefriendLocation(string locationName)
                                {
                                    const string prefix = "Befriend Supplier: ";
                                    if (locationName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                    {
                                        return locationName.Substring(prefix.Length).Trim();
                                    }
                                    return null;
                                }

                                /// <summary>
                                /// Gets the befriend location name for a supplier.
                                /// E.g., "Shirley Watts" -> "Befriend Supplier: Shirley Watts"
                                /// </summary>
                                public static string GetBefriendLocationForSupplier(string supplierName)
                                {
                                    string locationName = $"Befriend Supplier: {supplierName}";
                                    if (Locations != null && Locations.ContainsKey(locationName))
                                    {
                                        return locationName;
                                    }
                                    return null;
                                }

                                /// <summary>
                                        /// Checks if a location exists for a given supplier.
                                        /// </summary>
                                        public static bool HasLocationForSupplier(string supplierName)
                                        {
                                            return GetBefriendLocationForSupplier(supplierName) != null;
                                        }

                                        /// <summary>
                                        /// Gets the location ID for a cartel influence check.
                                        /// E.g., GetCartelInfluenceLocationId("Westville", 3) -> ID for "Westville cartel influence 3"
                                        /// </summary>
                                        public static int GetCartelInfluenceLocationId(string region, int checkNumber)
                                        {
                                            string locationName = $"{region} cartel influence {checkNumber}";
                                            return GetLocationId(locationName);
                                        }

                                        /// <summary>
                                        /// Gets all cartel influence location names for a specific region.
                                        /// </summary>
                                        public static List<string> GetCartelInfluenceLocationsForRegion(string region)
                                        {
                                            var result = new List<string>();
                                            if (Locations == null) return result;

                                            string prefix = $"{region} cartel influence ";
                                            foreach (var kvp in Locations)
                                            {
                                                if (kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    result.Add(kvp.Key);
                                                }
                                            }
                                            return result;
                                        }

                                        /// <summary>
                                        /// Gets the number of completed cartel influence checks for a region based on Archipelago data.
                                        /// </summary>
                                        public static int GetCompletedCartelInfluenceCount(string region)
                                        {
                                            int count = 0;
                                            for (int i = 1; i <= 7; i++)
                                            {
                                                int locationId = GetCartelInfluenceLocationId(region, i);
                                                if (locationId > 0 && NarcopelagoLocations.AllLocationsChecked?.Contains(locationId) == true)
                                                {
                                                    count++;
                                                }
                                                else
                                                {
                                                    break; // Stop at first uncompleted
                                                }
                                            }
                                            return count;
                                        }
                                    }
                                }

