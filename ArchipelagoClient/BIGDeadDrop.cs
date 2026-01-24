using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;
using System;
using UnityEngine;
using UnityEngine.UI;


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
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[DeadDropExpansion] Backend error: {ex}");
            }
        }

        // ---------------- UI ----------------

        [HarmonyPatch(typeof(StorageMenu), "Open", new[] { typeof(StorageEntity) })]
        [HarmonyPostfix]
        private static void StorageMenu_Open_Postfix(StorageMenu __instance)
        {
            try
            {
                var storage = __instance.OpenedStorageEntity;
                if (storage == null || !IsTarget(storage))
                    return;

                ExpandUI(__instance);
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[DeadDropExpansion] UI error: {ex}");
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
            catch { return false; }
        }

        // ---------------- Backend expansion ----------------

        private static void ExpandBackend(StorageEntity entity)
        {
            var slots = entity.ItemSlots;
            if (slots == null)
                return;

            while (slots.Count < TargetSlots)
                slots.Add(new ItemSlot());
        }

        // ---------------- UI expansion + scrolling ----------------

        private static void ExpandUI(StorageMenu menu)
        {
            var grid = menu.SlotGridLayout;
            var container = menu.SlotContainer;
            var uiSlots = menu.SlotsUIs;

            if (grid == null || container == null || uiSlots == null || uiSlots.Length == 0)
                return;

            // ------------------------------------------------------------
            // Save original UI ONLY when opening a NON-target inventory
            // ------------------------------------------------------------
            // Save original UI once
            // Save original UI once
            if (!savedOriginal)
            {
                // These positions are identical across all inventories
                originalConstraint = grid.constraint;
                originalConstraintCount = grid.constraintCount;
                originalPosition = container.anchoredPosition;

                originalTitlePos = menu.TitleLabel.rectTransform.anchoredPosition;
                originalSubtitlePos = menu.SubtitleLabel.rectTransform.anchoredPosition;
                originalClosePos = menu.CloseButton.anchoredPosition;

                // Only save slot array from NON-target inventories
                if (!IsTarget(menu.OpenedStorageEntity))
                    originalSlots = menu.SlotsUIs;

                savedOriginal = true;
            }

            // ------------------------------------------------------------
            // STOP HERE if this is NOT the target DeadDrop
            // ------------------------------------------------------------
            if (!IsTarget(menu.OpenedStorageEntity))
                return;

            // ------------------------------------------------------------
            // Create ScrollRect ONLY for DeadDrop
            // ------------------------------------------------------------
            ScrollRect scroll = null; // <-- FIX: do NOT search for ScrollRect on other inventories
            scroll = menu.Container.GetComponentInChildren<ScrollRect>();

            if (scroll == null)
            {
                // ⭐ WIDEN THE PARENT PANEL FIRST ⭐
                var parentRT = menu.Container.GetComponent<RectTransform>();

                parentRT.anchorMin = new Vector2(0f, 0f);
                parentRT.anchorMax = new Vector2(1f, 1f);

                parentRT.sizeDelta = new Vector2(0f, parentRT.sizeDelta.y);

                parentRT.offsetMin = new Vector2(-200f, parentRT.offsetMin.y);
                parentRT.offsetMax = new Vector2(200f, parentRT.offsetMax.y);

                // Create ScrollRect object
                var scrollGO = new GameObject("SlotScrollRect");
                scrollGO.transform.SetParent(menu.Container, false);

                var scrollRT = scrollGO.AddComponent<RectTransform>();
                scroll = scrollGO.AddComponent<ScrollRect>();

                scroll.horizontal = false;
                scroll.vertical = true;
                scroll.movementType = ScrollRect.MovementType.Clamped;

                // ⭐ FIXED HEIGHT + FULL WIDTH ⭐
                scrollRT.anchorMin = new Vector2(0f, 0.5f);
                scrollRT.anchorMax = new Vector2(1f, 0.5f);
                scrollRT.pivot = new Vector2(0.5f, 0.5f);

                scrollRT.offsetMin = new Vector2(10f, 0f);
                scrollRT.offsetMax = new Vector2(-10f, 0f);

                scrollRT.sizeDelta = new Vector2(0f, 600f);

                // Create viewport
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
            // Layout changes (ONLY for DeadDrop)
            // ------------------------------------------------------------

            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = TargetColumns;

            // Move title
            var titleRT = menu.TitleLabel.rectTransform;
            if (titleRT.anchoredPosition == originalTitlePos)
            {
                var pos = titleRT.anchoredPosition;
                pos.y += 450f;
                titleRT.anchoredPosition = pos;
            }

            // Move subtitle
            var subRT = menu.SubtitleLabel.rectTransform;
            if (subRT.anchoredPosition == originalSubtitlePos)
            {
                var pos = subRT.anchoredPosition;
                pos.y += 450f;
                subRT.anchoredPosition = pos;
            }

            // Move DONE button
            var closeRT = menu.CloseButton;
            if (closeRT.anchoredPosition == originalClosePos)
            {
                var pos = closeRT.anchoredPosition;
                pos.y -= 280f;
                closeRT.anchoredPosition = pos;
            }

            // Move slot container upward once
            if (!hasMovedContainer)
            {
                var pos = container.anchoredPosition;
                pos.y += 450f;
                container.anchoredPosition = pos;
                hasMovedContainer = true;
            }

            // ------------------------------------------------------------
            // Expand slot array ONLY for DeadDrop
            // ------------------------------------------------------------
            int originalLength = uiSlots.Length;

            if (originalLength < TargetSlots)
            {
                var newArray = new Il2CppReferenceArray<ItemSlotUI>(TargetSlots);

                for (int i = 0; i < originalLength; i++)
                    newArray[i] = uiSlots[i];

                var template = uiSlots[0];

                for (int i = originalLength; i < TargetSlots; i++)
                {
                    var clone = UnityEngine.Object.Instantiate(template, container);
                    clone.gameObject.name = $"{template.gameObject.name}_Extra_{i}";
                    newArray[i] = clone;
                }

                menu.SlotsUIs = newArray;
            }

            // ------------------------------------------------------------
            // Resize container for scrolling ONLY for DeadDrop
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

            var grid = menu.SlotGridLayout;
            var container = menu.SlotContainer;

            // Restore grid layout
            grid.constraint = originalConstraint;
            grid.constraintCount = originalConstraintCount;

            // Restore positions
            container.anchoredPosition = originalPosition;
            menu.TitleLabel.rectTransform.anchoredPosition = originalTitlePos;
            menu.SubtitleLabel.rectTransform.anchoredPosition = originalSubtitlePos;
            menu.CloseButton.anchoredPosition = originalClosePos;

            // Restore parent container width
            var parentRT = menu.Container.GetComponent<RectTransform>();
            parentRT.offsetMin = new Vector2(0f, parentRT.offsetMin.y);
            parentRT.offsetMax = new Vector2(0f, parentRT.offsetMax.y);
            parentRT.sizeDelta = new Vector2(0f, parentRT.sizeDelta.y);

            // Restore SlotContainer parent
            menu.SlotContainer.SetParent(menu.Container, false);

            // Restore SlotContainer RectTransform
            var contRT = menu.SlotContainer.GetComponent<RectTransform>();
            contRT.anchorMin = new Vector2(0f, 1f);
            contRT.anchorMax = new Vector2(1f, 1f);
            contRT.offsetMin = new Vector2(0f, 0f);
            contRT.offsetMax = new Vector2(0f, 0f);
            contRT.sizeDelta = new Vector2(contRT.sizeDelta.x, 0f);

            // Destroy ScrollRect
            var scroll = menu.Container.GetComponentInChildren<ScrollRect>();
            if (scroll != null)
                UnityEngine.Object.Destroy(scroll.gameObject);

            // Restore original slot array
            menu.SlotsUIs = originalSlots;

            // Reset movement flag
            hasMovedContainer = false;

            LayoutRebuilder.ForceRebuildLayoutImmediate(container);
        }
    }
}
