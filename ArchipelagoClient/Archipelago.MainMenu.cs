using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;


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
            MelonLogger.Msg("Scene during MainMenuScreen.Awake: " + SceneManager.GetActiveScene().name);

            // Only create panel in the real main menu
            if (SceneManager.GetActiveScene().name != "Menu")
                return;

            if (Schedule1PanelManager.PanelInstance != null)
                return;

            var canvas = GameObject.FindObjectOfType<Canvas>();
            GameObject panel = NarcopelagoUI.CreatePanel(canvas.transform);

            Schedule1PanelManager.PanelInstance = panel;
            panel.SetActive(true);
        }
    }
}
