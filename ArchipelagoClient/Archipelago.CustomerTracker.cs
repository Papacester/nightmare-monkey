using HarmonyLib;
using MelonLoader;
using ScheduleOne.Economy;
using ScheduleOne.NPCs;
using ScheduleOne.NPCs.Relation;
using System;
using System.Collections.Generic;

namespace Archipelago
{
    public static class CustomerUnlockTracker
    {
        // Current runtime state
        public static Dictionary<string, bool> Unlocked = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // Baseline defaults (who exists + their default unlocked state)
        private static readonly Dictionary<string, bool> DefaultUnlocked = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
    {
        // Northtown
        { "Kyle", true},
        { "Austin", true},
        { "Kathy", true},
        { "Mick", true},
        { "Sam", true},
        { "Jessi", true},
        { "Peter", false},
        { "Chloe", false},
        { "Ludwig", false},
        { "Mrs.", false},
        { "Geraldine", false},
        { "Beth", false},
        { "Peggy", false},
        { "Donna", false},

        // Westville
        { "Trent", false},
        { "Meg", false},
        { "Joyce", false},
        { "Keith", false},
        { "Doris", false},
        { "Jerry", false},
        { "Kim", false},
        { "Charles", false},
        { "George", false},
        { "Dean", false},

        // Downtown
        { "Jennifer", false},
        { "Elizabeth", false},
        { "Eugene", false},
        { "Greg", false},
        { "Jeff", false},
        { "Kevin", false},
        { "Louis", false},
        { "Lucy", false},
        { "Philip", false},
        { "Randy", false},

        // Docks
        { "Anna", false},
        { "Billy", false},
        { "Cranky Frank", false},
        { "Genghis", false},
        { "Javier", false},
        { "Lisa", false},
        { "Mac", false},
        { "Marco", false},
        { "Melissa", false},

        // Suburbia
        { "Chris", false},
        { "Allison", false},
        { "Carl", false},
        { "Dennis", false},
        { "Hank", false},
        { "Harold", false},
        { "Jack", false},
        { "Jackie", false},
        { "Jeremy", false},
        { "Karen", false},

        // Uptown
        { "Lily", false},
        { "Fiona", false},
        { "Herbert", false},
        { "Jen", false},
        { "Michael", false},
        { "Pearl", false},
        { "Ray", false},
        { "Tobas", false},
        { "Walter", false},

        //Dealers
        {"Benji", false},
        {"Molly", false},
        {"Brad", false},
        {"Jane", false},
        {"Wei", false},
        {"Leo", false}
    };

        // Static constructor ensures defaults are loaded at startup
        static CustomerUnlockTracker()
        {
            ResetAll();
        }

        public static bool IsUnlocked(string name)
        {
            return Unlocked.TryGetValue(name, out bool value) && value;
        }

        public static void SetUnlocked(string name, bool value)
        {
            if (!Unlocked.ContainsKey(name))
            {
                MelonLogger.Warning($"[CustomerUnlockTracker] Unknown customer: {name}");
                return;
            }

            Unlocked[name] = value;
            MelonLogger.Msg($"[CustomerUnlockTracker] SetUnlocked: {name} = {value}");
        }

        public static void ResetAll()
        {
            Unlocked.Clear();
            foreach (var kvp in DefaultUnlocked)
            {
                Unlocked[kvp.Key] = kvp.Value;
            }

            MelonLogger.Msg("[CustomerUnlockTracker] All unlocks reset to defaults.");
        }
    }
    /*
    [HarmonyPatch(typeof(NPCRelationData), "Unlock")]
    public class Patch_NPCRelationData_Unlock
    {
        static bool Prefix(NPCRelationData __instance, NPCRelationData.EUnlockType type, bool notify)
        {
            var npc = __instance.NPC;
            if (npc == null)
            {
                MelonLogger.Warning("[Unlock Patch] NPC is null — skipping patch to avoid save crash.");
                return true; // Let original method run
            }

            string name = npc.FirstName ?? npc.name ?? "Unknown";
            Customer customer = npc.GetComponent<Customer>();
            */
            /*
            SampleTracker.SetGiven(name, true);
            if (customer != null)
            {
                SampleTracker.SetGiven(name, true, customer);
            }
            */
            /*
            if (CustomerUnlockTracker.IsUnlocked(name))
            {
                MelonLogger.Msg($"[Unlock Allowed] Proceeding with unlock for: {name}");
                return true;
            }

            MelonLogger.Msg($"[Unlock Blocked] {name} is not approved by mod — skipping unlock.");
            return false;
        }
    }
    */
    public class UnlockingCustomers
    {
        public static void TryUnlockCustomer(string name, string name2, NPCRelationData.EUnlockType type)
        {
            CustomerUnlockTracker.SetUnlocked(name, true);

            var npc = NPCManager.GetNPC(name2);
            var relation = npc?.RelationData;

            if (relation != null && !relation.Unlocked)
                relation.Unlock(type);
        }

    }
    //Unlocking Example
    //UnlockingCustomers.TryUnlockCustomer("Meg", "meg_cooley", NPCRelationData.EUnlockType.DirectApproach);

    [HarmonyPatch(typeof(Customer), "OnCustomerUnlocked")]
    public class OnCustomerUnlockedPatch
    {
        static void Postfix(Customer __instance)
        {
            var field = AccessTools.Field(typeof(Customer), "sampleOfferedToday");
            if (field != null)
            {
                field.SetValue(__instance, false);
                MelonLogger.Msg($"[OnCustomerUnlockedPatch] Reset sampleOfferedToday for {__instance.name} after unlock");
            }
        }
    }
}
