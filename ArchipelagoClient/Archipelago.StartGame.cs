using MelonLoader;
using ScheduleOne.DevUtilities;
using ScheduleOne.Persistence;
using ScheduleOne.UI.MainMenu;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using HarmonyLib;

namespace Archipelago
{
    [HarmonyPatch(typeof(SetupScreen), "StartGame")] // Replace with actual class name
    public class ModNewGameResetPatch
    {
        static void Prefix(object __instance)
        {
            var slotIndexField = AccessTools.Field(__instance.GetType(), "slotIndex");
            var defaultUnlockedCustomers = new List<string>
            {
                "Kyle",
                "Austin",
                "Kathy",
                "Mick",
                "Sam",
                "Jessi"
            };
            int slotIndex = (int)slotIndexField.GetValue(__instance);

            string basePath = Singleton<SaveManager>.Instance.IndividualSavesContainerPath;
            string newSaveFolder = Path.Combine(basePath, $"SaveGame_{slotIndex + 1}");
            string key = "Mod_PropertyState_" + newSaveFolder;

            PropertyPurchaseTracker.Purchased.Clear();
            PlayerPrefs.DeleteKey(key);

            CustomerUnlockTracker.ResetAll();
            SampleTracker.ResetAll();


            foreach (var name in defaultUnlockedCustomers)
            {
                CustomerUnlockTracker.SetUnlocked(name, true);
            }

            MelonLogger.Msg($"[ModNewGameResetPatch] New game started in slot {slotIndex + 1}. Mod flags reset.");
        }
    }

    [HarmonyPatch(typeof(GameManager), "Start")]
    public class ModApplyPatch
    {
        static void Prefix()
        {
            string saveFolderPath = Singleton<LoadManager>.Instance.LoadedGameFolderPath;
            string key = "Mod_PropertyState_" + saveFolderPath;

            // Restore default property state
            PropertyPurchaseTracker.RegisterDefaults();
            MelonLogger.Msg($"[ModApplyPatch] Registered {PropertyPurchaseTracker.GetTrackedItems().Count()} mod-managed properties.");

            // Reset sample tracker to static defaults (true for Kyle, etc.)
            SampleTracker.ResetAll();
            MelonLogger.Msg($"[ModApplyPatch] Reset sample tracker to default state with {SampleTracker.Given.Count} recipients.");

            CustomerUnlockTracker.ResetAll();

            if (PlayerPrefs.HasKey(key))
            {
                var json = PlayerPrefs.GetString(key);
                var state = JsonUtility.FromJson<SavedModState>(json);

                // Restore property state
                SweatshopAndMotelRoomCanBuy.alreadypurchasedMotelRoom = PropertyPurchaseTracker.IsPurchased("MotelRoom");
                SweatshopAndMotelRoomCanBuy.alreadypurchasedSweatShop = PropertyPurchaseTracker.IsPurchased("Sweatshop");

                // Apply saved sample state (overwrites static defaults)
                foreach (var name in state.SamplesGiven)
                    SampleTracker.SetGiven(name, true);

                MelonLogger.Msg($"[ModApplyPatch] Restored {state.SamplesGiven.Count} samples from save.");

                // Restore customer unlocks
                foreach (var name in state.UnlockedCustomers)
                    CustomerUnlockTracker.SetUnlocked(name, true);

                MelonLogger.Msg($"[ModApplyPatch] Restored {state.UnlockedCustomers.Count} unlocked customers.");
            }
            else
            {
                MelonLogger.Msg("[ModApplyPatch] No saved mod state found — using default sample and unlock values.");
            }
        }
    }
}
