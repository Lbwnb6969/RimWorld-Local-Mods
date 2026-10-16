using System;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace EvolvingEnemyStrongholds;

[HarmonyPatch(typeof(MapGenerator), "GenerateMap")]
public static class Patch_MapGenerator_GenerateMap_Refog
{
	public static void Postfix(Map __result)
	{
		try
		{
			MapComponent_StrongholdSiege mapComponent_StrongholdSiege = __result?.GetComponent<MapComponent_StrongholdSiege>();
			if (mapComponent_StrongholdSiege == null || !mapComponent_StrongholdSiege.eesGenerated || EESCustomBaseDetector.IsDedicatedOutpostFaction(__result.ParentFaction))
			{
				return;
			}
			Settlement s = __result.Parent as Settlement;
			if (EESHostility.IsFriendlyOutpost(EESHostility.OwnerOf(__result)))
			{
				if (RimPactsCompat.NotAPlayerAssault(s))
				{
					int num = GravshipReinforceUtility.RevealWholeMap(__result);
					EESLog.Message("Friendly outpost [" + __result.Parent?.LabelCap + "] (" + __result.ParentFaction?.Name + "): " + $"아군 거점이라 ★맵 전체 안개 해제 {num}칸 (맵 {__result.Size.x}x{__result.Size.z}).");
					return;
				}
				EESLog.Message("Friendly outpost SKIP [" + __result.Parent?.LabelCap + "] (" + __result.ParentFaction?.Name + "): 플레이어 직공(적대 전환 전) — 안개 해제 안 함, 일반 refog 로 진행.");
			}
			if (EESFactionKind.IsInsect(__result.ParentFaction))
			{
				int num2 = RefogVault(__result, mapComponent_StrongholdSiege);
				EESLog.Message($"Insect base [{__result.Parent?.LabelCap}]: 사각형 refog 생략(둘레벽 없음) — 보상방만 재안개 {num2}칸.");
				return;
			}
			if (VacuumMapUtility.IsVacuum(__result))
			{
				int num3 = RefogVault(__result, mapComponent_StrongholdSiege);
				EESLog.Message($"Vacuum [{__result.Parent?.LabelCap}]: 사각형 refog 생략(궤도플랫폼 기존 안개 유지) — 보상방만 재안개 {num3}칸.");
				return;
			}
			if (mapComponent_StrongholdSiege.openBase && !mapComponent_StrongholdSiege.isFake)
			{
				int num4 = RefogVault(__result, mapComponent_StrongholdSiege);
				EESLog.Message($"Open base (T7-) [{__result.Parent?.LabelCap}]: 내부 refog 생략(둘레 뚫림) — 보상방만 재안개 {num4}칸.");
				return;
			}
			bool flag = mapComponent_StrongholdSiege.eesBaseRect.Width >= 5;
			if (mapComponent_StrongholdSiege.isFake)
			{
				if (flag)
				{
					GravshipReinforceUtility.HideBaseInterior(__result, mapComponent_StrongholdSiege.eesBaseRect);
				}
				else
				{
					__result.fogGrid.Refog(CellRect.WholeMap(__result));
				}
				EESLog.Message("Fake refog: " + __result.Parent?.LabelCap + " — 기지 내부 재안개(★외벽 둘레 노출, 외부 노출).");
			}
			else if (mapComponent_StrongholdSiege.eesOverlay)
			{
				if (flag)
				{
					GravshipReinforceUtility.HideBaseInterior(__result, mapComponent_StrongholdSiege.eesBaseRect);
				}
				else
				{
					__result.fogGrid.Refog(CellRect.WholeMap(__result));
				}
				EESLog.Message("Overlay refog: " + __result.Parent?.LabelCap + " — 기지 내부 재안개(★외벽 둘레 노출, 외부 노출).");
			}
		}
		catch (Exception ex)
		{
			EESLog.Warning("Overlay refog failed: " + ex.Message);
		}
	}

	internal static int RefogVault(Map map, MapComponent_StrongholdSiege siege)
	{
		if (map == null)
		{
			return 0;
		}
		if (siege == null)
		{
			siege = map.GetComponent<MapComponent_StrongholdSiege>();
		}
		if (siege == null || siege.eesVaultRect.Width < 2)
		{
			return 0;
		}
		CellRect rect = siege.eesVaultRect.ClipInsideMap(map);
		map.fogGrid.Refog(rect);
		return rect.Area;
	}
}
