using Archipelago.MultiClient.Net;
using Harmony;
using MelonLoader;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace Narcopelago
{
    /// <summary>
    /// Stores and provides access to Archipelago slot options for Schedule I.
    /// Options are retrieved from the server's SlotData when connecting.
    /// </summary>
    public static class NarcopelagoOptions
    {
        /// <summary>
        /// Indicates whether options have been loaded from the server.
        /// </summary>
        public static bool IsLoaded { get; private set; } = false;

        /// <summary>
        /// Raw slot data dictionary from the server.
        /// </summary>
        public static Dictionary<string, object> RawSlotData { get; private set; }

        // ============================================================
        // Define your game-specific options below
        // These should match the options defined in your APWorld
        // ============================================================

        /// <summary>
        /// Goal condition
        /// </summary>
        public static int Goal { get; private set; } = 0;

        /// <summary>
        /// networth amount required to win. Only Sometimes applicable
        /// </summary>
        public static int Networth_amount_required { get; private set; } = 0;

        /// <summary>
        /// Shuffle Cartel influence
        /// </summary>
        public static bool Randomize_cartel_influence { get; private set; } = false;

        /// <summary>
        /// Shuffle properties
        /// </summary>
        public static bool Randomize_drug_making_properties { get; private set; } = false;

        /// <summary>
        /// Shuffle properties
        /// </summary>
        public static bool Randomize_business_properties { get; private set; } = false;

        /// <summary>
        /// Randomize Dealers, does not include benji (yet)
        /// </summary>
        public static bool Randomize_dealers { get; private set; } = false;

        /// <summary>
        /// Randomzie customers
        /// </summary>
        public static bool Randomize_customers { get; private set; } = false;

        /// <summary>
        /// # of Recipe checks
        /// </summary>
        public static int Recipe_checks { get; private set; } = 0;

        /// <summary>
        /// # of cash for trash checks
        /// </summary>
        public static int Cash_for_trash { get; private set; } = 0;

        /// <summary>
        /// Randomize Level Unlocks
        /// </summary>
        public static bool Randomize_level_unlocks { get; private set; } = false;

        /// <summary>
        /// if Deathlink is enabled or not
        /// </summary>
        public static bool Deathlink { get; private set; } = false;

        // ============================================================
        // Methods
        // ============================================================

        /// <summary>
        /// Loads options from the Archipelago session's SlotData.
        /// Call this after successful connection.
        /// </summary>
        /// <param name="session">The connected Archipelago session.</param>
        public static void LoadFromSession(ArchipelagoSession session)
        {
            if (session == null)
            {
                MelonLogger.Warning("Cannot load options - session is null");
                return;
            }

            try
            {
                var slotData = session.DataStorage.GetSlotData();
                
                if (slotData == null)
                {
                    MelonLogger.Warning("SlotData is null - using default options");
                    IsLoaded = true;
                    return;
                }

                RawSlotData = slotData;
                
                // Parse each option from slot data
                Goal = GetInt(slotData, "goal", 0);
                Networth_amount_required = GetInt(slotData, "networth_amount_required", 0);
                Randomize_cartel_influence = GetBool(slotData, "randomize_cartel_influence", false);
                Randomize_drug_making_properties = GetBool(slotData, "randomize_drug_making_properties", false);
                Randomize_business_properties = GetBool(slotData, "randomize_business_properties", false);
                Randomize_dealers = GetBool(slotData, "randomize_dealers", false);
                Randomize_customers = GetBool(slotData, "randomize_customers", false);
                Recipe_checks = GetInt(slotData, "recipe_checks", 0);
                Cash_for_trash = GetInt(slotData, "cash_for_trash", 0);
                Randomize_level_unlocks = GetBool(slotData, "randomize_level_unlocks", false);
                Deathlink = GetBool(slotData, "deathlink", false);

                IsLoaded = true;
                LogOptions();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to load options: {ex.Message}");
                IsLoaded = false;
            }
        }

        /// <summary>
        /// Loads options directly from a SlotData dictionary.
        /// </summary>
        /// <param name="slotData">The slot data dictionary.</param>
        public static void LoadFromSlotData(Dictionary<string, object> slotData)
        {
            if (slotData == null)
            {
                MelonLogger.Warning("SlotData is null - using default options");
                IsLoaded = true;
                return;
            }

            try
            {
                RawSlotData = slotData;

                // Parse each option from slot data
                Goal = GetInt(slotData, "goal", 0);
                Networth_amount_required = GetInt(slotData, "networth_amount_required", 0);
                Randomize_cartel_influence = GetBool(slotData, "randomize_cartel_influence", false);
                Randomize_drug_making_properties = GetBool(slotData, "randomize_drug_making_properties", false);
                Randomize_business_properties = GetBool(slotData, "randomize_business_properties", false);
                Randomize_dealers = GetBool(slotData, "randomize_dealers", false);
                Randomize_customers = GetBool(slotData, "randomize_customers", false);
                Recipe_checks = GetInt(slotData, "recipe_checks", 0);
                Cash_for_trash = GetInt(slotData, "cash_for_trash", 0);
                Randomize_level_unlocks = GetBool(slotData, "randomize_level_unlocks", false);
                Deathlink = GetBool(slotData, "deathlink", false);

                IsLoaded = true;
                LogOptions();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to load options: {ex.Message}");
                IsLoaded = false;
            }
        }

        /// <summary>
        /// Resets all options to their default values.
        /// </summary>
        public static void Reset()
        {
            Goal = 0;
            Networth_amount_required = 0;
            Randomize_cartel_influence = false;
            Randomize_drug_making_properties = false;
            Randomize_business_properties = false;
            Randomize_dealers = false;
            Randomize_customers = false;
            Recipe_checks = 0;
            Cash_for_trash = 0;
            Randomize_level_unlocks = false;
            Deathlink = false;
        }

        /// <summary>
        /// Logs all current option values for debugging.
        /// </summary>
        public static void LogOptions()
        {
            MelonLogger.Msg("=== Archipelago Options ===");
            MelonLogger.Msg($"  Goal: {Goal}");
            MelonLogger.Msg($"  Networth_amount_required: {Networth_amount_required}");
            MelonLogger.Msg($"  Randomize_cartel_influence: {Randomize_cartel_influence}");
            MelonLogger.Msg($"  Randomize_drug_making_properties: {Randomize_drug_making_properties}");
            MelonLogger.Msg($"  Randomize_business_properties: {Randomize_business_properties}");
            MelonLogger.Msg($"  Randomize_dealers: {Randomize_dealers}");
            MelonLogger.Msg($"  Randomize_customers: {Randomize_customers}");
            MelonLogger.Msg($"  Recipe_checks: {Recipe_checks}");
            MelonLogger.Msg($"  Cash_for_trash: {Cash_for_trash}");
            MelonLogger.Msg($"  Randomize_level_unlocks: {Randomize_level_unlocks}");
            MelonLogger.Msg($"  DeathLink: {Deathlink}");
        }

        /// <summary>
        /// Gets a raw option value by key. Returns null if not found.
        /// </summary>
        /// <param name="key">The option key.</param>
        /// <returns>The raw value, or null if not found.</returns>
        public static object GetRawOption(string key)
        {
            if (RawSlotData != null && RawSlotData.TryGetValue(key, out var value))
            {
                return value;
            }
            return null;
        }

        // ============================================================
        // Helper methods for parsing slot data values
        // ============================================================

        private static bool GetBool(Dictionary<string, object> data, string key, bool defaultValue)
        {
            if (data.TryGetValue(key, out var value))
            {
                if (value is bool b) return b;
                if (value is long l) return l != 0;
                if (value is int i) return i != 0;
                if (value is JValue jv) return jv.ToObject<bool>();
                if (bool.TryParse(value?.ToString(), out var parsed)) return parsed;
            }
            return defaultValue;
        }

        private static int GetInt(Dictionary<string, object> data, string key, int defaultValue)
        {
            if (data.TryGetValue(key, out var value))
            {
                if (value is int i) return i;
                if (value is long l) return (int)l;
                if (value is JValue jv) return jv.ToObject<int>();
                if (int.TryParse(value?.ToString(), out var parsed)) return parsed;
            }
            return defaultValue;
        }

        private static string GetString(Dictionary<string, object> data, string key, string defaultValue)
        {
            if (data.TryGetValue(key, out var value))
            {
                if (value is string s) return s;
                if (value is JValue jv) return jv.ToObject<string>();
                return value?.ToString() ?? defaultValue;
            }
            return defaultValue;
        }

        private static List<string> GetStringList(Dictionary<string, object> data, string key)
        {
            var result = new List<string>();
            if (data.TryGetValue(key, out var value))
            {
                if (value is JArray jArray)
                {
                    foreach (var item in jArray)
                    {
                        result.Add(item.ToString());
                    }
                }
                else if (value is IEnumerable<object> enumerable)
                {
                    foreach (var item in enumerable)
                    {
                        result.Add(item?.ToString() ?? "");
                    }
                }
            }
            return result;
        }
    }
}
