using Archipelago.MultiClient.Net;
using Archipelago.MultiClient.Net.BounceFeatures.DeathLink;
using Archipelago.MultiClient.Net.Helpers;
using Archipelago.MultiClient.Net.MessageLog.Messages;
using Archipelago.MultiClient.Net.MessageLog.Parts;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Phone.Messages;
using MelonLoader;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Narcopelago
{
    /// <summary>
    /// Manages Archipelago-related phone contacts and text messaging in Schedule I.
    /// Provides two phone contacts that send text messages:
    /// 1. "Archipelago" - Receives ALL item sent messages and deathlinks from the multiworld
    /// 2. "AP (You)" - Receives only messages mentioning our player name and deathlinks
    /// 
    /// These contacts appear in the phone's Messages app and send actual text messages.
    /// </summary>
    public static class NarcopelagoAPContacts
    {
        // Contact name constants
        private const string CONTACT_ALL = "Archipelago";
        private const string CONTACT_FILTERED = "AP (You)";

        /// <summary>
        /// Queue of messages to send via phone text.
        /// Tuple: (contactName, message)
        /// </summary>
        private static ConcurrentQueue<(string contactName, string message)> _messageQueue = 
            new ConcurrentQueue<(string, string)>();

        /// <summary>
        /// Tracks if we're in a game scene where messaging is available.
        /// </summary>
        private static bool _inGameScene = false;

        /// <summary>
        /// Tracks if we've subscribed to Archipelago events.
        /// </summary>
        private static bool _eventsSubscribed = false;

        /// <summary>
        /// Tracks if contacts have been initialized.
        /// </summary>
        private static bool _contactsInitialized = false;

        /// <summary>
        /// Delay frames before trying to initialize contacts (allows game to fully load).
        /// </summary>
        private static int _initDelayFrames = 0;

        /// <summary>
        /// Flag indicating we're waiting to initialize.
        /// </summary>
        private static bool _initPending = false;

        /// <summary>
        /// The MSGConversation for the "Archipelago" contact (all messages).
        /// </summary>
        private static MSGConversation _allMessagesConversation = null;

        /// <summary>
        /// The MSGConversation for the "AP (You)" contact (filtered messages).
        /// </summary>
        private static MSGConversation _filteredMessagesConversation = null;

        /// <summary>
        /// Reference to the NPC we use as a fake sender for our custom contacts.
        /// We borrow an existing NPC (like Uncle Nelson) to satisfy MSGConversation requirements.
        /// </summary>
        private static NPC _fakeContactNPC = null;

        /// <summary>
        /// History of all Archipelago messages received.
        /// </summary>
        private static List<string> _allMessagesHistory = new List<string>();

        /// <summary>
        /// History of filtered messages (player-specific + deathlinks).
        /// </summary>
        private static List<string> _filteredMessagesHistory = new List<string>();

        /// <summary>
        /// Maximum number of messages to keep in history.
        /// </summary>
        private const int MAX_HISTORY = 100;

        /// <summary>
        /// Sets whether we're in a game scene.
        /// </summary>
        public static void SetInGameScene(bool inGame)
        {
            _inGameScene = inGame;
            if (inGame)
            {
                MelonLogger.Msg("[APContacts] Entered game scene - queueing contact initialization");
                // Queue initialization with a delay to let the game fully load
                _initPending = true;
                _initDelayFrames = 180; // ~3 seconds at 60fps
            }
            else
            {
                // Leaving game scene - clear contacts
                _contactsInitialized = false;
                _allMessagesConversation = null;
                _filteredMessagesConversation = null;
                _fakeContactNPC = null;
            }
        }

        /// <summary>
        /// Subscribe to Archipelago events after successful connection.
        /// </summary>
        public static void SubscribeToEvents(ArchipelagoSession session)
        {
            if (session == null)
            {
                MelonLogger.Warning("[APContacts] Cannot subscribe - session is null");
                return;
            }

            if (_eventsSubscribed)
            {
                MelonLogger.Msg("[APContacts] Already subscribed to events");
                return;
            }

            try
            {
                // Subscribe to the MessageLog which receives all server messages
                session.MessageLog.OnMessageReceived += OnArchipelagoMessageReceived;
                _eventsSubscribed = true;
                MelonLogger.Msg("[APContacts] Subscribed to Archipelago message events");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[APContacts] Failed to subscribe to events: {ex.Message}");
            }
        }

        /// <summary>
        /// Unsubscribe from Archipelago events.
        /// </summary>
        public static void UnsubscribeFromEvents(ArchipelagoSession session)
        {
            if (session == null || !_eventsSubscribed)
                return;

            try
            {
                session.MessageLog.OnMessageReceived -= OnArchipelagoMessageReceived;
                _eventsSubscribed = false;
                MelonLogger.Msg("[APContacts] Unsubscribed from Archipelago message events");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[APContacts] Failed to unsubscribe from events: {ex.Message}");
            }
        }

        /// <summary>
        /// Called when an Archipelago message is received.
        /// </summary>
        private static void OnArchipelagoMessageReceived(LogMessage message)
        {
            try
            {
                // Convert the message to a readable string
                string messageText = GetMessageText(message);
                if (string.IsNullOrEmpty(messageText))
                    return;

                // Check if this is an item-related message
                bool isItemMessage = message is ItemSendLogMessage || message is ItemCheatLogMessage;
                bool isHintMessage = message is HintItemSendLogMessage;
                
                // We want item sent messages - these show when anyone sends/receives items
                if (!isItemMessage && !isHintMessage)
                    return;

                // Add to all messages history
                AddToHistory(_allMessagesHistory, messageText);

                // Queue message for "Archipelago" contact (all messages)
                _messageQueue.Enqueue((CONTACT_ALL, messageText));

                // Check if this message mentions our player name
                string playerName = ConnectionHandler.LastSlotName ?? "";
                if (!string.IsNullOrEmpty(playerName))
                {
                    if (messageText.Contains(playerName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Add to filtered history
                        AddToHistory(_filteredMessagesHistory, messageText);
                        
                        // Queue message for "AP (You)" contact
                        _messageQueue.Enqueue((CONTACT_FILTERED, messageText));
                    }
                }

                MelonLogger.Msg($"[APContacts] Received: {messageText}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[APContacts] Error processing message: {ex.Message}");
            }
        }

        /// <summary>
        /// Converts a LogMessage to a readable string.
        /// </summary>
        private static string GetMessageText(LogMessage message)
        {
            try
            {
                // Build the message from its parts
                StringBuilder sb = new StringBuilder();
                foreach (var part in message.Parts)
                {
                    sb.Append(part.Text);
                }
                return sb.ToString();
            }
            catch
            {
                return message?.ToString() ?? "";
            }
        }

        /// <summary>
        /// Adds a message to a history list, maintaining max size.
        /// </summary>
        private static void AddToHistory(List<string> history, string message)
        {
            history.Add(message);
            while (history.Count > MAX_HISTORY)
            {
                history.RemoveAt(0);
            }
        }

        /// <summary>
        /// Queues a deathlink message for both contacts.
        /// Called from NarcopelagoDeathLink when a deathlink is received.
        /// </summary>
        public static void OnDeathLinkReceived(string source, string cause)
        {
            string message = $"☠️ {source} {cause}";
            
            // Add to histories
            AddToHistory(_allMessagesHistory, message);
            AddToHistory(_filteredMessagesHistory, message);
            
            // DeathLinks go to both contacts
            _messageQueue.Enqueue((CONTACT_ALL, message));
            _messageQueue.Enqueue((CONTACT_FILTERED, message));
            
            MelonLogger.Msg($"[APContacts] DeathLink received: {message}");
        }

        /// <summary>
        /// Queues a deathlink sent message for both contacts.
        /// Called from NarcopelagoDeathLink when we send a deathlink.
        /// </summary>
        public static void OnDeathLinkSent(string playerName, string cause)
        {
            string message = $"☠️ You {cause}";
            
            // Add to histories
            AddToHistory(_allMessagesHistory, message);
            AddToHistory(_filteredMessagesHistory, message);
            
            // DeathLinks go to both contacts
            _messageQueue.Enqueue((CONTACT_ALL, message));
            _messageQueue.Enqueue((CONTACT_FILTERED, message));
            
            MelonLogger.Msg($"[APContacts] DeathLink sent: {message}");
        }

        /// <summary>
        /// Process queued messages on the main thread.
        /// Call this from Core.OnUpdate().
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            if (!_inGameScene)
                return;

            // Process pending initialization
            ProcessPendingInit();

            // If contacts aren't initialized, use fallback notification system
            if (!_contactsInitialized)
            {
                ProcessMessagesAsFallbackNotifications();
                return;
            }

            // Process up to 3 messages per frame to avoid spamming
            int processed = 0;

            while (processed < 3 && _messageQueue.TryDequeue(out var msg))
            {
                try
                {
                    SendTextMessage(msg.contactName, msg.message);
                    processed++;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[APContacts] Error sending text message: {ex.Message}");
                    // Fall back to notification
                    SendFallbackNotification(msg.contactName, msg.message);
                }
            }
        }

        /// <summary>
        /// Processes pending contact initialization.
        /// </summary>
        private static void ProcessPendingInit()
        {
            if (!_initPending)
                return;

            if (_initDelayFrames > 0)
            {
                _initDelayFrames--;
                return;
            }

            _initPending = false;
            InitializeContacts();
        }

        /// <summary>
        /// Initializes the AP phone contacts by creating MSGConversation instances.
        /// Uses an existing NPC as a "fake" sender for the conversations.
        /// </summary>
        private static void InitializeContacts()
        {
            if (_contactsInitialized)
                return;

            try
            {
                MelonLogger.Msg("[APContacts] Initializing phone contacts...");

                // Check if MessagesApp is available - required for creating conversation UI
                if (!PlayerSingleton<MessagesApp>.InstanceExists)
                {
                    MelonLogger.Warning("[APContacts] MessagesApp not available yet - will retry");
                    // Retry in a few frames
                    _initPending = true;
                    _initDelayFrames = 60;
                    return;
                }

                var messagesApp = PlayerSingleton<MessagesApp>.Instance;
                if (messagesApp == null)
                {
                    MelonLogger.Warning("[APContacts] MessagesApp instance is null - will retry");
                    _initPending = true;
                    _initDelayFrames = 60;
                    return;
                }

                // Check if MessagingManager is available - required for registering conversations
                if (!NetworkSingleton<MessagingManager>.InstanceExists)
                {
                    MelonLogger.Warning("[APContacts] MessagingManager not available yet - will retry");
                    _initPending = true;
                    _initDelayFrames = 60;
                    return;
                }

                // Find an NPC to use as our "fake" sender
                // We'll try to find Uncle Nelson first, then fall back to any available NPC
                _fakeContactNPC = FindFakeContactNPC();

                if (_fakeContactNPC == null)
                {
                    MelonLogger.Warning("[APContacts] Could not find an NPC to use for contacts - using notification fallback");
                    return;
                }

                MelonLogger.Msg($"[APContacts] Using NPC '{_fakeContactNPC.fullName}' as base for AP contacts");

                // Create the "Archipelago" contact conversation
                _allMessagesConversation = CreateAPConversation(_fakeContactNPC, CONTACT_ALL);
                
                // Create the "AP (You)" contact conversation  
                _filteredMessagesConversation = CreateAPConversation(_fakeContactNPC, CONTACT_FILTERED);

                if (_allMessagesConversation != null && _filteredMessagesConversation != null)
                {
                    _contactsInitialized = true;
                    MelonLogger.Msg("[APContacts] Phone contacts initialized successfully!");
                    MelonLogger.Msg($"[APContacts] MessagesApp.Conversations count: {MessagesApp.Conversations.Count}");
                    MelonLogger.Msg($"[APContacts] MessagesApp.ActiveConversations count: {MessagesApp.ActiveConversations.Count}");
                }
                else
                {
                    MelonLogger.Warning("[APContacts] Failed to create one or more conversations - using notification fallback");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[APContacts] Failed to initialize contacts: {ex.Message}");
                MelonLogger.Error($"[APContacts] Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Finds an NPC to use as the fake sender for our AP contacts.
        /// Prefers Uncle Nelson but will use any available NPC.
        /// </summary>
        private static NPC FindFakeContactNPC()
        {
            try
            {
                // Try to find Uncle Nelson first (he's a good choice as he's not interactive)
                var uncleNelson = NPCManager.GetNPC("unclenelson");
                if (uncleNelson != null)
                {
                    MelonLogger.Msg("[APContacts] Found Uncle Nelson for contact base");
                    return uncleNelson;
                }

                // Try common NPC IDs
                string[] commonNPCs = { "ming", "ray", "benji", "molly", "dean" };
                foreach (var npcId in commonNPCs)
                {
                    var npc = NPCManager.GetNPC(npcId);
                    if (npc != null)
                    {
                        MelonLogger.Msg($"[APContacts] Found '{npcId}' for contact base");
                        return npc;
                    }
                }

                // Fall back to any NPC in the registry
                if (NPCManager.NPCRegistry != null && NPCManager.NPCRegistry.Count > 0)
                {
                    foreach (var npc in NPCManager.NPCRegistry)
                    {
                        if (npc != null)
                        {
                            return npc;
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[APContacts] Error finding fake contact NPC: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Creates a custom MSGConversation for an AP contact.
        /// </summary>
        private static MSGConversation CreateAPConversation(NPC npc, string contactName)
        {
            try
            {
                MelonLogger.Msg($"[APContacts] Creating conversation for '{contactName}'...");
                
                // Create a new MSGConversation with our custom contact name
                // Note: This registers with MessagingManager using the NPC, but uses our custom name
                var conversation = new MSGConversation(npc, contactName);
                
                MelonLogger.Msg($"[APContacts] Conversation created, ensuring UI exists...");
                
                // Ensure the UI is created immediately so messages can be displayed
                // This creates the entry in the Messages app list
                conversation.EnsureUIExists();
                
                MelonLogger.Msg($"[APContacts] UI created, configuring conversation...");
                
                // Configure the conversation
                conversation.SetIsKnown(true); // Always show the contact name
                
                // Set empty categories (not a customer/supplier/dealer)
                conversation.SetCategories(new Il2CppSystem.Collections.Generic.List<EConversationCategory>());

                MelonLogger.Msg($"[APContacts] Conversation for '{contactName}' created successfully");
                return conversation;
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[APContacts] Failed to create conversation for '{contactName}': {ex.Message}");
                MelonLogger.Error($"[APContacts] Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Sends a text message to the specified contact.
        /// </summary>
        private static void SendTextMessage(string contactName, string messageText)
        {
            MSGConversation conversation = null;

            if (contactName == CONTACT_ALL)
            {
                conversation = _allMessagesConversation;
            }
            else if (contactName == CONTACT_FILTERED)
            {
                conversation = _filteredMessagesConversation;
            }

            if (conversation == null)
            {
                MelonLogger.Warning($"[APContacts] No conversation found for '{contactName}' - using notification fallback");
                SendFallbackNotification(contactName, messageText);
                return;
            }

            try
            {
                // Ensure UI exists before sending (creates the visual elements if needed)
                conversation.EnsureUIExists();
                
                // Create a message from the "Other" (NPC/contact) side
                var message = new Message(messageText, Message.ESenderType.Other, true, UnityEngine.Random.Range(int.MinValue, int.MaxValue));
                
                // Send without network sync since these are local-only AP messages
                // notify: true to show notification popup
                // network: false because this is local only
                conversation.SendMessage(message, true, false);
                
                // Log success with truncated message
                string truncatedMsg = messageText.Length > 50 ? messageText.Substring(0, 50) + "..." : messageText;
                MelonLogger.Msg($"[APContacts] Sent text to '{contactName}': {truncatedMsg}");
                MelonLogger.Msg($"[APContacts] Conversation messageHistory count: {conversation.messageHistory.Count}");
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[APContacts] Failed to send text to '{contactName}': {ex.Message}");
                MelonLogger.Error($"[APContacts] Stack trace: {ex.StackTrace}");
                SendFallbackNotification(contactName, messageText);
            }
        }

        /// <summary>
        /// Processes messages as fallback notifications when contacts aren't initialized.
        /// </summary>
        private static void ProcessMessagesAsFallbackNotifications()
        {
            int processed = 0;
            while (processed < 3 && _messageQueue.TryDequeue(out var msg))
            {
                SendFallbackNotification(msg.contactName, msg.message);
                processed++;
            }
        }

        /// <summary>
        /// Sends a fallback notification when text messaging isn't available.
        /// </summary>
        private static void SendFallbackNotification(string title, string message)
        {
            try
            {
                if (!Singleton<NotificationsManager>.InstanceExists)
                    return;

                var notifManager = Singleton<NotificationsManager>.Instance;
                if (notifManager == null)
                    return;

                notifManager.SendNotification(title, message, null, 5f, true);
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[APContacts] Failed to send fallback notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the history of all Archipelago messages.
        /// </summary>
        public static IReadOnlyList<string> GetAllMessagesHistory() => _allMessagesHistory;

        /// <summary>
        /// Gets the history of filtered messages (player-specific + deathlinks).
        /// </summary>
        public static IReadOnlyList<string> GetFilteredMessagesHistory() => _filteredMessagesHistory;

        /// <summary>
        /// Resets the AP contacts state.
        /// </summary>
        public static void Reset()
        {
            _eventsSubscribed = false;
            _inGameScene = false;
            _contactsInitialized = false;
            _initPending = false;
            _allMessagesConversation = null;
            _filteredMessagesConversation = null;
            _fakeContactNPC = null;
            _allMessagesHistory.Clear();
            _filteredMessagesHistory.Clear();
            
            // Clear queue
            while (_messageQueue.TryDequeue(out _)) { }
        }
    }
}
