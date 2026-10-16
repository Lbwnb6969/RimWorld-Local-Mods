using System;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(MapGenerator), "GenerateContentsIntoMap")]
public static class Patch_MapGenerator_GenerateContentsIntoMap
{
	public static void Postfix(Map map)
	{
		WorldObject worldObject = map?.Parent;
		if (!(worldObject is Settlement { Faction: var faction } settlement))
		{
			if (worldObject is MapParent { Faction: not null } mapParent && mapParent.Faction != Faction.OfPlayer)
			{
				EESLog.Message("GenContents SKIP: 적대 기지인데 map.Parent=" + worldObject.GetType().Name + "(Settlement 아님), faction=" + mapParent.Faction.Name + " → EES 오버레이 미적용. (이게 '벽/포탑 없음'의 원인일 수 있음.)");
			}
			return;
		}
		Patch_GenStep_Settlement_ScatterAt.LogGenmapEnter("GenContents", settlement, map, faction);
		if (faction == null || faction == Faction.OfPlayer)
		{
			return;
		}
		MapComponent_StrongholdSiege component = map.GetComponent<MapComponent_StrongholdSiege>();
		if (component != null && component.isPolluted)
		{
			try
			{
				int num = PollutedOutpostUtility.SwapGeneratorsToToxifier(map, faction);
				if (num > 0)
				{
					EESLog.Message($"PollutedOutpost deferred swap [{faction.Name}]: 늦게 생성된 발전기 추가 교체 {num}개.");
				}
			}
			catch (Exception ex)
			{
				EESLog.Error("PollutedOutpost deferred gen swap failed: " + ex);
			}
		}
		if (component != null && component.fakePending)
		{
			FinalizeFakeOutpost(settlement, map, component);
			return;
		}
		if (component != null && component.eesGenerated)
		{
			TurretGridUtility.RewireTurretsToGenerators(map, faction, component);
			EESLog.Message("GenContents SKIP: " + settlement.LabelCap + " [" + faction.Name + "] — eesGenerated=true(ScatterAt 바닐라 경로가 이미 처리).");
			return;
		}
		EESLog.Message("GenContents fallback: " + settlement.LabelCap + " [" + faction.Name + "] — ScatterAt 미실행(커스텀 생성기) → EES 오버레이 적용.");
		Patch_GenStep_Settlement_ScatterAt.BuildStronghold(settlement, map, overlay: true);
		if (component != null && component.fakePending)
		{
			FinalizeFakeOutpost(settlement, map, component);
		}
		else if (component != null)
		{
			TurretGridUtility.RewireTurretsToGenerators(map, faction, component);
		}
	}

	private static void FinalizeFakeOutpost(Settlement settlement, Map map, MapComponent_StrongholdSiege siege)
	{
		siege.fakePending = false;
		StrongholdData strongholdData = WorldComponent_Strongholds.Instance?.GetOrCreate(settlement);
		if (strongholdData == null)
		{
			return;
		}
		EESLog.Message($"FakeOutpost finalize (post-genstep): {settlement.LabelCap} [{settlement.Faction?.Name}] T{strongholdData.tier} {strongholdData.roleDefName}.");
		try
		{
			FakeOutpostGen.BuildFake(settlement, map, strongholdData, siege);
		}
		catch (Exception ex)
		{
			EESLog.Error("FakeOutpost finalize failed: " + ex);
		}
	}
}
