using MelonLoader;
using ScheduleOne.Casino;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;

namespace Archipelago
{
    [HarmonyPatch(typeof(SlotMachine), "GetWinAmount")]
    public class Patch_GetWinAmount
    {
        // Track which SlotMachines have recently logged
        static HashSet<int> recentlyLogged = new HashSet<int>();

        static bool Prefix(SlotMachine.EOutcome outcome, int betAmount, SlotMachine __instance, ref int __result)
        {
            int id = __instance.GetInstanceID();
            if (recentlyLogged.Contains(id))
                return false;

            switch (outcome)
            {
                case SlotMachine.EOutcome.Jackpot:
                    __result = betAmount * 100;
                    MelonLogger.Msg($"Jackpot! Payout: {__result}");
                    break;
                case SlotMachine.EOutcome.BigWin:
                    __result = betAmount * 25;
                    MelonLogger.Msg($"Big Win! Payout: {__result}");
                    break;
                case SlotMachine.EOutcome.SmallWin:
                    __result = betAmount * 10;
                    MelonLogger.Msg($"Small Win! Payout: {__result}");
                    break;
                case SlotMachine.EOutcome.MiniWin:
                    __result = betAmount * 2;
                    MelonLogger.Msg($"Mini Win! Payout: {__result}");
                    break;
                default:
                    __result = 0;
                    MelonLogger.Msg($"No win. Outcome: {outcome}");
                    break;
            }

            recentlyLogged.Add(id);
            ResetLogFlagAsync(id);
            return false;
        }

        static async void ResetLogFlagAsync(int id)
        {
            await Task.Delay(1000); // 1-second cooldown
            recentlyLogged.Remove(id);
        }
    }


    //BLACK JACK LISTENER
    [HarmonyPatch(typeof(BlackjackGameController), "GetPayout")]
    public class Patch_BlackJackListener
    {
        static HashSet<BlackjackGameController.EPayoutType> loggedPayouts = new HashSet<BlackjackGameController.EPayoutType>();

        static bool Prefix(float bet, BlackjackGameController.EPayoutType payout, ref float __result)
        {
            if (loggedPayouts.Contains(payout))
                return false;

            switch (payout)
            {
                case BlackjackGameController.EPayoutType.Blackjack:
                    __result = bet * 2.5f;
                    MelonLogger.Msg("Blackjack!");
                    break;
                case BlackjackGameController.EPayoutType.Win:
                    __result = bet * 2f;
                    MelonLogger.Msg("Win!");
                    break;
                case BlackjackGameController.EPayoutType.Push:
                    __result = bet;
                    MelonLogger.Msg("Push.");
                    break;
                default:
                    __result = 0f;
                    MelonLogger.Msg("No win.");
                    break;
            }

            loggedPayouts.Add(payout);
            ClearLoggedPayoutsAsync();
            return false;
        }

        static async void ClearLoggedPayoutsAsync()
        {
            await Task.Delay(1000); // Reset after 1 second
            loggedPayouts.Clear();
        }

    }
}
