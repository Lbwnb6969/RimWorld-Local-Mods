using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using Verse;

namespace Local.RimDark40K.LostArmoryVanilla
{
    public sealed class ArmoryRewardSlot : IExposable
    {
        public int tier;
        public string defName;
        public int thingId;
        public bool recovered;
        public void ExposeData()
        {
            Scribe_Values.Look(ref tier, "tier");
            Scribe_Values.Look(ref defName, "defName");
            Scribe_Values.Look(ref thingId, "thingId");
            Scribe_Values.Look(ref recovered, "recovered");
        }
    }

    public sealed class LostArmoryMod : Mod
    {
        public LostArmoryMod(ModContentPack content) : base(content)
        {
            new Harmony("Local.RimDark40K.LostArmory.Vanilla").PatchAll(typeof(LostArmoryMod).Assembly);
        }
    }

    public sealed class ArmoryProgressionComponent : GameComponent
    {
        public int completedTier;
        public int activeTier;
        public int pendingClueTick = -1;
        public int pendingSettlementId = -1;
        private int lastClueSettlementId = -1;
        private bool needsPostLoadValidation;
        public List<ArmoryRewardSlot> rewardSlots = new List<ArmoryRewardSlot>();
        private List<string> clueSources = new List<string>();
        public ArmoryProgressionComponent(Game game) { }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref completedTier, "completedTier", 0);
            Scribe_Values.Look(ref activeTier, "activeTier", 0);
            Scribe_Values.Look(ref pendingClueTick, "pendingClueTick", -1);
            Scribe_Values.Look(ref pendingSettlementId, "pendingSettlementId", -1);
            Scribe_Values.Look(ref lastClueSettlementId, "lastClueSettlementId", -1);
            Scribe_Collections.Look(ref rewardSlots, "rewardSlots", LookMode.Deep);
            Scribe_Collections.Look(ref clueSources, "clueSources", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (rewardSlots == null) rewardSlots = new List<ArmoryRewardSlot>();
                if (clueSources == null) clueSources = new List<string>();
            }
        }

        public override void LoadedGame()
        {
            base.LoadedGame();
            needsPostLoadValidation = activeTier > 0;
            ValidateLoadedProgression();
        }

        public override void GameComponentTick()
        {
            base.GameComponentTick();
            if (needsPostLoadValidation)
                ValidateLoadedProgression();
            if (pendingClueTick > 0 && Find.TickManager.TicksGame >= pendingClueTick)
                GeneratePendingQuest();
            if (Find.TickManager.TicksGame % 250 == 0)
                CheckRecoveryParts();
        }

        private static void CheckRecoveryParts()
        {
            List<Quest> quests = Find.QuestManager?.QuestsListForReading;
            if (quests == null)
                return;

            foreach (Quest quest in quests.ToList())
            {
                if (quest == null || quest.State != QuestState.Ongoing || quest.PartsListForReading == null)
                    continue;
                foreach (QuestPart_ArmoryRecovery part in quest.PartsListForReading.OfType<QuestPart_ArmoryRecovery>().ToList())
                    part.TickRecovery();
            }
        }

        public static void QueueDefeatedStrongholdClue(Settlement settlement)
        {
            ArmoryProgressionComponent c = Instance;
            string source = settlement == null || !settlement.Tile.Valid || settlement.Faction == null ? null :
                settlement.Tile.Layer.GetUniqueLoadID() + ":" + settlement.Tile.tileId + ":" + settlement.Faction.loadID;
            if (c == null || source == null || c.completedTier >= 3 || c.activeTier != 0 ||
                c.pendingClueTick > 0 || c.lastClueSettlementId == settlement.ID || c.clueSources.Contains(source))
                return;

            int nextTier = c.completedTier + 1;
            c.pendingSettlementId = settlement.ID;
            c.lastClueSettlementId = settlement.ID;
            c.clueSources.Add(source);
            c.pendingClueTick = Find.TickManager.TicksGame + 600;
            string tierLabel = nextTier == 1 ? "失落战团军械库" : nextTier == 2 ? "原铸封存库" : "黄金军械库";
            Find.LetterStack.ReceiveLetter("军械库线索", "在被攻陷的据点中心发现了指向" + tierLabel + "的密封坐标。线索正在解码，稍后会开启独立任务。", LetterDefOf.NeutralEvent, new GlobalTargetInfo(settlement.Tile));
            Log.Message("[Lost Armory] Defeated EES stronghold produced tier " + nextTier + " clue at " + settlement.LabelCap + ".");
        }

        private void GeneratePendingQuest()
        {
            int tier = completedTier + 1;
            if (tier < 1 || tier > 3 || activeTier != 0)
            {
                pendingClueTick = -1;
                return;
            }
            QuestScriptDef root = DefDatabase<QuestScriptDef>.GetNamedSilentFail(tier == 1
                ? "Local_RimDark40K_LostChapterArmory"
                : tier == 2 ? "Local_RimDark40K_PrimarisSealedVault" : "Local_RimDark40K_GoldenArmory");
            if (root == null)
            {
                Log.Error("[Lost Armory] Missing quest root for tier " + tier + ".");
                pendingClueTick = Find.TickManager.TicksGame + 600;
                return;
            }
            // A later generation node may fail after the quest has already registered its site and slots.
            HashSet<int> previousQuests = new HashSet<int>(Find.QuestManager.QuestsListForReading.Select(quest => quest.id));
            HashSet<int> previousSites = new HashSet<int>(Find.WorldObjects.Sites.Select(site => site.ID));
            List<ArmoryRewardSlot> previousSlots = rewardSlots.Select(slot => new ArmoryRewardSlot
                { tier = slot.tier, defName = slot.defName, thingId = slot.thingId, recovered = slot.recovered }).ToList();
            try
            {
                Quest quest = QuestUtility.GenerateQuestAndMakeAvailable(root, new Slate());
                if (quest == null) throw new InvalidOperationException("quest generation returned null");
                pendingClueTick = -1;
                pendingSettlementId = -1;
                Log.Message("[Lost Armory] Opened tier " + tier + " quest from defeated EES stronghold clue.");
            }
            catch (Exception ex)
            {
                foreach (Quest quest in Find.QuestManager.QuestsListForReading.Where(quest => !previousQuests.Contains(quest.id) &&
                    quest.PartsListForReading.OfType<QuestPart_ArmoryProgression>().Any(part => part.tier == tier)).ToList())
                    quest.End(QuestEndOutcome.Fail, false);
                foreach (Site site in Find.WorldObjects.Sites.Where(site => !previousSites.Contains(site.ID) &&
                    site.parts.Any(part => part.def.Worker is SitePartWorker_ArmoryCastle)).ToList())
                    if (!site.Destroyed && site.Map == null) site.Destroy();
                rewardSlots = previousSlots;
                activeTier = 0;
                Log.Error("[Lost Armory] Could not open tier " + tier + " quest from clue: " + ex);
                pendingClueTick = Find.TickManager.TicksGame + 600;
            }
        }

        private void ValidateLoadedProgression()
        {
            if (completedTier < 0 || completedTier > 3)
            {
                Log.Warning("[Lost Armory] Clamped invalid completed tier " + completedTier + " from the loaded game.");
                completedTier = System.Math.Max(0, System.Math.Min(3, completedTier));
            }

            if (activeTier <= 0)
            {
                needsPostLoadValidation = false;
                return;
            }
            if (activeTier > 3)
            {
                Log.Warning("[Lost Armory] Cleared an invalid active tier reference from the loaded game: tier " + activeTier + ".");
                activeTier = 0;
                needsPostLoadValidation = false;
                return;
            }

            if (Find.QuestManager == null)
                return;

            List<Quest> quests = Find.QuestManager.QuestsListForReading;
            if (quests == null)
                return;

            needsPostLoadValidation = false;

            bool hasMatchingQuest = quests.Any(quest =>
                quest != null &&
                (quest.State == QuestState.NotYetAccepted || quest.State == QuestState.Ongoing) &&
                quest.PartsListForReading != null &&
                quest.PartsListForReading.OfType<QuestPart_ArmoryProgression>()
                    .Any(part => part != null && part.tier == activeTier));
            if (!hasMatchingQuest)
            {
                Log.Warning("[Lost Armory] Cleared an orphaned active tier reference from the loaded game: tier " + activeTier + ".");
                activeTier = 0;
            }
        }

        public static ArmoryProgressionComponent Instance => Verse.Current.Game?.GetComponent<ArmoryProgressionComponent>();
    }

    [HarmonyPatch(typeof(SettlementDefeatUtility), "IsDefeated")]
    public static class LostArmoryDefeatTrigger
    {
        public static void Postfix(Map map, ref bool __result)
        {
            Settlement settlement = map?.Parent as Settlement;
            if (!__result || settlement == null || !HasGeneratedEesStronghold(map)) return;
            ArmoryProgressionComponent.QueueDefeatedStrongholdClue(settlement);
        }

        private static bool HasGeneratedEesStronghold(Map map)
        {
            if (map?.components == null) return false;
            foreach (MapComponent component in map.components)
            {
                if (component?.GetType().FullName != "EvolvingEnemyStrongholds.MapComponent_StrongholdSiege") continue;
                FieldInfo generated = component.GetType().GetField("eesGenerated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return generated?.GetValue(component) is bool value && value;
            }
            return false;
        }
    }

    public sealed class QuestNode_ArmoryTierAvailable : QuestNode
    {
        public int tier = 1;
        protected override bool TestRunInt(Slate slate)
        {
            ArmoryProgressionComponent c = ArmoryProgressionComponent.Instance;
            // Do not let a quest pass while the progression component is
            // unavailable: RegisterArmoryTier cannot register it in that
            // state, leaving an orphaned quest that can block the chain.
            return c != null && tier >= 1 && tier <= 3 && c.activeTier == 0 && c.completedTier == tier - 1;
        }
        protected override void RunInt() { }
    }

    public sealed class QuestNode_SetArmoryPoints : QuestNode
    {
        public int minimum;
        public int maximum;
        protected override bool TestRunInt(Slate slate) { SetPoints(slate); return true; }
        protected override void RunInt()
        {
            SetPoints(QuestGen.slate);
        }

        private void SetPoints(Slate slate)
        {
            float points = slate.Get("points", (float)minimum, false);
            if (float.IsNaN(points) || float.IsInfinity(points))
                points = minimum;
            points = points < minimum ? minimum : points > maximum ? maximum : points;
            slate.Set("points", points, false);
        }
    }

    public sealed class QuestNode_RegisterArmoryTier : QuestNode
    {
        public SlateRef<Site> site;
        public SlateRef<List<Thing>> items;
        public int tier = 1;
        public int recoveryTimeoutDays = 30;
        protected override bool TestRunInt(Slate slate)
        {
            ArmoryProgressionComponent c = ArmoryProgressionComponent.Instance;
            // GenerateSite does not create a Site during the quest's TestRun phase.
            return c != null && tier >= 1 && tier <= 3 &&
                c.activeTier == 0 && c.completedTier == tier - 1;
        }

        protected override void RunInt()
        {
            Site generatedSite = site.GetValue(QuestGen.slate);
            List<Thing> generatedRewards = items.GetValue(QuestGen.slate);
            if (generatedSite == null)
                throw new System.InvalidOperationException("[Lost Armory] Cannot register a tier without a generated site.");
            ArmoryProgressionComponent c = ArmoryProgressionComponent.Instance;
            if (generatedRewards == null || (generatedRewards.Count == 0 && !c.rewardSlots.Any(slot => slot.tier == tier)))
                throw new System.InvalidOperationException("[Lost Armory] Cannot register recovery without generated rewards.");
            foreach (Thing reward in generatedRewards)
            {
                ArmoryRewardSlot slot = c.rewardSlots.FirstOrDefault(s => s.tier == tier && s.defName == reward.def.defName);
                if (slot == null) { slot = new ArmoryRewardSlot { tier = tier, defName = reward.def.defName }; c.rewardSlots.Add(slot); }
                slot.thingId = reward.thingIDNumber;
            }
            QuestGen.quest.AddPart(new QuestPart_ArmoryProgression { tier = tier, completionVersion = 1, inSignalEnable = QuestGen.quest.InitiateSignal });
            QuestGen.quest.AddPart(new QuestPart_ArmoryRecovery
            {
                tier = tier,
                recoveryVersion = 1,
                site = generatedSite,
                successSignal = QuestGenUtility.HardcodedSignalWithQuestID("armory.Recovered"),
                failSignal = QuestGenUtility.HardcodedSignalWithQuestID("armory.RecoveryExpired"),
                rewardThingIds = c.rewardSlots.Where(slot => slot.tier == tier && slot.thingId > 0).Select(slot => slot.thingId).Distinct().ToList(),
                recoveryTimeoutTicks = System.Math.Max(1, recoveryTimeoutDays) * GenDate.TicksPerDay
            });
            QuestGen.quest.AddPart(new QuestPart_QuestEnd
            {
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID("armory.Recovered"),
                outcome = QuestEndOutcome.Success,
                sendLetter = true,
                playSound = true,
                signalListenMode = QuestPart.SignalListenMode.OngoingOnly
            });
            QuestGen.quest.AddPart(new QuestPart_QuestEnd
            {
                inSignal = QuestGenUtility.HardcodedSignalWithQuestID("armory.RecoveryExpired"),
                outcome = QuestEndOutcome.Fail,
                sendLetter = true,
                playSound = true,
                signalListenMode = QuestPart.SignalListenMode.OngoingOnly
            });
        }
    }

    public sealed class QuestPart_ArmoryProgression : QuestPartActivable
    {
        public int tier;
        public int completionVersion;

        protected override void Enable(SignalArgs signalArgs)
        {
            ArmoryProgressionComponent c = ArmoryProgressionComponent.Instance;
            if (c == null || tier < 1 || tier > 3 || c.completedTier != tier - 1 ||
                (c.activeTier != 0 && c.activeTier != tier))
            {
                Log.WarningOnce("[Lost Armory] Refused to activate an invalid armory tier " + tier + ".",
                    GenText.StableStringHash("LostArmory:InvalidEnable:" + tier));
                return;
            }

            c.activeTier = tier;
            try
            {
                base.Enable(signalArgs);
            }
            catch
            {
                if (c.activeTier == tier)
                    c.activeTier = 0;
                throw;
            }

            if (State != QuestPartState.Enabled && c.activeTier == tier)
                c.activeTier = 0;
        }

        public override void Cleanup()
        {
            ArmoryProgressionComponent c = ArmoryProgressionComponent.Instance;
            if (c != null && c.activeTier == tier)
            {
                List<QuestPart_ArmoryRecovery> recoveryParts = quest?.PartsListForReading?.OfType<QuestPart_ArmoryRecovery>().ToList();
                foreach (QuestPart_ArmoryRecovery part in recoveryParts ?? new List<QuestPart_ArmoryRecovery>())
                    part.RecordRecoveredRewards();
                // Quests accepted before recovery parts existed retain their original completion contract.
                bool recovered = recoveryParts != null && ((recoveryParts.Count == 0 && completionVersion == 0) ||
                    recoveryParts.Any(part => part.tier == tier && part.recoveryComplete));
                if (quest != null && quest.State == QuestState.EndedSuccess && recovered)
                    c.completedTier = System.Math.Max(c.completedTier, tier);
                c.activeTier = 0;
            }
            base.Cleanup();
        }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref tier, "tier", 1);
            Scribe_Values.Look(ref completionVersion, "completionVersion", 0);
        }
    }

    public sealed class QuestPart_ArmoryRecovery : QuestPart
    {
        public int tier;
        public Site site;
        public List<int> rewardThingIds = new List<int>();
        public int recoveryTimeoutTicks;
        public bool recoveryComplete;
        public string successSignal;
        public string failSignal;
        private int recoveryDeadlineTick = -1;
        private bool mapGenerated;
        public int recoveryVersion;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref tier, "tier", 1);
            Scribe_References.Look(ref site, "site");
            Scribe_Collections.Look(ref rewardThingIds, "rewardThingIds", LookMode.Value);
            Scribe_Values.Look(ref recoveryTimeoutTicks, "recoveryTimeoutTicks", 30 * GenDate.TicksPerDay);
            Scribe_Values.Look(ref recoveryDeadlineTick, "recoveryDeadlineTick", -1);
            Scribe_Values.Look(ref mapGenerated, "mapGenerated", false);
            Scribe_Values.Look(ref recoveryComplete, "recoveryComplete", false);
            Scribe_Values.Look(ref successSignal, "successSignal");
            Scribe_Values.Look(ref failSignal, "failSignal");
            Scribe_Values.Look(ref recoveryVersion, "recoveryVersion", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && rewardThingIds == null)
                rewardThingIds = new List<int>();
        }

        public void TickRecovery()
        {
            if (quest == null || quest.State != QuestState.Ongoing || recoveryComplete)
                return;

            bool inspectExisting = mapGenerated || site?.Map != null ||
                rewardThingIds?.Any(id => ArmoryProgressionComponent.Instance?.rewardSlots.Any(slot => slot.tier == tier && slot.thingId == id) != true) == true;
            ArmoryRewardSearch search = ArmoryRewardSearch.Capture(UnrecoveredIds(), inspectExisting);
            RecordRecoveredRewards(search);

            if (site?.Map != null)
            {
                mapGenerated = true;
                if (recoveryDeadlineTick < 0)
                    recoveryDeadlineTick = Find.TickManager.TicksGame + recoveryTimeoutTicks;
            }

            if (AreAllRewardsRecovered())
            {
                recoveryComplete = true;
                Log.Message("[Lost Armory] Tier " + tier + " rewards were recovered to player control; completing quest.");
                SendRecoverySignal(successSignal);
                return;
            }

            bool missingReward = mapGenerated && rewardThingIds.Any(id =>
                ArmoryProgressionComponent.Instance?.rewardSlots.Any(slot => slot.tier == tier && slot.thingId == id && slot.recovered) != true &&
                !search.existing.ContainsKey(id));
            if (missingReward || site == null || (site.Destroyed && !mapGenerated) ||
                (mapGenerated && recoveryDeadlineTick > 0 && Find.TickManager.TicksGame >= recoveryDeadlineTick))
            {
                Log.Warning("[Lost Armory] Tier " + tier + " recovery expired before all registered rewards were recovered.");
                SendRecoverySignal(failSignal);
                return;
            }

        }

        public void RecordRecoveredRewards()
        {
            ArmoryProgressionComponent c = ArmoryProgressionComponent.Instance;
            bool legacySlots = rewardThingIds?.Any(id => c?.rewardSlots.Any(slot => slot.tier == tier && slot.thingId == id) != true) == true;
            RecordRecoveredRewards(ArmoryRewardSearch.Capture(UnrecoveredIds(), legacySlots));
        }

        private IEnumerable<int> UnrecoveredIds()
        {
            ArmoryProgressionComponent c = ArmoryProgressionComponent.Instance;
            return (rewardThingIds ?? Enumerable.Empty<int>()).Where(id =>
                c?.rewardSlots.Any(slot => slot.tier == tier && slot.thingId == id && slot.recovered) != true);
        }

        private void RecordRecoveredRewards(ArmoryRewardSearch search)
        {
            ArmoryProgressionComponent c = ArmoryProgressionComponent.Instance;
            if (c == null || rewardThingIds == null) return;
            foreach (int id in rewardThingIds)
            {
                ArmoryRewardSlot slot = c.rewardSlots.FirstOrDefault(s => s.tier == tier && s.thingId == id);
                if (slot?.recovered == true) continue;
                search.playerControlled.TryGetValue(id, out Thing found);
                search.existing.TryGetValue(id, out Thing existing);
                if (slot == null && existing != null)
                {
                    slot = new ArmoryRewardSlot { tier = tier, defName = existing.def.defName, thingId = id };
                    c.rewardSlots.Add(slot);
                }
                if (slot != null && found != null) slot.recovered = true;
            }
        }

        private static void SendRecoverySignal(string signal)
        {
            if (!signal.NullOrEmpty())
                Find.SignalManager.SendSignal(new Signal(signal));
        }

        private bool AreAllRewardsRecovered()
        {
            if (rewardThingIds == null || rewardThingIds.Count == 0)
                return false;
            foreach (int thingId in rewardThingIds)
            {
                if (ArmoryProgressionComponent.Instance?.rewardSlots.Any(slot => slot.tier == tier && slot.thingId == thingId && slot.recovered) != true)
                    return false;
            }
            return true;
        }

        private static Thing FindPlayerControlledThing(int thingId)
        {
            ArmoryRewardSearch.Capture(new[] { thingId }, false).playerControlled.TryGetValue(thingId, out Thing found);
            return found;
        }

        internal static Thing FindExistingReward(int thingId)
        {
            ArmoryRewardSearch.Capture(new[] { thingId }, true).existing.TryGetValue(thingId, out Thing found);
            return found;
        }
    }

    internal sealed class ArmoryRewardSearch
    {
        internal readonly Dictionary<int, Thing> existing = new Dictionary<int, Thing>();
        internal readonly Dictionary<int, Thing> playerControlled = new Dictionary<int, Thing>();
        private readonly HashSet<int> wanted;
        private readonly List<Thing> held = new List<Thing>();

        private ArmoryRewardSearch(IEnumerable<int> ids) { wanted = new HashSet<int>(ids); }

        internal static ArmoryRewardSearch Capture(IEnumerable<int> ids, bool includeExisting)
        {
            var search = new ArmoryRewardSearch(ids);
            if (search.wanted.Count == 0) return search;
            // This snapshot lasts for one check; transfers and destruction are visible next time.
            foreach (Map map in Find.Maps)
            {
                if (map?.listerThings == null) continue;
                bool controlledMap = map.IsPlayerHome || map.ParentFaction == Faction.OfPlayer;
                if (!includeExisting && !controlledMap) continue;
                foreach (Thing thing in map.listerThings.AllThings)
                {
                    search.Record(thing, controlledMap);
                    if (thing is IThingHolder holder &&
                        (includeExisting || !(thing is Pawn pawn) || pawn.Faction == Faction.OfPlayer))
                        search.ScanHolder(holder, controlledMap && (!(thing is Pawn owner) || owner.Faction == Faction.OfPlayer));
                    if (search.existing.Count == search.wanted.Count) return search;
                }
            }
            if (Find.WorldObjects != null)
            {
                if (includeExisting)
                {
                    foreach (WorldObject worldObject in Find.WorldObjects.AllWorldObjects)
                    {
                        if (worldObject is IThingHolder holder)
                            search.ScanHolder(holder, worldObject is Caravan caravan && caravan.Faction == Faction.OfPlayer);
                        if (search.existing.Count == search.wanted.Count) return search;
                    }
                }
                else
                {
                    foreach (Caravan caravan in Find.WorldObjects.Caravans)
                    {
                        if (caravan.Faction == Faction.OfPlayer) search.ScanHolder(caravan, true);
                        if (search.existing.Count == search.wanted.Count) return search;
                    }
                }
            }
            if (includeExisting && Find.WorldPawns != null)
                foreach (Pawn pawn in Find.WorldPawns.AllPawnsAliveOrDead)
                    if (pawn != null) search.ScanHolder(pawn, false);
            return search;
        }

        private void ScanHolder(IThingHolder holder, bool controlled)
        {
            held.Clear();
            ThingOwnerUtility.GetAllThingsRecursively(holder, held);
            foreach (Thing thing in held) Record(thing, controlled);
        }

        private void Record(Thing thing, bool controlled)
        {
            if (thing == null || thing.Destroyed || !wanted.Contains(thing.thingIDNumber)) return;
            existing[thing.thingIDNumber] = thing;
            if (controlled) playerControlled[thing.thingIDNumber] = thing;
        }
    }

    public sealed class QuestNode_SetArmoryStashContents : QuestNode_SetItemStashContents
    {
        public int tier;
        protected override void RunInt()
        {
            base.RunInt();
            IEnumerable<ThingDef> contents = QuestGen.slate.Get<IEnumerable<ThingDef>>("itemStashThings");
            ArmoryProgressionComponent c = ArmoryProgressionComponent.Instance;
            ArmoryRewardSearch search = ArmoryRewardSearch.Capture(c.rewardSlots.Where(slot => slot.tier == tier && !slot.recovered).Select(slot => slot.thingId), true);
            List<ThingDef> missing = contents.Where(def => !c.rewardSlots.Any(slot => slot.tier == tier && slot.defName == def.defName &&
                (slot.recovered || search.existing.ContainsKey(slot.thingId)))).ToList();
            QuestGen.slate.Set("itemStashThings", (IEnumerable<ThingDef>)missing);
        }
    }

    public sealed class QuestNode_ResearchFinished : QuestNode
    {
        public SlateRef<ResearchProjectDef> research;
        protected override bool TestRunInt(Slate slate)
        {
            ResearchProjectDef project = research.GetValue(slate);
            return project != null && project.IsFinished;
        }
        protected override void RunInt() { }
    }

    public sealed class QuestNode_SetArmoryQuality : QuestNode
    {
        public SlateRef<List<Thing>> items;
        public int tier = 1;

        protected override bool TestRunInt(Slate slate)
        {
            // ItemStash generates the actual Things only when GenerateSite runs.
            return true;
        }

        protected override void RunInt()
        {
            List<Thing> rewards = items.GetValue(QuestGen.slate);
            if (rewards == null)
                throw new System.InvalidOperationException("[Lost Armory] Generate the item-stash site before applying reward quality.");

            foreach (Thing reward in rewards)
            {
                CompQuality quality = reward?.TryGetComp<CompQuality>();
                if (quality == null)
                    continue;

                float roll = Rand.Value;
                float excellent = tier <= 1 ? 0.60f : tier == 2 ? 0.40f : 0.15f;
                float masterwork = tier <= 1 ? 0.35f : tier == 2 ? 0.45f : 0.45f;
                QualityCategory category = roll < excellent
                    ? QualityCategory.Excellent
                    : roll < excellent + masterwork ? QualityCategory.Masterwork : QualityCategory.Legendary;
                quality.SetQuality(category, null);
            }
        }
    }

    public sealed class SitePartWorker_ArmoryCastle : SitePartWorker
    {
        private static readonly ConditionalWeakTable<Map, object> BuiltMaps = new ConditionalWeakTable<Map, object>();

        public override void PostMapGenerate(Map map)
        {
            if (map == null || BuiltMaps.TryGetValue(map, out _))
                return;
            BuiltMaps.Add(map, new object());

            int size = System.Math.Min(map.Size.x, map.Size.z);
            int rings = def?.defName != null && def.defName.EndsWith("_III") ? 3 :
                def?.defName != null && def.defName.EndsWith("_II") ? 2 : 1;
            int margin = 8;
            int outer = System.Math.Max(36, System.Math.Min(size - margin * 2, 34 + rings * 18));
            IntVec3 center = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);

            ThingDef wall = ThingDefOf.Wall;
            ThingDef wallStuff = GetValidStuff(wall);
            ThingDef door = ThingDefOf.Door;
            ThingDef doorStuff = GetValidStuff(door);
            for (int ring = 0; ring < rings; ring++)
            {
                int half = outer / 2 - ring * 9;
                if (half < 8)
                    continue;
                CellRect rect = new CellRect(center.x - half, center.z - half, half * 2 + 1, half * 2 + 1).ClipInsideMap(map);
                try
                {
                    BuildRing(map, rect, wall, wallStuff, door, doorStuff);
                }
                catch (System.Exception exception)
                {
                    Log.Error("[Lost Armory] Castle ring generation failed for tier " + rings + ": " + exception);
                }
            }

            // Small internal rooms make the site read as a multi-room armory without
            // replacing the vanilla Outpost/ItemStash threat or loot generation.
            if (rings > 1)
            {
                int roomHalf = System.Math.Max(5, outer / 6);
                try
                {
                    BuildRing(map, new CellRect(center.x - roomHalf, center.z - roomHalf, roomHalf * 2 + 1, roomHalf * 2 + 1).ClipInsideMap(map),
                        wall, wallStuff, door, doorStuff);
                }
                catch (System.Exception exception)
                {
                    Log.Error("[Lost Armory] Internal armory-room generation failed for tier " + rings + ": " + exception);
                }
            }
        }

        private static void BuildRing(Map map, CellRect rect, ThingDef wall, ThingDef wallStuff, ThingDef door, ThingDef doorStuff)
        {
            if (map == null || rect.Width < 8 || rect.Height < 8)
                return;
            IntVec3 gate = new IntVec3(rect.CenterCell.x, 0, rect.minZ);
            foreach (IntVec3 cell in rect.EdgeCells)
            {
                if (cell == gate || !CanPlace(map, cell))
                    continue;
                Spawn(map, wall, wallStuff, cell);
            }
            if (CanPlace(map, gate))
                Spawn(map, door, doorStuff, gate);
        }

        private static bool CanPlace(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map)) return false;
            foreach (Thing thing in cell.GetThingList(map))
            {
                if (thing?.def == null ||
                    (thing.def.category != ThingCategory.Plant && thing.def.category != ThingCategory.Filth))
                    return false;
            }
            return true;
        }

        private static void Spawn(Map map, ThingDef def, ThingDef stuff, IntVec3 cell)
        {
            if (def == null || !CanPlace(map, cell))
                return;
            ThingDef actualStuff = GetValidStuff(def, stuff);
            if (def.MadeFromStuff && actualStuff == null)
            {
                Log.ErrorOnce("[Lost Armory] Could not find valid stuff for " + def.defName + "; skipped castle pieces.",
                    GenText.StableStringHash("LostArmory:MissingStuff:" + def.defName));
                return;
            }

            try
            {
                Thing thing = ThingMaker.MakeThing(def, actualStuff);
                if (thing == null)
                {
                    Log.ErrorOnce("[Lost Armory] ThingMaker returned null for " + def.defName + "; skipped castle pieces.",
                        GenText.StableStringHash("LostArmory:NullThing:" + def.defName));
                    return;
                }

                GenSpawn.Spawn(thing, cell, map, WipeMode.Vanish);
            }
            catch (System.Exception exception)
            {
                Log.Error("[Lost Armory] Could not spawn " + def.defName + " at " + cell + "; castle piece skipped: " + exception);
            }
        }

        private static ThingDef GetValidStuff(ThingDef def, ThingDef preferred = null)
        {
            if (def == null || !def.MadeFromStuff)
                return null;
            if (CanMakeWith(preferred, def))
                return preferred;

            ThingDef defaultStuff = null;
            try
            {
                defaultStuff = GenStuff.DefaultStuffFor(def);
            }
            catch (System.Exception exception)
            {
                Log.Error("[Lost Armory] Default stuff lookup failed for " + def.defName + ": " + exception);
            }

            if (CanMakeWith(defaultStuff, def))
                return defaultStuff;

            foreach (string fallbackName in new[] { "Granite", "Steel", "WoodLog" })
            {
                ThingDef fallback = DefDatabase<ThingDef>.GetNamedSilentFail(fallbackName);
                if (CanMakeWith(fallback, def))
                    return fallback;
            }

            return null;
        }

        private static bool CanMakeWith(ThingDef stuff, ThingDef def)
        {
            if (stuff == null || def == null || !stuff.IsStuff || stuff.stuffProps == null)
                return false;

            try
            {
                return stuff.stuffProps.CanMake(def);
            }
            catch (System.Exception exception)
            {
                Log.ErrorOnce("[Lost Armory] Stuff validation failed for " + stuff.defName + " -> " + def.defName + ": " + exception,
                    GenText.StableStringHash("LostArmory:StuffValidation:" + stuff.defName + ":" + def.defName));
                return false;
            }
        }
    }
}
