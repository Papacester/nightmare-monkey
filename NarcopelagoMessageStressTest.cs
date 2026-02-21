using MelonLoader;
using System;
using System.Threading;
using UnityEngine;

namespace Narcopelago
{
    /// <summary>
    /// Debug stress test for NarcopelagoAPContacts message handling.
    /// Press '-' (Minus key) to start/stop simulating ~10,000 messages per second
    /// arriving from a background thread, just like real Archipelago server callbacks.
    /// </summary>
    public static class NarcopelagoMessageStressTest
    {
        private static bool _running = false;
        private static Thread _spamThread = null;
        private static int _totalSent = 0;

        /// <summary>
        /// Call from Core.OnUpdate() to check for the toggle keybind.
        /// </summary>
        public static void Update()
        {
            if (Input.GetKeyDown(KeyCode.Minus))
            {
                if (_running)
                    Stop();
                else
                    Start();
            }
        }

        private static void Start()
        {
            if (_running) return;

            _running = true;
            _totalSent = 0;

            MelonLogger.Msg("[StressTest] STARTED - simulating ~10,000 AP messages/sec (press '-' to stop)");

            _spamThread = new Thread(SpamLoop)
            {
                IsBackground = true,
                Name = "APMessageStressTest"
            };
            _spamThread.Start();
        }

        private static void Stop()
        {
            _running = false;
            MelonLogger.Msg($"[StressTest] STOPPED - sent {_totalSent} total messages");
        }

        /// <summary>
        /// Background thread that enqueues messages directly into APContacts' queue,
        /// mimicking how the real Archipelago ItemReceived/MessageLog callbacks work.
        /// Produces ~10,000 messages per second in bursts.
        /// </summary>
        private static void SpamLoop()
        {
            string[] fakePlayerNames = { "Alice", "Bob", "Charlie", "Diana", "Eve", "Frank" };
            string[] fakeItemNames = { "Gasoline", "Fertilizer", "OG Kush Seed", "Mixing Station", "Pseudo", "Jar", "Banana", "Cash Bundle" };
            string[] fakeGameNames = { "Schedule I", "Hollow Knight", "Celeste", "Factorio", "Stardew Valley", "Terraria" };

            string ourName = ConnectionHandler.LastSlotName ?? "Player1";
            var rng = new System.Random();

            // Target: 10,000 msgs/sec ? 100 msgs every 10ms
            const int batchSize = 100;
            const int sleepMs = 10;

            while (_running)
            {
                for (int i = 0; i < batchSize && _running; i++)
                {
                    string sender = fakePlayerNames[rng.Next(fakePlayerNames.Length)];
                    string item = fakeItemNames[rng.Next(fakeItemNames.Length)];
                    string game = fakeGameNames[rng.Next(fakeGameNames.Length)];

                    // ~10% of messages mention our player name to test AP (You) filtering
                    bool mentionsUs = rng.Next(10) == 0;
                    string receiver = mentionsUs ? ourName : fakePlayerNames[rng.Next(fakePlayerNames.Length)];

                    string messageText = $"{sender} ({game}) sent {item} to {receiver} ({game})";

                    // Simulate the same routing that OnArchipelagoMessageReceived does
                    bool mentionsPlayer = !string.IsNullOrEmpty(ourName) &&
                                          messageText.Contains(ourName, StringComparison.OrdinalIgnoreCase);

                    if (mentionsPlayer)
                    {
                        NarcopelagoAPContacts.EnqueueMessage("AP (You)", messageText);
                    }
                    else
                    {
                        if (!NarcopelagoAPContacts.MuteAllMessages)
                        {
                            NarcopelagoAPContacts.EnqueueMessage("Archipelago", messageText);
                        }
                    }

                    Interlocked.Increment(ref _totalSent);
                }

                Thread.Sleep(sleepMs);
            }
        }
    }
}
