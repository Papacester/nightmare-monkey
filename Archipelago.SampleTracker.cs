using MelonLoader;
using ScheduleOne.Economy;
using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace Archipelago
{
    public static class SampleTracker
    {
        public static Dictionary<string, bool> Given = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
    {
         //Northtown
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
        //Westville
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
        //Downtown
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
        //Docks
        { "Anna", false},
        { "Billy", false},
        { "Cranky Frank", false},
        { "Genghis", false},
        { "Javier", false},
        { "Lisa", false},
        { "Mac", false},
        { "Marco", false},
        { "Melissa", false},
        //Suburbia
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
        //Uptown
        { "Lily", false},
        { "Fiona", false},
        { "Herbert", false},
        { "Jen", false},
        { "Michael", false},
        { "Pearl", false},
        { "Ray", false},
        { "Tobas", false},
        { "Walter", false}
    };
        private static readonly HashSet<string> DefaultGiven = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Kyle", "Austin", "Kathy", "Mick", "Sam", "Jessi"
        };

        public static bool HasGiven(string name)
        {
            return Given.TryGetValue(name, out bool value) && value;
        }

        public static void SetGiven(string name, bool value, Customer customer = null)
        {

            if (!Given.ContainsKey(name))
            {
                MelonLogger.Warning($"[SampleTracker] Unknown recipient: {name}");
                return;
            }

            Given[name] = value;
            MelonLogger.Msg($"[SampleTracker] SetGiven: {name} = {value}");

            // Only update marker if customer reference is provided
            if (customer != null)
            {
                var method = AccessTools.Method(customer.GetType(), "UpdatePotentialCustomerPoI");
                if (method != null)
                {
                    try
                    {
                        method.Invoke(customer, null);
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"[SampleTracker] Failed to update marker for {customer.name}: {ex.Message}");
                    }
                }
            }

        }


        public static void ResetAll()
        {
            foreach (var key in Given.Keys.ToList())
            {
                Given[key] = DefaultGiven.Contains(key);
            }

            MelonLogger.Msg("[SampleTracker] All sample flags reset (preserved default-true recipients).");
        }


        public static void RegisterDefaults(IEnumerable<string> allCustomerNames)
        {
            foreach (var name in allCustomerNames)
            {
                if (!Given.ContainsKey(name))
                {
                    Given[name] = false;
                    MelonLogger.Msg($"[SampleTracker] Registered: {name}");
                }
            }
        }
    }


    public static class CustomerKey
    {
        public static string Get(Customer c)
        {
            var npc = c?.NPC;
            return npc?.FirstName ?? c?.name ?? "Unknown";
        }
    }

    public static class SampleEligibility
    {
        public static bool IsEligible(Customer customer, out string reason)
        {
            // Check if the NPC is mutually known
            if (!customer.NPC.RelationData.IsMutuallyKnown())
            {
                reason = "Unlock one of " + customer.NPC.FirstName + "'s connections first";
                return false;
            }

            // Check if the region is unlocked
            if (!RegionUtils.IsCustomerRegionUnlocked(customer))
            {
                reason = "region locked";
                return false;
            }

            // Check if a sample has already been given
            if (SampleTracker.HasGiven(customer.name))
            {
                reason = "sample succeeded";
                return false;
            }

            // Eligible if none of the above conditions fail
            reason = null;
            return true;
        }
    }

    [HarmonyPatch(typeof(Customer), "UpdatePotentialCustomerPoI")]
    public class Patch_UpdatePotentialCustomerPoI
    {
        static void Postfix(Customer __instance)
        {
            if (__instance.potentialCustomerPoI == null)
            {
                return;
            }

            if (!SampleEligibility.IsEligible(__instance, out _))
            {
                __instance.potentialCustomerPoI.enabled = false;
            }
            else
            {
                __instance.potentialCustomerPoI.enabled = true;
            }
        }

        public static void RefreshPoIMarker(Customer customer)
        {
            if (customer?.potentialCustomerPoI == null)
            {
                MelonLogger.Msg($"[PotentialCustomerPoIPatch] Customer {customer?.name} PoI is null");
                return;
            }

            MelonLogger.Msg($"[PotentialCustomerPoIPatch] Customer {customer.name} PoI is present");

            if (SampleEligibility.IsEligible(customer, out _))
            {
                customer.potentialCustomerPoI.enabled = true;
            }
            else
            {
                customer.potentialCustomerPoI.enabled = false;
            }
        }
    }

    [HarmonyPatch(typeof(Customer), "SampleWasSufficient")]
    public class SampleWasSufficientPatch
    {
        static void Postfix(Customer __instance)
        {
            SampleTracker.SetGiven(__instance.name, true, __instance);
            MelonLogger.Msg($"[SampleWasSufficientPatch] Sample succeeded for {__instance.name}");
        }
    }
}
