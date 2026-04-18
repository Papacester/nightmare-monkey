using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using Archipelago.MultiClient.Net.Packets;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Money;
using MelonLoader;
using System;

namespace Narcopelago
{
    /// <summary>
    /// Handles goal/win condition checking for Schedule I Archipelago.
    /// 
    /// Goal types (NarcopelagoOptions.Goal):
    /// 0 = Bomb Fragments only
    /// 1 = Missions only (Finish the Job quest)
    /// 2 = Missions AND Networth
    /// 3 = Missions AND Bomb Fragments
    /// 4 = Missions AND Networth AND Bomb Fragments
    /// 5 = Bomb Fragments AND Networth
    /// </summary>
    public static class NarcopelagoGoal
    {
        // Goal type constants
        public const int GOAL_BOMB_FRAGMENTS_ONLY = 0;
        public const int GOAL_MISSIONS_ONLY = 1;
        public const int GOAL_MISSIONS_NETWORTH = 2;
        public const int GOAL_MISSIONS_BOMB_FRAGMENTS = 3;
        public const int GOAL_MISSIONS_NETWORTH_BOMB_FRAGMENTS = 4;
        public const int GOAL_BOMB_FRAGMENTS_NETWORTH = 5;

        /// <summary>
        /// The location name for the final quest step.
        /// </summary>
        private const string FINISH_THE_JOB_LOCATION = "Finishing the Job|Wait for the bomb to detonate";

        /// <summary>
        /// The modern ID for the Bomb Fragment item.
        /// </summary>
        public const int BOMB_FRAGMENT_MODERN_ID = 600;

        /// <summary>
        /// Tracks if the networth goal has been reached.
        /// </summary>
        private static bool _networthGoalReached = false;

        /// <summary>
        /// Tracks if the Finish the Job quest has been completed (missions goal).
        /// </summary>
        private static bool _finishTheJobComplete = false;

        /// <summary>
        /// Tracks if the bomb fragments goal has been reached.
        /// </summary>
        private static bool _bombFragmentsGoalReached = false;

        /// <summary>
        /// Tracks the number of bomb fragments received.
        /// </summary>
        private static int _bombFragmentsReceived = 0;

        /// <summary>
        /// Tracks if the overall goal has been completed and sent to server.
        /// </summary>
        private static bool _goalCompleted = false;

        /// <summary>
        /// Tracks if we're in a game scene.
        /// </summary>
        private static bool _inGameScene = false;

        /// <summary>
        /// Frame counter for periodic networth checks.
        /// </summary>
        private static int _checkFrameCounter = 0;

        /// <summary>
        /// How often to check networth (in frames). ~60 = 1 second at 60fps.
        /// </summary>
        private const int CHECK_INTERVAL_FRAMES = 120;

        /// <summary>
        /// Indicates whether the goal has been completed.
        /// </summary>
        public static bool IsGoalComplete => _goalCompleted;

        /// <summary>
        /// Gets the number of bomb fragments received.
        /// </summary>
        public static int BombFragmentsReceived => _bombFragmentsReceived;

        /// <summary>
        /// Gets whether the bomb fragments goal has been reached.
        /// </summary>
        public static bool IsBombFragmentsGoalReached => _bombFragmentsGoalReached;

        /// <summary>
        /// Gets whether the networth goal has been reached.
        /// </summary>
        public static bool IsNetworthGoalReached => _networthGoalReached;

        /// <summary>
        /// Gets whether the missions goal has been reached.
        /// </summary>
        public static bool IsMissionsGoalReached => _finishTheJobComplete;

        /// <summary>
        /// Sets whether we're in a game scene.
        /// </summary>
        public static void SetInGameScene(bool inGame)
        {
            _inGameScene = inGame;
            if (inGame)
            {
                MelonLogger.Msg("[Goal] Entered game scene");
            }
        }

        /// <summary>
        /// Called from Core.OnUpdate to process goal checks.
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            if (!_inGameScene || _goalCompleted)
            {
                return;
            }

            // Only check periodically to avoid performance impact
            _checkFrameCounter++;
            if (_checkFrameCounter < CHECK_INTERVAL_FRAMES)
            {
                return;
            }
            _checkFrameCounter = 0;

            // Check if networth goal needs to be checked based on goal type
            int goalType = NarcopelagoOptions.Goal;
            if (GoalRequiresNetworth(goalType))
            {
                CheckNetworthGoal();
            }

            // Check overall goal completion
            CheckGoalCompletion();
        }

        /// <summary>
        /// Called when a bomb fragment item is received from Archipelago.
        /// </summary>
        public static void OnBombFragmentReceived()
        {
            _bombFragmentsReceived++;
            int required = NarcopelagoOptions.Number_of_bomb_fragments_required;

            MelonLogger.Msg($"[Goal] Bomb Fragment received! ({_bombFragmentsReceived}/{required})");

            if (!_bombFragmentsGoalReached && _bombFragmentsReceived >= required && required > 0)
            {
                MelonLogger.Msg($"[Goal] All bomb fragments collected!");
                _bombFragmentsGoalReached = true;
                CheckGoalCompletion();
            }
        }

        /// <summary>
        /// Called when a location check is sent to notify goal system.
        /// This is used to detect when "Finish the Job" quest is completed.
        /// </summary>
        public static void OnLocationChecked(string locationName)
        {
            if (_finishTheJobComplete)
            {
                return;
            }

            if (locationName == FINISH_THE_JOB_LOCATION)
            {
                MelonLogger.Msg("[Goal] Finish the Job quest completed (Missions goal reached)!");
                _finishTheJobComplete = true;
                CheckGoalCompletion();
            }
        }

        /// <summary>
        /// Called when a location check is sent to notify goal system (by ID).
        /// </summary>
        public static void OnLocationChecked(int locationId)
        {
            if (_finishTheJobComplete)
            {
                return;
            }

            // Get the location name from the ID
            string locationName = Data_Locations.GetLocationName(locationId);
            if (!string.IsNullOrEmpty(locationName))
            {
                OnLocationChecked(locationName);
            }
        }

        /// <summary>
        /// Checks if the networth goal has been reached.
        /// </summary>
        private static void CheckNetworthGoal()
        {
            if (_networthGoalReached)
            {
                return;
            }

            try
            {
                if (!NetworkSingleton<MoneyManager>.InstanceExists)
                {
                    return;
                }

                float currentNetworth = NetworkSingleton<MoneyManager>.Instance.GetNetWorth();
                int requiredNetworth = NarcopelagoOptions.Networth_amount_required;

                if (currentNetworth >= requiredNetworth)
                {
                    MelonLogger.Msg($"[Goal] Networth goal reached! Current: ${currentNetworth:N0}, Required: ${requiredNetworth:N0}");
                    _networthGoalReached = true;
                    CheckGoalCompletion();
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Goal] Error checking networth: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if the given goal type requires networth.
        /// </summary>
        private static bool GoalRequiresNetworth(int goalType)
        {
            return goalType == GOAL_MISSIONS_NETWORTH || 
                   goalType == GOAL_MISSIONS_NETWORTH_BOMB_FRAGMENTS ||
                   goalType == GOAL_BOMB_FRAGMENTS_NETWORTH;
        }

        /// <summary>
        /// Checks if the given goal type requires missions (Finish the Job).
        /// </summary>
        private static bool GoalRequiresMissions(int goalType)
        {
            return goalType == GOAL_MISSIONS_ONLY || 
                   goalType == GOAL_MISSIONS_NETWORTH || 
                   goalType == GOAL_MISSIONS_BOMB_FRAGMENTS || 
                   goalType == GOAL_MISSIONS_NETWORTH_BOMB_FRAGMENTS;
        }

        /// <summary>
        /// Checks if the given goal type requires bomb fragments.
        /// </summary>
        private static bool GoalRequiresBombFragments(int goalType)
        {
            return goalType == GOAL_BOMB_FRAGMENTS_ONLY || 
                   goalType == GOAL_MISSIONS_BOMB_FRAGMENTS || 
                   goalType == GOAL_MISSIONS_NETWORTH_BOMB_FRAGMENTS ||
                   goalType == GOAL_BOMB_FRAGMENTS_NETWORTH;
        }

        /// <summary>
        /// Checks if all goal conditions are met and sends completion to server.
        /// </summary>
        private static void CheckGoalCompletion()
        {
            if (_goalCompleted)
            {
                return;
            }

            int goalType = NarcopelagoOptions.Goal;
            bool goalMet = false;

            switch (goalType)
            {
                case GOAL_BOMB_FRAGMENTS_ONLY:
                    // Bomb Fragments only
                    goalMet = _bombFragmentsGoalReached;
                    break;

                case GOAL_MISSIONS_ONLY:
                    // Missions only (Finish the Job)
                    goalMet = _finishTheJobComplete;
                    break;

                case GOAL_MISSIONS_NETWORTH:
                    // Missions AND Networth
                    goalMet = _finishTheJobComplete && _networthGoalReached;
                    break;

                case GOAL_MISSIONS_BOMB_FRAGMENTS:
                    // Missions AND Bomb Fragments
                    goalMet = _finishTheJobComplete && _bombFragmentsGoalReached;
                    break;

                case GOAL_MISSIONS_NETWORTH_BOMB_FRAGMENTS:
                    // Missions AND Networth AND Bomb Fragments
                    goalMet = _finishTheJobComplete && _networthGoalReached && _bombFragmentsGoalReached;
                    break;

                case GOAL_BOMB_FRAGMENTS_NETWORTH:
                    // Bomb Fragments AND Networth (no missions)
                    goalMet = _bombFragmentsGoalReached && _networthGoalReached;
                    break;

                default:
                    MelonLogger.Warning($"[Goal] Unknown goal type: {goalType}");
                    break;
            }

            if (goalMet)
            {
                CompleteGoal();
            }
        }

        /// <summary>
        /// Sends goal completion to the Archipelago server.
        /// </summary>
        private static void CompleteGoal()
        {
            if (_goalCompleted)
            {
                return;
            }

            _goalCompleted = true;

            MelonLogger.Msg("[Goal] ========================================");
            MelonLogger.Msg("[Goal] GOAL COMPLETE! Sending to Archipelago...");
            MelonLogger.Msg("[Goal] ========================================");

            try
            {
                var session = ConnectionHandler.CurrentSession;
                if (session == null || !session.Socket.Connected)
                {
                    MelonLogger.Error("[Goal] Cannot send goal completion - not connected to Archipelago");
                    _goalCompleted = false; // Allow retry
                    return;
                }

                // Send StatusUpdate packet with GOAL status
                var statusPacket = new StatusUpdatePacket
                {
                    Status = ArchipelagoClientState.ClientGoal
                };

                session.Socket.SendPacket(statusPacket);

                MelonLogger.Msg("[Goal] Goal completion sent to server!");
                MelonLogger.Msg("[Goal] Congratulations on completing Schedule I!");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Goal] Failed to send goal completion: {ex.Message}");
                _goalCompleted = false; // Allow retry
            }
        }

        /// <summary>
        /// Syncs goal state from Archipelago session.
        /// Checks if Finish the Job location was already sent.
        /// </summary>
        public static void SyncFromSession()
        {
            if (_goalCompleted)
            {
                return;
            }

            try
            {
                // Check if Finish the Job location was already checked
                int finishJobLocationId = Data_Locations.GetLocationId(FINISH_THE_JOB_LOCATION);
                if (finishJobLocationId > 0)
                {
                    var checkedLocations = NarcopelagoLocations.AllLocationsChecked;
                    if (checkedLocations != null && checkedLocations.Contains(finishJobLocationId))
                    {
                        if (!_finishTheJobComplete)
                        {
                            MelonLogger.Msg("[Goal] Synced: Finish the Job quest already completed");
                            _finishTheJobComplete = true;
                        }
                    }
                }

                // Immediately check networth and goal on sync
                CheckNetworthGoal();
                CheckGoalCompletion();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Goal] Error syncing from session: {ex.Message}");
            }
        }

        /// <summary>
        /// Resets all goal tracking state.
        /// </summary>
        public static void Reset()
        {
            _networthGoalReached = false;
            _finishTheJobComplete = false;
            _bombFragmentsGoalReached = false;
            _bombFragmentsReceived = 0;
            _goalCompleted = false;
            _inGameScene = false;
            _checkFrameCounter = 0;
            MelonLogger.Msg("[Goal] Reset goal state");
        }

        /// <summary>
        /// Logs the current goal status.
        /// </summary>
        public static void LogStatus()
        {
            int goalType = NarcopelagoOptions.Goal;
            string goalDescription = goalType switch
            {
                GOAL_BOMB_FRAGMENTS_ONLY => "Bomb Fragments Only",
                GOAL_MISSIONS_ONLY => "Missions Only (Finish the Job)",
                GOAL_MISSIONS_NETWORTH => "Missions AND Networth",
                GOAL_MISSIONS_BOMB_FRAGMENTS => "Missions AND Bomb Fragments",
                GOAL_MISSIONS_NETWORTH_BOMB_FRAGMENTS => "Missions AND Networth AND Bomb Fragments",
                GOAL_BOMB_FRAGMENTS_NETWORTH => "Bomb Fragments AND Networth",
                _ => $"Unknown ({goalType})"
            };

            MelonLogger.Msg($"[Goal] === Goal Status ===");
            MelonLogger.Msg($"[Goal] Type: {goalDescription}");

            if (GoalRequiresNetworth(goalType))
            {
                MelonLogger.Msg($"[Goal] Required Networth: ${NarcopelagoOptions.Networth_amount_required:N0}");
                MelonLogger.Msg($"[Goal] Networth Reached: {_networthGoalReached}");
            }

            if (GoalRequiresMissions(goalType))
            {
                MelonLogger.Msg($"[Goal] Missions (Finish the Job) Complete: {_finishTheJobComplete}");
            }

            if (GoalRequiresBombFragments(goalType))
            {
                MelonLogger.Msg($"[Goal] Bomb Fragments: {_bombFragmentsReceived}/{NarcopelagoOptions.Number_of_bomb_fragments_required}");
                MelonLogger.Msg($"[Goal] Bomb Fragments Goal Reached: {_bombFragmentsGoalReached}");
            }

            MelonLogger.Msg($"[Goal] Goal Completed: {_goalCompleted}");
        }
    }
}
