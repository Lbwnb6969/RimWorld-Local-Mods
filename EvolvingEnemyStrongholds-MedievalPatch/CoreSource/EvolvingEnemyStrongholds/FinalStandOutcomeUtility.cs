using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class FinalStandOutcomeUtility
{
    public sealed class DefeatContext
    {
        public Settlement settlement;
        public Map map;
        public FinalStandOutcome outcome;
        internal DefeatContext previous;
    }

    [ThreadStatic] private static Dictionary<int, DefeatContext> defeats;

    public static bool IsDefeatInProgress(Settlement settlement) => settlement != null && defeats != null && defeats.ContainsKey(settlement.ID);

    public static DefeatContext BeginDefeat(Settlement settlement)
    {
        Map map = settlement?.Map;
        if (!FinalStandUtility.IsEligibleSettlement(settlement) || map?.mapPawns.AnyColonistSpawned != true ||
            RimPactsCompat.NotAPlayerAssault(settlement) || RimPactsCompat.IsTerritoryRaze(settlement) ||
            !SettlementDefeatUtility.IsDefeated(map, settlement.Faction)) return null;
        WorldComponent_Strongholds.Instance?.PrepareFinalStandCandidate(settlement);
        defeats ??= new Dictionary<int, DefeatContext>();
        defeats.TryGetValue(settlement.ID, out var previous);
        var context = new DefeatContext
        {
            settlement = settlement,
            map = map,
            outcome = FinalStandOutcome.Capture(settlement, FinalStandOutcomeKind.PlayerConquest, true),
            previous = previous
        };
        defeats[settlement.ID] = context;
        return context;
    }

    public static void CompleteDefeat(DefeatContext context)
    {
        if (context == null || !context.settlement.Destroyed || context.map.Parent is not DestroyedSettlement) return;
        if (context.outcome.destroyedSettlementId >= 0) context.outcome.kind = FinalStandOutcomeKind.PendingDecision;
        WorldComponent_Strongholds.Instance?.ResolveSettlementOutcome(context.outcome);
    }

    public static void EndDefeat(DefeatContext context)
    {
        if (context == null || defeats == null) return;
        if (context.previous == null) defeats.Remove(context.settlement.ID);
        else defeats[context.settlement.ID] = context.previous;
        if (defeats.Count == 0) defeats = null;
    }

    public static void RecordPendingDecision(DestroyedSettlement destroyed, Faction originalFaction, Faction ally)
    {
        if (destroyed == null || originalFaction == null || defeats == null) return;
        foreach (var context in defeats.Values)
        {
            if (context.outcome.factionLoadId != originalFaction.loadID || context.settlement.Tile != destroyed.Tile) continue;
            context.outcome.destroyedSettlementId = destroyed.ID;
            context.outcome.alliedFactionLoadId = ally?.loadID ?? -1;
            return;
        }
    }

    public static void NotifyRemoval(Settlement settlement)
    {
        if (settlement == null || Scribe.mode != LoadSaveMode.Inactive || Current.ProgramState != ProgramState.Playing ||
            IsDefeatInProgress(settlement)) return;
        var world = WorldComponent_Strongholds.Instance;
        if (world?.GetFinalStandOutcome(FinalStandOutcome.KeyFor(settlement))?.kind == FinalStandOutcomeKind.PendingDecision) return;
        world?.ResolveSettlementOutcome(FinalStandOutcome.Capture(settlement,
            RimPactsCompat.IsTerritoryRaze(settlement) ? FinalStandOutcomeKind.NpcLoss : FinalStandOutcomeKind.PassiveRemoval));
        world?.RefreshFinalStandTargets(settlement.Faction?.loadID ?? -1);
    }

    public static void NotifyOwnershipChange(FinalStandOutcome previous, Settlement settlement)
    {
        if (previous == null || settlement?.Faction?.loadID == previous.factionLoadId || IsDefeatInProgress(settlement)) return;
        var world = WorldComponent_Strongholds.Instance;
        world?.ResolveSettlementOutcome(previous);
        world?.RefreshFinalStandTargets(previous.factionLoadId);
    }
}
