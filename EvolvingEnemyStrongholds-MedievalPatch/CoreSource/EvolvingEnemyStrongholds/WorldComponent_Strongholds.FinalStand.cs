using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public partial class WorldComponent_Strongholds
{
    private List<FinalStandClusterRecord> finalStandCandidates = new List<FinalStandClusterRecord>();
    private Dictionary<string, FinalStandOutcome> finalStandOutcomes = new Dictionary<string, FinalStandOutcome>();
    private List<string> outcomeKeys;
    private List<FinalStandOutcome> outcomeValues;

    public IEnumerable<FinalStandClusterRecord> FinalStandClusters => finalStandClusters;
    public IEnumerable<FinalStandClusterRecord> FinalStandCandidates => finalStandCandidates;
    public IEnumerable<FinalStandOutcome> FinalStandOutcomes => finalStandOutcomes.Values;
    public IEnumerable<KeyValuePair<string, string>> FinalStandLineage => finalStandLineage;

    // Kept for callers using the original API; preparation does not commit a campaign.
    public FinalStandClusterRecord BeginFinalStandCluster(Settlement anchor) => PrepareFinalStandCandidate(anchor);

    public FinalStandClusterRecord PrepareFinalStandCandidate(Settlement anchor)
    {
        if (!FinalStandUtility.IsEligibleSettlement(anchor)) return null;
        string key = FinalStandOutcome.KeyFor(anchor);
        if (HasSettledOutcome(key)) return null;
        FinalStandClusterRecord existing = FindFinalStandRecord(key);
        if (existing != null) return existing;

        List<Settlement> members = FinalStandUtility.SnapshotCluster(anchor)
            .Where(s => !HasSettledOutcome(FinalStandOutcome.KeyFor(s)) && FindFinalStandRecord(FinalStandOutcome.KeyFor(s)) == null)
            .ToList();
        if (members.Count < FinalStandUtility.MinimumInitialBases) return null;
        var record = new FinalStandClusterRecord
        {
            schemaVersion = 2,
            clusterId = nextFinalStandClusterId++,
            factionLoadId = anchor.Faction.loadID,
            anchorSettlementId = anchor.ID,
            anchorLayerId = anchor.Tile.Layer.GetUniqueLoadID(),
            anchorTileId = anchor.Tile.tileId,
            initialCount = members.Count,
            phase = FinalStandPhase.Candidate,
            settlementIds = members.Select(s => s.ID).ToList(),
            memberKeys = members.Select(FinalStandOutcome.KeyFor).ToList()
        };
        finalStandCandidates.Add(record);
        EESLog.Message($"Final stand candidate: cluster={record.clusterId}, members={record.initialCount}, anchor={key}.");
        return record;
    }

    /// <summary>
    /// Explicitly transfers one still-unresolved member to a replacement settlement.
    /// The caller must supply the old stable key; no world scan or distance heuristic
    /// may infer this relationship. Triggered targets and settled members stay fixed.
    /// </summary>
    public bool RegisterFinalStandLineage(string sourceMemberKey, Settlement replacement)
    {
        if (string.IsNullOrEmpty(sourceMemberKey) || !FinalStandUtility.IsEligibleSettlement(replacement)) return false;
        string replacementKey = FinalStandOutcome.KeyFor(replacement);
        if (replacementKey == null || replacementKey == sourceMemberKey || finalStandLineage.ContainsKey(replacementKey)) return false;
        if (Find.WorldObjects.Settlements.Any(s => FinalStandOutcome.KeyFor(s) == sourceMemberKey)) return false;
        // Removing a member records a PassiveRemoval outcome before a caller can
        // provide an explicit replacement. Preserve that history, but allow only
        // this reversible outcome to be transferred by the caller-supplied key;
        // pending or settled conquest/FT outcomes remain immutable.
        bool sourcePassiveRemoval = finalStandOutcomes.TryGetValue(sourceMemberKey, out var sourceOutcome) &&
            sourceOutcome.kind == FinalStandOutcomeKind.PassiveRemoval;
        if ((!sourcePassiveRemoval && (HasSettledOutcome(sourceMemberKey) || finalStandOutcomes.ContainsKey(sourceMemberKey))) ||
            HasSettledOutcome(replacementKey) || finalStandOutcomes.ContainsKey(replacementKey)) return false;
        FinalStandClusterRecord record = finalStandClusters.Concat(finalStandCandidates).FirstOrDefault(r =>
            (r.phase == FinalStandPhase.Candidate || r.phase == FinalStandPhase.Active) &&
            r.factionLoadId == replacement.Faction.loadID && r.memberKeys.Contains(sourceMemberKey));
        if (record == null || (record.removedMemberKeys.Contains(sourceMemberKey) && !sourcePassiveRemoval) ||
            FindFinalStandRecord(replacementKey) != null) return false;

        int sourceIndex = record.memberKeys.IndexOf(sourceMemberKey);
        record.memberKeys[sourceIndex] = replacementKey;
        if (!record.settlementIds.Contains(replacement.ID)) record.settlementIds.Add(replacement.ID);
        finalStandLineage[replacementKey] = sourceMemberKey;
        EESLog.Message($"Final stand lineage registered: cluster={record.clusterId}, source={sourceMemberKey}, replacement={replacementKey}.");
        return true;
    }

    private bool HasSettledOutcome(string key) => key != null && finalStandOutcomes.TryGetValue(key, out var outcome) &&
        outcome.kind != FinalStandOutcomeKind.PendingDecision;

    private FinalStandClusterRecord FindFinalStandRecord(string key)
    {
        if (key == null) return null;
        return finalStandClusters.Concat(finalStandCandidates).FirstOrDefault(record =>
            record.phase != FinalStandPhase.Cancelled && record.phase != FinalStandPhase.Resolved && record.memberKeys.Contains(key));
    }

    public FinalStandOutcome GetFinalStandOutcome(string key) => key != null && finalStandOutcomes.TryGetValue(key, out var value) ? value : null;

    public FinalStandOutcome GetPendingFinalStandOutcome(int destroyedSettlementId) => finalStandOutcomes.Values.FirstOrDefault(outcome =>
        outcome.kind == FinalStandOutcomeKind.PendingDecision && outcome.destroyedSettlementId == destroyedSettlementId);

    public bool ResolveSettlementOutcome(FinalStandOutcome outcome)
    {
        if (outcome?.memberKey == null || outcome.factionLoadId < 0) return false;
        if (outcome.kind == FinalStandOutcomeKind.PlayerConquest && !outcome.playerVictoryConfirmed) return false;
        if (HasSettledOutcome(outcome.memberKey)) return false;
        FinalStandClusterRecord record = FindFinalStandRecord(outcome.memberKey);
        if (outcome.kind == FinalStandOutcomeKind.PendingDecision)
        {
            finalStandOutcomes[outcome.memberKey] = outcome.WithKind(outcome.kind);
            return false;
        }

        int before = record == null ? 0 : RemainingFinalStandMembers(record, outcome.memberKey);
        finalStandOutcomes[outcome.memberKey] = outcome.WithKind(outcome.kind);
        if (record == null) return false;
        if (!record.removedMemberKeys.Contains(outcome.memberKey)) record.removedMemberKeys.Add(outcome.memberKey);
        bool conquest = outcome.kind == FinalStandOutcomeKind.PlayerConquest;
        if (conquest)
        {
            if (!record.playerConquestKeys.Contains(outcome.memberKey))
            {
                record.playerConquestKeys.Add(outcome.memberKey);
                // Capture only newly confirmed player victories. Old unknown losses
                // retain zero source budgets and are never retroactively guessed.
                if (outcome.sourceTier > 0 && outcome.sourceMilitaryPower > 0)
                {
                    record.reservePeople = System.Math.Min(48, record.reservePeople + UnityEngine.Mathf.Clamp(outcome.sourceTier + 2, 6, 16));
                    record.reserveElites = System.Math.Min(8, record.reserveElites + UnityEngine.Mathf.Clamp(outcome.sourceTier / 5, 1, 3));
                    record.reservePoints = UnityEngine.Mathf.Min(6000f, record.reservePoints + outcome.sourceMilitaryPower * .15f);
                    record.reserveKindDefs ??= new List<string>();
                    foreach (string kind in outcome.sourceCombatKindDefs ?? new List<string>())
                        if (!record.reserveKindDefs.Contains(kind)) record.reserveKindDefs.Add(kind);
                }
            }
            if (!record.destroyedSettlementIds.Contains(outcome.settlementId)) record.destroyedSettlementIds.Add(outcome.settlementId);
            if (record.phase == FinalStandPhase.Candidate)
            {
                finalStandCandidates.Remove(record);
                finalStandClusters.Add(record);
                record.phase = FinalStandPhase.Active;
                record.battleStartedTick = Find.TickManager.TicksGame;
            }
        }
        if (record.phase == FinalStandPhase.Triggered && record.targetMemberKey == outcome.memberKey)
        {
            if (conquest)
            {
                record.phase = FinalStandPhase.Resolved;
                record.resolvedTick = Find.TickManager.TicksGame;
                record.resolutionReason = "player-conquest";
                ClearFinalStandTarget(record);
            }
            else CancelFinalStand(record, outcome.kind.ToString());
            return false;
        }
        int after = RemainingFinalStandMembers(record);
        if (record.phase == FinalStandPhase.Active && conquest && before == 2 && after == 1 && record.playerConquestKeys.Count >= 3)
            return TriggerFinalStand(record);
        return false;
    }

    // Compatibility for explicit, already validated conquest notifications, not a Destroy heuristic.
    public bool NotifyFinalStandSettlementDestroyed(Settlement destroyed)
    {
        if (!FinalStandUtility.IsEligibleSettlement(destroyed)) return false;
        PrepareFinalStandCandidate(destroyed);
        return ResolveSettlementOutcome(FinalStandOutcome.Capture(destroyed, FinalStandOutcomeKind.PlayerConquest, true));
    }

    private int RemainingFinalStandMembers(FinalStandClusterRecord record, string includeDepartingKey = null)
    {
        int remaining = 0;
        var living = new HashSet<string>(Find.WorldObjects.Settlements.Where(FinalStandUtility.IsEligibleSettlement).Select(FinalStandOutcome.KeyFor));
        foreach (string key in record.memberKeys)
        {
            if (record.removedMemberKeys.Contains(key)) continue;
            if (key == includeDepartingKey || living.Contains(key) ||
                (finalStandOutcomes.TryGetValue(key, out var outcome) && outcome.kind == FinalStandOutcomeKind.PendingDecision))
                remaining++;
        }
        return remaining;
    }

    private bool TriggerFinalStand(FinalStandClusterRecord record)
    {
        Settlement target = Find.WorldObjects.Settlements.FirstOrDefault(s => FinalStandUtility.IsEligibleSettlement(s) &&
            record.memberKeys.Contains(FinalStandOutcome.KeyFor(s)) && !record.removedMemberKeys.Contains(FinalStandOutcome.KeyFor(s)));
        StrongholdData data = target == null ? null : GetOrCreate(target);
        if (data == null) { CancelFinalStand(record, "target-unavailable"); return false; }
        record.phase = FinalStandPhase.Triggered;
        record.triggered = true;
        record.targetSettlementId = target.ID;
        record.targetMemberKey = FinalStandOutcome.KeyFor(target);
        record.existingMapPending = target.HasMap;
        data.finalStandReservePeople = UnityEngine.Mathf.Clamp(record.reservePeople, 24, 48);
        data.finalStandReserveElites = UnityEngine.Mathf.Clamp(record.reserveElites, 4, 8);
        data.finalStandReservePoints = UnityEngine.Mathf.Clamp(record.reservePoints > 0 ? record.reservePoints : data.militaryPower * .4f, 400f, 6000f);
        data.finalStandReserveKindDefs = new List<string>(record.reserveKindDefs ?? new List<string>());
        FinalStandUtility.Activate(data, record.clusterId, target.ID);
        if (record.existingMapPending && target.Map != null)
            TryApplyExistingMapFinalStandReinforcement(target.Map);
        // Apply after the normal generation recalculation. Existing maps await safe reinforcement.
        // The location outlives conquest/NPC replacement of the Settlement object.
        Find.LetterStack?.ReceiveLetter(EESText.T("EES.FinalStand.Title"),
            EESText.T("EES.FinalStand.Text", target.LabelCap, target.Faction.Name), LetterDefOf.ThreatBig, new LookTargets(target.Tile));
        EESLog.Message($"Final stand triggered: cluster={record.clusterId}, conquests={record.playerConquestKeys.Count}, target={record.targetMemberKey}, existingMap={record.existingMapPending}.");
        return true;
    }

    public bool TryApplyExistingMapFinalStandReinforcement(Map map)
    {
        if (map?.Parent is not Settlement settlement) return false;
        FinalStandClusterRecord record = finalStandClusters.FirstOrDefault(r =>
            r.phase == FinalStandPhase.Triggered && r.existingMapPending && r.targetSettlementId == settlement.ID);
        if (record == null) return false;
        StrongholdData data = GetExisting(settlement.ID);
        if (data == null) { record.existingMapReinforcementReason = "stronghold-data-missing"; return false; }
        if (record.existingMapReinforcementAttempted && record.existingMapReinforcementPawnCount > 0) return false;
        record.existingMapReinforcementAttempted = true;
        FinalStandUtility.ApplyBeforeGeneration(data, settlement);
        int count = FinalStandUtility.TryApplyExistingMapReinforcement(map, settlement, data, out string reason);
        record.existingMapReinforcementPawnCount = count;
        record.existingMapReinforcementTick = Find.TickManager.TicksGame;
        record.existingMapReinforcementReason = reason;
        if (count > 0)
        {
            record.existingMapPending = false;
            return true;
        }
        return false;
    }

    public bool HasPendingExistingMapFinalStand(int settlementId)
    {
        return finalStandClusters.Any(r => r.phase == FinalStandPhase.Triggered && r.existingMapPending &&
            r.targetSettlementId == settlementId);
    }

    private void ClearFinalStandTarget(FinalStandClusterRecord record)
    {
        StrongholdData data = GetExisting(record.targetSettlementId);
        if (data != null && data.finalStandClusterId == record.clusterId) data.finalStandActive = false;
        record.existingMapPending = false;
    }

    private void CancelFinalStand(FinalStandClusterRecord record, string reason)
    {
        record.phase = FinalStandPhase.Cancelled;
        record.resolvedTick = Find.TickManager.TicksGame;
        record.resolutionReason = reason;
        ClearFinalStandTarget(record);
        EESLog.Message($"Final stand cancelled: cluster={record.clusterId}, reason={reason}.");
    }

    public void RefreshFinalStandTargets(int factionLoadId = -1)
    {
        foreach (var record in finalStandClusters.Concat(finalStandCandidates).ToList())
        {
            if (record.phase == FinalStandPhase.Resolved || record.phase == FinalStandPhase.Cancelled ||
                (factionLoadId >= 0 && record.factionLoadId != factionLoadId)) continue;
            Faction faction = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f => f.loadID == record.factionLoadId);
            if (faction == null || !faction.HostileTo(Faction.OfPlayer)) CancelFinalStand(record, "faction-not-hostile");
            else if (record.phase == FinalStandPhase.Triggered)
            {
                Settlement target = GetSettlement(record.targetSettlementId);
                // FT removes the Settlement and leaves a DestroyedSettlement marker while
                // the player's vassalise/cede choice is unresolved. Preserve the triggered
                // campaign across load and relation refresh until that choice is resolved.
                if (target == null && record.targetMemberKey != null &&
                    finalStandOutcomes.TryGetValue(record.targetMemberKey, out var pending) &&
                    pending.kind == FinalStandOutcomeKind.PendingDecision && pending.destroyedSettlementId >= 0 &&
                    Find.WorldObjects.AllWorldObjects.Any(o => o.ID == pending.destroyedSettlementId && o is DestroyedSettlement))
                    continue;
                if (!FinalStandUtility.IsEligibleSettlement(target) || FinalStandOutcome.KeyFor(target) != record.targetMemberKey)
                    CancelFinalStand(record, "target-removed-or-transferred");
            }
        }
    }

    private void ClearFinalStandOutcomes()
    {
        finalStandCandidates.Clear();
        finalStandOutcomes.Clear();
    }

    private void ExposeFinalStandState()
    {
        Scribe_Collections.Look(ref finalStandCandidates, "finalStandCandidates", LookMode.Deep);
        Scribe_Collections.Look(ref finalStandOutcomes, "finalStandOutcomes", LookMode.Value, LookMode.Deep, ref outcomeKeys, ref outcomeValues);
        if (Scribe.mode != LoadSaveMode.PostLoadInit) return;
        finalStandCandidates ??= new List<FinalStandClusterRecord>();
        finalStandOutcomes ??= new Dictionary<string, FinalStandOutcome>();
    }

    public override void FinalizeInit(bool fromLoad)
    {
        base.FinalizeInit(fromLoad);
        // World.FinalizeInit runs before Scribe resolves faction/world-object references.
        // Run after the whole load, before player interaction, so no valid campaign is cancelled early.
        LongEventHandler.ExecuteWhenFinished(() =>
        {
            foreach (var record in finalStandClusters.Concat(finalStandCandidates))
            {
                if (record.schemaVersion >= 2) continue;
                record.memberKeys = record.settlementIds.Select(id => FinalStandOutcome.KeyFor(GetSettlement(id)) ??
                    "legacy:" + record.factionLoadId + ":" + id).ToList();
                record.targetMemberKey = FinalStandOutcome.KeyFor(GetSettlement(record.targetSettlementId));
                record.phase = record.triggered ? FinalStandPhase.Triggered : FinalStandPhase.Active;
                Settlement anchor = GetSettlement(record.anchorSettlementId);
                record.anchorLayerId = anchor?.Tile.Layer.GetUniqueLoadID();
                record.anchorTileId = anchor?.Tile.tileId ?? -1;
                // Historical destroyed IDs do not prove player victories or recover their old locations.
                record.schemaVersion = 2;
            }
            if (fromLoad)
            {
                // Migrate pre-feature saves before the world inspector is first drawn.  This
                // keeps the left-hand settlement text and derived bonuses in sync without
                // creating data for settlements that were never EES strongholds.
                foreach (var pair in dataById.ToList())
                {
                    Settlement settlement = GetSettlement(pair.Key);
                    if (settlement == null) continue;
                    StrongholdData data = pair.Value;
                    string featureBefore = data.featureKey;
                    int versionBefore = data.featureVersion;
                    bool forcedBefore = data.featureForced;
                    StrongholdFeatureUtility.EnsureAssigned(data, settlement, this);
                    if (featureBefore != data.featureKey || versionBefore != data.featureVersion || forcedBefore != data.featureForced)
                        StrongholdScoreUtility.ApplyDerived(data, settlement);
                }
            }
            RefreshFinalStandTargets();
        });
    }
}
