using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace EvolvingEnemyStrongholds;

public static class Patch_IRBM_CounterScoutVision
{
	private const string UtilType = "IRBM.AIFactionMissileUtility";

	public static void TryApply(Harmony harmony)
	{
		Type type = AccessTools.TypeByName("IRBM.AIFactionMissileUtility");
		if (!(type == null))
		{
			MethodInfo methodInfo = AccessTools.Method(type, "HasPawnVision", new Type[2]
			{
				typeof(Faction),
				typeof(Map)
			});
			if (methodInfo == null)
			{
				EESLog.Warning("IRBM 감지: AIFactionMissileUtility.HasPawnVision(Faction,Map) 를 못 찾았다 — 시그니처 변경? 역정찰 시야 제외 미적용.");
				return;
			}
			harmony.Patch(methodInfo, null, new HarmonyMethod(typeof(Patch_IRBM_CounterScoutVision), "HasPawnVisionPostfix"));
			EESLog.Message("IRBM 감지: 역정찰병을 미사일 시야 집계에서 제외(HasPawnVision Postfix).");
		}
	}

	public static void HasPawnVisionPostfix(Faction faction, Map map, ref bool __result)
	{
		try
		{
			if (!__result || faction == null || map == null)
			{
				return;
			}
			MapComponent_EESCounterScout component = map.GetComponent<MapComponent_EESCounterScout>();
			if (component == null || !component.AnyActiveMission)
			{
				return;
			}
			List<Pawn> list = map.mapPawns.SpawnedPawnsInFaction(faction);
			for (int i = 0; i < list.Count; i++)
			{
				Pawn pawn = list[i];
				if (pawn != null && !pawn.Dead && !pawn.Downed && !pawn.IsPrisoner && !pawn.IsSlave && !component.IsCounterScout(pawn))
				{
					return;
				}
			}
			__result = false;
		}
		catch (Exception ex)
		{
			EESLog.Warning("IRBM 역정찰 시야 제외 Postfix 실패(무시): " + ex.Message);
		}
	}
}
