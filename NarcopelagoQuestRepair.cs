using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Quests;
using MelonLoader;
using System;
using System.Collections.Generic;

namespace Narcopelago
{
    /// <summary>
    /// Watchdog that repairs quests which the game has left in an unprogressable state.
    ///
    /// Why this exists:
    /// Quest entries are driven by one-shot notifications (the static <c>Customer.onCustomerUnlocked</c>
    /// action, dialogue callbacks, variable-change events). Archipelago grants unlocks on its own
    /// schedule, so a grant can fire while the quest entry that was listening for it is still Inactive.
    /// The entry then activates against a world that already satisfies it, and nothing ever re-notifies
    /// it. "Dodgy Dealing" entry 1 ("Unlock Chloe, Ludwig, or Beth as customers") is the known case;
    /// the manual fix is <c>setquestentrystate "Dodgy Dealing" 1 2</c>. This class does that automatically.
    ///
    /// SAFETY MODEL - a false positive here is as bad as the bug, so every repair is heavily gated:
    ///  - Only quests with State == Active are ever considered. Completed/Failed/Expired/Cancelled
    ///    quests are skipped entirely.
    ///  - Only two transitions are ever written: Inactive -> Active and Active -> Completed.
    ///    <see cref="IsForwardTransition"/> is the single choke point and refuses anything else,
    ///    so a Completed entry can never be reset.
    ///  - Contract and DeaddropQuest (runtime-generated customer contracts / dead drops) are excluded.
    ///    Auto-completing one of those would hand the player a free delivery.
    ///  - Nothing runs until the scene has been loaded for ~30s, so save loading and the mod's own
    ///    item sync have finished settling.
    ///  - Every candidate must be observed on N *consecutive* scans before it is repaired. A quest
    ///    mid-transition (entry N completed, entry N+1 not started yet) clears its counter on the very
    ///    next scan and is never touched.
    ///  - Per-quest and per-session repair caps stop runaway behaviour if something fights us.
    /// </summary>
    public static class NarcopelagoQuestRepair
    {
        #region Tuning

        /// <summary>Frames between scans (~5s at 60fps).</summary>
        private const int CHECK_INTERVAL_FRAMES = 300;

        /// <summary>Frames after entering a game scene before the watchdog may act (~30s at 60fps).</summary>
        private const int SCENE_GRACE_FRAMES = 1800;

        /// <summary>
        /// Consecutive scans an Active-but-already-satisfied entry must be seen before completing it
        /// (~30s). Well evidenced - the game's own conditions say it is done - so a short window is fine.
        /// </summary>
        private const int CONFIRMATIONS_STALLED_ENTRY = 6;

        /// <summary>
        /// Consecutive scans an Active quest must have no Active entry before we start one (~2 minutes).
        /// Deliberately much longer: a linear quest can briefly have no active entry while one entry
        /// completes and the next starts, and some quests legitimately wait on an external trigger.
        /// Two minutes of nothing is a stall, not a transition.
        /// </summary>
        private const int CONFIRMATIONS_NO_ACTIVE_ENTRY = 24;

        /// <summary>Give up on a quest after this many repairs in one session (something else is fighting us).</summary>
        private const int MAX_REPAIRS_PER_QUEST = 3;

        /// <summary>Global session cap, as a last-resort circuit breaker.</summary>
        private const int MAX_REPAIRS_PER_SESSION = 25;

        #endregion

        #region State

        private static bool _inGameScene;
        private static int _frameCounter;
        private static int _graceFrames;
        private static int _repairsThisSession;

        /// <summary>Consecutive-observation counters, keyed by <see cref="CandidateKey"/>.</summary>
        private static readonly Dictionary<string, int> _confirmations = new Dictionary<string, int>();

        /// <summary>Repairs applied per quest GUID this session.</summary>
        private static readonly Dictionary<string, int> _repairsPerQuest = new Dictionary<string, int>();

        /// <summary>Candidate keys seen during the current scan, used to clear stale counters.</summary>
        private static readonly HashSet<string> _seenThisScan = new HashSet<string>();

        /// <summary>
        /// Titles of the authored main-story quests, derived from the "Mission"-tagged
        /// "&lt;Quest Title&gt;|&lt;Entry Title&gt;" locations. Built lazily on first scan.
        /// </summary>
        private static HashSet<string> _knownMainQuests;

        #endregion

        #region Rule table

        /// <summary>
        /// A quest entry whose completion is driven by a C# event rather than by
        /// <see cref="QuestEntry.AutoCompleteConditions"/>, so the generic condition re-check cannot
        /// see it. The predicate must be a faithful restatement of the entry's own wording - never
        /// looser - because it is the only evidence the repair has.
        /// </summary>
        private sealed class EntryRule
        {
            public readonly string QuestTitle;
            public readonly string EntryTitle;
            public readonly Func<bool> IsAlreadySatisfied;

            public EntryRule(string questTitle, string entryTitle, Func<bool> isAlreadySatisfied)
            {
                QuestTitle = questTitle;
                EntryTitle = entryTitle;
                IsAlreadySatisfied = isAlreadySatisfied;
            }
        }

        private static readonly EntryRule[] _rules = new EntryRule[]
        {
            // The known Dodgy Dealing stall. Satisfied the moment any one of the three is unlocked.
            new EntryRule(
                "Dodgy Dealing",
                "Unlock Chloe, Ludwig, or Beth as customers",
                () => NarcopelagoCustomers.IsCustomerUnlockedInGame("Chloe Bowers")
                   || NarcopelagoCustomers.IsCustomerUnlockedInGame("Ludwig Meyer")
                   || NarcopelagoCustomers.IsCustomerUnlockedInGame("Beth Penn")),

            // Same failure mode: the counter is fed by Customer.onCustomerUnlocked, which we suppress
            // for sample-based unlocks when Randomize_customers is on.
            new EntryRule(
                "Moving Up",
                "Unlock 10 customers",
                () => Customer.UnlockedCustomers != null && Customer.UnlockedCustomers.Count >= 10),

            // Shirley Watts is the Westville pseudo supplier (see items.json, Supplier tag).
            new EntryRule(
                "We Need To Cook",
                "Unlock the pseudo supplier in Westville",
                () => NarcopelagoSuppliers.IsSupplierUnlockedInGame("Shirley Watts")),
        };

        #endregion

        #region Lifecycle

        /// <summary>
        /// Sets whether we are in a game scene. Call from Core when the scene changes.
        /// Entering a scene restarts the grace period and clears all debounce state.
        /// </summary>
        public static void SetInGameScene(bool inGame)
        {
            _inGameScene = inGame;
            _frameCounter = 0;
            _confirmations.Clear();

            if (inGame)
            {
                _graceFrames = SCENE_GRACE_FRAMES;
                MelonLogger.Msg("[QuestRepair] Entered game scene - watchdog armed after grace period");
            }
        }

        /// <summary>Resets all session tracking. Call on disconnect / returning to menu.</summary>
        public static void Reset()
        {
            _inGameScene = false;
            _frameCounter = 0;
            _graceFrames = 0;
            _repairsThisSession = 0;
            _confirmations.Clear();
            _repairsPerQuest.Clear();
        }

        #endregion

        /// <summary>
        /// Called from Core.OnUpdate. Scans active quests on a fixed interval and repairs any that
        /// have been confirmably stuck for long enough.
        /// </summary>
        public static void ProcessMainThreadQueue()
        {
            if (!_inGameScene) return;

            // Let the save finish loading and the mod's own item sync settle before touching anything.
            if (_graceFrames > 0)
            {
                _graceFrames--;
                return;
            }

            // Only interfere in an Archipelago run - the stall is caused by AP grant timing.
            if (!NarcopelagoOptions.IsLoaded) return;

            if (_repairsThisSession >= MAX_REPAIRS_PER_SESSION) return;

            _frameCounter++;
            if (_frameCounter < CHECK_INTERVAL_FRAMES) return;
            _frameCounter = 0;

            try
            {
                Scan();
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[QuestRepair] Error during scan: {ex.Message}");
            }
        }

        private static void Scan()
        {
            var quests = SnapshotQuests();
            if (quests == null) return;

            _seenThisScan.Clear();

            foreach (var quest in quests)
            {
                try
                {
                    InspectQuest(quest);
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[QuestRepair] Error inspecting quest: {ex.Message}");
                }
            }

            // Any candidate not observed this scan was transient - drop its counter so it has to
            // build up confirmations from scratch if it reappears.
            PruneUnseenCandidates();
        }

        /// <summary>
        /// Copies Quest.Quests into a managed array. Repairs mutate quest state, which mutates
        /// Quest.ActiveQuests, so we must not iterate a live IL2CPP list. We read the full registry
        /// rather than ActiveQuests and filter on State ourselves, so the watchdog does not depend on
        /// how the game maintains that secondary list.
        /// </summary>
        private static Quest[] SnapshotQuests()
        {
            var all = Quest.Quests;
            if (all == null) return null;

            var result = new Quest[all.Count];
            for (int i = 0; i < all.Count; i++)
            {
                result[i] = all[i];
            }
            return result;
        }

        private static void InspectQuest(Quest quest)
        {
            if (quest == null) return;

            // Only ever consider quests the game itself considers in progress.
            if (quest.State != EQuestState.Active) return;

            // Contracts and dead drops are generated at runtime with their own lifecycles.
            // Completing one of those entries would be a gameplay exploit, not a repair.
            if (quest.TryCast<Contract>() != null) return;
            if (quest.TryCast<DeaddropQuest>() != null) return;

            var entries = quest.Entries;
            if (entries == null || entries.Count == 0) return;

            string questGuid = SafeGuid(quest);
            if (string.IsNullOrEmpty(questGuid)) return;

            if (GetRepairCount(questGuid) >= MAX_REPAIRS_PER_QUEST) return;

            string questTitle = SafeTitle(quest);

            // ---- Pass 1: entries that are Active but whose completion trigger has already passed ----
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null) continue;
                if (entry.State != EQuestState.Active) continue;

                string reason = GetStalledEntryReason(quest, entry);
                if (reason == null) continue;

                string entryTitle = SafeTitle(entry);
                string key = CandidateKey(questGuid, entry.QuestEntryIndex, "stalled");
                _seenThisScan.Add(key);

                if (Confirm(key, questTitle, entryTitle, CONFIRMATIONS_STALLED_ENTRY, reason) < CONFIRMATIONS_STALLED_ENTRY)
                    continue;

                ApplyRepair(quest, questTitle, questGuid, entry.QuestEntryIndex,
                            entryTitle, entry.State, EQuestState.Completed, reason);
                return; // one repair per quest per scan; re-evaluate the new state next time
            }

            // ---- Pass 2: quest is Active but has nothing for the player to do ----
            InspectForNoActiveEntry(quest, questTitle, questGuid, entries);
        }

        /// <summary>
        /// Returns a human-readable reason if this Active entry should already have completed,
        /// or null if there is no evidence it is stuck.
        /// </summary>
        private static string GetStalledEntryReason(Quest quest, QuestEntry entry)
        {
            // Case A: the entry declares itself auto-completing and the game's own conditions pass.
            // If AutoComplete is true and Evaluate() is true, the game intended to complete it.
            if (entry.AutoComplete)
            {
                var conditions = entry.AutoCompleteConditions;
                if (conditions != null)
                {
                    int conditionCount = 0;
                    if (conditions.ConditionList != null) conditionCount += conditions.ConditionList.Length;
                    if (conditions.QuestConditionList != null) conditionCount += conditions.QuestConditionList.Length;

                    // An empty Conditions object evaluates true vacuously - never act on that.
                    if (conditionCount > 0 && conditions.Evaluate())
                    {
                        return $"AutoComplete conditions ({conditionCount}) already evaluate true";
                    }
                }
            }

            // Case B: an entry driven by a C# event we know Archipelago can fire early.
            var rule = FindRule(quest, entry);
            if (rule != null && rule.IsAlreadySatisfied())
            {
                return $"rule '{rule.QuestTitle}|{rule.EntryTitle}' is already satisfied in-game";
            }

            return null;
        }

        /// <summary>
        /// Handles the "quest Active, nothing Active under it" stall - the state the player fixes with
        /// <c>setquestentrystate &lt;quest&gt; &lt;index&gt; 1</c>.
        /// </summary>
        private static void InspectForNoActiveEntry(Quest quest, string questTitle, string questGuid,
                                                    Il2CppSystem.Collections.Generic.List<QuestEntry> entries)
        {
            int firstInactiveIndex = -1;
            bool anyActive = false;
            bool anyCompleted = false;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null) continue;

                switch (entry.State)
                {
                    case EQuestState.Active:
                        anyActive = true;
                        break;
                    case EQuestState.Completed:
                        anyCompleted = true;
                        break;
                    case EQuestState.Inactive:
                        if (firstInactiveIndex < 0) firstInactiveIndex = i;
                        break;
                }
            }

            // Something is in progress, or there is nothing left to start - either way, not our problem.
            if (anyActive) return;
            if (firstInactiveIndex < 0) return;

            // Starting an entry is the riskier repair: a quest could legitimately be parked waiting
            // on an external trigger. Restrict it to the authored main-story quests we have data for.
            if (!IsKnownMainQuest(questTitle)) return;

            // Require positive evidence that the quest was supposed to have something running:
            //  - it already completed an entry and then stalled, or
            //  - it declares AutoStartFirstEntry and yet never started one.
            // Without either, the quest is legitimately parked waiting on an external trigger.
            if (!anyCompleted && !quest.AutoStartFirstEntry) return;

            var target = entries[firstInactiveIndex];
            if (target == null) return;

            string reason = anyCompleted
                ? "quest is Active with a completed entry but nothing Active"
                : "quest is Active with AutoStartFirstEntry but no entry ever started";

            string entryTitle = SafeTitle(target);
            string key = CandidateKey(questGuid, target.QuestEntryIndex, "noactive");
            _seenThisScan.Add(key);

            if (Confirm(key, questTitle, entryTitle, CONFIRMATIONS_NO_ACTIVE_ENTRY, reason) < CONFIRMATIONS_NO_ACTIVE_ENTRY)
                return;

            ApplyRepair(quest, questTitle, questGuid, target.QuestEntryIndex,
                        entryTitle, target.State, EQuestState.Active, reason);
        }

        #region Repair application

        /// <summary>
        /// The single choke point for state writes. Only forward progress is permitted:
        /// Inactive -> Active (start a stalled quest) and Active -> Completed (finish a stalled entry).
        /// Everything else - notably any transition out of Completed - is refused.
        /// </summary>
        private static bool IsForwardTransition(EQuestState from, EQuestState to)
        {
            if (from == EQuestState.Inactive && to == EQuestState.Active) return true;
            if (from == EQuestState.Active && to == EQuestState.Completed) return true;
            return false;
        }

        private static void ApplyRepair(Quest quest, string questTitle, string questGuid, int entryIndex,
                                        string entryTitle, EQuestState from, EQuestState to, string reason)
        {
            if (!IsForwardTransition(from, to))
            {
                MelonLogger.Error(
                    $"[QuestRepair] REFUSED non-forward transition on '{questTitle}' entry {entryIndex} " +
                    $"('{entryTitle}'): {from} -> {to}. This is a bug in the watchdog.");
                return;
            }

            MelonLogger.Warning(
                $"[QuestRepair] Repairing '{questTitle}' entry {entryIndex} ('{entryTitle}'): " +
                $"{from} -> {to}. Reason: {reason}. (GUID {questGuid})");

            quest.SetQuestEntryState(entryIndex, to, true);

            _repairsThisSession++;
            _repairsPerQuest.TryGetValue(questGuid, out int count);
            _repairsPerQuest[questGuid] = count + 1;

            // The quest has moved on; drop every counter for it so the new state is judged fresh.
            ClearCandidatesForQuest(questGuid);

            if (_repairsPerQuest[questGuid] >= MAX_REPAIRS_PER_QUEST)
            {
                MelonLogger.Error(
                    $"[QuestRepair] '{questTitle}' has been repaired {MAX_REPAIRS_PER_QUEST} times this " +
                    "session - no further repairs will be attempted for it. Please report this with your log.");
            }

            if (_repairsThisSession >= MAX_REPAIRS_PER_SESSION)
            {
                MelonLogger.Error(
                    $"[QuestRepair] Session repair cap ({MAX_REPAIRS_PER_SESSION}) reached - watchdog disabled " +
                    "for the rest of this session. Please report this with your log.");
            }
        }

        private static int GetRepairCount(string questGuid)
        {
            return _repairsPerQuest.TryGetValue(questGuid, out int count) ? count : 0;
        }

        #endregion

        #region Debounce bookkeeping

        private static string CandidateKey(string questGuid, int entryIndex, string kind)
        {
            return $"{questGuid}|{entryIndex}|{kind}";
        }

        /// <summary>
        /// Increments and returns the consecutive-observation count for a candidate.
        /// The first observation is logged so a stall shows up in player logs even if it later
        /// resolves on its own and never reaches the confirmation threshold.
        /// </summary>
        private static int Confirm(string key, string questTitle, string entryTitle, int required, string reason)
        {
            _confirmations.TryGetValue(key, out int count);
            count++;
            _confirmations[key] = count;

            if (count == 1)
            {
                MelonLogger.Msg(
                    $"[QuestRepair] Watching '{questTitle}' entry '{entryTitle}' - {reason}. " +
                    $"Will repair if this holds for {required} consecutive scans.");
            }

            return count;
        }

        private static void PruneUnseenCandidates()
        {
            if (_confirmations.Count == 0) return;

            List<string> stale = null;
            foreach (var kvp in _confirmations)
            {
                if (!_seenThisScan.Contains(kvp.Key))
                {
                    (stale ?? (stale = new List<string>())).Add(kvp.Key);
                }
            }

            if (stale == null) return;
            foreach (var key in stale)
            {
                _confirmations.Remove(key);
            }
        }

        private static void ClearCandidatesForQuest(string questGuid)
        {
            string prefix = questGuid + "|";
            List<string> toRemove = null;
            foreach (var kvp in _confirmations)
            {
                if (kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    (toRemove ?? (toRemove = new List<string>())).Add(kvp.Key);
                }
            }

            if (toRemove == null) return;
            foreach (var key in toRemove)
            {
                _confirmations.Remove(key);
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// True if this is one of the authored main-story quests the APWorld knows about.
        /// The set is derived from the "Mission"-tagged locations, whose keys are
        /// "&lt;Quest Title&gt;|&lt;Entry Title&gt;" - so it covers exactly the quests we have logic for
        /// and excludes contracts, dead drops, and anything else generated at runtime.
        /// </summary>
        private static bool IsKnownMainQuest(string questTitle)
        {
            if (string.IsNullOrEmpty(questTitle)) return false;

            if (_knownMainQuests == null)
            {
                var locations = Data_Locations.Locations;
                if (locations == null) return false; // not loaded yet - retry on the next scan

                var built = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in locations)
                {
                    if (kvp.Value?.Tags == null || !kvp.Value.Tags.Contains("Mission")) continue;

                    int pipeIndex = kvp.Key.IndexOf('|');
                    if (pipeIndex <= 0) continue;

                    built.Add(kvp.Key.Substring(0, pipeIndex));
                }

                // Empty means the data is malformed - stay disabled rather than caching a bad set.
                if (built.Count == 0) return false;

                _knownMainQuests = built;
                MelonLogger.Msg($"[QuestRepair] Built main-quest allowlist with {built.Count} quests");
            }

            return _knownMainQuests.Contains(NarcopelagoQuests.StripParentheticalContent(questTitle));
        }

        private static EntryRule FindRule(Quest quest, QuestEntry entry)
        {
            string questTitle = NarcopelagoQuests.StripParentheticalContent(SafeTitle(quest));
            string entryTitle = NarcopelagoQuests.StripParentheticalContent(SafeTitle(entry));

            if (string.IsNullOrEmpty(questTitle) || string.IsNullOrEmpty(entryTitle)) return null;

            foreach (var rule in _rules)
            {
                if (string.Equals(rule.QuestTitle, questTitle, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(rule.EntryTitle, entryTitle, StringComparison.OrdinalIgnoreCase))
                {
                    return rule;
                }
            }
            return null;
        }

        private static string SafeTitle(Quest quest)
        {
            try { return quest.Title ?? quest.name ?? "Unknown"; }
            catch { return "Unknown"; }
        }

        private static string SafeTitle(QuestEntry entry)
        {
            try { return entry.Title ?? entry.name ?? "Unknown"; }
            catch { return "Unknown"; }
        }

        private static string SafeGuid(Quest quest)
        {
            try { return quest.GUID.ToString(); }
            catch { return null; }
        }

        #endregion
    }
}
