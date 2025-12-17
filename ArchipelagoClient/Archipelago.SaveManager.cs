using MelonLoader;
using ScheduleOne.Economy;
using ScheduleOne.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Archipelago
{

    [HarmonyPatch(typeof(SaveManager), "Save", new[] { typeof(string) })]
    public class ModSavePatch
    {
        static void Prefix(string saveFolderPath)
        {
            var state = new SavedModState
            {
                PurchasedCodes = PropertyPurchaseTracker.Purchased
                    .Where(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToList(),

                UnlockedCustomers = CustomerUnlockTracker.Unlocked
                    .Where(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToList(),

                SamplesGiven = SampleTracker.Given
                    .Where(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToList()


            };
            MelonLogger.Msg($"[ModSavePatch] Saving {state.SamplesGiven.Count} samples.");
            string json = JsonUtility.ToJson(state);
            string key = "Mod_PropertyState_" + saveFolderPath;
            PlayerPrefs.SetString(key, json);
            PlayerPrefs.Save();

            MelonLogger.Msg($"[ModSavePatch] Saved mod state for: {saveFolderPath}");
            MelonLogger.Msg($"[ModSavePatch] Purchased codes being saved: {string.Join(", ", state.PurchasedCodes)}");

        }
    }


    [HarmonyPatch(typeof(LoadManager), "TryLoadSaveInfo")]
    public class ModEarlyLoadPatch
    {
        static void Postfix(string saveFolderPath, int saveSlotIndex, ref SaveInfo saveInfo)
        {
            string key = "Mod_PropertyState_" + saveFolderPath;

            // Log current sample count (already statically initialized)
            MelonLogger.Msg($"[ModEarlyLoadPatch] Sample tracker contains {SampleTracker.Given.Count} recipients.");

            if (PlayerPrefs.HasKey(key))
            {


                string json = PlayerPrefs.GetString(key);
                MelonLogger.Msg($"[ModApplyPatch] Raw JSON: {json}");
                var state = JsonUtility.FromJson<SavedModState>(json);

                // Restore property state early
                PropertyPurchaseTracker.Purchased.Clear();
                foreach (var code in state.PurchasedCodes)
                    PropertyPurchaseTracker.SetPurchased(code, true);

                foreach (var name in state.UnlockedCustomers)
                    CustomerUnlockTracker.SetUnlocked(name, true);

                // DO NOT restore samples here — let ModApplyPatch handle it

                MelonLogger.Msg($"[PropertyPurchaseTracker] Tracked items: {string.Join(", ", PropertyPurchaseTracker.GetTrackedItems())}");
                MelonLogger.Msg($"[ModEarlyLoadPatch] Restored purchase state early for: {saveFolderPath}");
                MelonLogger.Msg($"[ModEarlyLoadPatch] Restored {state.UnlockedCustomers.Count} unlocked customers.");
            }

            // Refresh PoI markers
            var allCustomers = GameObject.FindObjectsOfType<Customer>();
            foreach (var customer in allCustomers)
            {
                Patch_UpdatePotentialCustomerPoI.RefreshPoIMarker(customer);
            }

            MelonLogger.Msg("[ModEarlyLoadPatch] Refreshed PoI markers after loading saved sample state.");
        }
    }




    [Serializable]
    public class SavedModState
    {
        public int Version = 1;
        public List<string> PurchasedCodes = new List<string>();
        public List<string> UnlockedCustomers = new List<string>();
        public List<string> SamplesGiven = new List<string>();

    }
}
