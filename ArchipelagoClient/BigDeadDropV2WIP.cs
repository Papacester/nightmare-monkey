using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;
using MelonLoader;
using System;
using UnityEngine;
using UnityEngine.UI;
using Il2CppSystem.Collections.Generic;

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

            // Grab owner / siblingSet from an existing slot, if any
            IItemSlotOwner owner = null;
            ItemSlotSiblingSet siblingSet = null;

            if (slots.Count > 0 && slots[0] != null)
            {
                owner = slots[0].SlotOwner;
                siblingSet = slots[0].SiblingSet;
            }

            while (slots.Count < TargetSlots)
            {
                var newSlot = new ItemSlot();

                // If we have a template, wire the new slot like the originals
                if (owner != null)
                    newSlot.SetSlotOwner(owner);
                if (siblingSet != null)
                    newSlot.SetSiblingSet(siblingSet);

                slots.Add(newSlot);
            }

            entity.SlotCount = TargetSlots;

        }

        // ---------------- UI ----------------

        [HarmonyPatch(typeof(StorageMenu), "Open", new[] { typeof(StorageEntity) })]
        [HarmonyPostfix]
        private static void StorageMenu_Open_Postfix(StorageMenu __instance)
        {
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

                // Target DeadDrop: expand backend + UI
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

                // ---------------------------------------------------------
                // ⭐ DEBUG: Dump backend slot contents after expansion
                // ---------------------------------------------------------
                if (slots != null)
                {
                    MelonLogger.Msg("=== DeadDropExpansion: Backend Slot Dump ===");

                    for (int i = 0; i < slots.Count; i++)
                    {
                        var s = slots[i];

                        string itemName = "null";
                        int qty = 0;

                        if (s != null)
                        {
                            if (s.ItemInstance != null)
                                itemName = s.ItemInstance.ToString();

                            qty = s.Quantity;
                        }

                        MelonLogger.Msg($"Slot {i}: ItemInstance={itemName}, Quantity={qty}");
                    }

                    MelonLogger.Msg("=== End Slot Dump ===");
                }
                // ---------------------------------------------------------

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

        private static void ExpandUI(StorageMenu menu)
        {
            var storage = menu.OpenedStorageEntity;
            if (storage == null)
                return;

            // Make absolutely sure backend is correct even after load
            ExpandBackend(storage);

            var grid = menu.SlotGridLayout;
            var container = menu.SlotContainer;
            var uiSlots = menu.SlotsUIs;

            if (grid == null || container == null || uiSlots == null || uiSlots.Length == 0)
                return;

            // Grid layout: 20 columns
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

            // Expand slot array to 200 and bind to backend
            int originalLength = uiSlots.Length;
            var backendSlots = storage.ItemSlots;

            if (backendSlots == null)
                return;

            if (originalLength < TargetSlots)
            {
                var newArray = new Il2CppReferenceArray<ItemSlotUI>(TargetSlots);

                // Copy existing UI slots
                for (int i = 0; i < originalLength; i++)
                    newArray[i] = uiSlots[i];

                var template = uiSlots[0];

                // Create new UI slots and bind them to backend slots
                for (int i = originalLength; i < TargetSlots; i++)
                {
                    // Ensure backend has a real slot
                    if (i >= backendSlots.Count)
                        backendSlots.Add(new ItemSlot());

                    var slot = backendSlots[i];

                    var clone = UnityEngine.Object.Instantiate(template, container);
                    clone.gameObject.name = $"{template.gameObject.name}_Extra_{i}";

                    // Bind to backend slot
                    clone.AssignSlot(slot);
                    clone.UpdateUI();

                    // ⭐ If the backend slot is empty, force-clear any copied visuals
                    if (slot.ItemInstance == null || slot.Quantity == 0)
                    {
                        clone.ClearSlot(); // nukes icon, quantity, etc.
                        clone.OverrideDisplayedQuantity(0); // extra paranoia
                        clone.ItemContainer.DetachChildren(); // remove any cloned icon objects
                        clone.SetVisible(true); // optional: hide slot if truly empty
                    }

                    newArray[i] = clone;
                }

                menu.SlotsUIs = newArray;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(container);
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
                    var slotPtr = s == null ? "null" : s.Pointer.ToString(); // or just s.GetHashCode()

                    MelonLogger.Msg($"[DeadDropExpansion][AssignSlot] UI='{goName}' assigned to slot={slotPtr}");
                }
                catch { }
            }
        }

    }
}
