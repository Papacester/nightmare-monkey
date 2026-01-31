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
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(Narcopelago.Core), "Narcopelago", "0.1.0", "Papacestor, MacH8s", null)]
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

        /// <summary>
        /// The name of the main game scene (not menu)
        /// </summary>
        private const string GAME_SCENE_NAME = "Main";

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("Narcopelago v" + Info.Version + " loaded!");
            
            // Load JSON data files
            LoadDataFiles();
            
            // Subscribe to connect button - connection happens when user clicks Connect
                NarcopelagoUI.OnConnectClicked += ConnectionHandler.HandleConnect;
            }

            public override void OnUpdate()
            {
                // Process any queued customer unlocks on the main thread
                // This is necessary because Archipelago callbacks run on background threads,
                // but Unity/IL2CPP operations must run on the main thread
                NarcopelagoCustomers.ProcessMainThreadQueue();
                
                // Process any queued dealer recruitments on the main thread
                NarcopelagoDealers.ProcessMainThreadQueue();
                
                // Process any queued supplier unlocks on the main thread
                NarcopelagoSuppliers.ProcessMainThreadQueue();
                
                // Process any queued cartel influence changes on the main thread
                NarcopelagoCartelInfluence.ProcessMainThreadQueue();
                
                // Process any queued level up checks on the main thread
                NarcopelagoLevels.ProcessMainThreadQueue();
                
                // Process any queued DeathLink deaths on the main thread
                NarcopelagoDeathLink.ProcessMainThreadQueue();
                
                // Process goal checking on the main thread
                NarcopelagoGoal.ProcessMainThreadQueue();
                
                // Process any queued Archipelago phone messages on the main thread
                NarcopelagoAPContacts.ProcessMainThreadQueue();
            }

            public override void OnSceneWasLoaded(int buildIndex, string sceneName)
            {
                LoggerInstance.Msg($"Scene loaded: {sceneName} (index: {buildIndex})");
            
                // Track if we're in a game scene
                bool isGameScene = sceneName != "Menu" && sceneName != "Bootstrap" && sceneName != "Loading";
                NarcopelagoCustomers.SetInGameScene(isGameScene);
                NarcopelagoDealers.SetInGameScene(isGameScene);
                NarcopelagoSuppliers.SetInGameScene(isGameScene);
                NarcopelagoCartelInfluence.SetInGameScene(isGameScene);
                NarcopelagoLevels.SetInGameScene(isGameScene);
                NarcopelagoGoal.SetInGameScene(isGameScene);
                NarcopelagoAPContacts.SetInGameScene(isGameScene);
            
                // When entering a game scene, sync customer/dealer/supplier unlocks from Archipelago
                if (isGameScene && NarcopelagoItems.IsInitialized)
                {
                    LoggerInstance.Msg("Game scene detected - syncing customer/dealer/supplier/cartel/levels from Archipelago");
                    NarcopelagoCustomers.SyncFromSession();
                    NarcopelagoDealers.SyncFromSession();
                    NarcopelagoSuppliers.SyncFromSession();
                    NarcopelagoCartelInfluence.SyncFromSession();
                    NarcopelagoLevels.SyncFromSession();
                    NarcopelagoGoal.SyncFromSession();
                }
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