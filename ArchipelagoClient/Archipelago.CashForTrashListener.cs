using HarmonyLib;
using MelonLoader;
using ScheduleOne.ObjectScripts;
using ScheduleOne.Trash;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Archipelago
{

    public static class RecyclerGuard
    {
        private static readonly HashSet<Recycler> Active = new HashSet<Recycler>();

        public static bool ShouldCount(Recycler recycler)
        {
            if (Active.Contains(recycler))
                return false;

            Mark(recycler);
            return true;
        }

        private static async void Mark(Recycler recycler)
        {
            Active.Add(recycler);

            // Clear after a short delay (same idea as Estate Agent)
            await Task.Delay(200);
            Active.Remove(recycler);
        }
    }


    [HarmonyPatch(typeof(Recycler), "ButtonInteracted")]
    public static class Recycler_ButtonInteracted_CountPatch
    {
        public static int TotalTrashInserted = 0;

        static void Prefix(Recycler __instance)
        {
            try
            {
                // Estate-Agent-style guard
                if (!RecyclerGuard.ShouldCount(__instance))
                    return;

                // Access private GetTrash()
                MethodInfo getTrashMethod = AccessTools.Method(typeof(Recycler), "GetTrash");
                TrashItem[] trash = (TrashItem[])getTrashMethod.Invoke(__instance, null);

                int count = trash?.Length ?? 0;
                TotalTrashInserted += count;

                MelonLogger.Msg($"[TrashCounter] Recycler processed {count} items. Total = {TotalTrashInserted}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[TrashCounter] Error counting trash: {ex}");
            }
        }
    }

}
