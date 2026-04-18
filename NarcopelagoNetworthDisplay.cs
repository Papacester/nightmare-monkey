using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using MelonLoader;
using System;
using UnityEngine;

namespace Narcopelago
{
    /// <summary>
    /// Displays goal progress in the top-right corner of the screen.
    /// Shows networth progress when goal requires networth (Goal type 2 or 4).
    /// Shows bomb fragment progress when goal requires bomb fragments (Goal type 0, 3, or 4).
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
        /// Called by MelonMod.OnGUI to draw the goal progress display.
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
                    _labelStyle.fontSize = 26;
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

                int goalType = NarcopelagoOptions.Goal;
                bool showNetworth = GoalRequiresNetworth(goalType);
                bool showBombFragments = GoalRequiresBombFragments(goalType);

                // Calculate how many lines we need
                int lineCount = 0;
                if (showNetworth) lineCount++;
                if (showBombFragments) lineCount++;

                if (lineCount == 0) return;

                // Calculate position (top-right corner)
                float boxWidth = 650f;
                float lineHeight = 70f;
                float boxHeight = lineHeight * lineCount + 10f;
                float padding = 10f;
                float x = Screen.width - boxWidth - padding;
                float y = padding + 50f; // Offset from top to avoid other UI

                Rect boxRect = new Rect(x, y, boxWidth, boxHeight);

                // Draw background box
                GUI.Box(boxRect, "", _boxStyle);

                float currentY = y + 5f;

                // Draw networth progress if needed
                if (showNetworth)
                {
                    string networthText;
                    if (_currentNetworth >= _requiredNetworth)
                    {
                        networthText = $"Networth: ${_currentNetworth:N0} ✓ GOAL!";
                        _labelStyle.normal.textColor = Color.green;
                    }
                    else
                    {
                        float progress = _requiredNetworth > 0 ? (_currentNetworth / _requiredNetworth) * 100f : 0f;
                        networthText = $"Networth: ${_currentNetworth:N0} / ${_requiredNetworth:N0} ({progress:F1}%)";
                        SetColorByProgress(progress);
                    }

                    Rect networthRect = new Rect(x + 5f, currentY, boxWidth - 10f, lineHeight);
                    GUI.Label(networthRect, networthText, _labelStyle);
                    currentY += lineHeight;
                }

                // Draw bomb fragment progress if needed
                if (showBombFragments)
                {
                    int fragmentsReceived = NarcopelagoGoal.BombFragmentsReceived;
                    int fragmentsRequired = NarcopelagoOptions.Number_of_bomb_fragments_required;

                    string fragmentText;
                    if (fragmentsReceived >= fragmentsRequired && fragmentsRequired > 0)
                    {
                        fragmentText = $"Bomb Fragments: {fragmentsReceived}/{fragmentsRequired} ✓ COMPLETE!";
                        _labelStyle.normal.textColor = Color.green;
                    }
                    else
                    {
                        float progress = fragmentsRequired > 0 ? ((float)fragmentsReceived / fragmentsRequired) * 100f : 0f;
                        fragmentText = $"Bomb Fragments: {fragmentsReceived}/{fragmentsRequired} ({progress:F1}%)";
                        SetColorByProgress(progress);
                    }

                    Rect fragmentRect = new Rect(x + 5f, currentY, boxWidth - 10f, lineHeight);
                    GUI.Label(fragmentRect, fragmentText, _labelStyle);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[GoalDisplay] Error in OnGUI: {ex.Message}");
            }
        }

        /// <summary>
        /// Sets the label color based on progress percentage.
        /// </summary>
        private static void SetColorByProgress(float progress)
        {
            if (progress >= 75f)
                _labelStyle.normal.textColor = Color.green;
            else if (progress >= 50f)
                _labelStyle.normal.textColor = Color.yellow;
            else
                _labelStyle.normal.textColor = new Color(1f, 0.6f, 0.2f); // Orange
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
        /// Checks if the goal type requires networth.
        /// </summary>
        private static bool GoalRequiresNetworth(int goalType)
        {
            return goalType == NarcopelagoGoal.GOAL_MISSIONS_NETWORTH || 
                   goalType == NarcopelagoGoal.GOAL_MISSIONS_NETWORTH_BOMB_FRAGMENTS ||
                   goalType == NarcopelagoGoal.GOAL_BOMB_FRAGMENTS_NETWORTH;
        }

        /// <summary>
        /// Checks if the goal type requires bomb fragments.
        /// </summary>
        private static bool GoalRequiresBombFragments(int goalType)
        {
            return goalType == NarcopelagoGoal.GOAL_BOMB_FRAGMENTS_ONLY || 
                   goalType == NarcopelagoGoal.GOAL_MISSIONS_BOMB_FRAGMENTS || 
                   goalType == NarcopelagoGoal.GOAL_MISSIONS_NETWORTH_BOMB_FRAGMENTS ||
                   goalType == NarcopelagoGoal.GOAL_BOMB_FRAGMENTS_NETWORTH;
        }

        /// <summary>
        /// Checks if the goal display should be shown.
        /// </summary>
        private static bool ShouldShowDisplay()
        {
            if (!NarcopelagoOptions.IsLoaded)
                return false;

            int goalType = NarcopelagoOptions.Goal;
            // Show display if goal requires networth or bomb fragments
            return GoalRequiresNetworth(goalType) || GoalRequiresBombFragments(goalType);
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
