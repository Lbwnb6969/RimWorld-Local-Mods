using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class StrongholdRoleDiversityUtility
{
    private const int RoleClusterRadius = 15;
    private const int RoleSelectionSeed = 0x5D17A;

    public static void ApplyOnCreation(StrongholdData data, Settlement settlement, WorldComponent_Strongholds world)
    {
        if (data == null || settlement?.Faction == null || world == null || !settlement.Tile.Valid)
        {
            return;
        }

        // Keep the structural choice alongside the role and feature choices. It
        // is selected once, survives save/load, and is balanced inside the same
        // faction/layer/radius cluster so nearby bases do not all use the same
        // center/town arrangement. Existing generated plans remain authoritative.
        if (!FakeOutpostUtility.IsFake(data.roleDefName) && !BattlefieldUtility.IsBattlefield(data.roleDefName) &&
            data.structureVariant < 0)
        {
            if (data.generationPlan != null && data.generationPlan.layoutVariant >= 0 && data.generationPlan.layoutVariant < 3)
            {
                data.structureVariant = data.generationPlan.layoutVariant;
            }
            else
            {
                int[] variantCounts = { 0, 0, 0 };
                foreach (Settlement nearby in Find.WorldObjects.Settlements)
                {
                    if (nearby == settlement || nearby.Faction?.loadID != settlement.Faction.loadID ||
                        !nearby.Tile.Valid || nearby.Tile.Layer != settlement.Tile.Layer ||
                        !FinalStandUtility.WithinClusterRadius(settlement.Tile, nearby.Tile))
                    {
                        continue;
                    }
                    StrongholdData existing = world.GetExisting(nearby.ID);
                    int variant = existing?.structureVariant ?? -1;
                    if (variant < 0 && existing?.generationPlan != null)
                        variant = existing.generationPlan.layoutVariant;
                    if (variant >= 0 && variant < 3)
                        variantCounts[variant]++;
                }

                int variantMinimum = variantCounts.Min();
                List<int> leastUsedVariants = Enumerable.Range(0, 3).Where(index => variantCounts[index] == variantMinimum).ToList();
                Rand.PushState(Gen.HashCombineInt(data.settlementId, 0x6A71D3));
                try
                {
                    data.structureVariant = leastUsedVariants[Rand.Range(0, leastUsedVariants.Count)];
                }
                finally
                {
                    Rand.PopState();
                }
                EESLog.Message($"Structure diversity [{settlement.Faction.Name}]: {settlement.LabelCap} T{data.tier} -> v{data.structureVariant} (cluster={RoleClusterRadius}, used={string.Join(",", variantCounts)}).");
            }
        }

        if (data.roleForced || !IsDiversityRole(data.roleDefName))
        {
            return;
        }

        List<string> candidates = StrongholdScoreUtility.ValidRolesFor(settlement.Faction, data.tier, settlement)
            .Where(IsDiversityRole).Distinct().ToList();
        if (candidates.Count < 2 || !candidates.Contains(data.roleDefName))
        {
            return;
        }

        var counts = candidates.ToDictionary(role => role, _ => 0);
        foreach (Settlement nearby in Find.WorldObjects.Settlements)
        {
            if (nearby == settlement || nearby.Faction?.loadID != settlement.Faction.loadID ||
                !nearby.Tile.Valid || nearby.Tile.Layer != settlement.Tile.Layer ||
                !FinalStandUtility.WithinClusterRadius(settlement.Tile, nearby.Tile))
            {
                continue;
            }
            StrongholdData existing = world.GetExisting(nearby.ID);
            if (existing != null && counts.ContainsKey(existing.roleDefName))
            {
                counts[existing.roleDefName]++;
            }
        }

        int minimum = counts.Values.Min();
        if (counts[data.roleDefName] == minimum)
        {
            return;
        }

        List<string> leastUsed = counts.Where(pair => pair.Value == minimum).Select(pair => pair.Key).ToList();
        Rand.PushState(Gen.HashCombineInt(settlement.ID, RoleSelectionSeed));
        try
        {
            data.roleDefName = leastUsed[Rand.Range(0, leastUsed.Count)];
        }
        finally
        {
            Rand.PopState();
        }
        EESLog.Message($"Role diversity [{settlement.Faction.Name}]: {settlement.LabelCap} T{data.tier} -> {data.roleDefName} (cluster={RoleClusterRadius}, used={string.Join(",", counts.Select(pair => pair.Key + "=" + pair.Value))}).");
    }

    private static bool IsDiversityRole(string role)
    {
        return role != null && StrongholdRoles.All.Contains(role) &&
            !BattlefieldUtility.IsBattlefield(role) && !FakeOutpostUtility.IsFake(role);
    }
}
