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
    /// 0 = Networth goal only
    /// 1 = Networth AND Finish the Job quest complete
    /// 2 = Finish the Job quest complete only
    /// </summary>
    public static class NarcopelagoGoal
    {
        /// <summary>
        /// The location name for the final quest step.
        /// </summary>
        private const string FINISH_THE_JOB_LOCATION = "Finishing the Job| Wait for the bomb to detonate";

        /// <summary>
        /// Tracks if the networth goal has been reached.
        /// </summary>
        private static bool _networthGoalReached = false;

        /// <summary>
        /// Tracks if the Finish the Job quest has been completed.
        /// </summary>
        private static bool _finishTheJobComplete = false;

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

            // Check if networth goal needs to be checked
            int goalType = NarcopelagoOptions.Goal;
            if (goalType == 0 || goalType == 1)
            {
                CheckNetworthGoal();
            }

            // Check overall goal completion
            CheckGoalCompletion();
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
                MelonLogger.Msg("[Goal] Finish the Job quest completed!");
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
                case 0: // Networth only
                    goalMet = _networthGoalReached;
                    break;

                case 1: // Networth AND Finish the Job
                    goalMet = _networthGoalReached && _finishTheJobComplete;
                    break;

                case 2: // Finish the Job only
                    goalMet = _finishTheJobComplete;
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
                0 => "Networth Only",
                1 => "Networth AND Finish the Job",
                2 => "Finish the Job Only",
                _ => $"Unknown ({goalType})"
            };

            MelonLogger.Msg($"[Goal] === Goal Status ===");
            MelonLogger.Msg($"[Goal] Type: {goalDescription}");
            MelonLogger.Msg($"[Goal] Required Networth: ${NarcopelagoOptions.Networth_amount_required:N0}");
            MelonLogger.Msg($"[Goal] Networth Reached: {_networthGoalReached}");
            MelonLogger.Msg($"[Goal] Finish the Job Complete: {_finishTheJobComplete}");
            MelonLogger.Msg($"[Goal] Goal Completed: {_goalCompleted}");
        }
    }
}
