using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Items;
using Il2CppSystem.Collections.Generic;
using Il2CppTMPro;
using MelonLoader;
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using static MelonLoader.MelonLogger;

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

        // Target storage settings
        private const int TargetSlots = 200;
        private const int TargetColumns = 20;
        private const bool DebugAssignLogging = false;

        private static StorageMenu _currentMenu;

        // Track which backends we've already expanded (per entity)
        private static readonly System.Collections.Generic.HashSet<IntPtr> _expandedBackends =
            new System.Collections.Generic.HashSet<IntPtr>();

        // ---------------- Helpers ----------------

        private static bool IsTarget(StorageEntity entity)
        {
            try
            {
                var name = entity.gameObject?.name ?? "";
                return name.Equals("Behind auto shop", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeadDropExpansion] IsTarget error: {ex}");
                return false;
            }
        }

        private static bool ApproximatelyEqual(Vector2 a, Vector2 b, float tolerance = 0.1f)
        {
            return Vector2.SqrMagnitude(a - b) <= tolerance * tolerance;
        }


        // ---------------- Backend ----------------

        [HarmonyPatch(typeof(StorageEntity), "Start")]
        public static class StorageEntity_Initialize_ExpandSlots
        {
            static void Postfix(StorageEntity __instance)
            {
                try
                {
                    if (!IsTarget(__instance))
                        return;

                    ExpandBackend(__instance);

                    MelonLogger.Msg($"[DeadDropExpansion] Backend expanded to {TargetSlots} slots for '{__instance.gameObject?.name}'.");
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"[DeadDropExpansion] Error expanding backend slots: {ex}");
                }
            }
        }

        private static void ExpandBackend(StorageEntity entity)
        {
            if (entity == null)
                return;

            var ptr = (IntPtr)entity.Pointer;
            if (_expandedBackends.Contains(ptr))
                return;

            var slots = entity.ItemSlots;

            if (slots == null)
            {
                slots = new List<ItemSlot>(TargetSlots);
                entity.ItemSlots = slots;
            }

            int originalCount = entity.SlotCount;
            if (originalCount < slots.Count)
                originalCount = slots.Count;

            while (slots.Count < originalCount)
                slots.Add(new ItemSlot());

            ItemSlot template = null;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null)
                {
                    template = slots[i];
                    break;
                }
            }

            IItemSlotOwner owner = null;
            ItemSlotSiblingSet siblingSet = null;

            if (template != null)
            {
                owner = template.SlotOwner;
                siblingSet = template.SiblingSet;
            }

            if (siblingSet == null)
                siblingSet = new ItemSlotSiblingSet(slots);

            while (slots.Count < TargetSlots)
            {
                var newSlot = new ItemSlot();

                if (owner != null)
                    newSlot.SetSlotOwner(owner);

                newSlot.SetSiblingSet(siblingSet);
                newSlot.ItemInstance = null;

                slots.Add(newSlot);
            }

            var list = siblingSet.Slots;
            if (list == null)
                list = new List<ItemSlot>(slots.Count);
            else
                list.Clear();

            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s != null)
                    list.Add(s);
            }

            siblingSet.Slots = list;

            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s != null)
                    s.SetSiblingSet(siblingSet);
            }

            var seen = new System.Collections.Generic.HashSet<IntPtr>();

            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                var inst = s?.ItemInstance;
                if (inst == null)
                    continue;

                var ip = (IntPtr)inst.Pointer;

                if (seen.Contains(ip))
                {
                    var newSlot = new ItemSlot();

                    if (owner != null)
                        newSlot.SetSlotOwner(owner);

                    newSlot.SetSiblingSet(siblingSet);
                    newSlot.ItemInstance = null;

                    slots[i] = newSlot;
                }
                else
                {
                    seen.Add(ip);
                }
            }

            entity.SlotCount = TargetSlots;
            _expandedBackends.Add(ptr);

            MelonLogger.Msg($"[DeadDropExpansion] Expanded backend to {slots.Count} slots, sibling set now has {siblingSet.Slots.Count} slots.");
        }

        // ---------------- UI ----------------

        [HarmonyPatch(typeof(StorageMenu), "Open", new[] { typeof(StorageEntity) })]
        [HarmonyPostfix]
        private static void StorageMenu_Open_Postfix(StorageMenu __instance)
        {
            try
            {
                _currentMenu = __instance;

                var storage = __instance.OpenedStorageEntity;
                if (storage == null)
                    return;

                // Save original UI ONLY when fully initialized AND only once
                if (!savedOriginal && MenuIsFullyInitialized(__instance))
                    SaveOriginalUI(__instance);

                // If not the DeadDrop → restore vanilla UI and exit
                if (!IsTarget(storage))
                {
                    RestoreUI(__instance);
                    return;
                }

                // DeadDrop → expand backend + UI
                ExpandBackend(storage);
                ExpandUI(__instance);

                // Debug info
                var slots = storage.ItemSlots;
                if (slots != null)
                {
                    int nonEmpty = 0;
                    for (int i = 0; i < slots.Count; i++)
                    {
                        var s = slots[i];
                        if (s != null && s.ItemInstance != null && s.Quantity > 0)
                            nonEmpty++;
                    }

                    MelonLogger.Msg($"[DeadDropExpansion] After load: backend slot count = {slots.Count}");
                    MelonLogger.Msg($"[DeadDropExpansion] Non-empty slots after load = {nonEmpty}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeadDropExpansion] UI error: {ex}");
            }
        }


        private static void SaveOriginalUI(StorageMenu menu)
        {
            var grid = menu.SlotGridLayout;
            var container = menu.SlotContainer;
            var uiSlots = menu.SlotsUIs;

            if (grid == null || container == null || uiSlots == null || uiSlots.Length == 0)
                return;

            try
            {
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
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeadDropExpansion] SaveOriginalUI error: {ex}");
            }
        }


        private static void ExpandUI(StorageMenu menu)
        {
            var storage = menu.OpenedStorageEntity;
            if (storage == null)
                return;

            var grid = menu.SlotGridLayout;
            var container = menu.SlotContainer;
            var uiSlots = menu.SlotsUIs;

            if (grid == null || container == null || uiSlots == null || uiSlots.Length == 0)
                return;

            var backendSlots = storage.ItemSlots;
            if (backendSlots == null || backendSlots.Count == 0)
                return;

            // Ensure original UI is saved here if not already
            if (!savedOriginal && MenuIsFullyInitialized(menu))
                SaveOriginalUI(menu);

            storage.SlotCount = TargetSlots;

            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = TargetColumns;

            // Move Title
            if (menu.TitleLabel != null)
            {
                var rt = menu.TitleLabel.rectTransform;
                if (ApproximatelyEqual(rt.anchoredPosition, originalTitlePos))
                {
                    var pos = rt.anchoredPosition;
                    pos.y += 450f;
                    rt.anchoredPosition = pos;
                }
            }

            // Move Subtitle
            if (menu.SubtitleLabel != null)
            {
                var rt = menu.SubtitleLabel.rectTransform;
                if (ApproximatelyEqual(rt.anchoredPosition, originalSubtitlePos))
                {
                    var pos = rt.anchoredPosition;
                    pos.y += 450f;
                    rt.anchoredPosition = pos;
                }
            }

            // Move Close Button
            if (menu.CloseButton != null)
            {
                var rt = menu.CloseButton;
                if (ApproximatelyEqual(rt.anchoredPosition, originalClosePos))
                {
                    var pos = rt.anchoredPosition;
                    pos.y -= 280f;
                    rt.anchoredPosition = pos;
                }
            }

            // Move Container
            if (ApproximatelyEqual(container.anchoredPosition, originalPosition))
            {
                var pos = container.anchoredPosition;
                pos.y += 450f;
                container.anchoredPosition = pos;
            }

            // Expand UI slot array
            int originalLength = uiSlots.Length;
            Il2CppReferenceArray<ItemSlotUI> newArray;

            if (originalLength < TargetSlots)
            {
                newArray = new Il2CppReferenceArray<ItemSlotUI>(TargetSlots);

                for (int i = 0; i < originalLength; i++)
                    newArray[i] = uiSlots[i];

                if (originalLength == 0)
                {
                    MelonLogger.Error("[DeadDropExpansion] No UI slots available; cannot expand UI.");
                    menu.SlotsUIs = uiSlots;
                    return;
                }

                var template = uiSlots[0];
                if (template == null)
                {
                    MelonLogger.Error("[DeadDropExpansion] UI template slot is null; cannot expand UI.");
                    menu.SlotsUIs = uiSlots;
                    return;
                }

                for (int i = originalLength; i < TargetSlots; i++)
                {
                    var clone = UnityEngine.Object.Instantiate(template, container);
                    clone.gameObject.name = $"{template.gameObject.name}_Extra_{i}";
                    newArray[i] = clone;
                }
            }
            else
            {
                newArray = uiSlots;
            }

            // Rebind UI to backend
            int max = Math.Min(TargetSlots, backendSlots.Count);

            for (int i = 0; i < max; i++)
            {
                var ui = newArray[i];
                if (ui == null)
                    continue;

                var backend = backendSlots[i];
                if (backend == null)
                    continue;

                BindUISlotToBackendSlot(ui, backend, i);
            }

            menu.SlotsUIs = newArray;

            LayoutRebuilder.ForceRebuildLayoutImmediate(container);
        }


        // ---------------- Restore UI ----------------

        private static void RestoreUI(StorageMenu menu)
        {
            if (!savedOriginal)
                return;

            try
            {
                var grid = menu.SlotGridLayout;
                var container = menu.SlotContainer;

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

                if (originalSlots != null)
                    menu.SlotsUIs = originalSlots;

                if (container != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(container);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeadDropExpansion] RestoreUI error: {ex}");
            }
        }



        // ---------------- Binding helper ----------------

        private static void BindUISlotToBackendSlot(ItemSlotUI ui, ItemSlot backend, int index)
        {
            if (ui == null)
                return;

            try
            {
                ui.AssignSlot(backend);
                ui.UpdateUI();

                if (DebugAssignLogging)
                {
                    string ptr = backend == null ? "null" : backend.Pointer.ToString();
                    MelonLogger.Msg($"[DeadDropExpansion][AssignSlot] UI='{ui.gameObject.name}' assigned to slot={ptr} (index {index})");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeadDropExpansion] Error binding UI slot {index}: {ex}");
            }
        }

        // ---------------- Debug / refresh hooks ----------------

        [HarmonyPatch(typeof(ItemSlotUI), "AssignSlot")]
        public static class ItemSlotUI_AssignSlot_Logger
        {
            static void Postfix(ItemSlotUI __instance, ItemSlot s)
            {
                if (!DebugAssignLogging)
                    return;

                try
                {
                    var goName = __instance.gameObject?.name ?? "<no name>";
                    var slotPtr = s == null ? "null" : s.Pointer.ToString();

                    MelonLogger.Msg($"[DeadDropExpansion][AssignSlot-Postfix] UI='{goName}' assigned to slot={slotPtr}");
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[DeadDropExpansion] AssignSlot logger error: {ex}");
                }
            }
        }

        [HarmonyPatch(typeof(ItemSlot), "AddItem", new[] { typeof(ItemInstance), typeof(bool) })]
        public static class ItemSlot_AddItem_Patch
        {
            static void Postfix(ItemSlot __instance)
            {
                try
                {
                    if (_currentMenu == null)
                        return;

                    var storage = _currentMenu.OpenedStorageEntity;
                    if (storage == null)
                        return;

                    if (!IsTarget(storage))
                        return;

                    var slots = storage.ItemSlots;
                    if (slots == null)
                        return;

                    int index = -1;
                    for (int i = 0; i < slots.Count; i++)
                    {
                        if (slots[i] == __instance)
                        {
                            index = i;
                            break;
                        }
                    }

                    if (index < 0)
                        return;

                    var uiSlots = _currentMenu.SlotsUIs;
                    if (uiSlots == null || index >= uiSlots.Length)
                        return;

                    var ui = uiSlots[index];
                    if (ui == null)
                        return;

                    ui.AssignSlot(__instance);
                    ui.UpdateUI();
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[DeadDropExpansion] ItemSlot_AddItem_Patch error: {ex}");
                }
            }
        }

        [HarmonyPatch(typeof(StorageMenu), "Close")]
        [HarmonyPostfix]
        private static void StorageMenu_Close_Postfix(StorageMenu __instance)
        {
            try
            {
                RestoreUI(__instance);
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[DeadDropExpansion] Error restoring UI on close: " + ex);
            }

            _currentMenu = null;
        }


        private static void RefreshSingleSlotFor(ItemSlot backend)
        {
            if (_currentMenu == null || backend == null)
                return;

            try
            {
                var storage = _currentMenu.OpenedStorageEntity;
                if (storage == null || !IsTarget(storage))
                    return;

                var slots = storage.ItemSlots;
                if (slots == null)
                    return;

                int index = -1;
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i] == backend)
                    {
                        index = i;
                        break;
                    }
                }

                if (index < 0)
                    return;

                var uiSlots = _currentMenu.SlotsUIs;
                if (uiSlots == null || index >= uiSlots.Length)
                    return;

                var ui = uiSlots[index];
                if (ui == null)
                    return;

                ui.AssignSlot(backend);
                ui.UpdateUI();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeadDropExpansion] RefreshSingleSlotFor error: {ex}");
            }
        }

        [HarmonyPatch(typeof(ItemSlot), "ChangeQuantity", new[] { typeof(int), typeof(bool) })]
        public static class ItemSlot_ChangeQuantity_Patch
        {
            static void Postfix(ItemSlot __instance)
            {
                try
                {
                    RefreshSingleSlotFor(__instance);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[DeadDropExpansion] ItemSlot_ChangeQuantity_Patch error: {ex}");
                }
            }
        }

        private static bool MenuIsFullyInitialized(StorageMenu menu)
        {
            return menu.SlotGridLayout != null
                && menu.SlotContainer != null
                && menu.SlotsUIs != null
                && menu.SlotsUIs.Length > 0
                && menu.TitleLabel != null
                && menu.SubtitleLabel != null
                && menu.CloseButton != null;
        }

    }
}
