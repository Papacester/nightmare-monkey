using Newtonsoft.Json;
using System.Collections.Generic;

namespace Narcopelago
{
    /// <summary>
    /// Represents a single event entry from events.json
    /// </summary>
    public class EventData
    {
        [JsonProperty("region")]
        public string Region { get; set; }

        [JsonProperty("itemName")]
        public string ItemName { get; set; }

        [JsonProperty("locationName")]
        public string LocationName { get; set; }

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
    }

    /// <summary>
    /// Static class to hold all event data from events.json
    /// </summary>
    public static class Data_Events
    {
        /// <summary>
        /// Dictionary mapping event name to EventData
        /// </summary>
        public static Dictionary<string, EventData> Events { get; private set; }

        /// <summary>
        /// Dictionary mapping item name to event name for quick lookup
        /// </summary>
        public static Dictionary<string, string> ItemNameToEventName { get; private set; }

        /// <summary>
        /// Dictionary mapping location name to event name for quick lookup
        /// </summary>
        public static Dictionary<string, string> LocationNameToEventName { get; private set; }

        /// <summary>
        /// Loads events from JSON string
        /// </summary>
        public static void LoadFromJson(string json)
        {
            Events = JsonConvert.DeserializeObject<Dictionary<string, EventData>>(json);
            
            ItemNameToEventName = new Dictionary<string, string>();
            LocationNameToEventName = new Dictionary<string, string>();

            if (Events != null)
            {
                foreach (var kvp in Events)
                {
                    if (!string.IsNullOrEmpty(kvp.Value.ItemName))
                        ItemNameToEventName[kvp.Value.ItemName] = kvp.Key;
                    
                    if (!string.IsNullOrEmpty(kvp.Value.LocationName))
                        LocationNameToEventName[kvp.Value.LocationName] = kvp.Key;
                }
            }
        }

        /// <summary>
        /// Gets an event by name
        /// </summary>
        public static EventData GetEvent(string name)
        {
            if (Events != null && Events.TryGetValue(name, out var eventData))
                return eventData;
            return null;
        }

        /// <summary>
        /// Gets an event by its item name
        /// </summary>
        public static EventData GetEventByItemName(string itemName)
        {
            if (ItemNameToEventName != null && ItemNameToEventName.TryGetValue(itemName, out var eventName))
                return GetEvent(eventName);
            return null;
        }

        /// <summary>
        /// Gets an event by its location name
        /// </summary>
        public static EventData GetEventByLocationName(string locationName)
        {
            if (LocationNameToEventName != null && LocationNameToEventName.TryGetValue(locationName, out var eventName))
                return GetEvent(eventName);
            return null;
        }
    }
}
