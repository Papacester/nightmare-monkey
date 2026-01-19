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
            EDrugType baseDrug = __instance.DrugType;

            MelonLogger.Msg(
                $"[Patch] Recipe added → Product: {__instance.name}, BaseDrug: {baseDrug}, Recipe: {recipe.name}"
            );
        }
    }
}
