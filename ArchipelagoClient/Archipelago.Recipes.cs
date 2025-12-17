using MelonLoader;
using ScheduleOne.Product;
using ScheduleOne.StationFramework;
using HarmonyLib;

namespace Archipelago
{
    [HarmonyPatch(typeof(ProductDefinition), "AddRecipe")]
    public class Patch_Product_AddRecipe
    {
        static void Postfix(ProductDefinition __instance, StationRecipe recipe)
        {
            MelonLogger.Msg($"[Patch] Recipe added to product: {__instance.name} → {recipe.name}");
        }
    }
}
