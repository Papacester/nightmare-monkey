using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.Enums;
using HarmonyLib;
using JetBrains.Annotations;
using MelonLoader;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

[assembly: MelonInfo(typeof(Narcopelago.Core), "Narcopelago", "0.2.0", "Mack", null)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace Narcopelago
{
    public class Core : MelonMod
    {

        public override void OnInitializeMelon()
        {
            LoggerInstance.Msg("Narcopelago v" + Info.Version + " loaded!");
            var session = ArchipelagoSessionFactory.CreateSession("localhost", 38281);
            session.ConnectAsync().GetAwaiter().GetResult();
            LoginResult result = session.LoginAsync("Schedule I", "Narcopelago", ItemsHandlingFlags.AllItems).GetAwaiter().GetResult();
            
            if (result.Successful)
            {
                LoggerInstance.Msg("Connected to Archipelago!");
            }
            else
            {
                LoginFailure failure = (LoginFailure)result;
                LoggerInstance.Error("Failed to connect: " + string.Join(", ", failure.Errors));
            }
        }

    }
}