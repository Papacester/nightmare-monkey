using HarmonyLib;
using MelonLoader;
using ScheduleOne.Dialogue;
using ScheduleOne.Economy;
using ScheduleOne.NPCs;
using ScheduleOne.NPCs.Relation;
using ScheduleOne.Persistence;
using ScheduleOne.PlayerScripts;
using System.Collections.Generic;
using System.Threading.Tasks;


[assembly: MelonInfo(typeof(Archipelago.Archipelago), "ScheduleOneArchipelago", "0.1.0", "Papacester")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace Archipelago
{
    public class Archipelago : MelonMod
    {
        public override void OnApplicationStart()
        {
            LoggerInstance.Msg($"ScheduleOneArchipelago v{Info.Version} loaded!");
            LoggerInstance.Msg("By Papacester");

            HarmonyInstance.PatchAll();

            var harmony = new HarmonyLib.Harmony("com.papacester.scheduleonearchipelago");
            
            var original = AccessTools.Method(typeof(LoadManager), "TryLoadSaveInfo", new[]
            {
                typeof(string),
                typeof(int),
                typeof(SaveInfo).MakeByRefType(),
                typeof(bool)

            });

            var postfix = AccessTools.Method(typeof(ModEarlyLoadPatch), "Postfix");

            harmony.Patch(original, postfix: new HarmonyMethod(postfix));

        }
    }

    [HarmonyPatch(typeof(Customer), "SampleOptionValid")]
    public class SampleOptionValidPatch
    {
        // Prefix runs before the original method
        static bool Prefix(Customer __instance, out string invalidReason, ref bool __result)
        {
            string key = CustomerKey.Get(__instance);
            bool regionUnlocked = RegionUtils.IsCustomerRegionUnlocked(__instance);
            bool relationUnlocked = __instance.NPC?.RelationData?.Unlocked ?? false;
            bool hasSample = SampleTracker.HasGiven(key);

            MelonLogger.Msg(
                $"[SampleOptionValid.Prefix] {key} region={regionUnlocked} relation={relationUnlocked} hasSample={hasSample}"
            );

            // If already sampled, block immediately
            if (hasSample)
            {
                invalidReason = "Sample already succeeded";
                __result = false;
                MelonLogger.Msg($"[SampleOptionValid.Prefix] {key} already sampled — blocking and skipping original.");
                return false; // skip original
            }

            // Otherwise let original run
            invalidReason = string.Empty;
            return true;
        }
    }

    [HarmonyPatch(typeof(Customer), "SetUpDialogue")]
    public class Patch_SetUpDialogue
    {
        // Simple debug toggle
        private static bool DebugEnabled = true;

        static void Postfix(Customer __instance)
        {
            var sampleChoiceField = AccessTools.Field(typeof(Customer), "sampleChoice");
            var sampleChoice = (DialogueController.DialogueChoice)sampleChoiceField.GetValue(__instance);

            // Keep references to the original delegates so we can fall back to vanilla behavior
            var origShow = sampleChoice.shouldShowCheck;
            var origValid = sampleChoice.isValidCheck;

            // Track last results to avoid log spam
            bool lastShowResult = false;
            bool lastValidResult = false;

            // Wrap shouldShowCheck: signature is (bool) -> bool
            sampleChoice.shouldShowCheck = new DialogueController.DialogueChoice.ShouldShowCheck(
                (bool arg) =>
                {
                    string key = CustomerKey.Get(__instance);
                    bool relationUnlocked = __instance.NPC?.RelationData?.Unlocked ?? false;

                    bool result = relationUnlocked || (origShow != null && (bool)origShow.DynamicInvoke(arg));

                    if (DebugEnabled && result != lastShowResult)
                    {
                        MelonLogger.Msg($"[SampleChoice.ShowCheck] {key} changed: {lastShowResult} -> {result} (relationUnlocked={relationUnlocked})");
                        lastShowResult = result;
                    }

                    return result;
                }
            );

            // Wrap isValidCheck: signature is (out string invalidReason) -> bool
            sampleChoice.isValidCheck = new DialogueController.DialogueChoice.IsChoiceValid(
                (out string invalidReason) =>
                {
                    string key = CustomerKey.Get(__instance);
                    bool relationUnlocked = __instance.NPC?.RelationData?.Unlocked ?? false;
                    bool hasSample = SampleTracker.HasGiven(key);

                    bool result;
                    if (relationUnlocked && !hasSample)
                    {
                        invalidReason = string.Empty;
                        result = true;
                    }
                    else
                    {
                        object[] args = new object[] { null };
                        result = origValid != null && (bool)origValid.DynamicInvoke(args);
                        invalidReason = (string)args[0] ?? string.Empty;
                    }

                    if (DebugEnabled && result != lastValidResult)
                    {
                        MelonLogger.Msg($"[SampleChoice.ValidCheck] {key} changed: {lastValidResult} -> {result} (relationUnlocked={relationUnlocked}, hasSample={hasSample}, reason={invalidReason})");
                        lastValidResult = result;
                    }

                    return result;
                }
            );

            if (DebugEnabled)
                MelonLogger.Msg($"[Patch_SetUpDialogue] Rewired sampleChoice checks for {CustomerKey.Get(__instance)}");
        }
    }

    public static class RegionUtils
    {
        public static bool IsCustomerRegionUnlocked(Customer customer)
        {
            var npcProp = AccessTools.Property(customer.GetType(), "NPC");
            var npc = npcProp?.GetValue(customer);
            if (npc == null) return false;

            var regionField = AccessTools.Field(npc.GetType(), "Region");
            var regionEnum = regionField?.GetValue(npc);
            if (regionEnum == null) return false;

            var mapType = AccessTools.TypeByName("ScheduleOne.Map.Map");
            var mapInstance = AccessTools.Property(mapType, "Instance")?.GetValue(null);
            if (mapInstance == null) return false;

            var getRegionData = AccessTools.Method(mapType, "GetRegionData", new[] { regionEnum.GetType() });
            var regionData = getRegionData?.Invoke(mapInstance, new object[] { regionEnum });
            if (regionData == null) return false;

            var isUnlockedProp = AccessTools.Property(regionData.GetType(), "IsUnlocked");
            return (bool)(isUnlockedProp?.GetValue(regionData) ?? false);
        }
    }






    [HarmonyPatch(typeof(Customer), "IsUnlockable")]
    public class Patch_IsUnlockable
    {
        static bool Prefix(Customer __instance, ref bool __result)
        {
            bool regionUnlocked = RegionUtils.IsCustomerRegionUnlocked(__instance);
            if (!regionUnlocked)
            {
                MelonLogger.Msg($"[IsUnlockable Patch] {CustomerKey.Get(__instance)} — region not unlocked, not unlockable.");
                __result = false;
                return false; // skip original
            }

            // Allow original for other checks; AP can still unlock relations
            return true;
        }
    }


    public static class ModUnlockControl
    {
        public static HashSet<string> AllowedUnlocks = new HashSet<string>();

        public static void AllowUnlock(string customerName)
        {
            AllowedUnlocks.Add(customerName);
            MelonLogger.Msg($"[Unlock Allowed] {customerName} added to allowlist.");
        }

        public static bool IsUnlockAllowed(string customerName)
        {
            return AllowedUnlocks.Contains(customerName);
        }
    }


    /* LOGS ALL COMPLETED DEALS */
    [HarmonyPatch(typeof(Customer), "ContractWellReceived")]
    public class ContractWellReceivedListener
    {
        static bool alreadyLoggedContracts = false;

        static void Postfix(Customer __instance, string npcToRecommend)
        {
            if (!alreadyLoggedContracts)
            {

                    MelonLogger.Msg("Contract Well Received");
                    UnlockingCustomers.TryUnlockCustomer("Peter", "peter_file", NPCRelationData.EUnlockType.DirectApproach);
                    alreadyLoggedContracts = true;
                    resetAlreadyLoggedContracts();
            }
        }

        static async void resetAlreadyLoggedContracts()
        {
            await Task.Delay(1000); // 1-second cooldown
            alreadyLoggedContracts = false;
        }

        /* BLOCKS UNLOCKING OF DEALERS AND SUPPLIERS (EXCEPT FOR ALBERT THROUGH NELSON) */
        static bool Prefix(string npcToRecommend)
        {
            var npc = NPCManager.GetNPC(npcToRecommend);
            if (npc is Dealer || npc is Supplier)
            {
                MelonLogger.Msg($"Prevented unlocking of {npcToRecommend}");
                return false; // Skip original method
            }

            return true; // Allow original method to run
        }
    }

    [HarmonyPatch(typeof(Player), "SleepStart")]
    public class PlayerSleepStartPatch
    {
        static void Prefix()
        {
            MelonLogger.Msg("Player is going to sleep.");
            ATM_impacted_Patch.loggedATMs.Clear();
        }
    }

}
