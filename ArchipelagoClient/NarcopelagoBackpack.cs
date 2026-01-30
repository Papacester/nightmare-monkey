using HarmonyLib;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI.Phone;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Narcopelago
{
    // ------------------------------------------------------------
    // RUNTIME STATE
    // ------------------------------------------------------------
    public static class APBackpackRuntime2
    {
        public static string LastLoadedProfileId = null;
    }

    // ------------------------------------------------------------
    // SAVE HOOK
    // ------------------------------------------------------------
    [HarmonyPatch(typeof(SaveManager), "Save", new Type[] { typeof(string) })]
    public static class Patch_SaveManager_Save_WithPath
    {
        static void Postfix(string saveFolderPath)
        {
            if (string.IsNullOrEmpty(saveFolderPath))
                return;

            string profileId = Path.GetFileName(saveFolderPath);

            APBackpackProfileStorage.SaveForProfile(profileId, saveFolderPath, BackpackSystem.Pending);
            MelonLogger.Msg($"[APBackpack] Save hook: profile '{profileId}' (path: {saveFolderPath})");
        }
    }

    // ------------------------------------------------------------
    // LOAD HOOK (ON GAME START)
    // ------------------------------------------------------------
    [HarmonyPatch(typeof(GameManager), "Start")]
    public static class Patch_GameManager_Start
    {
        static void Prefix()
        {
            string path = Singleton<LoadManager>.Instance.LoadedGameFolderPath;

            if (string.IsNullOrEmpty(path))
                return;

            string profileId = Path.GetFileName(path);

            if (APBackpackRuntime2.LastLoadedProfileId == profileId)
                return;

            MelonLogger.Msg($"[APBackpack] GameManager.Start: loading profile '{profileId}' (path: {path})");

            APBackpackProfileStorage.LoadForProfile(profileId, path, BackpackSystem.Pending);
            APBackpackRuntime2.LastLoadedProfileId = profileId;
        }
    }

    // ------------------------------------------------------------
    // STORAGE WITH WORLD ID
    // ------------------------------------------------------------
    public class APBackpackSaveData
    {
        public string WorldId { get; set; }
        public List<APItem> Items { get; set; } = new List<APItem>();
    }

    public static class APBackpackProfileStorage
    {
        private static string BaseFolder =>
            Path.Combine(MelonEnvironment.UserDataDirectory, "APBackpack");

        private static string GetProfileFilePath(string profileId)
        {
            Directory.CreateDirectory(BaseFolder);
            return Path.Combine(BaseFolder, $"APBackpack_{profileId}.json");
        }

        private static string GetWorldIdFromGameJson(string saveFolderPath)
        {
            try
            {
                var gameJsonPath = Path.Combine(saveFolderPath, "Game.json");
                if (!File.Exists(gameJsonPath))
                {
                    MelonLogger.Warning($"[APBackpack] Game.json not found at '{gameJsonPath}'.");
                    return null;
                }

                var json = File.ReadAllText(gameJsonPath);
                var obj = JObject.Parse(json);

                // Support both "Seed" and "seed"
                var seedToken = obj["Seed"] ?? obj["seed"];
                if (seedToken == null)
                {
                    MelonLogger.Warning("[APBackpack] 'Seed' field not found in Game.json.");
                    return null;
                }

                string seed = seedToken.ToString();
                if (string.IsNullOrEmpty(seed))
                {
                    MelonLogger.Warning("[APBackpack] 'Seed' in Game.json is empty.");
                    return null;
                }

                return seed;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[APBackpack] Failed to read worldId from Game.json: {ex}");
                return null;
            }
        }

        public static void SaveForProfile(string profileId, string saveFolderPath, Queue<APItem> pending)
        {
            var worldId = GetWorldIdFromGameJson(saveFolderPath);
            if (worldId == null)
            {
                MelonLogger.Warning($"[APBackpack] Could not determine worldId for profile '{profileId}'. Skipping APBackpack save.");
                return;
            }

            var path = GetProfileFilePath(profileId);

            // ------------------------------------------------------------
            // NEW: Detect world change immediately on save
            // ------------------------------------------------------------
            if (File.Exists(path))
            {
                try
                {
                    var existingJson = File.ReadAllText(path);
                    var existingData = JsonConvert.DeserializeObject<APBackpackSaveData>(existingJson);

                    if (existingData != null && existingData.WorldId != worldId)
                    {
                        MelonLogger.Msg($"[APBackpack] WorldId changed for '{profileId}' (old={existingData.WorldId}, new={worldId}). Clearing APBackpack immediately.");
                        pending.Clear();
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[APBackpack] Failed to compare worldIds for '{profileId}': {ex}");
                }
            }

            // ------------------------------------------------------------
            // Save updated APBackpack file
            // ------------------------------------------------------------
            var data = new APBackpackSaveData
            {
                WorldId = worldId,
                Items = pending.ToList()
            };

            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(path, json);

            MelonLogger.Msg($"[APBackpack] Saved {data.Items.Count} items for profile '{profileId}' (worldId={worldId}).");
        }

        public static void LoadForProfile(string profileId, string saveFolderPath, Queue<APItem> pending)
        {
            var worldId = GetWorldIdFromGameJson(saveFolderPath);
            if (worldId == null)
            {
                MelonLogger.Warning($"[APBackpack] Could not determine worldId for profile '{profileId}'. Clearing APBackpack to be safe.");
                pending.Clear();
                ClearForProfile(profileId);
                return;
            }

            var path = GetProfileFilePath(profileId);

            if (!File.Exists(path))
            {
                MelonLogger.Msg($"[APBackpack] No APBackpack file for profile '{profileId}', starting empty.");
                pending.Clear();
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                var data = JsonConvert.DeserializeObject<APBackpackSaveData>(json);

                if (data == null || data.WorldId == null)
                {
                    MelonLogger.Msg($"[APBackpack] APBackpack file for '{profileId}' has no worldId. Treating as stale and clearing.");
                    pending.Clear();
                    ClearForProfile(profileId);
                    return;
                }

                if (!string.Equals(data.WorldId, worldId, StringComparison.Ordinal))
                {
                    MelonLogger.Msg($"[APBackpack] WorldId mismatch for '{profileId}' (file={data.WorldId}, current={worldId}). Treating as new world, clearing APBackpack.");
                    pending.Clear();
                    ClearForProfile(profileId);
                    return;
                }

                pending.Clear();
                if (data.Items != null)
                {
                    foreach (var item in data.Items)
                        pending.Enqueue(item);
                }

                MelonLogger.Msg($"[APBackpack] Loaded {pending.Count} items for profile '{profileId}' (worldId={worldId}).");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[APBackpack] Failed to load APBackpack for '{profileId}': {ex}");
                pending.Clear();
            }
        }

        public static void ClearForProfile(string profileId)
        {
            var path = GetProfileFilePath(profileId);

            if (File.Exists(path))
                File.Delete(path);

            MelonLogger.Msg($"[APBackpack] Cleared APBackpack file for profile '{profileId}'.");
        }
    }

    // ------------------------------------------------------------
    // AP ITEM
    // ------------------------------------------------------------
    public class APItem
    {
        public string ID { get; set; }
        public string Name { get; set; }
        public int Quantity { get; set; }

        public APItem(string id, string name, int quantity)
        {
            ID = id;
            Name = name;
            Quantity = quantity;
        }
    }

    // ------------------------------------------------------------
    // BACKPACK SYSTEM
    // ------------------------------------------------------------
    public static class BackpackSystem
    {
        public static Queue<APItem> Pending { get; } = new Queue<APItem>();

        private static PlayerInventory Inv => PlayerInventory.Instance;

        private static bool InsertIntoFirstEmptySlot(ItemInstance inst)
        {
            foreach (var ui in Inv.slotUIs)
            {
                var slot = ui.assignedSlot;
                if (slot != null && slot.ItemInstance == null)
                {
                    slot.InsertItem(inst);
                    return true;
                }
            }
            return false;
        }

        public static bool TryStackIntoExistingSlot(ItemInstance incoming)
        {
            if (Inv == null || incoming == null)
                return false;

            foreach (var ui in Inv.slotUIs)
            {
                var slot = ui.assignedSlot;
                if (slot == null)
                    continue;

                var inst = slot.ItemInstance;
                if (inst == null)
                    continue;

                if (!inst.CanStackWith(incoming, false))
                    continue;

                int max = inst.StackLimit;
                int current = inst.Quantity;

                if (current >= max)
                    continue;

                int space = max - current;
                int toAdd = Mathf.Min(space, incoming.Quantity);

                inst.ChangeQuantity(toAdd);
                incoming.ChangeQuantity(-toAdd);

                MelonLogger.Msg($"[APBackpack] Stacked {toAdd} into existing slot.");

                if (incoming.Quantity <= 0)
                    return true;
            }

            return false;
        }

        public static bool GiveItemToPlayer(APItem item)
        {
            var def = Registry.GetItem(item.ID);
            if (def == null)
                return false;

            var inst = def.GetDefaultInstance(item.Quantity);
            if (inst == null)
                return false;

            TryStackIntoExistingSlot(inst);

            if (inst.Quantity <= 0)
                return true;

            if (InsertIntoFirstEmptySlot(inst))
                return true;

            MelonLogger.Warning("[APBackpack] No empty slot available for leftover items.");
            return false;
        }

        public static bool IsInventoryFull(APItem item)
        {
            if (Inv == null)
                return true;

            var def = Registry.GetItem(item.ID);
            if (def == null)
                return true;

            var tempInst = def.GetDefaultInstance(item.Quantity);
            if (tempInst == null)
                return true;

            foreach (var ui in Inv.slotUIs)
            {
                var slot = ui.assignedSlot;
                if (slot == null)
                    continue;

                var inst = slot.ItemInstance;

                if (inst == null)
                    return false;

                if (inst.CanStackWith(tempInst, false))
                {
                    if (inst.Quantity < inst.StackLimit)
                        return false;
                }
            }

            return true;
        }
    }

    // ------------------------------------------------------------
    // IMGUI WINDOW / MAIN MOD
    // ------------------------------------------------------------
    public class APBackpackWindow : MelonMod
    {
        public static APBackpackWindow Instance { get; private set; }

        private bool _visible = false;
        private Rect _windowRect = new Rect(200, 200, 350, 500);
        private Vector2 _scrollPos;

        private GameObject _popupGO;
        private UnityEngine.UI.Text _popupText;
        private float _popupTimer = 0f;

        public override void OnInitializeMelon()
        {
            Instance = this;
            MelonLogger.Msg("[APBackpack] Mod initialized.");
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            CreatePopupUI();
        }

        private void CreatePopupUI()
        {
            if (_popupGO != null)
                return;

            _popupGO = new GameObject("APBackpackPopup");
            var canvas = _popupGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 9999;

            _popupGO.AddComponent<UnityEngine.UI.CanvasScaler>();
            _popupGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            var textGO = new GameObject("PopupText");
            textGO.transform.SetParent(_popupGO.transform, false);

            _popupText = textGO.AddComponent<UnityEngine.UI.Text>();
            _popupText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _popupText.fontSize = 32;
            _popupText.alignment = TextAnchor.MiddleCenter;
            _popupText.color = new Color(1f, 0.3f, 0.3f, 1f);

            var rect = _popupText.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 0.85f);
            rect.anchorMax = new Vector2(0.5f, 0.85f);
            rect.sizeDelta = new Vector2(600, 100);

            _popupGO.SetActive(false);
        }

        public void ShowPopup(string message)
        {
            if (_popupText == null)
                return;

            _popupText.text = message;
            _popupGO.SetActive(true);
            _popupTimer = 2f;
        }

        public override void OnUpdate()
        {
            // Debug: add items with F6
            if (Input.GetKeyDown(KeyCode.F6))
            {
                BackpackSystem.Pending.Enqueue(new APItem("soil", "Soil", 10));
                BackpackSystem.Pending.Enqueue(new APItem("ogkush", "OG Kush", 1));
                BackpackSystem.Pending.Enqueue(new APItem("ogkush", "OG Kush", 100));
                BackpackSystem.Pending.Enqueue(new APItem("goldenskateboard", "Golden Skateboard", 1));
                BackpackSystem.Pending.Enqueue(new APItem("goldenskateboard", "Golden Skateboard", 2));

                MelonLogger.Msg("[APBackpack] Debug items added.");
            }

            if (_popupTimer > 0f)
            {
                _popupTimer -= Time.deltaTime;

                float alpha = Mathf.Clamp01(_popupTimer / 2f);
                var c = _popupText.color;
                c.a = alpha;
                _popupText.color = c;

                if (_popupTimer <= 0f)
                    _popupGO.SetActive(false);
            }
        }

        public void ShowFromPhone() => _visible = true;
        public void HideFromPhone() => _visible = false;

        public override void OnGUI()
        {
            if (!_visible)
                return;

            GUI.backgroundColor = new Color(0, 0, 0, 0.85f);

            _windowRect = GUI.Window(
                1337,
                _windowRect,
                (GUI.WindowFunction)DrawWindow,
                "AP Backpack"
            );
        }

        private void DrawWindow(int id)
        {
            GUILayout.Label($"Pending: {BackpackSystem.Pending.Count}");

            _scrollPos = GUILayout.BeginScrollView(_scrollPos, GUILayout.Height(380));

            foreach (var apItem in BackpackSystem.Pending.ToArray())
            {
                GUILayout.BeginHorizontal("box");

                GUILayout.Label($"[{apItem.ID}] {apItem.Name} x{apItem.Quantity}", GUILayout.Width(220));

                if (GUILayout.Button("Take", GUILayout.Width(80)))
                {
                    if (BackpackSystem.IsInventoryFull(apItem))
                    {
                        ShowPopup("Inventory is full!");
                    }
                    else
                    {
                        bool success = BackpackSystem.GiveItemToPlayer(apItem);

                        if (success)
                        {
                            var list = new List<APItem>(BackpackSystem.Pending);
                            list.Remove(apItem);

                            BackpackSystem.Pending.Clear();
                            foreach (var it in list)
                                BackpackSystem.Pending.Enqueue(it);
                        }
                        else
                        {
                            ShowPopup("Inventory is full!");
                        }
                    }
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();

            if (GUILayout.Button("Close"))
                HideFromPhone();

            GUI.DragWindow();
        }
    }

    // ------------------------------------------------------------
    // PHONE OPEN/CLOSE PATCH
    // ------------------------------------------------------------
    [HarmonyPatch(typeof(Phone), "SetIsOpen")]
    public static class PhoneOpenPatch
    {
        static void Postfix(bool __0)
        {
            if (APBackpackWindow.Instance == null)
                return;

            if (__0)
                APBackpackWindow.Instance.ShowFromPhone();
            else
                APBackpackWindow.Instance.HideFromPhone();
        }
    }
}
