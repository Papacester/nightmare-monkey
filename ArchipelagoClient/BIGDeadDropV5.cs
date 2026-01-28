using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;
using System;
using UnityEngine;
using UnityEngine.UI;
using static Il2CppScheduleOne.ScriptableObjects.PhoneCallData;

namespace DeadDropExpansion
{
    [HarmonyPatch]
    public static class DeadDropPatch
    {
        // Saved original UI state
        private static GridLayoutGroup.Constraint originalConstraint;
        private static int originalConstraintCount;
        private static Vector2 originalPosition;
        private static Vector2 originalTitlePos;
        private static Vector2 originalSubtitlePos;
        private static Vector2 originalClosePos;
        private static Il2CppReferenceArray<ItemSlotUI> originalSlots;
        private static bool savedOriginal = false;
        private static bool hasMovedContainer = false;

        // Target storage settings
        private const int TargetSlots = 500;
        private const int TargetColumns = 10;

        // ---------------- Backend ----------------

        [HarmonyPatch(typeof(StorageEntity), "Start")]
        [HarmonyPostfix]
        private static void Storage_Start_Postfix(StorageEntity __instance)
        {
            try
            {
                if (!IsTarget(__instance))
                    return;

                ExpandBackend(__instance);

                MelonLoader.MelonLogger.Msg(
                    $"[DeadDropExpansion] StorageEntity.Start fired for: {__instance.gameObject.name}, slots={__instance.ItemSlots.Count}"
                );

                MelonLoader.MelonLogger.Msg(
                    $"[BACKEND] SlotCount={__instance.SlotCount}, ItemSlots={__instance.ItemSlots.Count}"
                );
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[DeadDropExpansion] Backend error: {ex}");
            }
        }

        private static void ExpandBackend(StorageEntity entity)
        {
            var slots = entity.ItemSlots;
            if (slots == null)
                return;

            while (slots.Count < TargetSlots)
                slots.Add(new ItemSlot());

            // Tell the game we now have 500 slots so it saves them
            entity.SlotCount = TargetSlots;
        }

        // ---------------- UI lifecycle ----------------

        [HarmonyPatch(typeof(StorageMenu), "Start")]
        [HarmonyPostfix]
        private static void StorageMenu_Start_Postfix(StorageMenu __instance)
        {
            try
            {
                // Reset state for new scene / new menu instance
                savedOriginal = false;
                hasMovedContainer = false;
                originalSlots = null;

                // Capture the fresh, clean UI layout
                //SaveOriginalUI(__instance);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[DeadDropExpansion] UI Start error: {ex}");
            }
        }

        private static void SaveOriginalUI(StorageMenu menu)
        {
            var grid = menu.SlotGridLayout;
            var container = menu.SlotContainer;
            var uiSlots = menu.SlotsUIs;

            if (grid == null || container == null || uiSlots == null || uiSlots.Length == 0)
                return;

            originalConstraint = grid.constraint;
            originalConstraintCount = grid.constraintCount;
            originalPosition = container.anchoredPosition;

            if (menu.TitleLabel != null)
                originalTitlePos = menu.TitleLabel.rectTransform.anchoredPosition;

            if (menu.SubtitleLabel != null)
                originalSubtitlePos = menu.SubtitleLabel.rectTransform.anchoredPosition;

            if (menu.CloseButton != null)
                originalClosePos = menu.CloseButton.anchoredPosition;

            originalSlots = menu.SlotsUIs;
            savedOriginal = true;
        }

        [HarmonyPatch(typeof(StorageMenu), "Open", new[] { typeof(StorageEntity) })]
        [HarmonyPostfix]
        private static void StorageMenu_Open_Postfix(StorageMenu __instance)
        {
            try
            {
                var storage = __instance.OpenedStorageEntity;
                if (storage == null || !IsTarget(storage))
                    return;

                if (!savedOriginal)
                {
                    SaveOriginalUI(__instance);
                }

                ExpandBackend(storage);
                ExpandUI(__instance);

                MelonLoader.MelonLogger.Msg("[OPEN] StorageMenu.Open fired");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[DeadDropExpansion] UI error: {ex}");
            }
        }

        [HarmonyPatch(typeof(StorageMenu), "Open", new[] { typeof(string), typeof(string), typeof(IItemSlotOwner) })]
        [HarmonyPostfix]
        private static void StorageMenu_Open_Other_Postfix(StorageMenu __instance)
        {
            try
            {
                var storage = __instance.OpenedStorageEntity;
                if (storage == null || !IsTarget(storage))
                    return;

                if (!savedOriginal)
                {
                    SaveOriginalUI(__instance);
                }

                ExpandBackend(storage);
                ExpandUI(__instance);

                MelonLoader.MelonLogger.Msg("[OPEN2] StorageMenu.Open(string,string,IItemSlotOwner) fired");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[DeadDropExpansion] UI error (other Open): {ex}");
            }
        }
        [HarmonyPatch(typeof(StorageMenu), "Close")]
        [HarmonyPrefix]
        private static void StorageMenu_Close_Prefix(StorageMenu __instance)
        {
            try
            {
                var storage = __instance.OpenedStorageEntity;
                if (storage == null || !IsTarget(storage))
                    return;

                RestoreUI(__instance);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[DeadDropExpansion] Restore on close error: {ex}");
            }
        }
        [HarmonyPatch(typeof(StorageMenu), "CloseMenu")]
        [HarmonyPrefix]
        private static void StorageMenu_CloseMenu_Prefix(StorageMenu __instance)
        {
            try
            {
                var storage = __instance.OpenedStorageEntity;
                if (storage == null || !IsTarget(storage))
                    return;

                RestoreUI(__instance);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[DeadDropExpansion] Restore on CloseMenu error: {ex}");
            }
        }

        // ---------------- Matching ----------------

        private static bool IsTarget(StorageEntity entity)
        {
            try
            {
                var name = entity.gameObject?.name ?? "";
                return name.Equals("Behind auto shop", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // ---------------- UI expansion + scrolling ----------------

        private static void ExpandUI(StorageMenu menu)
        {
            if (menu.SlotsUIs != null && menu.SlotsUIs.Length >= TargetSlots && hasMovedContainer)
                return;

            MelonLoader.MelonLogger.Msg("[EXPAND] Running ExpandUI");

            var storage = menu.OpenedStorageEntity;
            if (storage == null)
                return;

            var grid = menu.SlotGridLayout;
            var container = menu.SlotContainer;
            var uiSlots = menu.SlotsUIs;

            if (grid == null || container == null || uiSlots == null || uiSlots.Length == 0)
                return;

            if (!IsTarget(storage))
                return;

            // ------------------------------------------------------------
            // Create ScrollRect ONLY for DeadDrop
            // ------------------------------------------------------------
            ScrollRect scroll = menu.Container.GetComponentInChildren<ScrollRect>();

            if (scroll == null)
            {
                var parentRT = menu.Container.GetComponent<RectTransform>();

                parentRT.anchorMin = new Vector2(0f, 0f);
                parentRT.anchorMax = new Vector2(1f, 1f);
                parentRT.sizeDelta = new Vector2(0f, parentRT.sizeDelta.y);
                parentRT.offsetMin = new Vector2(-200f, parentRT.offsetMin.y);
                parentRT.offsetMax = new Vector2(200f, parentRT.offsetMax.y);

                var scrollGO = new GameObject("SlotScrollRect");
                scrollGO.transform.SetParent(menu.Container, false);

                var scrollRT = scrollGO.AddComponent<RectTransform>();
                scroll = scrollGO.AddComponent<ScrollRect>();

                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;

                scrollRT.anchorMin = new Vector2(0f, 0.5f);
                scrollRT.anchorMax = new Vector2(1f, 0.5f);
                scrollRT.pivot = new Vector2(0.5f, 0.5f);
                scrollRT.offsetMin = new Vector2(10f, 0f);
                scrollRT.offsetMax = new Vector2(-10f, 0f);
                scrollRT.sizeDelta = new Vector2(0f, 600f);

                var viewportGO = new GameObject("Viewport");
                viewportGO.transform.SetParent(scrollGO.transform, false);

                var viewportRT = viewportGO.AddComponent<RectTransform>();
                viewportGO.AddComponent<RectMask2D>();

                viewportRT.anchorMin = new Vector2(0f, 0f);
                viewportRT.anchorMax = new Vector2(1f, 1f);
                viewportRT.offsetMin = Vector2.zero;
                viewportRT.offsetMax = Vector2.zero;

                scroll.viewport = viewportRT;

                menu.SlotContainer.SetParent(viewportRT, false);
                scroll.content = menu.SlotContainer;
            }

            // ------------------------------------------------------------
            // Layout changes
            // ------------------------------------------------------------
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = TargetColumns;

            var titleRT = menu.TitleLabel.rectTransform;
            if (titleRT.anchoredPosition == originalTitlePos)
            {
                var pos = titleRT.anchoredPosition;
                pos.y += 450f;
                titleRT.anchoredPosition = pos;
            }

            var subRT = menu.SubtitleLabel.rectTransform;
            if (subRT.anchoredPosition == originalSubtitlePos)
            {
                var pos = subRT.anchoredPosition;
                pos.y += 450f;
                subRT.anchoredPosition = pos;
            }

            var closeRT = menu.CloseButton;
            if (closeRT.anchoredPosition == originalClosePos)
            {
                var pos = closeRT.anchoredPosition;
                pos.y -= 280f;
                closeRT.anchoredPosition = pos;
            }

            if (!hasMovedContainer)
            {
                var pos = container.anchoredPosition;
                pos.y += 450f;
                container.anchoredPosition = pos;
                hasMovedContainer = true;
            }

            // ------------------------------------------------------------
            // Expand slot array (IL2CPP‑safe, non‑destructive)
            // ------------------------------------------------------------
            int originalLength = uiSlots.Length;
            var newArray = uiSlots;

            if (originalLength < TargetSlots)
            {
                newArray = new Il2CppReferenceArray<ItemSlotUI>(TargetSlots);

                for (int i = 0; i < originalLength; i++)
                    newArray[i] = uiSlots[i];

                var template = uiSlots[0];

                // ⭐ DO NOT CLEAN THE ORIGINAL TEMPLATE — leave it intact

                for (int i = originalLength; i < TargetSlots; i++)
                {
                    var clone = UnityEngine.Object.Instantiate(template, container);
                    clone.gameObject.name = $"{template.gameObject.name}_Extra_{i}";

                    // ⭐ IL2CPP‑SAFE CLEANUP ON CLONES ONLY
                    for (int c = clone.transform.childCount - 1; c >= 0; c--)
                    {
                        var child = clone.transform.GetChild(c);
                        string name = child.name;

                        if (name != "Background" && name != "ItemContainer")
                            UnityEngine.Object.Destroy(child.gameObject);
                    }

                    newArray[i] = clone;
                }

                menu.SlotsUIs = newArray;
            }

            // ------------------------------------------------------------
            // Bind UI slots to backend slots
            // ------------------------------------------------------------
            var backendSlots = storage.ItemSlots;
            if (backendSlots != null)
            {
                MelonLoader.MelonLogger.Msg($"[BACKEND BIND] backend={backendSlots.Count}, ui={menu.SlotsUIs.Length}");

                int backendCount = backendSlots.Count;
                int uiCount = menu.SlotsUIs.Length;

                for (int i = 0; i < uiCount; i++)
                {
                    var ui = menu.SlotsUIs[i];
                    if (ui == null)
                        continue;

                    if (i < backendCount)
                    {
                        var backend = backendSlots[i];
                        var inst = backend.ItemInstance;

                        ui.gameObject.SetActive(true);

                        if (inst == null)
                        {
                            if (ui.ItemUI != null)
                                ui.ItemUI.gameObject.SetActive(false);
                        }
                        else
                        {
                            if (ui.ItemUI != null)
                                ui.ItemUI.gameObject.SetActive(true);

                            ui.AssignSlot(backend);
                            ui.UpdateUI();

                            if (ui.ItemUI != null)
                                ui.ItemUI.UpdateUI();
                        }
                    }
                    else
                    {
                        ui.gameObject.SetActive(false);
                    }
                }
            }

            // ------------------------------------------------------------
            // Resize container for scrolling
            // ------------------------------------------------------------
            int rows = Mathf.CeilToInt(TargetSlots / (float)TargetColumns);
            float cellHeight = grid.cellSize.y;
            float spacing = grid.spacing.y;

            float totalHeight = (rows * cellHeight) + ((rows - 1) * spacing);

            var size = menu.SlotContainer.sizeDelta;
            size.y = totalHeight;
            menu.SlotContainer.sizeDelta = size;

            LayoutRebuilder.ForceRebuildLayoutImmediate(container);
        }

        // ---------------- Restore UI ----------------

        private static void RestoreUI(StorageMenu menu)
        {
            if (!savedOriginal)
                return;

            var storage = menu.OpenedStorageEntity;
            if (storage == null || !IsTarget(storage))
                return;


            var grid = menu.SlotGridLayout;
            var container = menu.SlotContainer;

            try
            {
                if (grid != null)
                {
                    grid.constraint = originalConstraint;
                    grid.constraintCount = originalConstraintCount;
                }

                if (container != null)
                    container.anchoredPosition = originalPosition;

                if (menu.TitleLabel != null)
                    menu.TitleLabel.rectTransform.anchoredPosition = originalTitlePos;

                if (menu.SubtitleLabel != null)
                    menu.SubtitleLabel.rectTransform.anchoredPosition = originalSubtitlePos;

                if (menu.CloseButton != null)
                    menu.CloseButton.anchoredPosition = originalClosePos;

                // Restore parent container width
                var parentRT = menu.Container.GetComponent<RectTransform>();
                if (parentRT != null)
                {
                    parentRT.offsetMin = new Vector2(0f, parentRT.offsetMin.y);
                    parentRT.offsetMax = new Vector2(0f, parentRT.offsetMax.y);
                    parentRT.sizeDelta = new Vector2(0f, parentRT.sizeDelta.y);
                }

                // Restore SlotContainer parent
                if (menu.SlotContainer != null)
                {
                    menu.SlotContainer.SetParent(menu.Container, false);

                    var contRT = menu.SlotContainer.GetComponent<RectTransform>();
                    if (contRT != null)
                    {
                        contRT.anchorMin = new Vector2(0f, 1f);
                        contRT.anchorMax = new Vector2(1f, 1f);
                        contRT.offsetMin = new Vector2(0f, 0f);
                        contRT.offsetMax = new Vector2(0f, 0f);
                        contRT.sizeDelta = new Vector2(contRT.sizeDelta.x, 0f);
                    }
                }

                // Destroy ScrollRect
                var scroll = menu.Container.GetComponentInChildren<ScrollRect>();
                if (scroll != null)
                    UnityEngine.Object.Destroy(scroll.gameObject);

                // Restore original slot array
                if (originalSlots != null)
                    menu.SlotsUIs = originalSlots;

                hasMovedContainer = false;

                if (container != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(container);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[DeadDropExpansion] RestoreUI error: {ex}");
            }
        }
    }
}
