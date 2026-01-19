using HarmonyLib;
using MelonLoader;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.UI;
using Il2CppScheduleOne.UI.MainMenu;
namespace Narcopelago
{
    public static class Schedule1PanelManager
    {
        public static GameObject PanelInstance;
        public static Text StatusText;
    }


    [HarmonyPatch(typeof(Il2CppScheduleOne.UI.MainMenu.MainMenuScreen), "Awake")]
    public class MainMenuScreen_Awake_Patch
    {
        static void Postfix(Il2CppScheduleOne.UI.MainMenu.MainMenuScreen __instance)
        {
            if (Schedule1PanelManager.PanelInstance != null)
                return;

            GameObject panel = NarcopelagoUI.CreatePanel(__instance.transform);
            Schedule1PanelManager.PanelInstance = panel;
            panel.SetActive(false);
        }
    }


    [HarmonyPatch(typeof(Il2CppScheduleOne.UI.MainMenu.MainMenuScreen), "Open")]
    public class MainMenuScreen_Open_Patch
    {
        static void Postfix(Il2CppScheduleOne.UI.MainMenu.MainMenuScreen __instance)
        {
            if (Schedule1PanelManager.PanelInstance != null)
            {
                Schedule1PanelManager.PanelInstance.SetActive(true);
                Schedule1PanelManager.PanelInstance.transform.SetAsLastSibling();
                MelonLogger.Msg("Reactivating Schedule1Panel");
            }
        }
    }


    [HarmonyPatch(typeof(Il2CppScheduleOne.UI.MainMenu.MainMenuScreen), "Close")]
    public class MainMenuScreen_Close_Patch
    {
        static void Postfix(Il2CppScheduleOne.UI.MainMenu.MainMenuScreen __instance)
        {
            if (Schedule1PanelManager.PanelInstance != null)
            {
                Schedule1PanelManager.PanelInstance.SetActive(false);
                MelonLogger.Msg("Hiding Schedule1Panel");
            }
        }
    }
}
