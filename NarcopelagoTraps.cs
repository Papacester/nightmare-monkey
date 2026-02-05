using Il2CppFishNet.Object;
using Il2CppInterop.Runtime.Injection;
using Il2CppScheduleOne;
using Il2CppScheduleOne.Cartel;
using Il2CppScheduleOne.Combat;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Law;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.PlayerScripts.Health;
using Il2CppScheduleOne.Trash;
using MelonLoader;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Narcopelago
{
    public static class NarcopelagoTraps
    {
        private enum TrapType
        {
            Money,
            Heat,
            Slippery,
            Ambush,
            Trash,
            Pan,
            TimeScale,
            Sleep
        }

        private static readonly ConcurrentQueue<(TrapType trapType, string sourceItem)> _pendingTraps
            = new ConcurrentQueue<(TrapType, string)>();

        public static void OnTrapItemReceived(string itemName)
        {
            if (!TryGetTrapType(itemName, out var trapType))
            {
                MelonLogger.Warning($"[Traps] Item '{itemName}' is not recognized as a trap");
                return;
            }

            _pendingTraps.Enqueue((trapType, itemName));
        }

                public static bool IsTrapItem(string itemName)
        {
            return Data_Items.HasTag(itemName, "Trap");
        }
        
        private static bool TryGetTrapType(string itemName, out TrapType trapType)
        {
            if (itemName.Contains("Money"))
            {
                trapType = TrapType.Money;
                return true;
            }

            if (itemName.Contains("Heat"))
            {
                trapType = TrapType.Heat;
                return true;
            }

            if (itemName.Contains("Slippery"))
            {
                trapType = TrapType.Slippery;
                return true;
            }

            if (itemName.Contains("Ambush"))
            {
                trapType = TrapType.Ambush;
                return true;
            }

            if (itemName.Contains("Trash"))
            {
                trapType = TrapType.Trash;
                return true;
            }

            if (itemName.Contains("Pan"))
            {
                trapType = TrapType.Pan;
                return true;
            }

            if (itemName.Contains("TimeScale"))
            {
                trapType = TrapType.TimeScale;
                return true;
            }

            if (itemName.Contains("Sleep"))
            {
                trapType = TrapType.Sleep;
                return true;
            }

            trapType = default;
            return false;
        }

        public static void ProcessMainThreadQueue()
        {
            while (_pendingTraps.TryDequeue(out var trap))
            {
                switch (trap.trapType)
                {
                    case TrapType.Money:
                        ApplyMoneyTrap(trap.sourceItem);
                        break;

                    case TrapType.Heat:
                        ApplyHeatTrap(trap.sourceItem);
                        break;
                    
                    case TrapType.TimeScale:
                        ApplyTimeScaleTrap(trap.sourceItem);
                        break;
                    
                    case TrapType.Pan:
                        ApplyPanTrap(trap.sourceItem);
                        break;
                    
                    case TrapType.Slippery:
                        ApplySlipperyTrap(trap.sourceItem);
                        break;
                    
                    case TrapType.Trash:
                        ApplyTrashTrap(trap.sourceItem);
                        break;

                    case TrapType.Ambush:
                        //ApplyAmbushTrap(trap.sourceItem);
                        break;

                    case TrapType.Sleep:
                        ApplySleepTrap(trap.sourceItem);
                        break;
                        
                }
            }
        }

        private static void ApplyMoneyTrap(string sourceItem)
        {
            if (!NetworkSingleton<MoneyManager>.InstanceExists)
            {
                MelonLogger.Warning("[Traps] MoneyManager not available");
                return;
            }

            var mm = NetworkSingleton<MoneyManager>.Instance;

            float current = mm.onlineBalance;
            if (current <= 0f)
            {
                MelonLogger.Msg("[Traps] Money Trap triggered, but player has no cash");
                return;
            }

            float loss = Mathf.Floor(current * 0.10f);
            if (loss < 1f)
                loss = 1f;

            mm.CreateOnlineTransaction(
                "Archipelago Money Trap",
                -loss,
                1f,
                "Money Trap from Archipelago"
            );

            MelonLogger.Msg($"[Traps] Money Trap removed ${loss} (10% of ${current})");
        }
        private static void ApplyHeatTrap(string sourceItem)
        {
            // 1. Raise wanted level using the built‑in console command
            // 4 times to get to wanted dead or alive
            Il2CppScheduleOne.Console.SubmitCommand("raiseWanted");
            Il2CppScheduleOne.Console.SubmitCommand("raiseWanted");
            Il2CppScheduleOne.Console.SubmitCommand("raiseWanted");
            Il2CppScheduleOne.Console.SubmitCommand("raiseWanted");
            MelonLoader.MelonLogger.Msg("[Traps] Heat Trap: raiseWanted via Console.SubmitCommand");

            // 2. Force police response immediately
            var lawManager = LawManager.Instance;
            var player = Player.Local;

            if (lawManager != null && player != null)
            {
                var crime = new Assault();
                lawManager.PoliceCalled(player, crime);
                MelonLoader.MelonLogger.Msg("[Traps] Heat Trap: PoliceCalled() with Assault");
            }
            else
            {
                MelonLoader.MelonLogger.Warning("[Traps] LawManager or Player.Local missing");
            }
        }

        private static void ApplyTimeScaleTrap(string sourceItem)
        {
            MelonLoader.MelonCoroutines.Start(TimeScaleTrapRoutine());
        }

        private static System.Collections.IEnumerator TimeScaleTrapRoutine()
        {
            // Run immediately
            Il2CppScheduleOne.Console.SubmitCommand("settimescale 5");
            MelonLoader.MelonLogger.Msg("[Traps] TimeScale Trap: settimescale 5");

            // Wait 60 seconds
            // Its on speed up timer so thats why 300
            yield return new UnityEngine.WaitForSeconds(300f);

            // Run again after 30 seconds
            Il2CppScheduleOne.Console.SubmitCommand("settimescale 1");
            MelonLoader.MelonLogger.Msg("[Traps] TimeScale Trap: settimescale 1 (after 30 seconds)");
        }


        private static void ApplyPanTrap(string sourceItem)
        {
            Il2CppScheduleOne.Console.SubmitCommand("give fryingpan 8");
            MelonLoader.MelonLogger.Msg("[Traps] Pan trap filled inventory with pans");
        }

        private static void ApplySleepTrap(string sourceItem)
        {
            Il2CppScheduleOne.Console.SubmitCommand("forcesleep");
            MelonLoader.MelonLogger.Msg("[Traps] Pan trap filled inventory with pans");
        }

        public static void ApplySlipperyTrap(string sourceItem)
        {
            MelonCoroutines.Start(SlipperyRoutine());
        }

        private static IEnumerator SlipperyRoutine()
        {
            var player = Player.Local;
            if (player == null)
            {
                MelonLogger.Warning("[Trap] No player found.");
                yield break;
            }

            var health = player.GetComponentInChildren<PlayerHealth>();
            if (health != null && !health.IsAlive)
            {
                MelonLogger.Warning("[Trap] Player is dead, cannot apply Slippery.");
                yield break;
            }

            // Apply the effect
            player.Slippery = true;
            MelonLogger.Msg("[Trap] Slippery applied for 60 seconds.");

            // Wait 60 seconds
            yield return new WaitForSeconds(60f);

            // Clear the effect
            player.Slippery = false;
            MelonLogger.Msg("[Trap] Slippery cleared.");
        }

        public static void ApplyTrashTrap(string sourceItem)
        {
            MelonCoroutines.Start(TrashRainRoutine());
        }

        private static IEnumerator TrashRainRoutine()
        {
            var player = Player.Local;
            if (player == null)
            {
                MelonLogger.Warning("[Trap] No player found for Trash Rain.");
                yield break;
            }

            var trashManager = TrashManager.Instance;
            if (trashManager == null)
            {
                MelonLogger.Error("[Trap] TrashManager.Instance is null!");
                yield break;
            }

            MelonLogger.Msg("[Trap] Trash Rain started!");

            float duration = 5f;
            float interval = 0.1f;
            float timer = 0f;

            while (timer < duration)
            {
                SpawnTrashAbovePlayer(player, trashManager);
                timer += interval;
                yield return new WaitForSeconds(interval);
            }

            MelonLogger.Msg("[Trap] Trash Rain ended.");
        }

        private static void SpawnTrashAbovePlayer(Player player, TrashManager trashManager)
        {
            Vector3 pos = player.transform.position;

            float radius = 2.5f;
            float x = Random.Range(-radius, radius);
            float z = Random.Range(-radius, radius);

            Vector3 spawnPos = new Vector3(pos.x + x, pos.y + 1f, pos.z + z);

            // Optional downward velocity for extra "rain" effect
            Vector3 velocity = new Vector3(0f, -2f, 0f);

            trashManager.CreateTrashItem(
                "trashbag",          // ID
                spawnPos,            // position
                Quaternion.identity, // rotation
                velocity             // initial velocity
            );
        }

        /*
        public class DummyCombatTarget : Il2CppSystem.Object
        {
            public DummyCombatTarget() : base(ClassInjector.DerivedConstructorPointer<DummyCombatTarget>())
            {
                ClassInjector.DerivedConstructorBody(this);
            }

            public NetworkObject NetworkObject => null;
            public Vector3 CenterPoint => Vector3.zero;
            public Transform CenterPointTransform => null;
            public Vector3 LookAtPoint => Vector3.zero;
            public bool IsCurrentlyTargetable => true;
            public float RangedHitChanceMultiplier => 1f;
            public Vector3 Velocity => Vector3.zero;
            public bool IsPlayer => false;
            public Player AsPlayer => null;

            public void RecordLastKnownPosition(bool resetTimeSinceLastSeen) { }
            public float GetSearchTime() => 0f;
            public bool IsNull() => false;
        }

        
        public static void ApplyAmbushTrap(string sourceItem)
        {
            MelonCoroutines.Start(DoAmbush());
        }


        private static System.Collections.IEnumerator DoAmbush()
        {
            yield return null;

            var player = Player.Local;
            if (player == null)
            {
                MelonLogger.Error("[Trap] No player found.");
                yield break;
            }

            var pool = UnityEngine.Object.FindObjectOfType<GoonPool>();
            if (pool == null)
            {
                MelonLogger.Error("[Trap] No GoonPool found in scene.");
                yield break;
            }

            Vector3 basePos = player.transform.position + player.transform.forward * 6f;

            int goonCount = 4;
            var goons = pool.SpawnMultipleGoons(basePos, goonCount, setAsGoonMates: true);
            if (goons == null || goons.Count == 0)
            {
                MelonLogger.Error("[Trap] GoonPool failed to spawn goons.");
                yield break;
            }

            foreach (var goon in goons)
            {
                if (goon == null)
                    continue;

                // Force AI into combat mode
                ForceCombatState(goon);
            }

            MelonLogger.Msg($"[Trap] Spawned {goons.Count} cartel goons and forced hostility.");
        }
        private static void ForceCombatState(CartelGoon goon)
        {
            // Look for any AI controller on the goon
            foreach (var comp in goon.GetComponentsInChildren<Component>())
            {
                var typeName = comp.GetType().FullName;

                // These names vary by build, so we match loosely
                if (typeName.Contains("AI") ||
                    typeName.Contains("Combat") ||
                    typeName.Contains("Behaviour") ||
                    typeName.Contains("Brain"))
                {
                    // Try to set a "combat" or "hostile" flag if it exists
                    var fields = comp.GetType().GetFields(
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    foreach (var f in fields)
                    {
                        if (f.FieldType == typeof(bool) &&
                            (f.Name.ToLower().Contains("combat") ||
                             f.Name.ToLower().Contains("hostile") ||
                             f.Name.ToLower().Contains("aggro")))
                        {
                            f.SetValue(comp, true);
                        }
                    }
                }
            }
        }
        */

    }
}
