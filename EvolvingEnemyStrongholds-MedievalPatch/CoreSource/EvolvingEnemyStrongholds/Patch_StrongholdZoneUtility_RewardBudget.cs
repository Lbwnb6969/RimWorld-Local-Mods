using HarmonyLib;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(StrongholdZoneUtility), nameof(StrongholdZoneUtility.FillVault))]
[HarmonyAfter("Local.EvolvingEnemyStrongholds.MedievalPatch")]
internal static class Patch_StrongholdZoneUtility_RewardBudget
{
    [HarmonyPostfix]
    private static void Postfix(Map map, Faction faction, StrongholdData data, CellRect vaultInterior)
    {
        StrongholdZoneUtility.FinalizeRewardBudget(map, data, vaultInterior);
    }
}
