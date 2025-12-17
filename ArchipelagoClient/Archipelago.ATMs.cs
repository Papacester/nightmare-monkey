using MelonLoader;
using ScheduleOne.Combat;
using ScheduleOne.Money;
using System.Collections.Generic;
using HarmonyLib;

namespace Archipelago
{
    [HarmonyPatch(typeof(ATM), "Impacted")]
    public class ATM_impacted_Patch
    {
        public static HashSet<int> loggedATMs = new HashSet<int>();
        static void Prefix(ATM __instance, Impact impact)
        {
            if (__instance.IsBroken)
            {
                return;
            }
            if (impact.ImpactForce >= 165f || impact.ImpactType == EImpactType.Bullet)
            {
                int id = __instance.GetInstanceID();
                if (!loggedATMs.Contains(id))
                {
                    loggedATMs.Add(id);
                    MelonLogger.Msg($"ATM impacted with force {impact.ImpactForce} of type {impact.ImpactType}");
                }
            }
        }

    }
}
