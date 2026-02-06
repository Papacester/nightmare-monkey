using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Levelling;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Phone;
using MelonLoader;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Narcopelago
{
    /// <summary>
    /// Manages claimable items received from Archipelago (fillers, cash bundles, XP bundles).
    /// 
    /// Items are added to a claimable list when received from AP.
    /// The player claims them via an in-game phone app called "Archipelago".
    /// Claiming an item applies it immediately (cash to bank, XP to rank, filler to inventory).
    /// 
    /// On reconnect/load, only NEW items (received since connecting) are added.
    /// The Archipelago client's ItemReceived event handles this automatically - it only
    /// fires for items not yet dequeued.
    /// </summary>
    public static class NarcopelagoFillers
    {
        /// <summary>
        /// Represents a single claimable item in the list.
        /// </summary>
        public class ClaimableItem
        {
            public string DisplayName { get; set; }
            public string ItemType { get; set; } // "Cash", "XP", "Filler"
            public int Amount { get; set; } // Cash amount, XP amount, or filler quantity
            public string OriginalItemName { get; set; } // Original AP item name (used for save tracking)
        }

        #region State

        private static readonly List<ClaimableItem> _claimableItems = new List<ClaimableItem>();
        private static readonly object _lock = new object();

        private static ConcurrentQueue<ClaimableItem> _pendingItems = new ConcurrentQueue<ClaimableItem>();

        /// <summary>
        /// Cache mapping AP item display names to their Registry IDs, built from Registry.GetAllItems().
        /// Avoids repeated lookups since Registry is keyed by ID, not display Name.
        /// </summary>
        private static readonly Dictionary<string, string> _itemNameToRegistryId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool _registryCacheBuilt = false;

        private static bool _inGameScene = false;
        private static bool _appInitialized = false;
        private static bool _initPending = false;
        private static int _initDelayFrames = 0;

        /// <summary>
        /// Tracks if a sync from session is pending.
        /// </summary>
        private static bool _syncPending = false;

        /// <summary>
        /// Counter for delayed sync.
        /// </summary>
        private static int _syncDelayFrames = 0;

        // Phone app UI references
        private static GameObject _appPanel = null;
        private static GameObject _appButton = null;
        private static Transform _listContent = null;
        private static Text _emptyText = null;
        private static readonly List<GameObject> _listEntries = new List<GameObject>();

        // Phone close-app integration
        private static bool _closeAppsRegistered = false;
        private static Phone _phoneReference = null;

        #endregion

        #region Public Methods

        /// <summary>
        /// Sets whether we're in a game scene.
        /// </summary>
        public static void SetInGameScene(bool inGame)
        {
            _inGameScene = inGame;
            if (inGame)
            {
                MelonLogger.Msg("[Fillers] Entered game scene");
                _appInitialized = false;
                _initPending = true;
                _initDelayFrames = 180; // ~3 seconds
            }
            else
            {
                _appInitialized = false;
                _appPanel = null;
                _appButton = null;
                _listContent = null;
                _emptyText = null;
                _listEntries.Clear();
            }
        }

        /// <summary>
        /// Checks if an item is a filler item.
        /// </summary>
        public static bool IsFillerItem(string itemName)
        {
            return Data_Items.HasTag(itemName, "Bad Filler") ||
                   Data_Items.HasTag(itemName, "Basic Filler") ||
                   Data_Items.HasTag(itemName, "Better Filler") ||
                   Data_Items.HasTag(itemName, "Amazing Filler");
        }

        /// <summary>
        /// Called when a filler item is received from Archipelago.
        /// Adds it to the claimable items list.
        /// </summary>
        /// <param name="itemName">Name of the item.</param>
        public static void OnFillerItemReceived(string itemName)
        {
            int quantity = GetFillerQuantity(itemName);
            if (quantity <= 0)
            {
                MelonLogger.Warning($"[Fillers] Could not determine quantity for: {itemName}");
                return;
            }

            var item = new ClaimableItem
            {
                DisplayName = $"{itemName} x{quantity}",
                ItemType = "Filler",
                Amount = quantity,
                OriginalItemName = itemName
            };

            _pendingItems.Enqueue(item);
            MelonLogger.Msg($"[Fillers] Queued filler item: {item.DisplayName}");
        }

        /// <summary>
        /// Called when a Cash Bundle is received from Archipelago.
        /// </summary>
        /// <param name="amount">Cash amount.</param>
        public static void OnCashBundleReceived(int amount)
        {
            var item = new ClaimableItem
            {
                DisplayName = $"Cash Bundle (${amount})",
                ItemType = "Cash",
                Amount = amount,
                OriginalItemName = "Cash Bundle"
            };

            _pendingItems.Enqueue(item);
            MelonLogger.Msg($"[Fillers] Queued cash bundle: ${amount}");
        }

        /// <summary>
        /// Called when an XP Bundle is received from Archipelago.
        /// </summary>
        /// <param name="amount">XP amount.</param>
        public static void OnXPBundleReceived(int amount)
        {
            var item = new ClaimableItem
            {
                DisplayName = $"XP Bundle ({amount} XP)",
                ItemType = "XP",
                Amount = amount,
                OriginalItemName = "XP Bundle"
            };

            _pendingItems.Enqueue(item);
            MelonLogger.Msg($"[Fillers] Queued XP bundle: {amount} XP");
        }

        /// <summary>
        /// Process queued items and UI updates on the main thread.
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            if (!_inGameScene)
                return;

            // Process pending initialization
            if (_initPending)
            {
                if (_initDelayFrames > 0)
                {
                    _initDelayFrames--;
                    return;
                }
                _initPending = false;
                InitializePhoneApp();
            }

            // Register with phone's closeApps delegate if not done yet
            if (!_closeAppsRegistered && _appInitialized)
            {
                RegisterWithPhoneCloseApps();
            }

            // Move pending items to the claimable list
            bool listChanged = false;
            while (_pendingItems.TryDequeue(out var item))
            {
                lock (_lock)
                {
                    _claimableItems.Add(item);
                }
                listChanged = true;
            }

            if (listChanged)
            {
                RefreshUI();
            }
        }

        /// <summary>
        /// Gets the count of claimable items.
        /// </summary>
        public static int GetClaimableCount()
        {
            lock (_lock)
            {
                return _claimableItems.Count;
            }
        }

        /// <summary>
        /// Resets state.
        /// </summary>
        public static void Reset()
        {
            lock (_lock)
            {
                _claimableItems.Clear();
            }
            while (_pendingItems.TryDequeue(out _)) { }
            _itemNameToRegistryId.Clear();
            _registryCacheBuilt = false;
            _inGameScene = false;
            _appInitialized = false;
            _initPending = false;
            _appPanel = null;
            _appButton = null;
            _listContent = null;
            _emptyText = null;
            _listEntries.Clear();
            _closeAppsRegistered = false;
            _phoneReference = null;
            _syncPending = false;
            MelonLogger.Msg("[Fillers] Reset tracking state");
        }

        #endregion

        #region Private Methods

        private static int GetFillerQuantity(string itemName)
        {
            if (Data_Items.HasTag(itemName, "Bad Filler")) return 5;
            if (Data_Items.HasTag(itemName, "Basic Filler")) return 3;
            if (Data_Items.HasTag(itemName, "Better Filler")) return 2;
            if (Data_Items.HasTag(itemName, "Amazing Filler")) return 1;
            return 0;
        }

        /// <summary>
        /// Creates the Archipelago phone app UI by injecting into the phone's app canvas.
        /// </summary>
        private static void InitializePhoneApp()
        {
            if (_appInitialized) return;

            try
            {
                MelonLogger.Msg("[Fillers] Initializing Archipelago phone app...");

                // Find the phone's HomeScreen to add our app button
                var homeScreen = PlayerSingleton<HomeScreen>.Instance;
                if (homeScreen == null)
                {
                    MelonLogger.Warning("[Fillers] HomeScreen not available - will retry");
                    _initPending = true;
                    _initDelayFrames = 60;
                    return;
                }

                // Find the phone canvas root
                var phoneCanvas = ((Component)homeScreen).GetComponentInParent<Canvas>();
                if (phoneCanvas == null)
                {
                    MelonLogger.Warning("[Fillers] Phone canvas not found - will retry");
                    _initPending = true;
                    _initDelayFrames = 60;
                    return;
                }

                // Try to load custom app icon from Data folder
                Sprite appIcon = LoadAppIcon();

                // Fall back to an existing icon if custom one not found
                if (appIcon == null)
                {
                    var existingImages = ((Component)homeScreen).GetComponentsInChildren<Image>(true);
                    foreach (var img in existingImages)
                    {
                        if (img.sprite != null && img.gameObject.name.Contains("Icon"))
                        {
                            appIcon = img.sprite;
                            break;
                        }
                    }
                    if (appIcon == null)
                    {
                        foreach (var img in existingImages)
                        {
                            if (img.sprite != null)
                            {
                                appIcon = img.sprite;
                                break;
                            }
                        }
                    }
                }

                // Create the app panel (the full-screen view when the app is opened)
                CreateAppPanel(phoneCanvas.transform);

                // Create the app button on the home screen
                CreateAppButton(((Component)homeScreen).transform, appIcon);

                _appInitialized = true;
                MelonLogger.Msg("[Fillers] Archipelago phone app initialized!");

                // Refresh the UI with any items already in the list
                RefreshUI();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Fillers] Error initializing phone app: {ex.Message}");
                MelonLogger.Error($"[Fillers] Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Loads the custom app icon from the Data folder (appicon.png).
        /// </summary>
        private static Sprite LoadAppIcon()
        {
            try
            {
                string iconPath = Path.Combine(Core.DataPath, "appicon.png");
                if (!File.Exists(iconPath))
                {
                    MelonLogger.Msg($"[Fillers] No custom app icon found at: {iconPath}");
                    return null;
                }

                byte[] imageData = File.ReadAllBytes(iconPath);
                var texture = new Texture2D(2, 2);
                if (UnityEngine.ImageConversion.LoadImage(texture, imageData))
                {
                    var sprite = Sprite.Create(
                        texture,
                        new Rect(0, 0, texture.width, texture.height),
                        new Vector2(0.5f, 0.5f),
                        100f);
                    MelonLogger.Msg($"[Fillers] Loaded custom app icon ({texture.width}x{texture.height})");
                    return sprite;
                }
                else
                {
                    MelonLogger.Warning("[Fillers] Failed to decode appicon.png");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Fillers] Error loading app icon: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Checks if the AP app panel is currently open/visible.
        /// </summary>
        public static bool IsAppPanelOpen()
        {
            return _appPanel != null && _appPanel.activeSelf;
        }

        /// <summary>
        /// Opens the app panel and sets Phone.ActiveApp so the phone knows
        /// an app is open. This makes right-click call RequestCloseApp()
        /// (returning to home screen) instead of closing the phone.
        /// </summary>
        private static void OpenAppPanel()
        {
            if (_appPanel == null) return;

            _appPanel.SetActive(true);

            // Tell the phone that our panel is the active app
            // This prevents right-click from closing the phone entirely
            Phone.ActiveApp = _appPanel;
        }

        /// <summary>
        /// Hides the app panel. Called by the Phone's closeApps delegate
        /// when RequestCloseApp() fires (right-click / home button).
        /// Do NOT touch Phone.ActiveApp here — RequestCloseApp() manages
        /// that after the delegate returns.
        /// </summary>
        public static void CloseAppPanel()
        {
            if (_appPanel != null && _appPanel.activeSelf)
            {
                _appPanel.SetActive(false);
            }
        }

        /// <summary>
        /// Registers our panel close handler with the Phone's closeApps delegate.
        /// When the phone's RequestCloseApp() is called (on right-click with ActiveApp set),
        /// it invokes this delegate, which closes our panel.
        /// </summary>
        private static void RegisterWithPhoneCloseApps()
        {
            try
            {
                if (!PlayerSingleton<Phone>.InstanceExists)
                    return;

                _phoneReference = PlayerSingleton<Phone>.Instance;
                if (_phoneReference == null)
                    return;

                var existingAction = _phoneReference.closeApps;
                _phoneReference.closeApps = existingAction + (Il2CppSystem.Action)new System.Action(CloseAppPanel);
                _closeAppsRegistered = true;
                MelonLogger.Msg("[Fillers] Registered with phone closeApps delegate");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Fillers] Error registering with phone closeApps: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates the main app panel that shows the claimable items list.
        /// </summary>
        private static void CreateAppPanel(Transform phoneRoot)
        {
            // Create the panel as a child of the phone canvas
            _appPanel = new GameObject("ArchipelagoApp");
            _appPanel.transform.SetParent(phoneRoot, false);

            var panelRect = _appPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;

            var panelImage = _appPanel.AddComponent<Image>();
            panelImage.color = new Color(0.08f, 0.08f, 0.12f, 0.98f);

            // Title bar
            var titleBar = new GameObject("TitleBar");
            titleBar.transform.SetParent(_appPanel.transform, false);
            var titleBarRect = titleBar.AddComponent<RectTransform>();
            titleBarRect.anchorMin = new Vector2(0, 1);
            titleBarRect.anchorMax = new Vector2(1, 1);
            titleBarRect.pivot = new Vector2(0.5f, 1);
            titleBarRect.sizeDelta = new Vector2(0, 50);
            var titleBarImg = titleBar.AddComponent<Image>();
            titleBarImg.color = new Color(0.15f, 0.15f, 0.2f, 1f);

            // Title text
            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(titleBar.transform, false);
            var titleText = titleGO.AddComponent<Text>();
            titleText.text = "Archipelago Items";
            titleText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 20;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = Color.white;
            var titleRect = titleGO.GetComponent<RectTransform>();
            titleRect.anchorMin = Vector2.zero;
            titleRect.anchorMax = Vector2.one;
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;

            // Close button
            var closeGO = new GameObject("CloseButton");
            closeGO.transform.SetParent(titleBar.transform, false);
            var closeBtn = closeGO.AddComponent<Button>();
            var closeBtnImg = closeGO.AddComponent<Image>();
            closeBtnImg.color = new Color(0.8f, 0.2f, 0.2f, 1f);
            var closeRect = closeGO.GetComponent<RectTransform>();
            closeRect.anchorMin = new Vector2(1, 0.5f);
            closeRect.anchorMax = new Vector2(1, 0.5f);
            closeRect.pivot = new Vector2(1, 0.5f);
            closeRect.anchoredPosition = new Vector2(-5, 0);
            closeRect.sizeDelta = new Vector2(35, 35);

            var closeLabelGO = new GameObject("Label");
            closeLabelGO.transform.SetParent(closeGO.transform, false);
            var closeLabel = closeLabelGO.AddComponent<Text>();
            closeLabel.text = "X";
            closeLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            closeLabel.fontSize = 18;
            closeLabel.alignment = TextAnchor.MiddleCenter;
            closeLabel.color = Color.white;
            var closeLabelRect = closeLabelGO.GetComponent<RectTransform>();
            closeLabelRect.anchorMin = Vector2.zero;
            closeLabelRect.anchorMax = Vector2.one;
            closeLabelRect.offsetMin = Vector2.zero;
            closeLabelRect.offsetMax = Vector2.zero;

            closeBtn.onClick.AddListener((UnityAction)CloseAppPanel);

            // Scroll area for the item list
            var scrollGO = new GameObject("ScrollArea");
            scrollGO.transform.SetParent(_appPanel.transform, false);
            var scrollRect = scrollGO.AddComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0, 0);
            scrollRect.anchorMax = new Vector2(1, 1);
            scrollRect.offsetMin = new Vector2(5, 5);
            scrollRect.offsetMax = new Vector2(-5, -55);

            var scrollView = scrollGO.AddComponent<ScrollRect>();
            scrollView.horizontal = false;
            var scrollImg = scrollGO.AddComponent<Image>();
            scrollImg.color = new Color(0.05f, 0.05f, 0.08f, 1f);
            var mask = scrollGO.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            // Content container for the scroll view
            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(scrollGO.transform, false);
            var contentRect = contentGO.AddComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0, 1);
            contentRect.anchorMax = new Vector2(1, 1);
            contentRect.pivot = new Vector2(0.5f, 1);
            contentRect.sizeDelta = new Vector2(0, 0);

            var vertLayout = contentGO.AddComponent<VerticalLayoutGroup>();
            vertLayout.spacing = 4;
            vertLayout.padding = new RectOffset(5, 5, 5, 5);
            vertLayout.childAlignment = TextAnchor.UpperCenter;
            vertLayout.childControlWidth = true;
            vertLayout.childControlHeight = false;
            vertLayout.childForceExpandWidth = true;
            vertLayout.childForceExpandHeight = false;

            var contentSizeFitter = contentGO.AddComponent<ContentSizeFitter>();
            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollView.content = contentRect;
            _listContent = contentGO.transform;

            // Empty text placeholder
            var emptyGO = new GameObject("EmptyText");
            emptyGO.transform.SetParent(scrollGO.transform, false);
            _emptyText = emptyGO.AddComponent<Text>();
            _emptyText.text = "No items to claim";
            _emptyText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _emptyText.fontSize = 16;
            _emptyText.alignment = TextAnchor.MiddleCenter;
            _emptyText.color = new Color(0.5f, 0.5f, 0.5f, 1f);
            var emptyRect = emptyGO.GetComponent<RectTransform>();
            emptyRect.anchorMin = Vector2.zero;
            emptyRect.anchorMax = Vector2.one;
            emptyRect.offsetMin = Vector2.zero;
            emptyRect.offsetMax = Vector2.zero;

            // Start hidden
            _appPanel.SetActive(false);
        }

        /// <summary>
        /// Creates the app button on the phone home screen.
        /// </summary>
        private static void CreateAppButton(Transform homeScreenTransform, Sprite icon)
        {
            // Find the app grid/layout on the home screen
            Transform appGrid = null;

            // Look for a GridLayoutGroup or the container that holds app icons
            var gridLayouts = ((Component)homeScreenTransform).GetComponentsInChildren<GridLayoutGroup>(true);
            if (gridLayouts != null && gridLayouts.Length > 0)
            {
                appGrid = ((Component)gridLayouts[0]).transform;
            }

            // Fallback - look for a VerticalLayoutGroup or HorizontalLayoutGroup
            if (appGrid == null)
            {
                var vertLayouts = ((Component)homeScreenTransform).GetComponentsInChildren<VerticalLayoutGroup>(true);
                if (vertLayouts != null && vertLayouts.Length > 0)
                {
                    appGrid = ((Component)vertLayouts[0]).transform;
                }
            }

            // Last fallback - use the home screen transform directly
            if (appGrid == null)
            {
                appGrid = homeScreenTransform;
            }

            MelonLogger.Msg($"[Fillers] Adding app button to: {appGrid.name}");

            // Try to clone an existing app button for consistent styling
            GameObject existingButton = null;
            for (int i = 0; i < appGrid.childCount; i++)
            {
                var child = appGrid.GetChild(i);
                if (child.GetComponent<Button>() != null)
                {
                    existingButton = child.gameObject;
                    break;
                }
            }

            if (existingButton != null)
            {
                // Clone an existing button for consistent look
                _appButton = GameObject.Instantiate(existingButton, appGrid);
                _appButton.name = "ArchipelagoAppButton";

                // Update the label text
                var texts = _appButton.GetComponentsInChildren<Text>(true);
                foreach (var t in texts)
                {
                    t.text = "AP Items";
                }

                // Update the icon sprite if we have a custom one
                if (icon != null)
                {
                    // Replace the sprite on ALL Image components (icon, background, etc.)
                    // The cloned button may have the original app's icon on any child Image
                    var images = _appButton.GetComponentsInChildren<Image>(true);
                    foreach (var img in images)
                    {
                        // Skip the root button's own Image (it's typically the background)
                        if (img.gameObject == _appButton)
                            continue;

                        // Replace any sprite found on child Image components
                        if (img.sprite != null)
                        {
                            img.sprite = icon;
                        }
                    }

                    // Also clear the Button's SpriteState transitions so the original
                    // sprite doesn't show through on hover/press/select
                    var btn2 = _appButton.GetComponent<Button>();
                    if (btn2 != null)
                    {
                        var spriteState = btn2.spriteState;
                        spriteState.highlightedSprite = null;
                        spriteState.pressedSprite = null;
                        spriteState.selectedSprite = null;
                        spriteState.disabledSprite = null;
                        btn2.spriteState = spriteState;
                    }
                }

                // Remove old listeners and add ours
                var btn = _appButton.GetComponent<Button>();
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener((UnityAction)OnAppButtonClicked);

                _appButton.SetActive(true);
            }
            else
            {
                // Create a simple button from scratch
                _appButton = new GameObject("ArchipelagoAppButton");
                _appButton.transform.SetParent(appGrid, false);

                var btnImg = _appButton.AddComponent<Image>();
                btnImg.color = new Color(0.3f, 0.2f, 0.5f, 1f);

                var btn = _appButton.AddComponent<Button>();
                btn.onClick.AddListener((UnityAction)OnAppButtonClicked);

                var btnRect = _appButton.GetComponent<RectTransform>();
                btnRect.sizeDelta = new Vector2(80, 80);

                // Icon
                if (icon != null)
                {
                    var iconGO = new GameObject("Icon");
                    iconGO.transform.SetParent(_appButton.transform, false);
                    var iconImg = iconGO.AddComponent<Image>();
                    iconImg.sprite = icon;
                    var iconRect = iconGO.GetComponent<RectTransform>();
                    iconRect.anchorMin = new Vector2(0.15f, 0.3f);
                    iconRect.anchorMax = new Vector2(0.85f, 0.95f);
                    iconRect.offsetMin = Vector2.zero;
                    iconRect.offsetMax = Vector2.zero;
                }

                // Label
                var labelGO = new GameObject("Label");
                labelGO.transform.SetParent(_appButton.transform, false);
                var label = labelGO.AddComponent<Text>();
                label.text = "AP Items";
                label.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                label.fontSize = 10;
                label.alignment = TextAnchor.LowerCenter;
                label.color = Color.white;
                var labelRect = labelGO.GetComponent<RectTransform>();
                labelRect.anchorMin = new Vector2(0, 0);
                labelRect.anchorMax = new Vector2(1, 0.3f);
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
            }

            MelonLogger.Msg("[Fillers] App button created on home screen");
        }

        /// <summary>
        /// Called when the app button on the phone home screen is clicked.
        /// Opens the app panel.
        /// </summary>
        private static void OnAppButtonClicked()
        {
            if (_appPanel == null) return;

            OpenAppPanel();
            RefreshUI();
        }

        /// <summary>
        /// Refreshes the claimable items list UI.
        /// </summary>
        private static void RefreshUI()
        {
            if (_listContent == null || _emptyText == null) return;

            // Clear existing entries
            foreach (var entry in _listEntries)
            {
                if (entry != null) GameObject.Destroy(entry);
            }
            _listEntries.Clear();

            List<ClaimableItem> items;
            lock (_lock)
            {
                items = new List<ClaimableItem>(_claimableItems);
            }

            _emptyText.gameObject.SetActive(items.Count == 0);

            for (int i = 0; i < items.Count; i++)
            {
                var claimItem = items[i];
                int index = i; // capture for closure

                var entryGO = new GameObject($"Item_{i}");
                entryGO.transform.SetParent(_listContent, false);

                var entryRect = entryGO.AddComponent<RectTransform>();
                entryRect.sizeDelta = new Vector2(0, 40);

                var entryLayout = entryGO.AddComponent<HorizontalLayoutGroup>();
                entryLayout.spacing = 5;
                entryLayout.padding = new RectOffset(5, 5, 2, 2);
                entryLayout.childAlignment = TextAnchor.MiddleLeft;
                entryLayout.childControlWidth = true;
                entryLayout.childControlHeight = true;
                entryLayout.childForceExpandWidth = true;
                entryLayout.childForceExpandHeight = true;

                var entryBg = entryGO.AddComponent<Image>();

                // Color code by type
                entryBg.color = claimItem.ItemType switch
                {
                    "Cash" => new Color(0.15f, 0.25f, 0.15f, 1f),
                    "XP" => new Color(0.15f, 0.15f, 0.3f, 1f),
                    _ => new Color(0.2f, 0.2f, 0.2f, 1f)
                };

                // Item name text
                var nameGO = new GameObject("Name");
                nameGO.transform.SetParent(entryGO.transform, false);
                var nameText = nameGO.AddComponent<Text>();
                nameText.text = claimItem.DisplayName;
                nameText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                nameText.fontSize = 14;
                nameText.color = Color.white;
                nameText.alignment = TextAnchor.MiddleLeft;
                var nameLayout = nameGO.AddComponent<LayoutElement>();
                nameLayout.flexibleWidth = 1;

                // Claim button
                var claimGO = new GameObject("ClaimButton");
                claimGO.transform.SetParent(entryGO.transform, false);
                var claimBtnImg = claimGO.AddComponent<Image>();
                claimBtnImg.color = new Color(0.2f, 0.6f, 0.2f, 1f);
                var claimBtn = claimGO.AddComponent<Button>();
                var claimLayout = claimGO.AddComponent<LayoutElement>();
                claimLayout.minWidth = 60;
                claimLayout.preferredWidth = 60;

                var claimLabelGO = new GameObject("Label");
                claimLabelGO.transform.SetParent(claimGO.transform, false);
                var claimLabel = claimLabelGO.AddComponent<Text>();
                claimLabel.text = "Claim";
                claimLabel.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                claimLabel.fontSize = 12;
                claimLabel.alignment = TextAnchor.MiddleCenter;
                claimLabel.color = Color.white;
                var claimLabelRect = claimLabelGO.GetComponent<RectTransform>();
                claimLabelRect.anchorMin = Vector2.zero;
                claimLabelRect.anchorMax = Vector2.one;
                claimLabelRect.offsetMin = Vector2.zero;
                claimLabelRect.offsetMax = Vector2.zero;

                claimBtn.onClick.AddListener((UnityAction)(() => ClaimItem(index)));

                _listEntries.Add(entryGO);
            }
        }

        /// <summary>
        /// Claims an item at the given index, applies its effect, and removes it from the list.
        /// </summary>
        private static void ClaimItem(int index)
        {
            ClaimableItem item;
            lock (_lock)
            {
                if (index < 0 || index >= _claimableItems.Count) return;
                item = _claimableItems[index];
            }

            bool claimed = false;

            try
            {
                switch (item.ItemType)
                {
                    case "Cash":
                        claimed = ClaimCash(item.Amount);
                        break;
                    case "XP":
                        claimed = ClaimXP(item.Amount);
                        break;
                    case "Filler":
                        claimed = ClaimFillerToInventory(item.OriginalItemName, item.Amount);
                        break;
                    default:
                        MelonLogger.Warning($"[Fillers] Unknown item type: {item.ItemType}");
                        break;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Fillers] Error claiming item: {ex.Message}");
            }

            if (claimed)
            {
                // Mark as claimed in save system using item name
                NarcopelagoSave.MarkItemAsClaimed(item.OriginalItemName);

                lock (_lock)
                {
                    if (index < _claimableItems.Count)
                    {
                        _claimableItems.RemoveAt(index);
                    }
                }
                MelonLogger.Msg($"[Fillers] Claimed: {item.DisplayName}");
                RefreshUI();
            }
        }

        private static bool ClaimCash(int amount)
        {
            if (!NetworkSingleton<MoneyManager>.InstanceExists)
            {
                MelonLogger.Warning("[Fillers] MoneyManager not available");
                return false;
            }

            var moneyManager = NetworkSingleton<MoneyManager>.Instance;
            if (moneyManager == null) return false;

            moneyManager.CreateOnlineTransaction("Archipelago Cash Bundle", (float)amount, 1f, "Cash bundle from Archipelago");
            MelonLogger.Msg($"[Fillers] Added ${amount} to online balance");
            return true;
        }

        private static bool ClaimXP(int amount)
        {
            if (!NetworkSingleton<LevelManager>.InstanceExists)
            {
                MelonLogger.Warning("[Fillers] LevelManager not available");
                return false;
            }

            var levelManager = NetworkSingleton<LevelManager>.Instance;
            if (levelManager == null) return false;

            levelManager.AddXP(amount);
            MelonLogger.Msg($"[Fillers] Added {amount} XP");
            return true;
        }

        private static bool ClaimFillerToInventory(string itemName, int quantity)
        {
            // Check if the PlayerInventory singleton is available
            if (!PlayerSingleton<PlayerInventory>.InstanceExists)
            {
                MelonLogger.Warning("[Fillers] PlayerInventory not available");
                return false;
            }

            var playerInventory = PlayerSingleton<PlayerInventory>.Instance;
            if (playerInventory == null)
            {
                MelonLogger.Warning("[Fillers] PlayerInventory instance is null");
                return false;
            }

            // Find the item definition
            var itemDef = TryFindItemDefinition(itemName);

            if (itemDef == null)
            {
                MelonLogger.Warning($"[Fillers] Could not find item definition for: {itemName}");
                // Still claim it (remove from list) since we can't do anything with it
                return true;
            }

            var itemInstance = itemDef.GetDefaultInstance(quantity);
            if (itemInstance == null)
            {
                MelonLogger.Error($"[Fillers] Failed to create item instance for: {itemName}");
                return true; // Remove from list anyway
            }

            // Check if inventory has space using the game's own method
            if (!playerInventory.CanItemFitInInventory(itemInstance, quantity))
            {
                MelonLogger.Warning("[Fillers] No inventory space - cannot claim filler item");
                if (Singleton<NotificationsManager>.InstanceExists)
                {
                    Singleton<NotificationsManager>.Instance?.SendNotification(
                        "Inventory Full",
                        "You need at least 1 open inventory slot to claim this item.",
                        null, 3f, true);
                }
                return false;
            }

            // Use the game's built-in method to add the item to inventory
            // This handles slot finding, stacking, and network sync properly
            playerInventory.AddItemToInventory(itemInstance);
            MelonLogger.Msg($"[Fillers] Added {quantity}x {itemName} to inventory via AddItemToInventory");
            return true;
        }

        /// <summary>
        /// Builds the name-to-ID cache from the game's Registry.
        /// Called once on first lookup, maps display Name -> Registry ID for all items.
        /// </summary>
        private static void BuildRegistryCache()
        {
            if (_registryCacheBuilt) return;
            _registryCacheBuilt = true;

            try
            {
                if (!Singleton<Registry>.InstanceExists)
                {
                    MelonLogger.Warning("[Fillers] Registry instance not available for cache build");
                    _registryCacheBuilt = false;
                    return;
                }

                var registry = Singleton<Registry>.Instance;
                if (registry == null)
                {
                    MelonLogger.Warning("[Fillers] Registry instance is null");
                    _registryCacheBuilt = false;
                    return;
                }

                var allItems = registry.GetAllItems();
                if (allItems == null)
                {
                    MelonLogger.Warning("[Fillers] GetAllItems returned null");
                    _registryCacheBuilt = false;
                    return;
                }

                int count = 0;
                for (int i = 0; i < allItems.Count; i++)
                {
                    var item = allItems[i];
                    if (item == null) continue;

                    string name = item.Name;
                    string id = item.ID;
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(id))
                    {
                        _itemNameToRegistryId[name] = id;
                        count++;
                    }
                }

                MelonLogger.Msg($"[Fillers] Built registry cache: {count} items mapped (Name -> ID)");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Fillers] Error building registry cache: {ex.Message}");
                _registryCacheBuilt = false;
            }
        }

        private static ItemDefinition TryFindItemDefinition(string itemName)
        {
            // Try exact name as ID (in case Name == ID for some items)
            if (Registry.ItemExists(itemName))
                return Registry.GetItem(itemName);

            // Check the name-to-ID cache (built from Registry.GetAllItems())
            if (_itemNameToRegistryId.TryGetValue(itemName, out string cachedId))
            {
                if (Registry.ItemExists(cachedId))
                    return Registry.GetItem(cachedId);
            }

            // Cache not built yet, or item not found — build/rebuild and retry
            if (!_registryCacheBuilt)
            {
                BuildRegistryCache();

                if (_itemNameToRegistryId.TryGetValue(itemName, out string newCachedId))
                {
                    if (Registry.ItemExists(newCachedId))
                        return Registry.GetItem(newCachedId);
                }
            }

            // Try common ID format variations as a last resort
            string[] variations = new[]
            {
                itemName.ToLower(),
                itemName.Replace(" ", "").ToLower(),
                itemName.Replace(" ", "_").ToLower(),
                itemName.Replace(" ", "-").ToLower(),
            };

            foreach (var variation in variations)
            {
                if (Registry.ItemExists(variation))
                {
                    MelonLogger.Msg($"[Fillers] Found '{itemName}' via ID variation: '{variation}'");
                    _itemNameToRegistryId[itemName] = variation;
                    return Registry.GetItem(variation);
                }
            }

            return null;
        }

        #endregion
    }
}
