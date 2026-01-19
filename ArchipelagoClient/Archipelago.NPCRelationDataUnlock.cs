using MelonLoader;
using ScheduleOne.NPCs.Relation;
using HarmonyLib;
using System.Linq;


namespace Archipelago
{
    [HarmonyPatch(typeof(NPCRelationData), "Unlock")]
    public static class Patch_NPCRelationData_Unlock
    {
        static bool Prefix(NPCRelationData __instance, NPCRelationData.EUnlockType type, bool notify)
        {
            var npc = __instance.NPC;
            if (npc == null)
            {
                MelonLogger.Warning("[Unlock Patch] NPC is null — allowing original to avoid crash.");
                return true;
            }

            string name = npc.FirstName ?? npc.name ?? "Unknown";

            bool isDealer = RecruitmentTracker.Dealers.Contains(name);

            // 1. AP unlocks always allowed
            if (RecruitmentTracker.HasRecruitedAP(name))
            {
                MelonLogger.Msg($"[RecruitmentTracker] {name} unlocked via Archipelago.");
                return true;
            }

            // 2. CustomerUnlockTracker unlocks allowed
            if (CustomerUnlockTracker.IsUnlocked(name))
            {
                MelonLogger.Msg($"[Unlock Allowed] Proceeding with unlock for: {name}");
                return true;
            }

            // 3. Track vanilla unlock attempts
            if (!RecruitmentTracker.HasRecruitedVanilla(name))
            {
                RecruitmentTracker.MarkRecruitedVanilla(name);
                MelonLogger.Msg($"[RecruitmentTracker] Vanilla attempted unlock for {name}.");
            }

            // 4. Block ALL vanilla unlocks (dealers AND non-dealers)
            MelonLogger.Msg($"[Unlock Blocked] {name} is not approved by AP or mod — blocking unlock.");
            return false;
        }
    }
}
