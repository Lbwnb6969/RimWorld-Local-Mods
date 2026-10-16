using System;
using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using System.Linq;

namespace EvolvingEnemyStrongholds;

public static class FinalStandUtility
{
    public const int ClusterRadius = 15;
    public const int MinimumInitialBases = 4;

    public static bool Active(StrongholdData data) => data?.finalStandActive == true;

    public static void Activate(StrongholdData data, int clusterId, int targetSettlementId)
    {
        if (data == null || data.finalStandActive) return;
        data.finalStandActive = true;
        data.finalStandClusterId = clusterId;
        data.finalStandStartedTick = Find.TickManager.TicksGame;
        data.finalStandBudgetVersion = 1;
        if (data.finalStandReservePeople <= 0) data.finalStandReservePeople = Mathf.Clamp(22 + data.tier * 2, 24, 48);
        if (data.finalStandReserveElites <= 0) data.finalStandReserveElites = Mathf.Clamp(3 + data.tier / 4, 4, 8);
        if (data.finalStandReservePoints <= 0f) data.finalStandReservePoints = Mathf.Clamp(data.militaryPower * .4f, 400f, 6000f);
        data.alertLevel = Mathf.Max(data.alertLevel, 5);
        EESLog.Message($"Final stand activated: settlement={targetSettlementId}, cluster={clusterId}, tier=T{data.tier}.");
    }

    public static void ApplyBeforeGeneration(StrongholdData data, Settlement settlement)
    {
        if (!Active(data) || data.finalStandApplied || settlement?.Faction == null) return;
        data.militaryPower = Mathf.Max(data.militaryPower, Mathf.RoundToInt(data.militaryPower * 1.6f));
        data.fortification = Mathf.Clamp(data.fortification + 30, 0, 100);
        data.supply += 30;
        data.hasCommander = true;
        data.hasElite = true;
        data.eliteCount = data.finalStandBudgetVersion > 0 ? Mathf.Max(4, data.eliteCount + data.finalStandReserveElites) :
            Mathf.Max(2, data.eliteCount + 1);
        data.hasZoneGuard = true;
        data.pursuerGroups = Mathf.Max(1, data.pursuerGroups + 1);
        data.hasAssault = true;
        if ((int)settlement.Faction.def.techLevel >= (int)TechLevel.Medieval)
            data.hasMortarBattery = true;
        data.finalStandApplied = true;
        EESLog.Message($"Final stand generation budget [{settlement.Faction.Name}]: mil={data.militaryPower}, fort={data.fortification}, elite={data.eliteCount}, assault={data.hasAssault}.");
    }

    public static int AreaBonus(StrongholdData data) => Active(data) ? 6 : 0;

    public static float LootMultiplier(StrongholdData data) => Active(data) ? (data.finalStandBudgetVersion > 0 ? 2f : 1.75f) : 1f;

    public static float TurretMultiplier(StrongholdData data) => Active(data) ? 1.35f : 1f;

    public static int PopulationBonus(StrongholdData data) => !Active(data) ? 0 : data.finalStandBudgetVersion <= 0 ? 12 :
        Mathf.Max(0, Mathf.Min(160, StrongholdGarrisonUtility.PopulationLimit(data.tier) +
            data.finalStandReservePeople) - StrongholdGarrisonUtility.PopulationLimit(data.tier));

    public static int PeakPopulationLimit(StrongholdData data) => StrongholdGarrisonUtility.PopulationLimit(data?.tier ?? 1) +
        PopulationBonus(data) + (Active(data) && data.finalStandBudgetVersion > 0 ? 20 : 0);

    public static int CountActiveHostiles(Map map) => StrongholdPawnOwnership.Population(map, null, true).Count;

    public static List<Pawn> LimitReinforcementPawns(Map map, Faction faction, StrongholdData data, IEnumerable<Pawn> pawns)
    {
        var result = new List<Pawn>();
        if (pawns == null) return result;
        int slots = map?.Parent is Settlement s && s.Faction == faction && faction?.IsPlayer == false &&
            data?.settlementId == s.ID && faction.HostileTo(Faction.OfPlayer) && StrongholdCenterPlanner.Eligible(map, data)
            ? Mathf.Max(0, PeakPopulationLimit(data) - CountActiveHostiles(map)) : int.MaxValue;
        int added = 0;
        foreach (var pawn in pawns)
        {
            if (pawn == null) continue;
            if (pawn.Faction != faction || pawn.Spawned) result.Add(pawn);
            else if (added < slots) { added += StrongholdPawnOwnership.FitGroup(pawn, slots - added); result.Add(pawn); }
            else StrongholdGarrisonUtility.DiscardUnspawnedPawn(pawn);
        }
        return result;
    }

    private static PawnKindDef ReserveKind(StrongholdData data, Faction faction, int index)
    {
        var pool = (data.finalStandReserveKindDefs ?? new List<string>()).Select(n => DefDatabase<PawnKindDef>.GetNamedSilentFail(n))
            .Where(k => k?.race?.race?.Humanlike == true && k.combatPower > 0).ToList();
        return pool.Count == 0 ? faction.RandomPawnKind() : pool[(Gen.HashCombineInt(data.settlementId, index) & int.MaxValue) % pool.Count];
    }

    public static int TrySpawnFiniteWave(Map map, Settlement settlement, StrongholdData data, out string reason)
    {
        return SpawnSafeReserve(map, settlement, data, PeakPopulationLimit(data),
            Mathf.Min(8, Mathf.Max(1, data?.finalStandReservePeople / 2 ?? 0)), true, out reason);
    }

    public static int TryApplyExistingMapReinforcement(Map map, Settlement settlement, StrongholdData data, out string reason)
    {
        return SpawnSafeReserve(map, settlement, data, StrongholdGarrisonUtility.PopulationLimit(data?.tier ?? 1) +
            PopulationBonus(data), Mathf.Clamp(2 + (data?.tier ?? 1) / 2, 2, 8), false, out reason);
    }

    private static int SpawnSafeReserve(Map map, Settlement settlement, StrongholdData data, int limit, int maxRequested, bool assault, out string reason)
    {
        reason = "invalid-map";
        if (map == null || map.Parent != settlement || settlement == null || data?.settlementId != settlement.ID || !Active(data) || settlement.Faction == null ||
            !IsEligibleSettlement(settlement) || !StrongholdCenterPlanner.Eligible(map, data))
            return 0;
        CellRect baseRect = map.GetComponent<MapComponent_StrongholdSiege>()?.eesBaseRect ?? CellRect.Empty;
        if (baseRect.Width <= 0 && data.generationPlan != null) baseRect = data.generationPlan.baseRect;
        CellRect whole = CellRect.WholeMap(map);
        List<Pawn> playerPawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer) ?? new List<Pawn>();
        const int safeDistance = 35;
        bool TryFindSafeEdgeCell(out IntVec3 cell)
        {
            return CellFinder.TryFindRandomEdgeCellWith(c =>
                c.InBounds(map) && c.Standable(map) && !c.Fogged(map) && c.GetFirstPawn(map) == null &&
                (baseRect.Width <= 0 || !baseRect.ExpandedBy(4).Contains(c)) &&
                playerPawns.All(p => p == null || !p.Spawned || c.DistanceToSquared(p.Position) >= safeDistance * safeDistance) &&
                map.reachability.CanReachMapEdge(c, TraverseParms.For(TraverseMode.PassDoors)),
                map, CellFinder.EdgeRoadChance_Ignore, out cell);
        }

        int existing = CountActiveHostiles(map);
        int requested = Mathf.Min(maxRequested, Mathf.Max(0, limit - existing));
        if (requested == 0)
        {
            reason = $"population-limit;existing={existing};limit={limit}";
            return 0;
        }
        List<Pawn> spawned = new List<Pawn>();
        float pointsRemaining = assault ? data.finalStandReservePoints / 2f : float.MaxValue;
        int spawnedPeople = 0;
        bool foundSafeEntry = false;
        for (int i = 0; i < requested; i++)
        {
            int available = Mathf.Min(requested - spawnedPeople, Mathf.Max(0, limit - CountActiveHostiles(map)));
            if (available <= 0 || !TryFindSafeEdgeCell(out IntVec3 cell)) break;
            foundSafeEntry = true;
            Pawn pawn = null;
            try
            {
                var kind = ReserveKind(data, settlement.Faction, i);
                if (kind == null || kind.combatPower > pointsRemaining) continue;
                pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    kind, settlement.Faction, PawnGenerationContext.NonPlayer,
                    map.Tile, forceGenerateNewPawn: false, allowDead: false, allowDowned: false,
                    canGeneratePawnRelations: true, mustBeCapableOfViolence: true, 1f,
                    forceAddFreeWarmLayerIfNeeded: false, allowGay: true, allowPregnant: false,
                    allowAddictions: true, inhabitant: true));
                // A reused RSC owner can bring several deeply held soldiers.
                // Allocate real people, never just physical roots, before spawn.
                int people = StrongholdPawnOwnership.FitGroup(pawn, available);
                float cost = ReserveGroupCombatPower(pawn);
                while (people > 1 && cost > pointsRemaining)
                {
                    int trimmed = StrongholdPawnOwnership.FitGroup(pawn, people - 1);
                    if (trimmed >= people) break;
                    people = trimmed;
                    cost = ReserveGroupCombatPower(pawn);
                }
                if (people <= 0 || people > available || cost > pointsRemaining ||
                    StrongholdPawnOwnership.Group(pawn).Any(p => p.Destroyed || p.Discarded))
                {
                    StrongholdPawnOwnership.ReturnUnused(pawn);
                    continue;
                }
                GenSpawn.Spawn(pawn, cell, map);
                spawned.Add(pawn);
                spawnedPeople += people;
                pointsRemaining -= cost;
                foreach (var member in StrongholdPawnOwnership.Group(pawn).Where(p => !p.Dead && !p.Destroyed && !p.Discarded))
                    StrongholdGarrisonUtility.GiveSiegeRations(member);
            }
            catch (System.Exception ex)
            {
                if (pawn != null && !pawn.Spawned) StrongholdPawnOwnership.ReturnUnused(pawn);
                EESLog.Warning($"Final stand existing-map reinforcement pawn failed: {ex.Message}");
            }
        }
        if (spawned.Count == 0)
        {
            reason = foundSafeEntry ? "no-valid-reserve-recruit" : "no-safe-edge-entry";
            EESLog.Warning($"Final stand existing-map reinforcement deferred for {settlement.LabelCap}: {reason}; existing={existing}, requested={requested}.");
            return 0;
        }
        CellRect defenseRect = baseRect.Width > 0 ? baseRect : whole;
        StrongholdFeatureEffects.ApplyToPawns(map, settlement.Faction, data, spawned);
        if (assault) Verse.AI.Group.LordMaker.MakeNewLord(settlement.Faction,
            new LordJob_AssaultColony_NoFlee(settlement.Faction, canKidnap: false, canTimeoutOrFlee: false, sappers: false), map, spawned);
        else StrongholdGarrisonUtility.AssignDispersedDefense(map, settlement.Faction, defenseRect, spawned, 2000000000, noFlee: true);
        reason = $"spawned={spawnedPeople};physical={spawned.Count};existing={existing};limit={limit};requested={requested};edge-distance={safeDistance}";
        EESLog.Message($"Final stand existing-map reinforcement applied [{settlement.Faction.Name}]: {reason}; no structures or loot changed.");
        return spawnedPeople;
    }

    private static float ReserveGroupCombatPower(Pawn root) => StrongholdPawnOwnership.Group(root)
        .Where(p => !p.Dead && !p.Destroyed && !p.Discarded)
        .Sum(p => Mathf.Max(0f, p.kindDef?.combatPower ?? 0f));

    public static bool IsEligibleSettlement(Settlement settlement)
    {
        return settlement != null && settlement.Faction != null && !settlement.Faction.IsPlayer &&
            settlement.Faction.HostileTo(Faction.OfPlayer) &&
            settlement.Faction.def?.humanlikeFaction == true && !EESFactionBlacklist.IsBlocked(settlement.Faction) &&
            !EESCustomBaseDetector.IsForeignSettlement(settlement) && settlement.Tile.Valid;
    }

    public static bool IsPlayerConquest(Settlement settlement)
    {
        return FinalStandOutcomeUtility.IsDefeatInProgress(settlement);
    }

    public static bool WithinClusterRadius(PlanetTile origin, PlanetTile candidate)
    {
        return origin.Valid && candidate.Valid && origin.Layer == candidate.Layer &&
            Find.WorldGrid.TraversalDistanceBetween(origin, candidate, passImpassable: true,
                maxDist: ClusterRadius, canTraverseLayers: false) <= ClusterRadius;
    }

    public static List<Settlement> SnapshotCluster(Settlement anchor)
    {
        var result = new List<Settlement>();
        if (!IsEligibleSettlement(anchor)) return result;
        foreach (Settlement candidate in Find.WorldObjects.Settlements)
        {
            if (!IsEligibleSettlement(candidate) || candidate.Faction.loadID != anchor.Faction.loadID ||
                !WithinClusterRadius(anchor.Tile, candidate.Tile)) continue;
            result.Add(candidate);
        }
        return result;
    }
}
