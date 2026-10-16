using HarmonyLib;
using RimWorld.Planet;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(MapParent), "Destroy")]
public static class Patch_MapParent_Destroy
{
	public static void Prefix(MapParent __instance)
	{
		if (__instance is Settlement settlement)
		{
			if (!RimPactsCompat.IsTerritoryRaze(settlement))
			{
				HVTQuestUtility.Notify_StrongholdDestroyed(settlement.ID);
				StrongholdData d = WorldComponent_Strongholds.Instance?.GetExisting(settlement.ID);
				WorldComponent_EESRebuild.Instance?.Notify_StrongholdDestroyed(settlement, d);
			}
			else
			{
				EESLog.Message("Destroy " + settlement.LabelCap + ": RimPacts NPC 전쟁 초토화 — HVT 청부·재건 호송대 판정 건너뜀.");
			}
			WorldComponent_Strongholds.Instance?.Remove(settlement.ID);
		}
	}
}
