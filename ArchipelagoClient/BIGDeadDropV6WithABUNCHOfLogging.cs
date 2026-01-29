using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;
using MelonLoader;
using System;
using System.Reflection;
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

                // -------------------------
                // 1. Dump StorageEntity fields
                // -------------------------
                var fields = typeof(StorageEntity).GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );

                MelonLogger.Msg("=== StorageEntity Fields ===");
                foreach (var f in fields)
                    MelonLogger.Msg($"{f.Name} : {f.FieldType}");

                // -------------------------
                // 2. Dump StorageEntity properties
                // -------------------------
                var props = typeof(StorageEntity).GetProperties(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );

                MelonLogger.Msg("=== StorageEntity Properties ===");
                foreach (var p in props)
                    MelonLogger.Msg($"{p.Name} : {p.PropertyType}");

                // -------------------------
                // 3. Expand backend
                // -------------------------
                ExpandBackend(__instance);

                // -------------------------
                // 4. Dump backend slots
                // -------------------------
                DumpSlots(__instance, "START");

                // -------------------------
                // 5. Trace logs
                // -------------------------
                MelonLogger.Msg(
                    $"[DeadDropExpansion] StorageEntity.Start fired for: {__instance.gameObject.name}, slots={__instance.ItemSlots.Count}"
                );

                MelonLogger.Msg(
                    $"[BACKEND] SlotCount={__instance.SlotCount}, ItemSlots={__instance.ItemSlots.Count}"
                );

                MelonLogger.Msg(
                    $"[TRACE] StorageEntity.Start finished for {__instance.name}, slots={__instance.ItemSlots?.Count}"
                );
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[DeadDropExpansion] Backend error: {ex}");
            }
        }

        // Dump all methods on StorageEntity once per entity
        [HarmonyPatch(typeof(StorageEntity), "Start")]
        public static class StorageEntity_Start_MethodDump
        {
            private static bool dumped = false;

            static void Prefix(StorageEntity __instance)
            {
                if (dumped) return;
                if (!DeadDropPatch.IsTarget(__instance)) return;

                dumped = true;

                MelonLogger.Msg("=== StorageEntity Methods ===");

                var methods = typeof(StorageEntity).GetMethods(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly
                );

                foreach (var m in methods)
                {
                    var pars = m.GetParameters();
                    string sig = string.Join(", ", System.Array.ConvertAll(pars, p => p.ParameterType.Name));
                    MelonLogger.Msg($"{m.Name}({sig}) : {m.ReturnType.Name}");
                }
            }
        }

        [HarmonyPatch(typeof(Il2CppScheduleOne.Storage.WorldStorageEntity), "Awake")]
        public static class WorldStorageEntity_MethodDump
        {
            private static bool dumped = false;

            static void Prefix(Il2CppScheduleOne.Storage.WorldStorageEntity __instance)
            {
                if (dumped) return;
                dumped = true;

                MelonLogger.Msg("=== WorldStorageEntity Methods ===");

                var methods = typeof(Il2CppScheduleOne.Storage.WorldStorageEntity).GetMethods(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly
                );

                foreach (var m in methods)
                {
                    var pars = m.GetParameters();
                    string sig = string.Join(", ", System.Array.ConvertAll(pars, p => p.ParameterType.Name));
                    MelonLogger.Msg($"{m.Name}({sig}) : {m.ReturnType.Name}");
                }
            }
        }

        [HarmonyPatch(typeof(Il2CppScheduleOne.Storage.WorldStorageEntity), "Load")]
        public static class WorldStorageEntity_Load_Trace
        {
            static void Prefix(Il2CppScheduleOne.Storage.WorldStorageEntity __instance, object data)
            {
                MelonLogger.Msg("[TRACE] WorldStorageEntity.Load fired");

                // Log the GUID if possible
                try
                {
                    var guidProp = data.GetType().GetProperty("GUID");
                    if (guidProp != null)
                    {
                        var guid = guidProp.GetValue(data);
                        MelonLogger.Msg($"[TRACE] Loading GUID: {guid}");
                    }
                }
                catch { }

                // Dump BEFORE load
                var storage = __instance.TryCast<Il2CppScheduleOne.Storage.StorageEntity>();
                if (storage != null)
                    DeadDropPatch.DumpSlots(storage, "BEFORE LOAD");
            }

            static void Postfix(Il2CppScheduleOne.Storage.WorldStorageEntity __instance, object data)
            {
                MelonLogger.Msg("[TRACE] WorldStorageEntity.Load finished");

                var storage = __instance.TryCast<Il2CppScheduleOne.Storage.StorageEntity>();
                if (storage != null)
                    DeadDropPatch.DumpSlots(storage, "AFTER LOAD");
            }
        }

        [HarmonyPatch(typeof(StorageEntity), "LoadFromItemSet")]
        [HarmonyPostfix]
        private static void Storage_LoadFromItemSet_Postfix(StorageEntity __instance)
        {
            try
            {
                if (!IsTarget(__instance))
                    return;

                DumpSlots(__instance, "LOAD");
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[DeadDropExpansion] LOAD error: {ex}");
            }
        }

        [HarmonyPatch(typeof(StorageEntity), "Awake")]
        [HarmonyPostfix]
        private static void Storage_Awake_Postfix(StorageEntity __instance)
        {
            if (IsTarget(__instance))
                DumpSlots(__instance, "AWAKE");
        }

        private static void ExpandBackend(StorageEntity entity)
        {
            var slots = entity.ItemSlots;
            if (slots == null || slots.Count == 0)
                return;

            int before = slots.Count;
            if (before >= TargetSlots)
                return;

            // Use slot 0 as template for structure
            var template = slots[0];
            var owner = template.SlotOwner;
            var siblingSet = template.SiblingSet;

            MelonLoader.MelonLogger.Msg($"[BACKEND] Expanding from {before} to {TargetSlots}");

            while (slots.Count < TargetSlots)
            {
                var slot = new ItemSlot();

                // Match structure of existing slots
                slot.SlotOwner = owner;
                slot.SiblingSet = siblingSet;
                slot.PlayerFilter = new SlotFilter();

                // Match "empty" state: no item, no locks, no ActiveLock
                // ClearStoredInstance should also ensure quantity is 0.
                try
                {
                    slot.ClearStoredInstance(true);
                }
                catch { }

                slot.IsAddLocked = false;
                slot.IsRemovalLocked = false;
                slot.ActiveLock = null;

                slots.Add(slot);
            }

            entity.SlotCount = slots.Count;

            MelonLoader.MelonLogger.Msg($"[BACKEND] Done. Now {slots.Count} slots, SlotCount={entity.SlotCount}");
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

                if (IsTarget(storage))
                    DumpSlots(storage, "Open");
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

        [HarmonyPatch(typeof(StorageMenu), "Open", new[] { typeof(StorageEntity) })]
        public static class StorageMenu_Open_Trace
        {
            static void Prefix(StorageMenu __instance, StorageEntity entity)
            {
                if (!DeadDropPatch.IsTarget(entity)) return;

                MelonLogger.Msg($"[TRACE] StorageMenu.Open about to run for {entity.name}, slots={entity.ItemSlots?.Count}");

                // Fields
                var fields = typeof(StorageMenu).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MelonLogger.Msg("=== StorageMenu Fields ===");
                foreach (var f in fields)
                    MelonLogger.Msg($"{f.Name} : {f.FieldType}");

                // Properties
                var props = typeof(StorageMenu).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MelonLogger.Msg("=== StorageMenu Properties ===");
                foreach (var p in props)
                    MelonLogger.Msg($"{p.Name} : {p.PropertyType}");
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

                for (int i = originalLength; i < TargetSlots; i++)
                {
                    var clone = UnityEngine.Object.Instantiate(template, container);
                    clone.gameObject.name = $"{template.gameObject.name}_Extra_{i}";

                    MelonLoader.MelonLogger.Msg($"[DEBUG UI SLOT] clone[{i}] BoundSlot ptr = {clone.assignedSlot?.Pointer ?? IntPtr.Zero}");

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

                        // Always bind the backend slot so interactions work,
                        // even when the slot is visually empty.
                        ui.AssignSlot(backend);
                        ui.UpdateUI();

                        if (ui.ItemUI != null)
                        {
                            bool hasItem = inst != null;
                            ui.ItemUI.gameObject.SetActive(hasItem);

                            if (hasItem)
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
        private static void DumpSlots(StorageEntity entity, string context)
        {
            try
            {
                var slots = entity.ItemSlots;
                if (slots == null)
                {
                    MelonLoader.MelonLogger.Msg($"[{context}] ItemSlots is NULL");
                    return;
                }

                MelonLoader.MelonLogger.Msg(
                    $"[{context}] Dumping slots for '{entity.gameObject.name}', count={slots.Count}, SlotCount={entity.SlotCount}"
                );

                for (int i = 0; i < slots.Count; i++)
                {
                    var s = slots[i];
                    if (s == null)
                    {
                        MelonLoader.MelonLogger.Msg($"[{context}] [{i}] <null slot>");
                        continue;
                    }

                    var inst = s.ItemInstance;
                    string itemDesc = inst == null ? "NULL" : inst.ToString();

                    MelonLoader.MelonLogger.Msg(
                        $"[{context}] [{i}] Qty={s.Quantity}, Item={itemDesc}, AddLocked={s.IsAddLocked}, RemLocked={s.IsRemovalLocked}"
                    );
                }
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Error($"[{context}] DumpSlots error: {ex}");
            }
        }

    }
}
