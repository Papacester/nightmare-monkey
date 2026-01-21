using HarmonyLib;
using Il2CppScheduleOne.Quests;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Narcopelago
{
    /// <summary>
    /// Handles Archipelago location checks when quest entries are completed in Schedule I.
    /// Each quest has multiple entries (sub-tasks), and we send a check for each entry completion.
    /// </summary>
    public static class NarcopelagoQuests
    {
        // Cache of mission locations for fast lookup (built on first use)
        private static Dictionary<string, int> _missionLocationCache = null;
        private static readonly object _cacheLock = new object();

        /// <summary>
        /// Called when a quest entry (sub-task) is completed.
        /// </summary>
        /// <param name="entry">The quest entry that was completed.</param>
        public static void OnQuestEntryCompleted(QuestEntry entry)
        {
            if (entry == null)
            {
                MelonLogger.Warning("OnQuestEntryCompleted called with null entry");
                return;
            }

            // Get entry and quest identifiers
            string entryName = entry.Title ?? entry.name ?? "Unknown";
            
            // Try to get the parent quest title from the transform hierarchy
            string questTitle = "Unknown";
            try
            {
                var parentQuest = entry.GetComponentInParent<Quest>();
                if (parentQuest != null)
                {
                    questTitle = parentQuest.Title ?? parentQuest.name ?? "Unknown";
                }
            }
            catch
            {
                // Fallback - try to get from gameObject name
                questTitle = entry.gameObject?.transform?.parent?.name ?? "Unknown";
            }
            
            MelonLogger.Msg($"Quest entry completed: {questTitle} - {entryName}");

            // Look up the location ID asynchronously to avoid hitching
            Task.Run(() => ProcessQuestEntryCompletion(questTitle, entryName));
        }

        /// <summary>
        /// Processes quest entry completion asynchronously.
        /// </summary>
        private static void ProcessQuestEntryCompletion(string questTitle, string entryName)
        {
            try
            {
                string locationName = GetLocationNameForQuestEntry(questTitle, entryName);
                
                if (!string.IsNullOrEmpty(locationName))
                {
                    // Use the Archipelago session to get the real location ID from the name
                    long locationId = NarcopelagoLocations.GetLocationId(locationName);
                    
                    if (locationId > 0)
                    {
                        MelonLogger.Msg($"Found location '{locationName}' with ID {locationId} for: {questTitle}, {entryName}");
                        NarcopelagoLocations.CompleteLocation(locationId);
                    }
                    else
                    {
                        MelonLogger.Warning($"Could not get Archipelago ID for location: {locationName}");
                    }
                }
                else
                {
                    MelonLogger.Msg($"No location mapping found for: {questTitle}, {entryName}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error processing quest entry: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when an entire quest is completed (all entries done).
        /// </summary>
        /// <param name="quest">The quest that was completed.</param>
        public static void OnQuestCompleted(Quest quest)
        {
            if (quest == null)
            {
                MelonLogger.Warning("OnQuestCompleted called with null quest");
                return;
            }

            string questName = quest.name ?? quest.gameObject?.name ?? "Unknown";
            string questGuid = quest.GUID.ToString();
            
            MelonLogger.Msg($"Quest fully completed: {questName} (GUID: {questGuid})");
        }

        /// <summary>
        /// Builds the mission location cache from Data_Locations.
        /// Only includes locations with the "Mission" tag.
        /// </summary>
        private static void BuildMissionCache()
        {
            lock (_cacheLock)
            {
                if (_missionLocationCache != null) return;

                _missionLocationCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                if (Data_Locations.Locations == null)
                {
                    MelonLogger.Warning("Cannot build mission cache - locations not loaded");
                    return;
                }

                foreach (var kvp in Data_Locations.Locations)
                {
                    // Only include locations with "Mission" tag
                    if (kvp.Value.Tags != null && kvp.Value.Tags.Contains("Mission"))
                    {
                        _missionLocationCache[kvp.Key] = kvp.Value.ModernId;
                    }
                }

                MelonLogger.Msg($"Built mission cache with {_missionLocationCache.Count} entries");
            }
        }

        /// <summary>
        /// Gets the Archipelago location name for a given quest entry.
        /// Location names are formatted as "Quest Title|Entry Name" (no spaces around the pipe)
        /// </summary>
        /// <param name="questTitle">The title of the parent quest.</param>
        /// <param name="entryName">The name of the quest entry.</param>
        /// <returns>The location name from the JSON, or null if not mapped.</returns>
        public static string GetLocationNameForQuestEntry(string questTitle, string entryName)
        {
            if (!Core.DataLoaded)
            {
                MelonLogger.Warning("Cannot look up quest entry - data not loaded");
                return null;
            }

            // Build cache on first use
            if (_missionLocationCache == null)
            {
                BuildMissionCache();
            }

            if (_missionLocationCache == null || _missionLocationCache.Count == 0)
            {
                return null;
            }

            // Build the expected location name: "Quest Title|Entry Name"
            string expectedLocationName = $"{questTitle}|{entryName}";
            
            // Try direct lookup first (case-insensitive due to dictionary comparer)
            if (_missionLocationCache.ContainsKey(expectedLocationName))
            {
                return expectedLocationName;
            }

            // If not found, search through cached mission locations for a match
            // Format: "Quest Title|Entry Name"
            foreach (var kvp in _missionLocationCache)
            {
                string locationName = kvp.Key;
                
                // Check if this location name contains a pipe separator
                int pipeIndex = locationName.IndexOf('|');
                if (pipeIndex <= 0) continue;

                // Extract the quest and entry parts
                string locationQuestPart = locationName.Substring(0, pipeIndex);
                string locationEntryPart = locationName.Substring(pipeIndex + 1);

                // Check if both quest title and entry name match (case-insensitive)
                if (string.Equals(locationQuestPart, questTitle, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(locationEntryPart, entryName, StringComparison.OrdinalIgnoreCase))
                {
                    return locationName; // Return the full location name for Archipelago lookup
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the Archipelago location ID for a given quest entry.
        /// Location names are formatted as "Quest Title|Entry Name"
        /// </summary>
        /// <param name="questTitle">The title of the parent quest.</param>
        /// <param name="entryName">The name of the quest entry.</param>
        /// <returns>The Archipelago location ID (modern_id), or -1 if not mapped.</returns>
        [Obsolete("Use GetLocationNameForQuestEntry and NarcopelagoLocations.GetLocationId instead")]
        public static int GetLocationIdForQuestEntry(string questTitle, string entryName)
        {
            var locationName = GetLocationNameForQuestEntry(questTitle, entryName);
            if (locationName != null && _missionLocationCache.TryGetValue(locationName, out int modernId))
            {
                return modernId;
            }
            return -1;
        }

        /// <summary>
        /// Clears the mission cache (call if locations data is reloaded).
        /// </summary>
        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _missionLocationCache = null;
            }
        }
    }

    /// <summary>
    /// Harmony patch to detect when a quest entry is completed.
    /// Patches QuestEntry.Complete method to trigger Archipelago location check.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppScheduleOne.Quests.QuestEntry), "Complete")]
    public class QuestEntry_Complete_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] QuestEntry.Complete patch is being prepared");
            return true;
        }

        static void Postfix(Il2CppScheduleOne.Quests.QuestEntry __instance)
        {
            try
            {
                MelonLogger.Msg("[DEBUG] QuestEntry.Complete Postfix triggered!");
                NarcopelagoQuests.OnQuestEntryCompleted(__instance);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in QuestEntry_Complete_Patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Patch for QuestEntry.SetState - this is how the game changes entry state
    /// Method signature: SetState(EQuestState newState, bool network)
    /// </summary>
    [HarmonyPatch(typeof(Il2CppScheduleOne.Quests.QuestEntry), "SetState")]
    public class QuestEntry_SetState_Patch
    {
        static bool Prepare()
        {
            MelonLogger.Msg("[PATCH] QuestEntry.SetState patch is being prepared");
            return true;
        }

        static void Postfix(Il2CppScheduleOne.Quests.QuestEntry __instance, Il2CppScheduleOne.Quests.EQuestState newState, bool network)
        {
            try
            {
                // Only process local completions to avoid duplicates (network=False is the local trigger)
                if (network) return;
                
                MelonLogger.Msg($"[DEBUG] QuestEntry.SetState called with state: {(int)newState} ({newState}), network: {network}");
                
                // Check if the new state is Complete
                if ((int)newState == 2)
                {
                    NarcopelagoQuests.OnQuestEntryCompleted(__instance);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in QuestEntry_SetState_Patch: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Harmony patch to detect when a full quest is completed.
    /// Patches Quest.Complete method to trigger Archipelago location check.
    /// </summary>
    [HarmonyPatch(typeof(Il2CppScheduleOne.Quests.Quest), "Complete")]
    public class Quest_Complete_Patch
    {
        static void Postfix(Il2CppScheduleOne.Quests.Quest __instance)
        {
            try
            {
                NarcopelagoQuests.OnQuestCompleted(__instance);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Error in Quest_Complete_Patch: {ex.Message}");
            }
        }
    }
}
