using FishNet.Connection;
using HarmonyLib;
using MelonLoader;
using ScheduleOne.Economy;
using ScheduleOne.NPCs.Relation;
using System.Collections.Generic;
using System.Linq;


namespace Archipelago
{
    public static class RecruitmentTracker
    {
        // Dealers unlocked via Archipelago sync
        private static readonly HashSet<string> recruitedAP = new HashSet<string>();

        // Dealers recruited via normal in‑game means (relationship, quests, etc.)
        private static readonly HashSet<string> recruitedVanilla = new HashSet<string>();

        public static readonly string[] Dealers = new[]
        {
        "Benji",
        "Molly",
        "Brad",
        "Jane",
        "Wei",
        "Leo"
    };

        // --- Archipelago methods ---
        public static bool HasRecruitedAP(string dealerName)
        {
            return recruitedAP.Contains(dealerName);
        }

        public static void MarkRecruitedAP(string dealerName)
        {
            if (Dealers.Contains(dealerName))
            {
                recruitedAP.Add(dealerName);
                MelonLogger.Msg($"[RecruitmentTracker] {dealerName} marked as recruited via Archipelago.");
            }
            else
            {
                MelonLogger.Warning($"[RecruitmentTracker] Unknown dealer (AP): {dealerName}");
            }
        }

        // --- Vanilla methods ---
        public static bool HasRecruitedVanilla(string dealerName)
        {
            return recruitedVanilla.Contains(dealerName);
        }

        public static void MarkRecruitedVanilla(string dealerName)
        {
            if (Dealers.Contains(dealerName))
            {
                recruitedVanilla.Add(dealerName);
                MelonLogger.Msg($"[RecruitmentTracker] {dealerName} marked as recruited via vanilla.");
            }
            else
            {
                MelonLogger.Warning($"[RecruitmentTracker] Unknown dealer (vanilla): {dealerName}");
            }
        }

        // --- Reset & getters ---
        public static void Reset()
        {
            recruitedAP.Clear();
            recruitedVanilla.Clear();
            MelonLogger.Msg("[RecruitmentTracker] Recruitment state reset.");
        }

        public static IEnumerable<string> GetRecruitedAP()
        {
            return recruitedAP.ToList();
        }

        public static IEnumerable<string> GetRecruitedVanilla()
        {
            return recruitedVanilla.ToList();
        }
    }



    [HarmonyPatch(typeof(Dealer), "CanOfferRecruitment")]
    public static class Patch_CanOfferRecruitment
    {
        static bool Prefix(Dealer __instance, ref bool __result, ref string reason)
        {
            string dealerName = __instance.name;

            MelonLogger.Msg($"[RecruitmentTracker] Recruitment check for: {dealerName}");

            // Case 1: Dealer recruited via vanilla -> block further recruitment offers
            if (RecruitmentTracker.HasRecruitedVanilla(dealerName))
            {
                reason = $"{dealerName} has already been recruited via vanilla and cannot be offered again.";
                __result = false;
                return false; // skip original method
            }

            // Case 2: Dealer recruited via Archipelago -> still allow recruitment offers
            if (RecruitmentTracker.HasRecruitedAP(dealerName))
            {
                __result = true;
                return false; // skip original method, force allow
            }

            // Case 3: Dealer not recruited yet -> always allow recruitment
            reason = string.Empty;
            __result = true;
            return false; // skip original method, force allow
        }
    }

    public static class DealerRecruitGuard
    {
        public static bool ShouldAllow(string name)
        {
            // 1. AP unlocks always allowed
            if (RecruitmentTracker.HasRecruitedAP(name))
                return true;

            // 2. CustomerUnlockTracker unlocks allowed
            if (CustomerUnlockTracker.IsUnlocked(name))
                return true;

            // Otherwise blocked
            return false;
        }
    }

    [HarmonyPatch(typeof(Dealer), "SetIsRecruited")]
    public static class Patch_Dealer_SetIsRecruited
    {
        static bool Prefix(Dealer __instance, NetworkConnection conn)
        {
            string name = __instance.name;

            if (DealerRecruitGuard.ShouldAllow(name))
            {
                RecruitmentTracker.MarkRecruitedAP(name);
                MelonLogger.Msg($"[RecruitmentTracker] Allowing recruitment for {name}.");
                return true;
            }

            MelonLogger.Msg($"[RecruitmentTracker] BLOCKED SetIsRecruited for {name}.");
            return false;
        }
    }

    [HarmonyPatch(typeof(Dealer), "RpcLogic___SetIsRecruited_328543758")]
    public static class Patch_Dealer_RpcLogic_SetIsRecruited
    {
        static bool Prefix(Dealer __instance, NetworkConnection conn)
        {
            string name = __instance.name;

            if (DealerRecruitGuard.ShouldAllow(name))
            {
                RecruitmentTracker.MarkRecruitedAP(name);
                MelonLogger.Msg($"[RecruitmentTracker] Allowing RpcLogic recruitment for {name}.");
                return true;
            }

            MelonLogger.Msg($"[RecruitmentTracker] BLOCKED RpcLogic recruitment for {name}.");
            return false;
        }
    }



    /*
    [HarmonyPatch(typeof(NPCRelationData), "Unlock")]
    public static class Patch_Unlock
    {
        static bool Prefix(NPCRelationData __instance, NPCRelationData.EUnlockType type, bool notify)
        {
            string dealerName = __instance.NPC?.name;

            if (string.IsNullOrEmpty(dealerName))
            {
                MelonLogger.Warning("[RecruitmentTracker] Unlock called but dealerName was null/empty.");
                return true; // let original run
            }

            // Case 1: Archipelago unlock
            if (RecruitmentTracker.HasRecruitedAP(dealerName))
            {
                // Already marked via AP, allow unlock to proceed
                MelonLogger.Msg($"[RecruitmentTracker] {dealerName} unlocked via Archipelago.");
                return true; // run original Unlock
            }

            // Case 2: Vanilla unlock
            if (!RecruitmentTracker.HasRecruitedVanilla(dealerName))
            {
                RecruitmentTracker.MarkRecruitedVanilla(dealerName);
                MelonLogger.Msg($"[RecruitmentTracker] {dealerName} recruited via vanilla.");
            }

            // Block vanilla unlock from actually unlocking the dealer
            if (RecruitmentTracker.Dealers.Contains(dealerName))
            {
                MelonLogger.Msg($"[RecruitmentTracker] Blocking vanilla unlock for {dealerName}.");
                return false; // skip original Unlock
            }

            return true; // let original run for non-dealer NPCs

        }
    }
    */

}
