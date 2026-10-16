using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System.Linq;
using Verse;

namespace EvolvingEnemyStrongholds;

/// <summary>
/// The medieval adapter performs its final whole-settlement cleanup in a postfix
/// on GenerateContentsIntoMap. Re-scan the shared reward ledger after that postfix
/// so modern-item replacement and outer-defense cleanup cannot bypass the cap.
/// </summary>
[HarmonyPatch(typeof(MapGenerator), "GenerateContentsIntoMap")]
[HarmonyAfter("Local.EvolvingEnemyStrongholds.MedievalPatch")]
[HarmonyPriority(Priority.Last)]
internal static class Patch_MapGenerator_FinalizeRewardBudget
{
    [HarmonyPostfix]
    private static void Postfix(Map map)
    {
        if (map?.Parent is not Settlement settlement || settlement.Faction == Faction.OfPlayer) return;
        var data = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
        var plan = data?.generationPlan;
        if (data?.finalStandActive == true && (plan == null || plan.vaultRect.Width <= 0 || plan.vaultRect.Height <= 0))
        {
            plan = CreateFinalStandLedgerFallback(map, settlement, data);
        }
        if (data == null || plan?.vaultRect.Width <= 0) return;
        plan.rewardAdapterFinalizeObserved = true;
        StrongholdZoneUtility.FinalizeRewardBudget(map, data, plan.vaultRect);
    }

    private static StrongholdGenerationPlan CreateFinalStandLedgerFallback(Map map, Settlement settlement, StrongholdData data)
    {
        var plan = StrongholdGenerationPlan.GetOrCreate(map, data);
        if (plan == null) return null;
        var siege = map.GetComponent<MapComponent_StrongholdSiege>();
        var baseRect = siege?.eesBaseRect ?? CellRect.Empty;
        if (baseRect.Width <= 0 || baseRect.Height <= 0)
        {
            var owned = map.listerThings.AllThings
                .Where(t => t?.Position.InBounds(map) == true && t.Faction == settlement.Faction)
                .Select(t => t.Position).ToList();
            if (owned.Count > 0)
            {
                baseRect = CellRect.FromLimits(owned.Min(c => c.x), owned.Min(c => c.z),
                    owned.Max(c => c.x), owned.Max(c => c.z)).ExpandedBy(2).ClipInsideMap(map);
            }
        }
        if (baseRect.Width <= 0 || baseRect.Height <= 0)
            baseRect = CellRect.WholeMap(map).ContractedBy(12);
        plan.RecordBase(baseRect);
        plan.RecordVault(baseRect);
        plan.placementNote = "final-stand-ledger-rescue";
        plan.rewardProtectedThingIds = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver)
            .Where(t => t?.def?.category == ThingCategory.Item && baseRect.Contains(t.Position))
            .Select(t => t.thingIDNumber).Distinct().OrderBy(id => id).ToList();
        EESLog.Warning("Final Stand ledger rescue: " + settlement.LabelCap + " had no generation plan after map generation; preserved existing items and finalized a bounded fallback ledger in " + baseRect + ".");
        return plan;
    }
}
