using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using MelonLoader;
using System;
using UnityEngine;

namespace Narcopelago
{
    /// <summary>
    /// Displays the current networth in the top-right corner of the screen
    /// when the goal type requires networth (Goal type 0 or 1).
    /// 
    /// Uses Unity's OnGUI system which is reliable in IL2CPP environments.
    /// </summary>
    public static class NarcopelagoNetworthDisplay
    {
        /// <summary>
        /// Tracks if we're in a game scene.
        /// </summary>
        private static bool _inGameScene = false;

        /// <summary>
        /// Tracks if the display should be active.
        /// </summary>
        private static bool _displayActive = false;

        /// <summary>
        /// Delay frames before activating display.
        /// </summary>
        private static int _activateDelayFrames = 0;

        /// <summary>
        /// Flag indicating activation is pending.
        /// </summary>
        private static bool _activatePending = false;

        /// <summary>
        /// Cached networth value.
        /// </summary>
        private static float _currentNetworth = 0f;

        /// <summary>
        /// Required networth from options.
        /// </summary>
        private static int _requiredNetworth = 0;

        /// <summary>
        /// Frame counter for updating cached values.
        /// </summary>
        private static int _updateCounter = 0;

        /// <summary>
        /// Update interval in frames.
        /// </summary>
        private const int UPDATE_INTERVAL = 30;

        /// <summary>
        /// GUI style for the label (created once).
        /// </summary>
        private static GUIStyle _labelStyle = null;

        /// <summary>
        /// GUI style for the background box.
        /// </summary>
        private static GUIStyle _boxStyle = null;

        /// <summary>
        /// Sets whether we're in a game scene.
        /// </summary>
        public static void SetInGameScene(bool inGame)
        {
            _inGameScene = inGame;

            if (inGame)
            {
                if (ShouldShowDisplay())
                {
                    _activatePending = true;
                    _activateDelayFrames = 180; // ~3 seconds delay
                    MelonLogger.Msg("[NetworthDisplay] Entered game scene - will activate display after delay");
                }
            }
            else
            {
                _displayActive = false;
                _activatePending = false;
            }
        }

        /// <summary>
        /// Process updates on the main thread.
        /// Call this from Core.OnUpdate().
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            if (!_inGameScene)
                return;

            // Process pending activation
            if (_activatePending)
            {
                if (_activateDelayFrames > 0)
                {
                    _activateDelayFrames--;
                    return;
                }

                _activatePending = false;
                _displayActive = true;
                _requiredNetworth = NarcopelagoOptions.Networth_amount_required;
                MelonLogger.Msg("[NetworthDisplay] Display activated");
            }

            if (!_displayActive)
                return;

            // Update cached values periodically
            _updateCounter++;
            if (_updateCounter >= UPDATE_INTERVAL)
            {
                _updateCounter = 0;
                UpdateCachedValues();
            }
        }

        /// <summary>
        /// Called by MelonMod.OnGUI to draw the networth display.
        /// Must be called from Core.OnGUI().
        /// </summary>
        public static void OnGUI()
        {
            if (!_displayActive || !_inGameScene)
                return;

            if (!ShouldShowDisplay())
                return;

            try
            {
                // Initialize styles if needed
                if (_labelStyle == null)
                {
                    _labelStyle = new GUIStyle(GUI.skin.label);
                    _labelStyle.fontSize = 30;
                    _labelStyle.fontStyle = FontStyle.Bold;
                    _labelStyle.alignment = TextAnchor.MiddleRight;
                    _labelStyle.normal.textColor = Color.green;
                    _labelStyle.wordWrap = false;
                }

                if (_boxStyle == null)
                {
                    _boxStyle = new GUIStyle(GUI.skin.box);
                    _boxStyle.normal.background = MakeTexture(2, 2, new Color(0f, 0f, 0f, 0.3f));
                }

                // Calculate position (top-right corner)
                float boxWidth = 650f;
                float boxHeight = 50f;
                float padding = 10f;
                float x = Screen.width - boxWidth - padding;
                float y = padding + 50f; // Offset from top to avoid other UI

                Rect boxRect = new Rect(x, y, boxWidth, boxHeight);

                // Draw background box
                GUI.Box(boxRect, "", _boxStyle);

                // Format the text
                string text;
                if (_currentNetworth >= _requiredNetworth)
                {
                    text = $"Networth: ${_currentNetworth:N0} ✓ GOAL!";
                    _labelStyle.normal.textColor = Color.green;
                }
                else
                {
                    float progress = _requiredNetworth > 0 ? (_currentNetworth / _requiredNetworth) * 100f : 0f;
                    text = $"Networth: ${_currentNetworth:N0} / ${_requiredNetworth:N0} ({progress:F1}%)";
                    
                    // Color based on progress
                    if (progress >= 75f)
                        _labelStyle.normal.textColor = Color.green;
                    else if (progress >= 50f)
                        _labelStyle.normal.textColor = Color.yellow;
                    else
                        _labelStyle.normal.textColor = new Color(1f, 0.6f, 0.2f); // Orange
                }

                // Draw the label
                Rect labelRect = new Rect(x + 5f, y, boxWidth - 10f, boxHeight);
                GUI.Label(labelRect, text, _labelStyle);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[NetworthDisplay] Error in OnGUI: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a solid color texture for the background.
        /// </summary>
        private static Texture2D MakeTexture(int width, int height, Color color)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            
            Texture2D texture = new Texture2D(width, height);
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// Checks if the networth display should be shown.
        /// </summary>
        private static bool ShouldShowDisplay()
        {
            if (!NarcopelagoOptions.IsLoaded)
                return false;

            int goalType = NarcopelagoOptions.Goal;
            return goalType == 0 || goalType == 1;
        }

        /// <summary>
        /// Updates the cached networth values.
        /// </summary>
        private static void UpdateCachedValues()
        {
            try
            {
                if (!NetworkSingleton<MoneyManager>.InstanceExists)
                    return;

                _currentNetworth = NetworkSingleton<MoneyManager>.Instance.GetNetWorth();
                _requiredNetworth = NarcopelagoOptions.Networth_amount_required;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[NetworthDisplay] Error updating cached values: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets the networth display state.
        /// </summary>
        public static void Reset()
        {
            _inGameScene = false;
            _displayActive = false;
            _activatePending = false;
            _updateCounter = 0;
            _currentNetworth = 0f;
            _requiredNetworth = 0;
            _labelStyle = null;
            _boxStyle = null;
        }
    }
}
