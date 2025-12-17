using Archipelago.UI;
using MelonLoader;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Archipelago
{
    public static class Schedule1PanelManager
    {
        public static GameObject PanelInstance;
        public static Text StatusText;
    }


    [HarmonyPatch(typeof(ScheduleOne.UI.MainMenu.MainMenuScreen), "Awake")]
    public class MainMenuScreen_Awake_Patch
    {
        static void Postfix(ScheduleOne.UI.MainMenu.MainMenuScreen __instance)
        {
            if (Schedule1PanelManager.PanelInstance != null)
                return;

            GameObject panel = Schedule1PanelBuilder.CreatePanel(__instance.transform);
            Schedule1PanelManager.PanelInstance = panel;
            panel.SetActive(false);
        }
    }


    [HarmonyPatch(typeof(ScheduleOne.UI.MainMenu.MainMenuScreen), "Open")]
    public class MainMenuScreen_Open_Patch
    {
        static void Postfix(ScheduleOne.UI.MainMenu.MainMenuScreen __instance)
        {
            if (Schedule1PanelManager.PanelInstance != null)
            {
                Schedule1PanelManager.PanelInstance.SetActive(true);
                Schedule1PanelManager.PanelInstance.transform.SetAsLastSibling();
                MelonLogger.Msg("Reactivating Schedule1Panel");
            }
        }
    }


    [HarmonyPatch(typeof(ScheduleOne.UI.MainMenu.MainMenuScreen), "Close")]
    public class MainMenuScreen_Close_Patch
    {
        static void Postfix(ScheduleOne.UI.MainMenu.MainMenuScreen __instance)
        {
            if (Schedule1PanelManager.PanelInstance != null)
            {
                Schedule1PanelManager.PanelInstance.SetActive(false);
                MelonLogger.Msg("Hiding Schedule1Panel");
            }
        }
    }
}
