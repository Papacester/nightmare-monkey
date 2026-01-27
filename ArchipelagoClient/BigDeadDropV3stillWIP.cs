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
        private const int TargetSlots = 200;
        private const int TargetColumns = 20;


        private static StorageMenu _currentMenu;

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

        // ---------------- Backend expansion ----------------

        private static void ExpandBackend(StorageEntity entity)
        {
            var slots = entity.ItemSlots;

            if (slots == null)
            {
                slots = new List<ItemSlot>(TargetSlots);
                entity.ItemSlots = slots;
            }

            // How many slots the game originally had (e.g., 5 or 10)
            int originalCount = entity.SlotCount;

            // Ensure we have at least the original slots
            while (slots.Count < originalCount)
                slots.Add(new ItemSlot());

            // Mirror owner from first slot if present
            IItemSlotOwner owner = null;
            if (slots.Count > 0 && slots[0] != null)
                owner = slots[0].SlotOwner;

            // Get or create sibling set
            ItemSlotSiblingSet siblingSet = null;
            if (slots.Count > 0 && slots[0] != null)
                siblingSet = slots[0].SiblingSet;

            if (siblingSet == null)
                siblingSet = new ItemSlotSiblingSet(slots);

            // -------------------------
            // Expand backend to TargetSlots
            // -------------------------
            while (slots.Count < TargetSlots)
            {
                var newSlot = new ItemSlot();

                if (owner != null)
                    newSlot.SetSlotOwner(owner);

                newSlot.SetSiblingSet(siblingSet);
                slots.Add(newSlot);
            }

            // -------------------------
            // Rebuild sibling list AFTER expansion
            // -------------------------
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

            // Ensure every slot points back to this sibling set
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s != null)
                    s.SetSiblingSet(siblingSet);
            }

            // -------------------------
            // Remove duplicated items across ALL slots
            // -------------------------
            var seen = new System.Collections.Generic.HashSet<IntPtr>();

            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                var inst = s?.ItemInstance;
                if (inst == null)
                    continue;

                var ptr = (IntPtr)inst.Pointer;

                if (seen.Contains(ptr))
                {
                    // Duplicate → replace with empty slot
                    var newSlot = new ItemSlot();

                    if (owner != null)
                        newSlot.SetSlotOwner(owner);

                    newSlot.SetSiblingSet(siblingSet);

                    slots[i] = newSlot;
                }
                else
                {
                    seen.Add(ptr);
                }
            }

            // Finalize slot count
            entity.SlotCount = TargetSlots;

            MelonLogger.Msg($"[DeadDropExpansion] Expanded backend to {slots.Count} slots, sibling set now has {siblingSet.Slots.Count} slots.");
        }

        // ---------------- UI ----------------

        [HarmonyPatch(typeof(StorageMenu), "Open", new[] { typeof(StorageEntity) })]
        [HarmonyPostfix]
        private static void StorageMenu_Open_Postfix(StorageMenu __instance)
        {
            _currentMenu = __instance;
            try
            {
                var storage = __instance.OpenedStorageEntity;
                if (storage == null)
                    return;

                // Always capture original UI once, from a NON‑DeadDrop inventory
                SaveOriginalUI(__instance);

                // Non‑target inventories: restore vanilla UI and bail
                if (!IsTarget(storage))
                {
                    RestoreUI(__instance);
                    return;
                }

                // 🔥 NEW: re-sanitize backend AFTER the game has fully loaded/restored it
                ExpandBackend(storage);

                // Target DeadDrop: now build UI against the cleaned backend
                ExpandUI(__instance);

                var slots = storage.ItemSlots;

                MelonLogger.Msg($"[DeadDropExpansion] After load: backend slot count = {slots.Count}");
                int nonEmpty = 0;
                for (int i = 0; i < slots.Count; i++)
                {
                    var s = slots[i];
                    if (s != null && s.ItemInstance != null && s.Quantity > 0)
                        nonEmpty++;
                }
                MelonLogger.Msg($"[DeadDropExpansion] Non-empty slots after load = {nonEmpty}");

                // Debug dump
                if (slots != null)
                {
                    MelonLogger.Msg("=== DeadDropExpansion: Backend Slot Dump ===");

                    for (int i = 0; i < slots.Count; i++)
                    {
                        var s = slots[i];

                        var inst = s.ItemInstance;
                        string itemName = "null";
                        string ptr = "null";
                        int qty = 0;

                        if (s != null)
                        {
                            if (inst != null)
                            {
                                itemName = inst.ToString();
                                ptr = inst.Pointer.ToString();
                            }
                            qty = s.Quantity;
                        }

                        MelonLogger.Msg($"Slot {i}: ItemInstance={itemName}, Ptr={ptr}, Quantity={qty}");
                    }

                    MelonLogger.Msg("=== End Slot Dump ===");
                }

            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeadDropExpansion] UI error: {ex}");
            }
        }
        // ---------------- Save original UI ----------------

        private static void SaveOriginalUI(StorageMenu menu)
        {
            if (savedOriginal)
                return;

            var grid = menu.SlotGridLayout;
            var container = menu.SlotContainer;
            var uiSlots = menu.SlotsUIs;

            if (grid == null || container == null || uiSlots == null || uiSlots.Length == 0)
                return;

            // Only save original UI from NON‑DeadDrop inventories
            if (!IsTarget(menu.OpenedStorageEntity))
            {
                originalConstraint = grid.constraint;
                originalConstraintCount = grid.constraintCount;
                originalPosition = container.anchoredPosition;

                originalTitlePos = menu.TitleLabel.rectTransform.anchoredPosition;
                originalSubtitlePos = menu.SubtitleLabel.rectTransform.anchoredPosition;
                originalClosePos = menu.CloseButton.anchoredPosition;

                originalSlots = menu.SlotsUIs;

                savedOriginal = true;
            }
        }

        // ---------------- UI expansion (no scroll, 200 slots, 20 columns) ----------------

        private static System.Collections.Generic.List<ItemSlotUI> GetPrimaryUISlots(StorageMenu menu)
        {
            var result = new System.Collections.Generic.List<ItemSlotUI>();
            var seen = new System.Collections.Generic.HashSet<ItemSlot>();

            var uiSlots = menu.SlotsUIs;
            if (uiSlots == null)
                return result;

            for (int i = 0; i < uiSlots.Length; i++)
            {
                var ui = uiSlots[i];
                if (ui == null)
                    continue;

                // NOTE: capital A – this is the IL2CPP wrapper property
                var slot = ui.assignedSlot;
                if (slot == null)
                    continue;

                if (seen.Add(slot))
                {
                    // first UI for this backend slot → keep it
                    result.Add(ui);
                }
                else
                {
                    // duplicate UI for the same backend slot → hide it
                    ui.gameObject.SetActive(false);
                }
            }

            MelonLogger.Msg($"[DeadDropExpansion] GetPrimaryUISlots: primary={result.Count}, totalUI={uiSlots.Length}");
            return result;
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
            if (backendSlots == null)
                return;

           

            storage.SlotCount = TargetSlots;

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

            int originalLength = uiSlots.Length;

            Il2CppReferenceArray<ItemSlotUI> newArray;
            if (originalLength < TargetSlots)
            {
                newArray = new Il2CppReferenceArray<ItemSlotUI>(TargetSlots);

                for (int i = 0; i < originalLength; i++)
                    newArray[i] = uiSlots[i];

                var template = uiSlots[0];

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

            int backendIndex = 0;
            for (int i = 0; i < TargetSlots; i++)
            {
                var ui = newArray[i];
                if (ui == null)
                    continue;

                if (i >= backendSlots.Count)
                    continue;

                var backend = backendSlots[i];
                if (backend == null)
                {
                    MelonLogger.Msg($"[DeadDropExpansion][AssignSlot] Skipping null backend for UI='{ui.gameObject.name}' (index {i})");
                    continue;
                }

                BindUISlotToBackendSlot(ui, backend, i);
            }

            menu.SlotsUIs = newArray;

            LayoutRebuilder.ForceRebuildLayoutImmediate(container);
        }

        private static void BindUISlotToBackendSlot(ItemSlotUI ui, ItemSlot backend, int index)
        {
            if (ui == null)
                return;

            // Assign the backend slot (can be null for out‑of‑range UI slots)
            ui.AssignSlot(backend);

            // Update visuals
            ui.UpdateUI();

            // Logging
            string ptr = backend == null ? "null" : backend.Pointer.ToString();
            MelonLogger.Msg($"[DeadDropExpansion][AssignSlot] UI='{ui.gameObject.name}' assigned to slot={ptr} (index {index})");
        }

        // ---------------- Restore UI ----------------

        private static void RestoreUI(StorageMenu menu)
        {
            if (!savedOriginal)
                return;

            var grid = menu.SlotGridLayout;
            var container = menu.SlotContainer;

            if (grid == null || container == null)
                return;

            // Restore grid layout
            grid.constraint = originalConstraint;
            grid.constraintCount = originalConstraintCount;

            // Restore positions
            container.anchoredPosition = originalPosition;
            menu.TitleLabel.rectTransform.anchoredPosition = originalTitlePos;
            menu.SubtitleLabel.rectTransform.anchoredPosition = originalSubtitlePos;
            menu.CloseButton.anchoredPosition = originalClosePos;

            // Restore original slot array
            if (originalSlots != null)
                menu.SlotsUIs = originalSlots;

            hasMovedContainer = false;

            LayoutRebuilder.ForceRebuildLayoutImmediate(container);
        }

        [HarmonyPatch(typeof(ItemSlotUI), "AssignSlot")]
        public static class ItemSlotUI_AssignSlot_Logger
        {
            static void Postfix(ItemSlotUI __instance, ItemSlot s)
            {
                try
                {
                    var goName = __instance.gameObject?.name ?? "<no name>";
                    var slotPtr = s == null ? "null" : s.Pointer.ToString();

                    MelonLogger.Msg($"[DeadDropExpansion][AssignSlot] UI='{goName}' assigned to slot={slotPtr}");
                    MelonLogger.Msg($"[DeadDropExpansion][AssignSlot] {Environment.StackTrace}");
                }
                catch { }
            }
        }

        [HarmonyPatch(typeof(ItemSlot), "AddItem", new[] { typeof(ItemInstance), typeof(bool) })]
        public static class ItemSlot_AddItem_Patch
        {
            static void Postfix(ItemSlot __instance)
            {
                try
                {
                    // No menu open? Nothing to do.
                    if (_currentMenu == null)
                        return;

                    var storage = _currentMenu.OpenedStorageEntity;
                    if (storage == null)
                        return;

                    // Only touch your DeadDrop
                    if (!IsTarget(storage))
                        return;

                    var slots = storage.ItemSlots;
                    if (slots == null)
                        return;

                    // Find which backend index this slot is
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

                    // Rebind and refresh the correct UI slot
                    ui.AssignSlot(__instance);
                    ui.UpdateUI();
                }
                catch { }
            }
        }


        [HarmonyPatch(typeof(StorageMenu), "Close")]
        [HarmonyPostfix]
        private static void StorageMenu_Close_Postfix()
        {
            _currentMenu = null;
        }
        private static void RefreshSingleSlotFor(ItemSlot backend)
        {
            if (_currentMenu == null || backend == null)
                return;

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
        [HarmonyPatch(typeof(ItemSlot), "ChangeQuantity", new[] { typeof(int), typeof(bool) })]
        public static class ItemSlot_ChangeQuantity_Patch
        {
            static void Postfix(ItemSlot __instance)
            {
                try
                {
                    RefreshSingleSlotFor(__instance);
                }
                catch { }
            }
        }

    }
}
