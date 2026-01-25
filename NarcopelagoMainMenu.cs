using HarmonyLib;
using Il2CppScheduleOne.UI.MainMenu;
using MelonLoader;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using System;
using System.Threading.Tasks;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;

namespace Narcopelago
{
    public static class ConnectionHandler
    {
        // Store last successful connection info
        public static string LastHost { get; private set; }
        public static int LastPort { get; private set; }
        public static string LastSlotName { get; private set; }
        public static string LastPassword { get; private set; }
        public static ArchipelagoSession CurrentSession { get; private set; }
        
        // Function to handle connection logic
        public static async void HandleConnect(string host, int port, string slotName, string password)
        {
            MelonLogger.Msg("Connecting to " + host + ":" + port + " as " + slotName);
            
            // Validate inputs first
            if (string.IsNullOrWhiteSpace(host))
            {
                NarcopelagoUI.SetConnectionStatus(false, "Error: Host cannot be empty");
                return;
            }
            
            if (string.IsNullOrWhiteSpace(slotName))
            {
                NarcopelagoUI.SetConnectionStatus(false, "Error: Slot name cannot be empty");
                return;
            }

            if (port <= 0 || port > 65535)
            {
                NarcopelagoUI.SetConnectionStatus(false, "Error: Invalid port number");
                return;
            }
            
            try
            {
                var session = ArchipelagoSessionFactory.CreateSession(host, port);
                // Try to connect with a timeout
                var connectTask = session.ConnectAsync();
                var timeoutTask = Task.Delay(10000); // 10 second timeout
                
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    MelonLogger.Error("Connection timed out");
                    NarcopelagoUI.SetConnectionStatus(false, "Error: Connection timed out");
                    return;
                }

                // Check if connection succeeded
                if (!session.Socket.Connected)
                {
                    MelonLogger.Error("Failed to connect to server");
                    NarcopelagoUI.SetConnectionStatus(false, "Error: Could not reach server");
                    return;
                }
                
                LoginResult result = await session.LoginAsync("Schedule I", slotName, ItemsHandlingFlags.AllItems, password: password);
                
                if (result.Successful)
                {
                    MelonLogger.Msg("Connected to Archipelago!");
                    NarcopelagoUI.SetConnectionStatus(true, "Connected successfully!");

                    // Store the successful connection details
                    LastHost = host;
                    LastPort = port;
                    LastSlotName = slotName;
                    LastPassword = password;
                    CurrentSession = session;

                    // Load options from the server's slot data
                    var loginSuccess = (LoginSuccessful)result;
                    if (loginSuccess.SlotData != null)
                    {
                        NarcopelagoOptions.LoadFromSlotData(loginSuccess.SlotData);
                    }
                    else
                    {
                        MelonLogger.Warning("No SlotData received from server");
                    }

                    // Enable DeathLink if the option is enabled
                    if (NarcopelagoOptions.Deathlink)
                    {
                        MelonLogger.Msg("DeathLink is enabled, activating...");
                        NarcopelagoDeathLink.Enable(session);
                    }
                    else
                    {
                        MelonLogger.Msg("DeathLink is disabled");
                    }

                    // Initialize item receiving - this will process starting items
                    NarcopelagoItems.Initialize(session);
                }
                else
                {
                    LoginFailure failure = (LoginFailure)result;
                    string errorMsg = string.Join(", ", failure.Errors);
                    MelonLogger.Error("Failed to connect: " + errorMsg);
                    NarcopelagoUI.SetConnectionStatus(false, "Failed: " + errorMsg);
                }
            }
            catch (AggregateException ae)
            {
                string msg = ae.InnerException?.Message ?? ae.Message;
                MelonLogger.Error("Connection error: " + msg);
                NarcopelagoUI.SetConnectionStatus(false, "Error: " + msg);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("Connection error: " + ex.Message);
                NarcopelagoUI.SetConnectionStatus(false, "Error: " + ex.Message);
            }
        }
    }

    public static class Schedule1PanelManager
    {
        public static GameObject PanelInstance;
        public static Text StatusText;
        
        // Store input field references
        public static InputField HostField;
        public static InputField PortField;
        public static InputField SlotNameField;
        public static InputField PasswordField;
        
        // Easy access to current values
        public static string Host => HostField?.text ?? "";
        public static int Port => int.TryParse(PortField?.text, out int p) ? p : 38281;
        public static string SlotName => SlotNameField?.text ?? "";
        public static string Password => PasswordField?.text ?? "";
    }

    [HarmonyPatch(typeof(Il2CppScheduleOne.UI.MainMenu.MainMenuScreen), "Awake")]
    public class MainMenuScreen_Awake_Patch
    {
        static void Postfix(Il2CppScheduleOne.UI.MainMenu.MainMenuScreen __instance)
        {
            MelonLogger.Msg("Scene during MainMenuScreen.Awake: " + SceneManager.GetActiveScene().name);

            // Only create panel in the real main menu
            if (SceneManager.GetActiveScene().name != "Menu")
                return;

            if (Schedule1PanelManager.PanelInstance != null)
                return;

            var canvas = GameObject.FindObjectOfType<Canvas>();
            GameObject panel = NarcopelagoUI.CreatePanel(canvas.transform);
            

            Schedule1PanelManager.PanelInstance = panel;
            panel.SetActive(true);
        }
    }

    
}

