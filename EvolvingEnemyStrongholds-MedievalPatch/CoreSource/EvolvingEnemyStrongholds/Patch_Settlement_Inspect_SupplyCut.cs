using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(Settlement), "GetInspectString")]
public static class Patch_Settlement_Inspect_SupplyCut
{
	public static void Postfix(Settlement __instance, ref string __result)
	{
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetExisting(__instance.ID);
		if (SupplyCaravanUtility.SupplyCutActive(strongholdData))
		{
			int numTicks = strongholdData.supplyCutUntilTick - Find.TickManager.TicksGame;
			if (!__result.NullOrEmpty())
			{
				__result += "\n";
			}
			__result += EESText.T("EES.Supply.CutInspect", numTicks.ToStringTicksToPeriod());
		}
	}
}
