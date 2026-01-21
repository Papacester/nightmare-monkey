using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using HarmonyLib;
using JetBrains.Annotations;
using MelonLoader;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Relation;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;   

[assembly: MelonInfo(typeof(Narcopelago.Core), "Narcopelago", "1.0.0", "Papacestor, MacH8s", null)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace Narcopelago
{
    public class Core : MelonMod
    {
        /// <summary>
        /// Path to the mod's data folder
        /// </summary>
        public static string DataPath { get; private set; }

        /// <summary>
        /// Indicates whether all data files were loaded successfully
        /// </summary>
        public static bool DataLoaded { get; private set; }

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("Narcopelago v" + Info.Version + " loaded!");
            
            // Load JSON data files
            LoadDataFiles();
            
            // Subscribe to connect button - connection happens when user clicks Connect
            NarcopelagoUI.OnConnectClicked += ConnectionHandler.HandleConnect;
        }

        private void LoadDataFiles()
        {
            try
            {
                // Get the path to the mod's Data folder (next to the DLL)
                string modDirectory = Path.GetDirectoryName(typeof(Core).Assembly.Location);
                DataPath = Path.Combine(modDirectory, "Data");

                // If Data folder doesn't exist next to DLL, try the Mods folder
                if (!Directory.Exists(DataPath))
                {
                    DataPath = Path.Combine(MelonLoader.Utils.MelonEnvironment.ModsDirectory, "Narcopelago", "Data");
                }

                // If still not found, try directly in Mods folder
                if (!Directory.Exists(DataPath))
                {
                    DataPath = Path.Combine(MelonLoader.Utils.MelonEnvironment.ModsDirectory, "Data");
                }

                LoggerInstance.Msg($"Loading data from: {DataPath}");

                // Load locations.json
                string locationsPath = Path.Combine(DataPath, "locations.json");
                if (File.Exists(locationsPath))
                {
                    string locationsJson = File.ReadAllText(locationsPath);
                    Data_Locations.LoadFromJson(locationsJson);
                    LoggerInstance.Msg($"Loaded {Data_Locations.Locations?.Count ?? 0} locations");
                }
                else
                {
                    LoggerInstance.Warning($"locations.json not found at: {locationsPath}");
                }

                // Load events.json
                string eventsPath = Path.Combine(DataPath, "events.json");
                if (File.Exists(eventsPath))
                {
                    string eventsJson = File.ReadAllText(eventsPath);
                    Data_Events.LoadFromJson(eventsJson);
                    LoggerInstance.Msg($"Loaded {Data_Events.Events?.Count ?? 0} events");
                }
                else
                {
                    LoggerInstance.Warning($"events.json not found at: {eventsPath}");
                }

                // Load items.json
                string itemsPath = Path.Combine(DataPath, "items.json");
                if (File.Exists(itemsPath))
                {
                    string itemsJson = File.ReadAllText(itemsPath);
                    Data_Items.LoadFromJson(itemsJson);
                    LoggerInstance.Msg($"Loaded {Data_Items.Items?.Count ?? 0} items");
                }
                else
                {
                    LoggerInstance.Warning($"items.json not found at: {itemsPath}");
                }

                DataLoaded = Data_Locations.Locations != null && 
                             Data_Events.Events != null && 
                             Data_Items.Items != null;

                if (DataLoaded)
                {
                    LoggerInstance.Msg("All data files loaded successfully!");
                }
                else
                {
                    LoggerInstance.Warning("Some data files failed to load");
                }
            }
            catch (Exception ex)
            {
                LoggerInstance.Error($"Failed to load data files: {ex.Message}");
                DataLoaded = false;
            }
        }
    }
}