using HarmonyLib;
using MelonLoader;
using ScheduleOne.DevUtilities;
using ScheduleOne.Dialogue;
using ScheduleOne.Money;
using ScheduleOne.Property;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Archipelago
{

    public static class PurchaseGuard
    {
        public static HashSet<string> RecentlyProcessed = new HashSet<string>();

        public static async void ScheduleClear(float seconds = 1f)
        {
            int ms = (int)(seconds * 1000);
            await Task.Delay(ms);
            RecentlyProcessed.Clear();
            MelonLogger.Msg("[PurchaseGuard] Cleared recently processed purchases.");
        }
    }

    [HarmonyPatch(typeof(DialogueHandler_EstateAgent), "DialogueCallback")]
    public class ModDialogueCallbackPatch
    {
        static bool Prefix(object __instance, string choiceLabel)
        {
            var propertyField = AccessTools.Field(__instance.GetType(), "selectedProperty");
            var businessField = AccessTools.Field(__instance.GetType(), "selectedBusiness");

            var property = propertyField.GetValue(__instance) as Property;
            var business = businessField.GetValue(__instance) as Business;

            string code = property?.PropertyCode.ToLowerInvariant() ?? business?.PropertyCode.ToLowerInvariant();
            if (string.IsNullOrEmpty(code)) return true;

            if (PurchaseGuard.RecentlyProcessed.Contains(code))
            {
                MelonLogger.Msg($"[ModDialogueCallbackPatch] Skipping duplicate purchase for '{code}'.");
                return false;
            }

            PurchaseGuard.RecentlyProcessed.Add(code);
            PurchaseGuard.ScheduleClear();

            if (choiceLabel == "CONFIRM_BUY" && property != null)
            {
                NetworkSingleton<MoneyManager>.Instance.CreateOnlineTransaction(
                    property.PropertyName + " purchase",
                    -property.Price,
                    1f,
                    string.Empty
                );

                property.SetOwned();
                PropertyPurchaseTracker.SetPurchased(code, true);
                MelonLogger.Msg($"[ModDialogueCallbackPatch] Purchased property '{code}' for ${property.Price:N0}.");
                return false;
            }

            if (choiceLabel == "CONFIRM_BUY_BUSINESS" && business != null)
            {
                NetworkSingleton<MoneyManager>.Instance.CreateOnlineTransaction(
                    business.PropertyName + " purchase",
                    -business.Price,
                    1f,
                    string.Empty
                );

                business.SetOwned();
                PropertyPurchaseTracker.SetPurchased(code, true);
                MelonLogger.Msg($"[ModDialogueCallbackPatch] Purchased business '{code}' for ${business.Price:N0}.");
                return false;
            }

            return true;
        }
    }


    [HarmonyPatch(typeof(DialogueHandler_EstateAgent), "ChoiceCallback")]
    public class ModChoiceCallbackPatch
    {
        static void Postfix(object __instance, string choiceLabel)
        {
            string code = choiceLabel.ToLowerInvariant();

            if (PropertyPurchaseTracker.Contains(code) && !PropertyPurchaseTracker.IsPurchased(code))
            {
                var propertyField = AccessTools.Field(__instance.GetType(), "selectedProperty");
                var businessField = AccessTools.Field(__instance.GetType(), "selectedBusiness");

                var property = Property.Properties.Find(p => p.PropertyCode.ToLower() == code);
                var business = Business.Businesses.Find(b => b.PropertyCode.ToLower() == code);

                if (property != null && property.IsOwned)
                {
                    propertyField.SetValue(__instance, property);
                    Traverse.Create(__instance).Method("ChoiceCallback", new[] { typeof(string) }).GetValue("CONFIRM_BUY");
                }
                else if (business != null && business.IsOwned)
                {
                    businessField.SetValue(__instance, business);
                    Traverse.Create(__instance).Method("ChoiceCallback", new[] { typeof(string) }).GetValue("CONFIRM_BUY_BUSINESS");
                }
            }
        }
    }


    [HarmonyPatch(typeof(DialogueHandler_EstateAgent), "CheckChoice")]
    public class ModCheckChoicePatch
    {
        static bool Prefix(string choiceLabel, ref string invalidReason, ref bool __result)
        {
            string code = choiceLabel.ToLowerInvariant();

            if (PropertyPurchaseTracker.Contains(code) && !PropertyPurchaseTracker.IsPurchased(code))
            {
                var property = Property.Properties.Find(p => p.PropertyCode.ToLower() == code);
                var business = Business.Businesses.Find(b => b.PropertyCode.ToLower() == code);

                float price = property?.Price ?? business?.Price ?? -1f;
                float balance = NetworkSingleton<MoneyManager>.Instance.sync___get_value_onlineBalance();

                if (price > 0 && balance < price)
                {
                    invalidReason = "Insufficient balance";
                    __result = false;
                    return false; // Skip original method
                }
            }

            return true; // Let original method handle everything else
        }
    }


    [HarmonyPatch(typeof(DialogueHandler_EstateAgent), "ModifyChoiceText")]
    public class ModPriceInjectionPatch
    {
        static bool Prefix(string choiceLabel, string choiceText, ref string __result)
        {
            string code = choiceLabel.ToLowerInvariant();

            // Only inject price if it's tracked and not purchased
            if (PropertyPurchaseTracker.Contains(code) && !PropertyPurchaseTracker.IsPurchased(code))
            {
                // Try to find the property or business by code
                var property = Property.Properties.Find(p => p.PropertyCode.ToLower() == code);
                var business = Business.Businesses.Find(b => b.PropertyCode.ToLower() == code);

                float price = property?.Price ?? business?.Price ?? -1;

                if (price > 0)
                {
                    string formatted = $"<color=#19BEF0>(${price:N0})</color>";
                    __result = choiceText.Replace("(<PRICE>)", formatted);
                    return false; // Skip original method
                }
            }

            return true; // Let original method handle other cases
        }
    }
    [HarmonyPatch(typeof(DialogueHandler_EstateAgent), "ShouldChoiceBeShown")]
    public class ModShouldChoiceBeShownPatch
    {
        static bool Prefix(string choiceLabel, ref bool __result)
        {
            string normalized = choiceLabel.ToLowerInvariant();

            MelonLogger.Msg($"[Patch] Intercepted: {normalized}");
            MelonLogger.Msg($"[Patch] Tracked: {PropertyPurchaseTracker.Contains(normalized)}, Purchased: {PropertyPurchaseTracker.IsPurchased(normalized)}");

            // If tracked but not purchased → show it
            if (PropertyPurchaseTracker.Contains(normalized) && !PropertyPurchaseTracker.IsPurchased(normalized))
            {
                __result = true;
                MelonLogger.Msg($"[ModShouldChoiceBeShownPatch] Showing tracked but unpurchased item: {normalized}");
                return false;
            }

            // If purchased → hide it
            if (PropertyPurchaseTracker.IsPurchased(normalized))
            {
                __result = false;
                MelonLogger.Msg($"[ModShouldChoiceBeShownPatch] Hiding purchased item: {normalized}");
                return false;
            }

            // Fallback to original logic
            return true;
        }
    }
}
