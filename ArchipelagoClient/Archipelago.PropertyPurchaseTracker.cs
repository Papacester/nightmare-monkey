using MelonLoader;
using ScheduleOne.DevUtilities;
using ScheduleOne.Dialogue;
using ScheduleOne.Money;
using ScheduleOne.NPCs;
using ScheduleOne.Property;
using System;
using System.Collections.Generic;
using HarmonyLib;

namespace Archipelago
{
    [HarmonyPatch(typeof(DialogueController_Ming), "CanBuyRoom")]
    public class SweatshopAndMotelRoomCanBuy
    {
        public static bool alreadypurchasedMotelRoom = false;
        public static bool alreadypurchasedSweatShop = false;

        static bool Prefix(DialogueController_Ming __instance, bool enabled, ref bool __result)
        {
            var propertyField = AccessTools.Field(typeof(DialogueController_Ming), "Property");
            var property = propertyField.GetValue(__instance) as Property;

            if (property == null)
            {
                MelonLogger.Warning("[CanBuyRoom] Property field is null.");
                return true; // fallback to original logic
            }

            if (property.name == "MotelRoom" && PropertyPurchaseTracker.IsPurchased("motelroom"))
            {
                __result = false;
                return false; // skip original method
            }

            if (property.name == "Sweatshop" && PropertyPurchaseTracker.IsPurchased("sweatshop"))
            {
                __result = false;
                return false; // skip original method
            }

            //Override: allow purchase regardless of quest state
            __result = true;
            return false; // skip original method
        }
    }



    public static class PropertyPurchaseTracker
    {
        public static Dictionary<string, bool> Purchased { get; private set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> trackedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Called once at startup
        public static void RegisterDefaults()
        {
            Register("motelroom");
            Register("sweatshop");
            Register("storageunit");
            Register("bungalow");
            Register("barn");
            Register("dockswarehouse");
            Register("laundromat");
            Register("carwash");
            Register("postoffice");
            Register("tacoticklers");
        }

        public static void Register(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return;
            code = code.ToLowerInvariant();
            trackedItems.Add(code);
            if (!Purchased.ContainsKey(code))
                Purchased[code] = false;
            MelonLogger.Msg($"[PropertyPurchaseTracker] Registered: {code}");
        }

        public static bool IsPurchased(string code)
        {
            return Purchased.TryGetValue(code.ToLowerInvariant(), out bool value) && value;
        }

        public static void SetPurchased(string code, bool value)
        {
            if (string.IsNullOrWhiteSpace(code)) return;
            code = code.ToLowerInvariant();
            Purchased[code] = value;
            MelonLogger.Msg($"[PropertyPurchaseTracker] SetPurchased: {code} = {value}");
        }

        public static bool Contains(string code)
        {
            return trackedItems.Contains(code.ToLowerInvariant());
        }

        public static IEnumerable<string> GetTrackedItems() => trackedItems;
    }

    //prevents owning the sweatshop and motel room through normal means

    [HarmonyPatch(typeof(DialogueController_Ming), "ChoiceCallback")]
    public class SweatshopAndMotelRoom
    {

        static bool alreadyloggedMotelRoom = false;
        static bool alreadyloggedSweatShop = false;
        static bool Prefix(DialogueController_Ming __instance, string choiceLabel)
        {
            if (choiceLabel != "CHOICE_CONFIRM")
                return true; // Let original method run

            // Access protected npc field
            var npcField = AccessTools.Field(typeof(DialogueController_Ming), "npc");
            var npc = npcField.GetValue(__instance) as NPC;

            var propertyField = AccessTools.Field(typeof(DialogueController_Ming), "Property");
            var property = propertyField.GetValue(__instance) as Property;

            // Execute logic
            NetworkSingleton<MoneyManager>.Instance.ChangeCashBalance(-__instance.Price, true, false);
            npc?.Inventory.InsertItem(NetworkSingleton<MoneyManager>.Instance.GetCashInstance(__instance.Price), true);

            __instance.onPurchase?.Invoke();
            if (property.name == "MotelRoom")
            {
                if (!alreadyloggedMotelRoom)
                {
                    alreadyloggedMotelRoom = true;
                    PropertyPurchaseTracker.SetPurchased("motelroom", true);
                    MelonLogger.Msg("property.name == MotelRoom worked");
                }

            }
            if (property.name == "Sweatshop")
            {
                if (!alreadyloggedSweatShop)
                {
                    alreadyloggedSweatShop = true;
                    PropertyPurchaseTracker.SetPurchased("sweatshop", true);

                    MelonLogger.Msg("property.name == Sweatshop worked");
                }

            }
            MelonLogger.Msg("Blocked Ownership of " + property.name);
            return false; // Skip original method
        }
    }
}
